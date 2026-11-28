// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.OsuSR;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SeriesTimelineTest
    {
        private static readonly SeriesSlot[] slots =
        {
            new SeriesSlot(-1000, 0, 2000, 10000, 800, 1, 1.25),
            new SeriesSlot(5000, 9000, 11000, 20000, 0, 0.8),
        };

        [Test]
        public void TestVolumeFadesInOnLeadIn()
        {
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 0), Is.EqualTo(0));
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 1000), Is.EqualTo(0.5).Within(0.001));
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 5000), Is.EqualTo(1));
        }

        [Test]
        public void TestCrossfadeAcrossJunction()
        {
            // The first part fades out after its last object while the next one is already fading in.
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 9600), Is.EqualTo(1));
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 10400), Is.GreaterThan(0).And.LessThan(1));
            Assert.That(SeriesTimeline.VolumeAt(slots, 0, 11100), Is.EqualTo(0));
            Assert.That(SeriesTimeline.VolumeAt(slots, 1, 10000), Is.EqualTo(0.5).Within(0.001));
            Assert.That(SeriesTimeline.IsAudible(slots, 1, 8999), Is.False);
            Assert.That(SeriesTimeline.IsAudible(slots, 1, 9000), Is.True);
            Assert.That(SeriesTimeline.IsAudible(slots, 0, 11200), Is.False);
        }

        [Test]
        public void TestTempoRampsAcrossTheJunction()
        {
            Assert.That(SeriesTimeline.TempoAt(slots, 0, 5000), Is.EqualTo(1));
            Assert.That(SeriesTimeline.TempoAt(slots, 0, 10000 + 1100), Is.EqualTo(1.25).Within(0.001));
            Assert.That(SeriesTimeline.TempoAt(slots, 1, 9000), Is.EqualTo(0.8).Within(0.001));
            Assert.That(SeriesTimeline.TempoAt(slots, 1, 11000), Is.EqualTo(1).Within(0.001));
        }

        [Test]
        public void TestMusicPositionIsOnTimeAtFirstObject()
        {
            // Slot 1: first object at map time 11000 - 5000 = 6000.
            Assert.That(SeriesTimeline.TrackPositionAt(slots, 1, 11000), Is.EqualTo(6000).Within(0.001));
            Assert.That(SeriesTimeline.TrackPositionAt(slots, 1, 12000), Is.EqualTo(7000).Within(0.001));
            // Slower at the start (0.8): 2000 ms of lead-in cover 1800 ms of music.
            Assert.That(SeriesTimeline.TrackPositionAt(slots, 1, 9000), Is.EqualTo(6000 - 1800).Within(0.001));
            Assert.That(SeriesTimeline.TrackPositionAt(slots, 0, 10001), Is.Null);
        }

        [Test]
        public void TestTempoRatioFoldsHalfAndDoubleTime()
        {
            Assert.That(BpmMatcher.TempoRatio(180, 90), Is.EqualTo(1).Within(0.001));
            Assert.That(BpmMatcher.TempoRatio(170, 190), Is.EqualTo(190 / 170.0).Within(0.001));
            Assert.That(BpmMatcher.TempoRatio(100, 400), Is.EqualTo(BpmMatcher.MAXIMUM_TEMPO).Or.EqualTo(1));
        }

        [Test]
        public void TestExtendCompletesTheLastSlotAndWaitsForLateParts()
        {
            var first = part(0, 2000, 10000);
            var second = part(0, 2000, 8000);
            var slots = new List<SeriesSlot>();

            SeriesTimeline.Extend(slots, first, null);
            Assert.That(slots[0].MusicStart, Is.EqualTo(0));
            Assert.That(slots[0].Gap, Is.EqualTo(0));

            // On time: the next part's first object follows the last one by the gap, and its music starts one lead-in before.
            var added = SeriesTimeline.Extend(slots, second, first);
            Assert.That(slots[0].Gap, Is.EqualTo(SeriesTimeline.MINIMUM_GAP));
            Assert.That(added.FirstObjectTime - slots[0].EndTime, Is.EqualTo(slots[0].Gap).Within(0.001));
            Assert.That(added.MusicStart, Is.EqualTo(slots[0].EndTime + slots[0].Gap - 2000).Within(0.001));

            // Late (the series is already well past the end of the previous part): it starts after the earliest time.
            var lateSlots = new List<SeriesSlot>();
            SeriesTimeline.Extend(lateSlots, first, null);
            var late = SeriesTimeline.Extend(lateSlots, second, first, 20000);
            Assert.That(late.MusicStart, Is.EqualTo(20000));
            Assert.That(late.FirstObjectTime, Is.EqualTo(22000));
            Assert.That(lateSlots[0].Gap, Is.EqualTo(22000 - lateSlots[0].EndTime).Within(0.001));
        }

        private static SeriesPart part(double start, double first, double end) => new SeriesPart(new TestWorkingBeatmap(new Beatmap()), new MapSegment
        {
            Skillset = Skillset.Jumps,
            StartTime = start,
            FirstObjectTime = first,
            EndTime = end,
            ObjectCount = 10,
            Scores = new Dictionary<Skillset, double> { [Skillset.Jumps] = 1 },
        });

        [Test]
        public void TestPartAt()
        {
            Assert.That(SeriesTimeline.PartAt(slots, 100), Is.EqualTo(0));
            Assert.That(SeriesTimeline.PartAt(slots, 10300), Is.EqualTo(0));
            Assert.That(SeriesTimeline.PartAt(slots, 10500), Is.EqualTo(1));
            Assert.That(SeriesTimeline.PartAt(slots, 30000), Is.EqualTo(1));
        }
    }
}
