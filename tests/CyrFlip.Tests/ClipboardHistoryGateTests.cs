using System;
using System.Linq;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What the clipboard history records (spec S0005): the privacy markers (CH-1), the suppression
    /// decided by the operation instead of the clock (CH-5), the history's own write skipped by its
    /// exact sequence number (CH-6), and the one-line preview the strip draws (CH-4).
    /// </summary>
    public sealed class ClipboardHistoryGateTests
    {
        // ---- CH-5: by operation, not by clock ----

        [Fact]
        public void An_update_during_an_operation_is_skipped()
        {
            var gate = new ClipboardHistoryGate();
            gate.Begin();
            Assert.False(gate.ShouldCapture(11));
            Assert.False(gate.ShouldCapture(12));
            gate.End(12);
            Assert.False(gate.ShouldCapture(12)); // the restore's own message, delivered late
        }

        [Fact]
        public void A_real_copy_right_after_an_operation_is_recorded()
        {
            var gate = new ClipboardHistoryGate();
            gate.Begin();
            gate.End(20);
            Thread.Sleep(100);
            Assert.True(gate.ShouldCapture(21)); // the old gate dropped anything within 2 s of the chord
        }

        [Fact]
        public void A_long_operation_leaks_nothing()
        {
            var gate = new ClipboardHistoryGate();
            gate.Begin();
            // Five seconds of clipboard traffic - a slow Excel backup, an RDP round trip - stays out,
            // however late in the operation it comes. The old 2 s window let the tail of it through.
            foreach (uint sequence in Enumerable.Range(1, 50).Select(i => (uint)i))
                Assert.False(gate.ShouldCapture(sequence));
            gate.End(50);
            Assert.False(gate.ShouldCapture(50));
            Assert.True(gate.ShouldCapture(51));
        }

        [Fact]
        public void Nested_operations_suppress_until_the_last_one_ends()
        {
            var gate = new ClipboardHistoryGate();
            gate.Begin();
            gate.Begin();
            gate.End(5);
            Assert.False(gate.ShouldCapture(6));
            gate.End(7);
            Assert.True(gate.ShouldCapture(8));
        }

        [Fact]
        public void With_no_operation_ever_everything_is_recorded()
        {
            var gate = new ClipboardHistoryGate();
            Assert.True(gate.ShouldCapture(0));
            Assert.True(gate.ShouldCapture(uint.MaxValue));
        }

        [Fact]
        public void The_end_mark_survives_the_counter_wrapping()
        {
            var gate = new ClipboardHistoryGate();
            gate.Begin();
            gate.End(uint.MaxValue);
            Assert.False(gate.ShouldCapture(uint.MaxValue));
            Assert.True(gate.ShouldCapture(0));
            Assert.True(gate.ShouldCapture(3));
        }

        // ---- CH-6: the history's own write ----

        [Fact]
        public void The_own_write_is_skipped_by_its_exact_number_only()
        {
            var gate = new ClipboardHistoryGate();
            gate.ExpectOwnWrite(40);
            Assert.False(gate.ShouldCapture(40));
            Assert.True(gate.ShouldCapture(41));
        }

        /// <summary>
        /// Pause, restore an entry, resume, copy: the copy is recorded. The boolean this replaced was
        /// never consumed while paused, and swallowed exactly that copy.
        /// </summary>
        [Fact]
        public void A_copy_after_a_paused_restore_is_recorded()
        {
            var gate = new ClipboardHistoryGate();
            gate.ExpectOwnWrite(40);   // restore while paused: the update is never even looked at
            Assert.True(gate.ShouldCapture(41)); // resume, copy
        }

        // ---- CH-1: privacy markers ----

        [Fact]
        public void An_unmarked_copy_is_recorded()
        {
            Assert.False(ClipboardPrivacy.ShouldSkip(default));
        }

        [Fact]
        public void Each_marker_on_its_own_skips_the_copy()
        {
            Assert.True(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { ExcludeFromMonitor = true }));
            Assert.True(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { ViewerIgnore = true }));
            Assert.True(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanIncludeInHistory = 0 }));
            Assert.True(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanUploadToCloud = 0 }));
        }

        [Fact]
        public void The_dword_markers_skip_only_on_zero()
        {
            Assert.False(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanIncludeInHistory = 1 }));
            Assert.False(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanUploadToCloud = 1 }));
            Assert.False(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanIncludeInHistory = 1, CanUploadToCloud = 1 }));
            Assert.True(ClipboardPrivacy.ShouldSkip(new ClipboardPrivacyMarkers { CanIncludeInHistory = 1, CanUploadToCloud = 0 }));
        }

        // ---- CH-4: the preview ----

        [Fact]
        public void The_preview_of_a_long_text_is_short_and_on_one_line()
        {
            string text = string.Concat(Enumerable.Repeat("line one\r\nline two\nline three\r", 3000));
            Assert.True(text.Length > 64 * 1024);

            string preview = ClipboardHistoryEntry.MakePreview(text);

            Assert.True(preview.Length <= ClipboardHistoryEntry.PreviewLength);
            Assert.DoesNotContain('\r', preview);
            Assert.DoesNotContain('\n', preview);
            Assert.StartsWith("line one line two line three line one", preview);
        }

        [Fact]
        public void The_preview_collapses_every_kind_of_line_break_and_trims()
        {
            Assert.Equal("a b c d", ClipboardHistoryEntry.MakePreview("\r\n a\r\nb" + (char)0x2028 + "c" + (char)0x85 + "d \n"));
            Assert.Equal("", ClipboardHistoryEntry.MakePreview(null));
        }

        [Fact]
        public void The_preview_is_computed_once()
        {
            var entry = new ClipboardHistoryEntry { Text = "first" };
            string preview = entry.Preview;
            Assert.Same(preview, entry.Preview);
        }
    }
}
