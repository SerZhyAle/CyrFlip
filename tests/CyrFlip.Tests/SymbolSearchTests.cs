using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Pure search logic verification (ticket S0056 section 13):
    /// character, code point variations, prefixes, multi-word conjunction,
    /// category words, empty query, no relevance reordering, surrogate pairs.
    /// </summary>
    public class SymbolSearchTests
    {
        [Fact]
        public void SearchByExactCharacter()
        {
            var results = SymbolSearch.Search(SymbolsData.Table, "€", "Русский");
            Assert.Contains(results, e => e.CodePoint == 0x20AC);
            Assert.Equal(0x20AC, results[0].CodePoint);

            var emDash = SymbolSearch.Search(SymbolsData.Table, "—", "English");
            Assert.Contains(emDash, e => e.CodePoint == 0x2014);
        }

        [Theory]
        [InlineData("20AC")]
        [InlineData("20ac")]
        [InlineData("U+20AC")]
        [InlineData("u+20ac")]
        [InlineData("0x20AC")]
        [InlineData("0x20ac")]
        public void SearchByCodePointFormatsFindsEuro(string query)
        {
            var results = SymbolSearch.Search(SymbolsData.Table, query, "English");
            Assert.NotEmpty(results);
            Assert.Equal(0x20AC, results[0].CodePoint);
        }

        [Fact]
        public void SearchByEnglishPrefix()
        {
            var results = SymbolSearch.Search(SymbolsData.Table, "eur", "English");
            Assert.Contains(results, e => e.CodePoint == 0x20AC);

            var dashResults = SymbolSearch.Search(SymbolsData.Table, "dash", "English");
            Assert.Contains(dashResults, e => e.CodePoint == 0x2014); // EM DASH
            Assert.Contains(dashResults, e => e.CodePoint == 0x2013); // EN DASH
        }

        [Fact]
        public void SearchByRussianPrefix()
        {
            var results = SymbolSearch.Search(SymbolsData.Table, "евро", "Русский");
            Assert.Contains(results, e => e.CodePoint == 0x20AC);

            var tireResults = SymbolSearch.Search(SymbolsData.Table, "тире", "Русский");
            Assert.Contains(tireResults, e => e.CodePoint == 0x2014); // длинное тире
            Assert.Contains(tireResults, e => e.CodePoint == 0x2013); // среднее тире

            var udarResults = SymbolSearch.Search(SymbolsData.Table, "удар", "Русский");
            Assert.Contains(udarResults, e => e.CodePoint == 0x0301); // ударение
        }

        [Fact]
        public void TwoWordsMustBothMatch()
        {
            var emResults = SymbolSearch.Search(SymbolsData.Table, "em dash", "English");
            Assert.Single(emResults, e => e.CodePoint == 0x2014);
            Assert.DoesNotContain(emResults, e => e.CodePoint == 0x2013);

            var ruResults = SymbolSearch.Search(SymbolsData.Table, "длинное тире", "Русский");
            Assert.Single(ruResults, e => e.CodePoint == 0x2014);
            Assert.DoesNotContain(ruResults, e => e.CodePoint == 0x2013);

            // Conflicting terms match nothing
            var empty = SymbolSearch.Search(SymbolsData.Table, "euro dash", "English");
            Assert.Empty(empty);
        }

        [Fact]
        public void SearchByCategoryWordInUiLanguage()
        {
            // Category "Валюты" in Russian UI
            var ruCurrency = SymbolSearch.Search(SymbolsData.Table, "Валюты", "Русский");
            Assert.NotEmpty(ruCurrency);
            Assert.All(ruCurrency, e => Assert.Equal(SymbolsData.CategoryCurrency, e.Category));

            // Category "Currencies" or "Currency" in English UI
            var enArrows = SymbolSearch.Search(SymbolsData.Table, "Arrows", "English");
            Assert.NotEmpty(enArrows);
            Assert.All(enArrows, e => Assert.Equal(SymbolsData.CategoryArrows, e.Category));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyQueryReturnsEntireTableInStandardOrder(string? emptyQuery)
        {
            var results = SymbolSearch.Search(SymbolsData.Table, emptyQuery, "Русский");
            Assert.Equal(SymbolsData.Table.Length, results.Count);

            // Standard order: categories follow SymbolsData.Categories order
            var catOrder = SymbolsData.Categories.Select(c => c.Key).ToList();
            int prevCatIndex = -1;
            foreach (SymbolEntry entry in results)
            {
                int catIndex = catOrder.IndexOf(entry.Category);
                Assert.True(catIndex >= prevCatIndex, "Results not in standard category order");
                prevCatIndex = catIndex;
            }
        }

        [Fact]
        public void QueryMatchingNothingReturnsEmptyList()
        {
            var results = SymbolSearch.Search(SymbolsData.Table, "xyznonexistent999", "English");
            Assert.Empty(results);
        }

        [Fact]
        public void NoRelevanceReorderingPreservesStandardOrder()
        {
            // "dash" matches both EM DASH (U+2014) and EN DASH (U+2013)
            var results = SymbolSearch.Search(SymbolsData.Table, "dash", "English");
            Assert.True(results.Count >= 2);

            // In SymbolsData.Table, EM DASH comes before EN DASH (both punctuation)
            int emIndex = results.FindIndex(e => e.CodePoint == 0x2014);
            int enIndex = results.FindIndex(e => e.CodePoint == 0x2013);
            Assert.True(emIndex < enIndex);

            // Category order takes precedence over term matches
            var order = SymbolsData.Categories.Select(c => c.Key).ToList();
            int prevIndex = -1;
            foreach (SymbolEntry r in results)
            {
                int idx = order.IndexOf(r.Category);
                Assert.True(idx >= prevIndex);
                prevIndex = idx;
            }
        }

        [Fact]
        public void SurrogatePairSearch()
        {
            // Test with a synthetic surrogate pair entry
            string surrogateChar = char.ConvertFromUtf32(0x1F600); // 😀
            var customEntry = new SymbolEntry(
                text: surrogateChar,
                codePoint: 0x1F600,
                category: SymbolsData.CategorySymbols,
                nameEn: "GRINNING FACE",
                nameRu: "улыбающееся лицо",
                keywords: "smile happy face");

            var list = new[] { customEntry };

            // 1. By character itself
            var charMatch = SymbolSearch.Search(list, surrogateChar, "English");
            Assert.Single(charMatch);
            Assert.Equal(0x1F600, charMatch[0].CodePoint);

            // 2. By code point
            var cpMatch = SymbolSearch.Search(list, "1F600", "English");
            Assert.Single(cpMatch);

            var uCpMatch = SymbolSearch.Search(list, "U+1F600", "English");
            Assert.Single(uCpMatch);

            // 3. By prefix
            var wordMatch = SymbolSearch.Search(list, "grin", "English");
            Assert.Single(wordMatch);
        }
    }
}
