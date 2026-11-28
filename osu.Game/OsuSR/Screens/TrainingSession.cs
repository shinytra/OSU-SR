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
        private const int max_attempts = 10;

        public readonly Skillset Skillset;

        public readonly BindableDouble MinimumStars = new BindableDouble(4) { MinValue = 0, MaxValue = 12, Precision = 0.1 };
        public readonly BindableDouble MaximumStars = new BindableDouble(6) { MinValue = 0, MaxValue = 12, Precision = 0.1 };

        /// <summary>Results of the segments played in this session, oldest first.</summary>
        public IReadOnlyList<SegmentResult> Results => results;

        private readonly List<SegmentResult> results = new List<SegmentResult>();

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private MusicController musicController { get; set; } = null!;

        private Container setupPanel = null!;
        private OsuSpriteText statusText = null!;
        private OsuSpriteText lastResultText = null!;
        private OsuSpriteText countdownText = null!;

        private IReadOnlyList<Mod> previousMods = Array.Empty<Mod>();
        private RulesetInfo osuRuleset = null!;
        private readonly ISkillsetSegmentFinder? finderOverride;
        private ISkillsetSegmentFinder finder = null!;

        private CancellationTokenSource? preparationCancellation;
        private SegmentPlayer? currentPlayer;
        private bool running;

        /// <param name="skillset">The skillset to train.</param>
        /// <param name="finder">Overrides how segments are found (for tests). Defaults to the osu! ruleset's finder.</param>
        public TrainingSession(Skillset skillset, ISkillsetSegmentFinder? finder = null)
        {
            Skillset = skillset;
            finderOverride = finder;
        }

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenBeatmap(Beatmap.Value);

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            osuRuleset = rulesets.GetRuleset("osu") ?? throw new InvalidOperationException("The osu! ruleset is not available.");
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
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"Session continue : {Skillset}",
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
                                        new SettingsSlider<double>
                                        {
                                            LabelText = "Étoiles minimum",
                                            Current = MinimumStars,
                                            KeyboardStep = 0.1f,
                                        },
                                        new SettingsSlider<double>
                                        {
                                            LabelText = "Étoiles maximum",
                                            Current = MaximumStars,
                                            KeyboardStep = 0.1f,
                                        },
                                        new RoundedButton
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

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            previousMods = Mods.Value;
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

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

            results.Add(player.Result);
            lastResultText.Text = $"Dernier passage : {player.Result.Accuracy:P2}, combo {player.Result.MaxCombo}x, score {player.Result.TotalScore:N0}  ·  {results.Count} passage(s), précision moyenne {results.Average(r => r.Accuracy):P2}";

            prepareNextSegment();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            preparationCancellation?.Cancel();
            restoreMods();
            return base.OnExiting(e);
        }

        /// <summary>
        /// Starts the session with the current star range (same as pressing the start button).
        /// </summary>
        public void StartSession() => startSession();

        private void startSession()
        {
            if (running)
                return;

            running = true;
            results.Clear();
            lastResultText.Text = string.Empty;
            setupPanel.FadeOut(200);

            prepareNextSegment();
        }

        private void stopSession()
        {
            running = false;
            preparationCancellation?.Cancel();
            restoreMods();
            countdownText.FadeOut(200);
            setupPanel.FadeIn(200);
            statusText.Text = results.Count > 0 ? "Session terminée." : string.Empty;
        }

        private void restoreMods()
        {
            if (!Mods.Disabled)
                Mods.Value = previousMods;
        }

        private void prepareNextSegment()
        {
            preparationCancellation?.Cancel();
            var cancellation = preparationCancellation = new CancellationTokenSource();

            double min = MinimumStars.Value;
            double max = MaximumStars.Value;

            statusText.Text = "Recherche du prochain passage…";

            Task.Run(() => findNextSegment(min, max, cancellation.Token), cancellation.Token).ContinueWith(t => Schedule(() =>
            {
                if (cancellation.IsCancellationRequested || !running)
                    return;

                if (t.IsFaulted)
                {
                    Logger.Error(t.Exception, "OSU SR: failed to prepare the next segment");
                    statusText.Text = "Erreur pendant la recherche d'un passage (voir les logs).";
                    stopSession();
                    return;
                }

                if (t.GetResultSafely() == null)
                {
                    statusText.Text = $"Aucune map osu! trouvée entre {min:N1} et {max:N1} étoiles.";
                    stopSession();
                    return;
                }

                var next = t.GetResultSafely()!.Value;
                startCountdown(next.Beatmap, next.Segment);
            }), TaskContinuationOptions.None);
        }

        private (WorkingBeatmap Beatmap, MapSegment Segment)? findNextSegment(double minStars, double maxStars, CancellationToken cancellationToken)
        {
            var pool = realm.Run(r => r.All<BeatmapInfo>()
                                       .Filter($"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0"
                                               + $" AND {nameof(BeatmapInfo.StarRating)} >= $1 AND {nameof(BeatmapInfo.StarRating)} <= $2"
                                               + $" AND {nameof(BeatmapInfo.Hidden)} == false"
                                               + $" AND {nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false",
                                           osuRuleset.ShortName, minStars, maxStars)
                                       .AsEnumerable()
                                       .Select(b => b.ID)
                                       .ToList());

            if (pool.Count == 0)
                return null;

            // Avoid replaying the map that was just played.
            Guid? lastPlayed = results.LastOrDefault()?.Segment.BeatmapID;

            for (int attempt = 0; attempt < max_attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = pool[Random.Shared.Next(pool.Count)];

                if (id == lastPlayed && pool.Count > 1)
                    continue;

                var info = realm.Run(r => r.Find<BeatmapInfo>(id)?.Detach());

                if (info == null)
                    continue;

                var working = beatmaps.GetWorkingBeatmap(info);

                try
                {
                    var segments = finder.FindSegments(working, Skillset, 3, cancellationToken);

                    if (segments.Count == 0)
                        continue;

                    // Mostly the hardest part, sometimes one of the next ones, for variety.
                    var segment = segments[Random.Shared.Next(segments.Count)];
                    return (working, segment with { BeatmapID = id });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Log($"OSU SR: skipping {info} ({ex.Message})");
                }
            }

            return null;
        }

        private void startCountdown(WorkingBeatmap working, MapSegment segment)
        {
            Ruleset.Value = osuRuleset;
            Beatmap.Value = working;
            Mods.Value = new[]
            {
                finder.CreateSegmentMod(segment),
                osuRuleset.CreateInstance().CreateMod<ModNoFail>()!,
            };

            ApplyToBackground(b => ((BackgroundScreenBeatmap)b).Beatmap = working);

            var info = working.BeatmapInfo;
            statusText.Text = $"{info.Metadata.Artist} - {info.Metadata.Title} [{info.DifficultyName}] ({info.StarRating:N2}★)"
                              + $"  ·  {formatTime(segment.FirstObjectTime)} – {formatTime(segment.EndTime)}";

            // Fade the next map's music in from just before the segment, so the transition is continuous.
            double previewStart = Math.Max(0, segment.StartTime - COUNTDOWN_DURATION);
            musicController.SeekTo(previewStart);
            musicController.Play();
            musicController.CurrentTrack.VolumeTo(0).Then().VolumeTo(1, COUNTDOWN_DURATION, Easing.OutQuad);

            var player = new SegmentPlayer(segment);
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

        private static string formatTime(double ms) => TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss");
    }
}
