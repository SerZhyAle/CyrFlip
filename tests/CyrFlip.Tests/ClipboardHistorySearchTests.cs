using Xunit;

namespace CyrFlip.Tests
{
    public sealed class ClipboardHistorySearchTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("ab")]
        [InlineData(" ab ")]
        public void Query_requires_at_least_three_non_whitespace_characters(string query)
            => Assert.False(ClipboardHistorySearch.IsReady(query));

        [Theory]
        [InlineData("abc")]
        [InlineData(" абв ")]
        public void Query_with_three_characters_is_ready(string query)
            => Assert.True(ClipboardHistorySearch.IsReady(query));

        [Fact]
        public void Matches_a_case_insensitive_text_fragment()
        {
            var entry = new ClipboardHistoryEntry { Text = "Привет, World!" };

            Assert.True(ClipboardHistorySearch.Matches(entry, "ВЕТ, W"));
            Assert.False(ClipboardHistorySearch.Matches(entry, "wo"));
        }
    }
}
