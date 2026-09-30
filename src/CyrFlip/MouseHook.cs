using System;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Global low-level mouse hook (WH_MOUSE_LL) behind CyrFlip's own text context menu (spec §5).
    /// Detects the configured <see cref="MouseChord"/>, swallows it so the application under the
    /// pointer never shows its own menu, and raises <see cref="ChordPressed"/> / <see cref="ChordReleased"/>.
    ///
    /// Four things here are load-bearing:
    ///
    /// 1. <b>The callback sees every mouse move</b> - up to 1000 events a second with a gaming mouse.
    ///    Everything that is not one of our button messages leaves through the switch below before a
    ///    single byte is marshalled. A GC pause on this thread stalls the pointer system-wide, and
    ///    Windows silently drops a hook that takes longer than <c>LowLevelHooksTimeout</c> (300 ms).
    /// 2. <b>Nothing is shown from here.</b> The subscriber posts to the UI thread; opening a menu
    ///    inside the callback would run a foreign message loop inside a hook.
    /// 3. <b>Both the down and the up are swallowed</b> (the second down of a fast double click is
    ///    swallowed like the first): most applications open their menu on <c>WM_CONTEXTMENU</c>,
    ///    which arrives after the button is released, so a lone up would raise their menu over ours.
    /// 4. <b>The up is swallowed by flag, not by re-checking the modifiers</b> - the user may well let
    ///    Ctrl go before the button.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        /// <summary>The chord went down (screen coordinates). Time to start probing the selection.</summary>
        public event Action<int, int>? ChordPressed;
        /// <summary>The chord was released (screen coordinates). Time to show the menu.</summary>
        public event Action<int, int>? ChordReleased;
        /// <summary>
        /// Some other button went down while <see cref="UpdateForeignClickWatch"/> is on - i.e. while
        /// the menu is open. The click is passed through untouched; the subscriber decides whether it
        /// landed outside the menu and should close it. A drop-down owned by a process that is not in
        /// the foreground does not get outside clicks of its own, so this is how it closes at all.
        /// </summary>
        public event Action<int, int>? ForeignButtonDown;

        private LowLevelMouseProc? _proc;
        private IntPtr _hook = IntPtr.Zero;
        private MouseChord _chord = MouseChord.Default;
        private readonly SwallowUpGate _swallowUp = new SwallowUpGate();
        // "Should be installed", for the same reason as KeyboardHook._wanted (ticket S0004, KC-3).
        private bool _wanted;
        private bool _watchForeignClicks;

        /// <summary>Install the hook. Called only while the feature is on - see the class remarks.</summary>
        public void Install(MouseChord chord)
        {
            _chord = chord;
            if (_hook != IntPtr.Zero)
                return;

            // Keep a reference to the delegate for the lifetime of the hook (else it's GC'd).
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install mouse hook: " + Marshal.GetLastWin32Error());
            _wanted = true;
        }

        public bool Installed => _hook != IntPtr.Zero;

        /// <summary>
        /// Re-arm the hook, for the same reason as <see cref="KeyboardHook.Reinstall"/>: Windows
        /// drops a low-level hook that overruns <c>LowLevelHooksTimeout</c> and says nothing, after
        /// which the context menu never opens again. Same rules too: the new hook goes in before the
        /// old one comes out, and a failure keeps the old handle and says so every time.
        ///
        /// <para><b>Skipped while the chord is held down.</b> The swallow-up flag is what makes
        /// the button-up get swallowed together with the down; re-arming between the two would be
        /// harmless for the flag itself (it is a field, not hook state), but the fresh hook sits at
        /// the head of the chain and there is no reason to disturb an interaction in flight. A flag
        /// older than <see cref="SwallowUpGate.ExpiryMs"/> is not an interaction in flight but an up
        /// that never arrived (KC-7), and is cleared here instead.</para>
        /// </summary>
        public bool Reinstall()
        {
            if (!_wanted || _proc == null) return true;
            uint now = unchecked((uint)Environment.TickCount);
            if (_swallowUp.IsArmed(now)) return true;
            _swallowUp.Clear();

            IntPtr fresh = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (fresh == IntPtr.Zero) return false;
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = fresh;
            return true;
        }

        /// <summary>Change the chord without reinstalling (the callback reads the field each time).</summary>
        public void UpdateChord(MouseChord chord) => _chord = chord;

        /// <summary>On only while the menu is open, so an ordinary click costs the callback nothing.</summary>
        public void UpdateForeignClickWatch(bool watch)
        {
            _watchForeignClicks = watch;
            if (!watch) _swallowUp.Clear();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // A low-level hook proc must never throw - an exception here can drop the hook.
            try
            {
                if (nCode < 0)
                    return CallNextHookEx(_hook, nCode, wParam, lParam);

                // The hot path: every WM_MOUSEMOVE and every wheel notification leaves here, before
                // any marshalling, on a jump table over a small int.
                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                    case WM_RBUTTONUP:
                    case WM_MBUTTONDOWN:
                    case WM_MBUTTONUP:
                        break;
                    default:
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                int msg = (int)wParam;
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Ignore synthesized clicks - ours and anybody else's.
                if ((data.flags & LLMHF_INJECTED) != 0)
                    return CallNextHookEx(_hook, nCode, wParam, lParam);

                bool right = _chord.Button == MouseChordButton.Right;
                int downMsg = right ? WM_RBUTTONDOWN : WM_MBUTTONDOWN;
                int upMsg = right ? WM_RBUTTONUP : WM_MBUTTONUP;
                if (msg == downMsg && ModifiersMatch())
                {
                    _swallowUp.Arm(data.time);
                    ChordPressed?.Invoke(data.pt.X, data.pt.Y);
                    return (IntPtr)1; // swallow, so the app under the pointer shows no menu of its own
                }

                if (msg == upMsg && _swallowUp.TakeUp(data.time))
                {
                    ChordReleased?.Invoke(data.pt.X, data.pt.Y);
                    return (IntPtr)1;
                }

                if (_watchForeignClicks
                    && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN))
                    ForeignButtonDown?.Invoke(data.pt.X, data.pt.Y);
            }
            catch { /* swallow - keep the hook alive */ }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // The keyboard hook's physical table, not GetAsyncKeyState: after a flip our own injected
        // key-ups make the system report a held Ctrl as released (ticket S0004, KC-1).
        private bool ModifiersMatch()
        {
            SideModifiers held = PhysicalModifiers.Shared.Snapshot();
            return _chord.Matches((held & SideModifiers.Ctrl) != 0, (held & SideModifiers.Shift) != 0,
                (held & SideModifiers.Alt) != 0, (held & SideModifiers.Win) != 0);
        }

        public void Dispose()
        {
            _wanted = false;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
            _swallowUp.Clear();
            _watchForeignClicks = false;
        }
    }

    /// <summary>
    /// "Swallow the next button-up" - the flag that keeps the application under the pointer from
    /// opening its own menu after ours (see <see cref="MouseHook"/>, rule 4), with an expiry
    /// (ticket S0004, KC-7). When the button is released over an elevated window, UIPI hides the up
    /// from the hook; without the expiry the flag stayed set, the next ordinary right click anywhere
    /// was swallowed and opened CyrFlip's menu, and the watchdog skipped its re-arm until then.
    /// Times are the hook's millisecond tick (<c>MSLLHOOKSTRUCT.time</c>), compared with wrap-around.
    /// </summary>
    internal sealed class SwallowUpGate
    {
        /// <summary>No real press-and-hold of a context-menu chord lasts this long.</summary>
        public const uint ExpiryMs = 3000;

        private volatile bool _armed;
        private uint _armedAt;

        public void Arm(uint time)
        {
            _armedAt = time;
            _armed = true;
        }

        /// <summary>A button-up arrived: true = it is the chord's, swallow it. Disarms either way.</summary>
        public bool TakeUp(uint time)
        {
            if (!_armed) return false;
            _armed = false;
            return unchecked(time - _armedAt) < ExpiryMs;
        }

        /// <summary>True while a chord is (still plausibly) held down.</summary>
        public bool IsArmed(uint now) => _armed && unchecked(now - _armedAt) < ExpiryMs;

        public void Clear() => _armed = false;
    }
}
