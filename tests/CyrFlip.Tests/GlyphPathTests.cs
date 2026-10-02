using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The SVG path reader and the renderer behind the vendored vocabulary glyphs (ticket S0022 A1). The
    /// contract's own measured-style rule (ICON-RENDER section 10 A: ink from 1 to 23 on the 24 grid) is
    /// held here for every glyph the app vendors.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class GlyphPathTests
    {
        private static RectangleF Bounds(string data, float[]? transform = null)
        {
            using GraphicsPath? path = GlyphPath.Parse(data, transform);
            Assert.NotNull(path);
            path!.Flatten(null, 0.05f);
            return path.GetBounds();
        }

        [Fact]
        public void AbsoluteLinesHorizontalsVerticalsAndCloseMakeARectangle()
        {
            RectangleF b = Bounds("M2 3 H10 V7 L2 7 Z");
            Assert.Equal(2, b.Left, 3); Assert.Equal(3, b.Top, 3); Assert.Equal(8, b.Width, 3); Assert.Equal(4, b.Height, 3);
        }

        [Fact]
        public void RelativeCommandsAreOffsetsFromTheCurrentPoint()
        {
            RectangleF b = Bounds("m2 3 h8 v4 l-8 0 z");
            Assert.Equal(2, b.Left, 3); Assert.Equal(3, b.Top, 3); Assert.Equal(8, b.Width, 3); Assert.Equal(4, b.Height, 3);
        }

        [Fact]
        public void AMovetoFollowedByPairsIsImplicitLinetos()
        {
            RectangleF b = Bounds("M0 0 10 0 10 5 0 5z");
            Assert.Equal(10, b.Width, 3); Assert.Equal(5, b.Height, 3);
        }

        [Fact]
        public void CompactNumbersSplitOnSignAndSecondDot()
        {
            // "1.5.5" is 1.5 and .5; "-1-2" is -1 and -2.
            RectangleF b = Bounds("M1.5.5L-1-2z");
            Assert.Equal(-1, b.Left, 3); Assert.Equal(-2, b.Top, 3); Assert.Equal(2.5, b.Width, 3); Assert.Equal(2.5, b.Height, 3);
        }

        [Fact]
        public void CubicAndSmoothCubicReachTheirEndpoints()
        {
            RectangleF b = Bounds("M0 10 C0 0 10 0 10 10 S20 20 20 10");
            Assert.Equal(0, b.Left, 2); Assert.Equal(20, b.Right, 2);
            Assert.True(b.Bottom > 10 && b.Top < 10);
        }

        [Fact]
        public void QuadraticAndSmoothQuadraticAreDegreeElevated()
        {
            // The quadratic with its control at (5,0) peaks at y = 2.5 halfway along.
            RectangleF b = Bounds("M0 5 Q5 0 10 5");
            Assert.Equal(2.5, b.Top, 1); Assert.Equal(10, b.Width, 2);
            RectangleF t = Bounds("M0 5 Q5 0 10 5 T20 5");
            Assert.Equal(20, t.Width, 2);
        }

        [Fact]
        public void ASweepArcIsAHalfCircleThroughTheTop()
        {
            // From (0,12) to (24,12), radius 12, sweep flag 1: clockwise on screen, over the top.
            RectangleF b = Bounds("M0 12 A12 12 0 0 1 24 12");
            Assert.Equal(0, b.Left, 2); Assert.Equal(24, b.Right, 2);
            Assert.Equal(0, b.Top, 1); Assert.Equal(12, b.Bottom, 2);
        }

        [Fact]
        public void TheOtherSweepFlagGoesUnderneath()
        {
            RectangleF b = Bounds("M0 12 A12 12 0 0 0 24 12");
            Assert.Equal(12, b.Top, 2); Assert.Equal(24, b.Bottom, 1);
        }

        [Fact]
        public void ArcFlagsMayRunIntoTheNextNumber()
        {
            // "a1 1 0 00.5.5": large=0, sweep=0, then 0.5 and .5.
            using GraphicsPath? path = GlyphPath.Parse("M0 0a1 1 0 00.5.5z");
            Assert.NotNull(path);
        }

        [Fact]
        public void TooSmallARadiusIsScaledUpToReachTheEndpoint()
        {
            RectangleF b = Bounds("M0 0 A1 1 0 0 1 10 0");
            Assert.Equal(10, b.Width, 2);
        }

        [Fact]
        public void OverlappingSubpathsAreFilledByTheNonzeroRule()
        {
            // Two squares drawn the same way round that overlap. GDI+'s default (Alternate) would leave the
            // overlap unfilled; the glyphs are nonzero-filled, so it must be solid.
            using GraphicsPath? path = GlyphPath.Parse("M0 0H10V10H0Z M5 5H15V15H5Z");
            Assert.NotNull(path);
            Assert.Equal(FillMode.Winding, path!.FillMode);
            Assert.True(path.IsVisible(7, 7));
            Assert.True(path.IsVisible(2, 2));
            Assert.True(path.IsVisible(12, 12));
            Assert.False(path.IsVisible(12, 2));
        }

        [Fact]
        public void AnEvenOddRecordPunchesItsOverlapAndTheDefaultDoesNot()
        {
            // The one SVG rendering attribute the generator carries is fill-rule="evenodd": the same two
            // squares, drawn the other way, leave the overlap hollow.
            using GraphicsPath? path = GlyphPath.Parse("M0 0H10V10H0Z M5 5H15V15H5Z", null, evenOdd: true);
            Assert.NotNull(path);
            Assert.Equal(FillMode.Alternate, path!.FillMode);
            Assert.False(path.IsVisible(7, 7));
            Assert.True(path.IsVisible(2, 2));
            Assert.True(path.IsVisible(12, 12));
        }

        [Fact]
        public void TheShortcutsGlyphIsDrawnEvenOddAndItsKeyCapsAreHollow()
        {
            // app.shortcuts is the first vendored glyph whose SVG says fill-rule="evenodd".
            using GraphicsPath? path = GlyphRenderer.CreatePath("app.shortcuts");
            Assert.NotNull(path);
            Assert.Equal(FillMode.Alternate, path!.FillMode);
            Assert.True(path.IsVisible(3, 12));       // the left key cap's wall (x 2..4)
            Assert.False(path.IsVisible(5.5f, 12));   // inside it
            Assert.True(path.IsVisible(12, 11.6f));   // the plus between the caps
        }

        [Fact]
        public void ATransformIsAppliedToTheWholePath()
        {
            // translate(12 12) scale(0.5 0.5) translate(-12 -12) of a 24-square: a 12-square centred on (12,12).
            float[] matrix = { 0.5f, 0, 0, 0.5f, 6, 6 };
            RectangleF b = Bounds("M0 0H24V24H0Z", matrix);
            Assert.Equal(6, b.Left, 3); Assert.Equal(12, b.Width, 3);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("M")]
        [InlineData("M1")]
        [InlineData("M0 0 X5 5")]
        [InlineData("5 5 L1 1")]
        [InlineData("M0 0 L1 1 z 5 5")]
        [InlineData("M0 0 A1 1 0 2 0 5 5")]
        [InlineData("M0 0 L1e 1")]
        public void APathThatDoesNotParseGivesNothingAndNeverThrows(string data)
        {
            Assert.Null(GlyphPath.Parse(data));
        }

        // -- the vendored glyphs ------------------------------------------------------------------------

        [Fact]
        public void EveryVendoredGlyphParses()
        {
            foreach (string id in GlyphRenderer.Ids)
                using (GraphicsPath? path = GlyphRenderer.CreatePath(id))
                    Assert.True(path != null, id + " did not parse");
        }

        [Fact]
        public void EveryVendoredGlyphKeepsItsInkInsideOneToTwentyThree()
        {
            // ICON-RENDER section 10 A: one unit of margin on every side of the 24 grid (a sliver of
            // tolerance for the flattening).
            foreach (string id in GlyphRenderer.Ids)
            {
                RectangleF? ink = GlyphRenderer.InkBounds(id);
                Assert.True(ink.HasValue, id);
                Assert.True(ink!.Value.Left >= 0.95f && ink.Value.Top >= 0.95f && ink.Value.Right <= 23.05f && ink.Value.Bottom <= 23.05f,
                    id + " ink is " + ink.Value);
            }
        }

        [Theory]
        [InlineData(16)]
        [InlineData(20)]
        [InlineData(24)]
        [InlineData(32)]
        public void EveryVendoredGlyphDrawsInTheColourItIsGiven(int size)
        {
            var colour = Color.FromArgb(200, 30, 60);
            foreach (string id in GlyphRenderer.Ids)
            {
                using Bitmap? bitmap = GlyphRenderer.Render(id, size, colour);
                Assert.NotNull(bitmap);
                int inked = 0;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Color p = bitmap!.GetPixel(x, y);
                        if (p.A == 0) continue;
                        inked++;
                        // Below half coverage the 8-bit colour is too coarse to compare (A=8 rounds R by 9).
                        if (p.A < 128) continue;
                        // Nothing is baked: every inked pixel carries the requested colour, only alpha varies.
                        Assert.True(Math.Abs(p.R - colour.R) <= 1 && Math.Abs(p.G - colour.G) <= 1 && Math.Abs(p.B - colour.B) <= 1,
                            id + " at " + size + " drew " + p + " at " + x + "," + y);
                    }
                Assert.True(inked > size, id + " drew almost nothing at " + size);
            }
        }

        [Fact]
        public void AnUnknownGlyphDrawsNothing()
        {
            Assert.Null(GlyphRenderer.Render("no.such.glyph", 24, Color.Black));
            using var bitmap = new Bitmap(24, 24);
            using Graphics g = Graphics.FromImage(bitmap);
            Assert.False(GlyphRenderer.Draw(g, "no.such.glyph", new RectangleF(0, 0, 24, 24), Color.Black));
        }

        [Fact]
        public void OnlyAMirrorRecordIsMirroredInARightToLeftLayout()
        {
            var colour = Color.Black;
            using Bitmap? exitLtr = GlyphRenderer.Render("nav.exit", 24, colour);
            using Bitmap? exitRtl = GlyphRenderer.Render("nav.exit", 24, colour, rightToLeft: true);
            using Bitmap? gearLtr = GlyphRenderer.Render("app.settings", 24, colour);
            using Bitmap? gearRtl = GlyphRenderer.Render("app.settings", 24, colour, rightToLeft: true);
            Assert.False(SamePixels(exitLtr!, exitRtl!));   // nav.exit is rtl: mirror
            Assert.True(SamePixels(gearLtr!, gearRtl!));    // app.settings is rtl: fixed
        }

        [Fact]
        public void AGlyphOnAPlateIsSixTenthsOfThePlate()
        {
            using Bitmap? plated = GlyphRenderer.RenderOnPlate("action.download", 40, Color.Blue, Color.White);
            Assert.NotNull(plated);
            int left = 40, right = -1;
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                {
                    Color p = plated!.GetPixel(x, y);
                    if (p.A > 200 && p.R > 200 && p.G > 200 && p.B > 200) { left = Math.Min(left, x); right = Math.Max(right, x); }
                }
            // The glyph's own grid spans 24 px (0.6 of 40); its ink sits one unit inside that.
            Assert.InRange(right - left + 1, 16, 24);
            Assert.InRange(left, 8, 14);
        }

        private static bool SamePixels(Bitmap a, Bitmap b)
        {
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
            return true;
        }
    }

    /// <summary>The generated table and the list it is generated from (ticket S0022 A1).</summary>
    public class GlyphVendoringTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

        [Fact]
        public void TheGeneratedFileSaysItIsGenerated()
        {
            string text = File.ReadAllText(Path.Combine(Root, "src", "CyrFlip", "Glyphs.g.cs"));
            Assert.StartsWith("// <auto-generated>", text);
            Assert.Contains("Never edit by hand", text);
        }

        [Fact]
        public void EveryIdInTheListIsInTheTableAndNothingElseIs()
        {
            string[] listed = File.ReadAllLines(Path.Combine(Root, "tools", "IconGen", "glyph-ids.txt"))
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)).ToArray();
            Assert.Equal(listed.OrderBy(i => i, StringComparer.Ordinal), Glyphs.All.Select(g => g.Id).OrderBy(i => i, StringComparer.Ordinal));
        }

        [Fact]
        public void EveryIdTheAppNamesIsVendored()
        {
            // The source gate of ICON-SET rule 1: an id is spelled once, in AppGlyphs, and each of them is
            // a record in the table - a private picture cannot enter under a made-up name.
            var ids = typeof(AppGlyphs).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToArray();
            Assert.NotEmpty(ids);
            foreach (string id in ids) Assert.True(GlyphRenderer.Has(id), id + " is named in AppGlyphs but not vendored");
        }

        [Fact]
        public void NoCodeOutsideAppGlyphsSpellsAVocabularyIdToTheRenderer()
        {
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "src", "CyrFlip"), "*.cs"))
            {
                string name = Path.GetFileName(file);
                if (name == "AppGlyphs.cs" || name == "Glyphs.g.cs") continue;
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file),
                    @"GlyphRenderer\.(?:Draw|Render|RenderOnPlate|CreatePath)\([^)]*""[a-z]+\.[a-z0-9.-]+"""), name + " names a glyph id as a literal");
            }
        }
        [Fact]
        public void EveryRecordIsDeclaredWithAKnownStatusAndRtl()
        {
            foreach (GlyphRecord record in Glyphs.All)
            {
                Assert.Contains(record.Status, new[] { "active", "proposed" });
                Assert.Contains(record.Rtl, new[] { "fixed", "mirror" });
            }
        }
    }
}
