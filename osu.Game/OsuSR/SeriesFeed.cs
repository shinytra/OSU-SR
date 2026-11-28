// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Threading;
using osu.Framework.Logging;

namespace osu.Game.OsuSR
{
    /// <summary>
    /// Finds, reads and prepares the parts of a series on a separate low priority thread, a couple of parts ahead of the player,
    /// so nothing heavy happens on the game thread while a series plays.
    /// </summary>
    public sealed class SeriesFeed : IDisposable
    {
        /// <summary>How many prepared parts are kept waiting (more would only use memory).</summary>
        public const int LOOKAHEAD = 2;

        private const int max_failures = 5;

        private readonly Func<CancellationToken, SeriesPart?> findNext;
        private readonly Func<SeriesPart, CancellationToken, ISeriesPartData> prepare;
        private readonly int limit;
        private readonly ConcurrentQueue<ISeriesPartData> ready = new ConcurrentQueue<ISeriesPartData>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        private Thread? thread;
        private volatile bool finished;
        private volatile bool noMapFound;
        private int produced;

        /// <param name="findNext">Finds the next part to play (reading maps, analysing them), or null when there is none. Runs on the feed's thread.</param>
        /// <param name="prepare">Gets a part ready (see <see cref="ISkillsetSegmentFinder.PrepareSeriesPart"/>). Runs on the feed's thread.</param>
        /// <param name="limit">The number of parts to produce. <see cref="int.MaxValue"/> for a series that never ends.</param>
        public SeriesFeed(Func<CancellationToken, SeriesPart?> findNext, Func<SeriesPart, CancellationToken, ISeriesPartData> prepare, int limit)
        {
            this.findNext = findNext;
            this.prepare = prepare;
            this.limit = limit;
        }

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

        public bool TryTake(out ISeriesPartData data) => ready.TryDequeue(out data!);

        private void run()
        {
            var token = cancellation.Token;
            int failures = 0;

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
                            noMapFound = true;
                            break;
                        }

                        var data = prepare(part, token);

                        // Opening the music file is slow too: do it here, not when the part starts.
                        if (!part.Beatmap.TrackLoaded)
                            part.Beatmap.LoadTrack();

                        ready.Enqueue(data);
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

        public void Dispose() => cancellation.Cancel();
    }
}
