using System;

namespace CyrFlip
{
    /// <summary>Rules shared by the history-search UI and its tests.</summary>
    internal static class ClipboardHistorySearch
    {
        public const int MinimumQueryLength = 3;

        public static bool IsReady(string? query) => (query?.Trim().Length ?? 0) >= MinimumQueryLength;

        public static bool Matches(ClipboardHistoryEntry entry, string? query)
        {
            string normalizedQuery = query?.Trim() ?? string.Empty;
            return normalizedQuery.Length >= MinimumQueryLength
                && entry.Text.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
