using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// Publishes the current layout code (EN, RU, DE, ZH, ..) to a small file so external tools can read
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
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
            "CyrFlip");

        private static readonly string FilePath = Path.Combine(Folder, "layout.txt");

        /// <summary>
        /// The active layout's KLID, published <b>beside</b> layout.txt rather than inside it. The
        /// extension reads the first four characters of layout.txt as the code, so appending anything
        /// to that file would break every already-installed copy of it - and the extension is published
        /// on its own clock, so old copies are the normal case, not the edge one. A second file is
        /// additive: an extension that does not know about it behaves exactly as before, and one that
        /// does gets the layout's own shade of the language colour.
        /// </summary>
        private static readonly string KlidPath = Path.Combine(Folder, "layout-klid.txt");

        public static void Publish(string code, string? klid = null)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, code);
                File.WriteAllText(KlidPath, klid ?? "");
            }
            catch
            {
                // Best-effort - never let publishing affect the app.
            }
        }
    }
}
