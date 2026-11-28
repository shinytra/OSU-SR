// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.OsuSR;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class BpmMatcherTest
    {
        [TestCase(180, 180, 0)]
        [TestCase(180, 90, 0)]
        [TestCase(90, 180, 0)]
        [TestCase(180, 189, 0.05)]
        [TestCase(180, 120, 0.333)]
        public void TestDistance(double a, double b, double expected)
            => Assert.That(BpmMatcher.Distance(a, b), Is.EqualTo(expected).Within(0.001));

        [Test]
        public void TestTolerances()
        {
            Assert.That(BpmMatcher.IsClose(180, 190, BpmMatcher.STRICT_TOLERANCE));
            Assert.That(BpmMatcher.IsClose(180, 200, BpmMatcher.STRICT_TOLERANCE), Is.False);
            Assert.That(BpmMatcher.IsClose(180, 200, BpmMatcher.RELAXED_TOLERANCE));
        }

        [Test]
        public void TestInvalidBpmNeverMatches()
        {
            Assert.That(BpmMatcher.IsClose(0, 180, BpmMatcher.RELAXED_TOLERANCE), Is.False);
            Assert.That(BpmMatcher.IsClose(180, -1, BpmMatcher.RELAXED_TOLERANCE), Is.False);
        }
    }
}
