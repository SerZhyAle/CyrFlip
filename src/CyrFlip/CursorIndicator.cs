using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Tracks the active window's keyboard layout and surfaces it as a two-letter code - any layout
    /// Windows knows, not a fixed set. (spec §2.3 - layout detection + label rendering.)
    ///
    /// Note: the spec describes replacing the mouse cursor globally. That requires
    /// SetSystemCursor (system-wide, must be restored on crash) and is intentionally avoided
    /// in v1. The same label is instead rendered into the tray icon via <see cref="RenderIcon"/>.
    /// </summary>
    internal sealed class CursorIndicator : IDisposable
    {
        /// <summary>Raised when the layout code or the CapsLock state changes (code, capsOn).</summary>
        public event Action<string, bool>? LayoutChanged;

        private readonly Timer _timer = new Timer { Interval = 150 };
        private string _last = "";
        private bool _lastCaps;
        private IntPtr _lastActive;

        /// <summary>
        /// The window the user was last typing in - the foreground window with the taskbar and our
        /// own windows filtered out. Clicking the tray icon moves the focus to the shell, so the
        /// tray's layout switch has to act on the window the user came from, not on the taskbar.
        /// Falls back to the live foreground window while nothing has been tracked yet.
        /// </summary>
        public IntPtr LastActiveWindow
        {
            get
            {
                IntPtr tracked = _lastActive;
                return tracked != IntPtr.Zero && IsWindow(tracked) ? tracked : GetForegroundWindow();
            }
        }

        public void Start()
        {
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
            Poll();
        }

        private void Poll()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsUserWindow(foreground)) _lastActive = foreground;

            string code = DetectLayout();
            bool caps = IsCapsLockOn();
            if (code != _last || caps != _lastCaps)
            {
                _last = code;
                _lastCaps = caps;
                LayoutChanged?.Invoke(code, caps);
            }
        }

        /// <summary>
        /// True when the window is somebody else's document/input window: not the shell's taskbar
        /// surfaces (which the notification area belongs to) and not one of ours - the settings
        /// window, the history strip and the overlay must never become the layout-switch target.
        /// </summary>
        private static bool IsUserWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == CurrentProcessId) return false;

            var cls = new System.Text.StringBuilder(128);
            if (GetClassName(hwnd, cls, cls.Capacity) == 0) return true; // unreadable class: assume a real window
            switch (cls.ToString())
            {
                case "Shell_TrayWnd":                    // the taskbar itself (holds the tray)
                case "Shell_SecondaryTrayWnd":           // taskbar on a secondary monitor
                case "NotifyIconOverflowWindow":         // the hidden-icons flyout (Win10)
                case "TopLevelWindowForOverflowXamlIsland": // the same flyout on Win11
                case "XamlExplorerHostIslandWindow":     // Start / task view shells
                    return false;
                default:
                    return true;
            }
        }

        private static readonly uint CurrentProcessId =
            (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>True when CapsLock is toggled on (low bit of the key's toggle state).</summary>
        public static bool IsCapsLockOn() => (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

        /// <summary>
        /// The code the marker shows for the foreground window's layout. The low word of the HKL is the
        /// language id; decoding it is <see cref="WorldLayouts.CodeForLangId"/>'s job, so the indicator
        /// and the settings tables can never disagree about what to call the same layout.
        ///
        /// <para>Hand-written EN/RU/UK cases used to sit in front of that call. They were not a limit -
        /// the decode answers EN, RU and UK for those very ids anyway - just a leftover from when three
        /// languages were the whole product.</para>
        ///
        /// <para><b>This is a language, not a layout:</b> US and Dvorak both read EN, and the standard
        /// Russian layout and Russian Typewriter both read RU. That is a deliberate decision, not an
        /// oversight - the marker is a few pixels wide and gains nothing from a variant number. The
        /// exact KLID of every installed layout is on the "Языки Windows" tab, and conversion profiles
        /// bind to it.</para>
        /// </summary>
        public static string DetectLayout()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            IntPtr hkl = GetKeyboardLayout(threadId);
            int langId = (int)((long)hkl & 0xFFFF);
            return WorldLayouts.CodeForLangId(langId);
        }

        /// <summary>
        /// Render a tray icon showing the layout <paramref name="code"/>. When
        /// <paramref name="capsOn"/> is true, a 1px layout-colour frame marks CapsLock as on.
        /// </summary>
        public static Icon RenderIcon(string code, bool capsOn = false)
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

                if (capsOn)
                    LayoutStyle.DrawCapsFrame(g, tile, 7f, code);
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
