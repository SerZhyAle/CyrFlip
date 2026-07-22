using System;
using System.Collections.Generic;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Detects whether the foreground window belongs to a remote-desktop client (mstsc etc.).
    ///
    /// Why this matters: when CyrFlip runs on BOTH ends of an RDP session, the local instance's
    /// hook fires first and mangles the hotkey (it swallows the trigger key and injects Ctrl+C,
    /// which - with the Shift the user is still holding - leaks into the remote as Ctrl+Shift+C),
    /// so the CyrFlip inside the remote session never sees the real chord. If the local instance
    /// yields whenever an RDP client is focused, the untouched key travels to the remote session
    /// where that machine's CyrFlip handles it. Each machine then serves only its own windows.
    /// </summary>
    internal static class RemoteDesktop
    {
        // exe base names (no extension) of remote-desktop clients that forward keystrokes to a
        // remote session. Extend this list for other tools with the same double-instance issue.
        private static readonly HashSet<string> ClientProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mstsc",    // classic Remote Desktop Connection
            "msrdc",    // Windows App / AVD / Windows 365 client
            "msrdcw",   // Remote Desktop (Store) window host
        };

        /// <summary>True when the currently focused window is an RDP/remote-desktop client.</summary>
        public static bool IsClientForeground()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return false;

                string? name = TryGetProcessBaseName(pid);
                return name != null && ClientProcesses.Contains(name);
            }
            catch { return false; }
        }

        private static string? TryGetProcessBaseName(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size))
                    return null;
                string path = sb.ToString();
                int slash = path.LastIndexOf('\\');
                string file = slash >= 0 ? path.Substring(slash + 1) : path;
                int dot = file.LastIndexOf('.');
                return dot >= 0 ? file.Substring(0, dot) : file;
            }
            finally { CloseHandle(h); }
        }
    }
}
