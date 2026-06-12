using System;
using System.Collections.Generic;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Bidirectional QWERTY ↔ ЙЦУКЕН transliteration, based on the standard
    /// Russian ЙЦУКЕН layout (see the spec, §5.1) and all punctuation keys.
    ///
    /// The engine detects the dominant layout of the entire selection block
    /// (by counting Latin vs Cyrillic characters) and transliterates all characters
    /// in that direction. This resolves ambiguities for shared punctuation symbols
    /// (like semicolon, period, comma, colon, etc.) that exist on different physical
    /// keys in each layout. Case is preserved.
    /// </summary>
    public static class TransliterationEngine
    {
        // Unshifted mappings for the 34 key positions (including letters and base punctuation).
        private const string LatinUnshifted = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`";
        private const string CyrillicUnshifted = "йцукенгшщзхъфывапролджэячсмитьбю.ё";

        // Shifted/extra symbols mapping.
        private static readonly Dictionary<char, char> EnToRuExtra = new Dictionary<char, char>
        {
            { '{', 'Х' },
            { '}', 'Ъ' },
            { ':', 'Ж' },
            { '"', 'Э' },
            { '<', 'Б' },
            { '>', 'Ю' },
            { '?', ',' },
            { '~', 'Ё' },
            { '@', '"' },
            { '#', '№' },
            { '$', ';' },
            { '^', ':' },
            { '&', '?' },
            { '|', '/' }
        };

        private static readonly Dictionary<char, char> EnToRu = new Dictionary<char, char>();
        private static readonly Dictionary<char, char> RuToEn = new Dictionary<char, char>();

        static TransliterationEngine()
        {
            if (LatinUnshifted.Length != CyrillicUnshifted.Length)
                throw new InvalidOperationException(
                    $"Transliteration rows misaligned: {LatinUnshifted.Length} Latin vs {CyrillicUnshifted.Length} Cyrillic.");

            // Build base mappings (letters and unshifted punctuation)
            for (int i = 0; i < LatinUnshifted.Length; i++)
            {
                char en = LatinUnshifted[i];
                char ru = CyrillicUnshifted[i];

                EnToRu[en] = ru;
                RuToEn[ru] = en;

                // Map uppercase/shifted versions of letters specifically
                if (char.IsLetter(en))
                {
                    char enUpper = char.ToUpperInvariant(en);
                    char ruUpper = char.ToUpperInvariant(ru);
                    EnToRu[enUpper] = ruUpper;
                    RuToEn[ruUpper] = enUpper;
                }
            }

            // Build shifted/extra symbol mappings
            foreach (var kvp in EnToRuExtra)
            {
                EnToRu[kvp.Key] = kvp.Value;
                RuToEn[kvp.Value] = kvp.Key;
            }
        }

        /// <summary>
        /// Transliterate <paramref name="input"/>, auto-detecting the dominant layout
        /// direction of the entire text block and preserving case.
        /// </summary>
        public static string Transliterate(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            bool isCyrillic = IsCyrillicDominant(input!);
            var sb = new StringBuilder(input!.Length);

            foreach (char c in input)
            {
                if (isCyrillic)
                {
                    sb.Append(RuToEn.TryGetValue(c, out char en) ? en : c);
                }
                else
                {
                    sb.Append(EnToRu.TryGetValue(c, out char ru) ? ru : c);
                }
            }

            return sb.ToString();
        }

        /// <summary>Map a single character. Used chiefly as a fallback or helper.</summary>
        public static char MapChar(char c)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                return RuToEn.TryGetValue(c, out char en) ? en : c;
            }
            else
            {
                return EnToRu.TryGetValue(c, out char ru) ? ru : c;
            }
        }

        /// <summary>Determines if Cyrillic letters are dominant in the given text.</summary>
        private static bool IsCyrillicDominant(string text)
        {
            int cyrillicCount = 0;
            int latinCount = 0;

            foreach (char c in text)
            {
                if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
                {
                    cyrillicCount++;
                }
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    latinCount++;
                }
            }

            if (cyrillicCount == 0 && latinCount == 0)
            {
                // Fallback: search for uniquely mapped symbols
                foreach (char c in text)
                {
                    if (RuToEn.ContainsKey(c) && !EnToRu.ContainsKey(c))
                        cyrillicCount++;
                    else if (EnToRu.ContainsKey(c) && !RuToEn.ContainsKey(c))
                        latinCount++;
                }
            }

            return cyrillicCount >= latinCount;
        }
    }
}
