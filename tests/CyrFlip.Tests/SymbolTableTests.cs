using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Verification of the curated symbols table (ticket S0056 section 13):
    /// code points, uniqueness, categories, inventory counts, invisible labels, combining marks.
    /// </summary>
    public class SymbolTableTests
    {
        [Fact]
        public void TableHas501EntriesAcross12CuratedCategories()
        {
            Assert.Equal(501, SymbolsData.Table.Length);

            var curatedCategories = SymbolsData.Categories
                .Where(c => !string.Equals(c.Key, SymbolsData.CategoryRecent, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Equal(12, curatedCategories.Count);

            foreach (SymbolCategory cat in curatedCategories)
            {
                int count = SymbolsData.Table.Count(e => string.Equals(e.Category, cat.Key, StringComparison.OrdinalIgnoreCase));
                Assert.True(count > 0, $"Category '{cat.Key}' is empty in SymbolsData.Table");
            }
        }

        [Fact]
        public void EveryEntryHasValidCodePointTextAndNames()
        {
            var knownCategories = new HashSet<string>(
                SymbolsData.Categories.Select(c => c.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                Assert.False(string.IsNullOrEmpty(entry.Text), "Entry text is null or empty");
                Assert.True(
                    entry.Text.Length == 1 || (entry.Text.Length == 2 && char.IsSurrogatePair(entry.Text, 0)),
                    $"Entry {entry.NameEn} has invalid text length {entry.Text.Length}");

                int expectedCodePoint = char.ConvertToUtf32(entry.Text, 0);
                Assert.Equal(expectedCodePoint, entry.CodePoint);

                Assert.True(knownCategories.Contains(entry.Category), $"Unknown category '{entry.Category}' for {entry.NameEn}");
                Assert.NotEqual(SymbolsData.CategoryRecent, entry.Category);

                Assert.False(string.IsNullOrWhiteSpace(entry.NameEn), $"NameEn is empty for code point U+{entry.CodePoint:X4}");
                Assert.False(string.IsNullOrWhiteSpace(entry.NameRu), $"NameRu is empty for code point U+{entry.CodePoint:X4}");
            }
        }

        [Fact]
        public void NoTwoEntriesShareTextOrCodePoint()
        {
            var seenTexts = new HashSet<string>(StringComparer.Ordinal);
            var seenCodePoints = new HashSet<int>();

            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                Assert.True(seenTexts.Add(entry.Text), $"Duplicate text: {entry.Text} ({entry.NameEn})");
                Assert.True(seenCodePoints.Add(entry.CodePoint), $"Duplicate code point: U+{entry.CodePoint:X4}");
            }
        }

        [Fact]
        public void InvisibleCharactersAreExactlyTheOnesWithALabel()
        {
            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                if (string.Equals(entry.Category, SymbolsData.CategoryInvisible, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.NotNull(entry.Label);
                    Assert.False(string.IsNullOrWhiteSpace(entry.Label), $"Invisible entry U+{entry.CodePoint:X4} has empty label");
                }
                else
                {
                    Assert.Null(entry.Label);
                }
            }

            int invisibleCount = SymbolsData.Table.Count(e => string.Equals(e.Category, SymbolsData.CategoryInvisible, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(14, invisibleCount);
        }

        [Fact]
        public void CombiningMarksAreExactlyTheOnesFlaggedIsCombining()
        {
            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                if (string.Equals(entry.Category, SymbolsData.CategoryCombining, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(entry.IsCombining, $"Combining entry U+{entry.CodePoint:X4} is not flagged IsCombining");
                }
                else
                {
                    Assert.False(entry.IsCombining, $"Non-combining entry U+{entry.CodePoint:X4} has IsCombining=true");
                }
            }

            int combiningCount = SymbolsData.Table.Count(e => entryIsCombining(e));
            Assert.Equal(12, combiningCount);

            static bool entryIsCombining(SymbolEntry e) => e.IsCombining;
        }

        [Fact]
        public void LookupsByTextAndCodePointMatchTable()
        {
            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                SymbolEntry? byText = SymbolsData.FindByText(entry.Text);
                Assert.Same(entry, byText);

                SymbolEntry? byCp = SymbolsData.FindByCodePoint(entry.CodePoint);
                Assert.Same(entry, byCp);
            }

            Assert.Null(SymbolsData.FindByText(""));
            Assert.Null(SymbolsData.FindByText("nonexistent"));
            Assert.Null(SymbolsData.FindByCodePoint(-1));
            Assert.Null(SymbolsData.FindByCodePoint(0x10FFFF + 1));
        }

        [Fact]
        public void LocalizedNameFollowsLanguageRule()
        {
            SymbolEntry entry = SymbolsData.Table.First(e => e.CodePoint == 0x20AC); // Euro sign
            Assert.Equal(entry.NameRu, entry.LocalizedName("Русский"));

            for (int i = 1; i < Localization.Names.Length; i++)
            {
                string nonRussian = Localization.Names[i];
                Assert.Equal(entry.NameEn, entry.LocalizedName(nonRussian));
            }
        }
    }
}
