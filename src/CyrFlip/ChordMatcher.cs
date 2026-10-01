using System;
using System.Threading;
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
    /// <para>The table is also <b>reconciled downwards</b> against the system (ticket S0033, KC2-1):
    /// some key-ups never reach the hook, and a key the table still holds that the system reports up
    /// is dropped - except the keys CyrFlip itself has released, which is the one case where the
    /// system's "up" is the lie. See <see cref="Reconcile"/>.</para>
    ///
    /// <para><b>AltGr</b> arrives as a fake LCtrl (scan code <c>0x21D</c>) followed by RAlt. That
    /// LCtrl is not recorded as Ctrl (KC-5): it is tracked on its own, and while it is down the key
    /// is AltGr - a modifier no CyrFlip chord can contain, so nothing matches and the character
    /// reaches the app.</para>
    /// </summary>
    internal sealed class PhysicalModifiers
    {
        /// <summary>The process-wide table the keyboard hook feeds and the clipboard worker reads.</summary>
        public static readonly PhysicalModifiers Shared = new PhysicalModifiers(IsDownAsync);

        /// <summary>The scan code (0x200 flag + LCtrl's 0x1D) of the LCtrl AltGr sends ahead of RAlt.</summary>
        public const uint AltGrCtrlScan = 0x21D;

        // Side-specific virtual keys; the LL hook reports these rather than the generic ones.
        public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
            VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        private const uint RShiftScan = 0x36;

        // Written by the hook thread and - through the downward reconcile - by the clipboard worker,
        // so every change is a compare-and-swap on one int: a reader always sees one whole state, and
        // a key the hook records never loses a race with a bit the worker clears.
        private readonly Func<int, bool>? _isDown;
        private int _held;
        // The held keys CyrFlip itself released by injection (KeyInjection.CtrlChord) and has not seen
        // come back down: GetAsyncKeyState reports them up while the user still holds them, so the
        // reconcile must not believe it for exactly these (KC2-1).
        private int _falsified;
        private volatile bool _altGr;
        private volatile bool _fed;

        /// <param name="isDown">
        /// The system's key state (<c>GetAsyncKeyState</c> in the app); null = never reconcile, the
        /// table is trusted as it is - what the tests of the event logic want.
        /// </param>
        public PhysicalModifiers(Func<int, bool>? isDown = null)
        {
            _isDown = isDown;
        }

        public SideModifiers Held => (SideModifiers)Volatile.Read(ref _held);

        /// <summary>True while AltGr's fake LCtrl is down.</summary>
        public bool AltGr => _altGr;

        /// <summary>
        /// The current state, reconciled downwards (<see cref="Reconcile"/>), or - before the hook
        /// has ever filled the table (a one-shot launcher process, a test) - what
        /// <c>GetAsyncKeyState</c> says, which is right as long as nothing has been synthesized yet.
        /// </summary>
        public SideModifiers Snapshot()
        {
            if (_fed)
            {
                Reconcile();
                return Held;
            }
            return Read(_isDown ?? IsDownAsync, out _);
        }

        /// <summary>
        /// Clear every key the table holds that the system reports up (ticket S0033, KC2-1). The
        /// table only learns from events it sees, and some ups it never sees: Ctrl+Alt+Del then
        /// Cancel (the ups happen on the secure desktop), a modifier released over an elevated window.
        /// A stale Ctrl|Alt used to make every chord mis-match for up to a minute, fire a chord on its
        /// bare trigger, and - through the Ctrl-reuse rule of <see cref="KeyInjection.CtrlChord"/> -
        /// leave an injected Ctrl logically down system-wide. Called before every injection plan and
        /// before a chord is matched, so a plan never reuses a Ctrl the system does not confirm.
        ///
        /// <para>Only this direction is safe, and only with one exception. Other tools never make
        /// the system say "up" for a key the user holds, but CyrFlip does, on purpose: the plan
        /// releases the held Shift/Alt/Win before its Ctrl+C. Those keys are recorded as falsified
        /// when the hook sees our own release (<see cref="TrackOwn"/> - the hook runs before the system
        /// state changes, so there is no moment when the system says "up" and the record is missing)
        /// and are left alone until the system reports them down again - the restore - or the user's
        /// own event for them arrives. Reconciling them would be KC-1 again: the second tap of a held
        /// chord leaking a bare trigger, the restore never running.</para>
        /// </summary>
        public void Reconcile()
        {
            Func<int, bool>? isDown = _isDown;
            if (isDown == null) return;

            int held = Volatile.Read(ref _held);
            int falsified = Volatile.Read(ref _falsified);
            int stale = 0, settled = falsified & ~held;
            for (int bit = 1; bit <= (int)SideModifiers.RWin; bit <<= 1)
            {
                if ((held & bit) == 0) continue;
                bool down = isDown(KeyInjection.VkOf((SideModifiers)bit));
                if ((falsified & bit) != 0)
                {
                    if (down) settled |= bit; // our restore arrived - the system agrees again
                }
                else if (!down)
                {
                    stale |= bit;
                }
            }
            if (stale != 0) Clear(ref _held, stale);
            if (settled != 0) Clear(ref _falsified, settled);

            if (_altGr && (falsified & (int)SideModifiers.RAlt) == 0
                && (!isDown(VK_LCONTROL) || !isDown(VK_RMENU)))
                _altGr = false;
        }

        /// <summary>
        /// Update the table from one key event that is not CyrFlip's own. Returns true when
        /// <paramref name="vk"/> is a modifier key (whether or not the event changed anything), so the
        /// caller never treats it as a trigger.
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

            // Only the fake LCtrl sets AltGr, so on a layout without one a flag Refresh guessed would
            // otherwise never clear (KC2-6): any real LCtrl up or RAlt up ends it.
            if (!down && (side == SideModifiers.LCtrl || side == SideModifiers.RAlt))
                _altGr = false;

            if (down) Set(ref _held, (int)side);
            else Clear(ref _held, (int)side);
            // The user's own event makes the system state true again for this key.
            Clear(ref _falsified, (int)side);
            _fed = true;
            return true;
        }

        /// <summary>
        /// One of CyrFlip's own injected modifier events, seen by the hook. It never changes what the
        /// user holds; a release of a key the table holds is remembered, so the reconcile does not
        /// take the system's word for that key until it is down again (see <see cref="Reconcile"/>).
        /// </summary>
        public void TrackOwn(uint vk, uint scanCode, uint flags, bool down)
        {
            SideModifiers side = SideOf(vk, scanCode, flags);
            if (side == SideModifiers.None || down) return;
            if ((Volatile.Read(ref _held) & (int)side) != 0)
                Set(ref _falsified, (int)side);
        }

        /// <summary>
        /// Re-read the table from the system - on install, on every watchdog re-arm and on session
        /// unlock, so a key held or released while the hook was not listening is not lost. Called
        /// only while nothing is being synthesized, so the system state is the truth.
        /// </summary>
        public void Refresh(Func<int, bool> isDown)
        {
            Volatile.Write(ref _held, (int)Read(isDown, out bool altGr));
            Volatile.Write(ref _falsified, 0);
            _altGr = altGr;
            _fed = true;
        }

        private static void Set(ref int field, int bits)
        {
            int seen;
            do { seen = Volatile.Read(ref field); }
            while ((seen | bits) != seen && Interlocked.CompareExchange(ref field, seen | bits, seen) != seen);
        }

        private static void Clear(ref int field, int bits)
        {
            int seen;
            do { seen = Volatile.Read(ref field); }
            while ((seen & bits) != 0 && Interlocked.CompareExchange(ref field, seen & ~bits, seen) != seen);
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
        /// The repeat window when the system's keyboard delay cannot be read: the "Long" delay
        /// (1000 ms) plus the margin.
        /// </summary>
        public const uint DefaultRepeatWindowMs = 1500;

        /// <summary>Added to the keyboard's initial repeat delay; repeats after it come every 30-400 ms.</summary>
        public const uint RepeatMarginMs = 500;

        private readonly PhysicalModifiers _modifiers;
        // Null = read from the system (and re-read on every Reset, so a changed Control Panel setting
        // is picked up by the next watchdog tick); a test passes a fixed value.
        private readonly uint? _fixedRepeatWindowMs;
        private uint _repeatWindowMs;
        private uint _firedVk;
        private uint _firedAt;
        // Whether the table was reconciled for the current candidate key-down, so a key bound in
        // several rows costs one reconcile, not one per row.
        private bool _reconciled;
        // A PrintScreen key-down was delivered since its last up (see Observe).
        private bool _snapshotDownSeen;
        private const uint VK_SNAPSHOT = 0x2C;

        public ChordMatcher(PhysicalModifiers modifiers, uint? repeatWindowMs = null)
        {
            _modifiers = modifiers;
            _fixedRepeatWindowMs = repeatWindowMs;
            _repeatWindowMs = repeatWindowMs ?? ReadSystemRepeatWindowMs();
        }

        public PhysicalModifiers Modifiers => _modifiers;

        /// <summary>
        /// A gap between two downs of a fired trigger longer than this means its up was lost (UIPI
        /// hides events bound for an elevated window) and this is a new press. It must outlast the
        /// keyboard's <b>initial</b> repeat delay, not only the repeat rate: at the "Long" delay the
        /// first repeat arrives after a full second, and a 1000 ms window read it as a second press -
        /// a second UAC prompt, the history window toggled open and shut (KC2-2).
        /// </summary>
        public uint RepeatWindowMs => _repeatWindowMs;

        /// <summary>The window for <c>SPI_GETKEYBOARDDELAY</c> = <paramref name="keyboardDelay"/> (0..3, 250 ms steps).</summary>
        public static uint CalculateRepeatWindowMs(int keyboardDelay, uint marginMs = RepeatMarginMs)
        {
            int clamped = Math.Max(0, Math.Min(3, keyboardDelay));
            return (uint)((clamped + 1) * 250) + marginMs;
        }

        public static uint ReadSystemRepeatWindowMs(uint marginMs = RepeatMarginMs)
        {
            try
            {
                int delay = 3;
                if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0))
                    return CalculateRepeatWindowMs(delay, marginMs);
            }
            catch { }
            return DefaultRepeatWindowMs;
        }

        /// <summary>Classify one event and update the state it carries.</summary>
        public Verdict Observe(uint vk, uint scanCode, uint flags, bool down, uint time, IntPtr extraInfo)
        {
            bool injected = (flags & LLKHF_INJECTED) != 0;
            bool ours = injected && extraInfo == KeyInjection.Tag;

            // Modifiers are never triggers; CyrFlip's own injected ones do not touch the table, they
            // only mark a held key the system now wrongly reports up.
            if (PhysicalModifiers.SideOf(vk, scanCode, flags) != SideModifiers.None)
            {
                if (ours) _modifiers.TrackOwn(vk, scanCode, flags, down);
                else _modifiers.Track(vk, scanCode, flags, down);
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
                if (unchecked(time - _firedAt) < _repeatWindowMs)
                {
                    _firedAt = time;
                    return Verdict.Swallow;
                }
                _firedVk = 0; // the up was lost; this is a fresh press
            }

            // PrintScreen reaches a low-level hook as a key-UP only (Windows keeps its key-down for
            // itself), so a chord on it can never see a press. An up with no down before it is the
            // press; the caller must not own the trigger afterwards (the up is already the release).
            if (vk == VK_SNAPSHOT)
            {
                bool downSeen = _snapshotDownSeen;
                _snapshotDownSeen = down;
                if (!down && !downSeen)
                {
                    _reconciled = false;
                    return Verdict.Candidate;
                }
            }

            if (!down) return Verdict.Pass;
            _reconciled = false;
            return Verdict.Candidate;
        }

        /// <summary>The key just matched and was swallowed: its repeats and its release are ours too.</summary>
        public void Fired(uint vk, uint time)
        {
            _firedVk = vk;
            _firedAt = time;
        }

        /// <summary>
        /// The hook is being re-armed and events may have been missed. A trigger still inside its
        /// repeat window is kept (KC2-2): the user is holding the chord across the watchdog tick, and
        /// forgetting it let the trigger's release - and a repeat read as a fresh press - reach the
        /// app. Its own state cannot be asked for instead: the system never saw the swallowed down, so
        /// <c>GetAsyncKeyState</c> reports a fired trigger up however long it is held.
        /// </summary>
        /// <param name="now">The tick count, in the hook's <c>time</c> base (<c>GetTickCount</c>).</param>
        public void Reset(uint now)
        {
            if (_fixedRepeatWindowMs == null) _repeatWindowMs = ReadSystemRepeatWindowMs();
            if (_firedVk != 0 && unchecked(now - _firedAt) < _repeatWindowMs) return;
            _firedVk = 0;
        }

        /// <summary>
        /// True when <paramref name="vk"/> with the modifiers held right now is exactly
        /// <paramref name="hotkey"/> - no more modifiers, no fewer. AltGr matches nothing: it is not
        /// Ctrl+Alt, and a chord that ate it would eat a character (KC-5). The table is reconciled
        /// first, once per key-down (KC2-1): a modifier whose up never arrived must neither spoil
        /// every chord nor let a chord fire on its bare trigger.
        /// </summary>
        public bool Matches(Hotkey hotkey, uint vk)
        {
            if ((int)vk != hotkey.Vk) return false;
            if (!_reconciled)
            {
                _modifiers.Reconcile();
                _reconciled = true;
            }
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
