using System;

namespace CyrFlip
{
    internal sealed class ClipboardHistoryEntry
    {
        public string Uuid { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Text { get; set; } = "";
        public bool IsPinned { get; set; }
        /// <summary>True only for clipboard content observed during this CyrFlip session.</summary>
        public bool IsCurrent { get; set; }
    }
}
