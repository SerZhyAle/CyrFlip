using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Feature #3: a small marker (EN/RU/UK) pinned next to the blinking text caret of the
    /// active window — because the mouse pointer is often an arrow while you type, so the
    /// caret is the place that actually shows where text will land.
    ///
    /// The overlay is a borderless, topmost, click-through, no-activate window that follows
    /// the system caret via <c>GetGUIThreadInfo</c>. It only works where the app exposes the
    /// system caret (classic Win32 edit controls — Notepad, dialog fields, many editors);
    /// Chromium/UWP/Electron draw their own caret and report no rect, so the overlay hides there.
    /// </summary>
    internal sealed class CaretOverlay : IDisposable
    {
        private readonly OverlayForm _form;
        private readonly Timer _timer = new Timer { Interval = 70 };
        private string _code = "";

        public CaretOverlay(int size) => _form = new OverlayForm(size);

        public void Start()
        {
            _timer.Tick += (_, _) => Follow();
            _timer.Start();
        }

        /// <summary>Set the layout code shown by the overlay (called on layout change).</summary>
        public void SetLayout(string code)
        {
            _code = code;
            _form.SetCode(code);
        }

        private void Follow()
        {
            if (string.IsNullOrEmpty(_code))
            {
                _form.HideOverlay();
                return;
            }

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                _form.HideOverlay();
                return;
            }

            uint tid = GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };

            // Need a real caret with non-zero height; otherwise the app doesn't expose one.
            if (!GetGUIThreadInfo(tid, ref gti)
                || gti.hwndCaret == IntPtr.Zero
                || gti.rcCaret.Bottom - gti.rcCaret.Top <= 0)
            {
                _form.HideOverlay();
                return;
            }

            var pt = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Top };
            ClientToScreen(gti.hwndCaret, ref pt);
            _form.ShowAt(pt.X + 4, pt.Y);
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            _form.Dispose();
        }

        // ------------------------------------------------------------------ overlay window

        private sealed class OverlayForm : Form
        {
            private readonly int _h;
            private readonly Font _font;
            private string _code = "";

            public OverlayForm(int size)
            {
                _h = Math.Max(14, Math.Min(40, size == 0 ? 18 : size));
                _font = new Font("Segoe UI", _h * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = ColorTranslator.FromHtml("#11161f");
                DoubleBuffered = true;
                ResizeToContent();
            }

            // Never take focus from the window the user is typing in.
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                    return cp;
                }
            }

            public void SetCode(string code)
            {
                if (code == _code)
                    return;
                _code = code;
                ResizeToContent();
                Invalidate();
            }

            public void ShowAt(int x, int y)
                => SetWindowPos(Handle, HWND_TOPMOST, x, y, Width, Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);

            public void HideOverlay()
            {
                if (IsHandleCreated && Visible)
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_HIDEWINDOW | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            private void ResizeToContent()
            {
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                SizeF s = g.MeasureString(_code.Length == 0 ? "EN" : _code, _font);
                int padX = (int)Math.Round(_h * 0.20);
                int padY = (int)Math.Round(_h * 0.12);
                Size = new Size((int)Math.Ceiling(s.Width) + padX * 2, (int)Math.Ceiling(s.Height) + padY * 2);

                // Rounded shape via a window region (opaque fill, no transparency-key fringe).
                using var path = new GraphicsPath();
                int r = Math.Max(3, _h / 4);
                int d = r * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - d, 0, d, d, 270, 90);
                path.AddArc(Width - d, Height - d, d, d, 0, 90);
                path.AddArc(0, Height - d, d, d, 90, 90);
                path.CloseFigure();
                Region oldRegion = Region;
                Region = new Region(path);
                oldRegion?.Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                LayoutStyle.DrawCode(g, _code, _font, new RectangleF(0, 0, Width, Height));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _font.Dispose();
                    Region?.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
