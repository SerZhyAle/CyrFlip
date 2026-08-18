using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class TransliterationEngineTests
    {
        [Theory]
        [InlineData("hello", "руддщ")]   // EN → RU
        [InlineData("world", "цщкдв")]
        [InlineData("zxcvbnm", "ячсмить")]
        [InlineData("hello, world!", "руддщб цщкдв!")] // punctuation mapping
        [InlineData("[brackets]", "хикфслуеыъ")]
        [InlineData("[test]; 'hello'", "хеуыеъж эруддщэ")]
        [InlineData("Shift: @#$^&|", "ЫршаеЖ \"№;:?/")]
        public void TransliteratesEnglishToRussian(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Theory]
        [InlineData("руддщ", "hello")]   // RU → EN
        [InlineData("ячсмить", "zxcvbnm")]
        [InlineData("руддщб цщкдв!", "hello, world!")]
        [InlineData("хеуыеъж эруддщэ", "[test]; 'hello'")]
        [InlineData("ЫршаеЖ \"№;:?/", "Shift: @#$^&|")]
        public void TransliteratesRussianToEnglish(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        /// <summary>
        /// The symbol swap is opt-out, and the default is the behaviour every release shipped before
        /// the switch existed: an ambiguous key is converted like everything else.
        /// </summary>
        [Theory]
        [InlineData("/ghbdtn", ".привет")]
        [InlineData("@ghbdtn", "\"привет")]
        public void ConvertsAmbiguousPunctuationByDefault(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
            Assert.Equal(expected, TransliterationEngine.Transliterate(input, convertSymbols: true));
        }

        /// <summary>
        /// With the swap off, a key that is punctuation in *both* layouts is left alone: it carries no
        /// evidence of which layout the user meant, and the slash in front of a word is often one they
        /// typed on purpose - not least from the numpad, which produces the very same character. A key
        /// whose other side is a *letter* is the opposite case and is still converted either way, which
        /// is what keeps "," → "б" and "[" → "х" working.
        /// </summary>
        [Theory]
        [InlineData("/ghbdtn", "/привет")]      // the slash survives, the word is fixed
        [InlineData("@ghbdtn", "@привет")]
        [InlineData("ghbdtn?", "привет?")]
        [InlineData("ghbdtn,", "приветб")]      // "," is the "б" key - still converted
        [InlineData("[ghbdtn]", "хприветъ")]    // brackets are "х"/"ъ" - still converted
        public void LeavesAKeyThatIsPunctuationInBothLayoutsAloneWhenTheSwapIsOff(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input, convertSymbols: false));
        }

        /// <summary>
        /// Mixed text is fixed in one press: each letter goes the way its own script says, so the
        /// Latin half becomes Cyrillic and the Cyrillic half becomes Latin in the same pass.
        /// </summary>
        [Theory]
        [InlineData("ghbdtnпривет", "приветghbdtn")]
        [InlineData("приветghbdtn", "ghbdtnпривет")]
        public void FlipsBothHalvesOfMixedText(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Fact]
        public void PreservesCase()
        {
            Assert.Equal("Рруддщ", TransliterationEngine.Transliterate("Hhello"));
            Assert.Equal("ПЕУ", TransliterationEngine.Transliterate("GTE"));
            Assert.Equal("ЭруддщЭ", TransliterationEngine.Transliterate("\"hello\""));
        }

        [Theory]
        [InlineData("hi!", "рш!")]            // punctuation passes through
        [InlineData("abc 123", "фис 123")]    // digits + whitespace pass through
        public void PassesThroughUnmappedCharacters(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Theory]
        [InlineData("ЙЦУRTY", "QWEКЕН")]   // mixed: each char flips independently
        [InlineData("ЙЦУКЕН", "QWERTY")]   // pure Cyrillic → pure Latin
        public void FlipsEachCharacterIndependently(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void HandlesEmptyAndNull(string? input)
        {
            Assert.Equal(string.Empty, TransliterationEngine.Transliterate(input));
        }

        [Fact]
        public void IsBijectiveOverTheFullAlphabet()
        {
            const string qwerty = "qwertyuiopasdfghjklzxcvbnm";
            string once = TransliterationEngine.Transliterate(qwerty);
            // EN→RU then RU→EN round-trips back to the original.
            Assert.Equal(qwerty, TransliterationEngine.Transliterate(once));
            // And every key actually changed (no accidental pass-through).
            Assert.DoesNotContain(once, c => qwerty.IndexOf(c) >= 0);
        }
    }
}
