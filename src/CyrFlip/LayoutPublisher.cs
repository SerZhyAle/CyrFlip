using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// Publishes the current layout code (EN/RU/UK/…) to a small file so external tools can read
    /// it — chiefly the companion VS Code extension, which can place the marker exactly at the
    /// editor caret (something the external UIA overlay can't do reliably in Monaco/Electron).
    ///
    /// File: %LOCALAPPDATA%\CyrFlip\layout.txt — a single line with the code.
    /// </summary>
    internal static class LayoutPublisher
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyrFlip", "layout.txt");

        public static void Publish(string code)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, code);
            }
            catch
            {
                // Best-effort — never let publishing affect the app.
            }
        }
    }
}
