// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.OsuSR;

namespace osu.Game.Rulesets.Osu.Tests.OsuSR
{
    [TestFixture]
    public class OsuModSegmentTest
    {
        [Test]
        public void TestOnlySegmentObjectsRemain()
        {
            var beatmap = new Beatmap<OsuHitObject>
            {
                HitObjects = Enumerable.Range(0, 10).Select(i => (OsuHitObject)new HitCircle { StartTime = i * 1000 }).ToList(),
            };
            beatmap.Breaks.Add(new BreakPeriod(500, 1500));
            beatmap.Breaks.Add(new BreakPeriod(4200, 4800));

            var mod = new OsuModSegment
            {
                FirstObjectTime = { Value = 3000 },
                EndTime = { Value = 6000 },
            };

            mod.ApplyToBeatmap(beatmap);

            Assert.That(beatmap.HitObjects.Select(h => h.StartTime), Is.EqualTo(new double[] { 3000, 4000, 5000, 6000 }));
            Assert.That(beatmap.Breaks.Select(b => b.StartTime), Is.EqualTo(new double[] { 4200 }));
        }

        [Test]
        public void TestDeepCloneKeepsSegment()
        {
            var mod = new OsuModSegment
            {
                FirstObjectTime = { Value = 12_345 },
                EndTime = { Value = 34_567 },
            };

            var clone = (OsuModSegment)mod.DeepClone();

            Assert.That(clone.FirstObjectTime.Value, Is.EqualTo(12_345));
            Assert.That(clone.EndTime.Value, Is.EqualTo(34_567));
        }

        [Test]
        public void TestRulesetProvidesFinder()
        {
            var finder = new OsuRuleset().CreateSkillsetSegmentFinder();
            Assert.That(finder, Is.InstanceOf<SkillsetSegmentFinder>());
        }
    }
}
