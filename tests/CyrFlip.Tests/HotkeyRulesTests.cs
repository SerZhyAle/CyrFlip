using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The chords the capture dialog refuses because they would eat typing (ticket S0004, KC-5),
    /// against fake layouts - what a chord types is a delegate, so no layout has to be installed.
    /// </summary>
    public class HotkeyRulesTests
    {
        private static readonly IntPtr Us = (IntPtr)0x04090409;
        private static readonly IntPtr German = (IntPtr)0x04070407;

        // US types nothing on AltGr; German types "€" on AltGr+E and "@" on AltGr+Q.
        private static string? Typed(Hotkey chord, IntPtr layout)
        {
            if (layout != German || !chord.Ctrl || !chord.Alt || chord.Shift) return null;
            return chord.Vk == 'E' ? "€" : chord.Vk == 'Q' ? "@" : null;
        }

        private static HotkeyRules.Refusal Check(string chord, params IntPtr[] layouts)
            => HotkeyRules.Check(Hotkey.Parse(chord), layouts, Typed).Refusal;

        [Theory]
        [InlineData("Shift+A")]
        [InlineData("Shift+1")]
        [InlineData("Shift+Space")]
        [InlineData("Shift+Tab")]
        [InlineData("Shift+Home")]
        [InlineData("Shift+PageUp")]
        [InlineData("Shift+Left")]
        [InlineData("Shift+Down")]
        [InlineData("Shift+Numpad5")]
        public void ShiftAloneIsRefusedOffTheFunctionKeys(string chord)
            => Assert.Equal(HotkeyRules.Refusal.ShiftOnly, Check(chord, Us));

        [Theory]
        [InlineData("Shift+F5")]
        [InlineData("Shift+F24")]
        public void ShiftAloneIsFineOnAFunctionKey(string chord)
            => Assert.Equal(HotkeyRules.Refusal.None, Check(chord, Us));

        [Theory]
        [InlineData("Ctrl+C")]
        [InlineData("Ctrl+V")]
        [InlineData("Ctrl+X")]
        [InlineData("Ctrl+Z")]
        [InlineData("Ctrl+Y")]
        [InlineData("Ctrl+A")]
        [InlineData("Ctrl+S")]
        [InlineData("Ctrl+Insert")]
        [InlineData("Shift+Insert")]
        [InlineData("Shift+Delete")]
        [InlineData("Ctrl+Space")]
        [InlineData("Ctrl+Backspace")]
        [InlineData("Ctrl+Delete")]
        [InlineData("Ctrl+Home")]
        [InlineData("Ctrl+End")]
        [InlineData("Ctrl+Left")]   // word and paragraph movement in every text field (S0116)
        [InlineData("Ctrl+Right")]
        [InlineData("Ctrl+Up")]
        [InlineData("Ctrl+Down")]
        [InlineData("Alt+F4")]
        [InlineData("Alt+Space")]
        public void EditingChordsAreRefused(string chord)
            => Assert.Equal(HotkeyRules.Refusal.EditingChord, Check(chord, Us));

        [Fact]
        public void ACtrlAltChordIsRefusedWhenAnInstalledLayoutTypesWithIt()
        {
            HotkeyRules.Verdict verdict = HotkeyRules.Check(Hotkey.Parse("Ctrl+Alt+E"), new[] { Us, German }, Typed);
            Assert.Equal(HotkeyRules.Refusal.TypesCharacter, verdict.Refusal);
            Assert.Equal("€", verdict.Character);
            Assert.Equal(German, verdict.Layout);
        }

        [Fact]
        public void TheSameChordIsFineWhenOnlyUsIsInstalled()
            => Assert.Equal(HotkeyRules.Refusal.None, Check("Ctrl+Alt+E", Us));

        [Theory]
        [InlineData("Ctrl+Shift+F12")]
        [InlineData("Ctrl+Shift+F11")]
        [InlineData("Ctrl+Shift+Alt+N")]
        [InlineData("Ctrl+Shift+C")]
        [InlineData("Ctrl+1")]
        [InlineData("Alt+Shift+F12")]
        public void TheShippedAndOrdinaryChordsAreAccepted(string chord)
            => Assert.Equal(HotkeyRules.Refusal.None, Check(chord, Us, German));

        [Fact]
        public void TheNewQuickNotesDefaultIsAccepted()
            => Assert.Equal(HotkeyRules.Refusal.None, Check(AppConfig.DefaultQuickNotesHotkey, Us, German));

        // The admitted trigger families (S0116, INPUT-CHORD 0.4): only the Ctrl-alone arrows join the
        // editing list; every other modifier set on an arrow, a keypad key, a bracket or a media key stands.
        [Theory]
        [InlineData("Ctrl+Shift+Left")]
        [InlineData("Alt+Up")]
        [InlineData("Ctrl+Alt+Right")]
        [InlineData("Ctrl+Numpad5")]
        [InlineData("Ctrl+Shift+NumpadAdd")]
        [InlineData("Ctrl+Equal")]
        [InlineData("Alt+Shift+BracketLeft")]
        [InlineData("Ctrl+MediaPlayPause")]
        [InlineData("Ctrl+Shift+AudioVolumeUp")]
        public void TheAdmittedTriggersAreAcceptedWithTheOtherModifierSets(string chord)
            => Assert.Equal(HotkeyRules.Refusal.None, Check(chord, Us, German));

        [Fact]
        public void ACtrlAltChordOnAnAdmittedTriggerAsksTheLayoutsToo()
        {
            // The ß key of a German keyboard is BracketLeft by virtual key, and AltGr+ß types a backslash.
            string? TypedBackslash(Hotkey chord, IntPtr layout)
                => layout == German && chord.Vk == 0xDB ? "\\" : null;

            HotkeyRules.Verdict verdict = HotkeyRules.Check(Hotkey.Parse("Ctrl+Alt+BracketLeft"), new[] { Us, German }, TypedBackslash);
            Assert.Equal(HotkeyRules.Refusal.TypesCharacter, verdict.Refusal);
            Assert.Equal("\\", verdict.Character);
            Assert.Equal(German, verdict.Layout);
            Assert.Equal(HotkeyRules.Refusal.None, HotkeyRules.Check(Hotkey.Parse("Ctrl+Alt+BracketLeft"), new[] { Us }, TypedBackslash).Refusal);
        }

        [Fact]
        public void OnlyCtrlAltChordsAskTheLayouts()
        {
            var asked = new List<string>();
            string? Recording(Hotkey chord, IntPtr layout) { asked.Add(chord.Display); return "x"; }
            HotkeyRules.Check(Hotkey.Parse("Alt+F9"), new[] { Us }, Recording);
            HotkeyRules.Check(Hotkey.Parse("Ctrl+Shift+F12"), new[] { Us }, Recording);
            Assert.Empty(asked);
            Assert.Equal(HotkeyRules.Refusal.TypesCharacter, HotkeyRules.Check(Hotkey.Parse("Ctrl+Alt+F9"), new[] { Us }, Recording).Refusal);
        }

        [Fact]
        public void EveryRefusalHasATranslatedReason()
        {
            foreach (HotkeyRules.Refusal refusal in new[] { HotkeyRules.Refusal.ShiftOnly, HotkeyRules.Refusal.EditingChord, HotkeyRules.Refusal.TypesCharacter })
                foreach (string language in Localization.Names)
                {
                    string text = HotkeyRules.Describe(new HotkeyRules.Verdict(refusal, "€", German), language);
                    Assert.False(string.IsNullOrWhiteSpace(text), refusal + " / " + language);
                    if (language != "English") Assert.NotEqual(HotkeyRules.ReasonKey(refusal), Localization.Translate(language, HotkeyRules.ReasonKey(refusal)));
                }
        }
    }

    /// <summary>
    /// Every key name the capture dialog can emit parses back to the same chord (ticket S0004, KC-2).
    /// PageUp/PageDown were offered by the dialog and unknown to the parser, so they came back as the
    /// Ctrl+Shift+F12 default; this is the guard that makes the next forgotten name impossible.
    /// </summary>
    public class HotkeyRoundTripTests
    {
        [Fact]
        public void EveryDialogKeyWithEveryModifierSetRoundTrips()
        {
            int checkedKeys = 0;
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (!HotkeyDialog.TryGetKeyName(key, out string name)) continue;
                checkedKeys++;
                for (int mask = 1; mask < 8; mask++)
                {
                    bool ctrl = (mask & 1) != 0, shift = (mask & 2) != 0, alt = (mask & 4) != 0;
                    var parts = new List<string>();
                    if (ctrl) parts.Add("Ctrl");
                    if (shift) parts.Add("Shift");
                    if (alt) parts.Add("Alt");
                    parts.Add(name);
                    string display = string.Join("+", parts);

                    Assert.True(Hotkey.TryParse(display, out Hotkey parsed), display);
                    Assert.Equal((int)key, parsed.Vk);
                    Assert.Equal(ctrl, parsed.Ctrl);
                    Assert.Equal(shift, parsed.Shift);
                    Assert.Equal(alt, parsed.Alt);
                    Assert.Equal(display, parsed.Display);
                }
                Assert.Equal(name, Hotkey.NameForVk((int)key));
            }
            Assert.True(checkedKeys >= 70, "the walk found only " + checkedKeys + " keys");
        }

        /// <summary>The admitted trigger families (S0116): the capture names each key the way the contract does.</summary>
        [Theory]
        [InlineData(Keys.Left, "Left")]
        [InlineData(Keys.Down, "Down")]
        [InlineData(Keys.NumPad0, "Numpad0")]
        [InlineData(Keys.NumPad9, "Numpad9")]
        [InlineData(Keys.Add, "NumpadAdd")]
        [InlineData(Keys.Decimal, "NumpadDecimal")]
        [InlineData(Keys.OemOpenBrackets, "BracketLeft")]
        [InlineData(Keys.OemCloseBrackets, "BracketRight")]
        [InlineData(Keys.OemPipe, "Backslash")]
        [InlineData(Keys.Oemplus, "Equal")]
        [InlineData(Keys.MediaPlayPause, "MediaPlayPause")]
        [InlineData(Keys.MediaPreviousTrack, "MediaTrackPrevious")]
        [InlineData(Keys.VolumeMute, "AudioVolumeMute")]
        public void TheCaptureNamesTheAdmittedKeysAsTheContractDoes(Keys key, string expected)
        {
            Assert.True(HotkeyDialog.TryGetKeyName(key, out string name));
            Assert.Equal(expected, name);
            Assert.True(Hotkey.TryParse("Ctrl+Shift+" + name, out Hotkey parsed));
            Assert.Equal((int)key, parsed.Vk);
        }

        [Fact]
        public void TheCapturePreviewIsMeasuredForTheWidestAdmittedChord()
        {
            Assert.True(Hotkey.TryParse(HotkeyDialog.LongestAdmittedChord, out Hotkey chord));
            Assert.True(HotkeyDialog.LongestAdmittedChord.Length >= HotkeyDialog.LongestChord.Length);
            Assert.True(chord.Ctrl && chord.Shift && chord.Alt);
        }

        [Theory]
        [InlineData("Ctrl+Shift+PageUp", 0x21)]
        [InlineData("Ctrl+Shift+PgUp", 0x21)]
        [InlineData("Alt+PageDown", 0x22)]
        [InlineData("Alt+PgDn", 0x22)]
        public void PageKeysParse(string text, int vk)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey parsed));
            Assert.Equal(vk, parsed.Vk);
        }
    }
}
