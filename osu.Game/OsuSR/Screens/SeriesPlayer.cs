// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
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

        /// <summary>The music of a late part starts at least this long (ms) after the time it was added.</summary>
        private const double late_part_delay = 500;

        private readonly List<SeriesPart> parts;
        private readonly List<SeriesSlot> slots;
        private readonly ISkillsetSegmentFinder finder;
        private readonly IBeatmap composite;
        private readonly SeriesFeed feed;
        private readonly SegmentFeedbackStore? feedback;
        private readonly HashSet<int> reported = new HashSet<int>();

        private readonly List<Track> tracks = new List<Track>();
        private readonly List<BindableDouble> tempos = new List<BindableDouble>();
        private readonly List<double> lastSeek = new List<double>();
        private readonly List<PartStats> stats = new List<PartStats>();
        private bool started;
        private bool finishing;

        private OsuSpriteText feedbackText = null!;

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
        public SeriesPlayer(ISkillsetSegmentFinder finder, IBeatmap composite, SeriesPart first, SeriesSlot firstSlot, SeriesFeed feed, SegmentFeedbackStore? feedback = null)
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

            addNextPart(time);
            updateMusic(time);

            // A series that ends: finish once the last part is over and the feed has nothing more.
            if (feed.Exhausted && time > slots[^1].EndTime + SeriesTimeline.FINAL_FADE_OUT)
            {
                finishing = true;
                finish();
            }
        }

        /// <summary>
        /// Adds the next prepared part to the end of the timeline, if there is one and the timeline isn't already far enough ahead.
        /// This is cheap (moving a part's hit objects and giving them their defaults), the heavy work was done by the feed's thread.
        /// </summary>
        private void addNextPart(double time)
        {
            if (parts.Count - 1 - SeriesTimeline.PartAt(slots, time) >= parts_ahead)
                return;

            if (!feed.TryTake(out var data))
                return;

            var previous = parts[^1];
            var slot = SeriesTimeline.Extend(slots, data.Part, previous, time + late_part_delay);
            double switchTime = slots[^2].EndTime + slots[^2].Gap / 2;

            var added = finder.AppendSeriesPart(composite, data, slot, switchTime);

            foreach (var h in added)
                DrawableRuleset.Playfield.Add(h);

            countAsPlayable(added);

            parts.Add(data.Part);
            addPartState(data.Part);
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

                tempos[i].Value = SeriesTimeline.TempoAt(slots, i, time);
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
