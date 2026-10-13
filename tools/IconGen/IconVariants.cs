using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace IconGen
{
    /// <summary>
    /// The <c>icon-variants</c> verb (ticket S0058, phase P1, decision O3): renders the candidate app marks
    /// for the repositioned product. The direction is the owner's: the text caret stays the main shape
    /// (installed users know it from the tray, the taskbar and the Start menu), the EN/RU label goes (it
    /// names a single feature), and a quiet "more than one tool" cue is added that still reads at 16 px.
    ///
    /// Writes only into the output folder (by default the ticket's research folder): every size as a PNG,
    /// a trial .ico, a preview sheet per variant (light and dark taskbar, magnified small sizes, the 300 px
    /// tile) and one overview beside the current icon. The shipped assets are not touched until the owner
    /// picks a mark. Run: dotnet run --project tools/IconGen -- icon-variants [-OutDir &lt;dir&gt;]
    ///
    /// The sizes up to 32 px are hinted by hand on the pixel grid (every edge an integer, drawn with
    /// <see cref="PixelOffsetMode.Half"/> so an integer edge is a pixel edge); 48 px and up are scaled from
    /// proportions and rounded to whole pixels, so they stay crisp too.
    /// </summary>
    internal static class IconVariants
    {
        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 256 };
        private const int TileSize = 300;

        // What the icon sits on: the Windows 11 taskbar and title bar in the light and the dark theme.
        private static readonly Color LightBar = ColorTranslator.FromHtml("#f3f3f3");
        private static readonly Color DarkBar = ColorTranslator.FromHtml("#202020");
        private static readonly Color LightText = ColorTranslator.FromHtml("#1b1b1b");
        private static readonly Color DarkText = ColorTranslator.FromHtml("#e4e4e4");
        private static readonly Color SheetBg = ColorTranslator.FromHtml("#ffffff");

        // The quiet module colour: lighter than the tile, darker than Ink, so the cue is visible on the
        // dark tile without competing with the accent caret.
        private static readonly Color Module = ColorTranslator.FromHtml("#6b7b93");

        private sealed class Variant
        {
            public Variant(string id, string name, string idea, Func<int, Bitmap> draw)
            {
                Id = id;
                Name = name;
                Idea = idea;
                Draw = draw;
            }

            public string Id { get; }
            public string Name { get; }
            public string Idea { get; }
            public Func<int, Bitmap> Draw { get; }
        }

        private static readonly Variant[] Variants =
        {
            new Variant("A", "Caret + module grid", "the EN/RU label becomes a 2x2 grid of modules", DrawA),
            new Variant("B", "Docked module", "one corner of the tile is a separate module", DrawB),
            new Variant("C", "Caret among modules", "four modules around a centred caret", DrawC),
        };

        // The owner's pick of 2026-10-13 (S0058 P1): variant A. Every shipped asset is generated from
        // this one painter; `icon-variants` keeps rendering all three for comparison.
        internal static Bitmap DrawChosen(int s) => DrawA(s);

        public static int Run(string root, string[] args)
        {
            string outDir = Path.GetFullPath(ArgValue(args, "-OutDir")
                ?? Path.Combine(root, "PLAN", "S0058_product-requalification", "research", "icon"));
            Directory.CreateDirectory(outDir);

            foreach (Variant v in Variants)
            {
                foreach (int s in Sizes)
                    Program.SavePng(v.Draw(s), Path.Combine(outDir, $"variant-{v.Id}-{s}.png"));
                Program.SavePng(v.Draw(TileSize), Path.Combine(outDir, $"variant-{v.Id}-tile-{TileSize}.png"));
                Program.WriteIco(Path.Combine(outDir, $"variant-{v.Id}.ico"), Sizes, v.Draw);
                Program.SavePng(DrawSheet(v), Path.Combine(outDir, $"variant-{v.Id}-sheet.png"));
            }
            Program.SavePng(DrawOverview(), Path.Combine(outDir, "variants-overview.png"));

            Console.WriteLine("Icon variants written to: " + outDir);
            return 0;
        }

        // ------------------------------------------------------------ variant A

        // The current composition with the label replaced: the caret on the left, a 2x2 grid of modules
        // where EN/RU stood. One module is in the accent colour - the caret's own tool among the others.
        private static Bitmap DrawA(int s)
        {
            CaretSpec caret;
            int gx, gy, cell, gap;
            switch (s)
            {
                case 16: caret = new CaretSpec(2, 3, 6, 2, 2, 10); gx = 10; gy = 5; cell = 2; gap = 1; break;
                case 24: caret = new CaretSpec(3, 5, 8, 2, 2, 14); gx = 14; gy = 8; cell = 3; gap = 2; break;
                case 32: caret = new CaretSpec(4, 7, 11, 3, 3, 18); gx = 19; gy = 11; cell = 4; gap = 2; break;
                default:
                    caret = ScaleCaret(s, 0.29f, 0.5f, 0.5625f, 0.34f, 0.075f, 0.07f);
                    cell = R(s * 0.125f);
                    gap = R(s * 0.0625f);
                    gx = R(s * 0.57f);
                    gy = R(s * 0.5f - (2 * cell + gap) / 2f);
                    break;
            }

            var bmp = NewCanvas(s, out Graphics g);
            using (g)
            {
                using (var tile = Program.Rounded(new RectangleF(0, 0, s, s), TileRadius(s)))
                    FillTile(g, tile, s);
                DrawCaret(g, caret, Program.Accent);

                float radius = s >= 48 ? cell * 0.25f : 0f;
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        Color c = row == 0 && col == 0 ? Program.Accent : Module;
                        Box(g, gx + col * (cell + gap), gy + row * (cell + gap), cell, cell, c, radius);
                    }
            }
            return bmp;
        }

        // ------------------------------------------------------------ variant B

        // The tile's top-right corner is cut off and stands as a module of its own, separated by a gap:
        // the silhouette is still the rounded square, but it is visibly made of parts. The caret sits in
        // the remaining body.
        private static Bitmap DrawB(int s)
        {
            int nx, ny, mx, my;
            CaretSpec caret;
            switch (s)
            {
                case 16: nx = 10; ny = 6; mx = 11; my = 5; caret = new CaretSpec(3, 4, 6, 2, 2, 10); break;
                case 24: nx = 15; ny = 9; mx = 17; my = 7; caret = new CaretSpec(5, 6, 8, 2, 2, 14); break;
                case 32: nx = 20; ny = 12; mx = 22; my = 10; caret = new CaretSpec(6, 9, 11, 3, 3, 18); break;
                default:
                    nx = R(s * 0.625f);
                    ny = R(s * 0.375f);
                    int moduleGap = Math.Max(2, R(s * 0.0625f));
                    mx = nx + moduleGap;
                    my = ny - moduleGap;
                    // As close to the body's centroid (0.45, 0.55) as the cut allows the top serif to go.
                    caret = ScaleCaret(s, 0.38f, 0.58f, 0.5625f, 0.34f, 0.075f, 0.07f);
                    break;
            }

            float r = TileRadius(s);
            float rc = s >= 48 ? s * 0.04f : 0f;   // the small convex corners the cut creates
            float ri = s >= 48 ? s * 0.06f : 0f;   // the concave corner inside the cut

            var bmp = NewCanvas(s, out Graphics g);
            using (g)
            {
                var body = new List<PointF>();
                ArcPoints(body, r, r, r, 180, 90);                  // top-left
                ArcPoints(body, nx - rc, rc, rc, 270, 90);          // top edge turns down into the cut
                ArcPoints(body, nx + ri, ny - ri, ri, 180, -90);    // inside corner of the cut
                ArcPoints(body, s - rc, ny + rc, rc, 270, 90);      // the cut meets the right edge
                ArcPoints(body, s - r, s - r, r, 0, 90);            // bottom-right
                ArcPoints(body, r, s - r, r, 90, 90);               // bottom-left
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(body.ToArray());
                    FillTile(g, path, s);
                }

                // A square module of its own, aligned to the tile's top and right edges. Keeping the tile's
                // large outer radius on it made a quarter circle that read as a folded page corner.
                int side = s - mx;
                float moduleRadius = s >= 48 ? side * 0.28f : 0f;
                Box(g, mx, 0, side, my, Program.Accent, moduleRadius);

                DrawCaret(g, caret, Program.Accent);
            }
            return bmp;
        }

        // ------------------------------------------------------------ variant C

        // The caret in the middle of the tile with four small modules in its corners, the way tools sit
        // around the place where you type. One module is in the accent colour.
        private static Bitmap DrawC(int s)
        {
            CaretSpec caret;
            int dot, inset;
            switch (s)
            {
                case 16: caret = new CaretSpec(5, 3, 6, 2, 2, 10); dot = 2; inset = 2; break;
                case 24: caret = new CaretSpec(8, 5, 8, 2, 2, 14); dot = 3; inset = 3; break;
                case 32: caret = new CaretSpec(10, 7, 12, 4, 3, 18); dot = 4; inset = 4; break;
                default:
                    caret = ScaleCaret(s, 0.5f, 0.5f, 0.5625f, 0.34f, 0.075f, 0.07f);
                    dot = R(s * 0.125f);
                    inset = R(s * 0.125f);
                    break;
            }

            var bmp = NewCanvas(s, out Graphics g);
            using (g)
            {
                using (var tile = Program.Rounded(new RectangleF(0, 0, s, s), TileRadius(s)))
                    FillTile(g, tile, s);
                DrawCaret(g, caret, Program.Accent);

                float radius = s >= 48 ? dot * 0.25f : 0f;
                int far = s - inset - dot;
                Box(g, inset, inset, dot, dot, Module, radius);
                Box(g, far, inset, dot, dot, Program.Accent, radius);
                Box(g, inset, far, dot, dot, Module, radius);
                Box(g, far, far, dot, dot, Module, radius);
            }
            return bmp;
        }

        // --------------------------------------------------------------- sheets

        // One sheet per variant: every size at 1:1 on the light and on the dark taskbar colour, the two
        // tray sizes magnified with nearest-neighbour scaling so each pixel can be judged, and the 300 px tile.
        private static Bitmap DrawSheet(Variant v)
        {
            const int width = 1320, header = 78, panel = 380;
            var bmp = new Bitmap(width, header + panel * 2, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(SheetBg);

            using (var title = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sub = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(LightText))
            {
                g.DrawString($"Variant {v.Id} - {v.Name}", title, ink, 24, 12);
                g.DrawString(v.Idea + ".  S0058 P1 candidate, not shipped.", sub, ink, 26, 46);
            }

            DrawPanel(g, v, new Rectangle(0, header, width, panel), LightBar, LightText, "Light taskbar / title bar (#f3f3f3)");
            DrawPanel(g, v, new Rectangle(0, header + panel, width, panel), DarkBar, DarkText, "Dark taskbar / title bar (#202020)");
            return bmp;
        }

        private static void DrawPanel(Graphics g, Variant v, Rectangle area, Color bg, Color fg, string caption)
        {
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, area);
            using var font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var text = new SolidBrush(fg);
            g.DrawString(caption, font, text, area.X + 24, area.Y + 12);

            int baseline = area.Y + 330;
            int x = area.X + 24;
            foreach (int s in Sizes)
            {
                using (var icon = v.Draw(s)) g.DrawImageUnscaled(icon, x, baseline - s);
                Label(g, s.ToString(), font, text, x, s, baseline);
                x += s + 28;
            }
            foreach ((int s, int zoom) in new[] { (16, 8), (24, 6) })
            {
                using (var icon = v.Draw(s)) Magnify(g, icon, x, baseline - s * zoom, zoom);
                Label(g, $"{s} px x{zoom}", font, text, x, s * zoom, baseline);
                x += s * zoom + 28;
            }
            using (var tile = v.Draw(TileSize)) g.DrawImageUnscaled(tile, area.Right - 24 - TileSize, baseline - TileSize);
        }

        // Every variant beside the current icon, at the tray, taskbar and title-bar sizes, on both themes.
        private static Bitmap DrawOverview()
        {
            const int labelW = 270, block = 380, rowH = 132, header = 64;
            int[] sizes = { 16, 24, 32, 48 };
            var rows = new List<(string name, string idea, Func<int, Bitmap> draw)>
            {
                ("Current", "caret + EN/RU label (assets/cyrflip.ico)", Program.DrawIcon),
            };
            foreach (Variant v in Variants) rows.Add(($"{v.Id} - {v.Name}", v.Idea, v.Draw));

            int width = labelW + block * 2 + 24;
            var bmp = new Bitmap(width, header + rowH * rows.Count, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(SheetBg);

            using var title = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var bold = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(LightText);
            g.DrawString("CyrFlip app mark - S0058 candidates (16 / 24 / 32 / 48 px, then 16 px x6)", title, ink, 20, 16);

            for (int i = 0; i < rows.Count; i++)
            {
                int top = header + i * rowH;
                var (name, idea, draw) = rows[i];
                g.DrawString(name, bold, ink, 20, top + 34);
                g.DrawString(idea, small, ink, new RectangleF(20, top + 58, labelW - 30, 60));

                for (int theme = 0; theme < 2; theme++)
                {
                    var area = new Rectangle(labelW + theme * (block + 12), top + 6, block, rowH - 12);
                    using (var b = new SolidBrush(theme == 0 ? LightBar : DarkBar)) g.FillRectangle(b, area);
                    int baseline = area.Bottom - 12;
                    int x = area.X + 18;
                    foreach (int s in sizes)
                    {
                        using (var icon = draw(s)) g.DrawImageUnscaled(icon, x, baseline - s);
                        x += s + 22;
                    }
                    using (var icon = draw(16)) Magnify(g, icon, x, baseline - 96, 6);
                }
            }
            return bmp;
        }

        private static void Label(Graphics g, string text, Font font, Brush brush, int x, int w, int baseline)
        {
            SizeF size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, x + (w - size.Width) / 2f, baseline + 8);
        }

        private static void Magnify(Graphics g, Bitmap icon, int x, int y, int zoom)
        {
            InterpolationMode mode = g.InterpolationMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(icon, new Rectangle(x, y, icon.Width * zoom, icon.Height * zoom));
            g.InterpolationMode = mode;
            g.PixelOffsetMode = offset;
        }

        // ----------------------------------------------------------- primitives

        /// <summary>An I-beam caret on whole pixels: the serif box (left, top, serif width, height) and the bar centred in it.</summary>
        private readonly struct CaretSpec
        {
            public CaretSpec(int left, int top, int serifW, int barW, int serifH, int height)
            {
                Left = left;
                Top = top;
                SerifW = serifW;
                BarW = barW;
                SerifH = serifH;
                Height = height;
            }

            public int Left { get; }
            public int Top { get; }
            public int SerifW { get; }
            public int BarW { get; }
            public int SerifH { get; }
            public int Height { get; }
        }

        /// <summary>A caret scaled from proportions and snapped: the bar and the serifs share a centre exactly.</summary>
        private static CaretSpec ScaleCaret(int s, float centerX, float centerY, float height, float serifW, float barW, float serifH)
        {
            int bw = Math.Max(2, R(s * barW));
            int sw = R(s * serifW);
            if ((sw - bw) % 2 != 0) sw++;
            int sh = Math.Max(2, R(s * serifH));
            int h = R(s * height);
            return new CaretSpec(R(s * centerX - sw / 2f), R(s * centerY - h / 2f), sw, bw, sh, h);
        }

        private static void DrawCaret(Graphics g, CaretSpec c, Color color)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, c.Left + (c.SerifW - c.BarW) / 2, c.Top, c.BarW, c.Height);
            g.FillRectangle(brush, c.Left, c.Top, c.SerifW, c.SerifH);
            g.FillRectangle(brush, c.Left, c.Top + c.Height - c.SerifH, c.SerifW, c.SerifH);
        }

        private static void Box(Graphics g, float x, float y, float w, float h, Color color, float radius)
        {
            using var brush = new SolidBrush(color);
            if (radius <= 0f)
            {
                g.FillRectangle(brush, x, y, w, h);
                return;
            }
            using var path = Program.Rounded(new RectangleF(x, y, w, h), radius);
            g.FillPath(brush, path);
        }

        // The tile gradient of the current icon (Bg1 to Bg2, top to bottom), flipped at the edges so the
        // first and last rows never pick up the opposite end of the gradient.
        private static void FillTile(Graphics g, GraphicsPath path, int s)
        {
            using var brush = new LinearGradientBrush(new RectangleF(0, 0, s, s), Program.Bg1, Program.Bg2, LinearGradientMode.Vertical)
            {
                WrapMode = WrapMode.TileFlipXY,
            };
            g.FillPath(brush, path);
        }

        // The current icon's corner radius, held to a whole 3 px at 16 so the corners stay round, not blurred.
        private static float TileRadius(int s) => s <= 16 ? 3f : s * 0.20f;

        private static Bitmap NewCanvas(int s, out Graphics g)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;   // integer coordinates are pixel edges
            g.Clear(Color.Transparent);
            return bmp;
        }

        /// <summary>Appends a circular arc (angles in degrees, clockwise on screen); radius 0 is the corner point itself.</summary>
        private static void ArcPoints(List<PointF> points, float cx, float cy, float radius, float startDeg, float sweepDeg)
        {
            if (radius <= 0f)
            {
                points.Add(new PointF(cx, cy));
                return;
            }
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepDeg) / 5f));
            for (int i = 0; i <= steps; i++)
            {
                double a = (startDeg + sweepDeg * i / steps) * Math.PI / 180.0;
                points.Add(new PointF(cx + radius * (float)Math.Cos(a), cy + radius * (float)Math.Sin(a)));
            }
        }

        private static int R(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        private static string? ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
