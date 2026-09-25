using System;
using System.Text;

namespace CyrFlip
{
    internal sealed class ClipboardHistoryEntry
    {
        /// <summary>How much of the text the strip and the search list ever look at (spec S0005 CH-4).</summary>
        public const int PreviewLength = 1024;

        private string? _preview;

        public string Uuid { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Text { get; set; } = "";
        /// <summary>Process name of the window that owned the clipboard when the text was captured (e.g. "chrome"). Empty if unknown.</summary>
        public string SourceApp { get; set; } = "";
        /// <summary>Title of the source window when captured. Empty if unknown.</summary>
        public string SourceTitle { get; set; } = "";
        public bool IsPinned { get; set; }
        /// <summary>True only for clipboard content observed during this CyrFlip session.</summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// The first <see cref="PreviewLength"/> characters on one line - what the strip draws and
        /// measures. Computed once per entry: the strip repaints on every copy and every resize step,
        /// on the thread that also runs both low-level hooks, and used to normalize and measure up to
        /// 64 K characters per visible cell each time.
        /// </summary>
        public string Preview => _preview ??= MakePreview(Text);

        /// <summary>Line breaks collapsed to one space each (<c>\r\n</c> counts once), then trimmed.</summary>
        internal static string MakePreview(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int length = Math.Min(text!.Length, PreviewLength);
            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = text[i];
                if (c == '\r' && i + 1 < length && text[i + 1] == '\n') continue; // the \n that follows speaks for both
                result.Append(IsLineBreak(c) ? ' ' : c);
            }
            return result.ToString().Trim();
        }

        private static bool IsLineBreak(char c) =>
            c == '\r' || c == '\n' || c == '\u0085' || c == (char)0x2028 || c == (char)0x2029 || c == '\v' || c == '\f';
    }
}
