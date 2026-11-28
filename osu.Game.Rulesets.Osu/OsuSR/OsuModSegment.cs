// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.OsuSR
{
    /// <summary>
    /// OSU SR: keeps only the hit objects of one segment of the map, so gameplay ends right after it.
    /// Not listed in the mod select overlay; it is applied by training sessions.
    /// </summary>
    public class OsuModSegment : Mod, IApplicableToBeatmap
    {
        public override string Name => "Segment";
        public override string Acronym => "SEG";
        public override LocalisableString Description => "Only play one part of the map.";
        public override ModType Type => ModType.System;
        public override bool UserPlayable => false;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;

        // Settings are bindables with [SettingSource] so that Mod.DeepClone() (used by Player) copies them.
        [SettingSource("Start time", "Hit objects starting before this time (ms) are removed.")]
        public BindableDouble FirstObjectTime { get; } = new BindableDouble { MinValue = 0, MaxValue = double.MaxValue };

        [SettingSource("End time", "Hit objects starting after this time (ms) are removed.")]
        public BindableDouble EndTime { get; } = new BindableDouble(double.MaxValue) { MinValue = 0, MaxValue = double.MaxValue };

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            var osuBeatmap = (Beatmap<OsuHitObject>)beatmap;

            osuBeatmap.HitObjects = osuBeatmap.HitObjects
                                              .Where(h => h.StartTime >= FirstObjectTime.Value && h.GetEndTime() <= EndTime.Value)
                                              .ToList();

            // Breaks outside the segment would only confuse the break overlay.
            osuBeatmap.Breaks.RemoveAll(b => b.EndTime <= FirstObjectTime.Value || b.StartTime >= EndTime.Value);
        }
    }
}
