using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The clipboard history strip's four glyphs (ticket S0022 A3): their colours hold 3 : 1 on every row
    /// of every theme - the unpinned pin used to be white on a white row - close and delete are two
    /// different pictures (ICON-SET rule 2), and the owner-drawn strip is reachable by a screen reader
    /// under the record's name in every UI language (ICON-RENDER rule 8).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ClipboardHistoryGlyphTests
    {
        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher) =>
                cipher.StartsWith("B64:", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(4))) : null;
        }

        private static readonly ThemePalette[] Palettes = { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast };

        public static IEnumerable<object[]> Themes() => Palettes.Select(p => new object[] { (int)p.Kind });

        [Theory]
        [MemberData(nameof(Themes))]
        public void EveryGlyphColourHolds3To1OnBothRowColours(int kindValue)
        {
            ThemeKind kind = (ThemeKind)kindValue;
            ThemePalette palette = ThemePalette.For(kind);
            foreach (Color row in new[] { palette.SurfaceRaised, palette.SurfaceAlternate })
            {
                Assert.True(ThemePalette.Contrast(HistoryStripGlyphs.ActionColor(palette), row) >= 3.0, kind + " action glyph on " + row);
                Assert.True(ThemePalette.Contrast(HistoryStripGlyphs.PinColor(false, palette), row) >= 3.0, kind + " unpinned pin on " + row);
                Assert.True(ThemePalette.Contrast(HistoryStripGlyphs.PinColor(true, palette), row) >= 3.0, kind + " pinned pin on " + row);
            }
        }

        [Theory]
        [MemberData(nameof(Themes))]
        public void ThePinIsVisibleOnTheRowWhenItIsRendered(int kindValue)
        {
            // The defect this replaces was a glyph drawn in a colour equal to the row: rendered, the
            // strongest pixel of the glyph must stand clear of the row it is on.
            ThemeKind kind = (ThemeKind)kindValue;
            ThemePalette palette = ThemePalette.For(kind);
            foreach (Color row in new[] { palette.SurfaceRaised, palette.SurfaceAlternate })
                foreach (bool pinned in new[] { false, true })
                {
                    using var bitmap = new Bitmap(40, 40);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(row);
                        Assert.True(HistoryStripGlyphs.DrawPin(g, new Rectangle(0, 0, 40, 40), pinned, palette));
                    }
                    double best = 0;
                    for (int y = 0; y < 40; y++)
                        for (int x = 0; x < 40; x++) best = Math.Max(best, ThemePalette.Contrast(bitmap.GetPixel(x, y), row));
                    Assert.True(best >= 3.0, kind + " pinned=" + pinned + " row " + row + " best contrast " + best);
                }
        }

        [Fact]
        public void PinnedIsTheFilledPinAndNotPinnedIsTheOutlineWithMoreInkWhenFilled()
        {
            Assert.Equal("action.pin", HistoryStripGlyphs.PinId(true));
            Assert.Equal("action.pin--off", HistoryStripGlyphs.PinId(false));
            Assert.True(Ink(AppGlyphs.Pin) > Ink(AppGlyphs.PinOff), "the pinned (filled) form must carry more ink than the outline");
        }

        [Fact]
        public void CloseAndDeleteAreNotTheSamePicture()
        {
            Assert.NotEqual(AppGlyphs.Close, AppGlyphs.Delete);
            using Bitmap? close = GlyphRenderer.Render(AppGlyphs.Close, 24, Color.Black);
            using Bitmap? delete = GlyphRenderer.Render(AppGlyphs.Delete, 24, Color.Black);
            bool differ = false;
            for (int y = 0; y < 24 && !differ; y++)
                for (int x = 0; x < 24 && !differ; x++) differ = close!.GetPixel(x, y) != delete!.GetPixel(x, y);
            Assert.True(differ);
        }

        private static int Ink(string id)
        {
            using Bitmap? bitmap = GlyphRenderer.Render(id, 48, Color.Black);
            int sum = 0;
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++) sum += bitmap!.GetPixel(x, y).A;
            return sum;
        }

        // -- accessibility ------------------------------------------------------------------------------

        [Fact]
        public void TheStripNamesItsGlyphOnlyControlsInEveryLanguage() => OnUiThread(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            var service = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher());
            try
            {
                string text = "first entry";
                service.Import(new[] { new ExchangeClipboardItem { Uuid = ClipboardHistoryService.Hash(text), Text = text, CopiedAtUtc = DateTime.UtcNow, Pinned = true } });
                foreach (string language in Localization.Names)
                {
                    using var form = new ClipboardHistoryWindow(service, new AppConfig { UiLanguage = language }, () => { });
                    IntPtr handle = form.Handle;
                    AccessibleObject strip = form.AccessibilityObject;
                    Assert.Equal(4, strip.GetChildCount());   // search, close, then pin and delete of the one row
                    string[] keys = { "Поиск по истории", "Закрыть", "Закрепить", "Удалить" };
                    for (int i = 0; i < 4; i++)
                    {
                        AccessibleObject child = strip.GetChild(i);
                        Assert.False(string.IsNullOrWhiteSpace(child.Name), language + " child " + i);
                        Assert.Equal(Localization.Translate(language, keys[i]), child.Name);
                        if (Localization.Codes[Array.IndexOf(Localization.Names, language)] is not ("ru" or "uk")) Assert.DoesNotMatch("[А-Яа-яЁё]", child.Name);
                    }
                    Assert.Equal(AccessibleRole.CheckButton, strip.GetChild(2).Role);
                    Assert.True((strip.GetChild(2).State & AccessibleStates.Checked) != 0, "the pinned row's pin reports itself checked");
                    Assert.Equal("first entry", strip.GetChild(2).Description);
                }
            }
            finally
            {
                service.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        });

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }
    }
}
