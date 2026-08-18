using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// Which <b>keyboard layout</b> - not just which language - is active: the KLID behind a live HKL.
    ///
    /// <para>The two letters on the marker name a language, and always will: "US-Intl" does not fit in a
    /// badge a few pixels wide. The colour is what separates two layouts of one language, and it can only
    /// do that if we know the layout, so the indicator resolves the HKL down to a KLID and hands both to
    /// <see cref="LayoutStyle.ColorForLayout"/>.</para>
    ///
    /// <para><b>The HKL's high word is the whole trick.</b> For a language's primary keyboard it is the
    /// language id again (US = <c>04090409</c>); for every other keyboard it is <c>0xF000 | LayoutId</c>,
    /// where <c>LayoutId</c> is the value Windows stores beside that layout in
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\&lt;klid&gt;</c> - verified live:
    /// US-International (<c>Layout Id</c> = 0001) loads as <c>F0010409</c> and Russian Typewriter
    /// (0008) as <c>F0080419</c>. An IME rides a <c>0xE0xx</c> handle instead and has no KLID of its
    /// own; that falls back to the language's primary layout, which is what the user sees the marker
    /// call it anyway.</para>
    /// </summary>
    internal static class LayoutIdentity
    {
        private const string LayoutsPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

        private static Dictionary<int, string>? _byLayoutId;
        private static readonly object Gate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _readMs;

        /// <summary>How long a fruitless map is kept before a missing layout id is looked up again.</summary>
        private const long RereadAfterMs = 30_000;

        /// <summary>The KLID of a live HKL, or "" when it cannot be worked out.</summary>
        public static string KlidForHkl(IntPtr hkl)
        {
            uint value = unchecked((uint)(long)hkl);
            string klid = Resolve(value, LayoutIdMap());

            // A substitute layout whose id is not in the map means the map predates it: a display
            // language pack installed while CyrFlip was running adds layouts to the machine store.
            // Re-read at most every 30 s - this runs on the 150 ms indicator tick.
            ushort high = (ushort)(value >> 16);
            if ((high & 0xF000) == 0xF000 && klid.StartsWith("0000", StringComparison.Ordinal)
                && Clock.ElapsedMilliseconds - _readMs > RereadAfterMs)
            {
                lock (Gate)
                    _byLayoutId = null;
                klid = Resolve(value, LayoutIdMap());
            }
            return klid;
        }

        /// <summary>
        /// The decode itself, with the registry handed in - the whole reason this is a separate method is
        /// that the mapping is the part a test can pin, while reading the registry is not.
        /// </summary>
        internal static string Resolve(uint hkl, IReadOnlyDictionary<int, string> byLayoutId)
        {
            ushort langId = (ushort)(hkl & 0xFFFF);
            if (langId == 0)
                return "";

            string primary = "0000" + langId.ToString("X4", CultureInfo.InvariantCulture);
            ushort high = (ushort)(hkl >> 16);
            if (high == 0 || high == langId)
                return primary;

            if ((high & 0xF000) == 0xF000
                && byLayoutId.TryGetValue(high & 0x0FFF, out string? klid)
                && klid.Length == 8
                && klid.EndsWith(langId.ToString("X4", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return klid;
            }

            // An IME (0xE0xx) or a device handle we cannot place: the language's primary layout is the
            // honest answer - it is the language the marker is about to draw either way.
            return primary;
        }

        /// <summary>
        /// <c>Layout Id</c> → KLID for every layout the machine has, read once and then only when a
        /// lookup misses (see <see cref="KlidForHkl"/>). This is the machine's own layout store, not the
        /// user's input list, so installing or removing a keyboard on the "Языки Windows" tab does not
        /// change it.
        /// </summary>
        private static IReadOnlyDictionary<int, string> LayoutIdMap()
        {
            lock (Gate)
            {
                if (_byLayoutId != null)
                    return _byLayoutId;

                var map = new Dictionary<int, string>();
                try
                {
                    using RegistryKey? root = Registry.LocalMachine.OpenSubKey(LayoutsPath);
                    if (root != null)
                    {
                        foreach (string klid in root.GetSubKeyNames())
                        {
                            using RegistryKey? key = root.OpenSubKey(klid);
                            string? id = key?.GetValue("Layout Id") as string;
                            if (id != null
                                && int.TryParse(id.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                                && !map.ContainsKey(value))
                            {
                                map[value] = klid;
                            }
                        }
                    }
                }
                catch
                {
                    // A readable registry is not something the indicator may depend on: an empty map
                    // simply means every layout reads as its language's primary one.
                }

                _byLayoutId = map;
                _readMs = Clock.ElapsedMilliseconds;
                return map;
            }
        }
    }
}
