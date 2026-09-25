using System;
using System.Collections.Generic;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// An editor's "no selection - copy the whole line" is not a selection (ticket S0009, FP-4): the
    /// classifier over fake clipboards, one per editor family.
    /// </summary>
    public sealed class LineCopyMarkersTests
    {
        private static readonly uint MsDev = ClipboardFormats.Register(LineCopyMarkers.MsDevLineSelect);
        private static readonly uint VisualStudio = ClipboardFormats.Register(LineCopyMarkers.VisualStudioLineCopy);
        private static readonly uint Chromium = ClipboardFormats.Register(LineCopyMarkers.ChromiumCustomData);

        private static FakeClipboard WithLine() =>
            new FakeClipboard().With(WindowInterop.CF_UNICODETEXT, Win32Clipboard.UnicodeBytes("    return x;\r\n"));

        /// <summary>
        /// Chromium's custom-data pickle as it lands on the clipboard: a payload-size header, a count,
        /// then UTF-16 type/value pairs, each field padded to four bytes.
        /// </summary>
        private static byte[] Pickle(params KeyValuePair<string, string>[] entries)
        {
            var body = new List<byte>();
            void Int(int value) => body.AddRange(BitConverter.GetBytes(value));
            void String16(string value)
            {
                Int(value.Length);
                body.AddRange(Encoding.Unicode.GetBytes(value));
                while (body.Count % 4 != 0) body.Add(0);
            }

            Int(entries.Length);
            foreach (KeyValuePair<string, string> entry in entries)
            {
                String16(entry.Key);
                String16(entry.Value);
            }
            var pickle = new List<byte>(BitConverter.GetBytes(body.Count));
            pickle.AddRange(body);
            return pickle.ToArray();
        }

        private static KeyValuePair<string, string> Entry(string type, string data) => new KeyValuePair<string, string>(type, data);

        [Fact]
        public void Scintilla_and_old_visual_studio_mark_a_line_copy()
        {
            Assert.True(LineCopyMarkers.IsLineCopy(WithLine().With(MsDev, new byte[0])));
        }

        [Fact]
        public void Visual_studio_marks_a_line_copy()
        {
            Assert.True(LineCopyMarkers.IsLineCopy(WithLine().With(VisualStudio, new byte[] { 1 })));
        }

        [Fact]
        public void Vs_code_says_so_in_its_editor_data()
        {
            byte[] data = Pickle(Entry(LineCopyMarkers.VsCodeEditorData,
                "{\"version\":1,\"isFromEmptySelection\":true,\"multicursorText\":null,\"mode\":\"csharp\"}"));

            Assert.True(LineCopyMarkers.IsLineCopy(WithLine().With(Chromium, data)));
        }

        [Fact]
        public void A_real_vs_code_selection_is_accepted()
        {
            byte[] data = Pickle(Entry(LineCopyMarkers.VsCodeEditorData,
                "{\"version\":1,\"isFromEmptySelection\":false,\"multicursorText\":null,\"mode\":\"csharp\"}"));

            Assert.False(LineCopyMarkers.IsLineCopy(WithLine().With(Chromium, data)));
        }

        [Fact]
        public void Another_web_pages_custom_data_is_not_a_marker()
        {
            // The flag alone, under some other page's type, proves nothing about an editor.
            byte[] data = Pickle(Entry("application/x-some-app", "{\"isFromEmptySelection\":true}"));

            Assert.False(LineCopyMarkers.IsLineCopy(WithLine().With(Chromium, data)));
        }

        [Fact]
        public void A_plain_copy_carries_no_marker()
        {
            Assert.False(LineCopyMarkers.IsLineCopy(WithLine()));
        }

        [Fact]
        public void Unreadable_custom_data_is_not_a_marker()
        {
            Assert.False(LineCopyMarkers.IsLineCopy(WithLine().Failing(Chromium)));
            Assert.False(LineCopyMarkers.IsVsCodeEmptySelection(null));
            Assert.False(LineCopyMarkers.IsVsCodeEmptySelection(new byte[] { 1, 2, 3 }));
        }
    }
}
