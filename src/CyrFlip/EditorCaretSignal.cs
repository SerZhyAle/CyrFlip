using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// "The editor is drawing the marker - stay out of the way."
    ///
    /// <para>Inside a VS Code editor two markers used to appear at one caret: the companion extension
    /// draws its own at Monaco's caret (the only way to be exact there), and IAccessible2 hands the app
    /// that same caret, so <see cref="CaretOverlay"/> drew a second one a few pixels away.</para>
    ///
    /// <para>The extension therefore publishes <c>editor-caret.txt</c> beside <c>layout.txt</c> while it
    /// is drawing, and this class is the app's half: <see cref="ShouldYield"/> is true when that file is
    /// fresh <b>and</b> the foreground window belongs to a VS Code-family editor. Both halves are needed.
    /// The file alone would leave the overlay hidden for a beat after the user alt-tabs away from VS Code
    /// - the extension only learns it lost the focus a poll later - and the process alone would hide the
    /// marker in VS Code's chat box and terminal, where the extension cannot draw at all.</para>
    ///
    /// <para>The signal is time-limited by design (the extension refreshes it only while there has been
    /// recent editor activity), because VS Code's API cannot tell whether the focus is in the editor or
    /// in the chat: <c>activeTextEditor</c> keeps pointing at the last editor either way. So "recently
    /// typing in the editor" is the honest claim, and once the user moves to the chat box the claim
    /// lapses and the overlay comes back.</para>
    /// </summary>
    internal static class EditorCaretSignal
    {
        /// <summary>How new the signal file must be for the extension's claim to count.</summary>
        internal const int FreshMs = 1500;

        /// <summary>The caret tracker ticks every ~90 ms; the file is not stat-ed more often than this.</summary>
        private const int PollMs = 300;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData
                : Environment.SpecialFolder.LocalApplicationData),
            "CyrFlip", "editor-caret.txt");

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _checkedMs = -PollMs;
        private static bool _fresh;

        /// <summary>Processes whose caret the extension can be drawing at - VS Code and its forks.</summary>
        internal static readonly string[] EditorProcesses =
        {
            "code", "code - insiders", "codium", "vscodium", "cursor", "windsurf", "trae",
        };

        public static bool ShouldYield()
            => IsSignalFresh() && IsEditorForeground();

        /// <summary>True when the file was written less than <see cref="FreshMs"/> ago (throttled).</summary>
        private static bool IsSignalFresh()
        {
            long now = Clock.ElapsedMilliseconds;
            if (now - _checkedMs < PollMs)
                return _fresh;
            _checkedMs = now;

            try
            {
                var info = new FileInfo(FilePath);
                _fresh = info.Exists && IsFresh(info.LastWriteTimeUtc, DateTime.UtcNow);
            }
            catch
            {
                _fresh = false; // an unreadable signal means "not claimed"
            }
            return _fresh;
        }

        /// <summary>The freshness rule on its own, so a test can pin it without a clock or a file.</summary>
        internal static bool IsFresh(DateTime writtenUtc, DateTime nowUtc)
        {
            double age = (nowUtc - writtenUtc).TotalMilliseconds;
            // A file dated in the future (a clock change, a copy from another machine) is not a live
            // claim - it would otherwise hide the overlay until the clocks agreed again.
            return age >= 0 && age < FreshMs;
        }

        private static bool IsEditorForeground() => IsEditorImage(ForegroundProcessName());

        /// <summary>True when an executable path belongs to a VS Code-family editor.</summary>
        internal static bool IsEditorImage(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return false;
            string name;
            try { name = Path.GetFileNameWithoutExtension(imagePath) ?? ""; }
            catch (ArgumentException) { return false; } // invalid path characters
            foreach (string editor in EditorProcesses)
                if (string.Equals(name, editor, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>The foreground window's process image path, or "" when it cannot be read.</summary>
        private static string ForegroundProcessName()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return "";
            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
                return "";

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return "";
            try
            {
                var buffer = new StringBuilder(512);
                uint size = (uint)buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : "";
            }
            catch
            {
                return "";
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
