// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;

namespace osu.Game.OsuSR.Screens
{
    /// <summary>
    /// Plays a whole series as one continuous gameplay: the parts' hit objects are one after the other on a shared timeline (see <see cref="SeriesTimeline"/>),
    /// so there is no loading or screen between them. The music of each part is started, tempo-ramped and crossfaded here, on the beat of its objects.
    /// </summary>
    /// <remarks>
    /// The selected beatmap must be the <see cref="SeriesWorkingBeatmap"/> of the first part.
    /// The other parts are taken from a <see cref="SeriesFeed"/> as they are ready and added to the running gameplay, a couple of parts ahead,
    /// so the series can start right away and can be endless.
    /// </remarks>
    public partial class SeriesPlayer : Player
    {
        /// <summary>Maximum difference (ms) between where a part's music is and where it should be, before it is moved.</summary>
        private const double max_drift = 80;

        /// <summary>Least time (ms) between two corrections of the same track, since a seek takes a moment to show.</summary>
        private const double min_seek_interval = 300;

        /// <summary>How many parts may be on the timeline beyond the one being played.</summary>
        private const int parts_ahead = 2;

        /// <summary>Most time (ms) per frame spent putting hit objects on the playfield.</summary>
        private const double playfield_budget = 2;

        /// <summary>The music of a late part starts at least this long (ms) after the time it was added.</summary>
        private const double late_part_delay = 500;

        private readonly List<SeriesPart> parts;
        private readonly List<SeriesSlot> slots;
        private readonly ISkillsetSegmentFinder finder;
        private readonly IBeatmap composite;
        private readonly SeriesFeed feed;
        private readonly SegmentFeedbackStore? feedback;
        private readonly bool adaptive;
        private readonly HashSet<int> reported = new HashSet<int>();

        private readonly List<Track> tracks = new List<Track>();
        private readonly List<BindableDouble> tempos = new List<BindableDouble>();
        private readonly List<double> lastSeek = new List<double>();
        private readonly List<PartStats> stats = new List<PartStats>();

        /// <summary>Hit objects of added parts that are not on the playfield yet: they are put there a few at a time, so no frame is long.</summary>
        private readonly Queue<HitObject> pendingObjects = new Queue<HitObject>();
        private bool started;
        private bool finishing;
        private int adaptedParts;

        private OsuSpriteText feedbackText = null!;
        private OsuSpriteText partInfoText = null!;

        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        /// <summary>The parts on the timeline so far.</summary>
        public IReadOnlyList<SeriesPart> Parts => parts;

        public IReadOnlyList<SeriesSlot> Slots => slots;

        /// <summary>Results of the parts played, in order. Set when the player ends (and also when it is left early, for the parts played).</summary>
        public IReadOnlyList<SegmentResult> Results { get; private set; } = Array.Empty<SegmentResult>();

        /// <summary>Current time of the gameplay clock (ms), on the series timeline.</summary>
        public double CurrentGameplayTime => GameplayClockContainer.CurrentTime;

        /// <param name="finder">Creates the hit objects of the parts that are added while playing.</param>
        /// <param name="composite">The beatmap of the <see cref="SeriesWorkingBeatmap"/>, which grows as parts are added.</param>
        /// <param name="first">The part the series starts with, already in the beatmap.</param>
        /// <param name="firstSlot">Its slot on the timeline.</param>
        /// <param name="feed">Where the next parts come from.</param>
        /// <param name="feedback">Where "wrongly classified" reports go. Reports are ignored when null.</param>
        /// <param name="adaptive">Whether the difficulty of the next parts follows how well the player did on the last ones.</param>
        public SeriesPlayer(ISkillsetSegmentFinder finder, IBeatmap composite, SeriesPart first, SeriesSlot firstSlot, SeriesFeed feed, SegmentFeedbackStore? feedback = null, bool adaptive = false)
            : base(new PlayerConfiguration
            {
                ShowResults = false,
                AllowRestart = false,
                AllowSkipping = false,
            })
        {
            this.finder = finder;
            this.composite = composite;
            this.feed = feed;
            this.feedback = feedback;
            this.adaptive = adaptive;

            parts = new List<SeriesPart> { first };
            slots = new List<SeriesSlot> { firstSlot };
            addPartState(first);
        }

        private void addPartState(SeriesPart part)
        {
            var tempo = new BindableDouble(1);
            var track = part.Beatmap.TrackLoaded ? part.Beatmap.Track : part.Beatmap.LoadTrack();
            track.Stop();
            track.Volume.Value = 0;
            track.AddAdjustment(AdjustableProperty.Tempo, tempo);

            tracks.Add(track);
            tempos.Add(tempo);
            lastSeek.Add(double.MinValue);
            stats.Add(new PartStats());
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            AddInternal(feedbackText = new OsuSpriteText
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Y = -20,
                Font = OsuFont.Default.With(size: 20),
                Alpha = 0,
            });

            // What the current part is (for checking that the detection is right): its skillset and how much of it really is that skillset.
            AddInternal(partInfoText = new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 4,
                Font = OsuFont.Default.With(size: 14),
                Alpha = 0.6f,
            });

