using System;
using System.Collections.Generic;
using System.Globalization;

namespace CyrFlip
{
    /// <summary>One installed keyboard layout as the command line names it (ticket S0109).</summary>
    internal sealed class LayoutEntry
    {
        private string? _label;
        private readonly Func<string>? _labelOf;

        public LayoutEntry(IntPtr hkl, string klid, string code, string label)
        {
            Hkl = hkl; Klid = klid; Code = code; _label = label;
        }

        /// <summary>An entry whose label is looked up when it is first read - it costs a registry read and a resource load.</summary>
        public LayoutEntry(IntPtr hkl, string klid, string code, Func<string> labelOf)
        {
            Hkl = hkl; Klid = klid; Code = code; _labelOf = labelOf;
        }

        /// <summary>The live handle, the identity a window's layout is compared by.</summary>
        public IntPtr Hkl { get; }
        /// <summary>Eight upper-case hexadecimal digits.</summary>
        public string Klid { get; }
        /// <summary>The code the marker draws (<see cref="LayoutIdentity.CodeFor"/>): two letters, or the language's hex id.</summary>
        public string Code { get; }
        /// <summary>Windows' own name of the layout; empty for the layout of a window, which does not need it.</summary>
        public string Label => _label ??= _labelOf != null ? _labelOf() : "";
    }

    internal enum LayoutValueKind
    {
        /// <summary>Not a layout name at all: names nothing.</summary>
        Invalid,
        /// <summary>The word <c>next</c>, the tray click's rotation.</summary>
        Next,
        /// <summary>Eight hexadecimal digits: exactly one layout.</summary>
        Klid,
        /// <summary>Two to four letters or digits: a language, and so possibly several layouts.</summary>
        Code,
    }

    /// <summary>
    /// What a person typed after <c>--to</c>, <c>--from</c> (ticket S0109, Assumed 3). The grammar is ASCII only,
    /// case-insensitive and trims nothing: a value with a space in it is not a layout name. <see cref="Text"/> is the
    /// normal form (upper case) for a KLID or a code.
    /// </summary>
    internal readonly struct LayoutValue
    {
        private LayoutValue(LayoutValueKind kind, string text)
        {
            Kind = kind; Text = text;
        }
        public LayoutValueKind Kind { get; }
        public string Text { get; }

        public static LayoutValue Parse(string? value)
        {
            if (value == null || value.Length == 0) return new LayoutValue(LayoutValueKind.Invalid, "");
            if (string.Equals(value, "next", StringComparison.OrdinalIgnoreCase)) return new LayoutValue(LayoutValueKind.Next, "NEXT");
            bool allHex = true;
            foreach (char c in value)
            {
                if (!IsAsciiLetterOrDigit(c)) return new LayoutValue(LayoutValueKind.Invalid, "");
                if (!IsHex(c)) allHex = false;
            }
            string upper = value.ToUpperInvariant();
            if (value.Length == 8 && allHex) return new LayoutValue(LayoutValueKind.Klid, upper);
            if (value.Length >= 2 && value.Length <= 4) return new LayoutValue(LayoutValueKind.Code, upper);
            return new LayoutValue(LayoutValueKind.Invalid, "");
        }

        private static bool IsAsciiLetterOrDigit(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        private static bool IsHex(char c)
            => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    /// <summary>
    /// The layouts a command can name, over a list handed in: no Win32, no registry, no configuration, so the whole
    /// matrix of "which layout does this value mean" is a unit test on any machine. A code takes the first layout
    /// of that code in the list's order (Windows' order); a KLID is exact.
    /// </summary>
    internal sealed class LayoutCatalog
    {
        private readonly IReadOnlyList<LayoutEntry> _entries;
        private static HashSet<string>? s_cultureNames;

        public LayoutCatalog(IReadOnlyList<LayoutEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<LayoutEntry> Entries => _entries;

        public LayoutEntry? FindByKlid(string klid)
        {
            foreach (LayoutEntry entry in _entries)
                if (string.Equals(entry.Klid, klid, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>The first layout of the code in list order, so a person whose first English layout is Dvorak gets Dvorak.</summary>
        public LayoutEntry? FindByCode(string code)
        {
            foreach (LayoutEntry entry in _entries)
                if (string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>The installed layout a <see cref="LayoutValueKind.Klid"/> or <see cref="LayoutValueKind.Code"/> value means; null when none.</summary>
        public LayoutEntry? Find(LayoutValue value)
        {
            switch (value.Kind)
            {
                case LayoutValueKind.Klid: return FindByKlid(value.Text);
                case LayoutValueKind.Code: return FindByCode(value.Text);
                default: return null;
            }
        }

        /// <summary>
        /// The first KLID of a curated language (<see cref="WorldLayouts.Popular"/>, then <see cref="WorldLayouts.ColouredOnly"/>)
        /// whose code matches - what lets <c>text convert --to ru</c> reach the built-in table on a PC without the Russian
        /// keyboard. Null when no curated language has that code.
        /// </summary>
        public static string? CuratedPrimaryKlid(string code)
        {
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
                if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase) && language.Klids.Length > 0)
                    return language.Klids[0];
            foreach (WorldLayouts.Recommended language in WorldLayouts.ColouredOnly)
                if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase) && language.Klids.Length > 0)
                    return language.Klids[0];
            return null;
        }

        /// <summary>
        /// Whether a well-formed value is a layout somebody could have: a KLID whose language id Windows can name, or a
        /// code that an installed layout carries, a curated language has, or a Windows culture is called (the two- or
        /// three-letter ISO name, without regard to case). A value that fails this <b>names nothing</b> (exit 10); one
        /// that passes but is not installed is <b>named but absent</b> (11 for a switch, 10 for a conversion).
        /// </summary>
        public bool NamesSomething(LayoutValue value)
        {
            switch (value.Kind)
            {
                case LayoutValueKind.Klid:
                    return LanguageIdNamed(value.Text);
                case LayoutValueKind.Code:
                    return FindByCode(value.Text) != null || CuratedPrimaryKlid(value.Text) != null || CultureNamed(value.Text);
                default:
                    return false;
            }
        }

        private static bool LanguageIdNamed(string klid)
        {
            if (!int.TryParse(klid.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int langId) || langId == 0)
                return false;
            // CodeForLangId answers the four hex digits for a language Windows cannot name.
            return WorldLayouts.CodeForLangId(langId) != langId.ToString("X4", CultureInfo.InvariantCulture);
        }

        private static bool CultureNamed(string code)
        {
            HashSet<string>? names = s_cultureNames;
            if (names == null)
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures))
                {
                    if (culture.TwoLetterISOLanguageName.Length >= 2) names.Add(culture.TwoLetterISOLanguageName);
                    if (culture.ThreeLetterISOLanguageName.Length >= 2) names.Add(culture.ThreeLetterISOLanguageName);
                }
                s_cultureNames = names;
            }
            return names.Contains(code);
        }
    }
}
