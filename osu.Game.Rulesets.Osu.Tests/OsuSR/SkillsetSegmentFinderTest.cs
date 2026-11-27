// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.OsuSR;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Osu.Tests.OsuSR
{
    [TestFixture]
    public class SkillsetSegmentFinderTest : DifficultyCalculatorTest
    {
        protected override string ResourceAssembly => "osu.Game.Rulesets.Osu.Tests";

        private readonly SkillsetSegmentFinder finder = new SkillsetSegmentFinder();

        [TestCase("801165")]
        [TestCase("diffcalc-test")]
        public void TestProfileCoversEveryObjectButTheFirst(string name)
        {
            var beatmap = GetBeatmap(name);
            var profile = finder.Analyse(beatmap);
            int objectCount = beatmap.GetPlayableBeatmap(new OsuRuleset().RulesetInfo).HitObjects.Count;

            Assert.That(profile.Objects, Has.Count.EqualTo(objectCount - 1));

            foreach (var skillset in Enum.GetValues<Skillset>())
                Assert.That(profile.Difficulties[skillset], Has.Count.EqualTo(profile.Objects.Count));
        }

        [Test]
        public void TestSegmentsAreValid([Values] Skillset skillset)
        {
            var beatmap = GetBeatmap("801165");
            var profile = finder.Analyse(beatmap);
            var segments = finder.FindSegments(profile, skillset, maxSegments: 3);

            Assert.That(segments, Has.Count.EqualTo(3));
            Assert.That(segments.Select(s => s.Score), Is.Ordered.Descending);

            double mapEnd = profile.Objects.Max(o => o.GetEndTime());

            foreach (var segment in segments)
            {
                TestContext.Out.WriteLine(segment);

                Assert.That(segment.Skillset, Is.EqualTo(skillset));
                Assert.That(segment.StartTime, Is.GreaterThanOrEqualTo(0));
                Assert.That(segment.StartTime, Is.LessThan(segment.FirstObjectTime).Or.EqualTo(0));
                Assert.That(segment.EndTime, Is.LessThanOrEqualTo(mapEnd));
                Assert.That(segment.FirstObjectTime - segment.StartTime, Is.LessThanOrEqualTo(finder.LeadIn));
                Assert.That(segment.ObjectCount, Is.GreaterThan(0));
            }

            foreach (var a in segments)
            {
                foreach (var b in segments.Where(b => b != a))
                    Assert.That(a.EndTime + finder.MinimumGap <= b.StartTime || b.EndTime + finder.MinimumGap <= a.StartTime, $"{a} overlaps {b}");
            }
        }

        [Test]
        public void TestHardestSegmentBeatsAnyOtherWindow([Values] Skillset skillset)
        {
            var profile = finder.Analyse(GetBeatmap("801165"));
            var all = finder.FindSegments(profile, skillset, maxSegments: int.MaxValue);
            var hardest = finder.FindSegments(profile, skillset, maxSegments: 1).Single();

            Assert.That(hardest.Score, Is.EqualTo(all.Max(s => s.Score)));
        }

        [Test]
        public void TestSkillsetsPickDifferentParts()
        {
            var profile = finder.Analyse(GetBeatmap("801165"));

            var aim = finder.FindSegments(profile, Skillset.Aim, maxSegments: 1).Single();
            var speed = finder.FindSegments(profile, Skillset.Speed, maxSegments: 1).Single();

            // A segment picked for a skillset should score at least as high on that skillset as the one picked for another skillset.
            Assert.That(aim.Scores[Skillset.Aim], Is.GreaterThanOrEqualTo(speed.Scores[Skillset.Aim]));
            Assert.That(speed.Scores[Skillset.Speed], Is.GreaterThanOrEqualTo(aim.Scores[Skillset.Speed]));
        }

        [Test]
        public void TestEmptyBeatmap()
        {
            var beatmap = new TestWorkingBeatmap(new Beatmap { BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo } });
            Assert.That(finder.FindSegments(beatmap, Skillset.Aim), Is.Empty);
        }

        protected override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap);

        protected override Ruleset CreateRuleset() => new OsuRuleset();
    }
}
