// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
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
        /// The mods the player plays with (easy, half time, hard rock, double time...). Segments are found and rated with them applied,
        /// since a part is not the same part under double time or hard rock.
        /// </summary>
        IReadOnlyList<Mod> Mods { get; set; }

        /// <summary>Weights that make the star ratings of different skillsets comparable when deciding which skillsets a part really has.</summary>
        SkillsetWeights Weights { get; set; }

        /// <summary>
        /// Works out which skillsets each segment really has (a skillset needs a real share of the notes and a weighted star rating close to the hardest one's),
        /// and the segment's star rating from those notes. Analyses the map once for all the segments.
        /// </summary>
        IReadOnlyList<MapSegment> RefineSegments(IWorkingBeatmap beatmap, IReadOnlyList<MapSegment> segments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the segments of every given skillset and rates them (see <see cref="RefineSegments"/>), analysing the map only once.
        /// Skillsets without a segment are in the result with an empty list.
        /// </summary>
        IReadOnlyDictionary<Skillset, IReadOnlyList<MapSegment>> FindRefinedSegments(IWorkingBeatmap beatmap, IReadOnlyCollection<Skillset> skillsets, int maxSegments,
                                                                                     CancellationToken cancellationToken = default);

        /// <summary>Total time (seconds, all threads together) spent analysing maps and rating segments so far. For finding where indexing is slow.</summary>
        (double Analysing, double Rating) TimeSpent { get; }

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
        /// Creates the hit objects of a series part at its slot of the series timeline: they get the difficulty settings (approach rate, circle size, ...) of their own map,
        /// and their combo numbers go on from the previous part. Has no effect outside of <paramref name="state"/>, so it can run on a background thread.
        /// </summary>
        /// <param name="part">The prepared part.</param>
        /// <param name="slot">Where the part is on the timeline.</param>
        /// <param name="timingSwitchTime">Timeline time from which the part's tempo applies (between the last object of the previous part and the first of this one).</param>
        /// <param name="state">What the previous parts left behind (timing points, combo numbers). Updated.</param>
        IReadOnlyList<HitObject> CreateSeriesObjects(ISeriesPartData part, SeriesSlot slot, double timingSwitchTime, SeriesBuildState state);

        /// <summary>
        /// Creates the beatmap of a series, which starts with the given part. Further parts are added with <see cref="AppendSeriesPart"/>.
        /// </summary>
        IBeatmap CreateSeriesBeatmap(SeriesPartObjects first);

        /// <summary>
        /// Puts a part at the end of the beatmap of a series (which may be playing).
        /// </summary>
        void AppendSeriesPart(IBeatmap beatmap, SeriesPartObjects part);
    }

    /// <summary>
    /// A series part that has been read and converted, waiting to be put on the timeline (see <see cref="ISkillsetSegmentFinder.PrepareSeriesPart"/>).
    /// </summary>
    public interface ISeriesPartData
    {
        SeriesPart Part { get; }

        /// <summary>The tempo of the part.</summary>
        TimingControlPoint Timing { get; }
    }

    /// <summary>
    /// Implemented by rulesets that support OSU SR training sessions.
    /// </summary>
    public interface IHasSkillsetSegments
    {
        ISkillsetSegmentFinder CreateSkillsetSegmentFinder();
    }
}
