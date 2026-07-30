using System;

namespace CyrFlip
{
    /// <summary>
    /// Best-effort diagnostics for the launcher: launch attempts, their source and failure reasons.
    /// Lives in the same MSIX-aware folder as <c>layout.txt</c> and the caret diagnostics
    /// (<c>%LOCALAPPDATA%\CyrFlip</c>, or <c>%ProgramData%\CyrFlip</c> when packaged) - no second
    /// application folder. Deliberately never logs the yt-dlp link, clipboard content or keystrokes
    /// (spec §9); a failure to write is swallowed.
    /// </summary>
    internal static class LauncherLog
    {
        private static readonly string FilePath = DiagnosticLog.Path("launcher.log");

        public static void Log(string message)
            => DiagnosticLog.Append(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + message);
    }
}
