using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Caret position via <b>IAccessible2</b> (<c>IAccessibleText.caretOffset</c> +
    /// <c>characterExtents</c>) - the API screen readers use to follow the caret. It is the only
    /// source that locates the caret in <b>Chromium/Electron</b> text inputs (the VS Code chat box,
    /// browsers): those expose neither a Win32 caret, nor a usable UIA <c>TextPattern</c> selection
    /// rect, nor <c>TextPattern2.GetCaretRange</c>, but they do implement IAccessible2 fully.
    ///
    /// Path: foreground window's focused HWND → <c>AccessibleObjectFromWindow</c> (oleacc) → drill
    /// <c>accFocus</c> to the focused element → <c>QueryService(IAccessible2 → IAccessibleText)</c>
    /// → caret offset + screen-relative character extents. All hand-rolled COM (IAccessible2 is not
    /// in the .NET BCL), but every piece ships with Windows - no NuGet/runtime dependency, so the
    /// single .exe still goes to winget and the Store (MSIX).
    ///
    /// Guarded with <see cref="HandleProcessCorruptedStateExceptionsAttribute"/> so any COM mishap
    /// degrades to a caught failure (→ next caret source) rather than crashing.
    /// </summary>
    internal static class Ia2Caret
    {
        private const uint IA2_COORDTYPE_SCREEN_RELATIVE = 0;
        private const int MaxFocusDepth = 16;

        private static readonly Guid IID_IAccessible = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        private static readonly Guid IID_IAccessible2 = new Guid("E89F726E-C4F4-4c19-BB19-B647D7FA8478");
        private static readonly Guid IID_IAccessibleText = new Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B");

        /// <summary>
        /// Current caret (screen px) - where the marker goes is <see cref="CaretPlacement"/>'s decision.
        /// False if the focused control exposes no IA2 text.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static bool TryGetCaret(out CaretRect caret)
        {
            caret = default;
            object? acc = null;
            IAccessibleText? text = null;
            try
            {
                acc = FocusedAccessible();
                if (acc == null) return false;

                text = QueryText(acc);
                if (text == null) return false;

                if (text.get_caretOffset(out int offset) != 0) return false;

                // Caret offset usually points just before a character: that character's near edge -
                // the left one, or the right one in right-to-left text (S0011 LI-10).
                if (Extents(text, offset) is CharBox following)
                {
                    CharBox? preceding = offset > 0 ? Extents(text, offset - 1) : null;
                    caret = CaretGeometry.ToCaret(CaretGeometry.XFromFollowing(following, preceding), following);
                    return true;
                }
                // Caret at end-of-text: no char at offset, so the previous char's far edge.
                if (offset > 0 && Extents(text, offset - 1) is CharBox last)
                {
                    CharBox? beforeThat = offset > 1 ? Extents(text, offset - 2) : null;
                    caret = CaretGeometry.ToCaret(CaretGeometry.XFromPreceding(last, beforeThat), last);
                    return true;
                }
                return false;
            }
            catch { return false; }
            finally { Release(text); Release(acc); }
        }

        /// <summary>One character's screen box, unless the provider has none or answers with a whole line.</summary>
        private static CharBox? Extents(IAccessibleText text, int offset)
        {
            if (text.get_characterExtents(offset, IA2_COORDTYPE_SCREEN_RELATIVE, out int cx, out int cy, out int cw, out int ch) != 0
                || ch <= 0 || CaretGeometry.IsWholeLine(cw, ch))
                return null;
            return new CharBox(cx, cy, cw, ch);
        }

        /// <summary>
        /// One IAccessible2 answer about a selection: whether the element could be asked at all,
        /// whether it holds a selection, and - when it would hand it over - the selected text itself.
        /// </summary>
        internal readonly struct Ia2Selection
        {
            /// <summary>False when the element exposes no IA2 text, i.e. "cannot tell".</summary>
            public readonly bool Known;
            public readonly bool HasSelection;
            /// <summary>The selected text when the provider returned it, else null.</summary>
            public readonly string? Text;

            private Ia2Selection(bool known, bool has, string? text)
            {
                Known = known; HasSelection = has; Text = text;
            }

            public static readonly Ia2Selection Unknown = new Ia2Selection(false, false, null);
            public static readonly Ia2Selection Nothing = new Ia2Selection(true, false, null);
            public static Ia2Selection Selected(string? text) => new Ia2Selection(true, true, text);
        }

        /// <summary>
        /// Longest selection this source reads. Kept in step with <c>SelectionProbe.MaxTextChars</c>,
        /// which is what the menu's "characters selected" count is measured against.
        /// </summary>
        private const int MaxSelectionChars = 65536;

        /// <summary>
        /// The selection of the <b>focused</b> element - the IAccessible2 source of
        /// <see cref="SelectionProbe"/>, and the only one that answers inside Chromium/Electron.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static Ia2Selection ReadFocusedSelection() => ReadSelection(FocusedAccessible);

        /// <summary>
        /// The selection of the element <b>under the pointer</b>. The menu opens where the user
        /// right-clicked, which in a read-only surface (a page, a chat transcript, a log view) is
        /// routinely not the element holding the keyboard focus.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static Ia2Selection ReadSelectionAt(int x, int y) => ReadSelection(() => AccessibleAtPoint(x, y));

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static Ia2Selection ReadSelection(Func<object?> element)
        {
            object? acc = null;
            IAccessibleText? text = null;
            try
            {
                acc = element();
                if (acc == null) return Ia2Selection.Unknown;

                text = QueryText(acc);
                if (text == null) return Ia2Selection.Unknown;

                if (text.get_nSelections(out int count) != 0) return Ia2Selection.Unknown;
                if (count <= 0) return Ia2Selection.Nothing;

                // A provider can report a selection that is in fact the collapsed caret.
                if (text.get_selection(0, out int start, out int end) != 0) return Ia2Selection.Unknown;
                if (start == end) return Ia2Selection.Nothing;
                if (start > end) { int swap = start; start = end; end = swap; }
                if (end - start > MaxSelectionChars) end = start + MaxSelectionChars;

                string? selected = null;
                try { if (text.get_text(start, end, out string value) == 0) selected = value; }
                catch { /* the state is the answer; the text is a bonus */ }
                return Ia2Selection.Selected(selected);
            }
            catch { return Ia2Selection.Unknown; }
            finally { Release(text); Release(acc); }
        }

        /// <summary>The deepest accessible element under a screen point (oleacc's own hit test).</summary>
        private static object? AccessibleAtPoint(int x, int y)
        {
            if (AccessibleObjectFromPoint(new POINT { X = x, Y = y }, out IntPtr pAcc, out object child) != 0
                || pAcc == IntPtr.Zero)
                return null;
            Release(child); // the child id variant can carry a COM object we have no use for
            return Wrap(pAcc);
        }

        /// <summary>Human-readable IA2 caret result, for the caret diagnostics.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static string Diagnose()
        {
            object? acc = null;
            IAccessibleText? text = null;
            try
            {
                acc = FocusedAccessible();
                if (acc == null) return "no focused IAccessible";
                string? accName = null;
                try { ((IAccessible)acc).get_accName((object)0, out accName); } catch { }
                // The element's name is the user's text (a mail subject, a document title), and the
                // report goes into the log bundle - only its length is recorded (S0010 TD-1).
                string name = "name " + CaretDiagnostics.TextLength(accName ?? "");

                text = QueryText(acc);
                if (text == null) return $"'{name}' exposes no IAccessible2 text";

                if (text.get_caretOffset(out int offset) != 0) return $"'{name}' caretOffset failed";
                int hr = text.get_characterExtents(offset, IA2_COORDTYPE_SCREEN_RELATIVE, out int cx, out int cy, out int cw, out int ch);
                if (hr != 0 && offset > 0)
                    hr = text.get_characterExtents(offset - 1, IA2_COORDTYPE_SCREEN_RELATIVE, out cx, out cy, out cw, out ch);
                if (hr != 0 || ch <= 0) return $"'{name}' offset={offset} characterExtents failed (hr=0x{hr:X8})";
                return $"'{name}' caretOffset={offset} caret[x={cx} y={cy} w={cw} h={ch}] -> marker x={cx + 2} y={cy + ch + 1}";
            }
            catch (Exception ex) { return "exception: " + ex.GetType().Name + " " + ex.Message; }
            finally { Release(text); Release(acc); }
        }

        /// <summary>Root accessible of the focused window, drilled down via accFocus to the focused element.</summary>
        private static object? FocusedAccessible()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            uint tid = GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            IntPtr hwnd = (GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero) ? gti.hwndFocus : fg;

            Guid iid = IID_IAccessible;
            if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out IntPtr pAcc) != 0 || pAcc == IntPtr.Zero)
                return null;
            object? acc = Wrap(pAcc);

            for (int depth = 0; depth < MaxFocusDepth; depth++)
            {
                if (acc is not IAccessible a) break;
                object? focused = null;
                if (a.get_accFocus(out focused) != 0 || focused == null) break;
                if (focused is not IAccessible)
                {
                    // A child id (an int) means this element already is the focus. But the variant can
                    // also carry a COM object that simply isn't an IAccessible - dropping that on the
                    // floor holds a cross-process proxy alive until the RCW is finalized, eight times
                    // a second. Release is a no-op for the int case.
                    Release(focused);
                    break;
                }
                Release(acc);
                acc = focused;
            }
            return acc;
        }

        private static IAccessibleText? QueryText(object acc)
        {
            if (acc is not IServiceProvider sp) return null;
            Guid sid = IID_IAccessible2, rid = IID_IAccessibleText;
            if (sp.QueryService(ref sid, ref rid, out IntPtr pText) != 0 || pText == IntPtr.Zero)
                return null;
            object? wrapped = Wrap(pText);
            if (wrapped is IAccessibleText text) return text;
            Release(wrapped); // QueryService handed back something else - don't leak the RCW
            return null;
        }

        private static object? Wrap(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            object o = Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            return o;
        }

        private static void Release(object? rcw)
        {
            try { if (rcw != null && Marshal.IsComObject(rcw)) Marshal.ReleaseComObject(rcw); }
            catch { /* best effort */ }
        }

        // ---- Minimal COM declarations (only the slots we call are typed; the rest are
        //      placeholders to keep each vtable aligned). IAccessible is laid out after IDispatch. ----

        [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessible
        {
            [PreserveSig] int _GetTypeInfoCount();                                       // IDispatch 1
            [PreserveSig] int _GetTypeInfo();                                            // IDispatch 2
            [PreserveSig] int _GetIDsOfNames();                                          // IDispatch 3
            [PreserveSig] int _Invoke();                                                 // IDispatch 4
            [PreserveSig] int _get_accParent(out IntPtr parent);                         // 5
            [PreserveSig] int _get_accChildCount(out int count);                         // 6
            [PreserveSig] int _get_accChild([MarshalAs(UnmanagedType.Struct)] object child, out IntPtr disp); // 7
            [PreserveSig] int get_accName([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string name); // 8
            [PreserveSig] int _get_accValue([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string value); // 9
            [PreserveSig] int _get_accDescription([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string desc); // 10
            [PreserveSig] int _get_accRole([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object role); // 11
            [PreserveSig] int _get_accState([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object state); // 12
            [PreserveSig] int _get_accHelp([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string help); // 13
            [PreserveSig] int _get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string file, [MarshalAs(UnmanagedType.Struct)] object child, out int topic); // 14
            [PreserveSig] int _get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string shortcut); // 15
            [PreserveSig] int get_accFocus([MarshalAs(UnmanagedType.Struct)] out object child); // 16
        }

        [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr ppv);
        }

        [ComImport, Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessibleText
        {
            [PreserveSig] int _addSelection(int startOffset, int endOffset);             // 1
            [PreserveSig] int _get_attributes(int offset, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string attributes); // 2
            [PreserveSig] int get_caretOffset(out int offset);                           // 3
            [PreserveSig] int get_characterExtents(int offset, uint coordType, out int x, out int y, out int width, out int height); // 4
            [PreserveSig] int get_nSelections(out int nSelections);                      // 5
            [PreserveSig] int _get_offsetAtPoint(int x, int y, uint coordType, out int offset); // 6
            [PreserveSig] int get_selection(int selectionIndex, out int startOffset, out int endOffset); // 7
            [PreserveSig] int get_text(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text); // 8
        }
    }
}
