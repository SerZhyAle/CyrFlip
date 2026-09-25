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
        /// <summary>Every source that could answer says nothing is selected.</summary>
        Absent,
        /// <summary>No source could tell. Treated as <see cref="Present"/> by the menu - see remarks.</summary>
        Unknown,
    }

    /// <summary>One source's answer: the state it is sure of, plus the selected text if it hands it over.</summary>
    internal readonly struct SelectionAnswer
    {
        public readonly SelectionState State;
        /// <summary>The selected text when this source could read it without touching the clipboard.</summary>
        public readonly string? Text;

        private SelectionAnswer(SelectionState state, string? text)
        {
            State = state;
            Text = string.IsNullOrEmpty(text) ? null : text;
        }

        public static readonly SelectionAnswer Unknown = new SelectionAnswer(SelectionState.Unknown, null);
        public static readonly SelectionAnswer Absent = new SelectionAnswer(SelectionState.Absent, null);
        public static SelectionAnswer Present(string? text = null) => new SelectionAnswer(SelectionState.Present, text);
    }

    /// <summary>What one probe found: the verdict and, when a source gave it, the selected text.</summary>
    internal sealed class SelectionSnapshot
    {
        public SelectionSnapshot(SelectionState state, string? text = null, LaunchTarget? launch = null)
        {
            State = state;
            Text = string.IsNullOrEmpty(text) ? null : text;
            Launch = launch;
        }

        public SelectionState State { get; }

        /// <summary>
        /// What <see cref="LaunchTargets"/> made of <see cref="Text"/>, parsed on the probe's own
        /// thread (ticket S0008, LS-1): deciding whether a path exists is file-system work, and the
        /// UI thread that draws the menu is the one both low-level hooks live on.
        /// </summary>
        public LaunchTarget? Launch { get; }

        /// <summary>The same snapshot with the text's launch target attached.</summary>
        public SelectionSnapshot WithLaunch(LaunchTarget? launch) => new SelectionSnapshot(State, Text, launch);
        /// <summary>
        /// The selection itself when it could be read through the accessibility stack - null
        /// otherwise, which is an ordinary outcome, not a failure. It is what "Launch" parses; every
        /// other command works on the real selection through the usual synthesized Ctrl+C.
        /// </summary>
        public string? Text { get; }

        /// <summary>
        /// The read stopped at the cap, so <see cref="Text"/> is a prefix of the selection rather
        /// than the selection. The menu says so with a "+" instead of printing the ceiling as a fact.
        /// </summary>
        public bool Truncated => Text != null && Text.Length >= SelectionProbe.MaxTextChars;

        public static readonly SelectionSnapshot Unknown = new SelectionSnapshot(SelectionState.Unknown);
    }

    /// <summary>
    /// Answers "is there a selection right now, and what does it say?" for the text context menu
    /// (spec §6), by asking the same accessibility stack that already carries the caret overlay.
    /// Each source is asked twice over: about the <b>focused</b> element and about the element
    /// <b>under the pointer</b>.
    ///
    ///   1. <c>EM_GETSEL</c> on the window - classic Edit/RichEdit controls, microseconds.
    ///   2. IAccessible2 <c>nSelections</c>/<c>selection</c>/<c>text</c> (<see cref="Ia2Caret"/>) - Chromium/Electron.
    ///   3. managed UIA <c>TextPattern.GetSelection</c> - WinUI/UWP/WPF.
    ///
    /// The Win32 source is gated on the window class: <c>EM_GETSEL</c> sent to something that is not
    /// an edit control reaches <c>DefWindowProc</c> and comes back as 0, i.e. a confident, wrong
    /// "nothing is selected".
    ///
    /// <b>Why the pointer is asked at all.</b> Every source used to describe the focused element
    /// alone, and the menu greyed Copy on the first definite answer it got. Read-only text is
    /// precisely where that goes wrong: a page, a chat transcript, a log view, a PDF - the selection
    /// is under the pointer while the keyboard focus sits in a search box, a container node or
    /// nowhere useful, so a source would confidently report "nothing selected" about an element the
    /// user was not pointing at, and Copy went grey beside a live selection.
    ///
    /// <b>No source may veto another's sighting.</b> A <see cref="SelectionState.Present"/> from any
    /// source wins; <see cref="SelectionState.Absent"/> is the verdict only when every source that
    /// could answer at all agreed there is nothing. Being wrong in that direction costs a command
    /// that runs and quietly does nothing - the behaviour every CyrFlip operation already has
    /// ("no selection → no-op", spec §5.3) - while the other direction is a visible bug.
    ///
    /// <b>What this never does:</b> probe by synthesizing Ctrl+C. That would put the text on the
    /// clipboard purely to draw a menu, and the clipboard history - which by design drops nothing -
    /// would record it as an ordinary copy.
    /// </summary>
    internal static class SelectionProbe
    {
        /// <summary>How long the probe may keep working before whatever it has is final.</summary>
        public const int BudgetMs = 250;

        /// <summary>
        /// The longest <see cref="Run.Collect"/> will block the UI thread. It is far below the
        /// budget on purpose: the keyboard hook shares this thread, and Windows drops a low-level
        /// hook that overruns <c>LowLevelHooksTimeout</c> (~300 ms). A probe that has not finished
        /// by then simply answers Unknown, i.e. everything enabled.
        /// </summary>
        public const int MaxWaitMs = 90;

        /// <summary>
        /// Longest selection carried back. It is no longer "enough for a link" but "enough to count":
        /// the menu prints the size of the selection, and a ceiling the user can reach silently turns
        /// that number into a lie. 64K of text costs nothing to carry and is read on the probe's own
        /// thread; past it the counts are printed with a "+" (see <see cref="SelectionStats.Format"/>).
        /// </summary>
        public const int MaxTextChars = 65536;

        private const uint SendTimeoutMs = 100;

        /// <summary>
        /// The decision itself, kept free of interop so it can be unit-tested: ask every source, let
        /// any sighting win, and keep the <b>longest</b> text anyone handed over.
        ///
        /// <b>Longest, not first</b>, because a source can answer about one node rather than about
        /// the selection: measured on Chromium, IA2 hands back only the part of the selection inside
        /// the element under the pointer - a run of syntax-highlighted code, a single styled word -
        /// so the first text to arrive was routinely one character of a selected paragraph. Since no
        /// source announces that it is giving a fragment, the only honest tie-break is length. Every
        /// source is therefore asked, which the probe can afford: it runs on its own thread while the
        /// user is still holding the button, and <see cref="Run.Collect"/> caps what the UI thread
        /// can lose to it regardless.
        /// </summary>
        internal static SelectionSnapshot Decide(IEnumerable<Func<SelectionAnswer>> sources)
        {
            if (sources == null) return SelectionSnapshot.Unknown;

            bool present = false, absent = false;
            string? text = null;
            foreach (Func<SelectionAnswer> source in sources)
            {
                SelectionAnswer answer;
                try { answer = source(); }
                catch { continue; } // a source that throws is a source that does not know

                if (answer.State == SelectionState.Present)
                {
                    present = true;
                    if (answer.Text != null && (text == null || answer.Text.Length > text.Length))
                        text = answer.Text;
                }
                else if (answer.State == SelectionState.Absent)
                {
                    absent = true;
                }
            }

            if (present) return new SelectionSnapshot(SelectionState.Present, text);
            return new SelectionSnapshot(absent ? SelectionState.Absent : SelectionState.Unknown);
        }

        /// <summary>
        /// Ask the real sources about the focused element and about the element under
        /// (<paramref name="x"/>, <paramref name="y"/>). Must run on an MTA thread - see <see cref="Start"/>.
        /// </summary>
        public static SelectionSnapshot Probe(int x, int y) => Decide(Sources(x, y));

        /// <summary>
        /// The source list, cheapest and most likely first. The pointer comes before the focus in
        /// each pair: the menu opens over the text the user means, and that is the element whose
        /// answer describes it.
        /// </summary>
        private static IEnumerable<Func<SelectionAnswer>> Sources(int x, int y)
        {
            IntPtr pointerWindow = WindowFromPoint(new POINT { X = x, Y = y });
            IntPtr focusWindow = FocusedWindow();

            yield return () => FromEditControl(pointerWindow);
            if (focusWindow != pointerWindow) yield return () => FromEditControl(focusWindow);
            yield return () => FromAnswer(Ia2Caret.ReadSelectionAt(x, y));
            yield return () => FromAnswer(Ia2Caret.ReadFocusedSelection());
            yield return () => FromUia(AutomationElementAt(x, y));
            yield return () => FromUia(AutomationElement.FocusedElement);
        }

        /// <summary>
        /// Start probing in the background and hand back a handle to collect the answer from. Called
        /// when the chord goes <b>down</b> and read when it comes <b>up</b>, so the 80-150 ms a user
        /// spends holding the button pays for the cross-process calls - no artificial delay.
        /// </summary>
        public static Run Start(int x, int y)
        {
            var run = new Run(x, y);
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
            private readonly int _x, _y;
            private SelectionSnapshot _snapshot = SelectionSnapshot.Unknown;

            internal Run(int x, int y) { _x = x; _y = y; }

            internal void Execute()
            {
                try
                {
                    SelectionSnapshot snapshot = Probe(_x, _y);
                    // Here, not when the menu is built: the parse may ask the disk whether a path
                    // exists, and that must never happen on the hooks' thread.
                    if (snapshot.Text != null && LaunchTargets.TryParse(snapshot.Text, out LaunchTarget? launch))
                        snapshot = snapshot.WithLaunch(launch);
                    Volatile.Write(ref _snapshot, snapshot);
                }
                catch { /* stays Unknown - the menu shows everything enabled */ }
                finally { _done.Set(); }
            }

            /// <summary>
            /// The answer, waiting out at most the remainder of <see cref="BudgetMs"/> counted from
            /// the moment the chord went down, and never more than <see cref="MaxWaitMs"/> in one go.
            /// A probe that has not finished by then is Unknown.
            /// </summary>
            public SelectionSnapshot Collect()
            {
                int left = Math.Min(MaxWaitMs, BudgetMs - unchecked(Environment.TickCount - _startedAt));
                if (left > 0) _done.Wait(left);
                return Volatile.Read(ref _snapshot);
            }
        }

        // ---- Sources ---------------------------------------------------------------------

        /// <summary>
        /// Classic Win32 edit controls. <c>EM_GETSEL</c> with both out-pointers null returns the range
        /// packed into the result, so nothing has to be marshalled into the other process; the text
        /// then comes from <c>WM_GETTEXT</c>, which USER32 marshals across processes for us.
        /// </summary>
        private static SelectionAnswer FromEditControl(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return SelectionAnswer.Unknown;
            string className = ClassNameOf(hwnd);
            if (!IsEditClass(className)) return SelectionAnswer.Unknown;

            if (SendMessageTimeout(hwnd, EM_GETSEL, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, SendTimeoutMs, out IntPtr result) == IntPtr.Zero)
                return SelectionAnswer.Unknown; // the app is hung or refused - do not guess

            return EditAnswer(className, result.ToInt64(), (start, end) => SelectedTextOf(hwnd, start, end));
        }

        /// <summary>
        /// The verdict of one <c>EM_GETSEL</c> reply, free of interop so it can be unit-tested.
        ///
        /// A <b>RichEdit</b> gives the verdict and nothing else (S0008 LS-7): its <c>EM_GETSEL</c>
        /// counts a paragraph end as one character (CR) while <c>WM_GETTEXT</c> hands back CRLF, so the
        /// offsets drift by one per preceding line break. The wrong text is as long as the right one,
        /// and this source comes first, so the "longest wins" rule would let it beat UIA's correct
        /// text - and the menu would offer to run, and count, text that is not selected.
        /// </summary>
        internal static SelectionAnswer EditAnswer(string? className, long packed, Func<int, int, string?> readText)
        {
            if (!IsEditClass(className)) return SelectionAnswer.Unknown;
            if (!TryUnpackSelection(packed, out int start, out int end)) return SelectionAnswer.Unknown;
            if (start == end) return SelectionAnswer.Absent;
            if (IsRichEditClass(className)) return SelectionAnswer.Present();
            return SelectionAnswer.Present(readText(start, end));
        }

        /// <summary>
        /// Unpack <c>EM_GETSEL</c>'s packed reply (S0008 LS-8). The low 32 bits are the whole answer;
        /// a zero-extended reply whose high word is ≥ 0x8000 used to overflow <c>ToInt32</c>, and the
        /// documented <c>-1</c> - "a position above 65535, look elsewhere" - used to unpack to
        /// start == end == 0xFFFF, i.e. a confident Absent beside a live selection. It is Unknown.
        /// </summary>
        internal static bool TryUnpackSelection(long packed, out int start, out int end)
        {
            long low = packed & 0xFFFFFFFFL;
            start = end = 0;
            if (low == 0xFFFFFFFFL) return false;
            start = (int)(low & 0xFFFF);
            end = (int)((low >> 16) & 0xFFFF);
            return true;
        }

        internal static bool IsRichEditClass(string? className)
            => !string.IsNullOrEmpty(className) && className!.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The text between two offsets of an edit control. Best effort: the state above is the
        /// answer, this is the bonus that lets "Launch" read a link out of the selection.
        /// </summary>
        private static string? SelectedTextOf(IntPtr hwnd, int start, int end)
        {
            if (SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, SendTimeoutMs, out IntPtr lengthResult) == IntPtr.Zero)
                return null;

            int length = lengthResult.ToInt32();
            // A whole document has to travel through the buffer to reach the selection, so a big
            // control is simply not read: the selection is still Present, just not quoted.
            if (length <= 0 || length > 64 * 1024 || end > length) return null;

            var buffer = new StringBuilder(length + 1);
            if (SendMessageTimeoutText(hwnd, WM_GETTEXT, new IntPtr(buffer.Capacity), buffer,
                    SMTO_ABORTIFHUNG, SendTimeoutMs, out _) == IntPtr.Zero)
                return null;

            string all = buffer.ToString();
            if (end > all.Length) return null;
            return Cap(all.Substring(start, end - start));
        }

        /// <summary>
        /// IAccessible2 - the only source that answers inside Chromium/Electron (the VS Code chat
        /// box, browsers, and every page of text a browser shows), exactly as for the caret.
        /// </summary>
        private static SelectionAnswer FromAnswer(Ia2Caret.Ia2Selection selection)
        {
            if (!selection.Known) return SelectionAnswer.Unknown;
            return selection.HasSelection ? SelectionAnswer.Present(Cap(selection.Text)) : SelectionAnswer.Absent;
        }

        /// <summary>
        /// Managed UIA. A text control with no selection reports one <b>degenerate</b> range (the
        /// caret), which is the difference between "nothing selected" and "cannot tell".
        /// </summary>
        private static SelectionAnswer FromUia(AutomationElement? element)
        {
            if (element == null) return SelectionAnswer.Unknown;

            TextPattern? pattern = TextPatternOf(element);
            if (pattern == null) return SelectionAnswer.Unknown;

            TextPatternRange[] selection = pattern.GetSelection();
            if (selection == null || selection.Length == 0)
                return SelectionAnswer.Unknown; // provider quirk, not an answer

            TextPatternRange range = selection[0];
            if (selection.Length == 1
                && range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                return SelectionAnswer.Absent;

            string? text = null;
            try { text = Cap(range.GetText(MaxTextChars)); } catch { /* the state is the answer */ }
            return SelectionAnswer.Present(text);
        }

        /// <summary>
        /// The element under the pointer carries the text pattern only in the simple cases; in a
        /// document it is a run inside one. Walk a few ancestors before giving up - but only a few,
        /// since every step is a cross-process call.
        /// </summary>
        private static TextPattern? TextPatternOf(AutomationElement element)
        {
            AutomationElement? current = element;
            for (int depth = 0; depth < 4 && current != null; depth++)
            {
                if (current.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
                    return (TextPattern)pattern;
                try { current = TreeWalker.ControlViewWalker.GetParent(current); }
                catch { return null; }
            }
            return null;
        }

        private static AutomationElement? AutomationElementAt(int x, int y)
        {
            try { return AutomationElement.FromPoint(new System.Windows.Point(x, y)); }
            catch { return null; }
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static string? Cap(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return text!.Length <= MaxTextChars ? text : text.Substring(0, MaxTextChars);
        }

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
