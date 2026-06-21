using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class HotkeyTests
    {
        [Fact]
        public void ParsesCtrlShiftLetterCombo()
        {
            var hk = Hotkey.Parse("Ctrl+Shift+T");
            Assert.True(hk.Ctrl);
            Assert.True(hk.Shift);
            Assert.False(hk.Alt);
            Assert.False(hk.Win);
            Assert.Equal(0x54, hk.Vk); // 'T'
            Assert.Equal("Ctrl+Shift+T", hk.Display);
        }

        [Fact]
        public void DefaultIsCtrlShiftF12()
        {
            var hk = Hotkey.Default;
            Assert.True(hk.Ctrl);
            Assert.True(hk.Shift);
            Assert.Equal(0x7B, hk.Vk); // F12
            Assert.Equal("Ctrl+Shift+F12", hk.Display);
        }

        [Theory]
        [InlineData("alt+f4", true, "Alt+F4", 0x73)]   // F4 = 0x73
        [InlineData("win+space", false, "Win+Space", 0x20)]
        [InlineData("control + 9", false, "Ctrl+9", 0x39)]
        public void ParsesModifiersAndKeys(string input, bool alt, string display, int vk)
        {
            var hk = Hotkey.Parse(input);
            Assert.Equal(alt, hk.Alt);
            Assert.Equal(display, hk.Display);
            Assert.Equal(vk, hk.Vk);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Ctrl+Shift")] // modifiers only, no trigger key
        public void FallsBackToDefaultWhenNoTriggerKey(string? input)
        {
            var hk = Hotkey.Parse(input);
            Assert.Equal("Ctrl+Shift+F12", hk.Display);
        }

        [Fact]
        public void IsCaseInsensitive()
        {
            Assert.Equal("Ctrl+Shift+T", Hotkey.Parse("CTRL+shift+t").Display);
        }

        [Fact]
        public void CaseDefaultIsCtrlShiftF11()
        {
            var hk = Hotkey.CaseDefault;
            Assert.True(hk.Ctrl);
            Assert.True(hk.Shift);
            Assert.False(hk.Alt);
            Assert.Equal(0x7A, hk.Vk); // F11
            Assert.Equal("Ctrl+Shift+F11", hk.Display);
        }

        [Fact]
        public void SameChordDistinguishesTheDefaultHotkeys()
        {
            Assert.False(Hotkey.Default.SameChord(Hotkey.CaseDefault));
            Assert.True(Hotkey.Default.SameChord(Hotkey.Parse("Shift+Ctrl+F12"))); // order-independent
            Assert.False(Hotkey.Default.SameChord(Hotkey.Parse("Ctrl+Alt+F12"))); // modifiers matter
        }
    }
}
