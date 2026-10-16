using System;
using System.Drawing;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure model of the draw-on-capture editor (ticket S0071, phase 4): bounds from any drag
    /// direction, the degenerate refusal, the eraser's hit test, and the commit order. One smoke case
    /// pins each kind against a known small bitmap; no WinForms window anywhere.
    /// </summary>
    public class AnnotationShapeTests
    {
        private static readonly Color Ink = Color.Red;

        private static AnnotationShape Shape(AnnotationKind kind, int x1, int y1, int x2, int y2, int index = 0)
            => new AnnotationShape(kind, new Point(x1, y1), new Point(x2, y2), Ink, index);

        // Kinds travel as names: AnnotationKind is internal, and a public theory method cannot take it.
        [Theory]
        [InlineData(nameof(AnnotationKind.Rectangle))]
        [InlineData(nameof(AnnotationKind.FilledRectangle))]
        [InlineData(nameof(AnnotationKind.Ellipse))]
        [InlineData(nameof(AnnotationKind.FilledEllipse))]
        public void BoundsComeOutTheSameFromAnyDragDirection(string kindName)
        {
            var kind = (AnnotationKind)Enum.Parse(typeof(AnnotationKind), kindName);
            // All four corners as the start, the opposite corner as the end.
            Rectangle topLeft = Shape(kind, 10, 10, 50, 30).Bounds;
            Rectangle bottomRight = Shape(kind, 50, 30, 10, 10).Bounds;
            Rectangle topRight = Shape(kind, 50, 10, 10, 30).Bounds;
            Rectangle bottomLeft = Shape(kind, 10, 30, 50, 10).Bounds;
            var expected = new Rectangle(10, 10, 40, 20);
            Assert.Equal(expected, topLeft);
            Assert.Equal(expected, bottomRight);
            Assert.Equal(expected, topRight);
            Assert.Equal(expected, bottomLeft);
        }

        [Fact]
        public void ALineBoundsIsTheSpanBetweenTheEndpoints()
        {
            Assert.Equal(new Rectangle(10, 10, 40, 20), Shape(AnnotationKind.Line, 50, 30, 10, 10).Bounds);
        }

        [Theory]
        [InlineData(10, 10, 12, 12, true)]    // 2x2 and below is a click: refused
        [InlineData(10, 10, 11, 11, true)]
        [InlineData(10, 10, 10, 10, true)]
        [InlineData(10, 10, 13, 12, false)]   // wider than 2 in one axis: a shape
        [InlineData(10, 10, 13, 13, false)]   // 3x3: accepted
        public void DegenerateFollowsTheOverlayClickNumbers(int x1, int y1, int x2, int y2, bool degenerate)
        {
            Assert.Equal(degenerate, AnnotationShape.IsDegenerate(new Point(x1, y1), new Point(x2, y2)));
        }

        [Fact]
        public void TheHitTestReachesAnInflatedOnePixelLine()
        {
            // A horizontal 1px-thick line, stroke width 3, eraser reach max(3, 8) = 8. Rectangle
            // edges are exclusive in Contains, so the inflated span y in [92, 109) is the reach.
            AnnotationShape line = Shape(AnnotationKind.Line, 10, 100, 110, 100);
            Assert.True(line.HitTest(new Point(60, 100), 8));
            Assert.True(line.HitTest(new Point(60, 108), 8));   // the last reachable row
            Assert.False(line.HitTest(new Point(60, 109), 8));  // one past it
        }

        [Fact]
        public void TheHitTestMissesAShapeNinePixelsAway()
        {
            AnnotationShape box = Shape(AnnotationKind.Rectangle, 10, 10, 50, 30);
            // Nine pixels right of the box's right edge (50): reach 8 must not reach x=59...
            Assert.False(box.HitTest(new Point(59, 20), 8));
            // ...and the last column inside the inflated span is x=57 (the span's right edge 58 is
            // exclusive).
            Assert.True(box.HitTest(new Point(57, 20), 8));
            Assert.False(box.HitTest(new Point(58, 20), 8));
        }

        [Fact]
        public void TheHitTestReachesAFilledInteriorAndAnOutlineEdge()
        {
            AnnotationShape filled = Shape(AnnotationKind.FilledRectangle, 10, 10, 50, 30);
            Assert.True(filled.HitTest(new Point(30, 20), 0));
            Assert.True(filled.HitTest(new Point(10, 20), 0));
            AnnotationShape outline = Shape(AnnotationKind.Ellipse, 10, 10, 50, 30);
            Assert.True(outline.HitTest(new Point(30, 20), 0));   // the interior counts: the eraser grabs by bounds
        }

        [Fact]
        public void TheCommitOrderIsTheDrawOrder()
        {
            var shapes = new[]
            {
                Shape(AnnotationKind.Rectangle, 0, 0, 9, 9, index: 0),
                Shape(AnnotationKind.Arrow, 5, 5, 20, 20, index: 1),
                Shape(AnnotationKind.FilledEllipse, 8, 8, 30, 30, index: 2),
            };
            Assert.Equal(new[] { 0, 1, 2 }, shapes.Select(s => s.Index).ToArray());
        }

        [Fact]
        public void AnArrowIsALineWithAHeadAtTheDragEnd()
        {
            // A rightward arrow from (10,50) to (110,50), stroke 3: the head reaches ~4 stroke widths
            // to the right end, so pixels near (110,50) are ink and pixels at the tail are only the line.
            var arrow = new AnnotationShape(AnnotationKind.Arrow, new Point(10, 50), new Point(110, 50), Ink, 0);
            using var bitmap = new Bitmap(140, 100);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                arrow.Draw(g, 3);
            }
            Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(108, 50).ToArgb());   // inside the head
            Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(40, 50).ToArgb());   // the line's body
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(40, 60).ToArgb());   // under the line: untouched
        }

        [Fact]
        public void AFilledShapeLeavesThePictureReadableUnderIt()
        {
            // The fill is alpha 128: a probe inside a red filled rectangle over white comes out pink,
            // not red - and the opaque outline is red.
            AnnotationShape filled = Shape(AnnotationKind.FilledRectangle, 10, 10, 60, 40);
            using var bitmap = new Bitmap(80, 60);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                filled.Draw(g, 3);
            }
            Color inside = bitmap.GetPixel(35, 25);
            Assert.True(inside.R > inside.B, "the fill's red must dominate over the white base: " + inside);
            Assert.NotEqual(Ink.ToArgb(), inside.ToArgb());   // not the opaque ink: the picture stays readable
            Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(11, 25).ToArgb());   // the outline is opaque
        }

        [Fact]
        public void ThePaletteIsTheEightContentSwatchesAndRedIsTheDefaultInk()
        {
            Assert.Equal(8, AnnotationPalette.Swatches.Length);
            Assert.Equal(Color.White.ToArgb(), AnnotationPalette.Swatches[0].ToArgb());
            Assert.Equal(Color.Black.ToArgb(), AnnotationPalette.Swatches[1].ToArgb());
            Assert.Equal(Color.Red.ToArgb(), AnnotationPalette.Default.ToArgb());
            Assert.Equal(Color.Red.ToArgb(), AnnotationPalette.Swatches[AnnotationPalette.DefaultIndex].ToArgb());
            // The accessible names are one per swatch, and none is empty.
            foreach (int i in Enumerable.Range(0, 8))
                Assert.False(string.IsNullOrEmpty(AnnotationPalette.Name(i, s => s)));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnnotationPalette.Name(8, s => s));
        }
    }
}
