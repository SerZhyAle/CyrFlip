using System;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>The eight side-specific modifier keys, as one atomically readable value.</summary>
    [Flags]
    internal enum SideModifiers
    {
        None = 0,
        LCtrl = 1 << 0,
        RCtrl = 1 << 1,
        LShift = 1 << 2,
        RShift = 1 << 3,
        LAlt = 1 << 4,
        RAlt = 1 << 5,
        LWin = 1 << 6,
        RWin = 1 << 7,

        Ctrl = LCtrl | RCtrl,
        Shift = LShift | RShift,
        Alt = LAlt | RAlt,
        Win = LWin | RWin,
    }

    /// <summary>
    /// The modifier keys the user is <b>physically</b> holding, kept by the keyboard hook from the
    /// key events it sees (ticket S0004, KC-1).
    ///
    /// <para><c>GetAsyncKeyState</c> cannot answer this question once CyrFlip has synthesized
    /// anything: the key-ups <see cref="ClipboardHandler"/> injects before its Ctrl+C update that
    /// state too, so from then on a held Ctrl+Shift reads as released. Every symptom of KC-1 came from
    /// that one lie - the second tap of a held chord leaking a bare F12 into the app, the restore of
    /// the held modifiers never running, a right-hand key being "restored" as the left one.</para>
    ///
    /// <para>Every event updates the table <b>except CyrFlip's own injections</b>, which carry
    /// <see cref="KeyInjection.Tag"/> in <c>dwExtraInfo</c>. Other tools' injected modifiers do count
    /// - a remapper (PowerToys, AutoHotkey) that turns CapsLock into Ctrl has to keep working as Ctrl,
    /// exactly as it did while the answer came from <c>GetAsyncKeyState</c>.</para>
    ///
    /// <para><b>AltGr</b> arrives as a fake LCtrl (scan code <c>0x21D</c>) followed by RAlt. That
    /// LCtrl is not recorded as Ctrl (KC-5): it is tracked on its own, and while it is down the key
    /// is AltGr - a modifier no CyrFlip chord can contain, so nothing matches and the character
    /// reaches the app.</para>
    /// </summary>
    internal sealed class PhysicalModifiers
    {
        /// <summary>The process-wide table the keyboard hook feeds and the clipboard worker reads.</summary>
        public static readonly PhysicalModifiers Shared = new PhysicalModifiers();

        /// <summary>The scan code (0x200 flag + LCtrl's 0x1D) of the LCtrl AltGr sends ahead of RAlt.</summary>
        public const uint AltGrCtrlScan = 0x21D;

        // Side-specific virtual keys; the LL hook reports these rather than the generic ones.
        public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
            VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        private const uint RShiftScan = 0x36;

        // Written by the hook thread, read by the clipboard worker: an int and a bool, both volatile,
        // so a reader always sees one whole state and never a half-written one.
        private volatile int _held;
        private volatile bool _altGr;
        private volatile bool _fed;

        public SideModifiers Held => (SideModifiers)_held;

        /// <summary>True while AltGr's fake LCtrl is down.</summary>
        public bool AltGr => _altGr;

        /// <summary>
        /// The current state, or - before the hook has ever filled the table (a one-shot launcher
        /// process, a test) - what <c>GetAsyncKeyState</c> says, which is right as long as nothing
        /// has been synthesized yet.
        /// </summary>
        public SideModifiers Snapshot() => _fed ? Held : Read(IsDownAsync, out _);

        /// <summary>
        /// Update the table from one key event. Returns true when <paramref name="vk"/> is a modifier
        /// key (whether or not the event changed anything), so the caller never treats it as a trigger.
        /// </summary>
        public bool Track(uint vk, uint scanCode, uint flags, bool down)
        {
            SideModifiers side = SideOf(vk, scanCode, flags);
            if (side == SideModifiers.None)
                return false;

            if (side == SideModifiers.LCtrl && scanCode == AltGrCtrlScan)
            {
                _altGr = down;
                return true;
            }

            int held = _held;
            _held = down ? held | (int)side : held & ~(int)side;
            _fed = true;
            return true;
        }

        /// <summary>
        /// Re-read the table from the system - on install, on every watchdog re-arm and on session
        /// unlock, so a key held or released while the hook was not listening is not lost.
        /// </summary>
        public void Refresh(Func<int, bool> isDown)
        {
            _held = (int)Read(isDown, out bool altGr);
            _altGr = altGr;
            _fed = true;
        }

        private static SideModifiers Read(Func<int, bool> isDown, out bool altGr)
        {
            SideModifiers held = SideModifiers.None;
            if (isDown(VK_LSHIFT)) held |= SideModifiers.LShift;
            if (isDown(VK_RSHIFT)) held |= SideModifiers.RShift;
            if (isDown(VK_RCONTROL)) held |= SideModifiers.RCtrl;
            if (isDown(VK_LMENU)) held |= SideModifiers.LAlt;
            if (isDown(VK_RMENU)) held |= SideModifiers.RAlt;
            if (isDown(Hotkey.VK_LWIN)) held |= SideModifiers.LWin;
            if (isDown(Hotkey.VK_RWIN)) held |= SideModifiers.RWin;
            // The system state cannot tell AltGr's fake LCtrl from a real one. With RAlt down the
            // likelier story is AltGr, and reading it as Ctrl would make every chord mis-match until
            // the next real LCtrl event.
            bool lctrl = isDown(VK_LCONTROL);
            altGr = lctrl && (held & SideModifiers.RAlt) != 0;
            if (lctrl && !altGr) held |= SideModifiers.LCtrl;
            return held;
        }

        private static bool IsDownAsync(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Which side-specific modifier an event is about. The hook normally reports the
        /// side-specific codes; a generic one (another tool's injection) is resolved the way Windows
        /// resolves it - the extended flag for Ctrl/Alt, the scan code for Shift.
        /// </summary>
        internal static SideModifiers SideOf(uint vk, uint scanCode, uint flags)
        {
            bool extended = (flags & LLKHF_EXTENDED) != 0;
            switch ((int)vk)
            {
                case VK_LSHIFT: return SideModifiers.LShift;
                case VK_RSHIFT: return SideModifiers.RShift;
                case VK_LCONTROL: return SideModifiers.LCtrl;
                case VK_RCONTROL: return SideModifiers.RCtrl;
                case VK_LMENU: return SideModifiers.LAlt;
                case VK_RMENU: return SideModifiers.RAlt;
                case Hotkey.VK_LWIN: return SideModifiers.LWin;
                case Hotkey.VK_RWIN: return SideModifiers.RWin;
                case Hotkey.VK_SHIFT: return (scanCode & 0xFF) == RShiftScan ? SideModifiers.RShift : SideModifiers.LShift;
                case Hotkey.VK_CONTROL: return extended ? SideModifiers.RCtrl : SideModifiers.LCtrl;
                case Hotkey.VK_MENU: return extended ? SideModifiers.RAlt : SideModifiers.LAlt;
                default: return SideModifiers.None;
            }
        }
    }

    /// <summary>
    /// The keyboard hook's decisions, lifted out of the callback so they can be tested as a sequence
    /// of events (ticket S0004, KC-1 and KC-5): which modifiers are held, whether a chord matches,
    /// and which events belong to a chord that has already fired.
    ///
    /// <para>A chord that fires owns its trigger key <b>until that key comes up</b>: the keyboard's
    /// auto-repeat downs and the final up are swallowed with no event. Before, only the first down was
    /// swallowed - a held chord ran its action at the repeat rate (one UAC prompt per repeat for an
    /// elevated launcher scenario) and the key-up leaked to the app on its own.</para>
    /// </summary>
    internal sealed class ChordMatcher
    {
        /// <summary>What the hook should do with an event.</summary>
        public enum Verdict
        {
            /// <summary>Not ours - hand it on.</summary>
            Pass,
            /// <summary>A repeat or the release of a trigger that already fired - eat it, raise nothing.</summary>
            Swallow,
            /// <summary>A physical key-down that may be a chord - match it.</summary>
            Candidate,
        }

        /// <summary>
        /// A repeat arrives every 30-400 ms while a key is held; a gap longer than this means the up
        /// was lost (UIPI hides events bound for an elevated window) and this is a new press.
        /// </summary>
        public const uint RepeatWindowMs = 1000;

        private readonly PhysicalModifiers _modifiers;
        private uint _firedVk;
        private uint _firedAt;

        public ChordMatcher(PhysicalModifiers modifiers) => _modifiers = modifiers;

        public PhysicalModifiers Modifiers => _modifiers;

        /// <summary>Classify one event and update the state it carries.</summary>
        public Verdict Observe(uint vk, uint scanCode, uint flags, bool down, uint time, IntPtr extraInfo)
        {
            bool injected = (flags & LLKHF_INJECTED) != 0;
            bool ours = injected && extraInfo == KeyInjection.Tag;

            // Modifiers are never triggers; CyrFlip's own injected ones do not touch the table.
            if (PhysicalModifiers.SideOf(vk, scanCode, flags) != SideModifiers.None)
            {
                if (!ours) _modifiers.Track(vk, scanCode, flags, down);
                return Verdict.Pass;
            }

            // Synthesized keys never fire a chord - ours would re-enter the hook - and never touch
            // the state of a physical trigger either.
            if (injected) return Verdict.Pass;

            if (_firedVk != 0 && vk == _firedVk)
            {
                if (!down)
                {
                    _firedVk = 0;
                    return Verdict.Swallow;
                }
                if (unchecked(time - _firedAt) < RepeatWindowMs)
                {
                    _firedAt = time;
                    return Verdict.Swallow;
                }
                _firedVk = 0; // the up was lost; this is a fresh press
            }

            return down ? Verdict.Candidate : Verdict.Pass;
        }

        /// <summary>The key just matched and was swallowed: its repeats and its release are ours too.</summary>
        public void Fired(uint vk, uint time)
        {
            _firedVk = vk;
            _firedAt = time;
        }

        /// <summary>Forget a fired trigger - used when the hook is re-armed and events may have been missed.</summary>
        public void Reset() => _firedVk = 0;

        /// <summary>
        /// True when <paramref name="vk"/> with the modifiers held right now is exactly
        /// <paramref name="hotkey"/> - no more modifiers, no fewer. AltGr matches nothing: it is not
        /// Ctrl+Alt, and a chord that ate it would eat a character (KC-5).
        /// </summary>
        public bool Matches(Hotkey hotkey, uint vk)
        {
            if ((int)vk != hotkey.Vk) return false;
            return Matches(hotkey, _modifiers.Held, _modifiers.AltGr);
        }

        internal static bool Matches(Hotkey hotkey, SideModifiers held, bool altGr)
        {
            if (altGr) return false;
            return ((held & SideModifiers.Ctrl) != 0) == hotkey.Ctrl
                && ((held & SideModifiers.Shift) != 0) == hotkey.Shift
                && ((held & SideModifiers.Alt) != 0) == hotkey.Alt
                && ((held & SideModifiers.Win) != 0) == hotkey.Win;
        }
    }
}
