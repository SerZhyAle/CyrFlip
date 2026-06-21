using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class CaseFlipEngineTests
    {
        [Theory]
        [InlineData("hello", "HELLO")]          // lower → UPPER
        [InlineData("WORLD", "world")]          // UPPER → lower
        [InlineData("Hello", "hELLO")]          // mixed flips per-character
        [InlineData("hELLO wORLD", "Hello World")] // the accidental-CapsLock fix
        public void InvertsLatinCase(string input, string expected)
        {
            Assert.Equal(expected, CaseFlipEngine.Flip(input));
        }

        [Theory]
        [InlineData("привет", "ПРИВЕТ")]        // lower → UPPER (Cyrillic)
        [InlineData("ПРИВЕТ", "привет")]        // UPPER → lower (Cyrillic)
        [InlineData("пРИВЕТ мИР", "Привет Мир")] // CapsLock fix, Cyrillic
        public void InvertsCyrillicCase(string input, string expected)
        {
            Assert.Equal(expected, CaseFlipEngine.Flip(input));
        }

        [Theory]
        [InlineData("Hello, World! 123", "hELLO, wORLD! 123")] // punctuation/digits/space pass through
        [InlineData("a1b2c3", "A1B2C3")]
        [InlineData("---", "---")]              // no cased letters at all
        public void PassesThroughUncasedCharacters(string input, string expected)
        {
            Assert.Equal(expected, CaseFlipEngine.Flip(input));
        }

        [Theory]
        [InlineData("Hello World")]
        [InlineData("ПрИвЕт МиР")]
        [InlineData("MixedCase123!?")]
        public void IsItsOwnInverse(string input)
        {
            // Flipping twice returns the original string exactly.
            Assert.Equal(input, CaseFlipEngine.Flip(CaseFlipEngine.Flip(input)));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void HandlesEmptyAndNull(string? input)
        {
            Assert.Equal(string.Empty, CaseFlipEngine.Flip(input));
        }
    }
}
