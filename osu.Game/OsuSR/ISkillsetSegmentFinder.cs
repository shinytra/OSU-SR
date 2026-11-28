// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Finds the hardest parts of a beatmap for a <see cref="Skillset"/>.
    /// </summary>
    public interface ISkillsetSegmentFinder
    {
        /// <summary>
        /// Returns up to <paramref name="maxSegments"/> non-overlapping segments, hardest first.
        /// </summary>
        IReadOnlyList<MapSegment> FindSegments(IWorkingBeatmap beatmap, Skillset skillset, int maxSegments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Same as <see cref="FindSegments(IWorkingBeatmap, Skillset, int, CancellationToken)"/> for several skillsets at once, analysing the map only once.
        /// Skillsets without any segment in this map are left out.
        /// </summary>
        IReadOnlyDictionary<Skillset, IReadOnlyList<MapSegment>> FindSegments(IWorkingBeatmap beatmap, IEnumerable<Skillset> skillsets, int maxSegments,
                                                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Extra minimum purity per skillset, on top of each skillset's default. Raised by "wrongly classified" reports.
        /// </summary>
        IReadOnlyDictionary<Skillset, double> PurityAdjustments { get; set; }

        /// <summary>
        /// Calculates the star rating of one segment alone, as if the rest of the map didn't exist.
        /// </summary>
        double CalculateStarRating(IWorkingBeatmap beatmap, MapSegment segment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the mod that restricts gameplay to the given segment.
        /// </summary>
        Mod CreateSegmentMod(MapSegment segment);

        /// <summary>
        /// Makes a segment longer for a series: adds the build-up before it (the pre-drop) and a little of what follows it (the post-drop),
        /// stopping at breaks, and snapping to bar lines.
        /// </summary>
        MapSegment ExtendSegment(IWorkingBeatmap beatmap, MapSegment segment, double preDrop, double postDrop, CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds a single beatmap holding all parts of a series one after the other, on the given timeline.
        /// Difficulty settings (approach rate, circle size, ...) come from the first part.
        /// </summary>
        IBeatmap CreateSeriesBeatmap(IReadOnlyList<SeriesPart> parts, IReadOnlyList<SeriesSlot> slots, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implemented by rulesets that support OSU SR training sessions.
    /// </summary>
    public interface IHasSkillsetSegments
    {
        ISkillsetSegmentFinder CreateSkillsetSegmentFinder();
    }
}
