// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading;
using NUnit.Framework;
using osu.Game.OsuSR;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SeriesFeedTest
    {
        [Test]
        public void TestStarOffsetStaysWithinItsLimits()
        {
            var feed = new SeriesFeed(null!, _ => null, 3);

            feed.StarOffset += 0.6;
            feed.StarOffset += 0.6;
            Assert.That(feed.StarOffset, Is.EqualTo(SeriesFeed.MAXIMUM_STAR_OFFSET));

            feed.StarOffset = -5;
            Assert.That(feed.StarOffset, Is.EqualTo(-SeriesFeed.MAXIMUM_STAR_OFFSET));
        }

        [Test]
        public void TestEndsWhenNoMapIsFound()
        {
            var feed = new SeriesFeed(null!, _ => null, 3);
            feed.Start();

            Assert.That(SpinWait.SpinUntil(() => feed.Exhausted, 5000), Is.True);
            Assert.That(feed.NothingFound, Is.True);
            feed.Dispose();
        }
    }
}
