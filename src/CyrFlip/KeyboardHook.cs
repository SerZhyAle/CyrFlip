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
        /// <summary>Raised when a new quick note should be opened, ready to type into.</summary>
        public event EventHandler? QuickNotesHotkeyPressed;
        /// <summary>Raised when a screen region capture should start (S0026).</summary>
        public event EventHandler? ScreenshotHotkeyPressed;
        /// <summary>The keyboard's way to the text context menu (S0045 K2): opens it at the caret.</summary>
        public event EventHandler? TextMenuHotkeyPressed;
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
        /// <summary>
        /// Raised on a bare F6 while <see cref="UpdateFocusKeyWatch"/> is on - i.e. while the finished
        /// translation popup is up (ticket S0045 K1, <c>INPUT-PARITY</c> rule 1). The popup never takes the
        /// focus by itself, so F6 - Windows' "next pane" key - is the keyboard's way into it and back out.
        /// Unlike Escape the key <b>is</b> swallowed, down, repeats and up: it is a command to us then.
        /// </summary>
        public event EventHandler? FocusKeyPressed;
        /// <summary>
        /// Raised, before the chord's own event, whenever a chord fires and its trigger is swallowed,
        /// with the modifiers held at that moment. The subscriber taps the mask key (on the UI thread,
        /// never in here): Windows never saw the trigger, so to it the user pressed and released the
        /// modifiers with nothing in between - which Ctrl+Shift or Alt+Shift turns into a layout
        /// switch and a lone Alt into the menu bar (ticket S0004, KC-4).
        /// </summary>
        public event Action<SideModifiers>? ChordFired;

        /// <summary>
        /// When true (e.g. while <see cref="HotkeyDialog"/> is capturing a key), chords are passed
        /// through rather than swallowed and triggered (ticket S0033, KC2-4).
        /// </summary>
        public static volatile bool SuspendChords;

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hook = IntPtr.Zero;
        // "Should be installed" - set by Install, cleared by Dispose. Never inferred from _hook: a
        // re-arm that failed leaves _hook describing a hook that may be dead, and the old
        // "_hook == 0 means nothing to keep alive" rule is exactly how one failure lost the keyboard
        // hook for good (KC-3).
        private bool _wanted;
        // The two Win32 calls behind a seam, so the re-arm rules are tested without a desktop.
        private readonly Func<LowLevelKeyboardProc, IntPtr> _setHook;
        private readonly Func<IntPtr, bool> _unhook;
        private readonly ChordMatcher _matcher;
        private Hotkey _caseHotkey = Hotkey.CaseDefault;
        private Hotkey _clipboardHistoryHotkey = new Hotkey(true, true, false, false, 0x79, "F10");
        private Hotkey _quickNotesHotkey = Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey);
        private bool _deferInRemoteClient;
        private bool _enabled = true;
        // Per-hotkey switches for the two fixed chords, so e.g. a machine can keep only the
        // clipboard-history hotkey. Conversion rows carry their own switch inside the profile.
        private bool _caseEnabled = true;
        private bool _historyEnabled = true;
        // Off unless the quick notes are switched on at all - the same discipline the tables use:
        // a feature nobody enabled costs the callback one field read and nothing else.
        private bool _quickNotesEnabled;
        // The screen region capture's chord - off until the context binds it from the config.
        private Hotkey _screenshotHotkey = Hotkey.Parse(AppConfig.DefaultScreenshotHotkey);
        private Hotkey _textMenuHotkey = Hotkey.Parse(AppConfig.DefaultTextMenuHotkey);
        // Off unless the context menu and the chord's own switch are both on.
        private bool _textMenuEnabled;
        private bool _screenshotEnabled;
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
        // On only while the finished translation popup is visible.
        private bool _watchFocusKey;
        private const int VK_F6 = 0x75;


        public KeyboardHook()
            : this(proc => SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0),
                   UnhookWindowsHookEx, PhysicalModifiers.Shared)
        {
        }

        /// <summary>Test seam: the hook calls and the modifier table come from the caller.</summary>
        internal KeyboardHook(Func<LowLevelKeyboardProc, IntPtr> setHook, Func<IntPtr, bool> unhook,
            PhysicalModifiers modifiers)
        {
            _setHook = setHook;
            _unhook = unhook;
            _matcher = new ChordMatcher(modifiers);
        }

        public void Install(Hotkey caseHotkey, Hotkey clipboardHistoryHotkey, Hotkey quickNotesHotkey,
            bool deferInRemoteClient, bool enabled, bool caseEnabled, bool historyEnabled, bool quickNotesEnabled)
        {
            _caseHotkey = caseHotkey;
            _clipboardHistoryHotkey = clipboardHistoryHotkey;
            _quickNotesHotkey = quickNotesHotkey;
            _deferInRemoteClient = deferInRemoteClient;
            _enabled = enabled;
            _caseEnabled = caseEnabled;
            _historyEnabled = historyEnabled;
            _quickNotesEnabled = quickNotesEnabled;
            if (_hook != IntPtr.Zero)
                return;

            // Keep a reference to the delegate for the lifetime of the hook (else it's GC'd).
            _proc = HookCallback;
            _hook = _setHook(_proc);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install keyboard hook: " + Marshal.GetLastWin32Error());
            _wanted = true;
            RefreshModifiers();
        }

        /// <summary>True once <see cref="Install"/> has put the hook in place.</summary>
        public bool Installed => _hook != IntPtr.Zero;

        /// <summary>
        /// Take the hook down and put it straight back up. <b>Windows can drop a low-level hook
        /// without telling anybody:</b> the callback runs on the thread that installed it - the tray
        /// UI thread - and if that thread does not answer within <c>LowLevelHooksTimeout</c>
        /// (~300 ms by default) the hook is silently removed. Nothing is raised, nothing fails, and
        /// every chord in the app is simply dead until the next restart. That is the "my hotkeys
        /// stopped working after a while" report, and there is no API to ask whether we are still
        /// hooked - so the only defence is to re-arm periodically.
        ///
        /// <para>Must be called from the thread that owns the hook (a hook can only be removed by
        /// its own thread), which is why <see cref="CyrFlipContext"/> drives it from a WinForms
        /// timer.</para>
        ///
        /// <para><b>The new hook goes in before the old one comes out</b>, and only a successful
        /// install removes the old one (KC-3). A failed <c>SetWindowsHookEx</c> keeps the old handle
        /// - it may well still be alive - and returns false, every time, so the caller's failure
        /// counter climbs and the next tick tries again. The two hooks never both run for one event:
        /// hook callbacks are dispatched through this thread's message loop, which is busy running
        /// this method.</para>
        /// </summary>
        /// <param name="refreshModifiers">
        /// Re-read the physical modifier table from the system. The caller passes false while a
        /// clipboard operation is synthesizing keys - the system state is false for exactly that long.
        /// </param>
        public bool Reinstall(bool refreshModifiers = true)
        {
            if (!_wanted || _proc == null) return true;

            IntPtr fresh = _setHook(_proc);
            if (fresh == IntPtr.Zero) return false;

            if (_hook != IntPtr.Zero) _unhook(_hook);
            _hook = fresh;
            if (refreshModifiers) RefreshModifiers();
            return true;
        }

        /// <summary>
        /// Re-read which modifiers are held (install, re-arm, session unlock): a key pressed or
        /// released while the hook was not listening would otherwise stay wrong in the table. A
        /// trigger held across the re-arm stays owned (KC2-2).
        /// </summary>
        public void RefreshModifiers()
        {
            _matcher.Modifiers.Refresh(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);
            _matcher.Reset(unchecked((uint)Environment.TickCount));
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // A low-level hook proc must never throw - an exception here can drop the hook.
            try
            {
                if (nCode >= 0)
                {
                    int message = (int)wParam;
                    bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                    if (down || message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                        // The caret overlay follows the caret only while somebody is typing (S0011
                        // LI-1) - one field write, well inside the callback's budget.
                        if (down && (data.flags & LLKHF_INJECTED) == 0)
                            CaretQueryGate.NoteKey();
                        if (Decide(data, down)) return (IntPtr)1;
                    }
                }
            }
            catch { /* swallow - keep the hook alive */ }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// The whole per-event decision: true = swallow. Everything but the matching itself is in
        /// <see cref="ChordMatcher"/>; this part only knows which chords are bound and whom to tell.
        /// </summary>
        internal bool Decide(KBDLLHOOKSTRUCT data, bool down)
        {
            switch (_matcher.Observe(data.vkCode, data.scanCode, data.flags, down, data.time, data.dwExtraInfo))
            {
                case ChordMatcher.Verdict.Swallow: return true;
                case ChordMatcher.Verdict.Pass: return false;
            }

            // Escape while a translation is running: tell the subscriber and pass the key on
            // regardless - it belongs to whatever the user is actually typing in. Ahead of the master
            // switch (KC-8): a translation started from the tray or the context menu promises
            // "Esc - cancel" with the hotkeys off too, and this branch never swallows anything.
            if (_watchCancelKey && data.vkCode == VK_ESCAPE)
                CancelKeyPressed?.Invoke(this, EventArgs.Empty);

            // F6 while the popup is up (S0045 K1): ahead of the master switch like Escape, since the
            // popup also opens from the tray with the hotkeys off. Only a bare F6 - Ctrl+F6, Shift+F6
            // and the rest keep their meaning in the user's window.
            // The matcher then owns its repeats and its release, exactly as for a chord's trigger.
            if (down && data.vkCode == VK_F6 && _watchFocusKey && _matcher.Modifiers.Held == SideModifiers.None)
            {
                _matcher.Fired(VK_F6, data.time);
                FocusKeyPressed?.Invoke(this, EventArgs.Empty);
                return true;
            }

            // Pass everything through when hotkey listening is switched off in settings or suspended.
            if (!_enabled || SuspendChords) return false;

            uint vk = data.vkCode;
            // Each hotkey is matched only when its own switch is on (so a machine can, say,
            // keep just the clipboard-history hotkey and let the others pass through).
            bool caseMatch = _caseEnabled && _matcher.Matches(_caseHotkey, vk);
            bool historyMatch = _historyEnabled && _matcher.Matches(_clipboardHistoryHotkey, vk);
            bool notesMatch = _quickNotesEnabled && _matcher.Matches(_quickNotesHotkey, vk);
            bool screenshotMatch = _screenshotEnabled && _matcher.Matches(_screenshotHotkey, vk);
            bool textMenuMatch = _textMenuEnabled && _matcher.Matches(_textMenuHotkey, vk);
            LayoutConversionProfile? conversion = FindConversion(vk);
            Guid? launcher = FindLauncher(vk);
            TranslationProfile? translation = FindTranslation(vk);

            if (!caseMatch && !historyMatch && !notesMatch && !screenshotMatch && !textMenuMatch && conversion == null && launcher == null && translation == null)
                return false;

            // When a remote-desktop client is focused and deferral is on, don't touch the key: let
            // it travel to the remote session, whose CyrFlip will handle it. Otherwise the local
            // instance would swallow the trigger and inject a Ctrl+C that leaks into the remote as
            // Ctrl+Shift+C. (Checked only on a real chord so the extra process lookup never runs on
            // ordinary keystrokes.)
            if (_deferInRemoteClient && RemoteDesktop.IsClientForeground())
                return false;

            // From here the key is swallowed, and with it its repeats and its release.
            _matcher.Fired(vk, data.time);
            ChordFired?.Invoke(_matcher.Modifiers.Held);

            if (caseMatch) CaseHotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (historyMatch) ClipboardHistoryHotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (notesMatch) QuickNotesHotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (screenshotMatch) ScreenshotHotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (textMenuMatch) TextMenuHotkeyPressed?.Invoke(this, EventArgs.Empty);
            else if (conversion != null) LayoutConversionHotkeyPressed?.Invoke(conversion.Id);
            else if (launcher != null) LauncherHotkeyPressed?.Invoke(launcher.Value);
            else TranslateHotkeyPressed?.Invoke(translation!.Id);
            return true;
        }

        private LayoutConversionProfile? FindConversion(uint vkCode)
        {
            // The array is replaced atomically by UpdateConversionProfiles; its entries are immutable
            // snapshots, so the hook never enumerates a collection being edited by the UI.
            foreach (ConversionBinding binding in _conversionProfiles)
            {
                LayoutConversionProfile profile = binding.Profile;
                if (!profile.Enabled || !profile.IsUsable) continue;
                if (_matcher.Matches(binding.Hotkey, vkCode)) return profile;
            }
            return null;
        }

        private Guid? FindLauncher(uint vkCode)
        {
            // Same snapshot discipline as FindConversion: the array is replaced atomically and its
            // entries are immutable, so the hook never observes a half-edited scenario list.
            foreach (LauncherBinding binding in _launcherHotkeys)
                if (_matcher.Matches(binding.Hotkey, vkCode))
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
                if (_matcher.Matches(binding.Hotkey, vkCode)) return profile;
            }
            return null;
        }


        /// <summary>
        /// Change the matched case-flip hotkey without reinstalling the hook. Safe to call from any
        /// thread since the hook callback reads the field on each invocation.
        /// </summary>
        public void UpdateCaseHotkey(Hotkey hotkey) => _caseHotkey = hotkey;
        public void UpdateClipboardHistoryHotkey(Hotkey hotkey) => _clipboardHistoryHotkey = hotkey;
        public void UpdateQuickNotesHotkey(Hotkey hotkey) => _quickNotesHotkey = hotkey;

        /// <summary>Toggle yielding the hotkeys to the remote session when an RDP client is focused.</summary>
        public void UpdateDeferInRemoteClient(bool defer) => _deferInRemoteClient = defer;

        /// <summary>Master switch: when false the hook passes every key through (no hotkeys act).</summary>
        public void UpdateEnabled(bool enabled) => _enabled = enabled;

        /// <summary>Per-hotkey switches (thread-safe field writes, read on each callback).</summary>
        public void UpdateCaseEnabled(bool enabled) => _caseEnabled = enabled;
        public void UpdateHistoryEnabled(bool enabled) => _historyEnabled = enabled;
        /// <summary>False whenever the quick notes are off <b>or</b> their own chord switch is.</summary>
        public void UpdateQuickNotesEnabled(bool enabled) => _quickNotesEnabled = enabled;
        public void UpdateScreenshotHotkey(Hotkey hotkey) => _screenshotHotkey = hotkey;
        public void UpdateTextMenuHotkey(Hotkey hotkey) => _textMenuHotkey = hotkey;
        /// <summary>False whenever the context menu is off <b>or</b> this chord's own switch is.</summary>
        public void UpdateTextMenuEnabled(bool enabled) => _textMenuEnabled = enabled;
        /// <summary>The capture chord's own switch.</summary>
        public void UpdateScreenshotEnabled(bool enabled) => _screenshotEnabled = enabled;

        /// <summary>Watch for Escape (see <see cref="CancelKeyPressed"/>) - on only while translating.</summary>
        public void UpdateCancelKeyWatch(bool watch) => _watchCancelKey = watch;

        /// <summary>Watch for a bare F6 (see <see cref="FocusKeyPressed"/>) - on only while the popup is up.</summary>
        public void UpdateFocusKeyWatch(bool watch) => _watchFocusKey = watch;

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
            _wanted = false;
            if (_hook != IntPtr.Zero)
            {
                _unhook(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }
    }
}
