using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;

namespace CyrFlip
{
    /// <summary>
    /// Live caret position via the COM UI Automation <c>IUIAutomationTextPattern2.GetCaretRange</c> -
    /// the API screen readers use to follow the caret while you type. The managed
    /// <c>System.Windows.Automation</c> wrappers (referenced by the project) expose only
    /// <c>TextPattern</c>/<c>GetSelection</c>, which Chromium/Electron report unreliably for a
    /// *collapsed* caret (it returns a stale/whole-line rect that updates only on a selection
    /// change). That is why the marker "sticks" far right in the VS Code chat input and only jumps
    /// into place when you arrow around. <c>GetCaretRange</c> always returns the current caret, so
    /// the marker follows typing in those webview inputs.
    ///
    /// <c>CUIAutomation</c> is a component of Windows itself - no NuGet/runtime dependency is added,
    /// so the same single .exe still ships to winget and the Microsoft Store (MSIX).
    ///
    /// Interface-returning slots use <c>out IntPtr</c> and are wrapped manually with
    /// <see cref="Wrap{T}"/> (the CLR's <c>out</c>-interface marshalling returned E_POINTER from
    /// GetFocusedElement here). Only the slots we call are typed; every preceding method is a
    /// placeholder so each COM vtable stays aligned. Calls are wrapped with
    /// <see cref="HandleProcessCorruptedStateExceptionsAttribute"/> so even a vtable mishap degrades
    /// to a caught failure (→ fall back to the managed GetSelection path) rather than crashing.
    /// </summary>
    internal static class UiaCaretCom
    {
        private const int UIA_TextPattern2Id = 10024;
        private const int TextUnit_Character = 0;
        private const int TextEndpoint_Start = 0;
        private const int TextEndpoint_End = 1;

        private static readonly Guid CLSID_CUIAutomation = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e");
        private static readonly Guid IID_IUIAutomationTextPattern2 = new Guid("506a921a-fcc9-409f-b23b-37eb74106872");

        private static readonly object _gate = new object();
        private static IUIAutomation? _automation;
        private static bool _broken; // CUIAutomation couldn't be created - stop retrying

        private static IUIAutomation? Automation()
        {
            if (_broken) return null;
            IUIAutomation? a = _automation;
            if (a != null) return a;
            lock (_gate)
            {
                if (_automation != null) return _automation;
                if (_broken) return null;
                try
                {
                    Type? t = Type.GetTypeFromCLSID(CLSID_CUIAutomation, throwOnError: false);
                    _automation = t == null ? null : Activator.CreateInstance(t) as IUIAutomation;
                    if (_automation == null) _broken = true;
                }
                catch
                {
                    _broken = true;
                    _automation = null;
                }
                return _automation;
            }
        }

        /// <summary>
        /// Current caret position (screen px), placed diagonally below-right of the caret to match
        /// <see cref="CaretOverlay"/>'s other sources. Returns false if there's no caret-bearing
        /// focused element or UIA/COM is unavailable.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static bool TryGetCaretRange(out int x, out int y)
        {
            x = 0; y = 0;
            IUIAutomationElement? element = null;
            IUIAutomationTextPattern2? tp2 = null;
            IUIAutomationTextRange? range = null;
            try
            {
                IUIAutomation? uia = Automation();
                if (uia == null) return false;

                element = GetFocusedElementSmart(uia);
                if (element == null) return false;

                Guid iid = IID_IUIAutomationTextPattern2;
                if (element.GetCurrentPatternAs(UIA_TextPattern2Id, ref iid, out IntPtr pPattern) != 0) return false;
                tp2 = Wrap<IUIAutomationTextPattern2>(pPattern);
                if (tp2 == null) return false;

                if (tp2.GetCaretRange(out _, out IntPtr pRange) != 0) return false;
                range = Wrap<IUIAutomationTextRange>(pRange);
                if (range == null) return false;

                return RectFromCaretRange(range, out x, out y);
            }
            catch { return false; }
            finally
            {
                Release(range); Release(tp2); Release(element);
            }
        }

        /// <summary>Human-readable result of the GetCaretRange path, for the caret diagnostics.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static string Diagnose()
        {
            IUIAutomationElement? element = null;
            IUIAutomationTextPattern2? tp2 = null;
            IUIAutomationTextRange? range = null;
            try
            {
                IUIAutomation? uia = Automation();
                if (uia == null) return "CUIAutomation unavailable";

                element = GetFocusedElementSmart(uia);
                if (element == null) return "no focused element";

                int hr;
                Guid iid = IID_IUIAutomationTextPattern2;
                hr = element.GetCurrentPatternAs(UIA_TextPattern2Id, ref iid, out IntPtr pPattern);
                tp2 = Wrap<IUIAutomationTextPattern2>(pPattern);
                if (hr != 0 || tp2 == null) return $"no TextPattern2 (hr=0x{hr:X8})";

                hr = tp2.GetCaretRange(out bool active, out IntPtr pRange);
                range = Wrap<IUIAutomationTextRange>(pRange);
                if (hr != 0 || range == null) return $"GetCaretRange hr=0x{hr:X8} range=null active={active}";

                if (RectFromCaretRange(range, out int mx, out int my))
                    return $"active={active} -> marker x={mx} y={my}";
                return $"GetCaretRange OK (active={active}) but no bounding rect from caret/forward/backward";
            }
            catch (Exception ex) { return "exception: " + ex.GetType().Name + " " + ex.Message; }
            finally
            {
                Release(range); Release(tp2); Release(element);
            }
        }

