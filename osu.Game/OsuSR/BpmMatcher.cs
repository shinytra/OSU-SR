// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Decides whether two maps' tempos are close enough for one to follow the other without the beat jumping.
    /// Half and double time count as matching, since the beat pulse is the same.
    /// </summary>
    public static class BpmMatcher
    {
        /// <summary>Tolerance (relative) used first: 7% is about the tempo change of a quarter tone.</summary>
        public const double STRICT_TOLERANCE = 0.07;

        /// <summary>Tolerance used when no map matches the strict one.</summary>
        public const double RELAXED_TOLERANCE = 0.15;

        /// <summary>
        /// The relative distance between two tempos once half and double time are folded in: 0 for the same tempo (or half, or double), 0.1 for 10% apart.
        /// </summary>
        public static double Distance(double bpmA, double bpmB)
        {
            if (bpmA <= 0 || bpmB <= 0)
                return double.PositiveInfinity;

            double ratio = bpmB / bpmA;
            return Math.Min(Math.Abs(ratio - 1), Math.Min(Math.Abs(ratio / 2 - 1), Math.Abs(ratio * 2 - 1)));
        }

        public static bool IsClose(double bpmA, double bpmB, double tolerance) => Distance(bpmA, bpmB) <= tolerance;

        /// <summary>Tempo changes beyond these are too audible to be a "remix" of the music, so a ramp stops there.</summary>
        public const double MINIMUM_TEMPO = 0.7;

        public const double MAXIMUM_TEMPO = 1.4;

        /// <summary>
        /// The tempo (1 = unchanged) at which music at <paramref name="fromBpm"/> must play to run at the pulse of <paramref name="toBpm"/>,
        /// folding half and double time (90 to 180 BPM is a ratio of 1) and limited to <see cref="MINIMUM_TEMPO"/>..<see cref="MAXIMUM_TEMPO"/>.
        /// </summary>
        public static double TempoRatio(double fromBpm, double toBpm)
        {
            if (fromBpm <= 0 || toBpm <= 0)
                return 1;

            double ratio = toBpm / fromBpm;
            double best = ratio;

            foreach (double candidate in new[] { ratio / 2, ratio * 2 })
            {
                if (Math.Abs(candidate - 1) < Math.Abs(best - 1))
                    best = candidate;
            }

            return Math.Clamp(best, MINIMUM_TEMPO, MAXIMUM_TEMPO);
        }
    }
}
