// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Rulesets.Osu.OsuSR
{
    /// <summary>
    /// A playable part of a beatmap, picked because it is hard for one <see cref="Skillset"/>.
    /// </summary>
    public class MapSegment
    {
        /// <summary>The skillset this segment was picked for.</summary>
        public required Skillset Skillset { get; init; }

        /// <summary>Time (ms) at which playback should start, including a short lead-in before the first object.</summary>
        public required double StartTime { get; init; }

        /// <summary>Time (ms) of the first hit object in the segment.</summary>
        public required double FirstObjectTime { get; init; }

        /// <summary>Time (ms) at which the last hit object in the segment ends.</summary>
        public required double EndTime { get; init; }

        /// <summary>Number of hit objects in the segment.</summary>
        public required int ObjectCount { get; init; }

        /// <summary>
        /// Difficulty density of the segment for each skillset: the sum of per-object difficulties divided by the segment length in seconds.
        /// Values of the same skillset are comparable across maps; values of different skillsets are on different scales.
        /// </summary>
        public required IReadOnlyDictionary<Skillset, double> Scores { get; init; }

        /// <summary>The score of this segment for its own <see cref="Skillset"/>.</summary>
        public double Score => Scores[Skillset];

        public double Duration => EndTime - StartTime;

        public override string ToString() => $"{Skillset} {StartTime:N0}-{EndTime:N0}ms ({ObjectCount} objects, score {Score:N2})";
    }
}
