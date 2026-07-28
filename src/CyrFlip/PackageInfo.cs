using System;

namespace CyrFlip
{
    /// <summary>
    /// Tells whether CyrFlip is running inside an MSIX package (Microsoft Store / sideloaded
    /// .msix) or as a plain unpackaged exe. The same binary ships both ways, so a few
    /// behaviours differ:
    ///   - autostart: unpackaged uses HKCU\..\Run; packaged uses the manifest's startupTask,
    ///     which the user toggles in Windows "Startup apps" settings (see <see cref="Autostart"/>);
    ///   - <see cref="LayoutPublisher"/> writes layout.txt to %ProgramData% when packaged, because
    ///     %LOCALAPPDATA% is virtualized into the package container and the VS Code extension
    ///     (an unpackaged process) wouldn't see it there.
    ///
    /// Detected via GetCurrentPackageFullName: it returns APPMODEL_ERROR_NO_PACKAGE for an
    /// unpackaged process and something else (ERROR_INSUFFICIENT_BUFFER) when packaged.
    /// </summary>
    internal static class PackageInfo
    {
        private static readonly Lazy<bool> _isPackaged = new Lazy<bool>(Detect);
        private static readonly Lazy<string> _familyName = new Lazy<string>(ReadFamilyName);

        public static bool IsPackaged => _isPackaged.Value;

        /// <summary>
        /// The package family name ("SZA.CyrFlip_fdk7e19xt9z9j"), or an empty string when the
        /// process is unpackaged. Windows keys the package's per-user state by this name - which
        /// is how <see cref="Autostart"/> reads the startupTask's on/off state.
        /// </summary>
        public static string FamilyName => _familyName.Value;

        private static string ReadFamilyName()
        {
            if (!IsPackaged) return string.Empty;
            try
            {
                int length = 0;
                int rc = WindowInterop.GetCurrentPackageFamilyName(ref length, null);
                if (rc != WindowInterop.ERROR_INSUFFICIENT_BUFFER || length <= 0) return string.Empty;
                var buffer = new System.Text.StringBuilder(length);
                rc = WindowInterop.GetCurrentPackageFamilyName(ref length, buffer);
                return rc == 0 ? buffer.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool Detect()
        {
            try
            {
                int length = 0;
                int rc = WindowInterop.GetCurrentPackageFullName(ref length, null);
                return rc != WindowInterop.APPMODEL_ERROR_NO_PACKAGE;
            }
            catch
            {
                // API is present on Win8+, so this shouldn't happen on supported OSes; if it
                // somehow does, assume unpackaged (the conservative, back-compatible default).
                return false;
            }
        }
    }
}
