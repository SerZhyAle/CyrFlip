using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CyrFlip
{
    /// <summary>
    /// Shared look for the layout marker across all three surfaces (mouse I-beam cursor,
    /// caret overlay, tray icon): a per-layout bright colour drawn with a black outline so
    /// the letters stay legible on any background.
    ///
    /// EN = blue, RU = red, UK = green; every other layout gets a deterministic bright colour
    /// derived from its code (stable across renders, never dark).
    /// </summary>
    internal static class LayoutStyle
    {
        private static readonly Color En = ColorTranslator.FromHtml("#4DA3FF"); // blue
        private static readonly Color Ru = ColorTranslator.FromHtml("#FF5A5A"); // red
        private static readonly Color Uk = ColorTranslator.FromHtml("#5AD86A"); // green

        public static Color ColorFor(string code)
        {
            switch (code)
            {
                case "EN": return En;
                case "RU": return Ru;
                case "UK": return Uk;
                default: return BrightFromCode(code);
            }
        }

        // Deterministic, always-bright colour for any other world layout.
        private static Color BrightFromCode(string code)
        {
            int hash = 17;
            foreach (char c in code ?? string.Empty)
                hash = hash * 31 + c;
            double hue = (((hash % 360) + 360) % 360);
            return FromHsl(hue, 0.85, 0.66); // high saturation + lightness => bright on black
        }

        /// <summary>Draw <paramref name="code"/> centred in <paramref name="area"/> with a bright
        /// per-layout fill and a black outline.</summary>
        public static void DrawCode(Graphics g, string code, Font font, RectangleF area)
        {
            if (string.IsNullOrEmpty(code))
                return;

            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var path = new GraphicsPath();
            path.AddString(code, font.FontFamily, (int)font.Style, font.Size, area, sf);

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = new Pen(Color.Black, Math.Max(2f, font.Size * 0.16f)) { LineJoin = LineJoin.Round })
                g.DrawPath(outline, path);
            using (var fill = new SolidBrush(ColorFor(code)))
                g.FillPath(fill, path);
            g.SmoothingMode = prev;
        }

        /// <summary>
        /// Draw a 1px rounded frame in the layout colour just inside <paramref name="area"/> -
        /// the CapsLock-is-on indicator drawn around the marker on all three surfaces.
        /// </summary>
        public static void DrawCapsFrame(Graphics g, RectangleF area, float radius, string code)
        {
            // Inset by half a pixel so the 1px stroke lands fully inside the badge.
            var r = RectangleF.Inflate(area, -0.5f, -0.5f);
            if (r.Width <= 0 || r.Height <= 0)
                return;

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f)))
            using (var pen = new Pen(ColorFor(code), 1f))
                g.DrawPath(pen, path);
            g.SmoothingMode = prev;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0f)
            {
                path.AddRectangle(r);
                path.CloseFigure();
                return path;
            }
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color FromHsl(double h, double s, double l)
        {
            h /= 360.0;
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3);
            }
            return Color.FromArgb(255, Clamp(r), Clamp(g), Clamp(b));
        }

        private static int Clamp(double v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
