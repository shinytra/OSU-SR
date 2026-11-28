// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osuTK;
using Realms;

namespace osu.Game.OsuSR.Screens
{
    /// <summary>
    /// A continuous training session: plays the hardest segment for one <see cref="Skillset"/> of random local maps
    /// within a star rating range, one after the other, with a short countdown in between.
    /// </summary>
    public partial class TrainingSession : OsuScreen
    {
        /// <summary>How long the countdown between two segments lasts (ms). The next map's music fades in meanwhile.</summary>
        public const double COUNTDOWN_DURATION = 3000;

        /// <summary>How many maps to try before giving up on finding a playable segment.</summary>
        /// <remarks>Some skillsets only exist in a few maps, so this is generous.</remarks>
        private const int max_attempts = 40;

        /// <summary>How many of the last maps played are avoided in a series, so it doesn't come back to the same few.</summary>
        private const int recent_maps_to_avoid = 6;

        /// <summary>How much of the build-up before the hard part (the pre-drop) a series part includes at most (ms).</summary>
        public const double PRE_DROP = 12_000;

        /// <summary>How much of what follows the hard part (the post-drop) a series part includes at most (ms).</summary>
        public const double POST_DROP = 5_000;

        /// <summary>A tempo-matched pool needs at least this many maps, otherwise the tolerance is widened.</summary>
        private const int min_bpm_pool_size = 5;

        /// <summary>
        /// The star range applies to the played segment, which is usually easier than its map.
        /// Maps are pre-selected with a wider range, then each candidate segment's own star rating decides.
        /// </summary>
        private const double map_stars_lower_factor = 0.6;

        private const double map_stars_upper_factor = 1.8;

        /// <summary>The skillsets picked last, so the next session starts with them.</summary>
        private static Skillset[] lastSkillsets = { Skillset.Jumps };

        /// <summary>
        /// Whether each skillset is part of the session. With several, segments alternate between them.
        /// </summary>
        public readonly IReadOnlyDictionary<Skillset, BindableBool> SkillsetEnabled = Enum.GetValues<Skillset>().ToDictionary(s => s, _ => new BindableBool());

        /// <summary>The skillsets currently part of the session, in menu order.</summary>
        public Skillset[] SelectedSkillsets => SkillsetEnabled.Where(p => p.Value.Value).Select(p => p.Key).ToArray();

        public readonly BindableDouble MinimumStars = new BindableDouble(4) { MinValue = 0, MaxValue = 12, Precision = 0.1 };
        public readonly BindableDouble MaximumStars = new BindableDouble(6) { MinValue = 0, MaxValue = 12, Precision = 0.1 };

        /// <summary>Whether maps that the community tagged for the selected skillsets are picked more often (from the local metadata cache, no network).</summary>
        public readonly BindableBool PreferTaggedMaps = new BindableBool(true);

        /// <summary>
        /// Series mode: a fixed number of segments played back to back, with no countdown and no screen in between.
        /// The next segment is found while the current one is played.
        /// </summary>
        public readonly BindableBool SeriesMode = new BindableBool();

        /// <summary>Series mode that never ends (until the player leaves): the next segments are found and loaded in the background while playing.</summary>
        public readonly BindableBool InfiniteMode = new BindableBool();

        /// <summary>
        /// Also plays maps whose online status is unknown (none). Maps imported without an online lookup have no status even if they are ranked,
        /// so with a library like that the ranked/loved filter would find nothing.
        /// </summary>
        public readonly BindableBool IncludeUnknownStatus = new BindableBool();

        /// <summary>In a series: makes the next parts easier when the player struggles and harder when they play them very well.</summary>
        public readonly BindableBool AdaptiveDifficulty = new BindableBool();

        /// <summary>How many segments a series lasts (unless it is infinite).</summary>
        public readonly BindableInt SeriesLength = new BindableInt(5) { MinValue = 1, MaxValue = 30 };

        /// <summary>Whether the next map's tempo has to be close to the current one's, so the beat doesn't jump between segments.</summary>
        public readonly BindableBool MatchBpm = new BindableBool(true);

        /// <summary>Results of the segments played in this session, oldest first.</summary>
        public IReadOnlyList<SegmentResult> Results => results;

        private readonly List<SegmentResult> results = new List<SegmentResult>();

