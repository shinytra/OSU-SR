// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Links osu! user tags (the community's tags on a beatmap, kept in the game's local metadata cache, so no network and no login)
    /// to <see cref="Skillset"/>s.
    /// </summary>
    public static class SkillsetTags
    {
        /// <summary>How much more likely a map tagged for a selected skillset is to be picked, compared to an untagged map.</summary>
        public const double TAGGED_WEIGHT = 4;

        /// <summary>
        /// Words that identify a skillset in a user tag (case-insensitive, matched anywhere in the tag).
        /// Tags are free-form, so this is a loose match on purpose.
        /// </summary>
        public static IReadOnlyList<string> KeywordsFor(Skillset skillset) => skillset switch
        {
            Skillset.Jumps => new[] { "jump", "snap" },
            Skillset.FlowAim => new[] { "flow" },
            Skillset.Streams => new[] { "stream" },
            Skillset.SpacedStreams => new[] { "spaced" },
            Skillset.Alt => new[] { "alt", "burst" },
            Skillset.FingerControl => new[] { "finger control" },
            Skillset.Tech => new[] { "tech" },
            Skillset.Sliders => new[] { "slider" },
            Skillset.LowAr => new[] { "low ar", "reading" },
            Skillset.HighAr => new[] { "high ar" },
            Skillset.Stamina => new[] { "stamina", "deathstream" },
            _ => throw new ArgumentOutOfRangeException(nameof(skillset), skillset, null),
        };

        /// <summary>The user tags that match a skillset.</summary>
        public static IEnumerable<string> MatchingTags(Skillset skillset, IEnumerable<string> userTags)
            => userTags.Where(tag => KeywordsFor(skillset).Any(keyword => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        /// <summary>Whether any user tag matches the skillset.</summary>
        public static bool IsTagged(Skillset skillset, IEnumerable<string> userTags) => MatchingTags(skillset, userTags).Any();

        /// <summary>
        /// The weight of a map when picking one at random for the selected skillsets:
        /// <see cref="TAGGED_WEIGHT"/> when its tags match any of them, 1 otherwise (including when it has no tags at all).
        /// Untagged maps stay possible, since many maps have no tags and the detection works on notes anyway.
        /// </summary>
        public static double Weight(IEnumerable<Skillset> selected, IReadOnlyCollection<string> userTags)
            => userTags.Count > 0 && selected.Any(s => IsTagged(s, userTags)) ? TAGGED_WEIGHT : 1;
    }
}
