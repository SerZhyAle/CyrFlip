using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// CyrFlip's headline feature: while writing, the system text cursor (the I-beam) is
    /// replaced with a caret carrying the current keyboard-layout marker (EN/RU/UK), updated
    /// live as the layout changes.
    ///
    /// This uses <c>SetSystemCursor(OCR_IBEAM)</c>, which is **global** — every app's text
    /// cursor changes until restored. The replacement is therefore always undone on Dispose,
    /// app exit, and unhandled exceptions (see CyrFlipContext); <see cref="ForceRestore"/>
    /// reloads the default system cursors unconditionally.
    /// </summary>
    internal sealed class LayoutCursor : IDisposable
    {
        private readonly int _scale;
        private string _current = "";
        private bool _applied;

        public LayoutCursor(int cursorSize)
        {
            // Marker/caret height in px; clamp to something legible.
            _scale = Math.Max(18, Math.Min(64, cursorSize == 0 ? 24 : cursorSize));
        }

        /// <summary>Replace the system I-beam with a caret marked with <paramref name="code"/>.</summary>
        public void Apply(string code)
        {
            if (code == _current && _applied)
                return;

            IntPtr hcur = BuildCursor(code);
            if (hcur == IntPtr.Zero)
                return;

            // SetSystemCursor copies the cursor contents then destroys the handle we pass.
            if (SetSystemCursor(hcur, OCR_IBEAM))
            {
                _applied = true;
                _current = code;
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
                ForceRestore();
                _applied = false;
            }
        }

        /// <summary>Reload default system cursors unconditionally (crash/exit safety net).</summary>
        public static void ForceRestore()
            => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);

        // -------------------------------------------------------------------- rendering

        private IntPtr BuildCursor(string code)
        {
            using Bitmap bmp = RenderCaret(code, _scale, out int hotX, out int hotY);

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

        private static Bitmap RenderCaret(string code, int scale, out int hotX, out int hotY)
        {
            float beamH = scale;
            float barW = Math.Max(2f, beamH * 0.10f);
            float serifW = beamH * 0.42f;
            float serifH = Math.Max(2f, beamH * 0.10f);
            int pad = (int)Math.Ceiling(beamH * 0.16f);
            int height = (int)Math.Ceiling(beamH) + pad * 2;

            float beamCx = pad + serifW / 2f;

            // Measure the marker text to size the canvas.
            using var font = new Font("Segoe UI", beamH * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
            float markerW, markerH;
            using (var probe = Graphics.FromImage(new Bitmap(1, 1)))
            {
                SizeF s = probe.MeasureString(code, font);
                markerW = s.Width;
                markerH = s.Height;
            }

            float gap = beamH * 0.14f;
            float pillPadX = beamH * 0.12f;
            float pillX = pad + serifW + gap;
            float pillW = markerW + pillPadX * 2f;
            int width = (int)Math.Ceiling(pillX + pillW) + pad;

            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                float beamTop = pad;

                // I-beam: white halo (so it shows on dark backgrounds) then black core on top.
                DrawBeam(g, beamCx, beamTop, beamH, barW + 2f, serifW + 2f, serifH + 2f, Color.FromArgb(230, Color.White));
                DrawBeam(g, beamCx, beamTop, beamH, barW, serifW, serifH, Color.Black);

                // Marker pill + text.
                var pill = new RectangleF(pillX, beamTop + (beamH - markerH) / 2f - beamH * 0.06f, pillW, markerH + beamH * 0.12f);
                using (var pillPath = Rounded(pill, beamH * 0.22f))
                using (var pillBg = new SolidBrush(Color.FromArgb(235, ColorTranslator.FromHtml("#11161f"))))
                {
                    g.FillPath(pillBg, pillPath);
                }
                LayoutStyle.DrawCode(g, code, font, pill);
            }

            // Hotspot sits on the I-beam (where the text caret would be).
            hotX = (int)Math.Round(beamCx);
            hotY = height / 2;
            return bmp;
        }

        private static void DrawBeam(Graphics g, float cx, float top, float beamH, float barW, float serifW, float serifH, Color color)
        {
            using var b = new SolidBrush(color);
            g.FillRectangle(b, cx - barW / 2f, top, barW, beamH);                 // vertical bar
            g.FillRectangle(b, cx - serifW / 2f, top, serifW, serifH);            // top serif
            g.FillRectangle(b, cx - serifW / 2f, top + beamH - serifH, serifW, serifH); // bottom serif
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

        public void Dispose() => Restore();
    }
}
