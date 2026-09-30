using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// CyrFlip's headline feature: while writing, the system text cursor (the I-beam) is
    /// replaced with a caret carrying the current keyboard-layout marker (any layout code), updated
    /// live as the layout changes.
    ///
    /// This uses <c>SetSystemCursor(OCR_IBEAM)</c>, which is **global** - every app's text
    /// cursor changes until restored. The replacement is therefore always undone on Dispose,
    /// app exit, and unhandled exceptions (see CyrFlipContext); <see cref="ForceRestore"/>
    /// reloads the default system cursors unconditionally.
    /// </summary>
    internal sealed class LayoutCursor : IDisposable
    {
        private int _baseSize;
        private int _scale;
        private int _badge;
        private string _current = "";
        private string _currentKlid = "";
        private bool _currentCaps;
        private bool _applied;

        public LayoutCursor(int cursorSize)
        {
            _baseSize = cursorSize;
            (_scale, _badge) = MeasureScale(cursorSize);
        }

        /// <summary>
        /// The I-beam's height in the nominal cursor cell (<c>SM_CYCURSOR</c>, which carries the DPI);
        /// Windows stretches it to the pointer size the user chose, as it does its own cursors
        /// (<see cref="MarkerSize.CursorHeight"/>). The badge is sized from the I-beam, so the two keep
        /// their proportions at every pointer size.
        /// </summary>
        private static (int beam, int badge) MeasureScale(int baseSize)
        {
            int beam = MarkerSize.CursorHeight(baseSize, MarkerSize.NominalCursorSize());
            return (beam, beam);
        }

        /// <summary>The height the I-beam is currently built at - for diagnostics and tests.</summary>
        internal int Scale => _scale;

        /// <summary>The marker size setting changed; rebuilds the I-beam when it is on.</summary>
        public void SetBaseSize(int cursorSize)
        {
            _baseSize = cursorSize;
            Rebuild(force: false);
        }

        /// <summary>
        /// Something reloaded the system cursors or changed the display (ticket S0011 LI-6). A reload -
        /// the user changing the pointer size or colour, a theme, another tool calling
        /// <c>SPI_SETCURSORS</c> - puts the stock I-beam back, but nothing about the layout changed, so
        /// <see cref="Apply"/> used to return early and the headline feature stayed off until the next
        /// layout or CapsLock change. The reload CyrFlip itself causes (<see cref="ForceRestore"/>) is
        /// ignored. <paramref name="cursorsReloaded"/> false = only the display changed: rebuilt only
        /// when the size it asks for differs.
        /// </summary>
        public void OnSystemCursorsChanged(bool cursorsReloaded)
        {
            if (Volatile.Read(ref s_reloading) != 0) return;
            Rebuild(force: cursorsReloaded);
        }

        private void Rebuild(bool force)
        {
            (int scale, int badge) = MeasureScale(_baseSize);
            bool resized = scale != _scale || badge != _badge;
            _scale = scale;
            _badge = badge;
            if (!_applied || (!force && !resized)) return;
            _applied = false;
            Apply(_current, _currentKlid, _currentCaps);
        }

        /// <summary>
        /// Replace the system I-beam with a caret marked with <paramref name="code"/>, coloured for the
        /// <paramref name="klid"/> layout. When <paramref name="capsOn"/> is true, a 1px frame around
        /// the marker flags CapsLock.
        /// </summary>
        public void Apply(string code, string? klid = null, bool capsOn = false)
        {
            klid ??= "";
            if (code == _current && klid == _currentKlid && capsOn == _currentCaps && _applied)
                return;

            IntPtr hcur = BuildCursor(code, klid, capsOn);
            if (hcur == IntPtr.Zero)
                return;

            // SetSystemCursor copies the cursor contents then destroys the handle we pass.
            if (SetSystemCursor(hcur, OCR_IBEAM))
            {
                Interlocked.Exchange(ref s_replaced, 1);
                _applied = true;
                _current = code;
                _currentKlid = klid;
                _currentCaps = capsOn;
                ForceCursorRefresh();
            }
            else
            {
                DestroyCursor(hcur);
            }
        }

        /// <summary>
        /// Nudge the OS to repaint the *currently shown* cursor. SetSystemCursor only swaps the
        /// cursor resource; the on-screen image refreshes on the next WM_SETCURSOR, which fires on
        /// mouse movement. A zero-delta mouse move forces that re-evaluation without moving the pointer.
        /// </summary>
        private static void ForceCursorRefresh()
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_MOVE } },
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Undo the replacement (reload default system cursors).</summary>
        public void Restore()
        {
            if (_applied)
            {
                // Cleared first: the reload below broadcasts WM_SETTINGCHANGE, which reaches our own
                // watcher window synchronously when this runs on the UI thread.
                _applied = false;
                ForceRestore();
            }
        }

        /// <summary>
        /// Reload the default system cursors (crash/exit safety net) - but only when this process has
        /// replaced one since the last reload. <c>SPI_SETCURSORS</c> reloads the whole scheme, undoing
        /// cursors other tools set with <c>SetSystemCursor</c>, and broadcasts <c>WM_SETTINGCHANGE</c> to
        /// every window; with the cursor change off (the default) CyrFlip never replaced anything and
        /// has nothing to undo. Idempotent and safe from any thread.
        /// </summary>
        public static void ForceRestore()
        {
            if (Interlocked.Exchange(ref s_replaced, 0) == 0) return;
            Interlocked.Exchange(ref s_reloading, 1);
            try { ReloadCursors(); }
            finally { Interlocked.Exchange(ref s_reloading, 0); }
        }

        /// <summary>1 while the system I-beam is ours; set by a successful <c>SetSystemCursor</c>.</summary>
        private static int s_replaced;

        /// <summary>1 while CyrFlip's own <c>SPI_SETCURSORS</c> is in flight - its broadcast is not a
        /// reload to answer (<see cref="OnSystemCursorsChanged"/>).</summary>
        private static int s_reloading;

        /// <summary>The <c>SPI_SETCURSORS</c> call - a seam so the "never replaced, never reloaded" rule is testable.</summary>
        internal static Action ReloadCursors = () => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);

        /// <summary>Test seam: marks the I-beam as replaced without touching the system cursor.</summary>
        internal static void MarkReplacedForTest() => Interlocked.Exchange(ref s_replaced, 1);

        // -------------------------------------------------------------------- rendering

        private IntPtr BuildCursor(string code, string klid, bool capsOn)
        {
            using Bitmap bmp = RenderCaret(code, klid, _scale, _badge, capsOn, out int hotX, out int hotY);

            // GetHicon preserves alpha; rebuild it as a *cursor* (fIcon = false) with a hotspot.
            IntPtr hicon = bmp.GetHicon();
            try
            {
                if (!GetIconInfo(hicon, out ICONINFO ii))
                    return IntPtr.Zero;
                try
                {
                    ii.fIcon = false;
                    ii.xHotspot = hotX;
                    ii.yHotspot = hotY;
                    return CreateIconIndirect(ref ii);
                }
                finally
                {
                    if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                    if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                }
            }
            finally
            {
                DestroyIcon(hicon);
            }
        }

        /// <summary>The cursor bitmap: an opaque I-beam with the translucent layout badge beside it.
        /// Internal so a test can read the pixels back - "is the badge see-through" is not something a
        /// build can answer, and it is the whole point of the marker not hiding the text under it.</summary>
        internal static Bitmap RenderCaret(string code, string klid, int scale, bool capsOn, out int hotX, out int hotY)
            => RenderCaret(code, klid, scale, scale, capsOn, out hotX, out hotY);

        /// <summary><paramref name="scale"/> is the I-beam's height, <paramref name="badgeSize"/> the
        /// badge's size - separate, since only the I-beam follows the Windows pointer size.</summary>
        internal static Bitmap RenderCaret(string code, string klid, int scale, int badgeSize, bool capsOn, out int hotX, out int hotY)
        {
            float beamH = scale;
            float badgeH = badgeSize > 0 ? badgeSize : scale;
            float barW = Math.Max(2f, beamH * 0.10f);
            float serifW = beamH * 0.42f;
            float serifH = Math.Max(2f, beamH * 0.10f);
            int pad = (int)Math.Ceiling(Math.Max(beamH, badgeH) * 0.16f);

            float beamCx = pad + serifW / 2f;
            float beamTop = pad;
            float beamMid = beamTop + beamH / 2f;

            // Measure the marker text to size the canvas.
            using var font = new Font("Segoe UI", badgeH * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
            float markerW, markerH;
            using (var probeBmp = new Bitmap(1, 1))
            using (var probe = Graphics.FromImage(probeBmp))
            {
                SizeF s = probe.MeasureString(code, font);
                markerW = s.Width;
                markerH = s.Height;
            }

            // The badge sits tight against the I-beam and hangs below it, its top at three quarters of
            // the beam - the descender zone of the line the beam spans: centred on the beam it covered
            // the text right after the pointer, which is the text the user is aiming at.
            float gap = Math.Max(1f, badgeH * 0.04f);
            float pillPadX = badgeH * 0.12f;
            float pillX = pad + serifW + gap;
            float pillW = markerW + pillPadX * 2f;
            float pillH = markerH + badgeH * 0.12f;
            var pill = new RectangleF(pillX, beamTop + beamH * 0.75f, pillW, pillH);

            int width = (int)Math.Ceiling(pillX + pillW) + pad;
            int height = (int)Math.Ceiling(Math.Max(beamTop + beamH, pill.Bottom)) + pad;

            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                // I-beam: white halo (so it shows on dark backgrounds) then black core on top.
                DrawBeam(g, beamCx, beamTop, beamH, barW + 2f, serifW + 2f, serifH + 2f, Color.FromArgb(230, Color.White));
                DrawBeam(g, beamCx, beamTop, beamH, barW, serifW, serifH, Color.Black);

                // Marker text, composed on its own layer and then blended in at
                // LayoutStyle.MarkerOpacity. The badge is the part that covers the user's text, so it is
                // the part that is translucent; the I-beam above stays fully opaque, because a mouse
                // pointer you can see through is a worse cursor, not a subtler one. There is no dark
                // plate behind the letters: DrawCode already outlines them in black, so a plate only
                // hid more of the text under the pointer without making the letters any easier to read.
                using (var badge = new Bitmap(width, height))
                {
                    using (var bg = Graphics.FromImage(badge))
                    {
                        bg.SmoothingMode = SmoothingMode.AntiAlias;
                        bg.TextRenderingHint = TextRenderingHint.AntiAlias;
                        LayoutStyle.DrawCode(bg, code, font, pill, klid);

                        if (capsOn)
                            LayoutStyle.DrawCapsFrame(bg, pill, badgeH * 0.22f, code, klid);
                    }

                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = LayoutStyle.MarkerOpacity });
                    g.DrawImage(badge, new Rectangle(0, 0, width, height),
                        0, 0, width, height, GraphicsUnit.Pixel, attributes);
                }
            }

            // Hotspot sits on the middle of the I-beam (where the text caret would be) - no longer the
            // middle of the bitmap, which the lowered badge makes taller below the beam than above it.
            hotX = (int)Math.Round(beamCx);
            hotY = (int)Math.Round(beamMid);
            return bmp;
        }

        private static void DrawBeam(Graphics g, float cx, float top, float beamH, float barW, float serifW, float serifH, Color color)
        {
            using var b = new SolidBrush(color);
            g.FillRectangle(b, cx - barW / 2f, top, barW, beamH);                 // vertical bar
            g.FillRectangle(b, cx - serifW / 2f, top, serifW, serifH);            // top serif
            g.FillRectangle(b, cx - serifW / 2f, top + beamH - serifH, serifW, serifH); // bottom serif
        }

        public void Dispose() => Restore();
    }
}
