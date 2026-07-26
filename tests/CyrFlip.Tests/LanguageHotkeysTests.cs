using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Covers the pure parts of <see cref="LanguageHotkeys"/> - the encoding that turns a chord into
    /// the MOD_* mask Windows stores and back, and the slot allocation. The registry access and the
    /// SPI_SETLANGTOGGLE call are interop-bound and verified manually.
    /// </summary>
    public class LanguageHotkeysTests
    {
        // ---- Modifier encoding ----

        [Fact]
        public void EncodesCtrlDigitAsTheMaskWindowsStores()
        {
            // The combination the built-in Windows dialog refuses to offer, and the reason the feature exists.
            uint mask = LanguageHotkeys.ToModifiers(Hotkey.Parse("Ctrl+1"));

            Assert.Equal(LanguageHotkeys.MOD_CONTROL, mask & LanguageHotkeys.MOD_CONTROL);
            Assert.Equal(0u, mask & LanguageHotkeys.MOD_SHIFT);
            Assert.Equal(0u, mask & LanguageHotkeys.MOD_ALT);
            // Both side bits: either Ctrl key triggers it, matching what Windows writes for its own presets.
            Assert.Equal(LanguageHotkeys.MOD_LEFT | LanguageHotkeys.MOD_RIGHT, mask & (LanguageHotkeys.MOD_LEFT | LanguageHotkeys.MOD_RIGHT));
        }

        [Theory]
        [InlineData("Ctrl+1")]
        [InlineData("Ctrl+Shift+0")]
        [InlineData("Shift+Alt+9")]
        [InlineData("Ctrl+Alt+F5")]
        [InlineData("Ctrl+Shift+Alt+Z")]
        public void ChordSurvivesEncodeThenFormat(string chord)
        {
            var hotkey = Hotkey.Parse(chord);
            uint mask = LanguageHotkeys.ToModifiers(hotkey);

            Assert.Equal(chord, LanguageHotkeys.FormatChord(mask, (uint)hotkey.Vk));
        }

        [Fact]
        public void FormatsTheObservedWindowsRecordFromTheRegistry()
        {
            // HKCU\Control Panel\Input Method\Hot Keys\00000104 as Windows ships it:
            // Key Modifiers = 0xC006, Virtual Key = 0x30.
            Assert.Equal("Ctrl+Shift+0", LanguageHotkeys.FormatChord(0xC006, 0x30));
        }

        [Fact]
        public void MarksLeftOnlyModifiersAsSuch()
        {
            // Windows' own "Left Alt+Shift" preset sets MOD_LEFT without MOD_RIGHT.
            uint mask = LanguageHotkeys.MOD_ALT | LanguageHotkeys.MOD_SHIFT | LanguageHotkeys.MOD_LEFT;

            // One leading qualifier, not one per modifier: the side bits cover the whole record.
            Assert.Equal("Left Shift+Alt+2", LanguageHotkeys.FormatChord(mask, 0x32));
        }

        // ---- Chord comparison ----

        [Fact]
        public void ChordEqualityIgnoresTheSideBits()
        {
            uint bothSides = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_LEFT | LanguageHotkeys.MOD_RIGHT;
            uint leftOnly = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_LEFT;

            Assert.True(LanguageHotkeys.SameChord(bothSides, 0x31, leftOnly, 0x31));
        }

        [Fact]
        public void ChordEqualitySeparatesDifferentModifiersAndKeys()
        {
            uint ctrl = LanguageHotkeys.MOD_CONTROL;
            uint ctrlShift = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_SHIFT;

            Assert.False(LanguageHotkeys.SameChord(ctrl, 0x31, ctrlShift, 0x31));
            Assert.False(LanguageHotkeys.SameChord(ctrl, 0x31, ctrl, 0x32));
        }

        // ---- Slot allocation ----

        [Fact]
        public void ReusesTheSlotTheLayoutAlreadyOwns()
        {
            var used = new List<int> { LanguageHotkeys.DirectSwitchFirst, 0x104 };

            Assert.Equal(0x104, LanguageHotkeys.PickSlot(used, 0x104));
        }

        [Fact]
        public void TakesTheLowestFreeSlot()
        {
            var used = new List<int> { 0x100, 0x101, 0x103 };

            Assert.Equal(0x102, LanguageHotkeys.PickSlot(used, null));
        }

        [Fact]
        public void NeverStealsASlotHeldByAnotherEntry()
        {
            // 0x104 is the orphaned Japanese record Windows ships; a new assignment must go elsewhere.
            var used = new List<int> { 0x104 };

            Assert.Equal(LanguageHotkeys.DirectSwitchFirst, LanguageHotkeys.PickSlot(used, null));
        }

        [Fact]
        public void ReportsAFullRange()
        {
            var used = new List<int>();
            for (int id = LanguageHotkeys.DirectSwitchFirst; id <= LanguageHotkeys.DirectSwitchLast; id++)
                used.Add(id);

            Assert.Equal(-1, LanguageHotkeys.PickSlot(used, null));
        }

        // ---- Virtual-key naming ----

        [Theory]
        [InlineData(0x31, "1")]
        [InlineData(0x30, "0")]
        [InlineData(0x41, "A")]
        [InlineData(0x7B, "F12")]
        [InlineData(0x87, "F24")]
        [InlineData(0x20, "Space")]
        public void NamesKnownVirtualKeys(int vk, string expected)
        {
            Assert.Equal(expected, Hotkey.NameForVk(vk));
        }

        [Fact]
        public void ShowsUnknownVirtualKeysRatherThanDroppingThem()
        {
            // 0xBE is the '.' key: a chord Windows can store but our key table doesn't name.
            Assert.Equal("0xBE", Hotkey.NameForVk(0xBE));
        }
    }
}
