// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;
using osuTK.Input;

namespace osu.Game.OsuSR.Screens
{
    /// <summary>
    /// Plays a single <see cref="MapSegment"/>: gameplay starts at the segment's start time,
    /// and the player exits on its own (without a results screen) once the segment's last object is judged.
    /// </summary>
    /// <remarks>
    /// Hit objects outside the segment are removed by the ruleset's segment mod (see <see cref="ISkillsetSegmentFinder.CreateSegmentMod"/>),
    /// which must be part of the selected mods when this player loads.
    /// </remarks>
    public partial class SegmentPlayer : Player
    {
        /// <summary>
        /// Time (ms) between the last judgement and leaving the player, so the end of the segment doesn't feel cut off.
        /// </summary>
        public const double EXIT_DELAY = 1000;

        /// <summary>The key that reports the current segment as wrongly classified.</summary>
        public const Key MISCLASSIFIED_KEY = Key.F7;

        public readonly MapSegment Segment;

        private readonly SegmentFeedbackStore? feedback;
        private readonly bool seamless;
        private OsuSpriteText feedbackText = null!;
        private bool reported;

        // No leaderboard during training.
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        /// <summary>
        /// The result of the segment, or null if the player exited before finishing it.
        /// </summary>
        public SegmentResult? Result { get; private set; }

        /// <summary>Current time of the gameplay clock (ms).</summary>
        public double CurrentGameplayTime => GameplayClockContainer.CurrentTime;

        /// <param name="segment">The segment to play.</param>
        /// <param name="feedback">Where "wrongly classified" reports go. Reports are ignored when null.</param>
        /// <param name="seamless">
        /// Whether this segment is part of a series with no pause: its music fades in over the lead-in (so it reaches full volume on the first note),
        /// and fades out during the exit delay (so it doesn't cut off).
        /// </param>
        public SegmentPlayer(MapSegment segment, SegmentFeedbackStore? feedback = null, bool seamless = false)
            : base(new PlayerConfiguration
            {
                ShowResults = false,
                AllowRestart = false,
                AllowSkipping = false,
            })
        {
            Segment = segment;
            this.feedback = feedback;
            this.seamless = seamless;
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

            ScoreProcessor.HasCompleted.BindValueChanged(completed =>
            {
                if (!completed.NewValue)
                    return;

                if (seamless)
                    this.TransformBindableTo(Beatmap.Value.Track.Volume, 0, EXIT_DELAY, Easing.InQuad);

                Scheduler.AddDelayed(finish, EXIT_DELAY);
            });
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == MISCLASSIFIED_KEY && !e.Repeat && feedback != null && !reported)
            {
                reported = true;
                double adjustment = feedback.ReportMisclassified(Segment, Beatmap.Value.BeatmapInfo);

                feedbackText.Text = $"Noté « mal classé » ({Segment.Skillset.GetDescription()} sera plus strict : +{adjustment:P0})";
                feedbackText.FadeIn(100).Then().Delay(2500).FadeOut(500);
                return true;
            }

            return base.OnKeyDown(e);
        }

        protected override void StartGameplay()
        {
            // Start at the segment (with its lead-in) instead of the beginning of the map.
            GameplayClockContainer.Reset(Segment.StartTime, startClock: true);

            // The segment starts on a bar line: bring the music in over the lead-in, so it is at full volume as the first notes arrive.
            if (seamless)
            {
                Beatmap.Value.Track.Volume.Value = 0;
                this.TransformBindableTo(Beatmap.Value.Track.Volume, 1, Segment.FirstObjectTime - Segment.StartTime, Easing.OutQuad);
            }
        }

        // Results are never shown (ShowResults = false); the training session shows its own summary instead.
        protected override ResultsScreen CreateResults(ScoreInfo score) => throw new NotSupportedException();

        private void finish()
        {
            if (!this.IsCurrentScreen())
                return;

            Result = new SegmentResult(Segment, ScoreProcessor.Accuracy.Value, ScoreProcessor.HighestCombo.Value, ScoreProcessor.TotalScore.Value);
            this.Exit();
        }
    }

    public record SegmentResult(MapSegment Segment, double Accuracy, int MaxCombo, long TotalScore);
}
