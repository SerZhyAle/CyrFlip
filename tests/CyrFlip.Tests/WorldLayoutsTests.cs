using System;
using System.Collections.Generic;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class WorldLayoutsTests
    {
        [Fact]
        public void CuratedSetContainsTheExpectedLanguagesAndCommonVariants()
        {
            // The world's ten most-spoken languages, plus German, Italian and Ukrainian (requested).
            Assert.Equal(13, WorldLayouts.Popular.Length);
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
            {
                Assert.True(codes.Add(language.Code));
                Assert.NotEmpty(language.Klids);
                Assert.NotEmpty(language.Name);
                foreach (string klid in language.Klids) Assert.Matches("^[0-9A-F]{8}$", klid);
            }
            foreach (string expected in new[] { "EN", "ZH", "HI", "ES", "FR", "AR", "BN", "PT", "RU", "UR", "DE", "IT", "UK" })
                Assert.Contains(expected, codes);
            Assert.True(WorldLayouts.Popular[0].Klids.Length > 1); // English variants are intentional.
        }

        [Fact]
        public void ProfileIsUsableOnlyWithBothLayoutsAndAHotkey()
        {
            var profile = new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419" };
            Assert.False(profile.IsUsable);
            profile.Hotkey = "Ctrl+Shift+F9";
            Assert.True(profile.IsUsable);
        }

        [Fact]
        public void EveryCuratedKlidCarriesItsOwnLanguageCode()
        {
            // A KLID's low 4 hex digits are its language id, so the code the indicator shows and the
            // code the curated table claims must agree - a typo'd KLID would silently label a row wrong.
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
                foreach (string klid in language.Klids)
                {
                    Assert.Equal(language.Code, WorldLayouts.LabelForKlid(klid));
                    Assert.Equal(language.Code, WorldLayouts.CodeForKlid(klid));
                    Assert.Equal(language.Code, WorldLayouts.CodeForKlid(klid.ToLowerInvariant()));
                }
        }

        [Fact]
        public void LayoutsOutsideTheCuratedSetStillGetALanguageCode()
        {
            Assert.Equal("", WorldLayouts.LabelForKlid("00000415"));  // Polish is not curated...
            Assert.Equal("PL", WorldLayouts.CodeForKlid("00000415")); // ...but still reads as PL, not as a KLID
            Assert.Equal("", WorldLayouts.CodeForKlid(null));
            Assert.Equal("zzzz", WorldLayouts.CodeForKlid("zzzz"));   // unparsable input is returned as-is
        }

        /// <summary>
        /// The settings tables (via <see cref="WorldLayouts.CodeForKlid"/>) and the live marker (via
        /// <see cref="CursorIndicator.DetectLayout"/>) decode a language id through this one method.
        /// They used to own a copy each, with different fallbacks - "??" on the marker, the raw KLID in
        /// the table - so the same exotic layout could read two different ways in the same window.
        /// </summary>
        [Theory]
        [InlineData(0x0409, "EN")]  // US
        [InlineData(0x0809, "EN")]  // UK English - a different sublang, still EN
        [InlineData(0x0419, "RU")]
        [InlineData(0x0422, "UK")]
        [InlineData(0x0415, "PL")]  // outside the curated 13
        [InlineData(0x0411, "JA")]
        public void ALanguageIdDecodesToTheCodeTheMarkerDraws(int langId, string expected)
            => Assert.Equal(expected, WorldLayouts.CodeForLangId(langId));

        [Fact]
        public void ALanguageWindowsCannotNameShowsItsIdInsteadOfQuestionMarks()
        {
            // 0x0C00 is the "custom locale" placeholder: no CultureInfo, and the old code drew "??"
            // on the cursor, the caret and the tray icon alike - three surfaces saying nothing at all.
            string code = WorldLayouts.CodeForLangId(0x0C00);
            Assert.NotEqual("??", code);
            Assert.Equal("0C00", code);
        }

        [Fact]
        public void TheCuratedLanguagesAreVisuallyDistinct()
        {
            // Reflection keeps the test project free of a System.Drawing reference; two languages
            // sharing a colour would make the caret/tray marker ambiguous at a glance.
            MethodInfo colorFor = typeof(AppConfig).Assembly.GetType("CyrFlip.LayoutStyle", true)!
                .GetMethod("ColorFor", BindingFlags.Public | BindingFlags.Static)!;
            var seen = new Dictionary<string, string>();
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
            {
                string color = colorFor.Invoke(null, new object[] { language.Code })!.ToString()!;
                Assert.False(seen.ContainsKey(color), language.Code + " shares its colour with another curated language");
                seen[color] = language.Code;
            }
            Assert.Equal(WorldLayouts.Popular.Length, seen.Count);
        }
    }
}
