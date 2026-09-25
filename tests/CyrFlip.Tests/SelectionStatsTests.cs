using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The two numbers the context menu prints above its commands. Both rules that are decisions
    /// rather than arithmetic have a test of their own, because both are the kind of thing a later
    /// "simplification" would quietly undo.
    /// </summary>
    public class SelectionStatsTests
    {
        private static (int Lines, int Chars) Count(string? text)
        {
            SelectionStats.Count(text, out int lines, out int chars);
            return (lines, chars);
        }

        [Fact]
        public void NoSelectionCountsAsNothing()
        {
            Assert.Equal((0, 0), Count(null));
            Assert.Equal((0, 0), Count(""));
        }

        [Fact]
        public void OneLineIsOneLine() => Assert.Equal((1, 5), Count("hello"));

        [Fact]
        public void EveryBreakOpensTheNextLine() => Assert.Equal((3, 5), Count("a\nb\nc"));

        /// <summary>
        /// Dragging past the end of a line takes its break with it. That is one line on screen, so
        /// it is one line here - counting the break as a second, empty line is the off-by-one the
        /// user would notice immediately.
        /// </summary>
        [Theory]
        [InlineData("one line\n", 1)]
        [InlineData("one line\r\n", 1)]
        [InlineData("first\nsecond\n", 2)]
        public void ATrailingBreakDoesNotOpenALine(string text, int expected)
            => Assert.Equal(expected, Count(text).Lines);

        /// <summary>A CRLF is one line break written with two characters - not two breaks.</summary>
        [Fact]
        public void CrLfIsOneBreak()
        {
            Assert.Equal(2, Count("a\r\nb").Lines);
            Assert.Equal(2, Count("a\rb").Lines);   // a lone CR still separates
            Assert.Equal(3, Count("a\r\nb\nc").Lines);
        }

        /// <summary>A selection that is nothing but a line break is still something - one line.</summary>
        [Fact]
        public void ASelectionOfOnlyABreakIsStillOneLine() => Assert.Equal((1, 1), Count("\n"));

        /// <summary>
        /// Characters are counted exactly as they will land on the clipboard - spaces and the breaks
        /// themselves included - because the whole point of the number is "how big is this".
        /// </summary>
        [Fact]
        public void CharactersAreCountedAsTheyWillBePasted()
        {
            Assert.Equal(11, Count("hello world").Chars);
            Assert.Equal(4, Count("a\r\nb").Chars);
            Assert.Equal(3, Count(" a ").Chars);
        }

        /// <summary>
        /// A count taken from a capped read is stated as "at least". Printing the ceiling as a fact
        /// would hand the user a number that looks exact and is not.
        /// </summary>
        [Fact]
        public void ACappedReadIsPrintedAsAtLeast()
        {
            Assert.Equal("65536", SelectionStats.Format(65536, truncated: false));
            Assert.Equal("65536+", SelectionStats.Format(65536, truncated: true));
            Assert.Equal("12", SelectionStats.Format(12, truncated: false));
        }

        /// <summary>The snapshot decides truncation by reaching the probe's own cap - keep them in step.</summary>
        [Fact]
        public void TheSnapshotFlagsAReadThatHitTheCap()
        {
            Assert.False(new SelectionSnapshot(SelectionState.Present, "short").Truncated);
            Assert.True(new SelectionSnapshot(SelectionState.Present,
                new string('x', SelectionProbe.MaxTextChars)).Truncated);
        }

        /// <summary>
        /// The cap has to be big enough that the counts are the truth for a real selection, not a
        /// link-sized 2048 that every long copy would run into.
        /// </summary>
        [Fact]
        public void TheCapIsLargeEnoughForTheCountToMeanSomething()
            => Assert.InRange(SelectionProbe.MaxTextChars, 16384, 1024 * 1024);
    }
}
