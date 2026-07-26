using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Pure-logic coverage for <see cref="InputLayouts"/>: the KLID → langid decode and the legacy
    /// Preload/Substitutes table construction, including the device-handle scheme for a second layout
    /// of one language. The live LoadKeyboardLayout calls and registry I/O are verified manually.
    /// </summary>
    public class InputLayoutsTests
    {
        [Theory]
        [InlineData("00000409", 0x0409)]
        [InlineData("00000419", 0x0419)]
        [InlineData("00000422", 0x0422)]
        [InlineData("00010419", 0x0419)] // Russian (Typewriter) - variant, same language id
        [InlineData("00020422", 0x0422)] // Ukrainian (Enhanced)
        public void LangIdOfReadsTheLowWord(string klid, int expected)
        {
            Assert.Equal((ushort)expected, InputLayouts.LangIdOf(klid));
        }

        [Fact]
        public void PreloadListsOneLayoutPerLanguageDirectly()
        {
            var (preload, subs) = InputLayouts.BuildPreload(new List<string> { "00000419", "00000409", "00000422" });

            Assert.Equal("00000419", preload["1"]);
            Assert.Equal("00000409", preload["2"]);
            Assert.Equal("00000422", preload["3"]);
            Assert.Empty(subs); // no two layouts share a language, so no device handles are needed
        }

        [Fact]
        public void SecondLayoutOfALanguageGetsADeviceHandleAndSubstitute()
        {
            // US + US-Dvorak: both language 0409, so the second must go through a d-handle.
            var (preload, subs) = InputLayouts.BuildPreload(new List<string> { "00000409", "00010409" });

            Assert.Equal("00000409", preload["1"]);
            Assert.Equal("d0010409", preload["2"]);
            Assert.Equal("00010409", subs["d0010409"]);
        }

        [Fact]
        public void ThirdLayoutOfALanguageIncrementsTheDeviceHandle()
        {
            var (preload, subs) = InputLayouts.BuildPreload(new List<string> { "00000409", "00010409", "00020409" });

            Assert.Equal("00000409", preload["1"]);
            Assert.Equal("d0010409", preload["2"]);
            Assert.Equal("d0020409", preload["3"]);
            Assert.Equal("00010409", subs["d0010409"]);
            Assert.Equal("00020409", subs["d0020409"]);
        }

        [Fact]
        public void PreloadPreservesOrderAcrossMixedLanguages()
        {
            // Order matters: entry "1" is the default layout. RU, US, RU-Typewriter, UK.
            var (preload, subs) = InputLayouts.BuildPreload(new List<string> { "00000419", "00000409", "00010419", "00000422" });

            Assert.Equal("00000419", preload["1"]);
            Assert.Equal("00000409", preload["2"]);
            Assert.Equal("d0010419", preload["3"]); // second Russian layout
            Assert.Equal("00000422", preload["4"]);
            Assert.Equal("00010419", subs["d0010419"]);
        }

        [Fact]
        public void GroupByLanguageTagKeepsAllKlidsUnderOneLanguage()
        {
            Dictionary<string, List<string>> byLang = InputLayouts.GroupByLanguageTag(
                new List<string> { "00000409", "00010409", "00000419" });

            // Two English layouts collapse to one tag holding both; Russian is its own.
            List<string>? en = null;
            List<string>? ru = null;
            foreach (var kv in byLang)
            {
                if (kv.Value.Contains("00000409")) en = kv.Value;
                if (kv.Value.Contains("00000419")) ru = kv.Value;
            }
            Assert.NotNull(en);
            Assert.Equal(2, en!.Count);
            Assert.Contains("00010409", en);
            Assert.NotNull(ru);
            Assert.Single(ru!);
        }

        [Fact]
        public void ToggleCodesOfferTheFourWindowsChoices()
        {
            Assert.Equal(new[] { "1", "2", "4", "3" }, LanguageHotkeys.ToggleCodes);
            Assert.Contains("Ctrl+Shift", LanguageHotkeys.ToggleLabel("2"));
            Assert.Contains("(3)", LanguageHotkeys.ToggleLabel("3")); // the "off" code keeps its number visible
        }
    }
}
