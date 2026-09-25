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
    ///
    /// It is its own inverse for every character it changes: a letter whose case
    /// partner does not map back to it is left alone (see <see cref="Flip"/>).
    /// </summary>
    public static class CaseFlipEngine
    {
        /// <summary>
        /// Return <paramref name="input"/> with every cased letter's case inverted.
        /// Null/empty returns <see cref="string.Empty"/>.
        ///
        /// <para>A letter is flipped only when its case partner flips back to it (ticket S0009,
        /// FP-10). A handful do not: the micro sign µ goes up to the Greek capital Mu and comes back
        /// as a Greek μ, the dotless ı and the long ſ come back as i and s, the final ς as σ, and the
        /// Kelvin sign K as a plain K. Flipping those twice changed the text, which a "fix CapsLock"
        /// correction applied twice must never do - so they stay as they are.</para>
        /// </summary>
        public static string Flip(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var sb = new StringBuilder(input!.Length);
            foreach (char c in input)
            {
                if (char.IsUpper(c))
                {
                    char lower = char.ToLowerInvariant(c);
                    sb.Append(char.ToUpperInvariant(lower) == c ? lower : c);
                }
                else if (char.IsLower(c))
                {
                    char upper = char.ToUpperInvariant(c);
                    sb.Append(char.ToLowerInvariant(upper) == c ? upper : c);
                }
                else
                    sb.Append(c); // digits, punctuation, whitespace, caseless letters
            }
            return sb.ToString();
        }

        /// <summary>
        /// The CapsLock state the corrected text asks for: <c>true</c> when it ends in an upper-case
        /// letter (the user is typing in capitals and should carry on doing so), <c>false</c> when it
        /// ends in a lower-case one, and <c>null</c> when there is no cased letter to judge by - a
        /// selection of digits or punctuation says nothing about CapsLock, so it is left alone.
        ///
        /// <para>This is the case-flip counterpart of switching the input layout after a conversion,
        /// and it is deliberately an <b>absolute</b> answer rather than "toggle it": a blind toggle
        /// is only right while CapsLock still holds the state that produced the wrong text, so
        /// correcting the same text twice, or after the user has already pressed CapsLock by hand,
        /// left the key exactly backwards.</para>
        ///
        /// <para>The <b>last</b> cased letter decides because that is where typing continues.</para>
        /// </summary>
        public static bool? DesiredCapsLock(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            for (int i = text!.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (char.IsUpper(c)) return true;
                if (char.IsLower(c)) return false;
            }
            return null;
        }
    }
}
