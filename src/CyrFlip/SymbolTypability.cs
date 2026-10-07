using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// Information on how a symbol can be typed using installed keyboard layouts (ticket S0056 section 7).
    /// </summary>
    internal sealed class TypabilityInfo
    {
        public bool IsTypable { get; }
        public string? Chord { get; }
        public string? LayoutName { get; }

        public TypabilityInfo(bool isTypable, string? chord = null, string? layoutName = null)
        {
            IsTypable = isTypable;
            Chord = chord;
            LayoutName = layoutName;
        }

        public string GetHint(string uiLanguage)
        {
            if (IsTypable && !string.IsNullOrEmpty(Chord))
            {
                string layout = !string.IsNullOrEmpty(LayoutName) ? LayoutName! : "";
                return Localization.Format(uiLanguage, "Набирается: {0} ({1})", Chord, layout);
            }
            return Localization.Translate(uiLanguage, "Нет на ваших раскладках");
        }
    }

    /// <summary>
    /// Computes and caches per-character typability across installed keyboard layouts.
    /// Thread-safe and background-pool friendly.
    /// </summary>
    internal sealed class SymbolTypability
    {
        private readonly Func<IntPtr[]> _getLayouts;
        private readonly Func<char, IntPtr, short> _vkScan;
        private readonly Func<IntPtr, string> _getLayoutName;

        private readonly object _lock = new object();
        private IntPtr[] _cachedLayouts = Array.Empty<IntPtr>();
        private readonly Dictionary<string, TypabilityInfo> _cache = new Dictionary<string, TypabilityInfo>(StringComparer.Ordinal);

        public SymbolTypability(
            Func<IntPtr[]>? getLayouts = null,
            Func<char, IntPtr, short>? vkScan = null,
            Func<IntPtr, string>? getLayoutName = null)
        {
            _getLayouts = getLayouts ?? KeyboardLayoutConverter.InstalledLayouts;
            _vkScan = vkScan ?? WindowInterop.VkKeyScanEx;
            _getLayoutName = getLayoutName ?? ResolveDefaultLayoutName;
        }

        public bool IsTypable(SymbolEntry entry)
        {
            return Query(entry).IsTypable;
        }

        public TypabilityInfo Query(SymbolEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Text))
                return new TypabilityInfo(false);

            IntPtr[] layouts = _getLayouts() ?? Array.Empty<IntPtr>();

            lock (_lock)
            {
                if (!AreLayoutsEqual(_cachedLayouts, layouts))
                {
                    _cachedLayouts = (IntPtr[])layouts.Clone();
                    _cache.Clear();
                }

                if (_cache.TryGetValue(entry.Text, out TypabilityInfo? cached))
                    return cached;
            }

            TypabilityInfo computed = Compute(entry.Text, layouts);

            lock (_lock)
            {
                _cache[entry.Text] = computed;
            }

            return computed;
        }

        private TypabilityInfo Compute(string text, IntPtr[] layouts)
        {
            if (text.Length != 1)
            {
                // Surrogate pairs or combining sequences cannot be produced by a single key stroke.
                return new TypabilityInfo(false);
            }

            char c = text[0];

            foreach (IntPtr hkl in layouts)
            {
                short encoded = _vkScan(c, hkl);
                if (encoded == -1) continue;

                byte vk = (byte)(encoded & 0xFF);
                byte modifiers = (byte)((encoded >> 8) & 0xFF);

                string? chord = FormatChord(vk, modifiers);
                if (chord != null)
                {
                    string layoutName = _getLayoutName(hkl);
                    return new TypabilityInfo(true, chord, layoutName);
                }
            }

            return new TypabilityInfo(false);
        }

        private static string? FormatChord(byte vk, byte modifiers)
        {
            string keyName = Hotkey.NameForVk(vk);
            if (string.IsNullOrEmpty(keyName)) return null;

            switch (modifiers)
            {
                case 0:
                    return keyName;
                case 1:
                    return "Shift+" + keyName;
                case 6:
                    return "AltGr+" + keyName;
                case 7:
                    return "AltGr+Shift+" + keyName;
                case 2:
                    return "Ctrl+" + keyName;
                case 3:
                    return "Ctrl+Shift+" + keyName;
                case 4:
                    return "Alt+" + keyName;
                case 5:
                    return "Alt+Shift+" + keyName;
                default:
                    return null;
            }
        }

        private static bool AreLayoutsEqual(IntPtr[] a, IntPtr[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static string ResolveDefaultLayoutName(IntPtr hkl)
        {
            try
            {
                string klid = LayoutIdentity.KlidForHkl(hkl);
                if (!string.IsNullOrEmpty(klid))
                {
                    string name = InputLayouts.DisplayNameFor(klid);
                    if (!string.IsNullOrEmpty(name) && !name.Equals(klid, StringComparison.OrdinalIgnoreCase))
                        return name;
                }
                return LanguageHotkeys.LanguageName(hkl);
            }
            catch
            {
                return "Layout";
            }
        }
    }
}
