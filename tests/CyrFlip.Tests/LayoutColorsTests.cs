using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The layout palette lives in two places on purpose - <see cref="LayoutStyle"/> for the exe (which
    /// ships alone and can read nothing at runtime) and <c>vscode-extension/src/layout-colors.json</c>
    /// for the extension (which vsce packages separately). Two places means they can drift, and they
    /// already had: the extension knew EN/RU/UK and painted the other ten curated languages grey while
    /// the app drew them in colour. Nothing about that failure is visible in either build - it only
    /// shows up as a wrong colour on someone's screen. This test is the thing that catches it.
    ///
    /// <para><c>tools/IconGen</c> is deliberately absent here: it compiles LayoutStyle.cs directly
    /// (see its csproj), so it cannot disagree.</para>
    /// </summary>
    public class LayoutColorsTests
    {
        private static readonly string PaletteFile = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "vscode-extension", "src", "layout-colors.json"));

        private static Dictionary<string, object> Palette()
        {
            Assert.True(File.Exists(PaletteFile), "Shared palette missing: " + PaletteFile);
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(PaletteFile));
            Assert.NotNull(parsed);
            return parsed!;
        }

        private static Dictionary<string, object> Section(string name)
        {
            Assert.True(Palette().TryGetValue(name, out object? section), "Palette has no \"" + name + "\" section");
            var map = section as Dictionary<string, object>;
            Assert.NotNull(map);
            return map!;
        }

        private static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        [Fact]
        public void TheSharedPaletteMatchesTheAppExactly()
        {
            Dictionary<string, object> curated = Section("curated");

            foreach (KeyValuePair<string, Color> entry in LayoutStyle.Curated)
            {
                Assert.True(curated.ContainsKey(entry.Key),
                    "The extension palette is missing " + entry.Key + ", which the app draws as " + Hex(entry.Value));
                Assert.Equal(Hex(entry.Value), Convert.ToString(curated[entry.Key]));
            }

            // ..and nothing extra: an entry the app does not know would render in the editor and
            // nowhere else, which is the same drift in the other direction.
            foreach (string code in curated.Keys)
                Assert.True(LayoutStyle.Curated.ContainsKey(code),
                    "The extension palette has " + code + ", which the app does not curate");
        }

        /// <summary>
        /// The fallback is an algorithm, not a table, so the two implementations can only be kept in
        /// step by pinning its constants and a few of its results. The extension has no test runner of
        /// its own; these samples are what a change to its colorFor() has to be checked against.
        /// </summary>
        [Fact]
        public void TheFallbackConstantsAndSamplesMatchTheApp()
        {
            Dictionary<string, object> fallback = Section("fallback");
            Assert.Equal(LayoutStyle.HashSeed, Convert.ToInt32(fallback["hashSeed"]));
            Assert.Equal(LayoutStyle.HashMultiplier, Convert.ToInt32(fallback["hashMultiplier"]));
            Assert.Equal(LayoutStyle.FallbackSaturation, Convert.ToDouble(fallback["saturation"]), 6);
            Assert.Equal(LayoutStyle.FallbackLightness, Convert.ToDouble(fallback["lightness"]), 6);

            Dictionary<string, object> samples = Section("fallbackSamples");
            Assert.NotEmpty(samples);
            foreach (KeyValuePair<string, object> sample in samples)
            {
                Assert.False(LayoutStyle.Curated.ContainsKey(sample.Key),
                    sample.Key + " is curated, so it never reaches the fallback - it is not a sample of it");
                Assert.Equal(Convert.ToString(sample.Value), Hex(LayoutStyle.ColorFor(sample.Key)));
            }
        }

        /// <summary>
        /// The property that makes the fallback worth having at all: an uncurated layout gets its own
        /// stable, bright colour rather than one shared grey. Without this the whole exercise reduces
        /// to "everything outside the thirteen looks the same", which is what the extension used to do.
        /// </summary>
        [Fact]
        public void UncuratedLayoutsGetDistinctStableColours()
        {
            Assert.Equal(LayoutStyle.ColorFor("PL"), LayoutStyle.ColorFor("PL"));
            Assert.NotEqual(LayoutStyle.ColorFor("PL"), LayoutStyle.ColorFor("JA"));

            // "Bright" is the point: the marker is drawn on unknown backgrounds with a black outline.
            foreach (string code in new[] { "PL", "JA", "TR", "KO", "CS", "VI", "NL" })
            {
                Color c = LayoutStyle.ColorFor(code);
                Assert.True(c.GetBrightness() > 0.5, code + " came out dark: " + Hex(c));
            }
        }
    }
}
