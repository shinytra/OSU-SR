// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.OsuSR
{
    /// <summary>
    /// What a single note is, as a pattern. A note can be several of these at once (a spaced stream note is also a stream note, for example).
    /// </summary>
    [Flags]
    public enum NotePattern
    {
        None = 0,

        /// <summary>A 1/2 or 1/1 move of 2+ radii that turns (snap aim).</summary>
        Jump = 1 << 0,

        /// <summary>A spaced move that keeps going roughly in the same direction (angle ≥ <see cref="PatternClassifier.FLOW_ANGLE"/>).</summary>
        FlowAim = 1 << 1,

        /// <summary>Part of a chain of 2 to 8 tightly snapped notes.</summary>
        Burst = 1 << 2,

        /// <summary>Part of a chain of 9+ tightly snapped notes.</summary>
        Stream = 1 << 3,

        /// <summary>Part of a stream where at least half the steps are 2+ radii.</summary>
        SpacedStream = 1 << 4,

        /// <summary>Part of a stream of <see cref="PatternClassifier.DEATHSTREAM_LENGTH"/>+ notes.</summary>
        Deathstream = 1 << 5,

        /// <summary>A fast 1/2 that has to be alternated, or a burst note.</summary>
        Alt = 1 << 6,

        /// <summary>A note where the rhythm changes, or an odd snap (1/3, 1/6...) outside long streams.</summary>
        FingerControl = 1 << 7,

        /// <summary>A slider with unusual velocity or repeats, or a rhythm change around a slider.</summary>
        Tech = 1 << 8,

        /// <summary>A short slider that has to be followed quickly, or a buzz slider.</summary>
        FastSlider = 1 << 9,

        /// <summary>Low approach rate with many objects on screen.</summary>
        LowAR = 1 << 10,

        /// <summary>High approach rate (objects appear late).</summary>
        HighAR = 1 << 11,
    }

    /// <summary>
    /// Measurements of a note relative to the previous one.
    /// </summary>
    /// <param name="BeatFraction">Time since the previous note's start, in beats (0.5 = 1/2, 0.25 = 1/4).</param>
    /// <param name="DistanceRadii">Distance from the previous note (from its end for sliders), in circle radii.</param>
    /// <param name="AngleDegrees">Angle at the previous note between the incoming and outgoing moves. 180 = straight line, 0 = back and forth.</param>
    /// <param name="ChainLength">Number of notes in the tight (≤ 1/3 beat) chain this note belongs to, or 0.</param>
    /// <param name="VisibleObjects">Objects on screen when this note has to be hit.</param>
    /// <param name="Pattern">What this note is.</param>
    public readonly record struct NoteFeatures(double BeatFraction, double DistanceRadii, double? AngleDegrees, int ChainLength, int VisibleObjects, NotePattern Pattern);

    /// <summary>
    /// Labels every note of a map with the <see cref="NotePattern"/>s it belongs to, using simple rules on rhythm (in beats), distance (in radii) and angles.
    /// </summary>
    /// <remarks>
    /// Thresholds come from community classifiers (mainly AliKhairy/osu-beatmap-classifier's feature spec) and are meant to be tuned by playing.
    /// Times are measured on the unmodified map: beat fractions don't depend on clock rate.
    /// </remarks>
    public static class PatternClassifier
    {
        /// <summary>Notes closer than this (in beats) form a chain (bursts, streams). 0.30 includes 1/4 and 1/3, excludes 1/2.</summary>
        public const double CHAIN_BEAT_FRACTION = 0.30;

        /// <summary>Jumps are between 1/2 and 1/1 (a bit of tolerance on each side).</summary>
        public const double JUMP_MIN_BEAT_FRACTION = 0.40;

        public const double JUMP_MAX_BEAT_FRACTION = 1.10;

        /// <summary>Minimum distance (radii) of a jump, or of a spaced stream step.</summary>
        public const double JUMP_MIN_DISTANCE = 2;

        /// <summary>Minimum distance (radii) of a flow aim move.</summary>
        public const double FLOW_MIN_DISTANCE = 1.5;

        /// <summary>Moves at least this open (degrees) are flow, tighter ones are snaps.</summary>
        public const double FLOW_ANGLE = 110;

        public const int STREAM_LENGTH = 9;

        public const int DEATHSTREAM_LENGTH = 33;

        /// <summary>1/2 notes faster than this (ms) have to be alternated (240 BPM).</summary>
        public const double ALT_MAX_DELTA = 125;

        /// <summary>Preempt (ms) at or above which a map is low AR (AR 8.5), and at or below which it is high AR (AR 10).</summary>
        public const double LOW_AR_PREEMPT = 675;

        public const double HIGH_AR_PREEMPT = 450;

        /// <summary>Objects on screen needed for a low AR note to count as a reading challenge.</summary>
        public const int LOW_AR_MIN_VISIBLE = 4;

        /// <summary>Notes further apart than this (ms) don't form any pattern together.</summary>
        private const double max_active_delta = 2000;

        /// <param name="beatmap">The playable beatmap the objects come from (for timing points).</param>
        /// <param name="objects">Difficulty objects in order, as processed by the difficulty calculator.</param>
        public static NoteFeatures[] Classify(IBeatmap beatmap, IReadOnlyList<DifficultyHitObject> objects)
        {
            int count = objects.Count;
            var result = new NoteFeatures[count];

            if (count == 0)
                return result;

            var notes = objects.Cast<OsuDifficultyHitObject>().ToArray();
            var bases = notes.Select(n => (OsuHitObject)n.BaseObject).ToArray();

            double[] beatFraction = new double[count];
            double[] delta = new double[count];
            double[] distance = new double[count];
            double?[] angle = new double?[count];
            bool[] active = new bool[count];

            for (int i = 0; i < count; i++)
            {
                var previous = (OsuHitObject)notes[i].LastObject;
                double beatLength = beatmap.ControlPointInfo.TimingPointAt(bases[i].StartTime).BeatLength;

                delta[i] = bases[i].StartTime - previous.StartTime;
                beatFraction[i] = beatLength > 0 ? delta[i] / beatLength : double.PositiveInfinity;
                distance[i] = notes[i].LazyJumpDistance / OsuDifficultyHitObject.NORMALISED_RADIUS;
                angle[i] = notes[i].Angle * 180 / Math.PI;
                active[i] = bases[i] is not Spinner && previous is not Spinner && delta[i] <= max_active_delta;
            }

            // Chains: runs of tight notes. A chain of N notes has N-1 tight transitions; the note before the first tight transition starts the chain.
            int[] chainLength = new int[count];
            double[] chainSpacedFraction = new double[count];

            for (int i = 0; i < count;)
            {
                if (!(active[i] && beatFraction[i] <= CHAIN_BEAT_FRACTION))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < count && active[i] && beatFraction[i] <= CHAIN_BEAT_FRACTION)
                    i++;

                int transitions = i - start;
                double spaced = (double)Enumerable.Range(start, transitions).Count(t => distance[t] >= JUMP_MIN_DISTANCE) / transitions;

                // The chain includes the note before its first tight transition, when that note is in the list.
                for (int j = Math.Max(0, start - 1); j < i; j++)
                {
                    chainLength[j] = transitions + 1;
                    chainSpacedFraction[j] = spaced;
                }
            }

            double medianVelocity = median(bases.OfType<Slider>().Select(s => s.Velocity));

            int visibleEnd = 0;

            for (int i = 0; i < count; i++)
            {
                var pattern = NotePattern.None;
                var current = bases[i];
                double beatLength = beatmap.ControlPointInfo.TimingPointAt(current.StartTime).BeatLength;
                int chain = chainLength[i];

                // Objects on screen when this one is hit: the ones that started fading in, not yet hit.
                visibleEnd = Math.Max(visibleEnd, i);
                while (visibleEnd + 1 < count && bases[visibleEnd + 1].StartTime - bases[visibleEnd + 1].TimePreempt <= current.StartTime)
                    visibleEnd++;
                int visible = visibleEnd - i + 1;

                if (chain >= 2)
                {
                    if (chain < STREAM_LENGTH)
                        pattern |= NotePattern.Burst | NotePattern.Alt;
                    else
                    {
                        pattern |= NotePattern.Stream;

                        if (chainSpacedFraction[i] >= 0.5)
                            pattern |= NotePattern.SpacedStream;

                        if (chain >= DEATHSTREAM_LENGTH)
                            pattern |= NotePattern.Deathstream;
                    }
                }

                if (active[i])
                {
                    double fraction = beatFraction[i];
                    bool tight = fraction <= CHAIN_BEAT_FRACTION;
                    bool jumpRhythm = fraction >= JUMP_MIN_BEAT_FRACTION && fraction <= JUMP_MAX_BEAT_FRACTION;
                    bool open = angle[i] >= FLOW_ANGLE;

                    // Without an angle (first moves, after spinners) a move can't be told snap or flow, so it is neither.
                    if (angle[i] != null)
                    {
                        if (jumpRhythm && distance[i] >= JUMP_MIN_DISTANCE)
                            pattern |= open ? NotePattern.FlowAim : NotePattern.Jump;
                        else if (tight && distance[i] >= FLOW_MIN_DISTANCE && open)
                            pattern |= NotePattern.FlowAim;
                    }

                    if (jumpRhythm && fraction <= 0.6 && delta[i] <= ALT_MAX_DELTA)
                        pattern |= NotePattern.Alt;

                    bool rhythmChanged = i > 0 && active[i - 1] && Math.Abs(fraction - beatFraction[i - 1]) > 0.15 * Math.Min(fraction, beatFraction[i - 1]);
                    bool oddSnap = isOddSnap(fraction);

                    if (fraction <= JUMP_MAX_BEAT_FRACTION && (rhythmChanged || oddSnap) && chain < STREAM_LENGTH)
                        pattern |= NotePattern.FingerControl;

                    bool nearSlider = current is Slider || (i > 0 && bases[i - 1] is Slider);

                    if (rhythmChanged && nearSlider)
                        pattern |= NotePattern.Tech;

                    if (current.TimePreempt >= LOW_AR_PREEMPT && visible >= LOW_AR_MIN_VISIBLE)
                        pattern |= NotePattern.LowAR;

                    if (current.TimePreempt <= HIGH_AR_PREEMPT && fraction <= JUMP_MAX_BEAT_FRACTION)
                        pattern |= NotePattern.HighAR;
                }

                if (current is Slider slider && beatLength > 0)
                {
                    double pathRadii = slider.Path.Distance / slider.Radius;
                    double spanBeats = slider.SpanDuration / beatLength;
                    bool buzz = slider.RepeatCount >= 2 && spanBeats <= 0.10;
                    bool fastShort = pathRadii >= JUMP_MIN_DISTANCE && spanBeats <= 1.0;

                    if (buzz || fastShort)
                        pattern |= NotePattern.FastSlider;

                    bool unusualVelocity = medianVelocity > 0 && (slider.Velocity < medianVelocity * 0.8 || slider.Velocity > medianVelocity * 1.25);

                    if (unusualVelocity || (slider.RepeatCount >= 1 && !buzz))
                        pattern |= NotePattern.Tech;
                }

                result[i] = new NoteFeatures(beatFraction[i], distance[i], angle[i], chain, visible, pattern);
            }

            return result;
        }

        /// <summary>
        /// Whether a beat fraction is a "triplet" snap (1/3, 2/3, 1/6...) rather than a straight one (1/1, 1/2, 1/4, 3/4, 1/8).
        /// </summary>
        private static bool isOddSnap(double fraction)
        {
            static bool near(double value, int divisor) => Math.Abs(value * divisor - Math.Round(value * divisor)) <= 0.08;

            return !near(fraction, 4) && !near(fraction, 8) && (near(fraction, 3) || near(fraction, 6));
        }

        private static double median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }
    }
}
