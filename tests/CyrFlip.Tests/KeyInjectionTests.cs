using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What CyrFlip injects around its synthesized Ctrl+C / Ctrl+V (ticket S0004, KC-1 and KC-4):
    /// side-specific keys only, only what is held, and the mask key before any modifier release.
    /// </summary>
    public class KeyInjectionTests
    {
        private const int C = 0x43, Mask = KeyInjection.MaskVk;
        private const int LCtrl = 0xA2, RCtrl = 0xA3, LShift = 0xA0, RShift = 0xA1, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, RWin = 0x5C;

        private static string Plan(IEnumerable<KeyStroke> keys) => string.Join(", ", keys.Select(k => k.ToString()));

        private static KeyStroke Down(int vk) => new KeyStroke(vk, false);
        private static KeyStroke Up(int vk) => new KeyStroke(vk, true);

        [Fact]
        public void NothingHeldIsAPlainCtrlC()
        {
            List<KeyStroke> plan = KeyInjection.CtrlChord(SideModifiers.None, C, out SideModifiers released);
            Assert.Equal(Plan(new[] { Down(LCtrl), Down(C), Up(C), Up(LCtrl) }), Plan(plan));
            Assert.Equal(SideModifiers.None, released);
        }

        /// <summary>
        /// The Shift that turns CapsLock off under "Press SHIFT to turn off Caps Lock" (S0009 FP-11):
        /// the left key, with the mask inside it - a bare Shift tap switches the mode of several IMEs.
        /// </summary>
        [Fact]
        public void TheCapsLockShiftTapIsMaskedSoNoImeReadsItAsABareShift()
        {
            Assert.Equal(Plan(new[] { Down(LShift), Down(Mask), Up(Mask), Up(LShift) }), Plan(KeyInjection.ShiftTap()));
        }

        [Fact]
        public void RightShiftIsReleasedAsRightShiftAfterTheMask()
        {
            List<KeyStroke> plan = KeyInjection.CtrlChord(SideModifiers.LCtrl | SideModifiers.RShift, C, out SideModifiers released);
            // The held LCtrl is reused and left down: the user is still holding it.
            Assert.Equal(Plan(new[] { Down(Mask), Up(Mask), Up(RShift), Down(LCtrl), Down(C), Up(C) }), Plan(plan));
            Assert.Equal(SideModifiers.RShift, released);
            Assert.DoesNotContain(plan, k => k.Vk == Hotkey.VK_SHIFT || k.Vk == Hotkey.VK_CONTROL || k.Vk == Hotkey.VK_MENU);
        }

        [Fact]
        public void AHeldRightCtrlIsTheCtrlUsed()
        {
            List<KeyStroke> plan = KeyInjection.CtrlChord(SideModifiers.RCtrl, C, out _);
            Assert.Equal(Plan(new[] { Down(RCtrl), Down(C), Up(C) }), Plan(plan));
        }

        [Fact]
        public void WinIsReleasedOnlyWhenHeld()
        {
            List<KeyStroke> none = KeyInjection.CtrlChord(SideModifiers.LShift, C, out _);
            Assert.DoesNotContain(none, k => k.Vk == LWin || k.Vk == RWin);

            List<KeyStroke> right = KeyInjection.CtrlChord(SideModifiers.RWin, C, out SideModifiers released);
            Assert.Contains(Up(RWin), right);
            Assert.DoesNotContain(right, k => k.Vk == LWin);
            Assert.Equal(SideModifiers.RWin, released);
        }

        [Fact]
        public void TheMaskComesBeforeEveryAltShiftOrWinRelease()
        {
            foreach (SideModifiers held in new[] { SideModifiers.LAlt, SideModifiers.RAlt, SideModifiers.LShift,
                SideModifiers.LWin, SideModifiers.LCtrl | SideModifiers.LShift | SideModifiers.LAlt })
            {
                List<KeyStroke> plan = KeyInjection.CtrlChord(held, C, out _);
                int firstRelease = plan.FindIndex(k => k.Up && k.Vk != Mask);
                int mask = plan.FindIndex(k => k.Vk == Mask && k.Up);
                Assert.True(mask >= 0 && mask < firstRelease, held + ": " + Plan(plan));
            }
        }

        [Fact]
        public void ACtrlOnlyChordNeedsNoMask()
        {
            Assert.Empty(KeyInjection.Mask(SideModifiers.LCtrl));
            Assert.Empty(KeyInjection.Mask(SideModifiers.None));
            Assert.Equal(Plan(new[] { Down(Mask), Up(Mask) }), Plan(KeyInjection.Mask(SideModifiers.RAlt)));
        }

        [Fact]
        public void RestorePressesAgainOnlyWhatIsStillHeldThenMasks()
        {
            List<KeyStroke> plan = KeyInjection.Restore(SideModifiers.RShift | SideModifiers.LAlt, stillHeld: SideModifiers.RShift | SideModifiers.LCtrl);
            Assert.Equal(Plan(new[] { Down(RShift), Down(Mask), Up(Mask) }), Plan(plan));
        }

        [Fact]
        public void RestoreOfAKeyLetGoIsNothing()
        {
            Assert.Empty(KeyInjection.Restore(SideModifiers.LShift, stillHeld: SideModifiers.None));
        }

        [Fact]
        public void RightHandKeysAndWinCarryTheExtendedFlag()
        {
            Assert.True(KeyInjection.IsExtended(RCtrl));
            Assert.True(KeyInjection.IsExtended(RAlt));
            Assert.True(KeyInjection.IsExtended(LWin));
            Assert.True(KeyInjection.IsExtended(RWin));
            Assert.True(KeyInjection.IsExtended(0x2E)); // Delete, not the numpad '.'
            Assert.False(KeyInjection.IsExtended(RShift)); // its own scan code, no E0 prefix
            Assert.False(KeyInjection.IsExtended(LCtrl));
            Assert.False(KeyInjection.IsExtended(C));
        }

        [Fact]
        public void EverySideMapsToItsOwnVirtualKey()
        {
            Assert.Equal(new[] { LCtrl, RCtrl, LShift, RShift, LAlt, RAlt, LWin, RWin },
                KeyInjection.Sides((SideModifiers)0xFF).Select(KeyInjection.VkOf).ToArray());
        }
    }

    /// <summary>The mouse hook's swallow-the-up flag and its expiry (ticket S0004, KC-7).</summary>
    public class SwallowUpGateTests
    {
        [Fact]
        public void TheChordsOwnUpIsSwallowed()
        {
            var gate = new SwallowUpGate();
            gate.Arm(1000);
            Assert.True(gate.IsArmed(1100));
            Assert.True(gate.TakeUp(1150));
            Assert.False(gate.TakeUp(1200)); // disarmed
        }

        [Fact]
        public void AnUpLostToAnElevatedWindowExpires()
        {
            var gate = new SwallowUpGate();
            gate.Arm(1000);
            // No up arrived; four seconds later an ordinary right click happens anywhere.
            Assert.False(gate.IsArmed(5000));          // the watchdog re-arms instead of waiting
            Assert.False(gate.TakeUp(5000));           // and the plain click's up passes through
        }

        [Fact]
        public void ExpiryWorksAcrossTheTickCounterWrap()
        {
            var gate = new SwallowUpGate();
            gate.Arm(uint.MaxValue - 100);
            Assert.True(gate.TakeUp(50)); // 151 ms later, after the wrap
        }
    }
}
