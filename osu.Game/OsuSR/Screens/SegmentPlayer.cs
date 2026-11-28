// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Ranking;

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

        public readonly MapSegment Segment;

        // No leaderboard during training.
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        /// <summary>
        /// The result of the segment, or null if the player exited before finishing it.
        /// </summary>
        public SegmentResult? Result { get; private set; }

        /// <summary>Current time of the gameplay clock (ms).</summary>
        public double CurrentGameplayTime => GameplayClockContainer.CurrentTime;

        public SegmentPlayer(MapSegment segment)
            : base(new PlayerConfiguration
            {
                ShowResults = false,
                AllowRestart = false,
                AllowSkipping = false,
            })
        {
            Segment = segment;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ScoreProcessor.HasCompleted.BindValueChanged(completed =>
            {
                if (completed.NewValue)
                    Scheduler.AddDelayed(finish, EXIT_DELAY);
            });
        }

        protected override void StartGameplay()
        {
            // Start at the segment (with its lead-in) instead of the beginning of the map.
            GameplayClockContainer.Reset(Segment.StartTime, startClock: true);
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
