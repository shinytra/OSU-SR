// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// A playable part of a beatmap, picked because it is hard for one <see cref="Skillset"/>.
    /// </summary>
    public record MapSegment
    {
        /// <summary>The beatmap this segment belongs to, when known.</summary>
        public Guid? BeatmapID { get; init; }

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
        /// Star rating of the segment alone (the map with everything else removed), when it was calculated.
        /// It is usually lower than the map's own, since a short part has fewer difficult notes to add up.
        /// </summary>
        public double? StarRating { get; init; }

        /// <summary>
        /// Score of the segment for each skillset: the difficulty of the notes that match the skillset's patterns, per second, times the share of matching notes.
        /// Values of the same skillset are comparable across maps; values of different skillsets are on different scales.
        /// </summary>
        public required IReadOnlyDictionary<Skillset, double> Scores { get; init; }

        /// <summary>
        /// Share (0 to 1) of the segment's notes that match its <see cref="Skillset"/>'s patterns.
        /// </summary>
        public double Purity { get; init; } = 1;

        /// <summary>
        /// All the skillsets that make up a real part of the segment (at least a quarter of its notes), the one it was picked for included when it is one of them.
        /// A session only plays a segment if all of them are picked. See <see cref="SkillsetCombinations"/>.
        /// </summary>
        public IReadOnlyList<Skillset> Labels { get; init; } = Array.Empty<Skillset>();

        /// <summary>
        /// Weighted star rating (see <see cref="SkillsetWeights"/>) of each label, from the notes that make it up alone, when it was calculated.
        /// A label is only kept if it is hard enough next to the hardest one: 60 % jumps at 8★ and 40 % streams at 6★ is a jumps part.
        /// </summary>
        public IReadOnlyDictionary<Skillset, double>? LabelStars { get; init; }

        /// <summary>The score of this segment for its own <see cref="Skillset"/>.</summary>
        public double Score => Scores[Skillset];

        public double Duration => EndTime - StartTime;

        public override string ToString() => $"{Skillset} {StartTime:N0}-{EndTime:N0}ms ({ObjectCount} objects, score {Score:N2}, purity {Purity:P0}{(StarRating is double stars ? $", {stars:N2}★" : string.Empty)})";
    }
}
