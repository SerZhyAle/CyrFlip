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

        public static bool IsPackaged => _isPackaged.Value;

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
