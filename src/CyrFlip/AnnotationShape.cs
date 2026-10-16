using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CyrFlip
{
    /// <summary>
    /// The toolbar's tools (S0071 6.3), in toolbar order. The eraser is a tool, not a shape kind:
    /// it removes objects instead of drawing them.
    /// </summary>
    internal enum AnnotationTool
    {
        Ellipse,
        FilledEllipse,
        Rectangle,
        FilledRectangle,
        Line,
        Arrow,
        Eraser,
    }

    /// <summary>The six drawable kinds of <see cref="AnnotationShape"/>.</summary>
    internal enum AnnotationKind
    {
        Ellipse,
        FilledEllipse,
        Rectangle,
        FilledRectangle,
        Line,
        Arrow,
    }

    /// <summary>
    /// One drawn object of the draw-on-capture editor (S0071 6.3): a kind, the two drag corners in
    /// virtual-screen physical pixels, the palette ink, and the commit order. Shapes are objects, not
    /// pixels - the windows paint frame + shapes every paint, and OK composes the list onto a copy of
    /// the frozen frame (<see cref="AnnotationComposer"/>), so nothing drawn ever exists in the frame
    /// and the eraser cannot touch it.
    ///
    /// <para>Pure and window-free: normalization happens at draw time (a drag in any direction yields
    /// the same shape - the <see cref="ScreenCapture.Normalize"/> precedent), so the whole model is
    /// testable without a desktop.</para>
    /// </summary>
    internal sealed class AnnotationShape
    {
        public AnnotationShape(AnnotationKind kind, Point p1, Point p2, Color ink, int index)
        {
            Kind = kind;
            P1 = p1;
            P2 = p2;
            Ink = ink;
            Index = index;
        }

        public AnnotationKind Kind { get; }
        /// <summary>The drag's start corner, virtual-screen physical pixels.</summary>
        public Point P1 { get; }
        /// <summary>The drag's end corner (for an arrow, the head end).</summary>
        public Point P2 { get; }
        public Color Ink { get; }
        /// <summary>The commit order: monotonic, the draw order; later ink wins where shapes overlap.</summary>
        public int Index { get; }

        /// <summary>
        /// Endpoints within the selection overlay's click size (2x2 px and below): a click, not a drag -
        /// refused at commit, mirroring the overlay.
        /// </summary>
        public static bool IsDegenerate(Point p1, Point p2)
            => Math.Abs(p1.X - p2.X) <= ScreenCapture.ClickSize && Math.Abs(p1.Y - p2.Y) <= ScreenCapture.ClickSize;

        /// <summary>The rectangle the shape covers, whichever corner the drag started from.</summary>
        public Rectangle Bounds
        {
            get
            {
                Rectangle bounds = ScreenCapture.Normalize(P1, P2);
                if (bounds.Width == 0) bounds.Width = 1;   // a shape is never less than a pixel thick
                if (bounds.Height == 0) bounds.Height = 1;
                return bounds;
            }
        }

        /// <summary>
        /// The eraser's reach: the bounds inflated by <paramref name="inflate"/>, so a one-pixel-wide
        /// line and a thin arrow are grabbable. The editor passes max(strokeWidth, 8).
        /// </summary>
        public bool HitTest(Point p, int inflate)
        {
            Rectangle bounds = Bounds;
            bounds.Inflate(Math.Max(0, inflate), Math.Max(0, inflate));
            return bounds.Contains(p);
        }

        /// <summary>
        /// Draw the shape onto <paramref name="g"/> in <see cref="Ink"/>. Outline kinds stroke opaque;
        /// filled kinds fill at alpha 128 under an opaque outline, so the screenshot under them stays
        /// readable - the point of boxing something on a screenshot. Coordinates are in the graphics'
        /// own space; the composer translates virtual-screen pixels to image pixels with a transform.
        /// </summary>
        public void Draw(Graphics g, int strokeWidth)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // One outline width for the whole editor (Q6): the frame is one bitmap.
            using var pen = new Pen(Ink, Math.Max(1, strokeWidth));
            pen.StartCap = LineCap.Flat;
            pen.EndCap = LineCap.Flat;

            switch (Kind)
            {
                case AnnotationKind.Ellipse:
                    g.DrawEllipse(pen, Bounds);
                    break;
                case AnnotationKind.FilledEllipse:
                    using (var ellipseFill = new SolidBrush(Color.FromArgb(128, Ink)))
                        g.FillEllipse(ellipseFill, Bounds);
                    g.DrawEllipse(pen, Bounds);
                    break;
                case AnnotationKind.Rectangle:
                    g.DrawRectangle(pen, Bounds);
                    break;
                case AnnotationKind.FilledRectangle:
                    using (var rectFill = new SolidBrush(Color.FromArgb(128, Ink)))
                        g.FillRectangle(rectFill, Bounds);
                    g.DrawRectangle(pen, Bounds);
                    break;
                case AnnotationKind.Line:
                    g.DrawLine(pen, P1, P2);
                    break;
                case AnnotationKind.Arrow:
                    g.DrawLine(pen, P1, P2);
                    DrawArrowHead(g, P1, P2, strokeWidth);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "not a drawable kind");
            }
        }

        /// <summary>
        /// A filled head at the drag's end point, ~4 stroke widths long - the arrow points where the
        /// drag ended, whatever its direction.
        /// </summary>
        private void DrawArrowHead(Graphics g, Point from, Point to, int strokeWidth)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            int head = Math.Max(4 * Math.Max(1, strokeWidth), 8);
            double wing = Math.PI / 7;   // ~26 degrees each side: a visible head without a barbed look
            var p2 = new PointF(to.X, to.Y);
            var left = new PointF(
                (float)(to.X - head * Math.Cos(angle - wing)),
                (float)(to.Y - head * Math.Sin(angle - wing)));
            var right = new PointF(
                (float)(to.X - head * Math.Cos(angle + wing)),
                (float)(to.Y - head * Math.Sin(angle + wing)));
            using var brush = new SolidBrush(Ink);
            g.FillPolygon(brush, new[] { p2, left, right });
        }
    }

    /// <summary>
    /// One undoable action of the editor (Q4, implemented by default): a committed shape, or the
    /// shapes one eraser drag (or one "clear") removed. The shape list itself stays unaware of it.
    /// </summary>
    internal abstract class AnnotationOp
    {
        private AnnotationOp() { }

        /// <summary>A shape that was committed; undo removes it.</summary>
        internal sealed class DrawOp : AnnotationOp
        {
            public DrawOp(AnnotationShape shape) { Shape = shape; }
            public AnnotationShape Shape { get; }
        }

        /// <summary>Shapes removed together, topmost first as encountered; undo puts them back.</summary>
        internal sealed class EraseOps : AnnotationOp
        {
            public EraseOps(IReadOnlyList<AnnotationShape> removed) { Removed = removed; }
            public IReadOnlyList<AnnotationShape> Removed { get; }
        }
    }
}