            ScoreProcessor.NewJudgement += onJudgement;
        }

        private void onJudgement(JudgementResult result)
        {
            if (!result.Type.AffectsAccuracy())
                return;

            var part = stats[partOf(result.HitObject.StartTime)];
            part.Numeric += ScoreProcessor.GetBaseScoreForResult(result.Type);
            part.Maximum += ScoreProcessor.GetBaseScoreForResult(result.Judgement.MaxResult);
            part.Combo = Math.Max(part.Combo, ScoreProcessor.Combo.Value);
            part.ScoreAtEnd = ScoreProcessor.TotalScore.Value;
        }

        private int partOf(double time)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (time <= slots[i].EndTime + 1)
                    return i;
            }

            return slots.Count - 1;
        }

        protected override void StartGameplay()
        {
            GameplayClockContainer.Reset(0, startClock: true);
            started = true;
        }

        protected override void Update()
        {
            base.Update();

            if (!started || finishing)
                return;

            double time = GameplayClockContainer.CurrentTime;

            updatePartInfo(time);
            adaptDifficulty(time);
            addNextPart(time);
            fillPlayfield();
            updateMusic(time);

            // A series that ends: finish once the last part is over and the feed has nothing more.
            if (feed.Exhausted && time > slots[^1].EndTime + SeriesTimeline.FINAL_FADE_OUT)
            {
                finishing = true;
                finish();
            }
        }

        private int shownPart = -1;

        private void updatePartInfo(double time)
        {
            int index = SeriesTimeline.PartAt(slots, time);

            if (index == shownPart || index < 0 || index >= parts.Count)
                return;

            shownPart = index;
            var segment = parts[index].Segment;
            partInfoText.Text = $"{(segment.Labels.Count > 0 ? string.Join(" + ", segment.Labels.Select(l => l.GetDescription())) : segment.Skillset.GetDescription())} · {segment.Purity:P0} de {segment.Skillset.GetDescription()} · {segment.StarRating:N2}★";
        }

        /// <summary>Accuracy under which the next parts get easier, and over which they get harder (adaptive difficulty).</summary>
        public const double EASIER_BELOW_ACCURACY = 0.85;

        public const double HARDER_ABOVE_ACCURACY = 0.96;

        /// <summary>Stars added to or taken from the range of the next parts after a part.</summary>
        public const double ADAPTIVE_STEP = 0.2;

        /// <summary>Once a part is over, makes the next ones harder or easier depending on how it went.</summary>
        private void adaptDifficulty(double time)
        {
            if (!adaptive)
                return;

            while (adaptedParts < parts.Count && time > slots[adaptedParts].EndTime + 700)
            {
                var part = stats[adaptedParts++];

                if (part.Maximum <= 0)
                    continue;

                double accuracy = part.Numeric / part.Maximum;

                if (accuracy < EASIER_BELOW_ACCURACY)
                    feed.StarOffset -= ADAPTIVE_STEP;
                else if (accuracy > HARDER_ABOVE_ACCURACY)
                    feed.StarOffset += ADAPTIVE_STEP;
            }
        }

        /// <summary>
        /// Adds the next prepared part to the end of the timeline, if there is one and the timeline isn't already far enough ahead.
        /// The feed's thread did the heavy work (the objects exist, are on their slot and have their defaults): this only registers them.
        /// </summary>
        private void addNextPart(double time)
        {
            if (parts.Count - 1 - SeriesTimeline.PartAt(slots, time) >= parts_ahead)
                return;

            if (!feed.TryTake(out var next))
                return;

            // The feed built this part after the end of the part it knew of. If the series has gone another way (the part is late, or a late part was moved), build it again here.
            if (Math.Abs(next.PreviousEndTime - slots[^1].EndTime) > 1 || next.Slot.MusicStart < time + late_part_delay - 1)
                next = rebuild(next, time);
            else if (next.CompletedPreviousSlot != null)
                slots[^1] = next.CompletedPreviousSlot;

            slots.Add(next.Slot);
            finder.AppendSeriesPart(composite, next);

            foreach (var h in next.Objects)
                pendingObjects.Enqueue(h);

            countAsPlayable(next.Objects);

            parts.Add(next.Part);
            addPartState(next.Part);
        }

        /// <summary>Places a part after the real end of the series so far, on the game thread. Rare: it only happens when the feed didn't keep up.</summary>
        private SeriesPartObjects rebuild(SeriesPartObjects next, double time)
        {
            var state = new SeriesBuildState();

            foreach (var timing in composite.ControlPointInfo.TimingPoints)
                state.ControlPoints.Add(timing.Time, timing);

            if (composite.HitObjects.LastOrDefault() is IHasComboInformation last)
            {
                state.ComboIndex = last.ComboIndex;
                state.ComboIndexWithOffsets = last.ComboIndexWithOffsets;
            }

            var newSlots = slots.ToList();
            var slot = SeriesTimeline.Extend(newSlots, next.Part, parts[^1], time + late_part_delay);
            var completedPrevious = newSlots[^2];
            double switchTime = completedPrevious.EndTime + completedPrevious.Gap / 2;
            var objects = finder.CreateSeriesObjects(next.Data, slot, switchTime, state);

            feed.Resync(newSlots, next.Part, state);
            return new SeriesPartObjects(next.Part, next.Data, slot, completedPrevious, completedPrevious.EndTime, switchTime, next.Timing, objects);
        }

        private void fillPlayfield()
        {
            if (pendingObjects.Count == 0)
                return;

            var watch = Stopwatch.StartNew();

            while (pendingObjects.Count > 0 && watch.Elapsed.TotalMilliseconds < playfield_budget)
                DrawableRuleset.Playfield.Add(pendingObjects.Dequeue());
        }

        /// <summary>
        /// The score processor counts the objects of the beatmap when the gameplay starts, and considers the play over when all of them are judged.
        /// Objects added later are counted too, or the play would look finished at the end of the first parts.
        /// </summary>
        private void countAsPlayable(IEnumerable<HitObject> added)
        {
            try
            {
                var property = typeof(JudgementProcessor).GetProperty("MaxHits", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                int extra = added.Sum(countRecursively);
                property.SetValue(ScoreProcessor, (int)property.GetValue(ScoreProcessor)! + extra);
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not extend the score processor ({ex.Message})");
            }

            static int countRecursively(HitObject h) => 1 + h.NestedHitObjects.Sum(countRecursively);
        }

        private void updateMusic(double time)
        {
            bool paused = GameplayClockContainer.IsPaused.Value;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];

                if (paused || !SeriesTimeline.IsAudible(slots, i, time))
                {
                    if (track.IsRunning)
                        track.Stop();

                    if (paused)
                        lastSeek[i] = double.MinValue;

                    continue;
                }

                // The gameplay itself may be sped up or slowed down by a mod (double time, half time): the music of each part follows.
                tempos[i].Value = SeriesTimeline.TempoAt(slots, i, time) * Beatmap.Value.Track.Rate;
                track.Volume.Value = SeriesTimeline.VolumeAt(slots, i, time);

                // While its objects are in play the music is kept in time with them. After its last object it just runs on, ramping its tempo.
                if (SeriesTimeline.TrackPositionAt(slots, i, time) is double position)
                {
                    position = Math.Max(0, position);

                    if ((!track.IsRunning || Math.Abs(track.CurrentTime - position) > max_drift) && Clock.CurrentTime - lastSeek[i] > min_seek_interval)
                    {
                        track.Seek(position);
                        lastSeek[i] = Clock.CurrentTime;
                    }
                }

                if (!track.IsRunning && !track.HasCompleted)
                    track.Start();
            }
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == SegmentPlayer.MISCLASSIFIED_KEY && !e.Repeat && feedback != null)
            {
                int part = SeriesTimeline.PartAt(slots, GameplayClockContainer.CurrentTime);

                if (reported.Add(part))
                {
                    var segment = parts[part].Segment;
                    double adjustment = feedback.ReportMisclassified(segment, parts[part].Beatmap.BeatmapInfo);

                    feedbackText.Text = $"Noté « mal classé » ({segment.Skillset.GetDescription()} sera plus strict : +{adjustment:P0})";
                    feedbackText.FadeIn(100).Then().Delay(2500).FadeOut(500);
                }

                return true;
            }

            return base.OnKeyDown(e);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            feed.Dispose();
            stopMusic();

            // Leaving early still gives the results of the parts played so far.
            if (Results.Count == 0)
                Results = collectResults();

            return base.OnExiting(e);
        }

        private void stopMusic()
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].Stop();
                tracks[i].RemoveAdjustment(AdjustableProperty.Tempo, tempos[i]);
                tracks[i].Volume.Value = 1;
            }

            tracks.Clear();
            tempos.Clear();
            lastSeek.Clear();
        }

        // Results are never shown (ShowResults = false); the training session shows its own summary instead.
        protected override ResultsScreen CreateResults(ScoreInfo score) => throw new NotSupportedException();

        private void finish()
        {
            if (!this.IsCurrentScreen())
                return;

            Results = collectResults();
            this.Exit();
        }

        /// <summary>Results of the parts that were played at least partly (a part that was never reached has nothing judged).</summary>
        private IReadOnlyList<SegmentResult> collectResults()
        {
            var results = new List<SegmentResult>();
            long previousScore = 0;

            for (int i = 0; i < parts.Count; i++)
            {
                var part = stats[i];

                if (part.Maximum <= 0)
                    continue;

                long score = Math.Max(0, Math.Max(part.ScoreAtEnd, previousScore) - previousScore);
                previousScore = Math.Max(part.ScoreAtEnd, previousScore);
                results.Add(new SegmentResult(parts[i].Segment, part.Numeric / part.Maximum, part.Combo, score));
            }

            return results;
        }

        private class PartStats
        {
            public double Numeric;
            public double Maximum;
            public int Combo;
            public long ScoreAtEnd;
        }
    }
}
