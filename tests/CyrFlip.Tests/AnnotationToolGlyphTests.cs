using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pictures on the annotation toolbar's tool buttons (S0071 6.4). A build cannot tell a drawn
    /// button from a blank one - the toolbar shipped with seven blank squares - so these paint each
    /// tool onto a transparent bitmap and read the pixels.
    /// </summary>
    public class AnnotationToolGlyphTests
    {
        private static readonly Color Ink = Color.FromArgb(20, 40, 200);
        private static readonly Size ButtonSize = new Size(28, 26);
        private const int Margin = 6;
        private const int SpillTolerance = 64;   // of 255: a quarter of a pixel - GDI+'s curve overshoot measured 40

        // Tools travel as names: AnnotationTool is internal, and a public theory method cannot take it.
        public static IEnumerable<object[]> ToolNames()
            => Enum.GetNames(typeof(AnnotationTool)).Select(name => new object[] { name });

        private static Bitmap Paint(AnnotationTool tool, Color ink)
        {
            var bitmap = new Bitmap(ButtonSize.Width, ButtonSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                Rectangle box = new Rectangle(Point.Empty, ButtonSize);
                box.Inflate(-Margin, -Margin);
                AnnotationToolGlyph.Draw(g, tool, box, ink);
            }
            return bitmap;
        }

        private static AnnotationTool Parse(string name) => (AnnotationTool)Enum.Parse(typeof(AnnotationTool), name);

        private static int[] Pixels(Bitmap bitmap)
        {
            var all = new int[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    all[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
            return all;
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void EveryToolDrawsSomething(string name)
        {
            using Bitmap bitmap = Paint(Parse(name), Ink);
            int drawn = Pixels(bitmap).Count(argb => Color.FromArgb(argb).A > 0);
            Assert.True(drawn >= 20, $"{name} painted {drawn} pixels - a blank button");
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void NothingIsDrawnOutsideItsBox(string name)
        {
            using Bitmap bitmap = Paint(Parse(name), Ink);
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    bool inside = x >= Margin && x < bitmap.Width - Margin && y >= Margin && y < bitmap.Height - Margin;
                    // A curve's anti-aliasing may leave a trace a fraction of a pixel wide beyond the
                    // box (GDI+ flattens a Bezier with a little overshoot); a visible pixel is a spill.
                    if (!inside)
                        Assert.True(bitmap.GetPixel(x, y).A <= SpillTolerance,
                            $"{name} touched {x},{y} outside its box (alpha {bitmap.GetPixel(x, y).A})");
                }
            }
        }

        [Fact]
        public void NoTwoToolsLookAlike()
        {
            var seen = new Dictionary<string, string>();
            foreach (string name in Enum.GetNames(typeof(AnnotationTool)))
            {
                using Bitmap bitmap = Paint(Parse(name), Ink);
                string picture = string.Join(",", Pixels(bitmap));
                if (seen.TryGetValue(picture, out string? twin))
                    Assert.Fail($"{name} and {twin} are drawn identically - the user could not tell them apart");
                seen[picture] = name;
            }
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void TheInkIsTheCallersAndFollowsIt(string name)
        {
            // The picture is drawn in the button's text colour, so it must take whatever colour it is given.
            var other = Color.FromArgb(10, 180, 60);
            using Bitmap blue = Paint(Parse(name), Ink);
            using Bitmap green = Paint(Parse(name), other);
            AssertSolidPixelsAre(blue, Ink, name);
            AssertSolidPixelsAre(green, other, name);
        }

        // A fill blended under its own outline rounds a channel by one; that is not another colour.
        private static void AssertSolidPixelsAre(Bitmap bitmap, Color ink, string name)
        {
            List<Color> solid = Pixels(bitmap).Select(Color.FromArgb).Where(c => c.A == 255).ToList();
            Assert.True(solid.Count > 0, $"{name} has no fully covered pixel");
            foreach (Color c in solid)
                Assert.True(Math.Abs(c.R - ink.R) <= 2 && Math.Abs(c.G - ink.G) <= 2 && Math.Abs(c.B - ink.B) <= 2,
                    $"{name} drew {c} where the ink is {ink}");
        }

        [Theory]
        [InlineData(nameof(AnnotationTool.Ellipse), nameof(AnnotationTool.FilledEllipse))]
        [InlineData(nameof(AnnotationTool.Rectangle), nameof(AnnotationTool.FilledRectangle))]
        public void AFilledToolIsTranslucentInsideAndItsOutlineToolIsEmpty(string outline, string filled)
        {
            // The same alpha as the real shapes, so the preview shows what will be drawn.
            var centre = new Point(ButtonSize.Width / 2, ButtonSize.Height / 2);
            using Bitmap hollow = Paint(Parse(outline), Ink);
            using Bitmap solid = Paint(Parse(filled), Ink);
            Assert.Equal(0, hollow.GetPixel(centre.X, centre.Y).A);
            int alpha = solid.GetPixel(centre.X, centre.Y).A;
            Assert.InRange(alpha, AnnotationShape.FillAlpha - 2, AnnotationShape.FillAlpha + 2);
        }
    }
}
