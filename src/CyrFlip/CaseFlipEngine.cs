using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Inverts the letter case of text per-character: UPPER → lower and lower → UPPER.
    /// The companion to <see cref="TransliterationEngine"/> for the second hotkey.
    ///
    /// Purpose: undo an accidental CapsLock. Typing with CapsLock stuck on turns
    /// "Hello World" into "hELLO wORLD" (Shift inverts again), so a straight
    /// per-character case flip restores the intended text. Works for Latin and
    /// Cyrillic (and any cased script); digits, punctuation and whitespace pass
    /// through unchanged. Case mapping is invariant-culture, so it never trips on
    /// locale quirks (e.g. the Turkish dotless-i). O(n).
    /// </summary>
    public static class CaseFlipEngine
    {
        /// <summary>
        /// Return <paramref name="input"/> with every cased letter's case inverted.
        /// Null/empty returns <see cref="string.Empty"/>.
        /// </summary>
        public static string Flip(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var sb = new StringBuilder(input!.Length);
            foreach (char c in input)
            {
                if (char.IsUpper(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (char.IsLower(c))
                    sb.Append(char.ToUpperInvariant(c));
                else
                    sb.Append(c); // digits, punctuation, whitespace, caseless letters
            }
            return sb.ToString();
        }
    }
}
