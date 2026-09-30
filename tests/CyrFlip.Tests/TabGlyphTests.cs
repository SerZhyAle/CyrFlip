using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The settings tab strip's icons (ticket S0022 A2): the pages that have a vocabulary record draw it,
    /// in the theme's text colour and nothing else (the mono look, ICON-RENDER section 10 C), no two pages
    /// share a glyph (ICON-SET rule 2), and the list has no slot for the picture nobody used.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class TabGlyphTests
    {
        private static readonly int[] Kinds = { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11 };

        [Fact]
        public void EveryMappedPageHasAVendoredGlyphAndNoTwoPagesShareOne()
        {
            string[] ids = Kinds.Select(SettingsForm.TabGlyphId).Where(id => id != null).Select(id => id!).ToArray();
            Assert.Equal(new[] { "app.settings", "app.info", "action.translate" }.OrderBy(i => i), ids.OrderBy(i => i));
            Assert.Equal(ids.Length, ids.Distinct().Count());
            foreach (string id in ids) Assert.True(GlyphRenderer.Has(id), id);
        }

        [Fact]
        public void ThePageIconsAreOnePicturePerPageInOrderWithNoUnusedSlot()
        {
            using ImageList images = SettingsForm.CreateTabIcons(Color.Black);
            Assert.Equal(Kinds.Length, images.Images.Count);
            Assert.Equal(Enumerable.Range(0, Kinds.Length), Kinds.Select(SettingsForm.IconIndex));
            Assert.Equal(new Size(SettingsForm.TabIconSize, SettingsForm.TabIconSize), images.ImageSize);
        }

        [Fact]
        public void AGlyphTabIsDrawnInTheThemesTextColourOnBothThemes()
        {
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark })
            {
                foreach (int kind in Kinds.Where(k => SettingsForm.TabGlyphId(k) != null))
                {
                    using Bitmap icon = SettingsForm.TabIcon(kind, palette.TextPrimary);
                    Assert.True(AllInkIs(icon, palette.TextPrimary), palette.Kind + " kind " + kind);
                }
            }
        }

        [Fact]
        public void TheColourCheckCanFail()
        {
            // The guard fact: the same walk against the wrong colour must say no, or the test above proves nothing.
            using Bitmap settings = SettingsForm.TabIcon(0, ThemePalette.Light.TextPrimary);
            Assert.False(AllInkIs(settings, ThemePalette.Dark.TextPrimary));
        }

        [Fact]
        public void APageWithoutARecordKeepsItsOwnPictureOnTheSameCanvas()
        {
            // Indicators has no record yet: drawn (not blank), and in the same ink as the vocabulary tabs.
            Color ink = Color.FromArgb(10, 20, 30);
            using Bitmap indicators = SettingsForm.TabIcon(1, ink);
            Assert.True(AllInkIs(indicators, ink));
            int inked = 0;
            for (int y = 0; y < indicators.Height; y++)
                for (int x = 0; x < indicators.Width; x++) if (indicators.GetPixel(x, y).A > 0) inked++;
            Assert.True(inked > 10);
        }

        private static bool AllInkIs(Bitmap bitmap, Color ink)
        {
            int checkedPixels = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color p = bitmap.GetPixel(x, y);
                    // An ImageList composites a partly covered edge pixel over a grey matte; only fully covered pixels carry the ink exactly.
                    if (p.A < 255) continue;
                    checkedPixels++;
                    if (Math.Abs(p.R - ink.R) > 2 || Math.Abs(p.G - ink.G) > 2 || Math.Abs(p.B - ink.B) > 2) return false;
                }
            return checkedPixels > 0;
        }
    }
}
