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
        public string TitleRu { get; }

        public SymbolCategory(string key, string titleRu)
        {
            Key = key;
            TitleRu = titleRu;
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
            new SymbolCategory(CategoryPunctuation, "Пунктуация и типографика"),
            new SymbolCategory(CategoryCurrency, "Валюты"),
            new SymbolCategory(CategoryMath, "Математика"),
            new SymbolCategory(CategoryArrows, "Стрелки"),
            new SymbolCategory(CategoryGreek, "Греческие"),
            new SymbolCategory(CategoryLatin, "Латиница с диакритикой"),
            new SymbolCategory(CategoryCyrillic, "Кириллица (редкая)"),
            new SymbolCategory(CategoryCombining, "Диакритика (комбинируемая)"),
            new SymbolCategory(CategorySymbols, "Знаки и символы"),
            new SymbolCategory(CategoryNumbers, "Дроби, индексы, цифры"),
            new SymbolCategory(CategoryInvisible, "Пробелы и невидимые"),
            new SymbolCategory(CategoryBox, "Рамки и блоки"),
        };

        private static readonly Dictionary<string, SymbolEntry> ByText =
            Table.ToDictionary(e => e.Text, StringComparer.Ordinal);

        private static readonly Dictionary<int, SymbolEntry> ByCodePoint =
            Table.ToDictionary(e => e.CodePoint);

        private static readonly Dictionary<string, string> CategoryNames =
            Categories.ToDictionary(c => c.Key, c => c.TitleRu, StringComparer.OrdinalIgnoreCase);

        /// <summary>Looks up a curated symbol by exact text representation.</summary>
        public static SymbolEntry? FindByText(string text) =>
            ByText.TryGetValue(text, out SymbolEntry? entry) ? entry : null;

        /// <summary>Looks up a curated symbol by code point.</summary>
        public static SymbolEntry? FindByCodePoint(int codePoint) =>
            ByCodePoint.TryGetValue(codePoint, out SymbolEntry? entry) ? entry : null;

        /// <summary>Returns the Russian source category name for localization.</summary>
        public static string CategoryTitleRu(string categoryKey) =>
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
