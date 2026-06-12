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
