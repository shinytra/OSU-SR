// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Screens;
using osu.Game.OsuSR;
using osu.Game.OsuSR.Screens;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.OsuSR;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.OsuSR
{
    public partial class TestSceneTrainingSession : OsuGameTestScene
    {
        private TrainingSession session = null!;

        // Short segments so a full segment plays within the test's timeouts.
        private readonly SkillsetSegmentFinder finder = new SkillsetSegmentFinder
        {
            SegmentLengths = new[] { 2000.0 },
            LeadIn = 500,
            MinimumGap = 0,
            SnapToBars = false,
            // Accept any part of the test map, whatever its patterns.
            MinimumPurity = 0,
            MinimumMatchingObjects = 0,
        };

        [Test]
        public void TestSegmentsChain()
        {
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());

            AddStep("enter jumps session", () => Game.ScreenStack.Push(session = new TrainingSession(new[] { Skillset.Jumps }, finder)));
            AddUntilStep("wait for session", () => session.IsCurrentScreen());

            AddStep("allow any star rating", () =>
            {
                session.MinimumStars.Value = 0;
                session.MaximumStars.Value = 12;
            });
            AddStep("start", () => session.StartSession());

            SegmentPlayer player = null!;

            AddUntilStep("wait for segment player", () => (player = (Game.ScreenStack.CurrentScreen as SegmentPlayer)!) != null);
            // The player may not have input focus yet, so press again until the report goes through (one report per segment).
            AddUntilStep("report as wrongly classified", () =>
            {
                if (!session.Feedback.PurityAdjustments.ContainsKey(Skillset.Jumps))
                    InputManager.Key(SegmentPlayer.MISCLASSIFIED_KEY);

                return session.Feedback.PurityAdjustments.ContainsKey(Skillset.Jumps);
            });
            AddAssert("skillset stricter by one step", () => session.Feedback.PurityAdjustments[Skillset.Jumps], () => Is.EqualTo(SegmentFeedbackStore.ADJUSTMENT_STEP));
            AddAssert("segment and no fail mods applied", () => player.Mods.Value.OfType<OsuModSegment>().Any() && player.Mods.Value.OfType<ModNoFail>().Any());
            AddUntilStep("gameplay starts at segment", () => player.CurrentGameplayTime, () => Is.GreaterThanOrEqualTo(player.Segment.StartTime - 50));
            AddAssert("only segment objects loaded", () => player.Beatmap.Value.GetPlayableBeatmap(player.Ruleset.Value, player.Mods.Value).HitObjects
                                                                 .All(h => h.StartTime >= player.Segment.FirstObjectTime));

            AddUntilStep("wait for return to session", () => session.IsCurrentScreen());
            AddAssert("one result recorded", () => session.Results, () => Has.Count.EqualTo(1));
            AddUntilStep("next segment starts", () => Game.ScreenStack.CurrentScreen is SegmentPlayer next && next != player);

            AddStep("quit segment", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("back at session setup", () => session.IsCurrentScreen());

            AddStep("exit session", () => session.Exit());
            AddAssert("segment mod removed from selection", () => Game.SelectedMods.Value.OfType<OsuModSegment>(), () => Is.Empty);
        }

        [Test]
        public void TestMixedSkillsetsAlternate()
        {
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());

            AddStep("enter mixed session", () => Game.ScreenStack.Push(session = new TrainingSession(new[] { Skillset.Jumps, Skillset.Streams }, finder)));
            AddUntilStep("wait for session", () => session.IsCurrentScreen());
            AddAssert("both skillsets selected", () => session.SelectedSkillsets, () => Is.EquivalentTo(new[] { Skillset.Jumps, Skillset.Streams }));

            AddStep("allow any star rating", () =>
            {
                session.MinimumStars.Value = 0;
                session.MaximumStars.Value = 12;
            });
            AddStep("start", () => session.StartSession());

            SegmentPlayer first = null!;

            AddUntilStep("wait for first segment", () => (first = (Game.ScreenStack.CurrentScreen as SegmentPlayer)!) != null);
            AddUntilStep("wait for return to session", () => session.IsCurrentScreen());
            AddUntilStep("second segment uses the other skillset", () => Game.ScreenStack.CurrentScreen is SegmentPlayer second && second != first && second.Segment.Skillset != first.Segment.Skillset);

            AddStep("quit segment", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("back at session setup", () => session.IsCurrentScreen());
            AddStep("exit session", () => session.Exit());
        }

        [Test]
        public void TestNoMapsInRange()
        {
            AddStep("enter jumps session", () => Game.ScreenStack.Push(session = new TrainingSession(new[] { Skillset.Jumps }, finder)));
            AddUntilStep("wait for session", () => session.IsCurrentScreen());

            AddStep("start", () => session.StartSession());
            AddWaitStep("wait a bit", 5);
            AddAssert("still at session", () => session.IsCurrentScreen());
            AddAssert("no player", () => Game.ScreenStack.CurrentScreen, () => Is.Not.InstanceOf<SegmentPlayer>());
        }
    }
}
