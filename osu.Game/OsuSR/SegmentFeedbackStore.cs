// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Keeps the player's "wrongly classified" reports on disk (in the game's storage, nothing is sent anywhere),
    /// and turns them into stricter minimum purities for the skillsets that were reported.
    /// </summary>
    public class SegmentFeedbackStore
    {
        /// <summary>How much a minimum purity grows (0 to 1) for each report of its skillset.</summary>
        public const double ADJUSTMENT_STEP = 0.03;

        /// <summary>The most a minimum purity can grow, so a few reports can't make a skillset impossible to find.</summary>
        public const double MAXIMUM_ADJUSTMENT = 0.3;

        private const string directory = "osu-sr";
        private const string feedback_file = "feedback.jsonl";
        private const string adjustments_file = "purity-adjustments.json";

        private readonly Storage storage;
        private readonly object syncRoot = new object();

        private Dictionary<Skillset, double> adjustments = new Dictionary<Skillset, double>();
        private int reportCount;

        public SegmentFeedbackStore(Storage storage)
        {
            this.storage = storage.GetStorageForDirectory(directory);
            load();
        }

        /// <summary>
        /// Extra minimum purity per skillset, on top of the skillset's default. A new copy is returned each time, so it is safe to keep.
        /// </summary>
        public IReadOnlyDictionary<Skillset, double> PurityAdjustments
        {
            get
            {
                lock (syncRoot)
                    return new Dictionary<Skillset, double>(adjustments);
            }
        }

        /// <summary>Number of "wrongly classified" reports saved.</summary>
        public int ReportCount
        {
            get
            {
                lock (syncRoot)
                    return reportCount;
            }
        }

        /// <summary>
        /// Saves a report for a segment and makes its skillset stricter.
        /// </summary>
        /// <returns>The skillset's new extra purity.</returns>
        public double ReportMisclassified(MapSegment segment, BeatmapInfo beatmap)
        {
            lock (syncRoot)
            {
                double adjustment = Math.Min(MAXIMUM_ADJUSTMENT, adjustments.GetValueOrDefault(segment.Skillset) + ADJUSTMENT_STEP);
                adjustments[segment.Skillset] = adjustment;
                reportCount++;

                try
                {
                    var report = new Report
                    {
                        Time = DateTimeOffset.UtcNow,
                        Skillset = segment.Skillset.ToString(),
                        Beatmap = $"{beatmap.Metadata.Artist} - {beatmap.Metadata.Title} [{beatmap.DifficultyName}]",
                        BeatmapMd5 = beatmap.MD5Hash,
                        StartTime = segment.FirstObjectTime,
                        EndTime = segment.EndTime,
                        Purity = segment.Purity,
                        Score = segment.Score,
                        SegmentStars = segment.StarRating,
                        NewAdjustment = adjustment,
                    };

                    using (var stream = storage.GetStream(feedback_file, FileAccess.Write, FileMode.Append))
                    using (var writer = new StreamWriter(stream))
                        writer.WriteLine(JsonSerializer.Serialize(report));

                    saveAdjustments();
                }
                catch (Exception ex)
                {
                    Logger.Log($"OSU SR: could not save a classification report ({ex.Message})");
                }

                return adjustment;
            }
        }

        /// <summary>
        /// Forgets all adjustments, so skillsets go back to their default minimum purity. The saved reports are kept.
        /// </summary>
        public void ResetAdjustments()
        {
            lock (syncRoot)
            {
                adjustments = new Dictionary<Skillset, double>();

                try
                {
                    saveAdjustments();
                }
                catch (Exception ex)
                {
                    Logger.Log($"OSU SR: could not reset the purity adjustments ({ex.Message})");
                }
            }
        }

        private void saveAdjustments()
        {
            using (var stream = storage.CreateFileSafely(adjustments_file))
                JsonSerializer.Serialize(stream, adjustments.ToDictionary(p => p.Key.ToString(), p => p.Value));
        }

        private void load()
        {
            try
            {
                if (storage.Exists(adjustments_file))
                {
                    using (var stream = storage.GetStream(adjustments_file))
                    {
                        var saved = JsonSerializer.Deserialize<Dictionary<string, double>>(stream) ?? new Dictionary<string, double>();

                        foreach (var (name, value) in saved)
                        {
                            if (Enum.TryParse(name, out Skillset skillset))
                                adjustments[skillset] = Math.Clamp(value, 0, MAXIMUM_ADJUSTMENT);
                        }
                    }
                }

                if (storage.Exists(feedback_file))
                {
                    using (var stream = storage.GetStream(feedback_file))
                    using (var reader = new StreamReader(stream))
                    {
                        while (reader.ReadLine() != null)
                            reportCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not read saved classification reports ({ex.Message})");
            }
        }

        private class Report
        {
            public DateTimeOffset Time { get; set; }
            public string Skillset { get; set; } = string.Empty;
            public string Beatmap { get; set; } = string.Empty;
            public string BeatmapMd5 { get; set; } = string.Empty;
            public double StartTime { get; set; }
            public double EndTime { get; set; }
            public double Purity { get; set; }
            public double Score { get; set; }
            public double? SegmentStars { get; set; }
            public double NewAdjustment { get; set; }
        }
    }
}
