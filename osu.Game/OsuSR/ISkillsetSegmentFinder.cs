// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;

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
        /// Does the heavy part of getting a series part ready (reading the map, creating its hit objects), so it can be done on a background thread.
        /// </summary>
        ISeriesPartData PrepareSeriesPart(SeriesPart part, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the beatmap of a series that starts with the given part. Further parts are added with <see cref="AppendSeriesPart"/>.
        /// </summary>
        IBeatmap CreateSeriesBeatmap(ISeriesPartData first, SeriesSlot slot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Puts a part at the end of a series beatmap (which may be playing): its hit objects are moved to its slot of the series timeline and get the
        /// difficulty settings (approach rate, circle size, ...) of their own map.
        /// </summary>
        /// <param name="beatmap">The series beatmap. Its hit objects and timing points grow.</param>
        /// <param name="part">The prepared part.</param>
        /// <param name="slot">Where the part is on the timeline.</param>
        /// <param name="timingSwitchTime">Timeline time from which the part's tempo applies (between the last object of the previous part and the first of this one).</param>
        /// <returns>The new hit objects, ready to be added to a playfield.</returns>
        IReadOnlyList<HitObject> AppendSeriesPart(IBeatmap beatmap, ISeriesPartData part, SeriesSlot slot, double timingSwitchTime);
    }

    /// <summary>
    /// A series part that has been read and converted, waiting to be put on the timeline (see <see cref="ISkillsetSegmentFinder.PrepareSeriesPart"/>).
    /// </summary>
    public interface ISeriesPartData
    {
        SeriesPart Part { get; }
    }

    /// <summary>
    /// Implemented by rulesets that support OSU SR training sessions.
    /// </summary>
    public interface IHasSkillsetSegments
    {
        ISkillsetSegmentFinder CreateSkillsetSegmentFinder();
    }
}
