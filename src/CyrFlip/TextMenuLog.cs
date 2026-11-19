using System;
using System.Collections.Generic;
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
        public static void Log(string message)
            => DiagnosticLog.Append(DiagnosticLog.Path("context-menu.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message);

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

        /// <summary>
        /// Each selection source's own verdict for one menu opening (S0133), in asking order:
        /// "edit@pointer=Unknown, ia2@pointer=Present(42), uia@focus=Absent". Counts only - a number
        /// in parentheses when a source read text, never the text itself, so the log's "no text"
        /// promise to the support bundle stays true. "n/a" marks an opening whose probe did not
        /// finish collecting: there is nothing honest to say about why a command was greyed.
        /// </summary>
        public static string DescribeSources(SelectionSnapshot selection)
        {
            IReadOnlyList<SelectionSourceAnswer>? answers = selection.SourceAnswers;
            if (answers == null || answers.Count == 0) return "n/a";
            var sb = new StringBuilder();
            foreach (SelectionSourceAnswer answer in answers)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(answer.Source).Append('=').Append(answer.State.ToString());
                if (answer.TextChars.HasValue) sb.Append('(').Append(answer.TextChars.Value).Append(')');
            }
            return sb.ToString();
        }
    }
}
