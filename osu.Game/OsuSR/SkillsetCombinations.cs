// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// How skillsets combine. A part can have several skillsets at once (see <see cref="MapSegment.Labels"/>);
    /// a session plays a part only if all of the part's skillsets are among the ones the player picked.
    /// </summary>
    public static class SkillsetCombinations
    {
        /// <summary>
        /// The skillsets that can be labels of a part: note patterns that can sit next to each other in one part.
        /// The others (approach rate, stamina, reading) describe how a part is played, not what its notes are, so they never keep a part out.
        /// </summary>
        public static readonly IReadOnlyList<Skillset> LABEL_SKILLSETS = new[]
        {
            Skillset.Jumps, Skillset.FlowAim, Skillset.Streams, Skillset.SpacedStreams, Skillset.Alt, Skillset.FingerControl, Skillset.Tech, Skillset.Sliders,
        };

        /// <summary>The two skillsets that a combined skillset (found only when both are picked) is made of, or null for a regular skillset.</summary>
        public static (Skillset First, Skillset Second)? ComponentsOf(Skillset skillset) => skillset switch
        {
            Skillset.JumpsStreams => (Skillset.Jumps, Skillset.Streams),
            Skillset.FlowStreams => (Skillset.FlowAim, Skillset.Streams),
            Skillset.TechSliders => (Skillset.Tech, Skillset.Sliders),
            _ => null,
        };

        public static bool IsCombined(Skillset skillset) => ComponentsOf(skillset) != null;

        /// <summary>The skillsets to look for when the player picked these: the picked ones, and every combination whose two parts were both picked.</summary>
        public static Skillset[] SearchedFor(IReadOnlyCollection<Skillset> picked)
        {
            var all = Allowed(picked);
            return picked.Where(s => !IsCombined(s))
                         .Concat(System.Enum.GetValues<Skillset>().Where(s => ComponentsOf(s) is { } c && all.Contains(c.First) && all.Contains(c.Second)))
                         .Distinct()
                         .ToArray();
        }

        /// <summary>The skillsets that parts may have: the picked ones, and the two parts of a picked combination.</summary>
        public static HashSet<Skillset> Allowed(IEnumerable<Skillset> picked)
        {
            var allowed = new HashSet<Skillset>();

            foreach (var s in picked)
            {
                if (ComponentsOf(s) is { } c)
                {
                    allowed.Add(c.First);
                    allowed.Add(c.Second);
                }
                else
                    allowed.Add(s);
            }

            return allowed;
        }

        /// <summary>Whether a part can be played when the player picked <paramref name="allowed"/> (see <see cref="Allowed"/>).</summary>
        public static bool IsEligible(MapSegment segment, IReadOnlySet<Skillset> allowed)
            => !LABEL_SKILLSETS.Contains(segment.Skillset) && !IsCombined(segment.Skillset) || segment.Labels.All(allowed.Contains);
    }
}
