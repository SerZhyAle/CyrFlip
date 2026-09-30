using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Where a CyrFlip shortcut lives.</summary>
    internal enum ShortcutPlace
    {
        /// <summary>The current user's Start menu (<c>%APPDATA%\Microsoft\Windows\Start Menu\Programs</c>).</summary>
        StartMenu,
        /// <summary>The current user's desktop.</summary>
        Desktop,
    }

    /// <summary>What <see cref="AppShortcuts.Decide"/> asks the startup pass to do with one shortcut.</summary>
    internal enum ShortcutAction
    {
        None,
        Create,
        Retarget,
    }

    /// <summary>
    /// The Start menu and desktop shortcuts of the unpackaged build. CyrFlip ships as a portable ZIP
    /// (and winget's portable install of that ZIP) with no installer, so nothing else ever puts it in
    /// the Start menu or on the desktop - the app does it itself, once, on its first run from a
    /// stable folder. After that the user owns them: a shortcut they deleted is never recreated by
    /// a start, only by the two checkboxes on the General settings page.
    ///
    /// The packaged (Store) build never touches either: Windows puts the package in the Start menu
    /// itself and removes it on uninstall, and a link written by the app would outlive the package.
    ///
    /// Two rules keep the links honest. A start from a <see cref="IsTransientLocation">transient
    /// folder</see> (a ZIP opened in Explorer runs from %TEMP%; a developer build from bin\) creates
    /// and changes nothing - the link would point at a file that is about to vanish. A start from
    /// anywhere else re-points an existing CyrFlip link at the running exe, the same way
    /// <see cref="Autostart.SyncToThisExe"/> keeps the Run entry current, so moving the folder does
    /// not leave a dead shortcut behind.
    /// </summary>
    internal static class AppShortcuts
    {
        private const string LinkName = "CyrFlip.lnk";
        private const string ExeName = "CyrFlip.exe";

        /// <summary>True when Windows owns the shortcuts (the MSIX build) and CyrFlip leaves them alone.</summary>
        public static bool ManagedByWindows => PackageInfo.IsPackaged;

        public static string PathFor(ShortcutPlace place) => System.IO.Path.Combine(
            Environment.GetFolderPath(place == ShortcutPlace.StartMenu
                ? Environment.SpecialFolder.Programs
                : Environment.SpecialFolder.DesktopDirectory),
            LinkName);

        public static bool Exists(ShortcutPlace place)
        {
            try { return File.Exists(PathFor(place)); }
            catch { return false; }
        }

        /// <summary>
        /// The startup pass: create both shortcuts on the first run, re-point existing ones at this
        /// exe on every later run. Never throws - a desktop that Controlled Folder Access protects
        /// must not stop CyrFlip from starting. The marker is set even when a write failed, so a
        /// refused folder is not asked again on every start.
        /// </summary>
        public static void SyncOnStartup(AppConfig config)
        {
            if (ManagedByWindows) return;
            try
            {
                string exe = Application.ExecutablePath;
                if (IsTransientLocation(exe, System.IO.Path.GetTempPath())) return;

                foreach (ShortcutPlace place in new[] { ShortcutPlace.StartMenu, ShortcutPlace.Desktop })
                {
                    try
                    {
                        string link = PathFor(place);
                        string? target = File.Exists(link) ? ReadTarget(link) : null;
                        switch (Decide(config.ShortcutsCreated, File.Exists(link), target, exe))
                        {
                            case ShortcutAction.Create:
                            case ShortcutAction.Retarget:
                                Write(link, exe);
                                break;
                        }
                    }
                    catch { }
                }
                if (!config.ShortcutsCreated) config.SaveShortcutsCreated();
            }
            catch { }
        }

        /// <summary>
        /// The pure decision behind <see cref="SyncOnStartup"/> for one shortcut. A missing link is
        /// created only on the first run. An existing one is re-pointed only when it is ours - its
        /// target is a <c>CyrFlip.exe</c>, or it could not be read at all - and names another file;
        /// a link the user aimed somewhere else under our name is left alone.
        /// </summary>
        internal static ShortcutAction Decide(bool firstRunDone, bool linkExists, string? linkTarget, string exePath)
        {
            if (!linkExists) return firstRunDone ? ShortcutAction.None : ShortcutAction.Create;
            if (string.IsNullOrEmpty(linkTarget)) return ShortcutAction.None;
            if (!string.Equals(System.IO.Path.GetFileName(linkTarget), ExeName, StringComparison.OrdinalIgnoreCase))
                return ShortcutAction.None;
            return SamePath(linkTarget!, exePath) ? ShortcutAction.None : ShortcutAction.Retarget;
        }

        /// <summary>
        /// A folder a shortcut must never point into: the temp folder (Explorer runs an exe from
        /// inside a ZIP by extracting it there) and a build output folder (<c>\bin\Debug\</c>,
        /// <c>\bin\Release\</c>).
        /// </summary>
        internal static bool IsTransientLocation(string exePath, string tempPath)
        {
            if (string.IsNullOrEmpty(exePath)) return true;
            string full = Normalize(exePath);
            if (!string.IsNullOrEmpty(tempPath))
            {
                string temp = Normalize(tempPath).TrimEnd('\\') + "\\";
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return full.IndexOf(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) >= 0
                || full.IndexOf(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The settings checkbox: create the shortcut to the running exe, or delete it.</summary>
        public static void Set(ShortcutPlace place, bool present)
        {
            if (ManagedByWindows) return;
            string link = PathFor(place);
            if (present) Write(link, Application.ExecutablePath);
            else if (File.Exists(link)) File.Delete(link);
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private static string Normalize(string path) => System.IO.Path.GetFullPath(path.Replace('/', '\\'));

        // ---- The .lnk itself: CLSID_ShellLink through IShellLinkW + IPersistFile. ----

        private static void Write(string linkPath, string exePath)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(linkPath)!);
            var link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(exePath);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exePath) ?? "");
                link.SetIconLocation(exePath, 0);
                link.SetDescription("CyrFlip");
                ((IPersistFile)link).Save(linkPath, true);
            }
            finally { Marshal.ReleaseComObject(link); }
        }

        private static string? ReadTarget(string linkPath)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0); // STGM_READ
                var path = new StringBuilder(1024);
                link.GetPath(path, path.Capacity, IntPtr.Zero, 0x4); // SLGP_RAWPATH: no resolve, no search
                return path.Length > 0 ? Environment.ExpandEnvironmentVariables(path.ToString()) : null;
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        // Method order matches shobjidl.h exactly (see the CLAUDE.md "COM vtable gotchas" note).
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
