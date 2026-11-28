// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Logging;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Finds, reads and prepares the parts of a series on a separate low priority thread, a couple of parts ahead of the player:
    /// the map is analysed, its hit objects are created, put on the timeline and given their defaults, and its music file is opened.
    /// The game thread only has to put the finished objects on the playfield, a few at a time.
    /// </summary>
    public sealed class SeriesFeed : IDisposable
    {
        /// <summary>How many prepared parts are kept waiting (more would only use memory).</summary>
        public const int LOOKAHEAD = 2;

        private const int max_failures = 5;
        private const int max_misses = 30;

        private readonly ISkillsetSegmentFinder finder;
        private readonly Func<CancellationToken, SeriesPart?> findNext;
        private readonly int limit;
        private readonly ConcurrentQueue<SeriesPartObjects> ready = new ConcurrentQueue<SeriesPartObjects>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object resyncLock = new object();

        // Only the feed's thread uses these, except through Resync.
        private List<SeriesSlot> slots = new List<SeriesSlot>();
        private SeriesPart? previous;
        private SeriesBuildState state = new SeriesBuildState();
        private (List<SeriesSlot> Slots, SeriesPart Previous, SeriesBuildState State)? pendingResync;

        private Thread? thread;
        private volatile bool finished;
        private volatile bool noMapFound;
        private int produced;

        /// <param name="finder">Creates the hit objects of the parts.</param>
        /// <param name="findNext">Finds the next part to play (reading maps, analysing them), or null when there is none. Runs on the feed's thread.</param>
        /// <param name="limit">The number of parts to produce. <see cref="int.MaxValue"/> for a series that never ends.</param>
        public SeriesFeed(ISkillsetSegmentFinder finder, Func<CancellationToken, SeriesPart?> findNext, int limit)
        {
            this.finder = finder;
            this.findNext = findNext;
            this.limit = limit;
        }

        /// <summary>
        /// Added to the star range of the next parts: the player does worse than they should, so easier ones, or better than they should, so harder ones.
        /// Parts already prepared keep their difficulty, so a change shows after a part or two.
        /// </summary>
        public double StarOffset
        {
            get => Volatile.Read(ref starOffset);
            set => Volatile.Write(ref starOffset, Math.Clamp(value, -MAXIMUM_STAR_OFFSET, MAXIMUM_STAR_OFFSET));
        }

        /// <summary>The farthest the adaptive difficulty goes from the chosen star range (stars).</summary>
        public const double MAXIMUM_STAR_OFFSET = 1;

        private double starOffset;

        /// <summary>The number of parts the series should have, or <see cref="int.MaxValue"/> if it never ends.</summary>
        public int Limit => limit;

        /// <summary>Whether no part could be found at all (so the series has nothing to play).</summary>
        public bool NothingFound => noMapFound && produced == 0;

        /// <summary>Whether no part will ever be produced again, and all produced parts were taken.</summary>
        public bool Exhausted => finished && ready.IsEmpty;

        public void Start()
        {
            thread = new Thread(run)
            {
                Name = "OSU SR series feed",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
            };

            thread.Start();
        }

        public bool TryTake(out SeriesPartObjects data) => ready.TryDequeue(out data!);

        /// <summary>
        /// Tells the feed where the series really is (when a part had to be placed again because it was late), so the next parts are built after it.
        /// </summary>
        public void Resync(List<SeriesSlot> slots, SeriesPart previous, SeriesBuildState state)
        {
            lock (resyncLock)
                pendingResync = (slots, previous, state);
        }

        private void run()
        {
            var token = cancellation.Token;
            int failures = 0;
            int misses = 0;

            try
            {
                while (!token.IsCancellationRequested && Volatile.Read(ref produced) < limit)
                {
                    if (ready.Count >= LOOKAHEAD)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    try
                    {
                        var part = findNext(token);

                        if (part == null)
                        {
                            // A series that never ends doesn't give up because of a dry spell (a tight star range, the adaptive offset...): it waits and tries again.
                            if (limit == int.MaxValue && Volatile.Read(ref produced) > 0 && ++misses < max_misses)
                            {
                                Thread.Sleep(2000);
                                continue;
                            }

                            noMapFound = true;
                            break;
                        }

                        misses = 0;

                        var data = finder.PrepareSeriesPart(part, token);

                        // Opening the music file is slow too: do it here, not when the part starts.
                        if (!part.Beatmap.TrackLoaded)
                            part.Beatmap.LoadTrack();

                        applyResync();

                        var slot = SeriesTimeline.Extend(slots, part, previous);
                        var completedPrevious = previous == null ? null : slots[^2];
                        double switchTime = completedPrevious == null ? 0 : completedPrevious.EndTime + completedPrevious.Gap / 2;
                        var objects = finder.CreateSeriesObjects(data, slot, switchTime, state);

                        previous = part;
                        ready.Enqueue(new SeriesPartObjects(part, data, slot, completedPrevious, completedPrevious?.EndTime ?? 0, switchTime, data.Timing, objects));
                        Interlocked.Increment(ref produced);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "OSU SR: failed to prepare a part of the series");

                        if (++failures >= max_failures)
                            break;
                    }
                }
            }
            finally
            {
                finished = true;
            }
        }

        private void applyResync()
        {
            lock (resyncLock)
            {
                if (pendingResync is not { } resync)
                    return;

                (slots, previous, state) = (resync.Slots, resync.Previous, resync.State);
                pendingResync = null;
            }
        }

        public void Dispose() => cancellation.Cancel();
    }
}
