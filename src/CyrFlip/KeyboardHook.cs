using System;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Global low-level keyboard hook (WH_KEYBOARD_LL). Detects the configured hotkeys and
    /// raises <see cref="HotkeyPressed"/> (transliteration flip) or <see cref="CaseHotkeyPressed"/>
    /// (case flip). (spec §2.1)
    ///
    /// The callback stays minimal (spec §5.4): it checks the chords and returns. The actual
    /// copy/transform/paste work is done by the subscriber off the hook, and injected
    /// keystrokes (LLKHF_INJECTED) are ignored so our own SendInput can't re-enter the hook.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        /// <summary>Raised when the transliteration flip hotkey is pressed.</summary>
        public event EventHandler? HotkeyPressed;
        /// <summary>Raised when the case-flip (fix CapsLock) hotkey is pressed.</summary>
        public event EventHandler? CaseHotkeyPressed;

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hook = IntPtr.Zero;
        private Hotkey _hotkey = Hotkey.Default;
        private Hotkey _caseHotkey = Hotkey.CaseDefault;

        public void Install(Hotkey hotkey, Hotkey caseHotkey)
        {
            _hotkey = hotkey;
            _caseHotkey = caseHotkey;
            if (_hook != IntPtr.Zero)
                return;

            // Keep a reference to the delegate for the lifetime of the hook (else it's GC'd).
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install keyboard hook: " + Marshal.GetLastWin32Error());
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // A low-level hook proc must never throw - an exception here can drop the hook.
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    // Ignore our own synthesized input - never treat it as a hotkey.
                    if ((data.flags & LLKHF_INJECTED) == 0)
                    {
                        if (Matches(_hotkey, data.vkCode))
                        {
                            HotkeyPressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1; // swallow the trigger key so the app under focus never sees it
                        }
                        if (Matches(_caseHotkey, data.vkCode))
                        {
                            CaseHotkeyPressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch { /* swallow - keep the hook alive */ }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool Matches(Hotkey hotkey, uint vkCode)
        {
            if ((int)vkCode != hotkey.Vk)
                return false;

            // Require exactly the configured modifiers - no more, no less.
            return Down(Hotkey.VK_CONTROL) == hotkey.Ctrl
                && Down(Hotkey.VK_SHIFT) == hotkey.Shift
                && Down(Hotkey.VK_MENU) == hotkey.Alt
                && (Down(Hotkey.VK_LWIN) || Down(Hotkey.VK_RWIN)) == hotkey.Win;
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Change the matched flip hotkey without reinstalling the hook. Safe to call from any
        /// thread since the hook callback reads <c>_hotkey</c> on each invocation.
        /// </summary>
        public void UpdateHotkey(Hotkey hotkey) => _hotkey = hotkey;

        /// <summary>Change the matched case-flip hotkey without reinstalling the hook (thread-safe, as above).</summary>
        public void UpdateCaseHotkey(Hotkey hotkey) => _caseHotkey = hotkey;

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }
    }
}
