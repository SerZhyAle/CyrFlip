using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CyrFlip
{
    /// <summary>
    /// The picture on a tool button of the annotation toolbar (S0071 6.4): the shape the tool draws,
    /// as a small vector preview. The vocabulary has no glyph for an oval, a filled rectangle or an
    /// arrow (the <c>WelcomePainted</c> precedent: a meaning it lacks is drawn by the control, not
    /// borrowed from a neighbour), so each tool draws its own here. The ink is the caller's - the
    /// button's text colour, so the picture follows the theme and high contrast; a palette swatch as
    /// ink would vanish on the button whenever the swatch is white or black.
    ///
    /// <para>Pure drawing with no control behind it: the toolbar paints it, and a test paints it onto
    /// a bitmap and reads the pixels (a build cannot tell a blank button from a drawn one - they were
    /// blank). The stroke stays inside <c>box</c>, so a caller that leaves a margin around the box
    /// gets exactly that margin.</para>
    /// </summary>
    internal static class AnnotationToolGlyph
    {
        /// <summary>The preview's outline width, device pixels.</summary>
        public const float StrokeWidth = 1.6f;

        public static void Draw(Graphics g, AnnotationTool tool, Rectangle box, Color ink)
        {
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Half: an integer coordinate is a pixel's corner, so the box is exactly the pixels it names
            // (the default puts it on the pixel's centre and the right and bottom edges spill one pixel).
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                float half = StrokeWidth / 2f;
                var r = new RectangleF(box.X + half, box.Y + half, box.Width - StrokeWidth, box.Height - StrokeWidth);
                using var pen = new Pen(ink, StrokeWidth) { LineJoin = LineJoin.Round };
                // The same fill the real shapes get, so the preview shows what will be drawn.
                using var fill = new SolidBrush(AnnotationShape.FillColor(ink));

                switch (tool)
                {
                    case AnnotationTool.Ellipse:
                        g.DrawEllipse(pen, r);
                        break;
                    case AnnotationTool.FilledEllipse:
                        g.FillEllipse(fill, r);
                        g.DrawEllipse(pen, r);
                        break;
                    case AnnotationTool.Rectangle:
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        break;
                    case AnnotationTool.FilledRectangle:
                        g.FillRectangle(fill, r);
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        break;
                    case AnnotationTool.Line:
                        g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top);
                        break;
                    case AnnotationTool.Arrow:
                        DrawArrow(g, pen, ink, r);
                        break;
                    case AnnotationTool.Eraser:
                        DrawEraser(g, pen, fill, r);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(tool), tool, "not a tool");
                }
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.PixelOffsetMode = offset;
            }
        }

        /// <summary>A diagonal shaft from the bottom-left to a filled head at the top-right, the way the real arrow ends.</summary>
        private static void DrawArrow(Graphics g, Pen pen, Color ink, RectangleF r)
        {
            var tail = new PointF(r.Left, r.Bottom);
            var tip = new PointF(r.Right, r.Top);
            double angle = Math.Atan2(tip.Y - tail.Y, tip.X - tail.X);
            float head = Math.Max(5f, Math.Min(r.Width, r.Height) * 0.55f);
            double wing = Math.PI / 6;
            var left = new PointF(
                (float)(tip.X - head * Math.Cos(angle - wing)),
                (float)(tip.Y - head * Math.Sin(angle - wing)));
            var right = new PointF(
                (float)(tip.X - head * Math.Cos(angle + wing)),
                (float)(tip.Y - head * Math.Sin(angle + wing)));
            // The shaft stops inside the head, so its square end cannot poke out beside the tip.
            var neck = new PointF((left.X + right.X) / 2f, (left.Y + right.Y) / 2f);
            g.DrawLine(pen, tail, neck);
            using var brush = new SolidBrush(ink);
            g.FillPolygon(brush, new[] { tip, left, right });
        }

        /// <summary>An eraser block tilted up to the right: the rubber end filled, the sleeve outlined.</summary>
        private static void DrawEraser(Graphics g, Pen pen, Brush fill, RectangleF r)
        {
            PointF At(float x, float y) => new PointF(r.Left + r.Width * x, r.Top + r.Height * y);
            PointF a = At(0.04f, 0.66f);
            PointF b = At(0.46f, 0.04f);
            PointF c = At(0.96f, 0.34f);
            PointF d = At(0.54f, 0.96f);
            // The divider joins the middles of the two long edges.
            PointF m1 = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            PointF m2 = new PointF((d.X + c.X) / 2f, (d.Y + c.Y) / 2f);
            g.FillPolygon(fill, new[] { a, m1, m2, d });
            g.DrawPolygon(pen, new[] { a, b, c, d });
            g.DrawLine(pen, m1, m2);
        }
    }
}
