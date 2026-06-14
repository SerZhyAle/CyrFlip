using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// Windows "start with Windows" toggle via the per-user Run key
    /// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
    ///
    /// CyrFlip runs in the interactive desktop session (it needs a global keyboard hook and
    /// access to the foreground window), so this is the correct mechanism - not a Windows
    /// service. A service runs in session 0 with no desktop, where WH_KEYBOARD_LL and the
    /// active-window APIs don't apply; AUTO/MANUAL service start types are therefore N/A here.
    ///
    /// MSIX (Store) builds don't use this: a packaged process's HKCU\..\Run write is virtualized
    /// and ignored at sign-in. There, autostart is declared in the package manifest as a
    /// windows.startupTask and toggled by the user in Windows "Startup apps" settings. The tray
    /// menu opens that settings page instead of flipping a checkbox - see <see cref="PackageInfo"/>.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CyrFlip";

        private static string ExeCommand => "\"" + Application.ExecutablePath + "\"";

        /// <summary>True when the OS owns the autostart toggle (MSIX startupTask), not this registry key.</summary>
        public static bool ManagedByWindows => PackageInfo.IsPackaged;

        public static bool IsEnabled
        {
            get
            {
                if (ManagedByWindows)
                    return false; // packaged: state lives in the startupTask, surfaced via Settings
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            if (ManagedByWindows)
                return; // packaged: never write the virtualized Run key; the manifest handles it
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, ExeCommand, RegistryValueKind.String);
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
