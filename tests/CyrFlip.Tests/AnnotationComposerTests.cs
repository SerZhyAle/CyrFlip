using System;
using System.Drawing;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure composition of the draw-on-capture editor (ticket S0071, phase 4): the size is the
    /// region's (area) or the virtual screen's (full), a zero-shape compose is pixel-equal to the
    /// plain crop, drawing changes exactly the shape's pixels, and the frame is never mutated.
    /// </summary>
    public class AnnotationComposerTests
    {
        private static readonly Rectangle Virtual = new Rectangle(0, 0, 200, 100);
        private static readonly Color Ink = Color.FromArgb(255, 255, 0, 0);

        private static Bitmap Frame()
        {
            var frame = new Bitmap(Virtual.Width, Virtual.Height);
            using Graphics g = Graphics.FromImage(frame);
            g.Clear(Color.White);
            return frame;
        }

        [Fact]
        public void AnAreaComposeIsExactlyTheRegionsSize()
        {
            using Bitmap frame = Frame();
            var region = new Rectangle(20, 10, 90, 50);
            using Bitmap composed = AnnotationComposer.Compose(frame, Virtual, region, new AnnotationShape[0], 3);
            Assert.Equal(new Size(90, 50), composed.Size);
        }

        [Fact]
        public void AFullComposeIsExactlyTheVirtualScreensSize()
        {
            using Bitmap frame = Frame();
            using Bitmap composed = AnnotationComposer.Compose(frame, Virtual, null, new AnnotationShape[0], 3);
            Assert.Equal(new Size(Virtual.Width, Virtual.Height), composed.Size);
        }

        [Fact]
        public void AZeroShapeComposeIsPixelEqualToThePlainCrop()
        {
            using Bitmap frame = Frame();
            var region = new Rectangle(20, 10, 90, 50);
            using Bitmap viaComposer = AnnotationComposer.Compose(frame, Virtual, region, new AnnotationShape[0], 3);
            using Bitmap plain = ScreenCapture.Crop(frame, Virtual, region);
            Assert.Equal(plain.Size, viaComposer.Size);
            for (int y = 0; y < plain.Height; y += 7)
                for (int x = 0; x < plain.Width; x += 7)
                    Assert.Equal(plain.GetPixel(x, y).ToArgb(), viaComposer.GetPixel(x, y).ToArgb());
        }

        [Fact]
        public void AShapeChangesExactlyThePixelsInsideIt()
        {
            using Bitmap frame = Frame();
            var region = new Rectangle(0, 0, 200, 100);
            var shape = new AnnotationShape(AnnotationKind.FilledRectangle,
                new Point(30, 20), new Point(80, 60), Ink, 0);
            using Bitmap composed = AnnotationComposer.Compose(frame, Virtual, region, new[] { shape }, 3);

            // Inside: the fill's alpha-128 red over white - red dominates, and it is not the base white.
            Color inside = composed.GetPixel(50, 40);
            Assert.True(inside.R > inside.B && inside.R > inside.G, "the shape's ink must dominate inside: " + inside);
            Assert.Equal(Color.White.ToArgb(), composed.GetPixel(150, 40).ToArgb()); // outside: untouched
            Assert.Equal(Color.White.ToArgb(), composed.GetPixel(10, 10).ToArgb());  // outside corner: untouched
        }

        [Fact]
        public void TheFrameIsNeverMutatedByCompose()
        {
            using Bitmap frame = Frame();
            var shape = new AnnotationShape(AnnotationKind.FilledRectangle,
                new Point(30, 20), new Point(80, 60), Ink, 0);
            AnnotationComposer.Compose(frame, Virtual, null, new[] { shape }, 3).Dispose();

            Assert.Equal(Color.White.ToArgb(), frame.GetPixel(50, 40).ToArgb());
            Assert.Equal(Color.White.ToArgb(), frame.GetPixel(31, 21).ToArgb());
        }

        [Fact]
        public void TheLaterShapesInkWinsWhereTwoOverlap()
        {
            // Opaque ink decides overlaps: two lines crossing at (80, 80), the later red wins there.
            using Bitmap frame = Frame();
            var first = new AnnotationShape(AnnotationKind.Line,
                new Point(80, 10), new Point(80, 150), Color.FromArgb(255, 0, 0, 255), index: 0);
            var second = new AnnotationShape(AnnotationKind.Line,
                new Point(10, 80), new Point(150, 80), Ink, index: 1);
            using Bitmap composed = AnnotationComposer.Compose(frame, Virtual, null, new[] { first, second }, 3);

            Assert.Equal(Ink.ToArgb(), composed.GetPixel(80, 80).ToArgb());
            // Each keeps its own ink away from the crossing.
            Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), composed.GetPixel(80, 30).ToArgb());
            Assert.Equal(Ink.ToArgb(), composed.GetPixel(30, 80).ToArgb());
        }

        [Fact]
        public void AnAreaComposeCoordinatesAreRelativeToTheRegion()
        {
            // A shape at the region's top-left corner paints into the copy's own top-left: virtual
            // pixels are translated by the region's origin.
            using Bitmap frame = Frame();
            var region = new Rectangle(100, 50, 60, 40);
            var shape = new AnnotationShape(AnnotationKind.FilledRectangle,
                new Point(102, 52), new Point(120, 70), Ink, 0);
            using Bitmap composed = AnnotationComposer.Compose(frame, Virtual, region, new[] { shape }, 3);

            // Local (5,5) is inside the translated fill: the ink dominates there and nowhere else.
            Color inside = composed.GetPixel(5, 5);
            Assert.True(inside.R > inside.B, "the shape must land at the region's own top-left: " + inside);
            Assert.Equal(Color.White.ToArgb(), composed.GetPixel(50, 30).ToArgb());
        }
    }
}
