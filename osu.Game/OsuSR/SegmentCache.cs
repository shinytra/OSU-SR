// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Remembers the segments found in each map, kept between sessions, so a map is only analysed once
    /// (until its content, the skillset's purity requirement or the detection itself changes).
    /// Analysing a map takes a while, which is what made finding the next part of a series slow.
    /// </summary>
    public class SegmentCache
    {
        /// <summary>Changes whenever the detection changes, which makes everything saved before it obsolete.</summary>
        public const int VERSION = 1;

        private const string directory = "osu-sr";
        private const string cache_file = "segment-cache.json";

        private readonly Storage storage;
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, MapSegment[]> entries = new Dictionary<string, MapSegment[]>();
        private bool dirty;

        public SegmentCache(Storage storage)
        {
            this.storage = storage.GetStorageForDirectory(directory);
            load();
        }

        public int Count
        {
            get
            {
                lock (syncRoot)
                    return entries.Count;
            }
        }

        /// <param name="mapHash">The hash of the map's file, which changes with its content.</param>
        /// <param name="skillset">The skillset the segments were found for.</param>
        /// <param name="purityAdjustment">The extra purity requirement that was in force.</param>
        /// <param name="segments">What was remembered, if anything.</param>
        public bool TryGet(string mapHash, Skillset skillset, double purityAdjustment, out IReadOnlyList<MapSegment> segments)
        {
            lock (syncRoot)
            {
                if (entries.TryGetValue(key(mapHash, skillset, purityAdjustment), out var found))
                {
                    segments = found;
                    return true;
                }
            }

            segments = Array.Empty<MapSegment>();
            return false;
        }

        /// <summary>Remembers the segments of a map for a skillset (none is a result too).</summary>
        public void Set(string mapHash, Skillset skillset, double purityAdjustment, IEnumerable<MapSegment> segments)
        {
            lock (syncRoot)
            {
                entries[key(mapHash, skillset, purityAdjustment)] = segments.ToArray();
                dirty = true;
            }
        }

        /// <summary>Writes what was added since the last time to disk.</summary>
        public void Save()
        {
            Dictionary<string, MapSegment[]> snapshot;

            lock (syncRoot)
            {
                if (!dirty)
                    return;

                snapshot = new Dictionary<string, MapSegment[]>(entries);
                dirty = false;
            }

            try
            {
                using var stream = storage.CreateFileSafely(cache_file);
                JsonSerializer.Serialize(stream, snapshot);
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not save the segment cache ({ex.Message})");
            }
        }

        private static string key(string mapHash, Skillset skillset, double purityAdjustment) => $"{VERSION}|{mapHash}|{(int)skillset}|{purityAdjustment:0.00}";

        private void load()
        {
            try
            {
                if (!storage.Exists(cache_file))
                    return;

                using var stream = storage.GetStream(cache_file);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, MapSegment[]>>(stream);

                if (loaded == null)
                    return;

                // Entries of older versions are left behind, not carried over.
                string prefix = $"{VERSION}|";

                foreach (var entry in loaded.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)))
                    entries[entry.Key] = entry.Value;
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not read the segment cache ({ex.Message})");
            }
        }
    }
}
