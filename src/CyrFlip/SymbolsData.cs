using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// An immutable entry in the curated symbols table (ticket S0056).
    /// </summary>
    internal sealed class SymbolEntry
    {
        public string Text { get; }
        public int CodePoint { get; }
        public string Category { get; }
        public string NameEn { get; }
        public string NameRu { get; }
        public string Keywords { get; }
        public string? Label { get; }
        public bool IsCombining { get; }

        public SymbolEntry(
            string text,
            int codePoint,
            string category,
            string nameEn,
            string nameRu,
            string keywords,
            string? label = null,
            bool isCombining = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            CodePoint = codePoint;
            Category = category ?? throw new ArgumentNullException(nameof(category));
            NameEn = nameEn ?? throw new ArgumentNullException(nameof(nameEn));
            NameRu = nameRu ?? throw new ArgumentNullException(nameof(nameRu));
            Keywords = keywords ?? string.Empty;
            Label = label;
            IsCombining = isCombining;
        }

        /// <summary>
        /// Display name following Section 6.3: Russian UI gets the Russian name;
        /// every other UI language gets the English name.
        /// </summary>
        public string LocalizedName(string uiLanguage) =>
            Localization.IndexOf(uiLanguage) == 0 ? NameRu : NameEn;
    }

    /// <summary>
    /// Metadata for a category in the symbols palette.
    /// </summary>
    internal sealed class SymbolCategory
    {
        public string Key { get; }
        public string Title { get; }

        public SymbolCategory(string key, string title)
        {
            Key = key;
            Title = title;
        }
    }

    /// <summary>
    /// Curated palette data and categories (ticket S0056).
    /// The character table itself is generated in <c>Symbols.g.cs</c>.
    /// </summary>
    internal static partial class SymbolsData
    {
        /// <summary>The list entry that shows every symbol; the one key that is not a group of the table.</summary>
        public const string AllCategories = "all";
        public const string CategoryPunctuation = "punctuation";
        public const string CategoryCurrency = "currency";
        public const string CategoryMath = "math";
        public const string CategoryArrows = "arrows";
        public const string CategoryGreek = "greek";
        public const string CategoryLatin = "latin";
        public const string CategoryCyrillic = "cyrillic";
        public const string CategoryCombining = "combining";
        public const string CategorySymbols = "symbols";
        public const string CategoryNumbers = "numbers";
        public const string CategoryInvisible = "invisible";
        public const string CategoryBox = "box";

        public static readonly SymbolCategory[] Categories =
        {
            new SymbolCategory(CategoryPunctuation, "Punctuation & typography"),
            new SymbolCategory(CategoryCurrency, "Currencies"),
            new SymbolCategory(CategoryMath, "Math"),
            new SymbolCategory(CategoryArrows, "Arrows"),
            new SymbolCategory(CategoryGreek, "Greek"),
            new SymbolCategory(CategoryLatin, "Latin with diacritics"),
            new SymbolCategory(CategoryCyrillic, "Cyrillic (rare)"),
            new SymbolCategory(CategoryCombining, "Diacritics (combining)"),
            new SymbolCategory(CategorySymbols, "Signs & symbols"),
            new SymbolCategory(CategoryNumbers, "Fractions, scripts, digits"),
            new SymbolCategory(CategoryInvisible, "Spaces & invisible"),
            new SymbolCategory(CategoryBox, "Box & block elements"),
        };

        private static readonly Dictionary<string, SymbolEntry> ByText =
            Table.ToDictionary(e => e.Text, StringComparer.Ordinal);

        private static readonly Dictionary<int, SymbolEntry> ByCodePoint =
            Table.ToDictionary(e => e.CodePoint);

        private static readonly Dictionary<string, string> CategoryNames =
            Categories.ToDictionary(c => c.Key, c => c.Title, StringComparer.OrdinalIgnoreCase);

        /// <summary>Looks up a curated symbol by exact text representation.</summary>
        public static SymbolEntry? FindByText(string text) =>
            ByText.TryGetValue(text, out SymbolEntry? entry) ? entry : null;

        /// <summary>Looks up a curated symbol by code point.</summary>
        public static SymbolEntry? FindByCodePoint(int codePoint) =>
            ByCodePoint.TryGetValue(codePoint, out SymbolEntry? entry) ? entry : null;

        /// <summary>Returns the English source category name for localization.</summary>
        public static string CategoryTitle(string categoryKey) =>
            CategoryNames.TryGetValue(categoryKey, out string? name) ? name : string.Empty;

        /// <summary>The group a stored key names; anything else (empty, damaged, a group a later version dropped) is <see cref="AllCategories"/>.</summary>
        public static string NormalizeCategory(string? key)
        {
            if (string.IsNullOrEmpty(key)) return AllCategories;
            foreach (SymbolCategory cat in Categories)
            {
                if (string.Equals(cat.Key, key, StringComparison.OrdinalIgnoreCase)) return cat.Key;
            }
            return AllCategories;
        }
    }
}