        /// <summary>
        /// Screen-px point diagonally below-right of the caret, derived from a (degenerate) caret
        /// range. A collapsed range often reports no bounding rectangle, so: try the range directly,
        /// then extend its End forward one character (caret = left edge of the result), then extend
        /// its Start back one character (caret = right edge) - the last covers a caret at end-of-text.
        /// </summary>
        private static bool RectFromCaretRange(IUIAutomationTextRange caret, out int x, out int y)
        {
            x = 0; y = 0;

            // 1) Directly - some providers give a zero/narrow-width caret rect here.
            if (caret.GetBoundingRectangles(out double[]? r) == 0 && HasRect(r))
            {
                Place(r!, rightEdge: false, out x, out y);
                return true;
            }

            // 2) Extend End forward one char -> caret sits at the LEFT edge of the result.
            if (caret.Clone(out IntPtr pf) == 0 && pf != IntPtr.Zero)
            {
                IUIAutomationTextRange? fwd = Wrap<IUIAutomationTextRange>(pf);
                try
                {
                    if (fwd != null
                        && fwd.MoveEndpointByUnit(TextEndpoint_End, TextUnit_Character, 1, out int moved) == 0 && moved == 1
                        && fwd.GetBoundingRectangles(out double[]? rf) == 0 && HasRect(rf))
                    {
                        Place(rf!, rightEdge: false, out x, out y);
                        return true;
                    }
                }
                finally { Release(fwd); }
            }

            // 3) Extend Start back one char -> caret sits at the RIGHT edge (caret at end-of-text).
            if (caret.Clone(out IntPtr pb) == 0 && pb != IntPtr.Zero)
            {
                IUIAutomationTextRange? bwd = Wrap<IUIAutomationTextRange>(pb);
                try
                {
                    if (bwd != null
                        && bwd.MoveEndpointByUnit(TextEndpoint_Start, TextUnit_Character, -1, out int moved) == 0 && moved == -1
                        && bwd.GetBoundingRectangles(out double[]? rb) == 0 && HasRect(rb))
                    {
                        Place(rb!, rightEdge: true, out x, out y);
                        return true;
                    }
                }
                finally { Release(bwd); }
            }

            return false;
        }

        private static bool HasRect(double[]? r) => r != null && r.Length >= 4 && r[3] > 0;

        private static void Place(double[] r, bool rightEdge, out int x, out int y)
        {
            double left = r[0], top = r[1], width = r[2], height = r[3];
            x = (int)(left + (rightEdge ? width : 0)) + 2;
            y = (int)(top + height) + 1;
        }

        /// <summary>
        /// The focused element, via <c>GetFocusedElement</c> with a fallback to
        /// <c>GetFocusedElementBuildCache</c>. The plain "current" variant returns E_POINTER for
        /// cross-process, HWND-less providers (e.g. the Chromium chat input); the cache-building
        /// variant returns the element cleanly.
        /// </summary>
        private static IUIAutomationElement? GetFocusedElementSmart(IUIAutomation uia)
        {
            if (uia.GetFocusedElement(out IntPtr p) == 0 && p != IntPtr.Zero)
                return Wrap<IUIAutomationElement>(p);

            if (uia.CreateCacheRequest(out IntPtr cr) != 0 || cr == IntPtr.Zero)
                return null;
            try
            {
                if (uia.GetFocusedElementBuildCache(cr, out IntPtr pe) == 0 && pe != IntPtr.Zero)
                    return Wrap<IUIAutomationElement>(pe);
            }
            finally { Marshal.Release(cr); }
            return null;
        }

        /// <summary>Wrap a COM interface pointer (with one ref we own) as an RCW, releasing the raw ref.</summary>
        private static T? Wrap<T>(IntPtr p) where T : class
        {
            if (p == IntPtr.Zero) return null;
            object o = Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            return o as T;
        }

        private static void Release(object? rcw)
        {
            try { if (rcw != null && Marshal.IsComObject(rcw)) Marshal.ReleaseComObject(rcw); }
            catch { /* best effort */ }
        }

        // ---- Minimal COM UIA interfaces (only the slots we call are typed; the rest are
        //      placeholders to keep each vtable aligned). All [PreserveSig] so we read HRESULTs. ----

