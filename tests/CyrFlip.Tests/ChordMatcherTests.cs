using System;
using CyrFlip;
using Xunit;
using static CyrFlip.WindowInterop;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The keyboard hook's decisions as sequences of key events (ticket S0004, KC-1 and KC-5): the
    /// physical modifier table, the fired trigger owning its repeats and its release, and AltGr.
    /// </summary>
    public class ChordMatcherTests
    {
        private const uint F12 = 0x7B, N = 0x4E, A = 0x41;
        private const uint LCtrl = 0xA2, RCtrl = 0xA3, LShift = 0xA0, RShift = 0xA1, LAlt = 0xA4, RAlt = 0xA5;
        private static readonly Hotkey CtrlShiftF12 = Hotkey.Parse("Ctrl+Shift+F12");
        private static readonly Hotkey CtrlAltN = Hotkey.Parse("Ctrl+Alt+N");

        /// <summary>What <c>GetAsyncKeyState</c> would say - the system's view, which a test moves by hand.</summary>
        private sealed class SystemKeys
        {
            private readonly System.Collections.Generic.HashSet<int> _down = new System.Collections.Generic.HashSet<int>();

            public SystemKeys(params uint[] down) { foreach (uint vk in down) _down.Add((int)vk); }

            public bool IsDown(int vk) { lock (_down) return _down.Contains(vk); }
            public void Down(uint vk) { lock (_down) _down.Add((int)vk); }
            public void Up(uint vk) { lock (_down) _down.Remove((int)vk); }
        }

        private sealed class Keyboard
        {
            public readonly ChordMatcher Matcher;
            private uint _time = 1000;

            public Keyboard(Func<int, bool>? isDown = null, uint? repeatWindowMs = null)
            {
                Matcher = new ChordMatcher(new PhysicalModifiers(isDown), repeatWindowMs);
            }

            /// <summary>The time of the last event, in the hook's tick base.</summary>
            public uint Now => _time;

            public ChordMatcher.Verdict Physical(uint vk, bool down, uint scan = 0, uint gap = 30)
            {
                _time += gap;
                return Matcher.Observe(vk, scan, 0, down, _time, IntPtr.Zero);
            }

            public ChordMatcher.Verdict Ours(uint vk, bool down)
                => Matcher.Observe(vk, 0, LLKHF_INJECTED, down, _time, KeyInjection.Tag);

            public ChordMatcher.Verdict Foreign(uint vk, bool down)
                => Matcher.Observe(vk, 0, LLKHF_INJECTED, down, _time, IntPtr.Zero);

            /// <summary>What the hook would do with a trigger key-down: fire (and own it) or pass.</summary>
            public bool Press(uint vk, Hotkey chord, uint gap = 30)
            {
                ChordMatcher.Verdict verdict = Physical(vk, true, gap: gap);
                if (verdict != ChordMatcher.Verdict.Candidate) return false;
                if (!Matcher.Matches(chord, vk)) return false;
                Matcher.Fired(vk, _time);
                return true;
            }
        }

        [Fact]
        public void HeldTriggerFiresOnceAndItsRepeatsAndReleaseAreSwallowed()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);

            Assert.True(kb.Press(F12, CtrlShiftF12));
            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, true)); // auto-repeat
            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, true));
            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, false)); // the release
            // And the next event of that key is an ordinary one again.
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Physical(F12, false));
        }

        [Fact]
        public void OurInjectedModifierUpsDoNotReleaseThePhysicalTable()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));
            kb.Physical(F12, false);

            // What ClipboardHandler injects around its Ctrl+C.
            kb.Ours(LShift, true);
            kb.Ours(LCtrl, true);

            Assert.Equal(SideModifiers.LCtrl | SideModifiers.LShift, kb.Matcher.Modifiers.Held);
            // The second tap of the still-held chord fires again instead of leaking a bare F12.
            Assert.True(kb.Press(F12, CtrlShiftF12));
        }

        [Fact]
        public void AnotherToolsInjectedModifierCounts()
        {
            // A remapper (CapsLock -> Ctrl) injects; its Ctrl must keep working as Ctrl.
            var kb = new Keyboard();
            kb.Foreign(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));
        }

        [Fact]
        public void InjectedTriggerNeverFires()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Foreign(F12, true));
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Ours(F12, true));
        }

        [Fact]
        public void RightShiftWithCtrlMatchesCtrlShift()
        {
            var kb = new Keyboard();
            kb.Physical(RShift, true);
            kb.Physical(LCtrl, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));
        }

        [Fact]
        public void ATriggerUpWithNoFiredDownPassesThrough()
        {
            var kb = new Keyboard();
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Physical(F12, false));
            Assert.Equal(ChordMatcher.Verdict.Candidate, kb.Physical(F12, true));
        }

        [Fact]
        public void ModifiersAreNeverCandidates()
        {
            var kb = new Keyboard();
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Physical(LCtrl, true));
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Physical(0x5B, true)); // LWin
        }

        [Fact]
        public void ExactModifiersOnly()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            kb.Physical(LAlt, true);
            Assert.False(kb.Press(F12, CtrlShiftF12));
        }

        [Fact]
        public void AReleasedModifierStopsMatching()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            kb.Physical(LShift, false);
            Assert.False(kb.Press(F12, CtrlShiftF12));
        }

        [Fact]
        public void APressAfterALostReleaseIsAFreshPress()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));
            // The up went to an elevated window and never reached the hook; much later, a new press.
            Assert.True(kb.Press(F12, CtrlShiftF12, gap: kb.Matcher.RepeatWindowMs + 500));
        }

        [Fact]
        public void AltGrDoesNotMatchCtrlAlt()
        {
            // AltGr = a fake LCtrl (scan 0x21D) + RAlt: AltGr+N types "ń" on Polish (Programmers).
            var kb = new Keyboard();
            kb.Physical(LCtrl, true, scan: PhysicalModifiers.AltGrCtrlScan);
            kb.Physical(RAlt, true);
            Assert.False(kb.Press(N, CtrlAltN));
            Assert.False(kb.Press(N, Hotkey.Parse("Alt+N")));
            Assert.True(kb.Matcher.Modifiers.AltGr);
            Assert.Equal(SideModifiers.RAlt, kb.Matcher.Modifiers.Held);
        }

        [Fact]
        public void RealCtrlAndLeftAltMatchCtrlAlt()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true, scan: 0x1D);
            kb.Physical(LAlt, true);
            Assert.True(kb.Press(N, CtrlAltN));
        }

        [Fact]
        public void AltGrReleaseClearsItsFakeCtrl()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true, scan: PhysicalModifiers.AltGrCtrlScan);
            kb.Physical(RAlt, true);
            kb.Physical(LCtrl, false, scan: PhysicalModifiers.AltGrCtrlScan);
            kb.Physical(RAlt, false);
            Assert.False(kb.Matcher.Modifiers.AltGr);
            Assert.Equal(SideModifiers.None, kb.Matcher.Modifiers.Held);
        }

        [Fact]
        public void GenericInjectedModifiersResolveToTheirSide()
        {
            Assert.Equal(SideModifiers.RCtrl, PhysicalModifiers.SideOf(Hotkey.VK_CONTROL, 0x1D, LLKHF_EXTENDED));
            Assert.Equal(SideModifiers.LCtrl, PhysicalModifiers.SideOf(Hotkey.VK_CONTROL, 0x1D, 0));
            Assert.Equal(SideModifiers.RShift, PhysicalModifiers.SideOf(Hotkey.VK_SHIFT, 0x36, 0));
            Assert.Equal(SideModifiers.LShift, PhysicalModifiers.SideOf(Hotkey.VK_SHIFT, 0x2A, 0));
            Assert.Equal(SideModifiers.RAlt, PhysicalModifiers.SideOf(Hotkey.VK_MENU, 0x38, LLKHF_EXTENDED));
            Assert.Equal(SideModifiers.None, PhysicalModifiers.SideOf(A, 0x1E, 0));
        }

        [Fact]
        public void RefreshReadsSideSpecificStateAndTreatsCtrlUnderRightAltAsAltGr()
        {
            var table = new PhysicalModifiers();
            table.Refresh(vk => vk == 0xA3 || vk == 0xA1); // RCtrl + RShift
            Assert.Equal(SideModifiers.RCtrl | SideModifiers.RShift, table.Held);

            table.Refresh(vk => vk == 0xA2 || vk == 0xA5); // LCtrl + RAlt: AltGr on an AltGr layout
            Assert.True(table.AltGr);
            Assert.Equal(SideModifiers.RAlt, table.Held);
        }

        [Fact]
        public void RightCtrlCountsAsCtrl()
        {
            Assert.True(ChordMatcher.Matches(CtrlShiftF12, SideModifiers.RCtrl | SideModifiers.RShift, altGr: false));
            Assert.False(ChordMatcher.Matches(CtrlShiftF12, SideModifiers.RCtrl | SideModifiers.RShift, altGr: true));
        }

        [Fact]
        public void ReconcileDownwardsClearsStaleModifiersWhenAsyncStateIsUp()
        {
            bool lctrlDown = false;
            var table = new PhysicalModifiers(vk => vk == 0xA2 && lctrlDown);
            table.Track(LCtrl, 0x1D, 0, true);
            Assert.Equal(SideModifiers.LCtrl, table.Held);

            // GetAsyncKeyState says LCtrl is up -> Snapshot clears the stale bit
            Assert.Equal(SideModifiers.None, table.Snapshot());
            Assert.Equal(SideModifiers.None, table.Held);
        }

        [Fact]
        public void StaleModifiersDoNotLetBareTriggerFire()
        {
            // Table recorded LCtrl + LShift, but key-ups happened on secure desktop (isDown returns false).
            var kb = new Keyboard(isDown: _ => false);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);

            // Lone F12 press must reconcile downwards and NOT fire Ctrl+Shift+F12
            Assert.False(kb.Press(F12, CtrlShiftF12));
            Assert.Equal(SideModifiers.None, kb.Matcher.Modifiers.Held);
        }

        [Theory]
        [InlineData(0, 750u)]
        [InlineData(1, 1000u)]
        [InlineData(2, 1250u)]
        [InlineData(3, 1500u)]
        public void CalculateRepeatWindowMsMatchesKeyboardDelaySetting(int delay, uint expected)
        {
            Assert.Equal(expected, ChordMatcher.CalculateRepeatWindowMs(delay, marginMs: 500));
        }

        [Fact]
        public void AutoRepeatAtOneSecondIsSwallowedUnderLongDelayWindow()
        {
            var kb = new Keyboard(repeatWindowMs: 1500);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));

            // With KeyboardDelay=3 (1000 ms), the first repeat arrives at ~1050 ms.
            // Under a 1500 ms window it must be swallowed rather than treated as a fresh press.
            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, true, gap: 1050));
        }

        [Fact]
        public void ResetKeepsATriggerHeldAcrossTheRearm()
        {
            // The system never saw the swallowed F12, so it reports it up throughout - the rule
            // cannot ask the system, and must not.
            var system = new SystemKeys(LCtrl, LShift);
            var kb = new Keyboard(system.IsDown, repeatWindowMs: 1500);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));

            // The watchdog re-arms during the keyboard's initial repeat delay.
            kb.Matcher.Reset(kb.Now + 900);

            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, true, gap: 1000)); // first repeat
            Assert.Equal(ChordMatcher.Verdict.Swallow, kb.Physical(F12, false));           // the release
        }

        [Fact]
        public void ResetForgetsATriggerWhoseUpWasLost()
        {
            var kb = new Keyboard(repeatWindowMs: 1500);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));

            // No event for longer than the window: the up went somewhere the hook cannot see.
            kb.Matcher.Reset(kb.Now + 1600);

            Assert.True(kb.Press(F12, CtrlShiftF12, gap: 1700));
        }

        [Fact]
        public void OurOwnReleaseOfAHeldKeyIsNotReconciledAway()
        {
            var system = new SystemKeys(LCtrl, LShift);
            var kb = new Keyboard(system.IsDown);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);

            // The plan releases the held Shift before its Ctrl+C: the hook sees our up first, then
            // the system reports the key up although the user still holds it.
            kb.Ours(LShift, false);
            system.Up(LShift);

            Assert.Equal(SideModifiers.LCtrl | SideModifiers.LShift, kb.Matcher.Modifiers.Snapshot());
            // The second tap of the held chord still fires (KC-1 must not come back through KC2-1).
            Assert.True(kb.Press(F12, CtrlShiftF12));

            // The restore: our down, then the system agrees again, and the mark is gone - a later
            // lost up of that key is reconciled like any other.
            kb.Ours(LShift, true);
            system.Down(LShift);
            Assert.Equal(SideModifiers.LCtrl | SideModifiers.LShift, kb.Matcher.Modifiers.Snapshot());
            system.Up(LShift);
            Assert.Equal(SideModifiers.LCtrl, kb.Matcher.Modifiers.Snapshot());
        }

        [Fact]
        public void TheUsersOwnReleaseEndsOurMark()
        {
            var system = new SystemKeys(LCtrl, LShift);
            var kb = new Keyboard(system.IsDown);
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            kb.Ours(LShift, false);
            system.Up(LShift);

            kb.Physical(LShift, false); // the user lets go while the key was released by us
            Assert.Equal(SideModifiers.LCtrl, kb.Matcher.Modifiers.Snapshot());

            // Pressed again for real, then its up lost: reconciled, not protected by an old mark.
            kb.Physical(LShift, true);
            Assert.Equal(SideModifiers.LCtrl, kb.Matcher.Modifiers.Snapshot());
        }

        [Fact]
        public void AStaleCtrlIsNotReusedByTheInjectionPlan()
        {
            // Ctrl+Alt+Del -> Cancel: the table still holds LCtrl, the system knows it is up.
            var system = new SystemKeys();
            var table = new PhysicalModifiers(system.IsDown);
            table.Track(LCtrl, 0x1D, 0, true);

            var plan = KeyInjection.CtrlChord(table.Snapshot(), 'C', out _);

            // The plan presses its own Ctrl and lets it go - never a Ctrl down with no up.
            Assert.Equal(PhysicalModifiers.VK_LCONTROL, plan[plan.Count - 1].Vk);
            Assert.True(plan[plan.Count - 1].Up);
        }

        [Fact]
        public void AnAltGrGuessIsReconciledAway()
        {
            var system = new SystemKeys(LCtrl, RAlt);
            var table = new PhysicalModifiers(system.IsDown);
            table.Refresh(system.IsDown);
            Assert.True(table.AltGr);

            system.Up(RAlt);
            table.Snapshot();
            Assert.False(table.AltGr);
        }

        [Fact]
        public void ConcurrentTrackAndReconcileNeverLoseAKeyTheHookRecorded()
        {
            // The hook thread records presses while the clipboard worker reconciles: a key that is
            // down in both the table and the system must survive every interleaving.
            var system = new SystemKeys(LCtrl, LShift, RAlt);
            var table = new PhysicalModifiers(system.IsDown);
            table.Track(LCtrl, 0x1D, 0, true);
            var worker = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 20000; i++) table.Snapshot();
            });
            worker.Start();
            for (int i = 0; i < 20000; i++)
            {
                table.Track(LShift, 0x2A, 0, true);
                table.Track(LShift, 0x2A, 0, false);
            }
            table.Track(LShift, 0x2A, 0, true);
            worker.Join();

            Assert.Equal(SideModifiers.LCtrl | SideModifiers.LShift, table.Snapshot());
        }

        [Fact]
        public void RealLCtrlUpClearsAltGrSetByRefresh()
        {
            var table = new PhysicalModifiers();
            table.Refresh(vk => vk == 0xA2 || vk == 0xA5); // LCtrl + RAlt -> AltGr flag set
            Assert.True(table.AltGr);

            // A real (non-0x21D) LCtrl up arrives
            table.Track(LCtrl, 0x1D, 0, false);
            Assert.False(table.AltGr);
        }

        [Fact]
        public void RAltUpClearsAltGrSetByRefresh()
        {
            var table = new PhysicalModifiers();
            table.Refresh(vk => vk == 0xA2 || vk == 0xA5); // LCtrl + RAlt -> AltGr flag set
            Assert.True(table.AltGr);

            // RAlt up arrives
            table.Track(RAlt, 0x38, LLKHF_EXTENDED, false);
            Assert.False(table.AltGr);
        }
    }
}

