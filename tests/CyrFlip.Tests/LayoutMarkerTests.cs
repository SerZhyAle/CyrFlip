using System;
using System.Drawing;
using System.Drawing.Imaging;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// How the marker is drawn, as opposed to what colour it is (<see cref="LayoutColorsTests"/>).
    /// Both facts here are ones a build cannot check and a screenshot can only suggest: the letters
    /// fill their badge to within one pixel, and the badge is translucent so the text underneath it
    /// stays readable.
    /// </summary>
    public class LayoutMarkerTests
    {
        private const int Ink = 8; // alpha above which a pixel counts as drawn (antialiasing tails)

        private static Bitmap Render(string code, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Transparent);
                LayoutStyle.DrawCode(g, code, font, new RectangleF(0, 0, width, height));
            }
            return bmp;
        }

        private static bool AnyInk(Bitmap bmp, Func<int, int, bool> where)
        {
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (where(x, y) && bmp.GetPixel(x, y).A > Ink)
                        return true;
            return false;
        }

        /// <summary>The bounding box of everything drawn, or an empty rectangle when nothing was.</summary>
        private static Rectangle InkBounds(Bitmap bmp)
        {
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y).A > Ink)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
            return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        /// <summary>
        /// The badge keeps its size and the letters grow into it: the outermost pixel ring stays clear,
        /// and along the axis that limits the fit the letters fill essentially all of the rest. The old
        /// renderer typeset the code with <c>MeasureString</c>, which reserves room for ascenders,
        /// descenders and leading that two capitals never use - the letters ended up filling barely two
        /// thirds of a visibly roomier badge, which is what this asks about.
        /// </summary>
        [Theory]
        [InlineData("EN", 40, 20)]
        [InlineData("RU", 32, 32)]
        [InlineData("ZH", 24, 14)]
        [InlineData("0C0A", 48, 18)] // a language Windows cannot name: four characters, same rule
        public void TheLettersFillTheBadgeToWithinOnePixel(string code, int width, int height)
        {
            using Bitmap bmp = Render(code, width, height);

            Assert.False(AnyInk(bmp, (x, y) => x == 0 || y == 0 || x == width - 1 || y == height - 1),
                code + " touches the edge of its badge - there is no 1px border left around it");

            Rectangle ink = InkBounds(bmp);
            Assert.False(ink.IsEmpty, code + " drew nothing at all");

            // The letters keep their aspect ratio, so only one axis can be filled - the margin on that
            // axis is what "one pixel of border" means, and it is measured in pixels rather than as a
            // percentage precisely because a 14px badge and a 32px one must both leave that same one
            // pixel. Two is the allowance for the half-pixel antialiasing tail on each side; the
            // renderer this replaced left five or six here.
            double margin = Math.Min((width - ink.Width) / 2.0, (height - ink.Height) / 2.0);
            Assert.True(margin <= 2.0,
                code + " leaves " + margin + "px around the letters - they were not fitted to the badge");
        }

        /// <summary>A guard for the guard: a badge nothing was drawn into would satisfy the "no ink at
        /// the edge" half above just as quietly as a correctly fitted one.</summary>
        [Fact]
        public void TheFitTestWouldNoticeAnEmptyBadge()
        {
            using Bitmap bmp = Render("", 40, 20);
            Assert.False(AnyInk(bmp, (_, _) => true));
        }

        [Fact]
        public void TheMarkerIsDrawnTranslucentBesideTheMousePointer()
        {
            using Bitmap bmp = LayoutCursor.RenderCaret("EN", "00000409", 24, capsOn: false, out int hotX, out _);

            // The I-beam itself is opaque - a pointer you can see through is a worse cursor, not a
            // subtler one. Its vertical bar runs down the hotspot column.
            int beamAlpha = 0;
            for (int y = 0; y < bmp.Height; y++)
                beamAlpha = Math.Max(beamAlpha, bmp.GetPixel(Math.Min(hotX, bmp.Width - 1), y).A);
            Assert.Equal(255, beamAlpha);

            // The badge beside it is not: everything there is scaled by LayoutStyle.MarkerOpacity.
            int badgeAlpha = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = bmp.Width / 2; x < bmp.Width; x++)
                    badgeAlpha = Math.Max(badgeAlpha, bmp.GetPixel(x, y).A);

            Assert.True(badgeAlpha > 150, "nothing was drawn where the badge should be (alpha " + badgeAlpha + ")");
            Assert.True(badgeAlpha <= (int)Math.Ceiling(255 * LayoutStyle.MarkerOpacity),
                "the badge came out opaque (alpha " + badgeAlpha + ")");
        }

        [Fact]
        public void TheCaretOverlayWindowIsTranslucentToo()
        {
            using var overlay = new CaretOverlay(18);
            Assert.Equal(LayoutStyle.MarkerOpacity, overlay.WindowOpacity, 3);
        }
    }
}
