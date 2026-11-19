using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// INPUT-CHORD rules 1-4 - the stored chord token - and the rung-1 conformance vectors of its
    /// section 6. The vectors file is <b>generated from the real parsers</b> by
    /// <see cref="TheVectorsFileIsWhatTheParsersSay"/>: run it with <c>CYRFLIP_WRITE_CHORD_VECTORS=1</c>
    /// to rewrite it, then copy it to the catalog's <c>input-controls/vectors/</c>. Without the
    /// variable the test fails whenever the file and the parsers disagree, so neither can drift.
    /// </summary>
    public class InputChordContractTests
    {
        private static readonly string VectorsPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures", "InputChord", "chord-tokens.jsonl"));

        // ---- rule 3: an unknown token, or a second trigger, makes the chord inert ----------------

        [Theory]
        [InlineData("Ctrl+Shift+Hyper+F12")]   // an unknown modifier name
        [InlineData("Ctrl+Shift+MediaStop")]   // a key name 0.5 has not admitted (a later version may add it)
        [InlineData("Ctrl+Shift+Semicolon")]
        [InlineData("Ctrl+Numpad10")]
        [InlineData("Ctrl+Left+Right")]        // two admitted triggers are still two triggers
        [InlineData("Ctrl+A+B")]               // two triggers
        [InlineData("Ctrl+F12+F11")]
        [InlineData("Ctrl+F25")]
        [InlineData("Ctrl+Й")]                 // rule 1: the letter is the US key name, never the typed character
        [InlineData("Ctrl+Shift+0x41")]        // the display form of an unknown VK is not a stored token
        public void AnUnknownTokenOrASecondTriggerMakesTheKeyboardChordInert(string text)
        {
            Assert.False(Hotkey.TryParse(text, out _));
        }

        [Theory]
        [InlineData("Ctrl+Win+RightClick")]         // Win is never part of a mouse chord
        [InlineData("Ctrl+X1Click")]                // reserved for a later version
        [InlineData("Ctrl+RightClick+MiddleClick")] // two buttons
        [InlineData("Ctrl+Shift+F12")]              // a keyboard chord
        public void AnUnknownTokenOrASecondButtonMakesTheMouseChordInert(string text)
        {
            Assert.False(MouseChord.TryParse(text, out _));
        }

        /// <summary>The single fixed settings keep their documented default; that is rule 3's one allowance.</summary>
        [Fact]
        public void TheFixedSettingsStillFallBackToTheirDefault()
        {
            Assert.True(Hotkey.Parse("Ctrl+Shift+Hyper+F12").SameChord(Hotkey.Default));
            Assert.Equal(MouseChord.Default.Token, MouseChord.Parse("Ctrl+Win+RightClick").Token);
        }

        // ---- rule 1 and 0.5 item A: the admitted trigger vocabulary (ticket S0116) ---------------

        [Theory]
        [InlineData("Ctrl+Shift+Left", 0x25, "Ctrl+Shift+Left")]
        [InlineData("ctrl+shift+RIGHT", 0x27, "Ctrl+Shift+Right")]
        [InlineData("Ctrl+Shift+NumPad5", 0x65, "Ctrl+Shift+Numpad5")] // row 28 of the 0.2 vector file
        [InlineData("Alt+bracketleft", 0xDB, "Alt+BracketLeft")]
        [InlineData("Ctrl+Alt+Period", 0xBE, "Ctrl+Alt+Period")]
        [InlineData("Ctrl+Alt+MEDIAPLAYPAUSE", 0xB3, "Ctrl+Alt+MediaPlayPause")]
        [InlineData("Ctrl+Shift+audiovolumemute", 0xAD, "Ctrl+Shift+AudioVolumeMute")]
        public void AnAdmittedTriggerIsACanonicalTokenReadWithoutRegardToCase(string text, int vk, string canonical)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey parsed), text);
            Assert.Equal(vk, parsed.Vk);
            Assert.Equal(canonical, parsed.Display);
        }

        [Fact]
        public void EveryAdmittedTriggerNamesItsOwnVirtualKeyBothWays()
        {
            var seen = new HashSet<int>();
            foreach (string trigger in AdmittedTriggers)
            {
                Assert.True(Hotkey.TryParse("Ctrl+" + trigger, out Hotkey parsed), trigger);
                Assert.Equal(trigger, parsed.KeyName);
                Assert.Equal(trigger, Hotkey.NameForVk(parsed.Vk));
                Assert.True(seen.Add(parsed.Vk), "two tokens share the virtual key of " + trigger);
            }
        }

        // ---- rule 2: read leniently, written back canonically -----------------------------------

        [Theory]
        [InlineData("control + shift + f12", "Ctrl+Shift+F12")]
        [InlineData("Shift+Ctrl+PgUp", "Ctrl+Shift+PageUp")]
        [InlineData("meta+space", "Win+Space")]
        public void AFixedChordIsReadInItsCanonicalSpelling(string stored, string canonical)
        {
            var cfg = new AppConfig();
            var key = new MemoryConfigKey();
            key.Values["CaseHotkey"] = stored;
            Assert.Equal(canonical, cfg.ReadHotkey(key, "CaseHotkey", "Ctrl+Shift+F11"));
            Assert.Empty(cfg.UnreadableValues);
        }

        [Fact]
        public void TableRowsAreRewrittenCanonicallyAndAnInertOneIsLeftAsStored()
        {
            var conversions = new List<LayoutConversionProfile>
            {
                new LayoutConversionProfile { Id = "a", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "shift+ctrl+f12" },
                new LayoutConversionProfile { Id = "b", SourceKlid = "00000409", TargetKlid = "00000422", Hotkey = "Ctrl+Shift+Hyper+F11" },
            };
            Assert.True(AppConfig.SanitizeConversionProfiles(conversions));
            Assert.Equal("Ctrl+Shift+F12", conversions[0].Hotkey);
            Assert.Equal("Ctrl+Shift+Hyper+F11", conversions[1].Hotkey);

            var translations = new List<TranslationProfile>
            {
                new TranslationProfile { Id = "t", TargetLang = "en", Hotkey = "alt+shift+f9" },
            };
            Assert.True(AppConfig.SanitizeTranslationProfiles(translations));
            Assert.Equal("Shift+Alt+F9", translations[0].Hotkey);
        }

        [Fact]
        public void ACanonicalTableIsNotReportedAsRepaired()
        {
            var rows = new List<LayoutConversionProfile>
            {
                new LayoutConversionProfile { Id = "a", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12" },
                new LayoutConversionProfile { Id = "b", SourceKlid = "00000409", TargetKlid = "00000422", Hotkey = "" },
            };
            Assert.False(AppConfig.SanitizeConversionProfiles(rows));
        }

        private sealed class MemoryConfigKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public object? GetValue(string name) => Values.TryGetValue(name, out object? v) ? v : null;
            public void SetValue(string name, object value, Microsoft.Win32.RegistryValueKind kind) => Values[name] = value;
        }

        // ---- section 6 rung 1: the vectors ------------------------------------------------------

        // The 0.2 vocabulary's rows. Row 28 of the 0.2 file (Ctrl+Shift+NumPad5, inert) moved to the
        // admitted rows below: INPUT-CHORD 0.5 item A reads it as Ctrl+Shift+Numpad5.
        private static readonly string[] ClassicKeyboardInputs =
        {
            // canonical
            "Ctrl+Shift+F12", "ctrl+shift+f12", "Shift+Ctrl+F12", "control + 9", "Win+Space", "meta+space",
            "Super+Space", "Alt+F4", "Ctrl+Alt+Return", "Ctrl+Escape", "Ctrl+Del", "Ctrl+Ins",
            "Ctrl+Shift+PgUp", "Ctrl+Shift+PgDn", "Ctrl+Shift+PrtSc", "Ctrl+Shift+PrtScn",
            "Ctrl+Shift+Alt+N", "Win+Alt+Shift+Ctrl+Backspace", "F24", "A", "Ctrl+Ctrl+A", "Ctrl++F12",
            "Ctrl+Shift+F12+",
            // inert
            "", "   ", "Ctrl+Shift", "Ctrl+Shift+Hyper+F12", "Ctrl+A+B", "Ctrl+F25",
            "Ctrl+F0", "Ctrl+Shift+0x41", "Ctrl+Й", "Ctrl+RightClick",
        };

        /// <summary>The triggers INPUT-CHORD 0.4 admitted, in the contract's own spelling (ticket S0116).</summary>
        private static readonly string[] AdmittedTriggers =
        {
            "Left", "Up", "Right", "Down",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
            "NumpadMultiply", "NumpadAdd", "NumpadSubtract", "NumpadDecimal", "NumpadDivide",
            "BracketLeft", "BracketRight", "Period", "Comma", "Equal", "Minus", "Backslash",
            "MediaPlayPause", "MediaTrackNext", "MediaTrackPrevious", "AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute",
        };

        /// <summary>
        /// The 0.2 rows plus what 0.5 item B asks the regenerated file to carry: one canonical row per
        /// admitted trigger, one case-insensitive spelling per family, the modifier combinations with an
        /// arrow and a keypad key, and the inert neighbours the contract has not admitted.
        /// </summary>
        private static readonly string[] KeyboardInputs = BuildKeyboardInputs();

        private static string[] BuildKeyboardInputs()
        {
            var inputs = new List<string>(ClassicKeyboardInputs);
            foreach (string trigger in AdmittedTriggers)
                inputs.Add("Ctrl+Shift+" + trigger);
            inputs.AddRange(new[]
            {
                // one case-insensitive spelling per family (item A)
                "ctrl+shift+left", "Ctrl+Shift+NumPad5", "ctrl+alt+numpadadd", "Alt+bracketleft", "Ctrl+Alt+MEDIAPLAYPAUSE",
                // the modifier combinations with an arrow and a keypad key, in every written order
                "Win+Alt+Shift+Ctrl+Up", "Shift+Alt+Down", "Alt+Shift+Numpad0", "Win+Alt+Shift+Ctrl+NumpadDecimal",
                // inert: two admitted triggers, and the neighbours of the families that 0.5 has not admitted
                "Ctrl+Left+Right", "Ctrl+Numpad5+Numpad6", "Ctrl+Numpad10", "Ctrl+Shift+Numpad", "Ctrl+Shift+MediaStop",
                "Ctrl+Semicolon", "Ctrl+Plus", "Ctrl+Shift+NumLock",
            });
            return inputs.ToArray();
        }

        private static readonly string[] MouseInputs =
        {
            // canonical
            "Ctrl+RightClick", "ctrl + rmb", "Ctrl+Right", "Alt+Ctrl+RightClick", "Shift+RightClick",
            "MiddleClick", "mmb", "Shift+Middle", "Ctrl+MiddleClick",
            // inert
            "", "RightClick", "rmb", "Ctrl+Shift", "Ctrl+Win+RightClick", "Ctrl+X1Click",
            "Ctrl+RightClick+MiddleClick", "Ctrl+Shift+F12", "Ctrl+LeftClick",
        };

        internal static string BuildVectors()
        {
            var text = new StringBuilder();
            foreach (string input in KeyboardInputs)
                Line(text, "keyboard", input, Hotkey.TryParse(input, out Hotkey key) ? key.Display : null);
            foreach (string input in MouseInputs)
                Line(text, "mouse", input, MouseChord.TryParse(input, out MouseChord mouse) ? mouse.Token : null);
            return text.ToString();
        }

        private static void Line(StringBuilder text, string kind, string input, string? canonical)
        {
            text.Append("{\"kind\":\"").Append(kind).Append("\",\"in\":").Append(Json(input));
            if (canonical == null) text.Append(",\"inert\":true}");
            else text.Append(",\"canonical\":").Append(Json(canonical)).Append('}');
            text.Append('\n');
        }

        private static string Json(string value)
        {
            var s = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') s.Append('\\').Append(c);
                else if (c < 0x20 || c > 0x7E) s.Append("\\u").Append(((int)c).ToString("x4"));
                else s.Append(c);
            }
            return s.Append('"').ToString();
        }

        [Fact]
        public void TheVectorsFileIsWhatTheParsersSay()
        {
            string expected = BuildVectors();
            if (Environment.GetEnvironmentVariable("CYRFLIP_WRITE_CHORD_VECTORS") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(VectorsPath)!);
                File.WriteAllText(VectorsPath, expected, new UTF8Encoding(false));
            }
            Assert.True(File.Exists(VectorsPath), "missing " + VectorsPath);
            Assert.Equal(expected, File.ReadAllText(VectorsPath, Encoding.UTF8));
        }

        /// <summary>Every canonical row parses back to itself: the writer emits only what the reader reads (rule 1).</summary>
        [Fact]
        public void EveryCanonicalFormRoundTrips()
        {
            foreach (string input in KeyboardInputs)
                if (Hotkey.TryParse(input, out Hotkey key))
                {
                    Assert.True(Hotkey.TryParse(key.Display, out Hotkey again), key.Display);
                    Assert.Equal(key.Display, again.Display);
                }
            foreach (string input in MouseInputs)
                if (MouseChord.TryParse(input, out MouseChord mouse))
                {
                    Assert.True(MouseChord.TryParse(mouse.Token, out MouseChord again), mouse.Token);
                    Assert.Equal(mouse.Token, again.Token);
                }
        }
    }
}
