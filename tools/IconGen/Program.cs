using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace IconGen
{
    /// <summary>
    /// Generates CyrFlip's visual assets with GDI+: the app icon (a text caret next to a
    /// two-row EN / RU layout label), a wide page banner, a GitHub social-preview image,
    /// and favicons. Run: dotnet run --project tools/IconGen
    /// Outputs into assets/ (repo) and docs/assets/ (GitHub Pages).
    /// </summary>
    internal static class Program
    {
        // Palette (matches docs/style.css).
        private static readonly Color Bg1 = ColorTranslator.FromHtml("#11161f");
        private static readonly Color Bg2 = ColorTranslator.FromHtml("#1c2636");
        private static readonly Color Accent = ColorTranslator.FromHtml("#4493f8");
        private static readonly Color Ink = ColorTranslator.FromHtml("#e6edf3");
        private static readonly Color Muted = ColorTranslator.FromHtml("#8b98a8");

        private static int Main()
        {
            string root = FindRepoRoot();
            string assets = Path.Combine(root, "assets");
            string docsAssets = Path.Combine(root, "docs", "assets");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(docsAssets);

            // --- App icon (multi-size .ico with PNG payloads) ---
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            WriteIco(Path.Combine(assets, "cyrflip.ico"), sizes, DrawIcon);

            // --- PNG renditions of the icon ---
            SavePng(DrawIcon(256), Path.Combine(assets, "icon-256.png"));
            SavePng(DrawIcon(256), Path.Combine(docsAssets, "icon-256.png"));
            SavePng(DrawIcon(64), Path.Combine(docsAssets, "favicon.png"));
            WriteIco(Path.Combine(docsAssets, "favicon.ico"), new[] { 16, 32, 48 }, DrawIcon);

            // --- Banners ---
            SavePng(DrawBanner(1280, 360), Path.Combine(assets, "banner.png"));
            SavePng(DrawBanner(1280, 360), Path.Combine(docsAssets, "banner.png"));
            SavePng(DrawBanner(1280, 640), Path.Combine(assets, "social-preview.png"));

            // --- Cursor preview (illustrative; the live cursor is LayoutCursor) ---
            SavePng(DrawCursorPreview(560, 200), Path.Combine(assets, "cursor-preview.png"));
            SavePng(DrawCursorPreview(560, 200), Path.Combine(docsAssets, "cursor-preview.png"));

            Console.WriteLine("Assets written to:");
            Console.WriteLine("  " + assets);
            Console.WriteLine("  " + docsAssets);
            return 0;
        }

        // ----------------------------------------------------------------- icon

        private static Bitmap DrawIcon(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            // Rounded background tile with a vertical gradient.
            float radius = s * 0.20f;
            var tile = new RectangleF(0, 0, s, s);
            using (var path = Rounded(tile, radius))
            using (var brush = new LinearGradientBrush(tile, Bg1, Bg2, LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }

            // Text caret (I-beam) on the left.
            float beamCx = s * 0.30f;
            float beamH = s * 0.50f;
            float beamTop = (s - beamH) / 2f;
            float barW = Math.Max(1.5f, s * 0.05f);
            float serifW = s * 0.15f;
            float serifH = Math.Max(1.5f, s * 0.05f);
            using (var caret = new SolidBrush(Accent))
            {
                // vertical bar
                g.FillRectangle(caret, beamCx - barW / 2f, beamTop, barW, beamH);
                // top + bottom serifs
                g.FillRectangle(caret, beamCx - serifW / 2f, beamTop, serifW, serifH);
                g.FillRectangle(caret, beamCx - serifW / 2f, beamTop + beamH - serifH, serifW, serifH);
            }

            // Two-row layout label: EN (active) over RU (muted), to the right of the caret.
            float labelLeft = s * 0.42f;
            float labelW = s - labelLeft;
            using var font = new Font("Segoe UI", s * 0.235f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            var topRow = new RectangleF(labelLeft, s * 0.10f, labelW, s * 0.40f);
            var botRow = new RectangleF(labelLeft, s * 0.50f, labelW, s * 0.40f);
            using (var inkBrush = new SolidBrush(Ink))
            using (var mutedBrush = new SolidBrush(Muted))
            {
                g.DrawString("EN", font, inkBrush, topRow, center);
                g.DrawString("RU", font, mutedBrush, botRow, center);
            }

            return bmp;
        }

        // --------------------------------------------------------------- banner

        private static Bitmap DrawBanner(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Background gradient.
            var rect = new Rectangle(0, 0, w, h);
            using (var bg = new LinearGradientBrush(rect, Bg1, ColorTranslator.FromHtml("#14233f"), LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(bg, rect);
            }

            // Icon at the left.
            int iconSize = (int)(h * 0.52f);
            int iconX = (int)(w * 0.07f);
            int iconY = (h - iconSize) / 2;
            using (var icon = DrawIcon(iconSize))
            {
                g.DrawImage(icon, iconX, iconY, iconSize, iconSize);
            }

            float textLeft = iconX + iconSize + w * 0.045f;

            // Wordmark: "Cyr" in ink, "Flip" in accent.
            using var wordFont = new Font("Segoe UI", h * 0.20f, FontStyle.Bold, GraphicsUnit.Pixel);
            float wordY = h * 0.27f;
            using (var inkBrush = new SolidBrush(Ink))
            using (var accentBrush = new SolidBrush(Accent))
            {
                string a = "Cyr", b = "Flip";
                g.DrawString(a, wordFont, inkBrush, textLeft, wordY);
                float aw = g.MeasureString(a, wordFont).Width;
                g.DrawString(b, wordFont, accentBrush, textLeft + aw - h * 0.03f, wordY);
            }

            // Tagline — auto-fit to the available width so it never clips.
            const string tagline = "Fix text typed on the wrong keyboard layout — instantly.";
            float maxTagW = w - textLeft - w * 0.05f;
            using var tagFont = FitFont(g, "Segoe UI", FontStyle.Regular, h * 0.072f, tagline, maxTagW);
            using (var mutedBrush = new SolidBrush(Muted))
            {
                g.DrawString(tagline, tagFont, mutedBrush, textLeft, h * 0.62f);
            }

            // Demo chip on the right (only on the wider/taller social image).
            if (w >= 1280 && h >= 600)
            {
                DrawDemo(g, w, h);
            }

            return bmp;
        }

        private static void DrawDemo(Graphics g, int w, int h)
        {
            using var monoFont = new Font("Consolas", h * 0.05f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var inkBrush = new SolidBrush(Ink);
            using var accentBrush = new SolidBrush(Accent);
            using var chipBrush = new SolidBrush(ColorTranslator.FromHtml("#1f2630"));

            string from = "ghbdtn", arrow = "→", to = "привет";
            float y = h * 0.80f;
            float x = w * 0.62f;
            using var center = new StringFormat { LineAlignment = StringAlignment.Center };

            void Chip(string text, Brush textBrush, ref float cx)
            {
                SizeF sz = g.MeasureString(text, monoFont);
                float padX = h * 0.022f, padY = h * 0.012f;
                var chip = new RectangleF(cx, y - sz.Height / 2 - padY, sz.Width + padX * 2, sz.Height + padY * 2);
                using (var p = Rounded(chip, h * 0.02f)) g.FillPath(chipBrush, p);
                g.DrawString(text, monoFont, textBrush, cx + padX, y, center);
                cx += chip.Width + h * 0.02f;
            }

            Chip(from, inkBrush, ref x);
            g.DrawString(arrow, monoFont, accentBrush, x, y, center);
            x += g.MeasureString(arrow, monoFont).Width + h * 0.02f;
            Chip(to, accentBrush, ref x);
        }

        // --------------------------------------------------------- cursor preview

        // Mirrors LayoutCursor.RenderCaret so we can eyeball the live cursor on light + dark.
        private static Bitmap DrawCursorPreview(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (var light = new SolidBrush(ColorTranslator.FromHtml("#e9edf1")))
            using (var dark = new SolidBrush(ColorTranslator.FromHtml("#1c2530")))
            {
                g.FillRectangle(light, 0, 0, w / 2, h);
                g.FillRectangle(dark, w / 2, 0, w - w / 2, h);
            }

            string[] codes = { "EN", "RU", "UK" };
            for (int i = 0; i < codes.Length; i++)
            {
                float cx = w * (0.18f + i * 0.30f);
                DrawCaret(g, cx, h / 2f, 56f, codes[i]);
            }
            return bmp;
        }

        private static void DrawCaret(Graphics g, float x, float yCenter, float beamH, string code)
        {
            float barW = beamH * 0.10f, serifW = beamH * 0.42f, serifH = beamH * 0.10f;
            float top = yCenter - beamH / 2f;

            void Beam(float bw, float sw, float sh, Color c)
            {
                using var b = new SolidBrush(c);
                g.FillRectangle(b, x - bw / 2f, top, bw, beamH);
                g.FillRectangle(b, x - sw / 2f, top, sw, sh);
                g.FillRectangle(b, x - sw / 2f, top + beamH - sh, sw, sh);
            }
            Beam(barW + 2f, serifW + 2f, serifH + 2f, Color.FromArgb(230, Color.White));
            Beam(barW, serifW, serifH, Color.Black);

            using var font = new Font("Segoe UI", beamH * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF sz = g.MeasureString(code, font);
            float pillX = x + serifW / 2f + beamH * 0.14f;
            float pillPad = beamH * 0.12f;
            var pill = new RectangleF(pillX, yCenter - sz.Height / 2f - beamH * 0.04f, sz.Width + pillPad * 2f, sz.Height + beamH * 0.08f);
            Color codeColor = PreviewColor(code);
            using (var path = Rounded(pill, beamH * 0.22f))
            using (var bg = new SolidBrush(Color.FromArgb(235, ColorTranslator.FromHtml("#11161f"))))
            {
                g.FillPath(bg, path);
            }
            using (var glyph = new GraphicsPath())
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                glyph.AddString(code, font.FontFamily, (int)FontStyle.Bold, font.Size, pill, sf);
                using var outline = new Pen(Color.Black, Math.Max(2f, font.Size * 0.16f)) { LineJoin = LineJoin.Round };
                using var fill = new SolidBrush(codeColor);
                g.DrawPath(outline, glyph);
                g.FillPath(fill, glyph);
            }
        }

        private static Color PreviewColor(string code)
        {
            switch (code)
            {
                case "EN": return ColorTranslator.FromHtml("#4DA3FF");
                case "RU": return ColorTranslator.FromHtml("#FF5A5A");
                case "UK": return ColorTranslator.FromHtml("#5AD86A");
                default: return ColorTranslator.FromHtml("#CCCCCC");
            }
        }

        // ----------------------------------------------------------------- util

        private static Font FitFont(Graphics g, string family, FontStyle style, float emSize, string text, float maxWidth)
        {
            float size = emSize;
            while (size > 8f)
            {
                var f = new Font(family, size, style, GraphicsUnit.Pixel);
                if (g.MeasureString(text, f).Width <= maxWidth)
                    return f;
                f.Dispose();
                size -= 1f;
            }
            return new Font(family, size, style, GraphicsUnit.Pixel);
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void SavePng(Bitmap bmp, string path)
        {
            using (bmp) bmp.Save(path, ImageFormat.Png);
        }

        private static void WriteIco(string path, int[] sizes, Func<int, Bitmap> render)
        {
            var images = new List<(int size, byte[] png)>();
            foreach (int s in sizes)
            {
                using var bmp = render(s);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                images.Add((s, ms.ToArray()));
            }

            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write((short)0);              // reserved
            bw.Write((short)1);              // type: icon
            bw.Write((short)images.Count);

            int offset = 6 + 16 * images.Count;
            foreach (var (size, png) in images)
            {
                bw.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
                bw.Write((byte)(size >= 256 ? 0 : size)); // height (0 => 256)
                bw.Write((byte)0);           // palette count
                bw.Write((byte)0);           // reserved
                bw.Write((short)1);          // color planes
                bw.Write((short)32);         // bits per pixel
                bw.Write(png.Length);        // bytes in resource
                bw.Write(offset);            // image offset
                offset += png.Length;
            }
            foreach (var (_, png) in images)
                bw.Write(png);
        }

        private static string FindRepoRoot()
        {
            // Walk up from the executable until we find CyrFlip.sln.
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CyrFlip.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
