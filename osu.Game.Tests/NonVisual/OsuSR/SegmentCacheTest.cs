// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.OsuSR;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SegmentCacheTest
    {
        private static MapSegment segment(double? stars = null) => new MapSegment
        {
            Skillset = Skillset.Jumps,
            StartTime = 1000,
            FirstObjectTime = 3000,
            EndTime = 15000,
            ObjectCount = 80,
            StarRating = stars,
            Purity = 0.8,
            Scores = new Dictionary<Skillset, double> { [Skillset.Jumps] = 12.5 },
        };

        [Test]
        public void TestSegmentsSurviveARestart()
        {
            using var storage = new TemporaryNativeStorage("osu-sr-cache");

            var cache = new SegmentCache(storage);
            Assert.That(cache.TryGet("abc", Skillset.Jumps, 0, out _), Is.False);

            cache.Set("abc", Skillset.Jumps, 0, new[] { segment(4.2) });
            cache.Set("abc", Skillset.Streams, 0, System.Array.Empty<MapSegment>());
            cache.Save();

            var reloaded = new SegmentCache(storage);
            Assert.That(reloaded.TryGet("abc", Skillset.Jumps, 0, out var jumps), Is.True);
            Assert.That(jumps, Has.Count.EqualTo(1));
            Assert.That(jumps[0].StarRating, Is.EqualTo(4.2));
            Assert.That(jumps[0].Scores[Skillset.Jumps], Is.EqualTo(12.5));

            // "Nothing found" is remembered too, so the map isn't analysed again.
            Assert.That(reloaded.TryGet("abc", Skillset.Streams, 0, out var streams), Is.True);
            Assert.That(streams, Is.Empty);
        }

        [Test]
        public void TestPurityAdjustmentAndMapContentChangeTheEntry()
        {
            using var storage = new TemporaryNativeStorage("osu-sr-cache");
            var cache = new SegmentCache(storage);
            cache.Set("abc", Skillset.Jumps, 0, new[] { segment() });

            Assert.That(cache.TryGet("abc", Skillset.Jumps, 0.05, out _), Is.False);
            Assert.That(cache.TryGet("changed", Skillset.Jumps, 0, out _), Is.False);
        }
    }
}
