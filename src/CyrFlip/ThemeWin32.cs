using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// What the control tree cannot paint: the title bar, scroll bars and borders, native menus. All of
    /// it is undocumented or version-dependent Windows behaviour, so every call is guarded and a refusal
    /// degrades to "looks as it did before" - never to an exception. The same discipline as
    /// <see cref="UiaCaretCom"/> and <see cref="LauncherJumpList"/>; only the live check
    /// (<c>tools/uitest/Test-DarkTheme.ps1</c>) can prove any of it landed.
    /// </summary>
    internal static class ThemeWin32
    {
        /// <summary>Windows 10 1809, where the dark title bar and the <c>DarkMode_*</c> themes first appear.</summary>
        private const int DarkModeBuild = 17763;

        /// <summary>Windows 10 1903, where uxtheme ordinal 135 became <c>SetPreferredAppMode</c>.</summary>
        private const int AppModeBuild = 18362;

        private const int AppModeDefault = 0;
        private const int AppModeForceDark = 2;
        private const int AppModeForceLight = 3;

        public const string ExplorerDark = "DarkMode_Explorer";
        public const string EditDark = "DarkMode_CFD";
        public const string HeaderDark = "DarkMode_ItemsView";

        private static int Build => Environment.OSVersion.Platform == PlatformID.Win32NT ? Environment.OSVersion.Version.Build : 0;

        public static bool DarkModeAvailable => Environment.OSVersion.Version.Major >= 10 && Build >= DarkModeBuild;

        /// <summary>
        /// The window's title bar. Attribute 20 first, 19 for the Windows 10 builds that knew it by that
        /// number; a visible window is told its frame changed, or the caption keeps its old colour until
        /// the next activation.
        /// </summary>
        public static void SetTitleBar(IntPtr hwnd, bool dark, bool visible)
        {
            if (hwnd == IntPtr.Zero || !DarkModeAvailable) return;
            try
            {
                int value = dark ? 1 : 0;
                if (WindowInterop.DwmSetWindowAttribute(hwnd, WindowInterop.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                    WindowInterop.DwmSetWindowAttribute(hwnd, WindowInterop.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
                if (visible)
                    WindowInterop.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER
                        | WindowInterop.SWP_NOACTIVATE | WindowInterop.SWP_FRAMECHANGED);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>A control's visual theme; <paramref name="subApp"/> null puts the default back.</summary>
        public static void SetTheme(IntPtr hwnd, string? subApp)
        {
            if (hwnd == IntPtr.Zero || !DarkModeAvailable) return;
            try { WindowInterop.SetWindowTheme(hwnd, subApp, null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>The header of a details-view list: its empty tail is drawn by the header control itself.</summary>
        public static void SetListViewHeaderTheme(ListView list, string? subApp)
        {
            if (!list.IsHandleCreated) return;
            try
            {
                IntPtr header = WindowInterop.SendMessage(list.Handle, WindowInterop.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                SetTheme(header, subApp);
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>The item tooltip of a list (<c>ShowItemToolTips</c>) is a window of its own too.</summary>
        public static void SetListViewToolTipTheme(ListView list, string? subApp)
        {
            if (!list.IsHandleCreated) return;
            try
            {
                IntPtr tip = WindowInterop.SendMessage(list.Handle, WindowInterop.LVM_GETTOOLTIPS, IntPtr.Zero, IntPtr.Zero);
                SetTheme(tip, subApp);
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>The combo box's drop-down list is a window of its own, with its own scroll bar.</summary>
        public static void SetComboListTheme(ComboBox combo, string? subApp)
        {
            if (!combo.IsHandleCreated) return;
            try
            {
                var info = new WindowInterop.COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(WindowInterop.COMBOBOXINFO)) };
                if (WindowInterop.GetComboBoxInfo(combo.Handle, ref info))
                    SetTheme(info.hwndList, subApp);
            }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>
        /// How the process's native popup menus are drawn. Dark and light are forced rather than left to
        /// Windows, because the user may have picked a mode that differs from Windows' own; high contrast
        /// hands it back to the system.
        /// </summary>
        public static void SetMenuMode(ThemeKind kind)
        {
            if (!DarkModeAvailable || Build < AppModeBuild) return;
            try
            {
                WindowInterop.SetPreferredAppMode(kind == ThemeKind.Dark ? AppModeForceDark
                    : kind == ThemeKind.Light ? AppModeForceLight : AppModeDefault);
                WindowInterop.FlushMenuThemes();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
