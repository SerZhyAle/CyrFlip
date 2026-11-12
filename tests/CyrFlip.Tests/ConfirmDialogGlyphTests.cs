using System;
using System.Linq;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0060 I-M8: the message box draws the vocabulary's picture of its kind, in the theme, instead of the
    /// system's light-theme bitmap - a status record in its state's role, a content record in the accent.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ConfirmDialogGlyphTests
    {
        [Theory]
        [InlineData(MessageBoxIcon.Information, AppGlyphs.Info)]
        [InlineData(MessageBoxIcon.Question, AppGlyphs.Info)]
        [InlineData(MessageBoxIcon.Warning, AppGlyphs.StatusWarning)]
        [InlineData(MessageBoxIcon.Error, AppGlyphs.StatusError)]
        public void EachKindOfBoxHasItsVocabularyGlyph(MessageBoxIcon icon, string glyph)
        {
            Assert.Equal(glyph, ConfirmDialog.GlyphFor(icon));
            Assert.True(GlyphRenderer.Has(glyph));
        }

        [Fact]
        public void ABoxWithoutAKindHasNoGlyph() => Assert.Null(ConfirmDialog.GlyphFor(MessageBoxIcon.None));

        [Fact]
        public void TheGlyphTakesItsStateRoleInEveryPalette() => OnSta(() =>
        {
            using var error = new ConfirmStateGlyph(AppGlyphs.StatusError, 32);
            using var warning = new ConfirmStateGlyph(AppGlyphs.StatusWarning, 32);
            using var info = new ConfirmStateGlyph(AppGlyphs.Info, 32);
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
            {
                Assert.Equal(palette.Danger, error.InkFor(palette));
                Assert.Equal(palette.Warning, warning.InkFor(palette));
                Assert.Equal(palette.Accent, info.InkFor(palette));
            }
        });

        [Fact]
        public void TheBoxShowsTheGlyphAndNoSystemPicture() => OnSta(() =>
        {
            using var box = new ConfirmDialog("English", "Delete?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true, owned: true);
            Assert.NotNull(box.StateGlyph);
            Assert.Equal(AppGlyphs.StatusWarning, box.StateGlyph!.Glyph);
            Assert.False(box.StateGlyph.TabStop);
            Assert.Empty(Descendants(box).OfType<PictureBox>());

            using var plain = new ConfirmDialog("English", "Done.", MessageBoxButtons.OK, MessageBoxIcon.None, danger: false, owned: true);
            Assert.Null(plain.StateGlyph);
        });

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the UI thread of the test hung");
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }
    }
}
