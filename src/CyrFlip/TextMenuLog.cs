using System;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Best-effort diagnostics for the text context menu, in the same MSIX-aware folder as
    /// <c>layout.txt</c>, the caret diagnostics and <c>launcher.log</c>.
    ///
    /// It exists for one question that cannot be answered from the outside: a menu item synthesizes
    /// Ctrl+C/Ctrl+X/Ctrl+V into <b>the foreground window</b>, so when "nothing happens" the only
    /// thing worth knowing is which window that actually was at the instant we sent. Like every log
    /// here it records <b>no keystrokes and no clipboard content</b> - only window identity.
    /// </summary>
    internal static class TextMenuLog
    {
        private static readonly string FilePath = DiagnosticLog.Path("context-menu.log");

        public static void Log(string message)
            => DiagnosticLog.Append(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message);

        /// <summary>"0x1234 'Notepad' pid=42" - enough to tell the user's window from one of ours.</summary>
        public static string Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "none";
            try
            {
                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                GetWindowThreadProcessId(hwnd, out uint pid);
                return "0x" + hwnd.ToInt64().ToString("X") + " '" + cls + "' pid=" + pid;
            }
            catch { return "0x" + hwnd.ToInt64().ToString("X") + " <unreadable>"; }
        }
    }
}
