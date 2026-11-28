// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.OsuSR;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SkillsetTagsTest
    {
        [TestCase(Skillset.Jumps, "jump aim")]
        [TestCase(Skillset.FlowAim, "Flow Aim")]
        [TestCase(Skillset.Streams, "streams")]
        [TestCase(Skillset.SpacedStreams, "spaced streams")]
        [TestCase(Skillset.Alt, "alternating")]
        [TestCase(Skillset.FingerControl, "finger control")]
        [TestCase(Skillset.Tech, "tech")]
        [TestCase(Skillset.Sliders, "slider aim")]
        [TestCase(Skillset.LowAr, "low AR")]
        [TestCase(Skillset.HighAr, "high ar")]
        [TestCase(Skillset.Stamina, "stamina")]
        [TestCase(Skillset.Reading, "reading")]
        [TestCase(Skillset.Reading, "overlaps")]
        [TestCase(Skillset.JumpsStreams, "streams")]
        [TestCase(Skillset.JumpsStreams, "jump aim")]
        [TestCase(Skillset.TechSliders, "slider aim")]
        public void TestTagMatchesItsSkillset(Skillset skillset, string tag)
        {
            Assert.That(SkillsetTags.IsTagged(skillset, new[] { tag }));
            Assert.That(SkillsetTags.MatchingTags(skillset, new[] { "unrelated", tag }), Is.EqualTo(new[] { tag }));
        }

        [Test]
        public void TestEverySkillsetHasKeywords()
        {
            foreach (var skillset in Enum.GetValues<Skillset>())
                Assert.That(SkillsetTags.KeywordsFor(skillset), Is.Not.Empty, skillset.ToString());
        }

        [Test]
        public void TestUnrelatedTagsDontMatch()
            => Assert.That(SkillsetTags.IsTagged(Skillset.Jumps, new[] { "anime", "vocal", "tech" }), Is.False);

        [Test]
        public void TestWeight()
        {
            var selected = new[] { Skillset.Streams, Skillset.Tech };

            Assert.That(SkillsetTags.Weight(selected, new[] { "streams", "anime" }), Is.EqualTo(SkillsetTags.TAGGED_WEIGHT));
            Assert.That(SkillsetTags.Weight(selected, new[] { "tech" }), Is.EqualTo(SkillsetTags.TAGGED_WEIGHT));
            Assert.That(SkillsetTags.Weight(selected, new[] { "jump aim" }), Is.EqualTo(1));
            Assert.That(SkillsetTags.Weight(selected, Array.Empty<string>()), Is.EqualTo(1));
            Assert.That(SkillsetTags.Weight(Enumerable.Empty<Skillset>(), new[] { "streams" }), Is.EqualTo(1));
        }
    }
}