        [Resolved]
        private Storage storage { get; set; } = null!;

        /// <summary>"Wrongly classified" reports and the purity adjustments they cause.</summary>
        public SegmentFeedbackStore Feedback { get; private set; } = null!;

        private SegmentCache segmentCache = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private MusicController musicController { get; set; } = null!;

        private Container setupPanel = null!;
        private OsuSpriteText titleText = null!;
        private OsuSpriteText feedbackSummaryText = null!;
        private RoundedButton startButton = null!;
        private OsuSpriteText statusText = null!;
        private OsuSpriteText lastResultText = null!;
        private OsuSpriteText countdownText = null!;

        private IReadOnlyList<Mod> previousMods = Array.Empty<Mod>();
        private RulesetInfo osuRuleset = null!;
        private readonly ISkillsetSegmentFinder? finderOverride;
        private ISkillsetSegmentFinder finder = null!;

        private CancellationTokenSource? preparationCancellation;
        private SegmentPlayer? currentPlayer;

        /// <summary>Skillsets of the segments started so far this session, oldest first (the one playing included).</summary>
        private readonly List<Skillset> startedSkillsets = new List<Skillset>();

        private Guid? currentBeatmapId;
        private double? currentBpm;

        private SeriesPlayer? currentSeriesPlayer;
        private bool running;

