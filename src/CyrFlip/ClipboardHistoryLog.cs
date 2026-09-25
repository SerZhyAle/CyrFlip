using System;

namespace CyrFlip
{
    /// <summary>
    /// Counts for the clipboard history - how many journal lines failed to replay, how many
    /// privacy-marked copies were skipped - and nothing else: never a payload, never a source window.
    /// A one-line wrapper over <see cref="DiagnosticLog"/>, like <see cref="QuickNotesLog"/>.
    /// </summary>
    internal static class ClipboardHistoryLog
    {
        /// <summary>Deliberately not <c>clipboard-history.log</c>: that name is the encrypted history itself.</summary>
        public const string FileName = "clipboard-history-diagnostics.log";

        public static void Log(string message)
        {
            try { DiagnosticLog.Append(DiagnosticLog.Path(FileName), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message); }
            catch { /* diagnostics must never affect the history */ }
        }
    }
}
