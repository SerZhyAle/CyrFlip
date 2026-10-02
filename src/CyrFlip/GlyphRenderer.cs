using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// Draws the vendored vocabulary glyphs (<see cref="Glyphs"/>) with GDI+ at draw time, in whatever
    /// colour the caller's theme says - nothing is baked (ICON-RENDER rule 2) and one path serves every
    /// size and DPI. An id that is not vendored, or a path that does not parse, draws nothing and says so
    /// by returning false: the control keeps its text caption, never an exception.
    /// </summary>
    internal static class GlyphRenderer
    {
        /// <summary>The contract's grid: a glyph is drawn on 24 x 24 units.</summary>
        public const float Grid = 24f;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, GlyphRecord> Records = Glyphs.All.ToDictionary(r => r.Id, StringComparer.Ordinal);
        // The parsed path per id, in grid units. GraphicsPath is not safe to share, so it is cloned per use.
        private static readonly Dictionary<string, GraphicsPath?> Parsed = new Dictionary<string, GraphicsPath?>(StringComparer.Ordinal);

        public static IEnumerable<string> Ids => Records.Keys;

        public static bool Has(string id) => Records.ContainsKey(id);

        /// <summary>A private copy of the glyph's path in grid units, or null when the id is unknown or unparsable.</summary>
        public static GraphicsPath? CreatePath(string id)
        {
            lock (Gate)
            {
                if (!Parsed.TryGetValue(id, out GraphicsPath? path))
                {
                    path = Records.TryGetValue(id, out GlyphRecord? record) ? GlyphPath.Parse(record.PathData, record.Transform, record.EvenOdd) : null;
                    Parsed[id] = path;
                }
                return path == null ? null : (GraphicsPath)path.Clone();
            }
        }

        /// <summary>
        /// Fills the glyph into <paramref name="box"/> (the 24 grid scaled to it) with one colour.
        /// <paramref name="rightToLeft"/> mirrors the glyphs whose record says <c>mirror</c> and no others.
        /// </summary>
        public static bool Draw(Graphics g, string id, RectangleF box, Color color, bool rightToLeft = false)
        {
            using GraphicsPath? path = CreatePath(id);
            if (path == null || box.Width <= 0 || box.Height <= 0) return false;
            bool mirror = rightToLeft && Records[id].Rtl == "mirror";
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(box.X, box.Y);
                g.ScaleTransform(box.Width / Grid, box.Height / Grid);
                if (mirror) { g.TranslateTransform(Grid, 0); g.ScaleTransform(-1, 1); }
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
            finally { g.Restore(state); }
            return true;
        }

        /// <summary>A square bitmap of the glyph, or null when it cannot be drawn.</summary>
        public static Bitmap? Render(string id, int size, Color color, bool rightToLeft = false)
        {
            if (!Has(id) || size <= 0) return null;
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                if (Draw(g, id, new RectangleF(0, 0, size, size), color, rightToLeft)) return bitmap;
            }
            bitmap.Dispose();
            return null;
        }

        /// <summary>
        /// The decorated look (ICON-RENDER section 10 B, E): the glyph in the on-plate colour, centred on a
        /// flat round plate, the 24 grid spanning 0.6 of the plate's side. Used where the surface brings no
        /// background of its own - a Jump List task, a shortcut.
        /// </summary>
        public static Bitmap? RenderOnPlate(string id, int size, Color plate, Color onPlate)
        {
            if (!Has(id) || size <= 0) return null;
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(plate)) g.FillEllipse(brush, 0, 0, size - 1f, size - 1f);
                float glyph = size * 0.6f, margin = (size - glyph) / 2f;
                if (Draw(g, id, new RectangleF(margin, margin, glyph, glyph), onPlate)) return bitmap;
            }
            bitmap.Dispose();
            return null;
        }

        /// <summary>
        /// The bounding box of the glyph's ink in grid units, measured on the flattened outline (the box
        /// of a curved path's control points is wider than its ink). Null when the glyph cannot be drawn.
        /// </summary>
        public static RectangleF? InkBounds(string id)
        {
            using GraphicsPath? path = CreatePath(id);
            if (path == null) return null;
            path.Flatten(null, 0.05f);
            return path.GetBounds();
        }
    }
}
