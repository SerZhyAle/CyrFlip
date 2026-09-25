using System;

namespace CyrFlip
{
    /// <summary>
    /// Best-effort diagnostics for the quick notes, in the same MSIX-aware folder as the other
    /// CyrFlip logs - and, like them, a one-line wrapper over <see cref="DiagnosticLog"/>, which
    /// owns the lock, the folder and the rotation.
    ///
    /// <para>It is a <b>separate file from the journal</b> and it never records a note: not a title,
    /// not a body, not a search query. The only reason it exists is the one question the user cannot
    /// answer from the outside - whether a record was skipped on replay, and how many.</para>
    /// </summary>
    internal static class QuickNotesLog
    {
        /// <summary>
        /// Deliberately not <c>quick-notes.log</c>: that name belongs to the encrypted journal, and
        /// a diagnostic file that sat beside it under a near-identical name is exactly how the two
        /// would one day be confused by a support bundle, a backup script or a person.
        /// </summary>
        public const string FileName = "quick-notes-diagnostics.log";

        public static void Log(string message)
            => DiagnosticLog.Append(DiagnosticLog.Path(FileName), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message);
    }
}
