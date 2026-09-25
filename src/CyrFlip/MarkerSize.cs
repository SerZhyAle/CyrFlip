using System;
using Microsoft.Win32;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// How big the marker and the branded I-beam are drawn (ticket S0011 LI-3). CyrFlip is per-monitor
    /// DPI aware, so Windows scales none of its drawing: a size in pixels stays that size on a 250%
    /// laptop panel, where the old fixed 24px badge was six or seven logical pixels - barely readable -
    /// and the I-beam sat beside a system pointer three times its size. The base size is the user's
    /// setting (<see cref="AppConfig.CursorSize"/>, one of <see cref="Presets"/>); the monitor's DPI and,
    /// for the I-beam, the pointer-size accessibility setting are applied on top.
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
        /// Height of the branded I-beam in physical pixels: the base size scaled by the DPI and by the
        /// pointer size the user chose in Windows (<c>CursorBaseSize</c>, 32 at the smallest step), so a
        /// user who enlarged the pointer for their eyesight does not get a small I-beam back.
        /// </summary>
        public static int CursorHeight(int baseSize, int dpi, int cursorBaseSize)
        {
            int b = baseSize <= 0 ? DefaultBase : baseSize;
            int pointer = cursorBaseSize < StandardCursorBaseSize ? StandardCursorBaseSize : cursorBaseSize;
            return Clamp((int)Math.Round(b * Dpi(dpi) / 96.0 * pointer / StandardCursorBaseSize), 18, 192);
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

        /// <summary>The pointer size from Settings ▸ Accessibility ▸ Mouse pointer (32 when unset).</summary>
        public static int ReadCursorBaseSize()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
                if (key?.GetValue("CursorBaseSize") is int size && size > 0)
                    return size;
            }
            catch { /* unreadable: the standard size */ }
            return StandardCursorBaseSize;
        }
    }
}
