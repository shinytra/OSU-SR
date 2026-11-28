// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.OsuSR;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.OsuSR
{
    /// <summary>
    /// Finds the hardest parts of a beatmap for each <see cref="Skillset"/>.
    /// </summary>
    /// <remarks>
    /// Every note is first labelled with the patterns it belongs to (<see cref="PatternClassifier"/>).
    /// A segment then scores, for a skillset, the difficulty of its matching notes per second (from the lazer evaluator that fits the skillset),
    /// times its purity: the share of its notes that match. Segments that aren't pure enough are skipped,
    /// so a very hard mixed part doesn't win every skillset.
    /// </remarks>
    public class SkillsetSegmentFinder : ISkillsetSegmentFinder
    {
        /// <summary>Candidate lengths (ms) of a segment's hit object window. The best scoring one wins.</summary>
        public IReadOnlyList<double> SegmentLengths { get; init; } = new[] { 10_000.0, 15_000, 20_000 };

        /// <summary>Candidate lengths (ms) for <see cref="Skillset.Stamina"/>, which needs longer segments.</summary>
        public IReadOnlyList<double> StaminaSegmentLengths { get; init; } = new[] { 20_000.0, 30_000 };

        /// <summary>How long (ms) playback starts before the first object of a segment.</summary>
        public double LeadIn { get; init; } = 2_000;

        /// <summary>Minimum gap (ms) between two segments picked from the same map.</summary>
        public double MinimumGap { get; init; } = 2_000;

        /// <summary>Whether segment edges move to the nearest bar lines, so segments start and end with the music's phrases.</summary>
        public bool SnapToBars { get; init; } = true;

        /// <summary>How far (ms) an edge may move when snapping to a bar line.</summary>
        public double MaximumSnap { get; init; } = 4_000;

        /// <summary>The highest minimum purity a skillset can get from adjustments.</summary>
        public const double MAXIMUM_PURITY = 0.95;

        public IReadOnlyDictionary<Skillset, double> PurityAdjustments { get; set; } = new Dictionary<Skillset, double>();

        /// <summary>Overrides every skillset's minimum purity (for tests).</summary>
        public double? MinimumPurity { get; init; }

        /// <summary>Minimum number of matching notes in a segment.</summary>
        public int MinimumMatchingObjects { get; init; } = 8;

        /// <summary>
        /// Minimum share of a segment's notes that must match the skillset. Lower for skillsets whose patterns are naturally mixed with others.
        /// </summary>
        public static double DefaultMinimumPurity(Skillset skillset) => skillset switch
        {
            Skillset.Jumps => 0.5,
            Skillset.FlowAim => 0.4,
            Skillset.Streams => 0.6,
            Skillset.SpacedStreams => 0.5,
            Skillset.Alt => 0.35,
            Skillset.FingerControl => 0.3,
            Skillset.Tech => 0.3,
            Skillset.Sliders => 0.25,
            Skillset.LowAr => 0.7,
            Skillset.HighAr => 0.8,
            Skillset.Stamina => 0.6,
            Skillset.Reading => 0.3,
            Skillset.JumpsStreams => 0.7,
            Skillset.FlowStreams => 0.7,
            Skillset.TechSliders => 0.5,
            _ => throw new ArgumentOutOfRangeException(nameof(skillset), skillset, null),
        };

        /// <summary>The note patterns that count for a skillset.</summary>
        public static NotePattern PatternsFor(Skillset skillset) => skillset switch
        {
            Skillset.Jumps => NotePattern.Jump,
            Skillset.FlowAim => NotePattern.FlowAim,
            Skillset.Streams => NotePattern.Stream,
            Skillset.SpacedStreams => NotePattern.SpacedStream,
            Skillset.Alt => NotePattern.Alt,
            Skillset.FingerControl => NotePattern.FingerControl,
            Skillset.Tech => NotePattern.Tech,
            Skillset.Sliders => NotePattern.FastSlider,
            Skillset.LowAr => NotePattern.LowAr,
            Skillset.HighAr => NotePattern.HighAr,
            Skillset.Stamina => NotePattern.Deathstream,
            Skillset.Reading => NotePattern.Overlap,
            Skillset.JumpsStreams => NotePattern.Jump | NotePattern.Stream,
            Skillset.FlowStreams => NotePattern.FlowAim | NotePattern.Stream,
            Skillset.TechSliders => NotePattern.Tech | NotePattern.FastSlider,
            _ => throw new ArgumentOutOfRangeException(nameof(skillset), skillset, null),
        };

        /// <summary>Minimum share of a mixed segment's notes that each of its two parts must have, so a mixed segment really has both.</summary>
        public const double MINIMUM_COMPONENT_PURITY = 0.3;

        /// <summary>
        /// The two skillsets that a mixed skillset combines, or null for a regular skillset.
        /// </summary>
        public static (Skillset First, Skillset Second)? ComponentsOf(Skillset skillset) => skillset switch
        {
            Skillset.JumpsStreams => (Skillset.Jumps, Skillset.Streams),
            Skillset.FlowStreams => (Skillset.FlowAim, Skillset.Streams),
            Skillset.TechSliders => (Skillset.Tech, Skillset.Sliders),
            _ => null,
        };

        IReadOnlyList<MapSegment> ISkillsetSegmentFinder.FindSegments(IWorkingBeatmap beatmap, Skillset skillset, int maxSegments, CancellationToken cancellationToken)
            => FindSegments(beatmap, skillset, maxSegments, null, cancellationToken);

        public IReadOnlyDictionary<Skillset, IReadOnlyList<MapSegment>> FindSegments(IWorkingBeatmap beatmap, IEnumerable<Skillset> skillsets, int maxSegments,
                                                                                     CancellationToken cancellationToken = default)
        {
            var profile = Analyse(beatmap, null, cancellationToken);

            return skillsets.Distinct()
                            .Select(s => (Skillset: s, Segments: FindSegments(profile, s, maxSegments)))
                            .Where(r => r.Segments.Count > 0)
                            .ToDictionary(r => r.Skillset, r => r.Segments);
        }

        public double CalculateStarRating(IWorkingBeatmap beatmap, MapSegment segment, CancellationToken cancellationToken = default)
        {
            var calculator = new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap);
            return calculator.Calculate(new[] { CreateSegmentMod(segment) }, cancellationToken).StarRating;
        }

        public Mod CreateSegmentMod(MapSegment segment) => new OsuModSegment
        {
            FirstObjectTime = { Value = segment.FirstObjectTime },
            EndTime = { Value = segment.EndTime },
        };

        /// <summary>
        /// Labels every note and computes its difficulty for every skillset.
        /// </summary>
        public SkillsetProfile Analyse(IWorkingBeatmap beatmap, IEnumerable<Mod>? mods = null, CancellationToken cancellationToken = default)
        {
            var calculator = new ProfilingDifficultyCalculator(beatmap);
            calculator.Calculate(mods ?? Array.Empty<Mod>(), cancellationToken);
            return calculator.CreateProfile();
        }

        /// <summary>
        /// Returns up to <paramref name="maxSegments"/> non-overlapping segments, hardest first, for the given skillset.
        /// Maps without a part that is pure enough for the skillset return no segment.
        /// </summary>
        public IReadOnlyList<MapSegment> FindSegments(IWorkingBeatmap beatmap, Skillset skillset, int maxSegments = 3, IEnumerable<Mod>? mods = null,
                                                      CancellationToken cancellationToken = default)
            => FindSegments(Analyse(beatmap, mods, cancellationToken), skillset, maxSegments);

        /// <summary>
        /// Returns up to <paramref name="maxSegments"/> non-overlapping segments, hardest first, for the given skillset.
        /// </summary>
        public IReadOnlyList<MapSegment> FindSegments(SkillsetProfile profile, Skillset skillset, int maxSegments = 3)
        {
            var objects = profile.Objects;

            if (objects.Count == 0 || maxSegments <= 0)
                return Array.Empty<MapSegment>();

            var sums = Enum.GetValues<Skillset>().ToDictionary(s => s, s => new SkillsetSums(profile, s));
            var own = sums[skillset];
            var components = ComponentsOf(skillset);
            var partA = components is { } pair ? sums[pair.First] : null;
            var partB = components is { } other ? sums[other.Second] : null;
            double minimumPurity = MinimumPurity ?? Math.Min(MAXIMUM_PURITY, DefaultMinimumPurity(skillset) + PurityAdjustments.GetValueOrDefault(skillset));
            var lengths = skillset == Skillset.Stamina ? StaminaSegmentLengths : SegmentLengths;

            var candidates = new List<(int First, int Last, double Length, double Score)>();

            foreach (double length in lengths)
            {
                int last = 0;

                for (int first = 0; first < objects.Count; first++)
                {
                    last = Math.Max(last, first);

                    while (last + 1 < objects.Count && objects[last + 1].StartTime - objects[first].StartTime <= length)
                        last++;

                    if (own.Matching(first, last) < MinimumMatchingObjects || own.Purity(first, last) < minimumPurity)
                        continue;

                    // A mixed segment needs a real share of both of its parts.
                    if (partA != null && partB != null && (partA.Purity(first, last) < MINIMUM_COMPONENT_PURITY || partB.Purity(first, last) < MINIMUM_COMPONENT_PURITY))
                        continue;

                    // Slightly favour longer windows, so a long pure part isn't cut into its densest 10 seconds.
                    double score = own.Score(first, last, length) * Math.Pow(length / 20_000, 0.3);
                    candidates.Add((first, last, length, score));
                }
            }

            var picked = new List<MapSegment>();

            foreach (var (first, last, length, score) in candidates.OrderByDescending(c => c.Score))
            {
                if (picked.Count >= maxSegments)
                    break;

                var segment = createSegment(first, last, length, score);

                if (picked.Any(p => segment.StartTime < p.EndTime + MinimumGap && p.StartTime < segment.EndTime + MinimumGap))
                    continue;

                picked.Add(segment);
            }

            return picked;

            MapSegment createSegment(int first, int last, double length, double score)
            {
                var scores = sums.ToDictionary(p => p.Key, p => p.Value.Score(first, last, length));
                scores[skillset] = score;
                double purity = own.Purity(first, last);

                if (SnapToBars && profile.Beatmap != null)
                    (first, last) = snapToBars(profile, first, last);

                return new MapSegment
                {
                    Skillset = skillset,
                    StartTime = Math.Max(0, startTimeFor(profile.Beatmap, objects[first].StartTime)),
                    FirstObjectTime = objects[first].StartTime,
                    EndTime = objects[last].GetEndTime(),
                    ObjectCount = last - first + 1,
                    Scores = scores,
                    Purity = purity,
                };
            }
        }

        /// <summary>A gap between two objects at least this long (ms) is a break: a pre-drop or post-drop doesn't run through it.</summary>
        public const double BREAK_GAP = 3_000;

        public MapSegment ExtendSegment(IWorkingBeatmap beatmap, MapSegment segment, double preDrop, double postDrop, CancellationToken cancellationToken = default)
        {
            var playable = beatmap.GetPlayableBeatmap(new OsuRuleset().RulesetInfo, Array.Empty<Mod>(), cancellationToken);
            var objects = playable.HitObjects;

            int first = objects.ToList().FindIndex(h => h.StartTime >= segment.FirstObjectTime - 0.5);
            int last = objects.ToList().FindLastIndex(h => h.GetEndTime() <= segment.EndTime + 0.5);

            if (first < 0 || last < first)
                return segment;

            while (first > 0 && canJoin(objects[first - 1], objects[first]) && segment.FirstObjectTime - objects[first - 1].StartTime <= preDrop)
                first--;

            while (last + 1 < objects.Count && canJoin(objects[last], objects[last + 1]) && objects[last + 1].GetEndTime() - segment.EndTime <= postDrop)
                last++;

            if (SnapToBars)
            {
                const double leniency = 5;
                double barStart = Math.Max(barLine(playable, objects[first].StartTime, false), objects[first].StartTime - MaximumSnap);

                while (first > 0 && canJoin(objects[first - 1], objects[first]) && objects[first - 1].StartTime >= barStart - leniency)
                    first--;

                double barEnd = Math.Min(barLine(playable, objects[last].GetEndTime(), true), objects[last].GetEndTime() + MaximumSnap);

                while (last + 1 < objects.Count && canJoin(objects[last], objects[last + 1]) && objects[last + 1].StartTime <= barEnd + leniency)
                    last++;
            }

            return segment with
            {
                StartTime = Math.Max(0, startTimeFor(playable, objects[first].StartTime)),
                FirstObjectTime = objects[first].StartTime,
                EndTime = objects[last].GetEndTime(),
                ObjectCount = last - first + 1,
            };

            static bool canJoin(HitObject before, HitObject after) => after.StartTime - before.GetEndTime() < BREAK_GAP;
        }

        private static double barLine(IBeatmap beatmap, double time, bool next)
        {
            var timingPoint = beatmap.ControlPointInfo.TimingPointAt(time);
            double barLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;

            if (barLength <= 0)
                return time;

            double bars = (time - timingPoint.Time) / barLength;
            double index = next ? Math.Ceiling(bars - 1e-6) : Math.Floor(bars + 1e-6);
            return timingPoint.Time + index * barLength;
        }

        public ISeriesPartData PrepareSeriesPart(SeriesPart part, CancellationToken cancellationToken = default)
        {
            var playable = (Beatmap<OsuHitObject>)part.Beatmap.GetPlayableBeatmap(new OsuRuleset().RulesetInfo, new[] { CreateSegmentMod(part.Segment) }, cancellationToken);
            var timing = playable.ControlPointInfo.TimingPointAt(part.Segment.FirstObjectTime);
            return new OsuSeriesPartData(part, playable, new TimingControlPoint { BeatLength = timing.BeatLength, TimeSignature = timing.TimeSignature });
        }

        public IReadOnlyList<HitObject> CreateSeriesObjects(ISeriesPartData part, SeriesSlot slot, double timingSwitchTime, SeriesBuildState state)
        {
            var data = (OsuSeriesPartData)part;

            // The tempo of this part applies from the middle of the junction before it, so the last sliders of the previous part keep their own.
            state.ControlPoints.Add(timingSwitchTime, data.Timing);

            var objects = new List<OsuHitObject>();
            int indexInCombo = 0;

            // The part's objects are consumed: they were made for this one use.
            foreach (var h in data.Beatmap.HitObjects)
            {
                // (The shift is relative to where the objects already are, since the part may be placed again if it is late.)
                h.StartTime += slot.Shift - data.AppliedShift;

                // Each part starts a new combo, and the numbers go on from the previous part.
                if (objects.Count == 0)
                    h.NewCombo = true;

                if (h.NewCombo)
                {
                    state.ComboIndex++;
                    state.ComboIndexWithOffsets += 1 + h.ComboOffset;
                    indexInCombo = 0;
                }

                h.ComboIndex = state.ComboIndex;
                h.ComboIndexWithOffsets = state.ComboIndexWithOffsets;
                h.IndexInCurrentCombo = indexInCombo++;

                // Each part keeps the difficulty settings (approach rate, circle size, overall difficulty, slider speed...) of its own map.
                h.ApplyDefaults(state.ControlPoints, data.Beatmap.Difficulty);
                objects.Add(h);
            }

            data.AppliedShift = slot.Shift;
            return objects;
        }

        public IBeatmap CreateSeriesBeatmap(SeriesPartObjects first)
        {
            var beatmap = new OsuBeatmap { BeatmapInfo = first.Part.Beatmap.BeatmapInfo };
            AppendSeriesPart(beatmap, first);
            return beatmap;
        }

        public void AppendSeriesPart(IBeatmap beatmap, SeriesPartObjects part)
        {
            var composite = (OsuBeatmap)beatmap;
            composite.ControlPointInfo.Add(part.TimingSwitchTime, part.Timing);
            composite.HitObjects.AddRange(part.Objects.Cast<OsuHitObject>());
        }

        private record OsuSeriesPartData(SeriesPart Part, Beatmap<OsuHitObject> Beatmap, TimingControlPoint Timing) : ISeriesPartData
        {
            /// <summary>How far the part's objects have been moved from their own map's times.</summary>
            public double AppliedShift { get; set; }
        }

        /// <summary>
        /// When playback of a segment starts. With <see cref="SnapToBars"/>, that is a bar line (or half bar, or beat, for slow maps) of the map's own timing,
        /// at least <see cref="LeadIn"/> × 0.75 before the first object, so the music's pulse starts in phase with the notes. Otherwise it is <see cref="LeadIn"/> before.
        /// </summary>
        private double startTimeFor(IBeatmap? beatmap, double firstObjectTime)
        {
            if (!SnapToBars || beatmap == null)
                return firstObjectTime - LeadIn;

            var timingPoint = beatmap.ControlPointInfo.TimingPointAt(firstObjectTime);
            double beatLength = timingPoint.BeatLength;
            double unit = beatLength * timingPoint.TimeSignature.Numerator;

            if (beatLength <= 0)
                return firstObjectTime - LeadIn;

            // Slow maps have very long bars: use half bars, then beats, to keep the lead-in short.
            while (unit > LeadIn && unit / 2 >= beatLength * 0.99)
                unit /= 2;

            double latest = firstObjectTime - LeadIn * 0.75;
            return timingPoint.Time + Math.Floor((latest - timingPoint.Time) / unit + 1e-6) * unit;
        }

        /// <summary>
        /// Moves the first object back to the start of its bar, and the last object forward to the next bar line (included, so the segment ends on a downbeat).
        /// </summary>
        private (int First, int Last) snapToBars(SkillsetProfile profile, int first, int last)
        {
            const double leniency = 5;
            var objects = profile.Objects;
            var controlPoints = profile.Beatmap!.ControlPointInfo;

            double firstStart = objects[first].StartTime;
            double barStart = Math.Max(barLine(firstStart, false), firstStart - MaximumSnap);

            while (first > 0 && objects[first - 1].StartTime >= barStart - leniency)
                first--;

            double lastEnd = objects[last].GetEndTime();
            double barEnd = Math.Min(barLine(lastEnd, true), lastEnd + MaximumSnap);

            while (last + 1 < objects.Count && objects[last + 1].StartTime <= barEnd + leniency)
                last++;

            return (first, last);

            double barLine(double time, bool next)
            {
                var timingPoint = controlPoints.TimingPointAt(time);
                double barLength = timingPoint.BeatLength * timingPoint.TimeSignature.Numerator;

                if (barLength <= 0)
                    return time;

                double bars = (time - timingPoint.Time) / barLength;
                double index = next ? Math.Ceiling(bars - 1e-6) : Math.Floor(bars + 1e-6);
                return timingPoint.Time + index * barLength;
            }
        }

        /// <summary>
        /// Prefix sums of a skillset's matching notes and their difficulties, to score any window in constant time.
        /// </summary>
        private class SkillsetSums
        {
            private readonly double[] difficulty;
            private readonly int[] matching;
            private readonly IReadOnlyList<HitObject> objects;

            public SkillsetSums(SkillsetProfile profile, Skillset skillset)
            {
                objects = profile.Objects;
                var patterns = PatternsFor(skillset);
                var values = profile.Difficulties[skillset];

                difficulty = new double[objects.Count + 1];
                matching = new int[objects.Count + 1];

                for (int i = 0; i < objects.Count; i++)
                {
                    bool matches = (profile.Notes[i].Pattern & patterns) != 0;
                    difficulty[i + 1] = difficulty[i] + (matches ? values[i] : 0);
                    matching[i + 1] = matching[i] + (matches ? 1 : 0);
                }
            }

            public int Matching(int first, int last) => matching[last + 1] - matching[first];

            public double Purity(int first, int last) => (double)Matching(first, last) / (last - first + 1);

            /// <summary>Difficulty of the matching notes per second, times purity.</summary>
            public double Score(int first, int last, double length)
            {
                // Divide by the full window length rather than the actual span, so that sparse windows don't get inflated scores.
                double seconds = Math.Max(length, objects[last].StartTime - objects[first].StartTime) / 1000;
                return (difficulty[last + 1] - difficulty[first]) / seconds * Purity(first, last);
            }
        }

        /// <summary>
        /// Runs the regular osu!standard difficulty calculation, and keeps the skills and the processed objects afterwards.
        /// </summary>
        private class ProfilingDifficultyCalculator : OsuDifficultyCalculator
        {
            private Skill[] skills = Array.Empty<Skill>();
            private ObjectRecorder recorder = null!;
            private IBeatmap? playableBeatmap;

            public ProfilingDifficultyCalculator(IWorkingBeatmap beatmap)
                : base(new OsuRuleset().RulesetInfo, beatmap)
            {
            }

            protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
            {
                playableBeatmap = beatmap;
                recorder = new ObjectRecorder(mods);
                skills = base.CreateSkills(beatmap, mods);

                // The recorder is processed alongside the real skills, so it sees objects in exactly the order they do.
                return skills.Append(recorder).ToArray();
            }

            private static IReadOnlyList<double> average(IReadOnlyList<double> a, IReadOnlyList<double> b) => a.Select((v, i) => (v + b[i]) / 2).ToArray();

            public SkillsetProfile CreateProfile()
            {
                var objects = recorder.Objects;

                if (playableBeatmap == null || objects.Count == 0)
                {
                    var empty = Enum.GetValues<Skillset>().ToDictionary(s => s, _ => (IReadOnlyList<double>)Array.Empty<double>());
                    return new SkillsetProfile(Array.Empty<HitObject>(), empty, Array.Empty<NoteFeatures>(), playableBeatmap);
                }

                var aim = skills.OfType<Aim>().Single(a => a.IncludeSliders).GetObjectDifficulties();
                var aimNoSliders = skills.OfType<Aim>().Single(a => !a.IncludeSliders).GetObjectDifficulties();
                var speed = skills.OfType<Speed>().Single().GetObjectDifficulties();
                var reading = skills.OfType<Reading>().Single().GetObjectDifficulties();

                var notes = PatternClassifier.Classify(playableBeatmap, objects);

                return new SkillsetProfile(objects.Select(o => o.BaseObject).ToArray(), new Dictionary<Skillset, IReadOnlyList<double>>
                {
                    [Skillset.Jumps] = recorder.Snap,
                    [Skillset.FlowAim] = recorder.Flow,
                    [Skillset.Streams] = speed,
                    [Skillset.SpacedStreams] = recorder.Agility,
                    [Skillset.Alt] = speed,
                    [Skillset.FingerControl] = speed.Select((s, i) => s * recorder.Rhythm[i]).ToArray(),
                    [Skillset.Tech] = aim.Select((a, i) => a * recorder.Rhythm[i]).ToArray(),
                    [Skillset.Sliders] = aim.Select((a, i) => Math.Max(0, a - aimNoSliders[i])).ToArray(),
                    [Skillset.LowAr] = reading,
                    [Skillset.HighAr] = aim.Select((a, i) => a + speed[i]).ToArray(),
                    [Skillset.Stamina] = speed,
                    [Skillset.Reading] = reading,
                    [Skillset.JumpsStreams] = average(recorder.Snap, speed),
                    [Skillset.FlowStreams] = average(recorder.Flow, speed),
                    [Skillset.TechSliders] = average(aim.Select((a, i) => a * recorder.Rhythm[i]).ToArray(), aim.Select((a, i) => Math.Max(0, a - aimNoSliders[i])).ToArray()),
                }, notes, playableBeatmap);
            }
        }

        /// <summary>
        /// Keeps every processed object, and the raw values of the evaluators that the aim and speed skills combine,
        /// so each skillset can use the one that fits it.
        /// </summary>
        private class ObjectRecorder : Skill
        {
            public readonly List<DifficultyHitObject> Objects = new List<DifficultyHitObject>();
            public readonly List<double> Snap = new List<double>();
            public readonly List<double> Flow = new List<double>();
            public readonly List<double> Agility = new List<double>();
            public readonly List<double> Rhythm = new List<double>();

            public ObjectRecorder(Mod[] mods)
                : base(mods)
            {
            }

            protected override double ProcessInternal(DifficultyHitObject current)
            {
                Objects.Add(current);

                // Same multipliers as the aim skill, so the values are on the scale lazer uses.
                Snap.Add(SnapAimEvaluator.EvaluateDifficultyOf(current, true) * 70.9);
                Flow.Add(FlowAimEvaluator.EvaluateDifficultyOf(current, true) * 242.0);
                Agility.Add(AgilityEvaluator.EvaluateDifficultyOf(current) * 2.35);
                Rhythm.Add(RhythmEvaluator.EvaluateDifficultyOf(current));
                return 0;
            }

            public override double DifficultyValue() => 0;
        }
    }

    /// <summary>
    /// Per-object data of a beatmap for every <see cref="Skillset"/>.
    /// <see cref="Difficulties"/>[skillset][i] is the difficulty of <see cref="Objects"/>[i] for that skillset, and <see cref="Notes"/>[i] its patterns.
    /// </summary>
    /// <remarks>The first hit object of a map has no difficulty value, so it is not included.</remarks>
    public record SkillsetProfile(
        IReadOnlyList<HitObject> Objects,
        IReadOnlyDictionary<Skillset, IReadOnlyList<double>> Difficulties,
        IReadOnlyList<NoteFeatures> Notes,
        IBeatmap? Beatmap);
}