        /// <param name="skillsets">The skillsets to train. Defaults to the ones picked last. They can be changed on the setup panel.</param>
        /// <param name="finder">Overrides how segments are found (for tests). Defaults to the osu! ruleset's finder.</param>
        public TrainingSession(IEnumerable<Skillset>? skillsets = null, ISkillsetSegmentFinder? finder = null)
        {
            foreach (var skillset in skillsets ?? lastSkillsets)
                SkillsetEnabled[skillset].Value = true;

            finderOverride = finder;
        }

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenBeatmap(Beatmap.Value);

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            osuRuleset = rulesets.GetRuleset("osu") ?? throw new InvalidOperationException("The osu! ruleset is not available.");
            Feedback = new SegmentFeedbackStore(storage);
            segmentCache = new SegmentCache(storage);
            finder = finderOverride ?? ((IHasSkillsetSegments)osuRuleset.CreateInstance()).CreateSkillsetSegmentFinder();

            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 480,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 15),
                    Children = new Drawable[]
                    {
                        titleText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Default.With(size: 36, weight: FontWeight.Bold),
                        },
                        setupPanel = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Masking = true,
                            CornerRadius = 10,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colours.Gray3,
                                    Alpha = 0.8f,
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Padding = new MarginPadding(15),
                                    Spacing = new Vector2(0, 10),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = "Skillsets (plusieurs = passages mélangés)",
                                            Font = OsuFont.Default.With(size: 16, weight: FontWeight.SemiBold),
                                            Margin = new MarginPadding { Horizontal = 20 },
                                        },
                                        new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            ColumnDimensions = new[] { new Dimension(), new Dimension() },
                                            RowDimensions = Enumerable.Range(0, (SkillsetEnabled.Count + 1) / 2).Select(_ => new Dimension(GridSizeMode.AutoSize)).ToArray(),
                                            // Every row needs both cells, so an odd count leaves an empty one.
                                            Content = SkillsetEnabled.Chunk(2).Select(row => row.Select(p => (Drawable)new SettingsCheckbox
                                            {
                                                LabelText = p.Key.GetDescription(),
                                                Current = p.Value,
                                            }).Append(Empty()).Take(2).ToArray()).ToArray(),
                                        },
                                        new SettingsSlider<double>
                                        {
                                            LabelText = "Étoiles minimum (du passage)",
                                            Current = MinimumStars,
                                            KeyboardStep = 0.1f,
                                        },
                                        new SettingsSlider<double>
                                        {
                                            LabelText = "Étoiles maximum (du passage)",
                                            Current = MaximumStars,
                                            KeyboardStep = 0.1f,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Préférer les maps taguées par la communauté",
                                            Current = PreferTaggedMaps,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Accorder les BPM entre passages",
                                            Current = MatchBpm,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Mode série (aucune pause entre les passages)",
                                            Current = SeriesMode,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Série infinie (jusqu'à ce que tu quittes)",
                                            Current = InfiniteMode,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Inclure les maps sans statut en ligne (si tes maps ne sont pas reconnues ranked/loved)",
                                            Current = IncludeUnknownStatus,
                                        },
                                        new SettingsCheckbox
                                        {
                                            LabelText = "Difficulté adaptative (selon ta précision)",
                                            Current = AdaptiveDifficulty,
                                        },
                                        new SettingsSlider<int>
                                        {
                                            LabelText = "Nombre de passages de la série",
                                            Current = SeriesLength,
                                        },
                                        feedbackSummaryText = new OsuSpriteText
                                        {
                                            Font = OsuFont.Default.With(size: 14),
                                            Colour = colours.Yellow,
                                        },
                                        new RoundedButton
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            Text = "Réinitialiser les seuils",
                                            Action = () =>
                                            {
                                                Feedback.ResetAdjustments();
                                                updateFeedbackSummary();
                                            },
                                        },
                                        startButton = new RoundedButton
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            Text = "Commencer",
                                            Action = startSession,
                                        },
                                    }
                                },
                            }
                        },
                        countdownText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Default.With(size: 72, weight: FontWeight.Bold),
                            Alpha = 0,
                        },
                        statusText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Default.With(size: 18),
                        },
                        lastResultText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Default.With(size: 18),
                            Colour = colours.Yellow,
                        },
                    }
                },
            };

            foreach (var enabled in SkillsetEnabled.Values)
                enabled.BindValueChanged(_ => updateSkillsets());
            updateSkillsets();
            updateFeedbackSummary();

            // Keep the range valid whichever slider is moved.
            MinimumStars.BindValueChanged(v =>
            {
                if (MaximumStars.Value < v.NewValue) MaximumStars.Value = v.NewValue;
            });
            MaximumStars.BindValueChanged(v =>
            {
                if (MinimumStars.Value > v.NewValue) MinimumStars.Value = v.NewValue;
            });
        }

        private void updateFeedbackSummary()
        {
            var adjustments = Feedback.PurityAdjustments.Where(p => p.Value > 0).ToList();

            feedbackSummaryText.Text = $"Pendant un passage, {SegmentPlayer.MISCLASSIFIED_KEY} = « mal classé » ({Feedback.ReportCount} retour(s))"
                                       + (adjustments.Count == 0 ? string.Empty : "\nSeuils plus stricts : " + string.Join(", ", adjustments.Select(p => $"{p.Key.GetDescription()} +{p.Value:P0}")));
        }

        private void updateSkillsets()
        {
            var selected = SelectedSkillsets;

            if (selected.Length > 0)
                lastSkillsets = selected;

            titleText.Text = selected.Length == 0 ? "Session continue" : $"Session continue : {string.Join(" + ", selected.Select(s => s.GetDescription()))}";
            startButton.Enabled.Value = selected.Length > 0;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            previousMods = Mods.Value;
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            var seriesPlayer = currentSeriesPlayer;
            currentSeriesPlayer = null;

            if (seriesPlayer != null)
            {
                results.AddRange(seriesPlayer.Results);
                updateFeedbackSummary();

                stopSession(seriesPlayer.Results.Count > 0
                    ? $"Série terminée : {results.Count} passage(s) joué(s)."
                    : null);
                return;
            }

            var player = currentPlayer;
            currentPlayer = null;

            if (!running || player == null)
                return;

            if (player.Result == null)
            {
                // The player quit the segment: end the session and show the setup again.
                stopSession();
                return;
            }

            updateFeedbackSummary();
            results.Add(player.Result);
            lastResultText.Text = $"Dernier passage : {player.Result.Accuracy:P2}, combo {player.Result.MaxCombo}x, score {player.Result.TotalScore:N0}  ·  {results.Count} passage(s), précision moyenne {results.Average(r => r.Accuracy):P2}";

            prepareNextSegment();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            saveSegmentCache();
            preparationCancellation?.Cancel();
            restoreVolume();
            restoreMods();
            return base.OnExiting(e);
        }

        /// <summary>
        /// Starts the session with the current star range (same as pressing the start button).
        /// </summary>
        public void StartSession() => startSession();

        private void startSession()
        {
            if (running || SelectedSkillsets.Length == 0)
                return;

            running = true;
            results.Clear();
            startedSkillsets.Clear();
            currentBeatmapId = null;
            currentBpm = null;
            lastResultText.Text = string.Empty;
            setupPanel.FadeOut(200);

            if (SeriesMode.Value)
                startSeries();
            else
                prepareNextSegment();
        }

        private void saveSegmentCache() => Task.Run(segmentCache.Save);

        private void stopSession(string? summary = null)
        {
            saveSegmentCache();
            running = false;
            preparationCancellation?.Cancel();
            restoreMods();
            restoreVolume();
            countdownText.FadeOut(200);
            setupPanel.FadeIn(200);
            updateFeedbackSummary();
            statusText.Text = summary ?? (results.Count > 0 ? "Session terminée." : string.Empty);
        }

        /// <summary>Series fade the music out and in, so the volume has to be put back for the menus.</summary>
        private void restoreVolume() => musicController.CurrentTrack.VolumeTo(1);

        private void restoreMods()
        {
            if (!Mods.Disabled)
                Mods.Value = previousMods;
        }

        private void prepareNextSegment()
        {
            preparationCancellation?.Cancel();
            var cancellation = preparationCancellation = new CancellationTokenSource();

            statusText.Text = "Recherche du prochain passage…";

            var search = searchNextSegment(cancellation.Token);
            search.ContinueWith(_ => Schedule(() => handleSearchResult(search, cancellation)), TaskContinuationOptions.None);
        }

        /// <summary>
        /// Starts looking for the segment that follows the one being started, on a background thread.
        /// </summary>
        private Task<(WorkingBeatmap Beatmap, MapSegment Segment)?> searchNextSegment(CancellationToken cancellationToken)
        {
            double min = MinimumStars.Value;
            double max = MaximumStars.Value;
            var skillsets = SelectedSkillsets;
            bool preferTagged = PreferTaggedMaps.Value;
            double? previousBpm = MatchBpm.Value ? currentBpm : null;
            var excluded = currentBeatmapId ?? results.LastOrDefault()?.Segment.BeatmapID;
            var recent = startedSkillsets.ToArray();
            finder.PurityAdjustments = Feedback.PurityAdjustments;

            var recentMaps = excluded is Guid id ? new[] { id } : Array.Empty<Guid>();

            return Task.Run(() => findNextSegment(loadPool(skillsets, min, max, preferTagged, IncludeUnknownStatus.Value), skillsets, min, max, previousBpm, recentMaps, recent, cancellationToken), cancellationToken);
        }

        private void handleSearchResult(Task<(WorkingBeatmap Beatmap, MapSegment Segment)?> search, CancellationTokenSource cancellation)
        {
            if (cancellation.IsCancellationRequested || !running)
                return;

            if (search.IsFaulted)
            {
                Logger.Error(search.Exception, "OSU SR: failed to prepare the next segment");
                statusText.Text = "Erreur pendant la recherche d'un passage (voir les logs).";
                stopSession();
                return;
            }

            if (search.GetResultSafely() == null)
            {
                statusText.Text = describeEmptyPool();
                stopSession();
                return;
            }

            var next = search.GetResultSafely()!.Value;
            startSegment(next.Beatmap, next.Segment);
        }

        /// <summary>The maps a session may pick from: ranked, approved or loved osu! maps whose star rating can have a segment in range.</summary>
        private List<(Guid ID, double Weight, double BPM)> loadPool(Skillset[] skillsets, double minStars, double maxStars, bool preferTagged, bool includeUnknown)
        {
            string statuses = $"{nameof(BeatmapInfo.Status)} == $3 OR {nameof(BeatmapInfo.Status)} == $4 OR {nameof(BeatmapInfo.Status)} == $5"
                              + (includeUnknown ? $" OR {nameof(BeatmapInfo.Status)} == $6" : string.Empty);

            var pool = realm.Run(r => r.All<BeatmapInfo>()
                                       .Filter($"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0"
                                               + $" AND {nameof(BeatmapInfo.StarRating)} >= $1 AND {nameof(BeatmapInfo.StarRating)} <= $2"
                                               + $" AND ({statuses})"
                                               + $" AND {nameof(BeatmapInfo.Hidden)} == false"
                                               + $" AND {nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false",
                                           osuRuleset.ShortName, minStars * map_stars_lower_factor, maxStars * map_stars_upper_factor,
                                           (int)BeatmapOnlineStatus.Ranked, (int)BeatmapOnlineStatus.Approved, (int)BeatmapOnlineStatus.Loved, (int)BeatmapOnlineStatus.None)
                                       .AsEnumerable()
                                       .Select(b => (b.ID, Weight: preferTagged ? SkillsetTags.Weight(skillsets, b.Metadata.UserTags.ToList()) : 1, b.BPM))
                                       .ToList());

            Logger.Log($"OSU SR: {pool.Count} candidate maps for {minStars:N1}-{maxStars:N1}★ (unknown status {(includeUnknown ? "included" : "excluded")})");
            return pool;
        }

        /// <summary>Why no map can be used, in words: how many osu! maps are in range, and how many of them have no online status.</summary>
        private string describeEmptyPool()
        {
            double min = MinimumStars.Value * map_stars_lower_factor;
            double max = MaximumStars.Value * map_stars_upper_factor;

            var (inRange, unknown) = realm.Run(r =>
            {
                var all = r.All<BeatmapInfo>()
                           .Filter($"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0"
                                   + $" AND {nameof(BeatmapInfo.StarRating)} >= $1 AND {nameof(BeatmapInfo.StarRating)} <= $2"
                                   + $" AND {nameof(BeatmapInfo.Hidden)} == false",
                               osuRuleset.ShortName, min, max);

                return (all.Count(), all.Filter($"{nameof(BeatmapInfo.Status)} == $0", (int)BeatmapOnlineStatus.None).Count());
            });

            string skillsets = string.Join(" / ", SelectedSkillsets.Select(s => s.GetDescription()));

            if (inRange > 0 && !IncludeUnknownStatus.Value)
                return $"Aucun passage {skillsets} trouvé : {inRange} maps dans {MinimumStars.Value:N1}-{MaximumStars.Value:N1}★, dont {unknown} sans statut en ligne. Coche « Inclure les maps sans statut » pour les jouer.";

            return $"Aucun passage {skillsets} trouvé dans les maps entre {MinimumStars.Value:N1} et {MaximumStars.Value:N1} étoiles ({inRange} maps dans cette plage).";
        }

        // candidates: the maps to pick from (see loadPool). recentMaps: maps played lately, which are skipped as long as there are others.
        private (WorkingBeatmap Beatmap, MapSegment Segment)? findNextSegment(
            List<(Guid ID, double Weight, double BPM)> candidates, Skillset[] skillsets, double minStars, double maxStars, double? previousBpm, IReadOnlyCollection<Guid> recentMaps,
            Skillset[] recentSkillsets, CancellationToken cancellationToken)
        {
            var pool = candidates;

            if (pool.Count == 0)
                return null;

            // Keep the tempo: first maps within a few percent of the previous one, then a wider range, then any (the session must never stall).
            if (previousBpm is double bpm)
            {
                foreach (double tolerance in new[] { BpmMatcher.STRICT_TOLERANCE, BpmMatcher.RELAXED_TOLERANCE })
                {
                    var close = pool.Where(p => BpmMatcher.IsClose(bpm, p.BPM, tolerance)).ToList();

                    if (close.Count >= Math.Min(min_bpm_pool_size, pool.Count))
                    {
                        pool = close;
                        break;
                    }
                }
            }

            // With several skillsets, prefer the ones played least recently, so segments alternate between them.
            var preference = skillsets.OrderBy(s => Array.LastIndexOf(recentSkillsets, s)).ThenBy(_ => Random.Shared.Next()).ToArray();

            for (int attempt = 0; attempt < max_attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = pickWeighted(pool);

                if (recentMaps.Contains(id) && pool.Count > recentMaps.Count)
                    continue;

                var info = realm.Run(r => r.Find<BeatmapInfo>(id)?.Detach());

                if (info == null)
                    continue;

                var working = beatmaps.GetWorkingBeatmap(info);

                try
                {
                    var adjustments = finder.PurityAdjustments;
                    string hash = info.MD5Hash;

                    // Maps analysed before (in this or an earlier session) are not analysed again.
                    var missing = skillsets.Where(s => !segmentCache.TryGet(hash, s, adjustments.GetValueOrDefault(s), out _)).ToArray();

                    if (missing.Length > 0)
                    {
                        var found = finder.FindSegments(working, missing, 3, cancellationToken);

                        foreach (var skillset in missing)
                            segmentCache.Set(hash, skillset, adjustments.GetValueOrDefault(skillset), found.TryGetValue(skillset, out var s) ? s : Array.Empty<MapSegment>());
                    }

                    // The first skillset in preference order that has a segment whose own star rating is in range.
                    foreach (var skillset in preference)
                    {
                        double adjustment = adjustments.GetValueOrDefault(skillset);

                        if (!segmentCache.TryGet(hash, skillset, adjustment, out var segments) || segments.Count == 0)
                            continue;

                        // The star rating of a segment is calculated once, then remembered with it.
                        if (segments.Any(s => s.StarRating == null))
                        {
                            segments = segments.Select(s => s.StarRating == null ? s with { StarRating = finder.CalculateStarRating(working, s, cancellationToken) } : s).ToList();
                            segmentCache.Set(hash, skillset, adjustment, segments);
                        }

                        var inRange = segments.Select(s => s with { BeatmapID = id })
                                              .Where(s => s.StarRating >= minStars && s.StarRating <= maxStars)
                                              .ToList();

                        if (inRange.Count == 0)
                            continue;

                        // Mostly the hardest part, sometimes one of the next ones, for variety.
                        return (working, inRange[Random.Shared.Next(inRange.Count)]);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Log($"OSU SR: skipping {info} ({ex.Message})");
                }
            }

            return null;
        }

        private static Guid pickWeighted(IReadOnlyList<(Guid ID, double Weight, double BPM)> pool)
        {
            double roll = Random.Shared.NextDouble() * pool.Sum(p => p.Weight);

            foreach (var (id, weight, _) in pool)
            {
                roll -= weight;

                if (roll < 0)
                    return id;
            }

            return pool[^1].ID;
        }

        /// <param name="working">The map to play.</param>
        /// <param name="segment">The segment to play.</param>
        private void startSegment(WorkingBeatmap working, MapSegment segment)
        {
            currentBeatmapId = segment.BeatmapID;
            currentBpm = working.BeatmapInfo.BPM;
            startedSkillsets.Add(segment.Skillset);

            Ruleset.Value = osuRuleset;
            Beatmap.Value = working;
            Mods.Value = new[]
            {
                finder.CreateSegmentMod(segment),
                osuRuleset.CreateInstance().CreateMod<ModNoFail>()!,
            };

            ApplyToBackground(b => ((BackgroundScreenBeatmap)b).Beatmap = working);

            var info = working.BeatmapInfo;
            statusText.Text = $"{segment.Skillset.GetDescription()}  ·  {info.Metadata.Artist} - {info.Metadata.Title} [{info.DifficultyName}] (passage {segment.StarRating:N2}★, map {info.StarRating:N2}★)"
                              + tagsText(segment.Skillset, info)
                              + $"  ·  {formatTime(segment.FirstObjectTime)} – {formatTime(segment.EndTime)}";

            // Fade the next map's music in from just before the segment, so the transition is continuous.
            double previewStart = Math.Max(0, segment.StartTime - COUNTDOWN_DURATION);
            musicController.SeekTo(previewStart);
            musicController.Play();
            musicController.CurrentTrack.VolumeTo(0).Then().VolumeTo(1, COUNTDOWN_DURATION, Easing.OutQuad);

            var player = new SegmentPlayer(segment, Feedback);
            bool loaded = false;
            bool countdownDone = false;

            LoadComponentAsync(player, _ =>
            {
                loaded = true;
                pushWhenReady();
            });

            countdownText.FadeIn(100);

            for (int i = 0; i < 3; i++)
            {
                int number = 3 - i;
                Scheduler.AddDelayed(() => countdownText.Text = number.ToString(), i * COUNTDOWN_DURATION / 3);
            }

            Scheduler.AddDelayed(() =>
            {
                countdownDone = true;
                pushWhenReady();
            }, COUNTDOWN_DURATION);

            void pushWhenReady()
            {
                if (!loaded || !countdownDone || !running || !this.IsCurrentScreen())
                    return;

                countdownText.FadeOut(100);
                currentPlayer = player;
                this.Push(player);
            }
        }

        private void startSeries()
        {
            preparationCancellation?.Cancel();
            var cancellation = preparationCancellation = new CancellationTokenSource();

            var skillsets = SelectedSkillsets;
            double min = MinimumStars.Value;
            double max = MaximumStars.Value;
            bool preferTagged = PreferTaggedMaps.Value;
            bool matchBpm = MatchBpm.Value;
            finder.PurityAdjustments = Feedback.PurityAdjustments;

            // Only the feed's thread touches these.
            var played = new List<Skillset>();
            var recentMaps = new List<Guid>();
            double? bpm = null;
            var seriesFinder = finder;
            SeriesFeed? seriesFeed = null;
            bool adaptive = AdaptiveDifficulty.Value;
            bool includeUnknown = IncludeUnknownStatus.Value;

            // The candidates are looked up once for the whole series, not for each part.
            List<(Guid ID, double Weight, double BPM)>? pool = null;

            SeriesPart? findNext(CancellationToken token)
            {
                // With adaptive difficulty the candidates cover the range the offset can reach.
                double reach = adaptive ? SeriesFeed.MAXIMUM_STAR_OFFSET : 0;
                pool ??= loadPool(skillsets, Math.Max(0, min - reach), max + reach, preferTagged, includeUnknown);

                double offset = adaptive ? seriesFeed?.StarOffset ?? 0 : 0;
                var found = findNextSegment(pool, skillsets, Math.Max(0, min + offset), max + offset, matchBpm ? bpm : null, recentMaps, played.ToArray(), token);

                if (found == null)
                    return null;

                var (working, segment) = found.Value;
                played.Add(segment.Skillset);

                // Not the same map again for a while.
                recentMaps.Add(segment.BeatmapID!.Value);

                if (recentMaps.Count > recent_maps_to_avoid)
                    recentMaps.RemoveAt(0);

                bpm = working.BeatmapInfo.BPM;

                return new SeriesPart(working, seriesFinder.ExtendSegment(working, segment, PRE_DROP, POST_DROP, token));
            }

            var feed = new SeriesFeed(seriesFinder, findNext, InfiniteMode.Value ? int.MaxValue : SeriesLength.Value);
            seriesFeed = feed;
            feed.Start();

            statusText.Text = "Préparation du premier passage…";
            waitForFirstPart(feed, cancellation);
        }

        /// <summary>Starts the series as soon as its first part is ready: the others are found and added while it plays.</summary>
        private void waitForFirstPart(SeriesFeed feed, CancellationTokenSource cancellation)
        {
            if (cancellation.IsCancellationRequested || !running)
            {
                feed.Dispose();
                return;
            }

            if (feed.NothingFound)
            {
                feed.Dispose();
                statusText.Text = describeEmptyPool();
                stopSession();
                return;
            }

            if (!feed.TryTake(out var first))
            {
                if (feed.Exhausted)
                {
                    feed.Dispose();
                    statusText.Text = "Erreur pendant la préparation de la série (voir les logs).";
                    stopSession();
                    return;
                }

                Scheduler.AddDelayed(() => waitForFirstPart(feed, cancellation), 200);
                return;
            }

            var composite = finder.CreateSeriesBeatmap(first);
            var working = new SeriesWorkingBeatmap(new[] { first.Part }, composite, SeriesTimeline.LIVE_LENGTH, audio);

            Ruleset.Value = osuRuleset;
            Beatmap.Value = working;
            Mods.Value = new[] { osuRuleset.CreateInstance().CreateMod<ModNoFail>()! };

            ApplyToBackground(b => ((BackgroundScreenBeatmap)b).Beatmap = first.Part.Beatmap);

            statusText.Text = InfiniteMode.Value ? "Série infinie (Échap pour quitter)" : $"Série de {SeriesLength.Value} passage(s)";

            var player = new SeriesPlayer(finder, composite, first.Part, first.Slot, feed, Feedback, AdaptiveDifficulty.Value);

            LoadComponentAsync(player, _ =>
            {
                if (!running || !this.IsCurrentScreen())
                {
                    feed.Dispose();
                    return;
                }

                currentSeriesPlayer = player;
                this.Push(player);
            });
        }

        private static string tagsText(Skillset skillset, BeatmapInfo info)
        {
            var tags = SkillsetTags.MatchingTags(skillset, info.Metadata.UserTags).ToList();
            return tags.Count == 0 ? string.Empty : $"  ·  tags osu! : {string.Join(", ", tags)}";
        }

        private static string formatTime(double ms) => TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss");
    }
}
