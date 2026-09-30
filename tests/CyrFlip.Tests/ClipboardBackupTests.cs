using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The decision <see cref="ClipboardHandler.RestoreClipboard"/> makes before it touches anything:
    /// is there something to hand back at all. Nothing here opens the real clipboard - a test that
    /// did would wipe whatever the person running it had copied.
    /// </summary>
    public sealed class ClipboardBackupTests
    {
        [Fact]
        public void An_empty_backup_has_nothing_to_restore()
        {
            var backup = new ClipboardHandler.ClipboardBackup(hadText: false, text: null);

            Assert.False(backup.HasContent);
        }

        [Fact]
        public void Text_alone_counts_as_content()
        {
            var backup = new ClipboardHandler.ClipboardBackup(hadText: true, text: "hello");

            Assert.True(backup.HasContent);
        }

        [Fact]
        public void A_clipboard_that_held_text_we_could_not_read_is_not_content()
        {
            // The format was announced but the read failed - restoring an empty string would clear
            // the clipboard rather than leave it be, which is worse than doing nothing.
            var backup = new ClipboardHandler.ClipboardBackup(hadText: true, text: null);

            Assert.False(backup.HasContent);
        }

        [Fact]
        public void An_image_with_no_text_still_counts_as_content()
        {
            // The whole point of the change: a screenshot on the clipboard used to leave HadText
            // false and the backup empty, so the flip's EmptyClipboard destroyed it for good.
            var backup = new ClipboardHandler.ClipboardBackup(hadText: false, text: null,
                image: new byte[] { 1, 2, 3 });

            Assert.True(backup.HasContent);
        }

        [Fact]
        public void Copied_files_with_no_text_still_count_as_content()
        {
            var backup = new ClipboardHandler.ClipboardBackup(hadText: false, text: null,
                files: new byte[] { 4, 5, 6 });

            Assert.True(backup.HasContent);
        }

        [Fact]
        public void Every_format_is_carried_through_to_the_restore()
        {
            var backup = new ClipboardHandler.ClipboardBackup(hadText: true, text: "hello",
                image: new byte[] { 1 }, files: new byte[] { 2 });

            Assert.True(backup.HasContent);
            Assert.Equal("hello", backup.Text);
            Assert.Equal(new byte[] { 1 }, backup.Image);
            Assert.Equal(new byte[] { 2 }, backup.Files);
        }
    }

    /// <summary>
    /// <see cref="ClipboardHandler.BackupClipboard(IClipboardReader)"/> over a fake clipboard (ticket
    /// S0009): what is kept, what goes back, and when a flip must not start at all.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public sealed class ClipboardBackupReadTests
    {
        private const uint Text = WindowInterop.CF_UNICODETEXT;
        private const uint Dib = WindowInterop.CF_DIB;
        private const uint Drop = WindowInterop.CF_HDROP;
        private const uint Locale = WindowInterop.CF_LOCALE;

        private static byte[] Unicode(string text) => Win32Clipboard.UnicodeBytes(text);
        private static byte[] Dword(uint value) => BitConverter.GetBytes(value);

        private static byte[]? Payload(ClipboardHandler.ClipboardBackup backup, uint format)
        {
            foreach (KeyValuePair<uint, byte[]> payload in ClipboardHandler.RestorePayloads(backup))
                if (payload.Key == format) return payload.Value;
            return null;
        }

        // ---- FP-2: a password manager's markers survive the restore ----

        [Fact]
        public void A_password_managers_markers_go_back_with_the_secret()
        {
            var clip = new FakeClipboard()
                .With(Text, Unicode("hunter2"))
                .With(ClipboardFormats.CanIncludeInHistory, Dword(0));

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            // Without the marker the restored password went straight into Win+V and the cloud clipboard.
            Assert.Equal(Unicode("hunter2"), Payload(backup, Text));
            Assert.Equal(Dword(0), Payload(backup, ClipboardFormats.CanIncludeInHistory));
        }

        [Fact]
        public void All_four_privacy_markers_are_carried_as_they_were()
        {
            var clip = new FakeClipboard()
                .With(Text, Unicode("secret"))
                .With(ClipboardFormats.ExcludeFromMonitor, new byte[] { 1 })
                .With(ClipboardFormats.ViewerIgnore, new byte[] { 7, 7 })
                .With(ClipboardFormats.CanIncludeInHistory, Dword(0))
                .With(ClipboardFormats.CanUploadToCloud, Dword(0));

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.Equal(new byte[] { 1 }, Payload(backup, ClipboardFormats.ExcludeFromMonitor));
            Assert.Equal(new byte[] { 7, 7 }, Payload(backup, ClipboardFormats.ViewerIgnore));
            Assert.Equal(Dword(0), Payload(backup, ClipboardFormats.CanIncludeInHistory));
            Assert.Equal(Dword(0), Payload(backup, ClipboardFormats.CanUploadToCloud));
        }

        [Fact]
        public void A_marker_that_cannot_be_read_still_goes_back_as_do_not_record()
        {
            // Its presence is the message: dropping it because its bytes would not come would publish
            // the very secret it guards.
            var clip = new FakeClipboard()
                .With(Text, Unicode("secret"))
                .Failing(ClipboardFormats.CanUploadToCloud)
                .With(ClipboardFormats.ExcludeFromMonitor, new byte[0]);

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.False(backup.Unreadable); // a companion is never a reason to refuse the flip
            Assert.Equal(Dword(0), Payload(backup, ClipboardFormats.CanUploadToCloud));
            Assert.Equal(Dword(0), Payload(backup, ClipboardFormats.ExcludeFromMonitor));
        }

        [Fact]
        public void An_explicit_yes_marker_is_not_turned_into_a_no()
        {
            var clip = new FakeClipboard()
                .With(Text, Unicode("fine"))
                .With(ClipboardFormats.CanIncludeInHistory, Dword(1));

            Assert.Equal(Dword(1), Payload(ClipboardHandler.BackupClipboard(clip), ClipboardFormats.CanIncludeInHistory));
        }

        [Fact]
        public void Markers_alone_are_not_content_and_restore_nothing()
        {
            var clip = new FakeClipboard().With(ClipboardFormats.ExcludeFromMonitor, new byte[] { 1 });

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.False(backup.HasContent);
            Assert.Empty(ClipboardHandler.RestorePayloads(backup));
        }

        // ---- FP-7: a Cut of files stays a move ----

        [Fact]
        public void Cut_files_keep_their_move_effect_and_shell_list()
        {
            const uint DropEffectMove = 2;
            var clip = new FakeClipboard()
                .With(Drop, new byte[] { 20, 0, 0, 0, 1 })
                .With(ClipboardFormats.PreferredDropEffect, Dword(DropEffectMove))
                .With(ClipboardFormats.ShellIdListArray, new byte[] { 9, 8, 7 });

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.Equal(new byte[] { 20, 0, 0, 0, 1 }, Payload(backup, Drop));
            Assert.Equal(Dword(DropEffectMove), Payload(backup, ClipboardFormats.PreferredDropEffect));
            Assert.Equal(new byte[] { 9, 8, 7 }, Payload(backup, ClipboardFormats.ShellIdListArray));
        }

        // ---- FP-8: the text's own locale comes back with it ----

        [Fact]
        public void The_texts_locale_is_restored_with_it()
        {
            var clip = new FakeClipboard()
                .With(Text, Unicode("привет"))
                .With(Locale, Dword(0x0419));

            Assert.Equal(Dword(0x0419), Payload(ClipboardHandler.BackupClipboard(clip), Locale));
        }

        // ---- FP-6: the text is one copy out and one copy back ----

        [Fact]
        public void The_text_is_kept_byte_for_byte_padding_included()
        {
            byte[] raw = Unicode("abc").Concat(new byte[] { 0x41, 0, 0x42, 0 }).ToArray(); // NUL, then padding

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(new FakeClipboard().With(Text, raw));

            Assert.Equal(raw, backup.TextBytes);
            Assert.Equal("abc", backup.Text);
            Assert.Equal(raw, Payload(backup, Text));
        }

        [Fact]
        public void The_backup_is_read_in_one_open_at_the_contents_sequence_number()
        {
            var clip = new FakeClipboard { SequenceNumber = 4242 }
                .With(Text, Unicode("x"))
                .With(Dib, new byte[] { 1 })
                .With(Drop, new byte[] { 2 })
                .With(Locale, Dword(0x409));

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.Equal(1, clip.Opens);
            Assert.False(clip.IsOpen);
            Assert.Equal(4242u, backup.Sequence);
            Assert.Equal(new uint[] { Text, Dib, Drop, Locale },
                ClipboardHandler.RestorePayloads(backup).Select(p => p.Key).ToArray());
        }

        // ---- FP-5: empty is not unreadable ----

        [Fact]
        public void A_locked_clipboard_that_holds_formats_is_unreadable()
        {
            var clip = new FakeClipboard { Locked = true }.With(Text, Unicode("mine"));

            Assert.True(ClipboardHandler.BackupClipboard(clip).Unreadable);
        }

        [Fact]
        public void A_locked_but_empty_clipboard_is_just_empty()
        {
            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(new FakeClipboard { Locked = true });

            Assert.False(backup.Unreadable);
            Assert.False(backup.HasContent);
        }

        [Fact]
        public void Text_whose_owner_cannot_render_it_is_unreadable()
        {
            var clip = new FakeClipboard().With(Dib, new byte[] { 1, 2, 3 }).Failing(Text);

            Assert.True(ClipboardHandler.BackupClipboard(clip).Unreadable);
        }

        /// <summary>
        /// S0032 FP2-2: a picture or a file list its owner fails to render (a huge Excel range) is not
        /// carried, but no longer blocks every flip until the next copy - the text still goes back.
        /// </summary>
        [Theory]
        [InlineData(WindowInterop.CF_DIB)]
        [InlineData(WindowInterop.CF_HDROP)]
        public void A_picture_or_file_list_that_will_not_render_is_dropped_not_fatal(uint format)
        {
            var clip = new FakeClipboard().With(Text, Unicode("mine")).Failing(format);

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.False(backup.Unreadable);
            Assert.Equal("mine", backup.Text);
            Assert.Null(backup.Image);
            Assert.Null(backup.Files);
            Assert.True(backup.HasContent);
        }

        [Fact]
        public void Text_over_the_cap_is_unreadable_rather_than_marshalled()
        {
            var clip = new FakeClipboard().With(Text, new byte[ClipboardHandler.MaxBackupTextBytes + 2]);

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.True(backup.Unreadable);
            Assert.Null(backup.TextBytes);
        }

        /// <summary>
        /// S0032 FP2-1: reading a delay-rendered format makes its owner render it, which moves the
        /// sequence. The backup keeps the number after its own reads, so the chord with nothing selected
        /// still sees "unchanged" and leaves Excel's live copy alone.
        /// </summary>
        [Fact]
        public void The_sequence_is_the_one_after_the_backups_own_reads()
        {
            var clip = new FakeClipboard { BumpSequenceOnRead = true }
                .With(Text, Unicode("cells"))
                .With(Dib, new byte[] { 1, 2, 3 });

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.True(clip.SequenceNumber > 100);
            Assert.Equal(clip.SequenceNumber, backup.Sequence);
            Assert.Equal(RestoreAction.Unchanged,
                ClipboardRestore.Plan(backup.Sequence, backup.Restorable, clip.SequenceNumber, stillOurs: null));
        }

        /// <summary>S0032 FP2-4: an empty clipboard is handed back empty, not left holding the selection.</summary>
        [Fact]
        public void An_empty_clipboard_is_restorable_as_empty()
        {
            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(new FakeClipboard());

            Assert.False(backup.HasContent);
            Assert.True(backup.WasEmpty);
            Assert.True(backup.Restorable);
            Assert.Empty(ClipboardHandler.RestorePayloads(backup));
            Assert.Equal(RestoreAction.Restore, ClipboardRestore.Plan(backup.Sequence, backup.Restorable, backup.Sequence + 2, stillOurs: true));
            Assert.Equal(RestoreAction.TakenOver, ClipboardRestore.Plan(backup.Sequence, backup.Restorable, backup.Sequence + 2, stillOurs: false));
        }

        [Fact]
        public void Markers_alone_are_neither_content_nor_empty()
        {
            var clip = new FakeClipboard().With(ClipboardFormats.ExcludeFromMonitor, new byte[] { 0 });

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.False(backup.WasEmpty);
            Assert.False(backup.Restorable);
        }

        [Fact]
        public void An_image_over_the_cap_is_skipped_rather_than_fatal()
        {
            var clip = new FakeClipboard()
                .With(Text, Unicode("caption"))
                .With(Dib, new byte[ClipboardHandler.MaxBackupImageBytes + 1]);

            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard(clip);

            Assert.False(backup.Unreadable);
            Assert.Null(backup.Image);
            Assert.Equal("caption", backup.Text);
        }

        [Fact]
        public void A_flip_over_an_unreadable_clipboard_stops_before_sending_a_single_key()
        {
            var sent = new List<KeyStroke>();
            var clip = new FakeClipboard { Locked = true }.With(Text, Unicode("the user's"));
            var handler = new ClipboardHandler(clip, keys => sent.AddRange(keys));

            Assert.Equal(ClipboardHandler.FlipResult.Failed, handler.FlipCase());
            Assert.Equal(ClipboardHandler.CaptureResult.Failed, handler.TakeSelection(out string text, out _));
            Assert.Equal("", text);
            Assert.Empty(sent);
        }
    }
}
