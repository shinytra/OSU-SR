// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// The beatmap of a whole series: all hit objects of its parts on one timeline (see <see cref="SeriesTimeline"/>).
    /// Its track is silent (virtual): the music of each part is played and crossfaded by <see cref="Screens.SeriesPlayer"/>.
    /// Look and skin come from the first part.
    /// </summary>
    public class SeriesWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap composite;
        private readonly WorkingBeatmap first;
        private readonly AudioManager audio;
        private readonly double length;

        public SeriesWorkingBeatmap(IReadOnlyList<SeriesPart> parts, IBeatmap composite, double length, AudioManager audio)
            : base(parts[0].Beatmap.BeatmapInfo, audio)
        {
            this.composite = composite;
            this.length = length;
            this.audio = audio;
            first = parts[0].Beatmap;
        }

        protected override IBeatmap GetBeatmap() => composite;

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token) => composite;

        public override Texture GetBackground() => first.GetBackground();

        protected override Track GetBeatmapTrack() => audio.Tracks.GetVirtual(length);

        protected internal override ISkin GetSkin() => first.Skin;

        public override Stream GetStream(string storagePath) => first.GetStream(storagePath);
    }
}
