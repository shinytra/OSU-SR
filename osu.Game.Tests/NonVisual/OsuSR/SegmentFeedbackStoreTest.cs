// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.OsuSR;

namespace osu.Game.Tests.NonVisual.OsuSR
{
    [TestFixture]
    public class SegmentFeedbackStoreTest
    {
        private TemporaryNativeStorage storage = null!;

        private static readonly BeatmapInfo beatmap = new BeatmapInfo
        {
            DifficultyName = "Insane",
            MD5Hash = "abc",
            Metadata = new BeatmapMetadata { Artist = "Artist", Title = "Title" },
        };

        [SetUp]
        public void SetUp() => storage = new TemporaryNativeStorage("osu-sr-feedback");

        [TearDown]
        public void TearDown() => storage.Dispose();

        [Test]
        public void TestReportMakesSkillsetStricter()
        {
            var store = new SegmentFeedbackStore(storage);

            Assert.That(store.PurityAdjustments, Is.Empty);

            double first = store.ReportMisclassified(segment(Skillset.Streams), beatmap);
            double second = store.ReportMisclassified(segment(Skillset.Streams), beatmap);

            Assert.That(first, Is.EqualTo(SegmentFeedbackStore.ADJUSTMENT_STEP));
            Assert.That(second, Is.EqualTo(SegmentFeedbackStore.ADJUSTMENT_STEP * 2));
            Assert.That(store.PurityAdjustments, Has.Count.EqualTo(1));
            Assert.That(store.PurityAdjustments[Skillset.Streams], Is.EqualTo(SegmentFeedbackStore.ADJUSTMENT_STEP * 2));
            Assert.That(store.ReportCount, Is.EqualTo(2));
        }

        [Test]
        public void TestAdjustmentIsCapped()
        {
            var store = new SegmentFeedbackStore(storage);

            for (int i = 0; i < 100; i++)
                store.ReportMisclassified(segment(Skillset.Jumps), beatmap);

            Assert.That(store.PurityAdjustments[Skillset.Jumps], Is.EqualTo(SegmentFeedbackStore.MAXIMUM_ADJUSTMENT));
        }

        [Test]
        public void TestReportsSurviveARestart()
        {
            var store = new SegmentFeedbackStore(storage);
            store.ReportMisclassified(segment(Skillset.Tech), beatmap);
            store.ReportMisclassified(segment(Skillset.Alt), beatmap);

            var reloaded = new SegmentFeedbackStore(storage);

            Assert.That(reloaded.ReportCount, Is.EqualTo(2));
            Assert.That(reloaded.PurityAdjustments.Keys, Is.EquivalentTo(new[] { Skillset.Tech, Skillset.Alt }));
            Assert.That(reloaded.PurityAdjustments[Skillset.Tech], Is.EqualTo(SegmentFeedbackStore.ADJUSTMENT_STEP));
        }

        [Test]
        public void TestResetKeepsReports()
        {
            var store = new SegmentFeedbackStore(storage);
            store.ReportMisclassified(segment(Skillset.Streams), beatmap);

            store.ResetAdjustments();

            Assert.That(store.PurityAdjustments, Is.Empty);
            Assert.That(store.ReportCount, Is.EqualTo(1));
            Assert.That(new SegmentFeedbackStore(storage).PurityAdjustments, Is.Empty);
        }

        [Test]
        public void TestReturnedAdjustmentsAreACopy()
        {
            var store = new SegmentFeedbackStore(storage);
            var before = store.PurityAdjustments;

            store.ReportMisclassified(segment(Skillset.Streams), beatmap);

            Assert.That(before, Is.Empty);
            Assert.That(store.PurityAdjustments.Keys.ToArray(), Is.EqualTo(new[] { Skillset.Streams }));
        }

        private static MapSegment segment(Skillset skillset) => new MapSegment
        {
            Skillset = skillset,
            StartTime = 1000,
            FirstObjectTime = 3000,
            EndTime = 13000,
            ObjectCount = 50,
            Scores = new Dictionary<Skillset, double> { [skillset] = 100 },
            Purity = 0.7,
        };
    }
}
