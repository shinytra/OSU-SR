// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Switches for OSU SR specific behaviour.
    /// Upstream features are hidden rather than deleted so that merging new osu!lazer releases stays easy.
    /// </summary>
    public static class OsuSRFeatures
    {
        /// <summary>
        /// When enabled, the main menu only offers playing local beatmaps (Play goes straight to song select),
        /// and hides multiplayer, playlists, daily challenge, the editors and beatmap browsing.
        /// </summary>
        public static bool MinimalMenu { get; set; } = true;
    }
}
