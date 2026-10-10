using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// Pure search logic for the symbols palette (ticket S0056 section 6.3).
    /// </summary>
    internal static class SymbolSearch
    {
        private static readonly char[] WordSeparators = { ' ', '\t', '-', '_', '/', '(', ')', ',', '.' };

        /// <summary>
        /// Filters entries by a search query and optional category / typability filter.
        /// Preserves standard order: category order, then table order. No relevance reordering.
        /// </summary>
        public static List<SymbolEntry> Search(
            IEnumerable<SymbolEntry> source,
            string? query,
            string uiLanguage,
            string? selectedCategory = null,
            Func<SymbolEntry, bool>? filter = null)
        {
            if (source == null) return new List<SymbolEntry>();

            // Category filter if a specific curated category is selected (null is the "all" list)
            IEnumerable<SymbolEntry> candidates = source;
            if (!string.IsNullOrEmpty(selectedCategory))
            {
                candidates = candidates.Where(e => string.Equals(e.Category, selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (filter != null)
            {
                candidates = candidates.Where(filter);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return OrderStandard(candidates).ToList();
            }

            string[] queryWords = query!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryWords.Length == 0)
            {
                return OrderStandard(candidates).ToList();
            }

            // Category names translated into current UI language
            var categoryLocalizedWords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (SymbolCategory cat in SymbolsData.Categories)
            {
                string localized = Localization.Translate(uiLanguage, cat.TitleRu);
                categoryLocalizedWords[cat.Key] = SplitWords(localized);
            }

            var results = new List<SymbolEntry>();
            foreach (SymbolEntry entry in OrderStandard(candidates))
            {
                if (MatchesAll(entry, queryWords, categoryLocalizedWords))
                {
                    results.Add(entry);
                }
            }

            return results;
        }

        private static bool MatchesAll(
            SymbolEntry entry,
            string[] queryWords,
            Dictionary<string, string[]> categoryWords)
        {
            string hex4 = entry.CodePoint.ToString("X4");
            string hexNorm = entry.CodePoint.ToString("X");
            string text = entry.Text;

            string[] wordsEn = SplitWords(entry.NameEn);
            string[] wordsRu = SplitWords(entry.NameRu);
            string[] wordsKeywords = SplitWords(entry.Keywords);
            categoryWords.TryGetValue(entry.Category, out string[]? catWords);

            foreach (string qWord in queryWords)
            {
                if (!MatchesOneWord(qWord, text, entry.CodePoint, hex4, hexNorm, wordsEn, wordsRu, wordsKeywords, catWords))
                    return false;
            }

            return true;
        }

        private static bool MatchesOneWord(
            string qWord,
            string text,
            int codePoint,
            string hex4,
            string hexNorm,
            string[] wordsEn,
            string[] wordsRu,
            string[] wordsKeywords,
            string[]? catWords)
        {
            // 1. Exact character or substring match (e.g. typing or pasting the character)
            if (text.IndexOf(qWord, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 2. Code point: 20AC, U+20AC, 0x20AC (case-insensitive)
            string hexTarget = qWord;
            if (hexTarget.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
                hexTarget.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexTarget = hexTarget.Substring(2);
            }

            if (hexTarget.Length > 0 &&
                (hex4.StartsWith(hexTarget, StringComparison.OrdinalIgnoreCase) ||
                 hexNorm.Equals(hexTarget, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 3. Prefix of English name words
            if (HasPrefix(wordsEn, qWord))
                return true;

            // 4. Prefix of Russian name words
            if (HasPrefix(wordsRu, qWord))
                return true;

            // 5. Prefix of keywords
            if (HasPrefix(wordsKeywords, qWord))
                return true;

            // 6. Prefix of category words in UI language
            if (catWords != null && HasPrefix(catWords, qWord))
                return true;

            return false;
        }

        private static bool HasPrefix(string[] words, string prefix)
        {
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string[] SplitWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        }

        private static IEnumerable<SymbolEntry> OrderStandard(IEnumerable<SymbolEntry> entries)
        {
            var categoryIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SymbolsData.Categories.Length; i++)
            {
                categoryIndices[SymbolsData.Categories[i].Key] = i;
            }

            return entries.OrderBy(e => categoryIndices.TryGetValue(e.Category, out int idx) ? idx : 999);
        }
    }
}
