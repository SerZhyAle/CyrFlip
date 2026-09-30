using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// When the flip pipeline hands the clipboard back (ticket S0009, FP-1 and FP-3): the restore
    /// decision, the paste wait against a fake delayed-render signal on a fake clock, and the
    /// state machine of a real <see cref="PasteOffer"/> - none of it touching the clipboard.
    /// </summary>
    public sealed class ClipboardRestoreTests
    {
        // ---- FP-3: restore only what was actually replaced ----

        [Fact]
        public void An_unchanged_clipboard_is_left_alone_whatever_it_holds()
        {
            // The chord pressed with nothing selected: our Ctrl+C produced nothing, we wrote nothing,
            // and Excel's cells, the RTF, the "marching ants" copy are all still there - untouched.
            Assert.Equal(RestoreAction.Unchanged, ClipboardRestore.Plan(7, hasContent: true, current: 7, stillOurs: null));
        }

        [Fact]
        public void A_clipboard_the_copy_changed_is_restored()
        {
            Assert.Equal(RestoreAction.Restore, ClipboardRestore.Plan(7, hasContent: true, current: 9, stillOurs: null));
        }

        [Fact]
        public void Nothing_to_restore_stays_nothing()
        {
            Assert.Equal(RestoreAction.NothingToRestore, ClipboardRestore.Plan(7, hasContent: false, current: 9, stillOurs: null));
        }

        // ---- FP-1A: never over somebody else's write ----

        [Fact]
        public void A_paste_still_on_the_clipboard_is_restored_over()
        {
            Assert.Equal(RestoreAction.Restore, ClipboardRestore.Plan(7, hasContent: true, current: 12, stillOurs: true));
        }

        [Fact]
        public void A_clipboard_written_after_the_paste_is_not_overwritten()
        {
            // The user copied something while the slow target was still pasting: it is theirs now.
            Assert.Equal(RestoreAction.TakenOver, ClipboardRestore.Plan(7, hasContent: true, current: 14, stillOurs: false));
        }

        [Fact]
        public void The_receipt_of_a_plain_write_is_judged_by_its_sequence_number()
        {
            ClipboardHandler.PasteReceipt receipt = ClipboardHandler.PasteReceipt.BySequence(12);

            Assert.True(receipt.Wrote);
            Assert.True(receipt.StillOurs(12));
            Assert.False(receipt.StillOurs(13));
            Assert.Null(default(ClipboardHandler.PasteReceipt).StillOurs(12)); // a capture wrote nothing
        }

        [Theory]
        [InlineData(false, PasteTiming.KeystrokeSettleMs, PasteTiming.LocalCapMs, PasteTiming.LocalCapturePollMs)]
        [InlineData(true, PasteTiming.RemoteSettleMs, PasteTiming.RemoteCapMs, PasteTiming.RemoteCapturePollMs)]
        public void A_remote_target_gets_the_long_waits(bool remote, int settle, int cap, int poll)
        {
            Assert.Equal(settle, PasteTiming.Settle(remote));
            Assert.Equal(cap, PasteTiming.Cap(remote));
            Assert.Equal(poll, PasteTiming.CapturePoll(remote));
            Assert.Equal(1500, PasteTiming.RemoteSettleMs); // phase A, as specified
            Assert.Equal(2000, PasteTiming.LocalCapMs);     // open decision 1
            Assert.Equal(5000, PasteTiming.RemoteCapMs);
        }

        // ---- FP-1B: wait for the target's read, not for a clock ----

        private sealed class FakeClock
        {
            public long Now;
            public readonly List<int> Sleeps = new List<int>();
            public void Sleep(int ms) { Sleeps.Add(ms); Now += ms; }
        }

        /// <summary>A delayed-render signal that fires (or not) at a set moment of the fake clock.</summary>
        private sealed class FakeSignal : IPasteSignal
        {
            private readonly FakeClock _clock;
            private readonly long? _firesAt;
            private readonly PasteSignalState _state;

            public FakeSignal(FakeClock clock, long? firesAt, PasteSignalState state = PasteSignalState.Consumed)
            {
                _clock = clock;
                _firesAt = firesAt;
                _state = state;
            }

            public long ConsumedAtMs { get; private set; }

            public PasteSignalState Wait(int timeoutMs)
            {
                if (_firesAt.HasValue && _firesAt.Value <= _clock.Now + timeoutMs)
                {
                    if (_firesAt.Value > _clock.Now) _clock.Now = _firesAt.Value;
                    ConsumedAtMs = _firesAt.Value;
                    return _state;
                }
                _clock.Now += timeoutMs;
                return PasteSignalState.Pending;
            }
        }

        [Fact]
        public void A_paste_taken_at_50_ms_is_restored_at_about_150_ms()
        {
            var clock = new FakeClock();
            var signal = new FakeSignal(clock, firesAt: 50);

            PasteOutcome outcome = PasteWait.Run(signal, remote: false, sentAtMs: 0, () => clock.Now, clock.Sleep);

            Assert.Equal(PasteOutcome.Consumed, outcome);
            Assert.Equal(50 + PasteTiming.GraceMs, clock.Now);
        }

        [Fact]
        public void The_grace_counts_from_the_read_not_from_when_the_wait_began()
        {
            // The modifier keys are pressed again 140 ms after the Ctrl+V; a read at 50 ms is long
            // over by then and is owed only what is left of its grace.
            var clock = new FakeClock { Now = PasteTiming.KeystrokeSettleMs };
            var signal = new FakeSignal(clock, firesAt: 50);

            PasteWait.Run(signal, remote: false, sentAtMs: 0, () => clock.Now, clock.Sleep);

            Assert.Equal(PasteTiming.KeystrokeSettleMs + 10, clock.Now);
        }

        [Theory]
        [InlineData(false, PasteTiming.LocalCapMs)]
        [InlineData(true, PasteTiming.RemoteCapMs)]
        public void A_paste_nobody_takes_is_restored_at_the_cap(bool remote, int cap)
        {
            var clock = new FakeClock();
            var signal = new FakeSignal(clock, firesAt: null);

            PasteOutcome outcome = PasteWait.Run(signal, remote, sentAtMs: 0, () => clock.Now, clock.Sleep);

            Assert.Equal(PasteOutcome.TimedOut, outcome);
            Assert.Equal(cap, clock.Now);
        }

        [Theory]
        [InlineData(false, PasteTiming.KeystrokeSettleMs)]
        [InlineData(true, PasteTiming.RemoteSettleMs)]
        public void Without_a_signal_the_fixed_wait_applies(bool remote, int settle)
        {
            var clock = new FakeClock();

            PasteOutcome outcome = PasteWait.Run(null, remote, sentAtMs: 0, () => clock.Now, clock.Sleep);

            Assert.Equal(PasteOutcome.NoSignal, outcome);
            Assert.Equal(settle, clock.Now);
        }

        [Fact]
        public void A_monitor_that_rendered_first_falls_back_to_the_fixed_wait()
        {
            var clock = new FakeClock();
            var signal = new FakeSignal(clock, firesAt: 0, PasteSignalState.RenderedEarly);

            PasteOutcome outcome = PasteWait.Run(signal, remote: true, sentAtMs: 0, () => clock.Now, clock.Sleep);

            Assert.Equal(PasteOutcome.RenderedEarly, outcome);
            Assert.Equal(PasteTiming.RemoteSettleMs, clock.Now);
        }

        [Fact]
        public void A_clipboard_lost_during_the_wait_ends_it_at_once()
        {
            var clock = new FakeClock();
            var signal = new FakeSignal(clock, firesAt: 300, PasteSignalState.Lost);

            Assert.Equal(PasteOutcome.TakenOver, PasteWait.Run(signal, remote: false, sentAtMs: 0, () => clock.Now, clock.Sleep));
            Assert.Equal(300, clock.Now);
            Assert.Empty(clock.Sleeps);
        }

        // ---- the real offer's state machine, driven by hand ----

        [Fact]
        public void A_render_after_the_ctrl_v_is_the_target_taking_the_paste()
        {
            var offer = new PasteOffer("x", 0, TransientMarks.All);
            offer.Arm();
            offer.MarkRendered();

            Assert.Equal(PasteSignalState.Consumed, offer.Wait(0));
        }

        [Fact]
        public void A_render_before_the_ctrl_v_was_a_monitor_and_stays_so()
        {
            var offer = new PasteOffer("x", 0, TransientMarks.All);
            offer.MarkRendered();   // a clipboard manager fetched it
            offer.Arm();
            offer.MarkRendered();   // cannot happen for real - rendered data is not asked for again

            Assert.Equal(PasteSignalState.RenderedEarly, offer.Wait(0));
        }

        // ---- S0032 FP2-3: only the target's read is the proof ----

        [Fact]
        public void A_foreign_render_after_the_ctrl_v_is_not_the_target_and_falls_back_to_the_fixed_wait()
        {
            var offer = new PasteOffer("x", 0, TransientMarks.All, targetProcessId: 42);
            offer.Arm();
            offer.MarkRendered(byTarget: false);   // a clipboard manager that ignores the markers

            Assert.Equal(PasteSignalState.RenderedEarly, offer.Wait(0));
        }

        [Fact]
        public void A_foreign_render_after_the_target_took_the_paste_changes_nothing()
        {
            var offer = new PasteOffer("x", 0, TransientMarks.All, targetProcessId: 42);
            offer.Arm();
            offer.MarkRendered(byTarget: true);
            offer.MarkRendered(byTarget: false);

            Assert.Equal(PasteSignalState.Consumed, offer.Wait(0));
        }

        [Theory]
        [InlineData(42u, 42u, false, true)]   // the target itself
        [InlineData(7u, 42u, false, false)]   // a clipboard manager
        [InlineData(7u, 42u, true, true)]     // a remote/VM clipboard bridge
        [InlineData(0u, 42u, false, true)]    // opened without a window: most likely the target
        [InlineData(7u, 0u, false, true)]     // the target's process is unknown: nothing to compare
        public void Whose_read_counts_as_the_paste(uint requester, uint target, bool bridge, bool expected)
        {
            Assert.Equal(expected, PasteOffer.IsTargetRead(requester, target, bridge));
        }

        // ---- S0032 FP2-6: a failed owner start is retried ----

        [Fact]
        public void A_failed_owner_start_backs_off_and_is_retried()
        {
            Assert.Equal(0, ClipboardOwner.RetryDelayMs(0));
            Assert.Equal(30000, ClipboardOwner.RetryDelayMs(1));
            Assert.Equal(60000, ClipboardOwner.RetryDelayMs(2));
            Assert.True(ClipboardOwner.RetryDelayMs(3) > ClipboardOwner.RetryDelayMs(2));
            Assert.Equal(30L * 60 * 1000, ClipboardOwner.RetryDelayMs(50));
        }

        [Fact]
        public void An_offer_nobody_asked_for_is_pending_and_one_emptied_is_lost()
        {
            var offer = new PasteOffer("x", 0, TransientMarks.All);
            offer.Arm();
            Assert.Equal(PasteSignalState.Pending, offer.Wait(0));

            offer.MarkLost();
            Assert.Equal(PasteSignalState.Lost, offer.Wait(0));
        }

        // ---- the scaffolding's markers ----

        [Fact]
        public void A_local_paste_is_marked_do_not_record_three_ways()
        {
            uint[] formats = Win32Clipboard.TransientMarkers(TransientMarks.All).Select(m => m.Key).ToArray();

            Assert.Equal(new[] { ClipboardFormats.ExcludeFromMonitor, ClipboardFormats.CanIncludeInHistory, ClipboardFormats.CanUploadToCloud }, formats);
            Assert.All(Win32Clipboard.TransientMarkers(TransientMarks.All), m => Assert.Equal(new byte[4], m.Value));
        }

        [Fact]
        public void A_remote_paste_is_never_hidden_from_the_bridge_that_carries_it()
        {
            // An RDP or VM clipboard bridge is a clipboard monitor itself: "exclude from monitors" could
            // keep the text from ever reaching the far side. Only the Win+V / cloud markers go up.
            uint[] formats = Win32Clipboard.TransientMarkers(TransientMarks.HistoryOnly).Select(m => m.Key).ToArray();

            Assert.Equal(new[] { ClipboardFormats.CanIncludeInHistory, ClipboardFormats.CanUploadToCloud }, formats);
        }

        [Fact]
        public void Text_meant_to_stay_carries_no_marker()
        {
            Assert.Empty(Win32Clipboard.TransientMarkers(TransientMarks.None));
        }

        // ---- FP-5: the busy notice ----

        [Fact]
        public void The_busy_notice_is_shown_once_per_interval()
        {
            Assert.True(BusyNotice.ShouldShow(long.MinValue, 0));
            Assert.False(BusyNotice.ShouldShow(1000, 1000 + BusyNotice.IntervalMs - 1));
            Assert.True(BusyNotice.ShouldShow(1000, 1000 + BusyNotice.IntervalMs));
        }
    }
}
