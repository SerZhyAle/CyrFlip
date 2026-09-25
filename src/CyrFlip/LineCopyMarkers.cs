using System.Text;
using System.Text.RegularExpressions;

namespace CyrFlip
{
    /// <summary>
    /// "The editor copied the whole line because nothing was selected" (ticket S0009, FP-4).
    ///
    /// <para>VS Code (<c>editor.emptySelectionClipboard</c>, on by default), Visual Studio, JetBrains
    /// IDEs and Notepad++/Scintilla answer Ctrl+C with no selection by copying the <b>current line</b>.
    /// To the copy half of a flip that looks exactly like a selection, so a chord pressed with nothing
    /// selected converted <c>line\r\n</c> and pasted it at the caret - a converted duplicate spliced
    /// into the middle of the line. Each of those editors puts a marker format beside such a copy, and
    /// the capture treats a marked copy as "nothing selected".</para>
    ///
    /// <para>JetBrains is not listed yet: its marker has to be identified live with a clipboard viewer
    /// first (open decision 2 of the ticket) - a guessed name would either never match or, worse,
    /// match an ordinary copy.</para>
    /// </summary>
    internal static class LineCopyMarkers
    {
        /// <summary>Scintilla (Notepad++) and the Visual Studio of old.</summary>
        public const string MsDevLineSelect = "MSDEVLineSelect";

        /// <summary>Visual Studio 2010 and later.</summary>
        public const string VisualStudioLineCopy = "VisualStudioEditorOperationsLineCutCopyClipboardTag";

        /// <summary>
        /// Chromium's pickled map of custom MIME types - where VS Code (and every Electron editor built
        /// on Monaco) keeps <c>vscode-editor-data</c>, a JSON object with <c>isFromEmptySelection</c>.
        /// </summary>
        public const string ChromiumCustomData = "Chromium Web Custom MIME Data Format";

        /// <summary>The Monaco custom type inside <see cref="ChromiumCustomData"/>.</summary>
        public const string VsCodeEditorData = "vscode-editor-data";

        /// <summary>How much of the Chromium custom data is worth reading: the marker is a short JSON object.</summary>
        internal const int MaxCustomDataBytes = 64 * 1024;

        private static readonly uint MsDevLineSelectFormat = ClipboardFormats.Register(MsDevLineSelect);
        private static readonly uint VisualStudioLineCopyFormat = ClipboardFormats.Register(VisualStudioLineCopy);
        private static readonly uint ChromiumCustomDataFormat = ClipboardFormats.Register(ChromiumCustomData);

        private static readonly Regex EmptySelectionFlag = new Regex("\"isFromEmptySelection\"\\s*:\\s*true", RegexOptions.CultureInvariant);

        /// <summary>
        /// True when the clipboard <paramref name="reader"/> has open carries a line-copy marker.
        /// Costs one availability check per marker; the Chromium data is read only when present.
        /// </summary>
        public static bool IsLineCopy(IClipboardReader reader)
        {
            if (reader.IsAvailable(MsDevLineSelectFormat) || reader.IsAvailable(VisualStudioLineCopyFormat))
                return true;
            if (!reader.IsAvailable(ChromiumCustomDataFormat))
                return false;
            return reader.Read(ChromiumCustomDataFormat, MaxCustomDataBytes, out byte[]? data) == ClipboardRead.Ok
                && IsVsCodeEmptySelection(data);
        }

        /// <summary>
        /// Does Chromium's custom data say "copied from an empty selection"? The data is a
        /// <c>base::Pickle</c> of UTF-16 type/value pairs, each field aligned to four bytes, so decoding
        /// the whole block as UTF-16 leaves every string intact between the length fields - no parser
        /// of the pickle format is needed to find the type name and the flag.
        /// </summary>
        internal static bool IsVsCodeEmptySelection(byte[]? data)
        {
            if (data == null || data.Length < 4) return false;
            string decoded = Encoding.Unicode.GetString(data, 0, data.Length & ~1);
            return decoded.IndexOf(VsCodeEditorData, System.StringComparison.Ordinal) >= 0
                && EmptySelectionFlag.IsMatch(decoded);
        }
    }
}
