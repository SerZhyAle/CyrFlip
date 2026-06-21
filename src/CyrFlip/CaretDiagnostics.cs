using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// One-shot diagnostics for "why doesn't the caret marker follow the caret here?". Captures a
    /// burst of snapshots over ~7s (so the user can click into the target input - e.g. the VS Code
    /// chat box - and type/arrow during capture) and writes a report comparing every caret source:
    ///   - the foreground window (class/title/process),
    ///   - the focused UIA element (ControlType/ClassName/FrameworkId),
    ///   - the *current* overlay path (managed <c>GetSelection</c>) and whether its width guard rejects it,
    ///   - the *proposed* path (COM <c>GetCaretRange</c>, see <see cref="UiaCaretCom"/>),
    ///   - the Win32 system caret (<c>GetGUIThreadInfo</c>).
    /// Runs on a background MTA thread (UIA wants MTA). The log goes to the same MSIX-aware folder
    /// as <see cref="LayoutPublisher"/> so it works for both the unpackaged and Store builds.
    /// </summary>
    internal static class CaretDiagnostics
    {
        private const int Samples = 14;
        private const int IntervalMs = 500;
        private static int _running;

        /// <summary>True while a capture is in progress (so the tray item can't start two).</summary>
        public static bool IsRunning => Volatile.Read(ref _running) != 0;

        /// <summary>Runs the capture off the UI thread; callbacks fire on the worker thread.</summary>
        public static bool Run(Action<string> onDone, Action<string> onError)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                return false;

            var t = new Thread(() =>
            {
                try { onDone(Capture()); }
                catch (Exception ex) { onError(ex.Message); }
                finally { Interlocked.Exchange(ref _running, 0); }
            })
            {
                IsBackground = true,
                Name = "CyrFlip.CaretDiagnostics",
            };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            return true;
        }

        private static string Capture()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CyrFlip caret diagnostics");
            sb.AppendLine("Generated:    " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Packaged:     " + PackageInfo.IsPackaged + " (MSIX/Store build)");
            sb.AppendLine("OS:           " + Environment.OSVersion.VersionString + "  64-bit=" + Environment.Is64BitProcess);
            sb.AppendLine($"Capture:      {Samples} snapshots, {IntervalMs} ms apart (~{Samples * IntervalMs / 1000.0:0.#}s).");
            sb.AppendLine("How to read:  click into the target input (e.g. the VS Code chat box) and");
            sb.AppendLine("              type / move the caret while this runs. Compare 'GetSelection'");
            sb.AppendLine("              (current overlay path) vs 'GetCaretRange' (proposed path).");
            sb.AppendLine(new string('=', 72));

            for (int i = 0; i < Samples; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"--- snapshot {i + 1}/{Samples}  (+{i * IntervalMs} ms) ---");
                try { Snapshot(sb); }
                catch (Exception ex) { sb.AppendLine("  snapshot error: " + ex.Message); }
                Thread.Sleep(IntervalMs);
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(PackageInfo.IsPackaged
                    ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                    : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
                "CyrFlip");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "caret-diagnostics.txt");
            File.WriteAllText(file, sb.ToString());
            return file;
        }

        private static void Snapshot(StringBuilder sb)
        {
            IntPtr fg = GetForegroundWindow();
            sb.AppendLine("  foreground hwnd: 0x" + fg.ToInt64().ToString("X"));
            if (fg != IntPtr.Zero)
            {
                var cls = new StringBuilder(256);
                GetClassName(fg, cls, cls.Capacity);
                var title = new StringBuilder(256);
                GetWindowText(fg, title, title.Capacity);
                sb.AppendLine("    class:   " + cls);
                sb.AppendLine("    title:   " + title);

                uint tid = GetWindowThreadProcessId(fg, out uint pid);
                string proc = "?";
                try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { /* exited */ }
                sb.AppendLine("    process: " + proc + " (pid " + pid + ")");

                var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero
                    && gti.rcCaret.Bottom - gti.rcCaret.Top > 0)
                    sb.AppendLine($"    GUITHREADINFO caret: L={gti.rcCaret.Left} T={gti.rcCaret.Top} R={gti.rcCaret.Right} B={gti.rcCaret.Bottom} (client coords)");
                else
                    sb.AppendLine("    GUITHREADINFO caret: none (no Win32 caret - expected for Electron)");
            }

            // Focused UIA element + the managed GetSelection path (what the overlay does today).
            try
            {
                AutomationElement? focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    sb.AppendLine("  UIA focused element: null");
                }
                else
                {
                    sb.AppendLine("  UIA focused element:");
                    sb.AppendLine("    Name:        " + Safe(() => focused.Current.Name));
                    sb.AppendLine("    ControlType: " + Safe(() => focused.Current.ControlType?.ProgrammaticName));
                    sb.AppendLine("    ClassName:   " + Safe(() => focused.Current.ClassName));
                    sb.AppendLine("    FrameworkId: " + Safe(() => focused.Current.FrameworkId));
                    sb.AppendLine("    GetSelection (current path): " + DescribeManagedSelection(focused));
                }
            }
            catch (Exception ex) { sb.AppendLine("  UIA focused element error: " + ex.Message); }

            // The COM GetCaretRange path (UIA TextPattern2: WinUI/UWP/WPF).
            sb.AppendLine("  UIA GetCaretRange: " + UiaCaretCom.Diagnose());
            // The IAccessible2 path (Chromium/Electron webviews - VS Code chat, browsers).
            sb.AppendLine("  IAccessible2 caret: " + Ia2Caret.Diagnose());
        }

        private static string DescribeManagedSelection(AutomationElement focused)
        {
            try
            {
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out object o))
                    return "element exposes no TextPattern";
                var tp = (TextPattern)o;
                TextPatternRange[] sel = tp.GetSelection();
                if (sel == null || sel.Length == 0)
                    return "empty selection";

                TextPatternRange range = sel[0].Clone();
                range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                range.ExpandToEnclosingUnit(TextUnit.Character);
                System.Windows.Rect[] rects = range.GetBoundingRectangles();
                if (rects.Length == 0)
                    return "no bounding rects";

                System.Windows.Rect r = rects[0];
                double h = r.Height > 0 ? r.Height : 16;
                bool rejected = r.Width > 4 * h;
                string verdict = rejected
                    ? "REJECTED by width>4h guard (treated as no caret)"
                    : $"-> marker x={(int)r.Right + 2} y={(int)r.Bottom + 1}";
                return $"rect[L={r.Left:0} T={r.Top:0} W={r.Width:0} H={r.Height:0}] {verdict}";
            }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        private static string Safe(Func<string?> f)
        {
            try { return f() ?? ""; }
            catch (Exception ex) { return "<err: " + ex.GetType().Name + ">"; }
        }
    }
}
