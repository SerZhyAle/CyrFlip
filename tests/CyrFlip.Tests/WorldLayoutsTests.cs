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
