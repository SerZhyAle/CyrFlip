using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// The one decision of where CyrFlip keeps its per-user files: the layout signal
    /// (<c>LAYOUT-SIGNAL</c> rule 1), every diagnostic log, the support-bundle archives and the quick-notes
    /// journal (ticket S0016).
    ///
    /// <para>Unpackaged: <c>%LOCALAPPDATA%\CyrFlip</c>. Packaged (Store): the package's own per-user folder,
    /// <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\LocalCache\Local\CyrFlip</c>, built from the
    /// <b>unvirtualized</b> <c>%LOCALAPPDATA%</c> string and written explicitly. That is exactly where MSIX
    /// puts the package's virtualized <c>%LOCALAPPDATA%</c> writes, so no second redirection applies, and
    /// an unpackaged reader - the VS Code extension, a mail client - finds the files at the same path.</para>
    ///
    /// <para>Up to S0016 the packaged build used <c>%ProgramData%\CyrFlip</c>, which every account on the
    /// machine shares: the second user's writes failed on the first user's files, the second user replayed
    /// the first user's notes journal, and anyone could read anyone's logs. That folder survives only as
    /// <see cref="LegacyShared"/> - the deprecated mirror of the two layout files and the source of the
    /// one-time <see cref="DataFolderMigration"/>.</para>
    /// </summary>
    internal static class DataFolder
    {
        internal const string ProductName = "CyrFlip";

        /// <summary>The live folder for this process.</summary>
        internal static readonly string Current = For(PackageInfo.IsPackaged, UnvirtualizedLocalAppData(), PackageInfo.FamilyName);

        /// <summary>
        /// <c>%ProgramData%\CyrFlip</c> for a packaged process, null otherwise: where a packaged CyrFlip
        /// before S0016 kept everything, and where <c>LAYOUT-SIGNAL</c> 1.1 keeps the deprecated mirror.
        /// </summary>
        internal static readonly string? LegacyShared = PackageInfo.IsPackaged
            ? LegacySharedFor(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            : null;

        /// <summary>
        /// The folder for an install mode. A packaged process whose family name could not be read falls
        /// back to <c>%LOCALAPPDATA%\CyrFlip</c>: still per user (virtualized into the package), just
        /// out of the extension's sight - never back to the machine-wide folder.
        /// </summary>
        internal static string For(bool packaged, string localAppData, string? familyName)
            => packaged && !string.IsNullOrEmpty(familyName)
                ? Path.Combine(localAppData, "Packages", familyName, "LocalCache", "Local", ProductName)
                : Path.Combine(localAppData, ProductName);

        internal static string LegacySharedFor(string programData) => Path.Combine(programData, ProductName);

        /// <summary>
        /// The real <c>%LOCALAPPDATA%</c> path. The environment string is never rewritten by MSIX (only the
        /// file-system calls are redirected), so it is what a foreign process means by the same name.
        /// </summary>
        private static string UnvirtualizedLocalAppData()
        {
            string? env = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            return !string.IsNullOrEmpty(env)
                ? env!
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
    }
}
