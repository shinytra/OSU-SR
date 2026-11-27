// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Skills;

namespace osu.Game.Rulesets.Osu.OsuSR
{
    /// <summary>
    /// Finds the hardest parts of a beatmap for each <see cref="Skillset"/>,
    /// using the per-object difficulties computed by the osu!standard difficulty calculator.
    /// </summary>
    public class SkillsetSegmentFinder
    {
        /// <summary>Target length (ms) of the hit object window of a segment.</summary>
        public double SegmentLength { get; init; } = 20_000;

        /// <summary>How long (ms) playback starts before the first object of a segment.</summary>
        public double LeadIn { get; init; } = 2_000;

        /// <summary>Minimum gap (ms) between two segments picked from the same map.</summary>
        public double MinimumGap { get; init; } = 2_000;

        /// <summary>
        /// Computes per-object difficulties for every skillset.
        /// </summary>
        public SkillsetProfile Analyse(IWorkingBeatmap beatmap, IEnumerable<Mod>? mods = null, CancellationToken cancellationToken = default)
        {
            var calculator = new ProfilingDifficultyCalculator(beatmap);
            calculator.Calculate(mods ?? Array.Empty<Mod>(), cancellationToken);
            return calculator.CreateProfile();
        }

        /// <summary>
        /// Returns up to <paramref name="maxSegments"/> non-overlapping segments, hardest first, for the given skillset.
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

            var prefixSums = Enum.GetValues<Skillset>().ToDictionary(s => s, s => prefixSum(profile.Difficulties[s]));

            // One candidate window per starting object: every object that starts within SegmentLength of it.
            var candidates = new List<(int First, int Last, double Score)>();
            int last = 0;

            for (int first = 0; first < objects.Count; first++)
            {
                last = Math.Max(last, first);

                while (last + 1 < objects.Count && objects[last + 1].StartTime - objects[first].StartTime <= SegmentLength)
                    last++;

                candidates.Add((first, last, density(prefixSums[skillset], first, last)));
            }

            var picked = new List<MapSegment>();

            foreach (var (first, lastIndex, _) in candidates.OrderByDescending(c => c.Score))
            {
                if (picked.Count >= maxSegments)
                    break;

                var segment = createSegment(first, lastIndex);

                if (picked.Any(p => segment.StartTime < p.EndTime + MinimumGap && p.StartTime < segment.EndTime + MinimumGap))
                    continue;

                picked.Add(segment);
            }

            return picked;

            MapSegment createSegment(int first, int lastIndex) => new MapSegment
            {
                Skillset = skillset,
                StartTime = Math.Max(0, objects[first].StartTime - LeadIn),
                FirstObjectTime = objects[first].StartTime,
                EndTime = objects[lastIndex].GetEndTime(),
                ObjectCount = lastIndex - first + 1,
                Scores = prefixSums.ToDictionary(p => p.Key, p => density(p.Value, first, lastIndex)),
            };

            double density(double[] sums, int first, int lastIndex)
            {
                // Divide by the full window length rather than the actual span, so that sparse windows don't get inflated scores.
                double seconds = Math.Max(SegmentLength, objects[lastIndex].StartTime - objects[first].StartTime) / 1000;
                return (sums[lastIndex + 1] - sums[first]) / seconds;
            }
        }

        private static double[] prefixSum(IReadOnlyList<double> values)
        {
            double[] sums = new double[values.Count + 1];

            for (int i = 0; i < values.Count; i++)
                sums[i + 1] = sums[i] + values[i];

            return sums;
        }

        /// <summary>
        /// Runs the regular osu!standard difficulty calculation, and keeps the skills and the processed objects afterwards.
        /// </summary>
        private class ProfilingDifficultyCalculator : OsuDifficultyCalculator
        {
            private Skill[] skills = Array.Empty<Skill>();
            private ObjectRecorder recorder = null!;

            public ProfilingDifficultyCalculator(IWorkingBeatmap beatmap)
                : base(new OsuRuleset().RulesetInfo, beatmap)
            {
            }

            protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
            {
                recorder = new ObjectRecorder(mods);
                skills = base.CreateSkills(beatmap, mods);

                // The recorder is processed alongside the real skills, so it sees objects in exactly the order they do.
                return skills.Append(recorder).ToArray();
            }

            public SkillsetProfile CreateProfile()
            {
                var aim = skills.OfType<Aim>().Single(a => a.IncludeSliders);
                var speed = skills.OfType<Speed>().Single();
                var reading = skills.OfType<Reading>().Single();

                return new SkillsetProfile(recorder.Objects.Select(o => o.BaseObject).ToArray(), new Dictionary<Skillset, IReadOnlyList<double>>
                {
                    [Skillset.Aim] = aim.GetObjectDifficulties(),
                    [Skillset.Speed] = speed.GetObjectDifficulties(),
                    [Skillset.Reading] = reading.GetObjectDifficulties(),
                });
            }
        }

        private class ObjectRecorder : Skill
        {
            public readonly List<DifficultyHitObject> Objects = new List<DifficultyHitObject>();

            public ObjectRecorder(Mod[] mods)
                : base(mods)
            {
            }

            protected override double ProcessInternal(DifficultyHitObject current)
            {
                Objects.Add(current);
                return 0;
            }

            public override double DifficultyValue() => 0;
        }
    }

    /// <summary>
    /// Per-object difficulties of a beatmap for every <see cref="Skillset"/>.
    /// <see cref="Difficulties"/>[skillset][i] is the difficulty of <see cref="Objects"/>[i].
    /// </summary>
    /// <remarks>The first hit object of a map has no difficulty value, so it is not included.</remarks>
    public record SkillsetProfile(IReadOnlyList<HitObject> Objects, IReadOnlyDictionary<Skillset, IReadOnlyList<double>> Difficulties);
}
