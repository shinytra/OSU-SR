// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
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
    /// The selected beatmap must be the <see cref="SeriesWorkingBeatmap"/> of these parts.
    /// </remarks>
    public partial class SeriesPlayer : Player
    {
        /// <summary>Maximum difference (ms) between where a part's music is and where it should be, before it is moved.</summary>
        private const double max_drift = 80;

        /// <summary>Least time (ms) between two corrections of the same track, since a seek takes a moment to show.</summary>
        private const double min_seek_interval = 300;

        public readonly IReadOnlyList<SeriesPart> Parts;
        public readonly IReadOnlyList<SeriesSlot> Slots;

        private readonly SegmentFeedbackStore? feedback;
        private readonly HashSet<int> reported = new HashSet<int>();

        private Track?[] tracks = Array.Empty<Track?>();
        private BindableDouble[] tempos = Array.Empty<BindableDouble>();
        private double[] lastSeek = Array.Empty<double>();
        private bool started;

        private double[] numeric = Array.Empty<double>();
        private double[] maximum = Array.Empty<double>();
        private int[] combos = Array.Empty<int>();
        private long[] scoreAtEnd = Array.Empty<long>();

        private OsuSpriteText feedbackText = null!;

        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        /// <summary>Results of the parts played, in order. Empty if the player was left before the end.</summary>
        public IReadOnlyList<SegmentResult> Results { get; private set; } = Array.Empty<SegmentResult>();

        /// <summary>Current time of the gameplay clock (ms), on the series timeline.</summary>
        public double CurrentGameplayTime => GameplayClockContainer.CurrentTime;

        public SeriesPlayer(IReadOnlyList<SeriesPart> parts, IReadOnlyList<SeriesSlot> slots, SegmentFeedbackStore? feedback = null)
            : base(new PlayerConfiguration
            {
                ShowResults = false,
                AllowRestart = false,
                AllowSkipping = false,
            })
        {
            Parts = parts;
            Slots = slots;
            this.feedback = feedback;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            int count = Parts.Count;
            numeric = new double[count];
            maximum = new double[count];
            combos = new int[count];
            scoreAtEnd = new long[count];
            lastSeek = Enumerable.Repeat(double.MinValue, count).ToArray();

            tempos = Parts.Select(_ => new BindableDouble(1)).ToArray();
            tracks = Parts.Select((p, i) =>
            {
                var track = p.Beatmap.TrackLoaded ? p.Beatmap.Track : p.Beatmap.LoadTrack();
                track.Stop();
                track.Volume.Value = 0;
                track.AddAdjustment(AdjustableProperty.Tempo, tempos[i]);
                return (Track?)track;
            }).ToArray();

            AddInternal(feedbackText = new OsuSpriteText
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Y = -20,
                Font = OsuFont.Default.With(size: 20),
                Alpha = 0,
            });

            ScoreProcessor.NewJudgement += onJudgement;

            ScoreProcessor.HasCompleted.BindValueChanged(completed =>
            {
                if (completed.NewValue)
                    Scheduler.AddDelayed(finish, SeriesTimeline.FINAL_FADE_OUT);
            });
        }

        private void onJudgement(JudgementResult result)
        {
            if (!result.Type.AffectsAccuracy())
                return;

            int part = partOf(result.HitObject.StartTime);
            numeric[part] += ScoreProcessor.GetBaseScoreForResult(result.Type);
            maximum[part] += ScoreProcessor.GetBaseScoreForResult(result.Judgement.MaxResult);
            combos[part] = Math.Max(combos[part], ScoreProcessor.Combo.Value);
            scoreAtEnd[part] = ScoreProcessor.TotalScore.Value;
        }

        private int partOf(double time)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (time <= Slots[i].EndTime + 1)
                    return i;
            }

            return Slots.Count - 1;
        }

        protected override void StartGameplay()
        {
            GameplayClockContainer.Reset(0, startClock: true);
            started = true;
        }

        protected override void Update()
        {
            base.Update();

            if (!started)
                return;

            double time = GameplayClockContainer.CurrentTime;
            bool paused = GameplayClockContainer.IsPaused.Value;

            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];

                if (track == null)
                    continue;

                if (paused || !SeriesTimeline.IsAudible(Slots, i, time))
                {
                    if (track.IsRunning)
                        track.Stop();

                    if (paused)
                        lastSeek[i] = double.MinValue;

                    continue;
                }

                tempos[i].Value = SeriesTimeline.TempoAt(Slots, i, time);
                track.Volume.Value = SeriesTimeline.VolumeAt(Slots, i, time);

                // While its objects are in play the music is kept in time with them. After its last object it just runs on, ramping its tempo.
                if (SeriesTimeline.TrackPositionAt(Slots, i, time) is double position)
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
                int part = SeriesTimeline.PartAt(Slots, GameplayClockContainer.CurrentTime);

                if (reported.Add(part))
                {
                    var segment = Parts[part].Segment;
                    double adjustment = feedback.ReportMisclassified(segment, Parts[part].Beatmap.BeatmapInfo);

                    feedbackText.Text = $"Noté « mal classé » ({segment.Skillset.GetDescription()} sera plus strict : +{adjustment:P0})";
                    feedbackText.FadeIn(100).Then().Delay(2500).FadeOut(500);
                }

                return true;
            }

            return base.OnKeyDown(e);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            stopMusic();
            return base.OnExiting(e);
        }

        private void stopMusic()
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];

                if (track == null)
                    continue;

                track.Stop();
                track.RemoveAdjustment(AdjustableProperty.Tempo, tempos[i]);
                track.Volume.Value = 1;
                tracks[i] = null;
            }
        }

        // Results are never shown (ShowResults = false); the training session shows its own summary instead.
        protected override ResultsScreen CreateResults(ScoreInfo score) => throw new NotSupportedException();

        private void finish()
        {
            if (!this.IsCurrentScreen())
                return;

            var results = new List<SegmentResult>();
            long previousScore = 0;

            for (int i = 0; i < Parts.Count; i++)
            {
                double accuracy = maximum[i] > 0 ? numeric[i] / maximum[i] : 1;
                long score = Math.Max(0, Math.Max(scoreAtEnd[i], previousScore) - previousScore);
                previousScore = Math.Max(scoreAtEnd[i], previousScore);
                results.Add(new SegmentResult(Parts[i].Segment, accuracy, combos[i], score));
            }

            Results = results;
            this.Exit();
        }
    }
}
