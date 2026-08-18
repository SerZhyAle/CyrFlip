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

        [Theory]
        [InlineData("Hello World", false)]      // the CapsLock fix: carry on in normal case
        [InlineData("HELLO WORLD", true)]       // the user wants capitals from here on
        [InlineData("Привет Мир", false)]
        [InlineData("ПРИВЕТ МИР", true)]
        [InlineData("Hello World!", false)]     // trailing punctuation is not a letter - look past it
        [InlineData("HELLO, WORLD! 123 ...", true)]
        [InlineData("a", false)]
        [InlineData("A", true)]
        public void DesiredCapsLockFollowsTheLastCasedLetter(string text, bool expected)
        {
            Assert.Equal(expected, CaseFlipEngine.DesiredCapsLock(text));
        }

        [Theory]
        [InlineData("123 !?")]                  // nothing cased to judge by
        [InlineData("日本語")]                   // a caseless script says nothing either
        [InlineData("")]
        [InlineData(null)]
        public void DesiredCapsLockLeavesTheKeyAloneWithoutACasedLetter(string? text)
        {
            Assert.Null(CaseFlipEngine.DesiredCapsLock(text));
        }

        [Theory]
        [InlineData("hELLO wORLD")]
        [InlineData("HELLO world")]
        [InlineData("Hello WORLD")]
        public void DesiredCapsLockIsAboutTheFlippedTextNotTheOriginal(string typed)
        {
            // What the key should end up as is read off the corrected text, so the answer is the
            // same however many times the correction is applied - a blind toggle was not.
            string corrected = CaseFlipEngine.Flip(typed);
            bool? once = CaseFlipEngine.DesiredCapsLock(corrected);
            bool? twice = CaseFlipEngine.DesiredCapsLock(CaseFlipEngine.Flip(CaseFlipEngine.Flip(corrected)));
            Assert.NotNull(once);
            Assert.Equal(once, twice);
        }
    }
}
