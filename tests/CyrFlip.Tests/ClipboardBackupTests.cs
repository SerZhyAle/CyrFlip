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
}
