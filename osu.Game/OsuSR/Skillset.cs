// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.OsuSR
{
    /// <summary>
    /// A skill that an OSU SR session can train.
    /// Each one maps to a skill of the osu!standard difficulty calculator (see SkillsetSegmentFinder in the osu! ruleset).
    /// </summary>
    public enum Skillset
    {
        /// <summary>Jumps and cursor movement (the calculator's aim skill, sliders included).</summary>
        Aim,

        /// <summary>Tapping speed: streams, bursts, fast doubles (the calculator's speed skill).</summary>
        Speed,

        /// <summary>Reading dense or low-approach patterns (the calculator's reading skill).</summary>
        Reading,
    }
}
