using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// The delivered image of a draw-on-capture (S0071 6.5), as a pure composition: a crop copy of
    /// the frozen frame (area mode) or a full-frame copy (full mode) with the shapes drawn onto it in
    /// commit order. The source frame is never mutated, and the input is frame + shape objects - the
    /// toolbar, the dimming and the region outline cannot enter the result <b>by construction</b>
    /// (nothing is re-grabbed from the screen), which is why the editor's windows need no
    /// <c>WDA_EXCLUDEFROMCAPTURE</c>. What lands in <see cref="CyrFlipContext.DeliverScreenshot"/> -
    /// and in the contract's saver - is only ever the final pixels.
    /// </summary>
    internal static class AnnotationComposer
    {
        /// <summary>
        /// Compose the delivery copy. <paramref name="shapes"/> is drawn in <see cref="AnnotationShape.Index"/>
        /// order, so later ink wins where two shapes overlap.
        /// </summary>
        public static Bitmap Compose(Bitmap frame, Rectangle virtualScreen, Rectangle? region,
            IReadOnlyList<AnnotationShape> shapes, int strokeWidth)
        {
            // Area mode delivers exactly the selected rectangle, full mode the whole virtual screen;
            // Crop is an exact row-by-row copy, so a zero-shape compose is pixel-equal to the plain
            // capture's crop.
            Rectangle target = region ?? virtualScreen;
            Bitmap copy = ScreenCapture.Crop(frame, virtualScreen, target);
            if (shapes.Count > 0)
            {
                using (Graphics g = Graphics.FromImage(copy))
                {
                    // Image pixel (0,0) is virtual-screen pixel target.Location.
                    g.TranslateTransform(-target.X, -target.Y);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    foreach (AnnotationShape shape in shapes.OrderBy(s => s.Index))
                        shape.Draw(g, strokeWidth);
                }
            }
            return copy;
        }
    }
}
