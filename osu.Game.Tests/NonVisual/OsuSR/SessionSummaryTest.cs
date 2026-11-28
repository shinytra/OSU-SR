// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.OsuSR;
using osu.Game.OsuSR.Screens;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SessionSummaryTest
    {
        private static SegmentResult result(Skillset skillset, double accuracy, double? stars = null)
            => new SegmentResult(new MapSegment { Skillset = skillset, StartTime = 0, FirstObjectTime = 1000, EndTime = 10000, ObjectCount = 20, StarRating = stars, Scores = new Dictionary<Skillset, double> { [skillset] = 1 } }, accuracy, 50, 100000);

        [Test]
        public void TestEmpty()
        {
            Assert.That(SessionSummary.Build([]), Is.Empty);
        }

        [Test]
        public void TestSingleSkillsetHasNoWeakestLine()
        {
            var lines = SessionSummary.Build(new[] { result(Skillset.Jumps, 0.9, 4), result(Skillset.Jumps, 0.8, 5) });

            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[1], Does.Contain("2 passage(s)"));
            Assert.That(lines[1], Does.Contain("4.5"));
        }

        [Test]
        public void TestWeakestSkillsetFlagged()
        {
            var lines = SessionSummary.Build(new[] { result(Skillset.Jumps, 0.95), result(Skillset.Streams, 0.7) });

            Assert.That(lines, Has.Count.EqualTo(4));
            Assert.That(lines[3], Does.StartWith("À retravailler"));
            Assert.That(lines[3], Does.Contain(nameof(Skillset.Streams)).Or.Contain("tream"));
        }
    }
}
