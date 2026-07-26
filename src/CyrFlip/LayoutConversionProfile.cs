using System;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>A user-defined physical-key conversion from one Windows keyboard layout to another.</summary>
    internal sealed class LayoutConversionProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceKlid { get; set; } = "";
        public string TargetKlid { get; set; } = "";
        public string Hotkey { get; set; } = "";
        public bool Enabled { get; set; } = true;

        [ScriptIgnore]
        public bool IsUsable => SourceKlid.Length == 8 && TargetKlid.Length == 8 && Hotkey.Length > 0;

        public LayoutConversionProfile Clone() => new LayoutConversionProfile
        {
            Id = Id, SourceKlid = SourceKlid, TargetKlid = TargetKlid, Hotkey = Hotkey, Enabled = Enabled,
        };
    }

    /// <summary>
    /// Curated, broadly-used Windows keyboard layouts (the world's ten most-spoken languages plus German,
    /// Italian and Ukrainian). The list is additive: users can still pick any Windows layout.
    /// </summary>
    internal static class WorldLayouts
    {
        internal sealed class Recommended
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string[] Klids { get; set; } = new string[0];
        }

        // The ten largest languages by total speakers, plus German and Italian (asked for explicitly:
        // both are everyday CyrFlip pairs in Europe and neither makes the world top ten), plus
        // Ukrainian - outside the world ten, but one of CyrFlip's own three headline layouts, so the
        // "add popular languages" button would look broken without it.  Each KLID is
        // a Microsoft-shipped *keyboard layout*, present on every Windows install - nothing is
        // downloaded.  Variants are listed where they are common rather than pretending a language has
        // exactly one keyboard.
        //
        // Note on CJK: 00000804 / 00000404 are the Chinese input *entries* (with a US keyboard), not
        // the Pinyin/Bopomofo IMEs - those are E0xx handles that ride on a display-language pack and
        // are Windows' job to install. Composed IME text is left alone by the converter anyway.
        public static readonly Recommended[] Popular =
        {
            new Recommended { Code = "EN", Name = "English", Klids = new[] { "00000409", "00020409", "00010409" } }, // US, US-International, Dvorak
            new Recommended { Code = "ZH", Name = "Chinese", Klids = new[] { "00000804", "00000404" } }, // Simplified (zh-CN), Traditional (zh-TW)
            new Recommended { Code = "HI", Name = "Hindi", Klids = new[] { "00000439", "00010439" } }, // Devanagari-INSCRIPT, Hindi Traditional
            new Recommended { Code = "ES", Name = "Spanish", Klids = new[] { "0000040A", "0000080A" } }, // Spanish, Latin American
            new Recommended { Code = "FR", Name = "French", Klids = new[] { "0000040C", "00001009" } }, // French (AZERTY), Canadian French
            new Recommended { Code = "AR", Name = "Arabic", Klids = new[] { "00000401", "00020401" } }, // Arabic (101), Arabic (102) AZERTY
            new Recommended { Code = "BN", Name = "Bengali", Klids = new[] { "00000445" } }, // Bangla - INSCRIPT
            new Recommended { Code = "PT", Name = "Portuguese", Klids = new[] { "00000416", "00000816" } }, // Brazilian ABNT, Portuguese
            new Recommended { Code = "RU", Name = "Russian", Klids = new[] { "00000419", "00010419" } }, // standard, typewriter
            new Recommended { Code = "UR", Name = "Urdu", Klids = new[] { "00000420" } },
            new Recommended { Code = "DE", Name = "German", Klids = new[] { "00000407", "00010407" } }, // German (QWERTZ), German (IBM)
            new Recommended { Code = "IT", Name = "Italian", Klids = new[] { "00000410", "00010410" } }, // Italian, Italian (142)
            new Recommended { Code = "UK", Name = "Ukrainian", Klids = new[] { "00000422", "00020422" } }, // Ukrainian, Ukrainian (Enhanced)
        };

        public static string LabelForKlid(string klid)
        {
            foreach (Recommended language in Popular)
                foreach (string candidate in language.Klids)
                    if (string.Equals(candidate, klid, StringComparison.OrdinalIgnoreCase)) return language.Code;
            return "";
        }

        /// <summary>
        /// The two-letter code shown for a layout, in the same shape the live indicator uses: the
        /// curated code when we have one, otherwise the language id decoded through the OS (so a
        /// layout outside the curated ten still reads as e.g. "PL", never as a raw KLID).
        /// </summary>
        public static string CodeForKlid(string? klid)
        {
            if (string.IsNullOrEmpty(klid)) return "";
            string curated = LabelForKlid(klid!);
            if (curated.Length > 0) return curated;

            // A KLID's low 4 hex digits are its language id (see InputLayouts).
            if (!int.TryParse(klid!.Substring(klid.Length - 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int langId) || langId == 0)
                return klid!;
            try { return new CultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant(); }
            catch (CultureNotFoundException) { return klid!; }
        }
    }
}
