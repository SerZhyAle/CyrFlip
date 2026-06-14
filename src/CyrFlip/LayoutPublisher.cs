using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// Publishes the current layout code (EN/RU/UK/..) to a small file so external tools can read
    /// it - chiefly the companion VS Code extension, which can place the marker exactly at the
    /// editor caret (something the external UIA overlay can't do reliably in Monaco/Electron).
    ///
    /// Unpackaged: %LOCALAPPDATA%\CyrFlip\layout.txt.
    /// MSIX (Store): %ProgramData%\CyrFlip\layout.txt - because under MSIX a write to
    /// %LOCALAPPDATA% is virtualized into the package container, where the (unpackaged) VS Code
    /// extension can't find it. %ProgramData% is not virtualized, so both sides agree on the path.
    /// The extension checks both locations (see vscode-extension/src/extension.ts).
    /// </summary>
    internal static class LayoutPublisher
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
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
                // Best-effort - never let publishing affect the app.
            }
        }
    }
}
