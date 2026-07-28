using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// Best-effort diagnostics for the translator, in the same MSIX-aware folder as <c>layout.txt</c>,
    /// <c>launcher.log</c> and <c>context-menu.log</c>.
    ///
    /// It exists because the feature spans three processes - CyrFlip, the Ollama server and the model
    /// - and when a chord produces nothing at all, the first question is which of them never got the
    /// work. Ollama's own log answers that only if the request reached it; this one covers everything
    /// before that.
    ///
    /// Records <b>no selection text and no translation</b> - only lengths, language codes, model
    /// names and outcomes. The whole point of a local translator is that the text stays local, and a
    /// log file is not local enough.
    /// </summary>
    internal static class TranslateLog
    {
        private static readonly object Lock = new object();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
            "CyrFlip", "translate.log");

        public static void Log(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " - " + message + Environment.NewLine);
                }
            }
            catch { /* diagnostics must never affect the app */ }
        }
    }
}
