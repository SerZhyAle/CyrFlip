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
    /// MSIX (Store) builds don't write this: a packaged process's HKCU\..\Run write is virtualized
    /// and ignored at sign-in. There, autostart is declared in the package manifest as a
    /// windows.startupTask and switched by the user in Windows "Startup apps" settings - so
    /// <see cref="Set"/> is a no-op and the settings checkbox opens that page instead. The state is
    /// still read (<see cref="StartupTaskEnabled"/>), so the checkbox tells the truth about what
    /// Windows currently does - see <see cref="PackageInfo"/>.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CyrFlip";

        /// <summary>TaskId of the manifest's windows.startupTask (msix/AppxManifest.xml).</summary>
        private const string StartupTaskId = "CyrFlipStartup";

        private const string StartupTaskStateKey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\";

        // Windows.ApplicationModel.StartupTaskState, as Windows writes it into the State value.
        private const int StateEnabled = 2;
        private const int StateEnabledByPolicy = 4;

        private static string ExeCommand => "\"" + Application.ExecutablePath + "\"";

        /// <summary>True when the OS owns the autostart toggle (MSIX startupTask), not this registry key.</summary>
        public static bool ManagedByWindows => PackageInfo.IsPackaged;

        public static bool IsEnabled
        {
            get
            {
                if (ManagedByWindows)
                    return StartupTaskEnabled(); // packaged: the startupTask is the truth, we only read it
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) != null;
            }
        }

        /// <summary>
        /// Reads the packaged build's startupTask state. Windows mirrors it per user under
        /// SystemAppData\{PackageFamilyName}\{TaskId}\State, holding a StartupTaskState value
        /// (0 Disabled, 1 DisabledByUser, 2 Enabled, 3 DisabledByPolicy, 4 EnabledByPolicy).
        ///
        /// This is a read, not the toggle: the state can only be changed by the user in Windows
        /// "Startup apps" - once they have touched it, even the WinRT RequestEnableAsync is refused.
        /// Reading it is what keeps the checkbox honest; without it the packaged build always
        /// reported "off", even right after the user turned autostart on.
        /// </summary>
        private static bool StartupTaskEnabled()
        {
            try
            {
                string family = PackageInfo.FamilyName;
                if (family.Length == 0) return false;
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    StartupTaskStateKey + family + "\\" + StartupTaskId, writable: false);
                return IsEnabledState(key?.GetValue("State"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The State value → on/off. Null (no record at all) means the task has never been touched,
        /// i.e. the manifest default, which is off. Anything that isn't a DWORD is treated the same
        /// way rather than guessed at.
        /// </summary>
        internal static bool IsEnabledState(object? state) =>
            state is int value && (value == StateEnabled || value == StateEnabledByPolicy);

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
