using System;
using System.IO;

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
        private static readonly object Lock = new object();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
            "CyrFlip", "launcher.log");

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + message + Environment.NewLine);
                }
            }
            catch { /* diagnostics must never affect the app */ }
        }
    }
}
