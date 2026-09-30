using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// The screen region capture's geometry and pixels (ticket S0026, sections 4.1 and 7): the
    /// virtual-screen frame frozen by one <c>BitBlt</c>, the rectangle math the overlay runs on, and
    /// the crop. Everything is in <b>physical</b> virtual-screen pixels - the process is PerMonitorV2,
    /// so a drag across two monitors of different scaling is one rectangle with no seam.
    ///
    /// <para>The rectangle math is <c>internal static</c> and pure, so <c>ScreenCaptureTests</c> covers
    /// it without a desktop.</para>
    /// </summary>
    internal static class ScreenCapture
    {
        /// <summary>A selection this small or smaller (in both directions) is a click, i.e. a cancel.</summary>
        internal const int ClickSize = 2;

        /// <summary>The virtual screen: every monitor's union, physical pixels, origin possibly negative.</summary>
        public static Rectangle VirtualScreen => SystemInformation.VirtualScreen;

        /// <summary>
        /// Freeze the whole virtual screen into one 32bpp bitmap. <c>SRCCOPY | CAPTUREBLT</c>, not
        /// <c>Graphics.CopyFromScreen</c>, which leaves out <c>CAPTUREBLT</c> and with it every layered
        /// window - the tooltip scenario 2 exists for. Returns null when the grab failed.
        /// </summary>
        public static Bitmap? Grab(Rectangle virtualScreen)
        {
            if (virtualScreen.Width <= 0 || virtualScreen.Height <= 0) return null;
            Bitmap? frame = null;
            try
            {
                frame = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    IntPtr dest = g.GetHdc();
                    IntPtr screen = GetDC(IntPtr.Zero);
                    bool ok;
                    try
                    {
                        ok = BitBlt(dest, 0, 0, virtualScreen.Width, virtualScreen.Height,
                            screen, virtualScreen.X, virtualScreen.Y, SRCCOPY | CAPTUREBLT);
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, screen);
                        g.ReleaseHdc(dest);
                    }
                    if (!ok)
                    {
                        frame.Dispose();
                        return null;
                    }
                }
                return frame;
            }
            catch
            {
                frame?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// The pixels of <paramref name="region"/> (virtual-screen coordinates) copied out of
        /// <paramref name="frame"/>, row by row - an exact copy, never a resampled one. The result is
        /// independent of the frame, which the caller disposes at once (section 7).
        /// </summary>
        public static Bitmap Crop(Bitmap frame, Rectangle virtualScreen, Rectangle region)
        {
            var source = new Rectangle(region.X - virtualScreen.X, region.Y - virtualScreen.Y, region.Width, region.Height);
            source.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));
            if (source.Width <= 0 || source.Height <= 0)
                throw new ArgumentException("The region lies outside the frame.", nameof(region));

            var cropped = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppRgb);
            BitmapData from = frame.LockBits(source, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                BitmapData to = cropped.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var row = new byte[source.Width * 4];
                    for (int y = 0; y < source.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(from.Scan0, y * from.Stride), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(to.Scan0, y * to.Stride), row.Length);
                    }
                }
                finally { cropped.UnlockBits(to); }
            }
            finally { frame.UnlockBits(from); }
            return cropped;
        }

        /// <summary>The rectangle spanned by two drag points, whichever corner the drag started from.</summary>
        internal static Rectangle Normalize(Point a, Point b)
            => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        /// <summary><paramref name="rect"/> cut to the virtual screen (whose origin may be negative).</summary>
        internal static Rectangle ClampToVirtualScreen(Rectangle rect, Rectangle virtualScreen)
        {
            Rectangle clamped = Rectangle.Intersect(rect, virtualScreen);
            return clamped.Width > 0 && clamped.Height > 0 ? clamped : Rectangle.Empty;
        }

        /// <summary>A press and release without a real drag - Win+Shift+S reads it as a cancel too.</summary>
        internal static bool IsClick(Rectangle selection)
            => selection.Width <= ClickSize && selection.Height <= ClickSize;

        /// <summary>
        /// The part of <paramref name="selection"/> that <paramref name="monitor"/> paints, in that
        /// monitor's client coordinates (its top-left is 0,0); empty when the two do not meet.
        /// </summary>
        internal static Rectangle MonitorSlice(Rectangle selection, Rectangle monitor)
        {
            Rectangle meet = Rectangle.Intersect(selection, monitor);
            if (meet.Width <= 0 || meet.Height <= 0) return Rectangle.Empty;
            meet.Offset(-monitor.X, -monitor.Y);
            return meet;
        }

        /// <summary>The monitors' bounds in physical pixels.</summary>
        public static List<Rectangle> MonitorBounds()
        {
            var bounds = new List<Rectangle>();
            foreach (Screen screen in Screen.AllScreens) bounds.Add(screen.Bounds);
            return bounds;
        }

        /// <summary>
        /// Keep one of CyrFlip's own marks over the user's content (the caret badge, the translation
        /// popup) out of every screen capture - ours, Win+Shift+S, a screen share (S0026 4.2). Windows
        /// 10 2004+; on an older build the call fails and is ignored.
        /// </summary>
        public static void ExcludeFromCapture(IntPtr window)
        {
            try { SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE); }
            catch { /* older Windows: the window simply stays capturable */ }
        }

        /// <summary>The pointer in physical screen coordinates (never a control-relative event arg).</summary>
        public static Point CursorPosition()
            => GetCursorPos(out POINT p) ? new Point(p.X, p.Y) : Cursor.Position;
    }
}
