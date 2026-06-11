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
    /// access to the foreground window), so this is the correct mechanism — not a Windows
    /// service. A service runs in session 0 with no desktop, where WH_KEYBOARD_LL and the
    /// active-window APIs don't apply; AUTO/MANUAL service start types are therefore N/A here.
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CyrFlip";

        private static string ExeCommand => "\"" + Application.ExecutablePath + "\"";

        public static bool IsEnabled
        {
            get
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string s
                    && string.Equals(s, ExeCommand, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, ExeCommand, RegistryValueKind.String);
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
