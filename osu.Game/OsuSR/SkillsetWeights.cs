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
    /// Multipliers that make the star ratings of different skillsets comparable. Lazer's star rating undervalues some skills
    /// (see <see cref="DEFAULTS"/>), so before comparing "how hard are the jumps" with "how hard are the streams" of a part, each is multiplied by its weight.
    /// The weights are kept in <c>osu-sr/skillset-weights.json</c> (created with the defaults the first time), where they can be edited.
    /// Changing them makes the analysis of the library run again, since the parts' skillsets depend on them.
    /// </summary>
    public class SkillsetWeights
    {
        private const string directory = "osu-sr";
        private const string file = "skillset-weights.json";

        /// <summary>
        /// Starting values. They are estimates, not measurements: 1 is the scale of aim in lazer's star rating (jumps, flow aim, sliders, high AR);
        /// the others are a little above 1 where lazer's rating tends to undervalue the skill (long streams and stamina, finger control and tech, low AR).
        /// Tune them from what feels wrong while playing (the F7 reports show which parts were misjudged).
        /// </summary>
        public static readonly IReadOnlyDictionary<Skillset, double> DEFAULTS = new Dictionary<Skillset, double>
        {
            [Skillset.Jumps] = 1.0,
            [Skillset.FlowAim] = 1.0,
            [Skillset.Streams] = 1.1,
            [Skillset.SpacedStreams] = 1.05,
            [Skillset.Alt] = 1.0,
            [Skillset.FingerControl] = 1.1,
            [Skillset.Tech] = 1.1,
            [Skillset.Sliders] = 1.0,
            [Skillset.LowAr] = 1.1,
            [Skillset.HighAr] = 1.0,
            [Skillset.Stamina] = 1.15,
            [Skillset.Reading] = 1.0,
        };

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };

        private readonly Dictionary<Skillset, double> values;

        public SkillsetWeights(IReadOnlyDictionary<Skillset, double>? overrides = null)
        {
            values = new Dictionary<Skillset, double>(DEFAULTS);

            if (overrides == null)
                return;

            foreach (var (skillset, value) in overrides)
            {
                if (value > 0.1 && value < 10)
                    values[skillset] = value;
            }
        }

        public double this[Skillset skillset] => values.GetValueOrDefault(skillset, 1);

        /// <summary>Identifies these values, so what was analysed with other weights isn't reused.</summary>
        public string Signature => string.Join(",", values.OrderBy(p => p.Key).Select(p => p.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Reads the weights file (writing it with the defaults if it doesn't exist).</summary>
        public static SkillsetWeights Load(Storage storage)
        {
            storage = storage.GetStorageForDirectory(directory);

            try
            {
                if (!storage.Exists(file))
                {
                    using var output = storage.CreateFileSafely(file);
                    JsonSerializer.Serialize(output, DEFAULTS.ToDictionary(p => p.Key.ToString(), p => p.Value), options);
                    return new SkillsetWeights();
                }

                using var stream = storage.GetStream(file);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, double>>(stream);

                return new SkillsetWeights(loaded?.Where(p => Enum.TryParse<Skillset>(p.Key, out _)).ToDictionary(p => Enum.Parse<Skillset>(p.Key), p => p.Value));
            }
            catch (Exception ex)
            {
                Logger.Log($"OSU SR: could not read the skillset weights ({ex.Message}), using the defaults");
                return new SkillsetWeights();
            }
        }
    }
}
