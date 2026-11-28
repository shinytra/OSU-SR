// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using osu.Game.OsuSR.Screens;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Builds the text shown at the end of a training session: totals, then one line per skillset played.
    /// </summary>
    public static class SessionSummary
    {
        /// <summary>
        /// Lines describing the session. Empty when nothing was played.
        /// The weakest skillset (lowest mean accuracy) is flagged when at least two skillsets were played.
        /// </summary>
        public static IReadOnlyList<string> Build(IReadOnlyList<SegmentResult> results)
        {
            if (results.Count == 0)
                return [];

            var lines = new List<string>
            {
                $"{results.Count} passage(s), précision moyenne {results.Average(r => r.Accuracy):P2}, meilleur combo {results.Max(r => r.MaxCombo)}x"
            };

            var groups = results.GroupBy(r => r.Segment.Skillset).ToList();

            foreach (var group in groups)
            {
                double? stars = group.Select(r => r.Segment.StarRating).Average();
                string starText = stars != null ? $", {stars:0.0}★ en moyenne" : string.Empty;
                lines.Add($"{label(group.Key)} : {group.Count()} passage(s), {group.Average(r => r.Accuracy):P2}{starText}");
            }

            if (groups.Count > 1)
            {
                var weakest = groups.OrderBy(g => g.Average(r => r.Accuracy)).First();
                lines.Add($"À retravailler : {label(weakest.Key)}");
            }

            return lines;
        }

        private static string label(Skillset skillset)
        {
            var attribute = typeof(Skillset).GetField(skillset.ToString())?.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault() as DescriptionAttribute;
            return attribute?.Description ?? skillset.ToString();
        }
    }
}
