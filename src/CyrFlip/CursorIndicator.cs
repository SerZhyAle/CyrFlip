using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Tracks the active window's keyboard layout and surfaces it as a two-letter code
    /// (EN/RU/UK). (spec §2.3 — layout detection + label rendering.)
    ///
    /// Note: the spec describes replacing the mouse cursor globally. That requires
    /// SetSystemCursor (system-wide, must be restored on crash) and is intentionally avoided
    /// in v1. The same label is instead rendered into the tray icon via <see cref="RenderIcon"/>.
    /// </summary>
    internal sealed class CursorIndicator : IDisposable
    {
        public event Action<string>? LayoutChanged;

        private readonly Timer _timer = new Timer { Interval = 150 };
        private string _last = "";

        public void Start()
        {
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
            Poll();
        }

        private void Poll()
        {
            string code = DetectLayout();
            if (code != _last)
            {
                _last = code;
                LayoutChanged?.Invoke(code);
            }
        }

        public static string DetectLayout()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            IntPtr hkl = GetKeyboardLayout(threadId);
            int langId = (int)((long)hkl & 0xFFFF);

            switch (langId & 0x3FF) // primary language id
            {
                case 0x09: return "EN";
                case 0x19: return "RU";
                case 0x22: return "UK";
            }

            try
            {
                return new CultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant();
            }
            catch (CultureNotFoundException)
            {
                return "??";
            }
        }

        /// <summary>Render a tray icon showing the layout <paramref name="code"/>.</summary>
        public static Icon RenderIcon(string code)
        {
            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var tile = new RectangleF(0, 0, 32, 32);
                using (var path = Rounded(tile, 7f))
                using (var bg = new LinearGradientBrush(tile,
                           ColorTranslator.FromHtml("#11161f"),
                           ColorTranslator.FromHtml("#1c2636"),
                           LinearGradientMode.Vertical))
                {
                    g.FillPath(bg, path);
                }

                using var font = new Font("Segoe UI", code.Length >= 2 ? 14f : 16f, FontStyle.Bold, GraphicsUnit.Pixel);
                LayoutStyle.DrawCode(g, code, font, tile);
            }

            return IconFromBitmap(bmp);
        }

        /// <summary>Wrap a bitmap in a managed Icon (PNG-payload .ico), avoiding leaked HICONs.</summary>
        private static Icon IconFromBitmap(Bitmap bmp)
        {
            byte[] png;
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }

            using var ico = new MemoryStream();
            using (var bw = new BinaryWriter(ico, System.Text.Encoding.Default, leaveOpen: true))
            {
                bw.Write((short)0);            // reserved
                bw.Write((short)1);            // type: icon
                bw.Write((short)1);            // image count
                bw.Write((byte)bmp.Width);
                bw.Write((byte)bmp.Height);
                bw.Write((byte)0);             // palette
                bw.Write((byte)0);             // reserved
                bw.Write((short)1);            // planes
                bw.Write((short)32);           // bpp
                bw.Write(png.Length);
                bw.Write(6 + 16);              // offset to image
                bw.Write(png);
            }
            ico.Position = 0;
            return new Icon(ico);
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

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
