using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Whether the focused control currently holds a text selection. Deliberately three-valued:
    /// there is no universal API for this question, and pretending "we could not tell" means "no"
    /// would grey out working commands - the one failure the user notices immediately.
    /// </summary>
    internal enum SelectionState
    {
        /// <summary>Something is selected.</summary>
        Present,
        /// <summary>Definitely nothing is selected.</summary>
        Absent,
        /// <summary>No source could tell. Treated as <see cref="Present"/> by the menu - see remarks.</summary>
        Unknown,
    }

    /// <summary>
    /// Answers "is there a selection right now?" for the text context menu (spec §6), by asking the
    /// same accessibility stack that already carries the caret overlay, in cost order:
    ///
    ///   1. <c>EM_GETSEL</c> on the focused window - classic Edit/RichEdit controls, microseconds.
    ///   2. IAccessible2 <c>nSelections</c>/<c>selection</c> (<see cref="Ia2Caret"/>) - Chromium/Electron.
    ///   3. managed UIA <c>TextPattern.GetSelection</c> - WinUI/UWP/WPF.
    ///
    /// The first source is gated on the window class: <c>EM_GETSEL</c> sent to something that is not
    /// an edit control reaches <c>DefWindowProc</c> and comes back as 0, i.e. a confident, wrong
    /// "nothing is selected".
    ///
    /// <b>Unknown means enabled.</b> A greyed-out command next to a live selection is a bug the user
    /// sees; a command that runs and quietly does nothing is the behaviour every CyrFlip operation
    /// already has ("no selection → no-op", spec §5.3 of the main specification).
    ///
    /// <b>What this never does:</b> probe by synthesizing Ctrl+C. That would put the text on the
    /// clipboard purely to draw a menu, and the clipboard history - which by design drops nothing -
    /// would record it as an ordinary copy.
    /// </summary>
    internal static class SelectionProbe
    {
        /// <summary>How long a menu will wait for an answer before falling back to Unknown.</summary>
        public const int BudgetMs = 150;

        private const uint SendTimeoutMs = 100;

        /// <summary>
        /// The decision itself, kept free of interop so it can be unit-tested: ask each source in
        /// order and take the first definite answer. All quiet → Unknown.
        /// </summary>
        internal static SelectionState Decide(IEnumerable<Func<SelectionState>> sources)
        {
            if (sources == null) return SelectionState.Unknown;
            foreach (Func<SelectionState> source in sources)
            {
                SelectionState state;
                try { state = source(); }
                catch { continue; } // a source that throws is a source that does not know
                if (state != SelectionState.Unknown) return state;
            }
            return SelectionState.Unknown;
        }

        /// <summary>Ask the real sources. Must run on an MTA thread - see <see cref="Start"/>.</summary>
        public static SelectionState Probe() => Decide(new Func<SelectionState>[]
        {
            FromEditControl,
            FromAccessible2,
            FromUia,
        });

        /// <summary>
        /// Start probing in the background and hand back a handle to collect the answer from. Called
        /// when the chord goes <b>down</b> and read when it comes <b>up</b>, so the 80-150 ms a user
        /// spends holding the button pays for the cross-process calls - no artificial delay.
        /// </summary>
        public static Run Start()
        {
            var run = new Run();
            var thread = new Thread(run.Execute) { IsBackground = true };
            // COM here is cross-process UIA/IAccessible2, exactly as in CaretOverlay's tracker.
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return run;
        }

        /// <summary>A probe in flight; <see cref="Collect"/> takes whatever it has by the deadline.</summary>
        internal sealed class Run
        {
            private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);
            private readonly int _startedAt = Environment.TickCount;
            private int _state = (int)SelectionState.Unknown;

            internal void Execute()
            {
                try { Interlocked.Exchange(ref _state, (int)Probe()); }
                catch { /* stays Unknown - the menu shows everything enabled */ }
                finally { _done.Set(); }
            }

            /// <summary>
            /// The answer, waiting out at most the remainder of <see cref="BudgetMs"/> counted from
            /// the moment the chord went down. A probe that has not finished by then is Unknown.
            /// </summary>
            public SelectionState Collect()
            {
                int left = BudgetMs - unchecked(Environment.TickCount - _startedAt);
                if (left > 0) _done.Wait(left);
                return (SelectionState)Interlocked.CompareExchange(ref _state, 0, 0);
            }
        }

        // ---- Sources ---------------------------------------------------------------------

        /// <summary>
        /// Classic Win32 edit controls. <c>EM_GETSEL</c> with both out-pointers null returns the range
        /// packed into the result, so nothing has to be marshalled into the other process.
        /// </summary>
        private static SelectionState FromEditControl()
        {
            IntPtr focus = FocusedWindow();
            if (focus == IntPtr.Zero) return SelectionState.Unknown;
            if (!IsEditClass(ClassNameOf(focus))) return SelectionState.Unknown;

            if (SendMessageTimeout(focus, EM_GETSEL, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, SendTimeoutMs, out IntPtr result) == IntPtr.Zero)
                return SelectionState.Unknown; // the app is hung or refused - do not guess

            int packed = result.ToInt32();
            int start = packed & 0xFFFF;
            int end = (packed >> 16) & 0xFFFF;
            return start != end ? SelectionState.Present : SelectionState.Absent;
        }

        /// <summary>
        /// IAccessible2 - the only source that answers inside Chromium/Electron inputs (the VS Code
        /// chat box, browsers), exactly as for the caret.
        /// </summary>
        private static SelectionState FromAccessible2()
        {
            bool? has = Ia2Caret.TryGetHasSelection();
            return has == null ? SelectionState.Unknown
                : has.Value ? SelectionState.Present : SelectionState.Absent;
        }

        /// <summary>
        /// Managed UIA. A text control with no selection reports one <b>degenerate</b> range (the
        /// caret), which is the difference between "nothing selected" and "cannot tell".
        /// </summary>
        private static SelectionState FromUia()
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj))
                return SelectionState.Unknown;

            TextPatternRange[] selection = ((TextPattern)patternObj).GetSelection();
            if (selection == null || selection.Length == 0)
                return SelectionState.Unknown; // provider quirk, not an answer
            if (selection.Length > 1)
                return SelectionState.Present;

            TextPatternRange range = selection[0];
            return range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0
                ? SelectionState.Present
                : SelectionState.Absent;
        }

        // ---- Helpers ---------------------------------------------------------------------

        /// <summary>
        /// Whether <c>EM_GETSEL</c> may be trusted for this window class. Everything else has to go
        /// through the accessibility sources, because a non-edit window answers 0 to it.
        /// </summary>
        internal static bool IsEditClass(string? className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            // "Edit" covers the plain control; "RichEdit20W"/"RICHEDIT50W"/"RichEditD2DPT" (Windows 11
            // WordPad and Notepad) all share the prefix.
            return className!.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
        }

        private static IntPtr FocusedWindow()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return IntPtr.Zero;
            uint tid = GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return (GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero) ? gti.hwndFocus : fg;
        }

        private static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            int length = GetClassName(hwnd, sb, sb.Capacity);
            return length > 0 ? sb.ToString() : "";
        }
    }
}
