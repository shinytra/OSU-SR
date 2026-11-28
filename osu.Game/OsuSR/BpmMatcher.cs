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
    }
}
