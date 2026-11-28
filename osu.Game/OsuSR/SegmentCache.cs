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
        public const int VERSION = 4;

        private const string directory = "osu-sr";
        private const string cache_file = "segment-cache.json";

        private readonly Storage storage;
        private readonly State state;

        private readonly string signature;

        /// <param name="storage">Where the cache file is kept.</param>
        /// <param name="signature">Identifies the skillset weights in force (<see cref="SkillsetWeights.Signature"/>): other weights give other results.</param>
        public SegmentCache(Storage storage, string signature = "")
        {
            this.signature = signature;
            this.storage = storage.GetStorageForDirectory(directory);
            state = new State();
            load();
        }

        private SegmentCache(SegmentCache other, string signature)
        {
            this.signature = signature;
            storage = other.storage;
            state = other.state;
        }

        /// <summary>
        /// The same cache for other circumstances (other mods, other weights): what is kept for one is not seen by the other.
        /// Both share what is saved and loaded, and can be used at the same time.
        /// </summary>
        public SegmentCache WithSignature(string newSignature) => new SegmentCache(this, newSignature);

        private class State
        {
            public readonly object SyncRoot = new object();
            public readonly Dictionary<string, MapSegment[]> Entries = new Dictionary<string, MapSegment[]>();
            public bool Dirty;
        }

        public int Count
        {
            get
            {
                lock (state.SyncRoot)
                    return state.Entries.Count;
            }
        }

        /// <param name="mapHash">The hash of the map's file, which changes with its content.</param>
        /// <param name="skillset">The skillset the segments were found for.</param>
        /// <param name="purityAdjustment">The extra purity requirement that was in force.</param>
        /// <param name="segments">What was remembered, if anything.</param>
        public bool TryGet(string mapHash, Skillset skillset, double purityAdjustment, out IReadOnlyList<MapSegment> segments)
        {
            lock (state.SyncRoot)
            {
                if (state.Entries.TryGetValue(key(mapHash, skillset, purityAdjustment), out var found))
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
            lock (state.SyncRoot)
            {
                state.Entries[key(mapHash, skillset, purityAdjustment)] = segments.ToArray();
                state.Dirty = true;
            }
        }

        /// <summary>Writes what was added since the last time to disk.</summary>
        public void Save()
        {
            Dictionary<string, MapSegment[]> snapshot;

            lock (state.SyncRoot)
            {
                if (!state.Dirty)
                    return;

                snapshot = new Dictionary<string, MapSegment[]>(state.Entries);
                state.Dirty = false;
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

        private string key(string mapHash, Skillset skillset, double purityAdjustment) => $"{VERSION}|{mapHash}|{(int)skillset}|{purityAdjustment:0.00}|{signature}";

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
                    state.Entries[entry.Key] = entry.Value;
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not read the segment cache ({ex.Message})");
            }
        }
    }
}