        // Vtable order per the Windows SDK (uiautomationclient.h, IUIAutomation): ElementFromHandle
        // and ElementFromPoint sit between GetRootElement and GetFocusedElement, so GetFocusedElement
        // is slot 6, GetFocusedElementBuildCache slot 10, CreateCacheRequest slot 18. (Getting this
        // wrong made slot-4 calls land on ElementFromHandle and return E_POINTER.)
        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomation
        {
            [PreserveSig] int _CompareElements(IntPtr a, IntPtr b, out int areSame);            // 1
            [PreserveSig] int _CompareRuntimeIds(IntPtr a, IntPtr b, out int areSame);          // 2
            [PreserveSig] int _GetRootElement(out IntPtr root);                                  // 3
            [PreserveSig] int _ElementFromHandle(IntPtr hwnd, out IntPtr element);               // 4
            [PreserveSig] int _ElementFromPoint(long pt, out IntPtr element);                    // 5
            [PreserveSig] int GetFocusedElement(out IntPtr element);                             // 6
            [PreserveSig] int _GetRootElementBuildCache(IntPtr cacheRequest, out IntPtr root);   // 7
            [PreserveSig] int _ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheReq, out IntPtr element); // 8
            [PreserveSig] int _ElementFromPointBuildCache(long pt, IntPtr cacheReq, out IntPtr element);      // 9
            [PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest, out IntPtr element); // 10
            [PreserveSig] int _CreateTreeWalker(IntPtr condition, out IntPtr walker);            // 11
            [PreserveSig] int _get_ControlViewWalker(out IntPtr walker);                         // 12
            [PreserveSig] int _get_ContentViewWalker(out IntPtr walker);                         // 13
            [PreserveSig] int _get_RawViewWalker(out IntPtr walker);                             // 14
            [PreserveSig] int _get_RawViewCondition(out IntPtr condition);                       // 15
            [PreserveSig] int _get_ControlViewCondition(out IntPtr condition);                   // 16
            [PreserveSig] int _get_ContentViewCondition(out IntPtr condition);                   // 17
            [PreserveSig] int CreateCacheRequest(out IntPtr cacheRequest);                       // 18
        }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElement
        {
            [PreserveSig] int _SetFocus();                                              // 1
            [PreserveSig] int _GetRuntimeId(out IntPtr runtimeId);                      // 2
            [PreserveSig] int _FindFirst(int scope, IntPtr condition, out IntPtr found); // 3
            [PreserveSig] int _FindAll(int scope, IntPtr condition, out IntPtr found);   // 4
            [PreserveSig] int _FindFirstBuildCache(int scope, IntPtr cond, IntPtr req, out IntPtr found); // 5
            [PreserveSig] int _FindAllBuildCache(int scope, IntPtr cond, IntPtr req, out IntPtr found);   // 6
            [PreserveSig] int _BuildUpdatedCache(IntPtr req, out IntPtr updated);        // 7
            [PreserveSig] int _GetCurrentPropertyValue(int propertyId, out IntPtr value); // 8
            [PreserveSig] int _GetCurrentPropertyValueEx(int propertyId, int ignoreDefault, out IntPtr value); // 9
            [PreserveSig] int _GetCachedPropertyValue(int propertyId, out IntPtr value);  // 10
            [PreserveSig] int _GetCachedPropertyValueEx(int propertyId, int ignoreDefault, out IntPtr value); // 11
            [PreserveSig] int GetCurrentPatternAs(int patternId, ref Guid riid, out IntPtr patternObject); // 12
        }

        [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextPattern2
        {
            [PreserveSig] int _RangeFromPoint(double px, double py, out IntPtr range);   // 1
            [PreserveSig] int _RangeFromChild(IntPtr child, out IntPtr range);           // 2
            [PreserveSig] int _GetSelection(out IntPtr ranges);                          // 3
            [PreserveSig] int _GetVisibleRanges(out IntPtr ranges);                      // 4
            [PreserveSig] int _get_DocumentRange(out IntPtr range);                      // 5
            [PreserveSig] int _get_SupportedTextSelection(out int supported);            // 6
            [PreserveSig] int _RangeFromAnnotation(IntPtr annotation, out IntPtr range); // 7
            [PreserveSig] int GetCaretRange([MarshalAs(UnmanagedType.Bool)] out bool isActive, out IntPtr range); // 8
        }

        [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextRange
        {
            [PreserveSig] int Clone(out IntPtr range);                                   // 1
            [PreserveSig] int _Compare(IntPtr range, out int areSame);                   // 2
            [PreserveSig] int _CompareEndpoints(int srcEndpoint, IntPtr range, int targetEndpoint, out int compValue); // 3
            [PreserveSig] int ExpandToEnclosingUnit(int textUnit);                       // 4
            [PreserveSig] int _FindAttribute(int attr, IntPtr val, int backward, out IntPtr range); // 5
            [PreserveSig] int _FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out IntPtr range); // 6
            [PreserveSig] int _GetAttributeValue(int attr, out IntPtr value);            // 7
            [PreserveSig] int GetBoundingRectangles(
                [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? boundingRects); // 8
            [PreserveSig] int _GetEnclosingElement(out IntPtr element);                  // 9
            [PreserveSig] int _GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text); // 10
            [PreserveSig] int _Move(int unit, int count, out int moved);                 // 11
            [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved); // 12
        }
    }
}
