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

        private sealed class Keyboard
        {
            public readonly ChordMatcher Matcher = new ChordMatcher(new PhysicalModifiers());
            private uint _time = 1000;

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
            Assert.True(kb.Press(F12, CtrlShiftF12, gap: ChordMatcher.RepeatWindowMs + 500));
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
        public void ResetForgetsAFiredTrigger()
        {
            var kb = new Keyboard();
            kb.Physical(LCtrl, true);
            kb.Physical(LShift, true);
            Assert.True(kb.Press(F12, CtrlShiftF12));
            kb.Matcher.Reset();
            Assert.Equal(ChordMatcher.Verdict.Pass, kb.Physical(F12, false));
        }

        [Fact]
        public void RightCtrlCountsAsCtrl()
        {
            Assert.True(ChordMatcher.Matches(CtrlShiftF12, SideModifiers.RCtrl | SideModifiers.RShift, altGr: false));
            Assert.False(ChordMatcher.Matches(CtrlShiftF12, SideModifiers.RCtrl | SideModifiers.RShift, altGr: true));
        }
    }
}
