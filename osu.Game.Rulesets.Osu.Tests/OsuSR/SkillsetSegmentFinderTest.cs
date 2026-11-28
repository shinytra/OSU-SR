// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.OsuSR;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.OsuSR;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.OsuSR
{
    [TestFixture]
    public class SkillsetSegmentFinderTest : DifficultyCalculatorTest
    {
        protected override string ResourceAssembly => "osu.Game.Rulesets.Osu.Tests";

        private const double bpm = 180;
        private const double beat = 60_000 / bpm;

        private readonly SkillsetSegmentFinder finder = new SkillsetSegmentFinder();

        [TestCase("801165")]
        [TestCase("diffcalc-test")]
        public void TestProfileCoversEveryObjectButTheFirst(string name)
        {
            var beatmap = GetBeatmap(name);
            var profile = finder.Analyse(beatmap);
            int objectCount = beatmap.GetPlayableBeatmap(new OsuRuleset().RulesetInfo).HitObjects.Count;

            Assert.That(profile.Objects, Has.Count.EqualTo(objectCount - 1));
            Assert.That(profile.Notes, Has.Count.EqualTo(profile.Objects.Count));

            foreach (var skillset in Enum.GetValues<Skillset>())
                Assert.That(profile.Difficulties[skillset], Has.Count.EqualTo(profile.Objects.Count));

            foreach (var pattern in Enum.GetValues<NotePattern>().Where(p => p != NotePattern.None))
                TestContext.Out.WriteLine($"{pattern}: {profile.Notes.Count(n => n.Pattern.HasFlag(pattern))} / {profile.Notes.Count}");
        }

        [Test]
        public void TestSegmentsAreValid([Values] Skillset skillset)
        {
            var beatmap = GetBeatmap("801165");
            var profile = finder.Analyse(beatmap);
            var segments = finder.FindSegments(profile, skillset, maxSegments: 3);

            Assert.That(segments, Has.Count.LessThanOrEqualTo(3));
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
                Assert.That(segment.ObjectCount, Is.GreaterThanOrEqualTo(finder.MinimumMatchingObjects));
                Assert.That(segment.Purity, Is.GreaterThanOrEqualTo(SkillsetSegmentFinder.DefaultMinimumPurity(skillset)));
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
            var hardest = finder.FindSegments(profile, skillset, maxSegments: 1);

            if (all.Count == 0)
            {
                Assert.That(hardest, Is.Empty);
                return;
            }

            Assert.That(hardest.Single().Score, Is.EqualTo(all.Max(s => s.Score)));
        }

        [Test]
        public void TestSegmentStarRatingIsOfThePlayedPartOnly()
        {
            var beatmap = GetBeatmap("801165");
            double mapStars = new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap).Calculate().StarRating;
            var segments = finder.FindSegments(beatmap, Skillset.Streams, maxSegments: 3);

            Assert.That(segments, Is.Not.Empty);

            foreach (var segment in segments)
            {
                double stars = finder.CalculateStarRating(beatmap, segment);
                TestContext.Out.WriteLine($"{segment}: {stars:N2}★ (map {mapStars:N2}★)");

                Assert.That(stars, Is.GreaterThan(0));
                Assert.That(stars, Is.LessThan(mapStars));
            }
        }

        [Test]
        public void TestStreamIsLabelledAsStream()
        {
            var notes = finder.Analyse(createBeatmap(stream(0, 40, spacing: 30))).Notes;

            Assert.That(notes.Count(n => n.Pattern.HasFlag(NotePattern.Stream)), Is.EqualTo(notes.Count));
            Assert.That(notes.Count(n => n.Pattern.HasFlag(NotePattern.Deathstream)), Is.EqualTo(notes.Count));
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.SpacedStream)), Is.False);
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.Jump)), Is.False);
            Assert.That(notes.All(n => Math.Abs(n.BeatFraction - 0.25) < 0.01));
        }

        [Test]
        public void TestWideStreamIsSpaced()
        {
            var notes = finder.Analyse(createBeatmap(stream(0, 24, spacing: 90))).Notes;

            Assert.That(notes.Count(n => n.Pattern.HasFlag(NotePattern.SpacedStream)), Is.EqualTo(notes.Count));
        }

        [Test]
        public void TestShortChainsAreBursts()
        {
            var objects = new List<HitObject>();

            // Five 5-note bursts, one beat apart.
            for (int i = 0; i < 5; i++)
                objects.AddRange(stream(i * 2 * beat, 5, spacing: 20));

            var notes = finder.Analyse(createBeatmap(objects)).Notes;

            Assert.That(notes.All(n => n.ChainLength == 5));
            Assert.That(notes.All(n => n.Pattern.HasFlag(NotePattern.Burst) && n.Pattern.HasFlag(NotePattern.Alt)));
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.Stream)), Is.False);
        }

        [Test]
        public void TestSquareJumpsAreSnapAim()
        {
            var notes = finder.Analyse(createBeatmap(squareJumps(0, 40))).Notes;

            Assert.That(notes.Count(n => n.Pattern.HasFlag(NotePattern.Jump)), Is.GreaterThan(notes.Count * 0.9));
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.FlowAim)), Is.False);
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.Stream)), Is.False);

            // Corners of a rectangle: right angles, and moves much longer than a circle (the first notes have no angle yet).
            foreach (var note in notes.Skip(2))
            {
                Assert.That(note.AngleDegrees, Is.EqualTo(90).Within(2));
                Assert.That(note.DistanceRadii, Is.GreaterThan(5));
            }
        }

        [Test]
        public void TestStraightJumpsAreFlowAim()
        {
            var objects = new List<HitObject>();

            // 1/2 notes going around a big circle: each turn is small, so the movement flows.
            for (int i = 0; i < 40; i++)
            {
                double a = i * Math.PI / 6;
                objects.Add(circle(i * beat / 2, new Vector2(256 + 150 * (float)Math.Cos(a), 192 + 150 * (float)Math.Sin(a))));
            }

            var notes = finder.Analyse(createBeatmap(objects)).Notes;

            Assert.That(notes.Count(n => n.Pattern.HasFlag(NotePattern.FlowAim)), Is.GreaterThan(notes.Count * 0.9));
            Assert.That(notes.Any(n => n.Pattern.HasFlag(NotePattern.Jump)), Is.False);
        }

        [Test]
        public void TestLowArNeedsManyObjectsOnScreen()
        {
            // 1/2 notes at 180 BPM: AR 8 shows 5 objects at once. AR 9 shows almost as many, but its approach time is too short to count as low AR.
            var low = finder.Analyse(createBeatmap(squareJumps(0, 60), approachRate: 8)).Notes;
            var normal = finder.Analyse(createBeatmap(squareJumps(0, 60), approachRate: 9)).Notes;

            // Stop before the end of the map, where fewer objects are left to show.
            Assert.That(low.Skip(2).Take(40).All(n => n.VisibleObjects >= 4 && n.Pattern.HasFlag(NotePattern.LowAr)));
            Assert.That(normal.Any(n => n.Pattern.HasFlag(NotePattern.LowAr)), Is.False);
        }

        [Test]
        public void TestSkillsetsPickTheirOwnPart()
        {
            // 20 s of square jumps, a 3 s break, then 20 s of streams.
            const double stream_start = 20_000 + 3_000;
            var objects = squareJumps(0, (int)(20_000 / (beat / 2))).Concat(stream(stream_start, (int)(20_000 / (beat / 4)), spacing: 30)).ToList();

            var profile = finder.Analyse(createBeatmap(objects));

            var jumps = finder.FindSegments(profile, Skillset.Jumps, maxSegments: 1).Single();
            var streams = finder.FindSegments(profile, Skillset.Streams, maxSegments: 1).Single();

            TestContext.Out.WriteLine(jumps);
            TestContext.Out.WriteLine(streams);

            Assert.That(jumps.EndTime, Is.LessThan(stream_start));
            Assert.That(streams.FirstObjectTime, Is.GreaterThanOrEqualTo(stream_start));
            Assert.That(jumps.Purity, Is.GreaterThan(0.9));
            Assert.That(streams.Purity, Is.GreaterThan(0.9));
        }

        [Test]
        public void TestPurityAdjustmentsMakeSegmentsStricter()
        {
            var beatmap = GetBeatmap("801165");
            var profile = finder.Analyse(beatmap);
            var strict = new SkillsetSegmentFinder { PurityAdjustments = new Dictionary<Skillset, double> { [Skillset.Streams] = 0.2 } };

            var normalSegments = finder.FindSegments(profile, Skillset.Streams, maxSegments: int.MaxValue);
            var strictSegments = strict.FindSegments(profile, Skillset.Streams, maxSegments: int.MaxValue);

            double minimum = SkillsetSegmentFinder.DefaultMinimumPurity(Skillset.Streams) + 0.2;

            Assert.That(strictSegments.Count, Is.LessThanOrEqualTo(normalSegments.Count));
            Assert.That(strictSegments.All(s => s.Purity >= minimum));

            // A skillset without adjustment is unaffected.
            var strictJumps = strict.FindSegments(profile, Skillset.Jumps, maxSegments: 3).Select(s => s.StartTime);
            var normalJumps = finder.FindSegments(profile, Skillset.Jumps, maxSegments: 3).Select(s => s.StartTime);
            Assert.That(strictJumps, Is.EqualTo(normalJumps));
        }

        [Test]
        public void TestStackedNotesOverlap()
        {
            var notes = finder.Analyse(createBeatmap(stackedNotes(0, 60))).Notes;

            Assert.That(notes.Skip(2).Take(40).All(n => n.Pattern.HasFlag(NotePattern.Overlap) && n.OverlapCount >= PatternClassifier.OVERLAP_MIN_COUNT));

            // Square jumps are far apart: nothing overlaps.
            var jumps = finder.Analyse(createBeatmap(squareJumps(0, 60))).Notes;
            Assert.That(jumps.Any(n => n.Pattern.HasFlag(NotePattern.Overlap)), Is.False);
            Assert.That(jumps.All(n => n.OverlapCount == 0));
        }

        [Test]
        public void TestReadingFindsOverlapsAndNotJumps()
        {
            var overlaps = finder.Analyse(createBeatmap(stackedNotes(0, 300)));
            var segment = finder.FindSegments(overlaps, Skillset.Reading, maxSegments: 1).Single();

            Assert.That(segment.Purity, Is.GreaterThan(0.9));

            var jumps = finder.Analyse(createBeatmap(squareJumps(0, 300)));
            Assert.That(finder.FindSegments(jumps, Skillset.Reading), Is.Empty);
        }

        [TestCase(Skillset.JumpsStreams, Skillset.Jumps, Skillset.Streams)]
        public void TestMixedSegmentNeedsBothParts(Skillset mixed, Skillset a, Skillset b)
        {
            Assert.That(SkillsetSegmentFinder.ComponentsOf(mixed), Is.EqualTo((a, b)));

            // 4 s of square jumps, then 4 s of stream, four times over.
            var objects = new List<HitObject>();

            for (int i = 0; i < 4; i++)
            {
                objects.AddRange(squareJumps(i * 8000, 24));
                objects.AddRange(stream(i * 8000 + 4000, 48, spacing: 30));
            }

            var profile = finder.Analyse(createBeatmap(objects));
            var segment = finder.FindSegments(profile, mixed, maxSegments: 1).Single();

            TestContext.Out.WriteLine(segment);

            Assert.That(segment.Purity, Is.GreaterThan(0.7));

            // Only one of the two parts is not mixed.
            Assert.That(finder.FindSegments(finder.Analyse(createBeatmap(squareJumps(0, 300))), mixed), Is.Empty);
            Assert.That(finder.FindSegments(finder.Analyse(createBeatmap(stream(0, 600, spacing: 30))), mixed), Is.Empty);
        }

        [Test]
        public void TestNoSegmentWithoutMatchingPatterns()
        {
            var profile = finder.Analyse(createBeatmap(squareJumps(0, 80)));

            Assert.That(finder.FindSegments(profile, Skillset.Streams), Is.Empty);
            Assert.That(finder.FindSegments(profile, Skillset.Stamina), Is.Empty);
            Assert.That(finder.FindSegments(profile, Skillset.Jumps), Is.Not.Empty);
        }

        [Test]
        public void TestSegmentsSnapToBars()
        {
            var profile = finder.Analyse(createBeatmap(squareJumps(0, 400)));
            var segments = finder.FindSegments(profile, Skillset.Jumps, maxSegments: 3);
            const double bar = beat * 4;

            Assert.That(segments, Is.Not.Empty);

            foreach (var segment in segments)
            {
                TestContext.Out.WriteLine(segment);

                // Edges are on bar lines, unless the map itself starts or ends there.
                if (segment.FirstObjectTime > profile.Objects[0].StartTime)
                    Assert.That(offBar(segment.FirstObjectTime), Is.EqualTo(0).Within(1));

                if (segment.EndTime < profile.Objects[^1].GetEndTime())
                    Assert.That(offBar(segment.EndTime), Is.EqualTo(0).Within(1));
            }

            double offBar(double time) => Math.Abs(time / bar - Math.Round(time / bar)) * bar;
        }

        [Test]
        public void TestEmptyBeatmap()
        {
            var beatmap = new TestWorkingBeatmap(new Beatmap { BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo } });
            Assert.That(finder.FindSegments(beatmap, Skillset.Jumps), Is.Empty);
        }

        private static IEnumerable<HitObject> stream(double startTime, int count, float spacing)
        {
            for (int i = 0; i < count; i++)
            {
                // Zigzag along the playfield so the stream stays on screen.
                float x = 50 + (i * spacing) % 400;
                float y = (i * spacing) / 400 % 2 == 0 ? 150 : 250;
                yield return circle(startTime + i * beat / 4, new Vector2(x, y));
            }
        }

        private static IEnumerable<HitObject> squareJumps(double startTime, int count)
        {
            var corners = new[] { new Vector2(100, 80), new Vector2(400, 80), new Vector2(400, 300), new Vector2(100, 300) };

            for (int i = 0; i < count; i++)
                yield return circle(startTime + i * beat / 2, corners[i % 4]);
        }

        /// <summary>Notes cycling between three spots that are right next to each other: each one sits under the next ones.</summary>
        private static IEnumerable<HitObject> stackedNotes(double startTime, int count)
        {
            var spots = new[] { new Vector2(240, 192), new Vector2(280, 192), new Vector2(260, 226) };

            for (int i = 0; i < count; i++)
                yield return circle(startTime + i * beat / 2, spots[i % 3]);
        }

        private static HitCircle circle(double time, Vector2 position) => new HitCircle { StartTime = time, Position = position };

        private static TestWorkingBeatmap createBeatmap(IEnumerable<HitObject> objects, float approachRate = 9)
        {
            var beatmap = new Beatmap
            {
                BeatmapInfo =
                {
                    Ruleset = new OsuRuleset().RulesetInfo,
                    Difficulty = { CircleSize = 4, ApproachRate = approachRate, OverallDifficulty = 8 },
                },
                HitObjects = objects.ToList(),
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beat });
            return new TestWorkingBeatmap(beatmap);
        }

        protected override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap);

        protected override Ruleset CreateRuleset() => new OsuRuleset();
    }
}
