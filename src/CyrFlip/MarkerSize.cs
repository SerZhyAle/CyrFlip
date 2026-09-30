using System;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// How big the marker and the branded I-beam are drawn (ticket S0011 LI-3). CyrFlip is per-monitor
    /// DPI aware, so Windows scales none of its drawing: a size in pixels stays that size on a 250%
    /// laptop panel, where the old fixed 24px badge was six or seven logical pixels - barely readable -
    /// and the I-beam sat beside a system pointer three times its size. The base size is the user's
    /// setting (<see cref="AppConfig.CursorSize"/>, one of <see cref="Presets"/>); the monitor's DPI is
    /// applied on top. The I-beam is drawn for the nominal cursor cell, and Windows applies the
    /// pointer-size accessibility setting to it itself (<see cref="CursorHeight"/>).
    /// </summary>
    internal static class MarkerSize
    {
        /// <summary>The three sizes the settings slider offers: small, medium, large (base px at 100%).</summary>
        public static readonly int[] Presets = { 18, 24, 32 };

        /// <summary>The default preset - what <see cref="AppConfig.CursorSize"/> starts at.</summary>
        public const int DefaultBase = 24;

        /// <summary>The Windows pointer size at "1" on the accessibility slider.</summary>
        public const int StandardCursorBaseSize = 32;

        /// <summary>The index of the preset nearest to <paramref name="baseSize"/> - a hand-edited registry
        /// value between two steps still shows the slider somewhere sensible.</summary>
        public static int NearestPreset(int baseSize)
        {
            int best = 0;
            for (int i = 1; i < Presets.Length; i++)
                if (Math.Abs(Presets[i] - baseSize) < Math.Abs(Presets[best] - baseSize))
                    best = i;
            return best;
        }

        /// <summary>Height of the caret overlay badge in physical pixels on a monitor of <paramref name="dpi"/>.</summary>
        public static int OverlayHeight(int baseSize, int dpi)
        {
            int b = baseSize <= 0 ? DefaultBase : baseSize;
            return Clamp((int)Math.Round(b * Dpi(dpi) / 96.0), 14, 128);
        }

        /// <summary>
        /// How tall Windows' own I-beam is at the default marker size, as a fraction of the base size.
        /// Measured on the stock <c>ibeam_eoa.cur</c>: its glyph is 0.57 of the pointer cell (73px in a
        /// 128px cell, 127px in a 224px one), i.e. about 18px in the standard 32px cell - so the default
        /// base of 24 maps onto 18px.
        /// </summary>
        internal const double StockIBeamPerBase = 0.75;

        /// <summary>
        /// Height of the branded I-beam in pixels <b>of the nominal cursor cell</b>
        /// (<paramref name="nominalCursor"/> = <c>SM_CYCURSOR</c>, which follows the DPI but not the
        /// pointer size): the base size calibrated to the stock I-beam (<see cref="StockIBeamPerBase"/>).
        /// Windows itself stretches a cursor set with <c>SetSystemCursor</c> from that nominal cell to the
        /// pointer size chosen in Settings ▸ Accessibility, exactly as it does its own cursors - verified
        /// live at pointer size 7 on a 175% panel, where a 126px I-beam came out about 4.7 times the
        /// height of the arrow (224px cell / 48px nominal). Multiplying by the pointer size here as well
        /// (S0011 LI-3) scaled it twice.
        /// </summary>
        public static int CursorHeight(int baseSize, int nominalCursor)
        {
            int b = baseSize <= 0 ? DefaultBase : baseSize;
            int cell = nominalCursor <= 0 ? StandardCursorBaseSize : nominalCursor;
            return Clamp((int)Math.Round(b * StockIBeamPerBase * cell / StandardCursorBaseSize), 14, 192);
        }

        /// <summary>The nominal cursor cell Windows scales every cursor from (<c>SM_CYCURSOR</c>: 32 at
        /// 100%, 48 at 150-175%, 64 at 200%); 32 when it cannot be read.</summary>
        public static int NominalCursorSize()
        {
            try
            {
                int size = GetSystemMetrics(SM_CYCURSOR);
                if (size > 0) return size;
            }
            catch (EntryPointNotFoundException) { }
            return StandardCursorBaseSize;
        }

        private static int Dpi(int dpi) => dpi <= 0 ? 96 : dpi;

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        // ------------------------------------------------------------------ live readings

        /// <summary>The effective DPI of the monitor holding the screen point (96 when it cannot be read).</summary>
        public static int MonitorDpi(IntPtr monitor)
        {
            try
            {
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                    return (int)dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }

        /// <summary>The monitor a screen point is on (the nearest one when it is on none).</summary>
        public static IntPtr MonitorAt(int x, int y) => MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);

        /// <summary>The DPI of the primary monitor - where a system cursor is designed for.</summary>
        public static int PrimaryDpi() => MonitorDpi(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY));
    }
}
