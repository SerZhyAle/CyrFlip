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

        /// <summary>Perceptual-ish RGB distance ("redmean"), good enough to catch two colours that
        /// would read as the same dot on screen.</summary>
        private static double Distance(Color a, Color b)
        {
            double rm = (a.R + b.R) / 2.0, dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db);
        }

        [Fact]
        public void TheSharedLanguagePaletteMatchesTheAppExactly()
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

        [Fact]
        public void TheSharedLayoutPaletteMatchesTheAppExactly()
        {
            Dictionary<string, object> layouts = Section("layouts");

            foreach (KeyValuePair<string, Color> entry in LayoutStyle.Layouts)
            {
                Assert.True(layouts.ContainsKey(entry.Key),
                    "The extension palette is missing layout " + entry.Key + " (" + Hex(entry.Value) + ")");
                Assert.Equal(Hex(entry.Value), Convert.ToString(layouts[entry.Key]));
            }

            foreach (string klid in layouts.Keys)
                Assert.True(LayoutStyle.Layouts.ContainsKey(klid),
                    "The extension palette has layout " + klid + ", which the app does not colour");

            Assert.True(Palette().TryGetValue("other", out object? other), "Palette has no \"other\" colour");
            Assert.Equal(Hex(LayoutStyle.Other), Convert.ToString(other));
        }

        /// <summary>
        /// The marker is drawn over the user's text by the app and, inside the editor, by the extension.
        /// A translucency that differs between them is exactly the kind of mismatch nobody spots until
        /// the two markers sit next to each other on screen.
        /// </summary>
        [Fact]
        public void TheExtensionDrawsTheMarkerAtTheSameOpacityAsTheApp()
        {
            Assert.True(Palette().TryGetValue("markerOpacity", out object? opacity), "Palette has no \"markerOpacity\"");
            Assert.Equal(LayoutStyle.MarkerOpacity, Convert.ToSingle(opacity), 3);
        }

        /// <summary>
        /// The colour table and the curated layout list are two halves of one promise: every layout
        /// CyrFlip offers to install has a colour, and no colour describes a layout nobody can reach.
        /// They are edited in different files, so nothing but this test keeps them in step.
        /// </summary>
        [Fact]
        public void EveryCuratedLayoutHasItsOwnColour()
        {
            var curatedKlids = new List<string>();
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
                foreach (string klid in language.Klids)
                {
                    curatedKlids.Add(klid);
                    Assert.True(LayoutStyle.Layouts.ContainsKey(klid),
                        klid + " (" + language.Code + ") is offered in the settings but has no colour");
                }

            Assert.Equal(curatedKlids.Count, LayoutStyle.Layouts.Count);
            foreach (string klid in LayoutStyle.Layouts.Keys)
                Assert.True(curatedKlids.Contains(klid), klid + " has a colour but is not a curated layout");
        }

        /// <summary>
        /// In the caret overlay's dot mode the colour is the entire marker - there are no letters to
        /// fall back on - so two layouts that render as the same dot are indistinguishable, which is
        /// the failure this whole table exists to prevent.
        /// </summary>
        [Fact]
        public void EveryLayoutColourIsTellableFromEveryOther()
        {
            var all = new List<KeyValuePair<string, Color>>(LayoutStyle.Layouts);
            all.Add(new KeyValuePair<string, Color>("other", LayoutStyle.Other));

            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    double d = Distance(all[i].Value, all[j].Value);
                    Assert.True(d >= 40,
                        all[i].Key + " " + Hex(all[i].Value) + " and " + all[j].Key + " " + Hex(all[j].Value)
                        + " are too close to tell apart (" + Math.Round(d) + ")");
                }
        }

        /// <summary>The marker is drawn over arbitrary application backgrounds, so no shade may be dark.</summary>
        [Fact]
        public void EveryLayoutColourIsBright()
        {
            foreach (KeyValuePair<string, Color> entry in LayoutStyle.Layouts)
                Assert.True(entry.Value.GetBrightness() > 0.45,
                    entry.Key + " came out dark: " + Hex(entry.Value));
            Assert.True(LayoutStyle.Other.GetBrightness() > 0.45, "Other came out dark: " + Hex(LayoutStyle.Other));
        }

        /// <summary>
        /// The three rungs of <see cref="LayoutStyle.ColorForLayout"/>: the layout's own shade, then
        /// its language's colour (a British or Swiss keyboard is not curated but is still English or
        /// German), then the one colour for everything else.
        /// </summary>
        [Fact]
        public void AnUncuratedLayoutFallsBackToItsLanguageThenToOther()
        {
            Assert.Equal(LayoutStyle.Layouts["00010419"], LayoutStyle.ColorForLayout("00010419", "RU"));
            Assert.Equal(LayoutStyle.Curated["EN"], LayoutStyle.ColorForLayout("00000809", "EN")); // UK keyboard
            Assert.Equal(LayoutStyle.Curated["EN"], LayoutStyle.ColorForLayout(null, "EN"));
            Assert.Equal(LayoutStyle.Other, LayoutStyle.ColorForLayout("00000415", "PL"));
            Assert.Equal(LayoutStyle.Other, LayoutStyle.ColorFor("JA"));
        }
    }
}
