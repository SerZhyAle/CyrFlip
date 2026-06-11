using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class TransliterationEngineTests
    {
        [Theory]
        [InlineData("hello", "руддщ")]   // EN → RU
        [InlineData("world", "цщкдв")]
        [InlineData("zxcvbnm", "ячсмить")] // the keys that diverge from the spec's mis-aligned table
        public void TransliteratesEnglishToRussian(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Theory]
        [InlineData("руддщ", "hello")]   // RU → EN
        [InlineData("ячсмить", "zxcvbnm")]
        public void TransliteratesRussianToEnglish(string input, string expected)
        {
            Assert.Equal(expected, TransliterationEngine.Transliterate(input));
        }

        [Fact]
        public void PreservesCase()
        {
            Assert.Equal("Руддщ", TransliterationEngine.Transliterate("Hello"));
            Assert.Equal("ПЕУ", TransliterationEngine.Transliterate("GTE"));
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
