// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;

namespace osu.Game.OsuSR
{
    /// <summary>One segment of a series, with the map it comes from.</summary>
    public record SeriesPart(WorkingBeatmap Beatmap, MapSegment Segment);

    /// <summary>
    /// Where one part of a series sits on the shared timeline of the series (ms).
    /// </summary>
    /// <param name="Shift">Added to the map's own times to get timeline times.</param>
    /// <param name="MusicStart">Timeline time at which this part's music starts (its segment's start, on a bar line).</param>
    /// <param name="FirstObjectTime">Timeline time of its first object.</param>
    /// <param name="EndTime">Timeline time at which its last object ends.</param>
    /// <param name="Gap">Time between its last object and the first object of the next part (0 for the last part).</param>
    /// <param name="StartTempo">Tempo of its music when it starts: it plays at the pulse of the previous part, then ramps to 1 by its first object (1 for the first part).</param>
    /// <param name="EndTempo">Tempo its music ramps to while it fades out, to go towards the pulse of the next part (1 for the last part).</param>
    public record SeriesSlot(double Shift, double MusicStart, double FirstObjectTime, double EndTime, double Gap, double StartTempo = 1, double EndTempo = 1);

    /// <summary>
    /// Puts the segments of a series one after the other on a single timeline, so they can be played as one continuous gameplay.
    /// The first object of a part comes shortly after the last object of the previous one (a couple of beats of the previous map),
    /// and the music of a part starts early enough (its own lead-in, on a bar line) to be at full volume on its first object.
    /// </summary>
    public static class SeriesTimeline
    {
        /// <summary>Shortest and longest time between the last object of a part and the first object of the next one (ms).</summary>
        public const double MINIMUM_GAP = 600;

        public const double MAXIMUM_GAP = 1200;

        /// <summary>Beats of the previous part's tempo between its last object and the next part's first object.</summary>
        public const double GAP_BEATS = 2;

        /// <summary>How long the last part's music takes to fade out after its last object (ms).</summary>
        public const double FINAL_FADE_OUT = 1000;

        /// <summary>How long before a part's last object its music starts to fade out (ms).</summary>
        public const double FADE_OUT_LEAD = 300;

        public static IReadOnlyList<SeriesSlot> Build(IReadOnlyList<SeriesPart> parts)
        {
            var slots = new List<SeriesSlot>();
            double previousEnd = 0;
            double previousGap = 0;

            for (int i = 0; i < parts.Count; i++)
            {
                var segment = parts[i].Segment;
                double leadIn = segment.FirstObjectTime - segment.StartTime;

                double musicStart = i == 0 ? 0 : previousEnd + previousGap - leadIn;
                double shift = musicStart - segment.StartTime;
                double end = shift + segment.EndTime;

                double gap = 0;

                if (i + 1 < parts.Count)
                {
                    double gapBpm = parts[i].Beatmap.BeatmapInfo.BPM;
                    gap = gapBpm > 0 ? Math.Clamp(GAP_BEATS * 60000 / gapBpm, MINIMUM_GAP, MAXIMUM_GAP) : MINIMUM_GAP;
                }

                double bpm = parts[i].Beatmap.BeatmapInfo.BPM;
                double startTempo = i == 0 ? 1 : BpmMatcher.TempoRatio(bpm, parts[i - 1].Beatmap.BeatmapInfo.BPM);
                double endTempo = i + 1 < parts.Count ? BpmMatcher.TempoRatio(bpm, parts[i + 1].Beatmap.BeatmapInfo.BPM) : 1;

                slots.Add(new SeriesSlot(shift, musicStart, shift + segment.FirstObjectTime, end, gap, startTempo, endTempo));
                previousEnd = end;
                previousGap = gap;
            }

            return slots;
        }

        /// <summary>Total length of the series gameplay (ms), including the final fade out.</summary>
        public static double TotalLength(IReadOnlyList<SeriesSlot> slots) => slots.Count == 0 ? 0 : slots[^1].EndTime + FINAL_FADE_OUT + 500;

        /// <summary>
        /// Volume (0-1) of a part's music at a timeline time: fades in over its lead-in, fades out across the junction with the next part.
        /// </summary>
        public static double VolumeAt(IReadOnlyList<SeriesSlot> slots, int index, double time)
        {
            var slot = slots[index];

            double fadeIn = slot.FirstObjectTime - slot.MusicStart;
            double volume = fadeIn > 0 ? Math.Clamp((time - slot.MusicStart) / fadeIn, 0, 1) : 1;

            double fadeOutStart = slot.EndTime - FADE_OUT_LEAD;
            double fadeOutLength = (index + 1 < slots.Count ? slot.Gap : FINAL_FADE_OUT) + FADE_OUT_LEAD;
            volume *= 1 - Math.Clamp((time - fadeOutStart) / fadeOutLength, 0, 1);

            return volume;
        }

        /// <summary>
        /// Tempo (1 = original speed) of a part's music: it starts at the pulse of the previous part and eases to 1 by its first object,
        /// and eases towards the pulse of the next part while it fades out. This is what blends two songs of different BPM.
        /// </summary>
        public static double TempoAt(IReadOnlyList<SeriesSlot> slots, int index, double time)
        {
            var slot = slots[index];

            if (time < slot.FirstObjectTime)
            {
                double fadeIn = slot.FirstObjectTime - slot.MusicStart;
                double progress = fadeIn > 0 ? Math.Clamp((time - slot.MusicStart) / fadeIn, 0, 1) : 1;
                return slot.StartTempo + (1 - slot.StartTempo) * progress;
            }

            if (time > slot.EndTime)
            {
                double length = (index + 1 < slots.Count ? slot.Gap : FINAL_FADE_OUT) + FADE_OUT_LEAD;
                return 1 + (slot.EndTempo - 1) * Math.Clamp((time - slot.EndTime) / length, 0, 1);
            }

            return 1;
        }

        /// <summary>
        /// Where (ms in the map's own time) a part's music should be at a timeline time while the part's objects are in play,
        /// following the tempo ramp of its lead-in so that it is exactly in time on its first object. Null after its last object.
        /// </summary>
        public static double? TrackPositionAt(IReadOnlyList<SeriesSlot> slots, int index, double time)
        {
            var slot = slots[index];

            if (time > slot.EndTime)
                return null;

            if (time >= slot.FirstObjectTime)
                return time - slot.Shift;

            // The position moves at the tempo, which is linear in time over the lead-in: the distance to the first object is the mean tempo times the time left.
            double tempo = TempoAt(slots, index, time);
            return slot.FirstObjectTime - slot.Shift - (slot.FirstObjectTime - time) * (1 + tempo) / 2;
        }

        /// <summary>Whether a part's music should be playing at this timeline time.</summary>
        public static bool IsAudible(IReadOnlyList<SeriesSlot> slots, int index, double time)
        {
            var slot = slots[index];
            double fadeOutLength = (index + 1 < slots.Count ? slot.Gap : FINAL_FADE_OUT) + FADE_OUT_LEAD;
            return time >= slot.MusicStart && time < slot.EndTime - FADE_OUT_LEAD + fadeOutLength;
        }

        /// <summary>The part whose objects are being played at a timeline time (the one just ended, until the next part's first object is near).</summary>
        public static int PartAt(IReadOnlyList<SeriesSlot> slots, double time)
        {
            for (int i = 0; i + 1 < slots.Count; i++)
            {
                if (time < slots[i].EndTime + slots[i].Gap / 2)
                    return i;
            }

            return Math.Max(0, slots.Count - 1);
        }
    }
}
