using System;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Switches the foreground window's input language after a flip (feature:
    /// "Change the language after the flip"). EN → RU; anything else (RU/UK/…) → EN.
    ///
    /// Uses <c>PostMessage(WM_INPUTLANGCHANGEREQUEST)</c> against the target window so the change
    /// applies to that app's input thread - <c>ActivateKeyboardLayout</c> only affects our own
    /// thread and wouldn't move the user's actual typing layout. The destination HKL is resolved
    /// from the installed layout list; if the requested language isn't installed, this is a no-op.
    /// </summary>
    internal static class LayoutSwitcher
    {
        private const int LANG_EN = 0x09; // primary LANGID for English
        private const int LANG_RU = 0x19; // primary LANGID for Russian

        /// <summary>Switch <paramref name="hwnd"/>'s layout to the opposite Latin/Cyrillic language.</summary>
        public static void SwitchAfterFlip(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            IntPtr current = GetKeyboardLayout(threadId);
            int currentLang = (int)((long)current & 0x3FF); // primary language id

            // EN → RU; any non-English layout → EN.
            int targetLang = currentLang == LANG_EN ? LANG_RU : LANG_EN;

            IntPtr target = FindLayout(targetLang);
            if (target == IntPtr.Zero) return; // requested layout not installed

            PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
        }

        /// <summary>First installed HKL whose primary language id matches, or zero if none.</summary>
        private static IntPtr FindLayout(int primaryLangId)
        {
            int count = (int)GetKeyboardLayoutList(0, null);
            if (count <= 0) return IntPtr.Zero;

            var list = new IntPtr[count];
            GetKeyboardLayoutList(count, list);

            foreach (IntPtr hkl in list)
            {
                if ((int)((long)hkl & 0x3FF) == primaryLangId)
                    return hkl;
            }
            return IntPtr.Zero;
        }
    }
}
