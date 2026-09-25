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

        /// <summary>
        /// One rule for a key that is punctuation on both sides (ticket S0009, FP-9): the pair's own
        /// direction first, per character. The fallback used to follow the text's dominant script
        /// instead, so "привет?" came back as "ghbdtn&amp;" without the Russian keyboard and as
        /// "ghbdtn," with it. These hold on both paths now, which is the rule of this class.
        /// </summary>
        [Theory]
        [InlineData("привет?", Us, Ru, "ghbdtn,")]
        [InlineData("привет?", Ru, Us, "ghbdtn&")]
        [InlineData("ghbdtn&", Us, Ru, "привет?")]
        [InlineData("ghbdtn;", Us, Ru, "приветж")]
        [InlineData("руддщ;", Ru, Us, "hello$")]
        public void AmbiguousPunctuationFollowsThePairsDirectionOnBothPaths(string input, string from, string to, string expected)
        {
            Assert.Equal(expected, KeyboardLayoutConverter.Convert(input, from, to));
        }

        /// <summary>
        /// The memo (FP-6) is exact: each character's answer never depends on its neighbours, so a
        /// random corpus converts the same with and without it.
        /// </summary>
        [Fact]
        public void TheMemoChangesNothingButTheCost()
        {
            const string pool = "qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM" +
                                "йцукенгшщзхъфывапролджэячсмитьбюёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮЁ" +
                                "0123456789 .,;:'\"[]{}<>/?!@#$%^&*()-_=+\\|`~№\t\r\n";
            var random = new System.Random(9009);
            for (int round = 0; round < 20; round++)
            {
                var text = new System.Text.StringBuilder();
                int length = random.Next(1, 400);
                for (int i = 0; i < length; i++) text.Append(pool[random.Next(pool.Length)]);
                string input = text.ToString();

                foreach (bool convertSymbols in new[] { true, false })
                    Assert.Equal(KeyboardLayoutConverter.Convert(input, Us, Ru, convertSymbols, memoize: false),
                                 KeyboardLayoutConverter.Convert(input, Us, Ru, convertSymbols, memoize: true));
            }
        }

        /// <summary>A million characters - the flip's own cap - convert in well under a second.</summary>
        [Fact]
        public void AMillionCharactersConvertUnderASecond()
        {
            string input = new string('x', ClipboardHandler.MaxFlipChars / 2) + new string('ф', ClipboardHandler.MaxFlipChars / 2);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            string output = KeyboardLayoutConverter.Convert(input, Us, Ru);
            clock.Stop();

            Assert.Equal(ClipboardHandler.MaxFlipChars, output.Length);
            Assert.Equal('ч', output[0]);
            Assert.Equal('a', output[output.Length - 1]);
            Assert.True(clock.ElapsedMilliseconds < 1000, "took " + clock.ElapsedMilliseconds + " ms");
        }
    }
}
