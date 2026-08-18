using System;
using System.Collections.Generic;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Bidirectional QWERTY ↔ ЙЦУКЕН transliteration, based on the standard
    /// Russian ЙЦУКЕН layout (see the spec, §5.1) and all punctuation keys.
    ///
    /// Letters (Latin/Cyrillic) are flipped per-character, so mixed-script
    /// text (e.g. ЙЦУRTY → QWEКЕН) is handled correctly. A non-letter whose
    /// key carries a *letter* in the other layout ("," → "б", "[" → "х") is
    /// always converted - nobody types a comma where "б" belongs. A key that
    /// is punctuation on <b>both</b> sides ("/" → ".", "@" → "\"") carries no
    /// such evidence, so converting it is the caller's choice
    /// (<c>convertSymbols</c>, default true = the historic behaviour).
    /// Which direction an ambiguous key takes comes from the dominant script
    /// of the text. Case is preserved; unmapped characters pass through
    /// unchanged.
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
        /// Transliterate <paramref name="input"/>. Letters flip per-character;
        /// ambiguous non-letter symbols use the dominant script direction.
        /// </summary>
        /// <param name="convertSymbols">
        /// Whether a key that is punctuation in <b>both</b> layouts ("/" against ".") is converted.
        /// Default true - the behaviour of every release before the switch existed. False leaves such
        /// a key alone, so "/ghbdtn" comes back as "/привет". Punctuation whose other side is a letter
        /// ("," → "б") is not ambiguous and is converted either way.
        /// </param>
        public static string Transliterate(string? input, bool convertSymbols = true)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            bool cyrillicDominant = IsCyrillicDominant(input!);
            var sb = new StringBuilder(input!.Length);
            foreach (char c in input)
            {
                bool isCyrillicChar = c >= 0x0400 && c <= 0x052F;
                bool isLatinLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

                if (isCyrillicChar || isLatinLetter)
                {
                    // Letters are unambiguous: flip per-character.
                    sb.Append(isCyrillicChar
                        ? (RuToEn.TryGetValue(c, out char en) ? en : c)
                        : (EnToRu.TryGetValue(c, out char ru) ? ru : c));
                }
                else
                {
                    // Punctuation/symbols can appear in both layouts with different
                    // meanings; use dominant direction for context - and only when the
                    // key carries a *letter* on the other side. A symbol that stays a
                    // symbol either way says nothing about which layout the user meant:
                    // "/" is "." on the Russian key, and a slash in front of a word is
                    // far more often deliberate (a command, a path, a date) than a
                    // mistyped full stop. Punctuation that becomes a letter is the
                    // opposite - nobody types "," where "б" belongs.
                    char mapped = cyrillicDominant
                        ? (RuToEn.TryGetValue(c, out char en) ? en : c)
                        : (EnToRu.TryGetValue(c, out char ru) ? ru : c);
                    sb.Append(convertSymbols || char.IsLetter(mapped) ? mapped : c);
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
                    cyrillicCount++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    latinCount++;
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
