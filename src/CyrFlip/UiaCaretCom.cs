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
        /// Current caret (screen px) - where the marker goes is <see cref="CaretPlacement"/>'s decision.
        /// Returns false if there's no caret-bearing focused element or UIA/COM is unavailable.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static bool TryGetCaretRange(out CaretRect caret)
        {
            caret = default;
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

                return RectFromCaretRange(range, out caret);
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

                if (RectFromCaretRange(range, out CaretRect found))
                    return $"active={active} -> caret {found}";
                return $"GetCaretRange OK (active={active}) but no bounding rect from caret/forward/backward";
            }
            catch (Exception ex) { return "exception: " + ex.GetType().Name + " " + ex.Message; }
            finally
            {
                Release(range); Release(tp2); Release(element);
            }
        }

        /// <summary>
        /// The caret, derived from a (degenerate) caret range. A collapsed range often reports no
        /// bounding rectangle, so: try the range directly, then the character after the caret, then
        /// the character before it (a caret at end-of-text). Every rectangle passes the whole-line check
        /// (a provider that answers a collapsed range with its line or its text box is not answering),
        /// and the side of the character the caret is on follows the text direction (S0011 LI-10).
        /// </summary>
        private static bool RectFromCaretRange(IUIAutomationTextRange caret, out CaretRect found)
        {
            found = default;

            // 1) Directly - some providers give a zero/narrow-width caret rect here.
            if (caret.GetBoundingRectangles(out double[]? r) == 0 && Box(r) is CharBox direct)
            {
                found = CaretGeometry.ToCaret(direct.Left, direct);
                return true;
            }

            // 2) The character after the caret: its left edge, or its right edge in right-to-left text -
            //    which only the character before it can tell.
            if (CharAt(caret, 0, 1) is CharBox following)
            {
                found = CaretGeometry.ToCaret(CaretGeometry.XFromFollowing(following, CharAt(caret, -1, 0)), following);
                return true;
            }

            // 3) The character before the caret (caret at end-of-text): its right edge, or its left edge
            //    in right-to-left text.
            if (CharAt(caret, -1, 0) is CharBox preceding)
            {
                found = CaretGeometry.ToCaret(CaretGeometry.XFromPreceding(preceding, CharAt(caret, -2, -1)), preceding);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The box of the character <paramref name="start"/>..<paramref name="end"/> characters from the
        /// caret (0..1 = the one after it, -1..0 = the one before), or null when the text ends there or
        /// the provider reports nothing usable.
        /// </summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static CharBox? CharAt(IUIAutomationTextRange caret, int start, int end)
        {
            if (caret.Clone(out IntPtr p) != 0 || p == IntPtr.Zero) return null;
            IUIAutomationTextRange? range = Wrap<IUIAutomationTextRange>(p);
            try
            {
                if (range == null) return null;
                // Start first: moving End below a Start that is still at the caret would collapse it.
                if (start != 0 && (range.MoveEndpointByUnit(TextEndpoint_Start, TextUnit_Character, start, out int ms) != 0 || ms != start))
                    return null;
                if (end != 0 && (range.MoveEndpointByUnit(TextEndpoint_End, TextUnit_Character, end, out int me) != 0 || me != end))
                    return null;
                return range.GetBoundingRectangles(out double[]? rects) == 0 ? Box(rects) : null;
            }
            catch { return null; }
            finally { Release(range); }
        }

        /// <summary>The first rectangle of a UIA answer, unless it is empty or a whole line.</summary>
        private static CharBox? Box(double[]? r)
        {
            if (r == null || r.Length < 4 || r[3] <= 0) return null;
            if (CaretGeometry.IsWholeLine(r[2], r[3])) return null;
            return new CharBox(r[0], r[1], r[2], r[3]);
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
            if (o is T typed) return typed;
            Release(o); // not the interface we asked for - release it now, don't wait for the finalizer
            return null;
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
