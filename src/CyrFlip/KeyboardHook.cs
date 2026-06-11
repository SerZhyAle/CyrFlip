using System;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Global low-level keyboard hook (WH_KEYBOARD_LL). Detects the configured hotkey
    /// and raises <see cref="HotkeyPressed"/>. (spec §2.1)
    ///
    /// The callback stays minimal (spec §5.4): it checks the chord and returns. The actual
    /// copy/transliterate/paste work is done by the subscriber off the hook, and injected
    /// keystrokes (LLKHF_INJECTED) are ignored so our own SendInput can't re-enter the hook.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        public event EventHandler? HotkeyPressed;

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hook = IntPtr.Zero;
        private Hotkey _hotkey = Hotkey.Default;

        public void Install(Hotkey hotkey)
        {
            _hotkey = hotkey;
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
            // A low-level hook proc must never throw — an exception here can drop the hook.
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    // Ignore our own synthesized input — never treat it as the hotkey.
                    if ((data.flags & LLKHF_INJECTED) == 0 && Matches(data.vkCode))
                    {
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1; // swallow the trigger key so the app under focus never sees it
                    }
                }
            }
            catch { /* swallow — keep the hook alive */ }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool Matches(uint vkCode)
        {
            if ((int)vkCode != _hotkey.Vk)
                return false;

            // Require exactly the configured modifiers — no more, no less.
            return Down(Hotkey.VK_CONTROL) == _hotkey.Ctrl
                && Down(Hotkey.VK_SHIFT) == _hotkey.Shift
                && Down(Hotkey.VK_MENU) == _hotkey.Alt
                && (Down(Hotkey.VK_LWIN) || Down(Hotkey.VK_RWIN)) == _hotkey.Win;
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

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
