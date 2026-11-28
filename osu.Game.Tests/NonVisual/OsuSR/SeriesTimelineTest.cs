// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.OsuSR;

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
        public void TestPartAt()
        {
            Assert.That(SeriesTimeline.PartAt(slots, 100), Is.EqualTo(0));
            Assert.That(SeriesTimeline.PartAt(slots, 10300), Is.EqualTo(0));
            Assert.That(SeriesTimeline.PartAt(slots, 10500), Is.EqualTo(1));
            Assert.That(SeriesTimeline.PartAt(slots, 30000), Is.EqualTo(1));
        }
    }
}
