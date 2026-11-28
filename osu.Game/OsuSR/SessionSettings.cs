// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Online.API;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// The setup of a training session (skillsets, star range, mods, series options...), kept in <c>osu-sr/session-settings.json</c>
    /// so the next session, and the next time the game starts, begins with the same.
    /// </summary>
    public class SessionSettings
    {
        private const string directory = "osu-sr";
        private const string file = "session-settings.json";

        public List<string> Skillsets { get; set; } = new List<string> { nameof(Skillset.Jumps) };
        public double MinimumStars { get; set; } = 4;
        public double MaximumStars { get; set; } = 6;
        public List<APIMod> Mods { get; set; } = new List<APIMod>();
        public bool PreferTaggedMaps { get; set; } = true;
        public bool MatchBpm { get; set; } = true;
        public bool SeriesMode { get; set; }
        public bool InfiniteMode { get; set; }
        public bool IncludeUnknownStatus { get; set; }
        public bool AdaptiveDifficulty { get; set; }
        public int SeriesLength { get; set; } = 5;

        public IEnumerable<Skillset> ParsedSkillsets => Skillsets.Select(s => (Ok: Enum.TryParse<Skillset>(s, out var parsed), Skillset: parsed)).Where(p => p.Ok).Select(p => p.Skillset);

        public static SessionSettings Load(Storage storage)
        {
            try
            {
                storage = storage.GetStorageForDirectory(directory);

                if (!storage.Exists(file))
                    return new SessionSettings();

                using var stream = storage.GetStream(file);
                using var reader = new System.IO.StreamReader(stream);
                return JsonConvert.DeserializeObject<SessionSettings>(reader.ReadToEnd()) ?? new SessionSettings();
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not read the session settings ({ex.Message}), using the defaults");
                return new SessionSettings();
            }
        }

        public void Save(Storage storage)
        {
            try
            {
                storage = storage.GetStorageForDirectory(directory);

                using var output = storage.CreateFileSafely(file);
                using var writer = new System.IO.StreamWriter(output);
                writer.Write(JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not save the session settings ({ex.Message})");
            }
        }
    }
}
