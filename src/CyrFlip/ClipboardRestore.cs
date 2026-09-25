using System;

namespace CyrFlip
{
    /// <summary>What to do with the user's clipboard once a flip has finished with it.</summary>
    internal enum RestoreAction
    {
        /// <summary>Put the backup back.</summary>
        Restore,
        /// <summary>
        /// Nothing changed since the backup: our Ctrl+C produced nothing and we wrote nothing, so the
        /// clipboard is still the user's, with every format on it - RTF, HTML, Excel's cells, the live
        /// "marching ants" copy. Rewriting it would destroy exactly those (FP-3).
        /// </summary>
        Unchanged,
        /// <summary>
        /// Somebody wrote the clipboard after our own write - the user copied something, or the
        /// target did. It is theirs now and must not be overwritten (FP-1).
        /// </summary>
        TakenOver,
        /// <summary>There was nothing restorable to begin with.</summary>
        NothingToRestore,
    }

    /// <summary>
    /// The decisions around handing the clipboard back (ticket S0009, FP-1 and FP-3), kept pure so
    /// they are unit-tested without a clipboard, a target window or a clock.
    /// </summary>
    internal static class ClipboardRestore
    {
        /// <param name="backupSequence">The clipboard sequence number the backup was read at.</param>
        /// <param name="current">The sequence number now, read with the clipboard held open.</param>
        /// <param name="stillOurs">
        /// Whether the clipboard still holds our own paste - judged by ownership for a delayed-rendered
        /// paste and by the sequence number of the write otherwise - or null when the operation wrote
        /// nothing of its own (a capture only).
        /// </param>
        public static RestoreAction Plan(uint backupSequence, bool hasContent, uint current, bool? stillOurs)
        {
            if (current == backupSequence) return RestoreAction.Unchanged;
            if (stillOurs == false) return RestoreAction.TakenOver;
            if (!hasContent) return RestoreAction.NothingToRestore;
            return RestoreAction.Restore;
        }
    }

    /// <summary>
    /// How long the paste half waits before the clipboard is handed back. The fixed numbers are the
    /// fallback for when the target's read cannot be observed (see <see cref="PasteWait"/>).
    /// </summary>
    internal static class PasteTiming
    {
        /// <summary>
        /// The original fixed wait after Ctrl+V: enough for a responsive local app to take the text,
        /// and the moment the modifier keys the user still holds are pressed again.
        /// </summary>
        public const int KeystrokeSettleMs = 140;

        /// <summary>
        /// The fixed wait for a remote-desktop or VM console, whose far side asks for the data
        /// asynchronously - 140 ms there pasted the user's previous clipboard over the selection (FP-1A).
        /// </summary>
        public const int RemoteSettleMs = 1500;

        /// <summary>After the target has taken the text: let it finish before the clipboard changes under it.</summary>
        public const int GraceMs = 100;

        /// <summary>Longest wait for a target that never takes the text (open decision 1).</summary>
        public const int LocalCapMs = 2000;
        public const int RemoteCapMs = 5000;

        /// <summary>How long the copy half waits for the selection to reach the clipboard.</summary>
        public const int LocalCapturePollMs = 480;
        public const int RemoteCapturePollMs = 1500;

        public static int Settle(bool remote) => remote ? RemoteSettleMs : KeystrokeSettleMs;
        public static int Cap(bool remote) => remote ? RemoteCapMs : LocalCapMs;
        public static int CapturePoll(bool remote) => remote ? RemoteCapturePollMs : LocalCapturePollMs;
    }

    /// <summary>Where a delayed-rendered paste stands (<see cref="ClipboardOwner"/>).</summary>
    internal enum PasteSignalState
    {
        /// <summary>Nobody has asked for the text since the Ctrl+V.</summary>
        Pending,
        /// <summary>The text was rendered after the Ctrl+V - the target read it.</summary>
        Consumed,
        /// <summary>
        /// The text was rendered <b>before</b> the Ctrl+V - a clipboard monitor fetched it - so the
        /// target's own read can no longer be observed.
        /// </summary>
        RenderedEarly,
        /// <summary>Somebody else emptied the clipboard: it is not ours any more.</summary>
        Lost,
    }

    /// <summary>The observable side of one delayed-rendered paste.</summary>
    internal interface IPasteSignal
    {
        /// <summary>Wait up to <paramref name="timeoutMs"/> for anything but <see cref="PasteSignalState.Pending"/>.</summary>
        PasteSignalState Wait(int timeoutMs);

        /// <summary>When the text was taken, on the <see cref="PasteWait"/> clock; valid once consumed.</summary>
        long ConsumedAtMs { get; }
    }

    /// <summary>How a paste wait ended - also what the diagnostics log records.</summary>
    internal enum PasteOutcome { Consumed, RenderedEarly, TakenOver, TimedOut, NoSignal }

    /// <summary>
    /// The paste half's wait (FP-1). With a delayed-rendered paste it waits for the target to
    /// actually take the text - plus <see cref="PasteTiming.GraceMs"/> - and never longer than the cap;
    /// without one (the owner is unavailable, or a monitor rendered the text before the Ctrl+V) it
    /// falls back to the fixed settle time, the long one for a remote target.
    /// </summary>
    internal static class PasteWait
    {
        /// <summary>The production clock: monotonic milliseconds, immune to wall-clock changes.</summary>
        public static long NowMs() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

        /// <param name="sentAtMs">When the Ctrl+V went out, on <paramref name="clock"/>.</param>
        public static PasteOutcome Run(IPasteSignal? signal, bool remote, long sentAtMs,
            Func<long> clock, Action<int> sleep)
        {
            if (signal == null)
            {
                SleepUntil(sentAtMs + PasteTiming.Settle(remote), clock, sleep);
                return PasteOutcome.NoSignal;
            }

            int remaining = (int)Math.Max(0, sentAtMs + PasteTiming.Cap(remote) - clock());
            switch (signal.Wait(remaining))
            {
                case PasteSignalState.Consumed:
                    SleepUntil(signal.ConsumedAtMs + PasteTiming.GraceMs, clock, sleep);
                    return PasteOutcome.Consumed;
                case PasteSignalState.RenderedEarly:
                    SleepUntil(sentAtMs + PasteTiming.Settle(remote), clock, sleep);
                    return PasteOutcome.RenderedEarly;
                case PasteSignalState.Lost:
                    return PasteOutcome.TakenOver;
                default:
                    return PasteOutcome.TimedOut;
            }
        }

        private static void SleepUntil(long deadline, Func<long> clock, Action<int> sleep)
        {
            long left = deadline - clock();
            if (left > 0) sleep((int)left);
        }
    }
}

namespace CyrFlip
{
    /// <summary>
    /// "Another clipboard operation is still running" is worth saying once, not once per auto-repeat
    /// of a held chord (ticket S0009, FP-5): at most one balloon per <see cref="IntervalMs"/>.
    /// </summary>
    internal static class BusyNotice
    {
        public const int IntervalMs = 5000;

        /// <param name="lastShownMs">When it was last shown, or <see cref="long.MinValue"/> for never.</param>
        public static bool ShouldShow(long lastShownMs, long nowMs)
            => lastShownMs == long.MinValue || nowMs - lastShownMs >= IntervalMs;
    }
}
