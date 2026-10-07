using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// MRU recent list verification (ticket S0056 section 13):
    /// newest first, deduplication, 24-cap, validation filtering, damaged JSON fallback, surrogate pairs.
    /// </summary>
    public class SymbolRecentTests
    {
        [Fact]
        public void NewestFirstAndDeduplicationMovesToFront()
        {
            var recent = new SymbolRecent();
            recent.Add("€");
            recent.Add("—");
            recent.Add("№");

            Assert.Equal(new[] { "№", "—", "€" }, recent.Items);

            // Re-adding "€" moves it to front
            recent.Add("€");
            Assert.Equal(new[] { "€", "№", "—" }, recent.Items);
            Assert.Equal(3, recent.Count);
        }

        [Fact]
        public void CapOf24HoldsOnThe25th()
        {
            var recent = new SymbolRecent();
            for (int i = 0; i < 30; i++)
            {
                recent.Add($"char_{i}");
            }

            Assert.Equal(24, recent.Count);
            Assert.Equal("char_29", recent.Items[0]);
            Assert.Equal("char_6", recent.Items[23]);
            Assert.DoesNotContain("char_5", recent.Items);
            Assert.DoesNotContain("char_0", recent.Items);
        }

        [Fact]
        public void CharacterNoLongerInTableIsDroppedOnRead()
        {
            // Create a JSON with 3 items: 2 valid in SymbolsData.Table and 1 absent
            var valid1 = "€";
            var valid2 = "—";
            var unknown = "xyz_unknown";

            string json = $"[\"{valid1}\", \"{unknown}\", \"{valid2}\"]";

            var loaded = SymbolRecent.FromJson(json, text => SymbolsData.FindByText(text) != null);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(new[] { valid1, valid2 }, loaded.Items);
            Assert.DoesNotContain(unknown, loaded.Items);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("{not an array}")]
        [InlineData("invalid json ][")]
        [InlineData("\"just a string\"")]
        [InlineData("123")]
        public void DamagedOrInvalidJsonFallsBackToEmpty(string? damagedJson)
        {
            var recent = SymbolRecent.FromJson(damagedJson, text => SymbolsData.FindByText(text) != null);
            Assert.NotNull(recent);
            Assert.Equal(0, recent.Count);
            Assert.Empty(recent.Items);
        }

        [Fact]
        public void SurrogatePairSurvivesRoundTrip()
        {
            string surrogate = char.ConvertFromUtf32(0x1F600); // 😀
            var recent = new SymbolRecent();
            recent.Add(surrogate);
            recent.Add("€");

            string json = recent.ToJson();
            Assert.Contains(surrogate, json);

            var restored = SymbolRecent.FromJson(json);
            Assert.Equal(2, restored.Count);
            Assert.Equal(surrogate, restored.Items[1]);
            Assert.Equal("€", restored.Items[0]);
        }

        [Fact]
        public void ClearEmptiesTheList()
        {
            var recent = new SymbolRecent();
            recent.Add("€");
            recent.Add("—");
            Assert.Equal(2, recent.Count);

            recent.Clear();
            Assert.Equal(0, recent.Count);
            Assert.Empty(recent.Items);
            Assert.Equal("[]", recent.ToJson());
        }
    }
}
