using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The conversion the seeded US ⇄ Russian row actually runs. These cases are deliberately chosen
    /// to hold on both paths: on a machine with the Russian keyboard installed they go through
    /// <see cref="KeyboardLayoutConverter"/>'s live layout lookup, and on one without it (a clean CI
    /// runner) through the <see cref="TransliterationEngine"/> fallback. A case whose two paths
    /// disagreed would belong in a manual check, not here.
    /// </summary>
    public class KeyboardLayoutConverterTests
    {
        private const string Us = "00000409";
        private const string Ru = "00000419";

        /// <summary>
        /// Direction is decided per character, not per press: a character the source layout cannot
        /// produce at all was typed the other way round and is converted back. Before this, half of a
        /// mixed string was converted and the other half was copied through unchanged - "ghbdtnпривет"
        /// came back as "приветпривет".
        /// </summary>
        [Theory]
        [InlineData("ghbdtnпривет", "приветghbdtn")]
        [InlineData("приветghbdtn", "ghbdtnпривет")]
        public void ConvertsEachCharacterInItsOwnDirection(string input, string expected)
        {
            Assert.Equal(expected, KeyboardLayoutConverter.Convert(input, Us, Ru));
        }

        /// <summary>The pair is symmetric, so naming it the other way round changes nothing.</summary>
        [Fact]
        public void MixedTextConvertsTheSameWhicheverWayThePairIsNamed()
        {
            Assert.Equal(KeyboardLayoutConverter.Convert("ghbdtnпривет", Us, Ru),
                         KeyboardLayoutConverter.Convert("ghbdtnпривет", Ru, Us));
        }

        /// <summary>The symbol swap defaults to on, i.e. to what every earlier release did.</summary>
        [Fact]
        public void ConvertsAmbiguousPunctuationByDefault()
        {
            Assert.Equal(".привет", KeyboardLayoutConverter.Convert("/ghbdtn", Us, Ru));
            Assert.Equal(".привет", KeyboardLayoutConverter.Convert("/ghbdtn", Us, Ru, convertSymbols: true));
        }

        /// <summary>
        /// With the swap off, a key that is punctuation on both sides is ambiguous and is left alone;
        /// one whose other side is a letter is not, and is still converted.
        /// </summary>
        [Theory]
        [InlineData("/ghbdtn", "/привет")]
        [InlineData("@ghbdtn", "@привет")]
        [InlineData("ghbdtn?", "привет?")]
        [InlineData("ghbdtn,", "приветб")]
        [InlineData("[ghbdtn]", "хприветъ")]
        public void LeavesAKeyThatIsPunctuationInBothLayoutsAloneWhenTheSwapIsOff(string input, string expected)
        {
            Assert.Equal(expected, KeyboardLayoutConverter.Convert(input, Us, Ru, convertSymbols: false));
        }

        /// <summary>Digits and whitespace sit on the same key in both layouts, so they never move.</summary>
        [Fact]
        public void DigitsAndWhitespacePassThrough()
        {
            Assert.Equal("привет 2026", KeyboardLayoutConverter.Convert("ghbdtn 2026", Us, Ru));
        }
    }
}
