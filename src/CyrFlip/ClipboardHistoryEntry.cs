using System;

namespace CyrFlip
{
    internal sealed class ClipboardHistoryEntry
    {
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
    }
}
