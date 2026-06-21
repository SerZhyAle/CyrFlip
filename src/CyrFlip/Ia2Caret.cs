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
        /// Current caret position (screen px), placed diagonally below-right of the caret to match
        /// <see cref="CaretOverlay"/>'s other sources. False if the focused control exposes no IA2 text.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static bool TryGetCaret(out int x, out int y)
        {
            x = 0; y = 0;
            object? acc = null;
            IAccessibleText? text = null;
            try
            {
                acc = FocusedAccessible();
                if (acc == null) return false;

                text = QueryText(acc);
                if (text == null) return false;

                if (text.get_caretOffset(out int offset) != 0) return false;

                // Caret offset usually points just before a character; use that char's left edge.
                if (text.get_characterExtents(offset, IA2_COORDTYPE_SCREEN_RELATIVE, out int cx, out int cy, out int cw, out int ch) == 0 && ch > 0)
                {
                    x = cx + 2; y = cy + ch + 1;
                    return true;
                }
                // Caret at end-of-text: no char at offset, so use the previous char's right edge.
                if (offset > 0
                    && text.get_characterExtents(offset - 1, IA2_COORDTYPE_SCREEN_RELATIVE, out cx, out cy, out cw, out ch) == 0 && ch > 0)
                {
                    x = cx + cw + 2; y = cy + ch + 1;
                    return true;
                }
                return false;
            }
            catch { return false; }
            finally { Release(text); Release(acc); }
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
                string name = "";
                try { ((IAccessible)acc).get_accName((object)0, out name); } catch { }

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
                if (focused is not IAccessible) break; // a child id (int) => this element is the focus
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
            return Wrap(pText) as IAccessibleText;
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
        }
    }
}
