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
    /// chat box - and type/arrow during capture) and writes a report comparing every caret source,
    /// <b>in the order the overlay asks them</b> (<see cref="CaretOverlay"/>; S0036 UI-7 - the report
    /// used to call the last resort the "current" path and the first cross-process one "proposed"):
    ///   1. the Win32 system caret (<c>GetGUIThreadInfo</c>),
    ///   2. COM <c>GetCaretRange</c> (<see cref="UiaCaretCom"/>, TextPattern2: WinUI/UWP/WPF),
    ///   3. IAccessible2 (<see cref="Ia2Caret"/>, Chromium/Electron - VS Code chat, browsers),
    ///   4. managed <c>GetSelection</c>, the last resort, with its whole-line guard.
    /// Each source is asked through the overlay's own function and reports the <see cref="CaretRect"/>
    /// it yields; the marker position printed is <see cref="CaretPlacement.Place"/>'s for the first
    /// source that answered - never a hand-computed "+2/+1". Beside them: the foreground window
    /// (class/process, and the title's length - never the title itself), the focused UIA element
    /// (ControlType/ClassName/FrameworkId; its Name only as a length), and whether the overlay stands
    /// down for the VS Code extension (<see cref="EditorCaretSignal"/>). The cross-process sources run
    /// in the app only while <see cref="CaretQueryGate"/> is open - i.e. while the user types, as
    /// during this capture. Runs on a background MTA thread (UIA wants MTA). The log goes to the same
    /// MSIX-aware folder as <see cref="LayoutPublisher"/> so it works for both the unpackaged and
    /// Store builds.
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
            sb.AppendLine("              type / move the caret while this runs. The sources are listed");
            sb.AppendLine("              in the overlay's order - Win32 caret, GetCaretRange, IAccessible2,");
            sb.AppendLine("              GetSelection - and the first one that answers places the marker.");
            sb.AppendLine(new string('=', 72));

            for (int i = 0; i < Samples; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"--- snapshot {i + 1}/{Samples}  (+{i * IntervalMs} ms) ---");
                try { Snapshot(sb); }
                catch (Exception ex) { sb.AppendLine("  snapshot error: " + ex.Message); }
                Thread.Sleep(IntervalMs);
            }

            string dir = DataFolder.Current;
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
                uint tid = GetWindowThreadProcessId(fg, out uint pid);
                string proc = "?";
                try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { /* exited */ }
                AppendWindow(sb, cls.ToString(), title.ToString(), proc, pid);

                var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero
                    && gti.rcCaret.Bottom - gti.rcCaret.Top > 0)
                {
                    sb.AppendLine($"    GUITHREADINFO caret: L={gti.rcCaret.Left} T={gti.rcCaret.Top} R={gti.rcCaret.Right} B={gti.rcCaret.Bottom} (client coords)");
                    // The rect is logical for a DPI-unaware or system-aware window (S0011 LI-2).
                    sb.AppendLine("    caret window DPI awareness: " + DescribeDpiAwareness(gti.hwndCaret));
                    var screen = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
                    CaretOverlay.ClientToPhysicalScreen(gti.hwndCaret, ref screen);
                    sb.AppendLine($"    caret bottom-right, physical screen px: x={screen.X} y={screen.Y}");
                }
                else
                    sb.AppendLine("    GUITHREADINFO caret: none (no Win32 caret - expected for Electron)");
            }

            // The focused UIA element, for context.
            AutomationElement? focused = null;
            try
            {
                focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    sb.AppendLine("  UIA focused element: null");
                }
                else
                {
                    AutomationElement element = focused;
                    sb.AppendLine("  UIA focused element:");
                    sb.AppendLine("    Name:        " + TextLength(SafeOrNull(() => element.Current.Name)));
                    sb.AppendLine("    ControlType: " + Safe(() => element.Current.ControlType?.ProgrammaticName));
                    sb.AppendLine("    ClassName:   " + Safe(() => element.Current.ClassName));
                    sb.AppendLine("    FrameworkId: " + Safe(() => element.Current.FrameworkId));
                }
            }
            catch (Exception ex) { sb.AppendLine("  UIA focused element error: " + ex.Message); }

            // The sources, in the overlay's order, each through the overlay's own function.
            sb.AppendLine("  sources, in the overlay's order:");
            CaretRect? first = null;
            string firstName = "";
            void Source(string name, Func<(bool ok, CaretRect caret)> ask, string detail)
            {
                (bool ok, CaretRect caret) answer;
                try { answer = ask(); }
                catch (Exception ex) { sb.AppendLine($"    {name}: error {ex.GetType().Name}"); return; }
                sb.AppendLine($"    {name}: " + (answer.ok ? "caret " + answer.caret : "no caret") + (detail.Length > 0 ? "  | " + detail : ""));
                if (answer.ok && first == null) { first = answer.caret; firstName = name; }
            }
            Source("1 Win32 caret", () => (CaretOverlay.TrySystemCaret(fg, out CaretRect c), c), "");
            Source("2 GetCaretRange", () => (UiaCaretCom.TryGetCaretRange(out CaretRect c), c), UiaCaretCom.Diagnose());
            Source("3 IAccessible2", () => (Ia2Caret.TryGetCaret(out CaretRect c), c), Ia2Caret.Diagnose());
            Source("4 GetSelection", () => (CaretOverlay.TryUiaCaret(out CaretRect c), c),
                focused != null ? DescribeManagedSelection(focused) : "");

            bool yields = false;
            try { yields = EditorCaretSignal.ShouldYield(); } catch { }
            if (yields)
                sb.AppendLine("  overlay: stands down - the VS Code extension draws the marker here (EditorCaretSignal)");
            else if (first is CaretRect caret)
                sb.AppendLine("  overlay: " + firstName + " -> " + DescribePlacement(caret));
            else
                sb.AppendLine("  overlay: no source answered - the marker is hidden here");
        }

        /// <summary>
        /// Where <see cref="CaretPlacement.Place"/> puts a badge for <paramref name="caret"/> - the
        /// app's own rule, monitor edge and all, for a two-letter badge of the default 24 px size at
        /// the caret monitor's DPI (the real one's width follows its letters and the size setting).
        /// </summary>
        private static string DescribePlacement(CaretRect caret)
        {
            System.Drawing.Point probe = CaretPlacement.MonitorProbe(caret);
            int dpi = MarkerSize.MonitorDpi(MarkerSize.MonitorAt(probe.X, probe.Y));
            int h = MarkerSize.OverlayHeight(24, dpi);
            var badge = new System.Drawing.Size(h * 2, h);
            System.Drawing.Rectangle monitor = System.Windows.Forms.Screen.FromPoint(probe).Bounds;
            System.Drawing.Point at = CaretPlacement.Place(caret, badge, monitor);
            return $"marker at x={at.X} y={at.Y} (a {badge.Width}x{badge.Height} badge, monitor DPI {dpi}, bounds {monitor})";
        }

        private static string DescribeDpiAwareness(IntPtr hwnd)
        {
            try
            {
                IntPtr context = GetWindowDpiAwarenessContext(hwnd);
                switch (context == IntPtr.Zero ? -1 : GetAwarenessFromDpiAwarenessContext(context))
                {
                    case 0: return "unaware (logical coordinates)";
                    case 1: return "system aware (logical coordinates off the system DPI)";
                    case 2: return "per-monitor aware (physical coordinates)";
                    default: return "unknown";
                }
            }
            catch (EntryPointNotFoundException) { return "unknown (pre-1607 Windows)"; }
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
                string verdict = CaretGeometry.IsWholeLine(r.Width, r.Height)
                    ? "REJECTED as a whole-line rect (treated as no caret)"
                    : "accepted";
                return $"rect[L={r.Left:0} T={r.Top:0} W={r.Width:0} H={r.Height:0}] {verdict}";
            }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        /// <summary>
        /// The foreground window as the report records it (ticket S0010 TD-1). This file goes into
        /// the log bundle the user mails to the author, and a window title is a mail subject, a
        /// document name or a URL - so the title appears only as its <b>length</b>. The diagnostic
        /// value is the class, the process image and what each caret source answers, not the text.
        /// </summary>
        internal static void AppendWindow(StringBuilder sb, string windowClass, string? title, string process, uint pid)
        {
            sb.AppendLine("    class:   " + windowClass);
            sb.AppendLine("    title:   " + TextLength(title));
            sb.AppendLine("    process: " + process + " (pid " + pid + ")");
        }

        /// <summary>"42 chars" - a piece of the user's text, reduced to how long it is.</summary>
        internal static string TextLength(string? text)
            => text == null ? "<unreadable>" : text.Length + " chars";

        private static string? SafeOrNull(Func<string?> f)
        {
            try { return f() ?? ""; }
            catch { return null; }
        }

        private static string Safe(Func<string?> f)
        {
            try { return f() ?? ""; }
            catch (Exception ex) { return "<err: " + ex.GetType().Name + ">"; }
        }
    }
}
