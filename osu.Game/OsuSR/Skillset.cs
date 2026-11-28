// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// A skill that an OSU SR session can train.
    /// Each one is detected from note patterns (see PatternClassifier and SkillsetSegmentFinder in the osu! ruleset).
    /// </summary>
    public enum Skillset
    {
        /// <summary>1/2 jumps that change direction (snap aim).</summary>
        [Description("Jumps")]
        Jumps,

        /// <summary>Spaced moves that keep their direction: flowing jumps, curved spaced streams.</summary>
        [Description("Flow aim")]
        FlowAim,

        /// <summary>Streams of 9+ notes.</summary>
        [Description("Streams")]
        Streams,

        /// <summary>Streams with large spacing.</summary>
        [Description("Spaced streams")]
        SpacedStreams,

        /// <summary>Bursts and fast 1/2 that need alternating.</summary>
        [Description("Bursts / Alt")]
        Alt,

        /// <summary>Changing rhythms and odd snaps.</summary>
        [Description("Finger control")]
        FingerControl,

        /// <summary>Sliders with varied velocity or repeats, rhythm changes around sliders.</summary>
        [Description("Tech")]
        Tech,

        /// <summary>Fast short sliders, kicksliders, buzz sliders.</summary>
        [Description("Sliders")]
        Sliders,

        /// <summary>Low approach rate with many objects on screen.</summary>
        [Description("Low AR")]
        LowAr,

        /// <summary>High approach rate.</summary>
        [Description("High AR")]
        HighAr,

        /// <summary>Very long streams.</summary>
        [Description("Stamina")]
        Stamina,

        /// <summary>Overlaps and pile-ups: telling circles apart (the reading skill, not tied to aim or approach rate).</summary>
        [Description("Reading (overlaps)")]
        Reading,

        /// <summary>Parts that mix jumps and streams.</summary>
        [Description("Jumps + streams")]
        JumpsStreams,

        /// <summary>Parts that mix flow aim and streams.</summary>
        [Description("Flow + streams")]
        FlowStreams,

        /// <summary>Parts that mix tech and fast sliders.</summary>
        [Description("Tech + sliders")]
        TechSliders,
    }
}
