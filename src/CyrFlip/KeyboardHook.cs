using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Global low-level keyboard hook (WH_KEYBOARD_LL). Detects the configured hotkeys and raises
    /// <see cref="CaseHotkeyPressed"/> (case flip), <see cref="ClipboardHistoryHotkeyPressed"/> or
    /// <see cref="LayoutConversionHotkeyPressed"/> (any row of the layout-conversion table, the
    /// EN ⇄ RU flip included). (spec §2.1)
    ///
    /// The callback stays minimal (spec §5.4): it checks the chords and returns. The actual
    /// copy/transform/paste work is done by the subscriber off the hook, and injected
    /// keystrokes (LLKHF_INJECTED) are ignored so our own SendInput can't re-enter the hook.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        /// <summary>Raised when the case-flip (fix CapsLock) hotkey is pressed.</summary>
        public event EventHandler? CaseHotkeyPressed;
        /// <summary>Raised when the clipboard-history window should be shown or hidden.</summary>
        public event EventHandler? ClipboardHistoryHotkeyPressed;
        /// <summary>Raised with the id of a user-configured layout conversion profile.</summary>
        public event Action<string>? LayoutConversionHotkeyPressed;
        /// <summary>Raised with the id of a launcher scenario whose per-scenario chord matched.</summary>
        public event Action<Guid>? LauncherHotkeyPressed;
        /// <summary>Raised with the id of a translation row whose chord matched.</summary>
        public event Action<string>? TranslateHotkeyPressed;
        /// <summary>
        /// Raised on Escape while <see cref="UpdateCancelKeyWatch"/> is on. The translation popup
        /// deliberately never takes focus, so it never receives a key of its own - without this the
        /// "Esc to cancel" it promises could not work at all. The key is <b>not</b> swallowed: Escape
        /// still reaches the window the user is typing in.
        /// </summary>
        public event EventHandler? CancelKeyPressed;

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hook = IntPtr.Zero;
        private Hotkey _caseHotkey = Hotkey.CaseDefault;
        private Hotkey _clipboardHistoryHotkey = new Hotkey(true, true, false, false, 0x79, "F10");
        private bool _deferInRemoteClient;
        private bool _enabled = true;
        // Per-hotkey switches for the two fixed chords, so e.g. a machine can keep only the
        // clipboard-history hotkey. Conversion rows carry their own switch inside the profile.
        private bool _caseEnabled = true;
        private bool _historyEnabled = true;
        private ConversionBinding[] _conversionProfiles = new ConversionBinding[0];
        // Launcher scenario chords. Same discipline as the conversion table: an immutable snapshot
        // array swapped atomically, empty whenever the launcher is off, so the callback never pays
        // for a feature that is disabled and never walks a list the UI is editing.
        private LauncherBinding[] _launcherHotkeys = new LauncherBinding[0];
        // Translation rows, same discipline again; the caller passes an empty set while the
        // translator is off, so a disabled feature costs the callback nothing.
        private TranslationBinding[] _translationProfiles = new TranslationBinding[0];
        // On only while a translation is streaming, so an ordinary Escape costs one field read.
        private bool _watchCancelKey;
        private const int VK_ESCAPE = 0x1B;

        public void Install(Hotkey caseHotkey, Hotkey clipboardHistoryHotkey,
            bool deferInRemoteClient, bool enabled, bool caseEnabled, bool historyEnabled)
        {
            _caseHotkey = caseHotkey;
            _clipboardHistoryHotkey = clipboardHistoryHotkey;
            _deferInRemoteClient = deferInRemoteClient;
            _enabled = enabled;
            _caseEnabled = caseEnabled;
            _historyEnabled = historyEnabled;
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
                    // Also pass everything through when hotkey listening is switched off in settings.
                    if (_enabled && (data.flags & LLKHF_INJECTED) == 0)
                    {
                        // Escape while a translation is running: tell the subscriber and pass the key
                        // on regardless - it belongs to whatever the user is actually typing in.
                        if (_watchCancelKey && data.vkCode == VK_ESCAPE)
                            CancelKeyPressed?.Invoke(this, EventArgs.Empty);

                        // Each hotkey is matched only when its own switch is on (so a machine can, say,
                        // keep just the clipboard-history hotkey and let the others pass through).
                        bool caseMatch = _caseEnabled && Matches(_caseHotkey, data.vkCode);
                        bool historyMatch = _historyEnabled && Matches(_clipboardHistoryHotkey, data.vkCode);
                        LayoutConversionProfile? conversion = FindConversion(data.vkCode);
                        Guid? launcher = FindLauncher(data.vkCode);
                        TranslationProfile? translation = FindTranslation(data.vkCode);

                        // When a remote-desktop client is focused and deferral is on, don't touch the
                        // key: let it travel to the remote session, whose CyrFlip will handle it.
                        // Otherwise the local instance would swallow the trigger and inject a Ctrl+C
                        // that leaks into the remote as Ctrl+Shift+C. (Checked only on a real chord so
                        // the extra process lookup never runs on ordinary keystrokes.)
                        if ((caseMatch || historyMatch || conversion != null || launcher != null || translation != null)
                            && _deferInRemoteClient && RemoteDesktop.IsClientForeground())
                            return CallNextHookEx(_hook, nCode, wParam, lParam);

                        if (caseMatch)
                        {
                            CaseHotkeyPressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1; // swallow the trigger key so the app under focus never sees it
                        }
                        if (historyMatch)
                        {
                            ClipboardHistoryHotkeyPressed?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1;
                        }
                        if (conversion != null)
                        {
                            LayoutConversionHotkeyPressed?.Invoke(conversion.Id);
                            return (IntPtr)1;
                        }
                        if (launcher != null)
                        {
                            LauncherHotkeyPressed?.Invoke(launcher.Value);
                            return (IntPtr)1;
                        }
                        if (translation != null)
                        {
                            TranslateHotkeyPressed?.Invoke(translation.Id);
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

        private LayoutConversionProfile? FindConversion(uint vkCode)
        {
            // The array is replaced atomically by UpdateConversionProfiles; its entries are immutable
            // snapshots, so the hook never enumerates a collection being edited by the UI.
            foreach (ConversionBinding binding in _conversionProfiles)
            {
                LayoutConversionProfile profile = binding.Profile;
                if (!profile.Enabled || !profile.IsUsable) continue;
                if (Matches(binding.Hotkey, vkCode)) return profile;
            }
            return null;
        }

        private Guid? FindLauncher(uint vkCode)
        {
            // Same snapshot discipline as FindConversion: the array is replaced atomically and its
            // entries are immutable, so the hook never observes a half-edited scenario list.
            foreach (LauncherBinding binding in _launcherHotkeys)
                if (Matches(binding.Hotkey, vkCode))
                    return binding.Id;
            return null;
        }

        private TranslationProfile? FindTranslation(uint vkCode)
        {
            // Snapshot discipline as above; rows keep their own switch, like conversion rows do.
            foreach (TranslationBinding binding in _translationProfiles)
            {
                TranslationProfile profile = binding.Profile;
                if (!profile.Enabled || !profile.IsUsable) continue;
                if (Matches(binding.Hotkey, vkCode)) return profile;
            }
            return null;
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Change the matched case-flip hotkey without reinstalling the hook. Safe to call from any
        /// thread since the hook callback reads the field on each invocation.
        /// </summary>
        public void UpdateCaseHotkey(Hotkey hotkey) => _caseHotkey = hotkey;
        public void UpdateClipboardHistoryHotkey(Hotkey hotkey) => _clipboardHistoryHotkey = hotkey;

        /// <summary>Toggle yielding the hotkeys to the remote session when an RDP client is focused.</summary>
        public void UpdateDeferInRemoteClient(bool defer) => _deferInRemoteClient = defer;

        /// <summary>Master switch: when false the hook passes every key through (no hotkeys act).</summary>
        public void UpdateEnabled(bool enabled) => _enabled = enabled;

        /// <summary>Per-hotkey switches (thread-safe field writes, read on each callback).</summary>
        public void UpdateCaseEnabled(bool enabled) => _caseEnabled = enabled;
        public void UpdateHistoryEnabled(bool enabled) => _historyEnabled = enabled;

        /// <summary>Watch for Escape (see <see cref="CancelKeyPressed"/>) - on only while translating.</summary>
        public void UpdateCancelKeyWatch(bool watch) => _watchCancelKey = watch;

        /// <summary>Replace the custom conversion hotkeys without reinstalling the global hook.</summary>
        public void UpdateConversionProfiles(IEnumerable<LayoutConversionProfile> profiles)
        {
            var copies = new List<ConversionBinding>();
            foreach (LayoutConversionProfile profile in profiles)
            {
                if (profile == null || !profile.IsUsable) continue;
                // TryParse, not Parse: a corrupt saved chord must stay inert rather than fall back to
                // the default and quietly bind itself to Ctrl+Shift+F12.
                if (!Hotkey.TryParse(profile.Hotkey, out Hotkey chord)) continue;
                copies.Add(new ConversionBinding(profile.Clone(), chord));
            }
            _conversionProfiles = copies.ToArray();
        }

        /// <summary>
        /// Replace the launcher scenario chords without reinstalling the global hook. The caller
        /// passes an empty set whenever the launcher is disabled, so a disabled launcher costs the
        /// callback nothing. TryParse, not Parse: a corrupt saved chord stays inert rather than
        /// quietly becoming Ctrl+Shift+F12.
        /// </summary>
        public void UpdateLauncherHotkeys(IEnumerable<(Guid id, string chord)> bindings)
        {
            var copies = new List<LauncherBinding>();
            foreach ((Guid id, string chord) in bindings)
            {
                if (!Hotkey.TryParse(chord, out Hotkey parsed)) continue;
                copies.Add(new LauncherBinding(id, parsed));
            }
            _launcherHotkeys = copies.ToArray();
        }

        /// <summary>
        /// Replace the translation chords without reinstalling the global hook. The caller passes an
        /// empty set whenever the translator is off. TryParse, not Parse, for the same reason as above.
        /// </summary>
        public void UpdateTranslationProfiles(IEnumerable<TranslationProfile> profiles)
        {
            var copies = new List<TranslationBinding>();
            foreach (TranslationProfile profile in profiles)
            {
                if (profile == null || !profile.IsUsable) continue;
                if (!Hotkey.TryParse(profile.Hotkey, out Hotkey chord)) continue;
                copies.Add(new TranslationBinding(profile.Clone(), chord));
            }
            _translationProfiles = copies.ToArray();
        }

        private sealed class ConversionBinding
        {
            public readonly LayoutConversionProfile Profile;
            public readonly Hotkey Hotkey;
            public ConversionBinding(LayoutConversionProfile profile, Hotkey hotkey) { Profile = profile; Hotkey = hotkey; }
        }

        private sealed class TranslationBinding
        {
            public readonly TranslationProfile Profile;
            public readonly Hotkey Hotkey;
            public TranslationBinding(TranslationProfile profile, Hotkey hotkey) { Profile = profile; Hotkey = hotkey; }
        }

        private sealed class LauncherBinding
        {
            public readonly Guid Id;
            public readonly Hotkey Hotkey;
            public LauncherBinding(Guid id, Hotkey hotkey) { Id = id; Hotkey = hotkey; }
        }

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
