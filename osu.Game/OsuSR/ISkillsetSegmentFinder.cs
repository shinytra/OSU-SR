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
        /// Creates the mod that restricts gameplay to the given segment.
        /// </summary>
        Mod CreateSegmentMod(MapSegment segment);
    }

    /// <summary>
    /// Implemented by rulesets that support OSU SR training sessions.
    /// </summary>
    public interface IHasSkillsetSegments
    {
        ISkillsetSegmentFinder CreateSkillsetSegmentFinder();
    }
}
