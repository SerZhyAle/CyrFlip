using System.Collections.Generic;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Bidirectional QWERTY ↔ ЙЦУКЕН transliteration, based on the standard
    /// Russian ЙЦУКЕН layout (see the spec, §5.1).
    ///
    /// Direction is auto-detected per character: a Latin key maps to its Cyrillic
    /// counterpart and vice-versa, so a single pass corrects text in either
    /// direction and handles mixed input. Characters with no mapping
    /// (digits, punctuation, whitespace) pass through unchanged. Case is preserved.
    /// </summary>
    public static class TransliterationEngine
    {
        // The 26 QWERTY letter keys and their ЙЦУКЕН counterparts, aligned 1:1.
        //
        // NOTE: the spec's §5.1 table lists 28 Cyrillic letters for these 26 keys —
        // it includes ж and э, which on ЙЦУКЕН live on the ';' and '\'' keys, not on
        // letter keys. They are dropped here so the rows stay aligned (z→я … m→ь).
        // Punctuation keys pass through unchanged; the full ЙЦУКЕН punctuation row
        // (х ъ ж э б ю ё …) can be added later if desired.
        private const string Latin = "qwertyuiopasdfghjklzxcvbnm";
        private const string Cyrillic = "йцукенгшщзфывапролдячсмить";

        private static readonly Dictionary<char, char> EnToRu = BuildEnToRu();
        private static readonly Dictionary<char, char> RuToEn = BuildRuToEn();

        static TransliterationEngine()
        {
            // Guard against the rows drifting out of alignment again.
            if (Latin.Length != Cyrillic.Length)
                throw new System.InvalidOperationException(
                    $"Transliteration rows misaligned: {Latin.Length} Latin vs {Cyrillic.Length} Cyrillic.");
        }

        private static Dictionary<char, char> BuildEnToRu()
        {
            var map = new Dictionary<char, char>(Latin.Length);
            for (int i = 0; i < Latin.Length; i++)
                map[Latin[i]] = Cyrillic[i];
            return map;
        }

        private static Dictionary<char, char> BuildRuToEn()
        {
            var map = new Dictionary<char, char>(Cyrillic.Length);
            for (int i = 0; i < Cyrillic.Length; i++)
                map[Cyrillic[i]] = Latin[i];
            return map;
        }

        /// <summary>
        /// Transliterate <paramref name="input"/>, auto-detecting direction per
        /// character and preserving case. Returns the input unchanged when null
        /// or empty. O(n) over the input length.
        /// </summary>
        public static string Transliterate(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var sb = new StringBuilder(input!.Length);
            foreach (char c in input)
                sb.Append(MapChar(c));
            return sb.ToString();
        }

        /// <summary>Map a single character, preserving case; unmapped chars return unchanged.</summary>
        public static char MapChar(char c)
        {
            char lower = char.ToLowerInvariant(c);
            bool isUpper = c != lower; // c had an uppercase form

            if (EnToRu.TryGetValue(lower, out char ru))
                return isUpper ? char.ToUpperInvariant(ru) : ru;

            if (RuToEn.TryGetValue(lower, out char en))
                return isUpper ? char.ToUpperInvariant(en) : en;

            return c;
        }
    }
}
