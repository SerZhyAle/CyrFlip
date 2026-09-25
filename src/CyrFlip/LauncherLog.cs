using System;

namespace CyrFlip
{
    /// <summary>
    /// Best-effort diagnostics for the launcher: launch attempts, their source and failure reasons.
    /// Lives in the same MSIX-aware folder as <c>layout.txt</c> and the caret diagnostics
    /// (<see cref="DataFolder"/>: <c>%LOCALAPPDATA%\CyrFlip</c>, or the package's per-user folder) - no second
    /// application folder. Deliberately never logs the yt-dlp link, clipboard content or keystrokes
    /// (spec §9), nor a scenario's path or arguments (ticket S0010 TD-1 - this file goes into the log
    /// bundle, and arguments are where tokens live; see <see cref="LauncherExecution.LaunchedLine"/>);
    /// a failure to write is swallowed.
    /// </summary>
    internal static class LauncherLog
    {
        public static void Log(string message)
            => DiagnosticLog.Append(DiagnosticLog.Path("launcher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + message);
    }
}
