using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CyrFlip
{
    /// <summary>
    /// Shared look for the layout marker across all three surfaces (mouse I-beam cursor,
    /// caret overlay, tray icon): a per-layout bright colour drawn with a black outline so
    /// the letters stay legible on any background.
    ///
    /// The curated world languages get intentionally distinct colours; every other layout gets a deterministic bright colour
    /// derived from its code (stable across renders, never dark).
    /// </summary>
    internal static class LayoutStyle
    {
        /// <summary>
        /// The curated palette, and <b>the source of truth for it</b>. The app ships as one exe with
        /// nothing to read at runtime, so the table has to live in code; the machine-readable copy for
        /// everything outside the exe (the VS Code extension) is
        /// <c>vscode-extension/src/layout-colors.json</c>, and <c>LayoutColorsTests</c> fails the build
        /// when the two disagree. <c>tools/IconGen</c> needs no copy at all - it compiles this very file.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Color> Curated = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "EN", ColorTranslator.FromHtml("#4DA3FF") }, // blue
            { "RU", ColorTranslator.FromHtml("#FF5A5A") }, // red
            { "UK", ColorTranslator.FromHtml("#5AD86A") }, // green
            { "ZH", ColorTranslator.FromHtml("#F3B33D") },
            { "HI", ColorTranslator.FromHtml("#E68742") },
            { "ES", ColorTranslator.FromHtml("#F06E9C") },
            { "FR", ColorTranslator.FromHtml("#8D7CF2") },
            { "AR", ColorTranslator.FromHtml("#35C6B4") },
            { "BN", ColorTranslator.FromHtml("#C67CDA") },
            { "PT", ColorTranslator.FromHtml("#46B978") },
            { "UR", ColorTranslator.FromHtml("#E5A3C7") },
            { "DE", ColorTranslator.FromHtml("#D8D14A") },
            { "IT", ColorTranslator.FromHtml("#7FB2E5") },
        };

        // The fallback's constants, named because the TypeScript side has to reproduce them exactly.
        internal const int HashSeed = 17;
        internal const int HashMultiplier = 31;
        internal const double FallbackSaturation = 0.85;
        internal const double FallbackLightness = 0.66;

        public static Color ColorFor(string code)
            => Curated.TryGetValue(code ?? string.Empty, out Color known) ? known : BrightFromCode(code);

        // Deterministic, always-bright colour for any other world layout.
        internal static Color BrightFromCode(string? code)
        {
            int hash = HashSeed;
            foreach (char c in code ?? string.Empty)
                hash = hash * HashMultiplier + c;
            double hue = (((hash % 360) + 360) % 360);
            // high saturation + lightness => bright on black
            return FromHsl(hue, FallbackSaturation, FallbackLightness);
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
