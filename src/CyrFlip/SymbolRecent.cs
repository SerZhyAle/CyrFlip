using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The MRU list of the last 24 distinct characters copied, newest first (ticket S0056 section 6.2).
    /// </summary>
    internal sealed class SymbolRecent
    {
        public const int MaxCount = 24;

        private readonly List<string> _items = new List<string>();

        public IReadOnlyList<string> Items => _items;

        public int Count => _items.Count;

        public SymbolRecent()
        {
        }

        public SymbolRecent(IEnumerable<string> items)
        {
            if (items != null)
            {
                foreach (string item in items)
                {
                    if (!string.IsNullOrEmpty(item) && !_items.Contains(item) && _items.Count < MaxCount)
                        _items.Add(item);
                }
            }
        }

        /// <summary>
        /// Adds a copied character. If already present, moves to front. Caps at 24.
        /// </summary>
        public bool Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            _items.Remove(text);
            _items.Insert(0, text);
            if (_items.Count > MaxCount)
                _items.RemoveRange(MaxCount, _items.Count - MaxCount);
            return true;
        }

        /// <summary>Clears the recent characters list.</summary>
        public void Clear() => _items.Clear();

        /// <summary>Serializes the list into a JSON array string.</summary>
        public string ToJson()
        {
            try
            {
                return new JavaScriptSerializer().Serialize(_items);
            }
            catch
            {
                return "[]";
            }
        }

        /// <summary>
        /// Parses a JSON array from config. Unknown characters (not recognized by isValid)
        /// are dropped, the list is capped at 24, and damaged JSON falls back to empty.
        /// </summary>
        public static SymbolRecent FromJson(string? json, Func<string, bool>? isValid = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return new SymbolRecent();

            try
            {
                var serializer = new JavaScriptSerializer();
                object? parsed = serializer.DeserializeObject(json);
                if (parsed is object[] array)
                {
                    var items = new List<string>();
                    foreach (object item in array)
                    {
                        if (item is string s && !string.IsNullOrEmpty(s))
                        {
                            if ((isValid == null || isValid(s)) && !items.Contains(s))
                            {
                                items.Add(s);
                                if (items.Count >= MaxCount) break;
                            }
                        }
                    }
                    return new SymbolRecent(items);
                }
            }
            catch
            {
                // Damaged value falls back to empty and is not saved over until a copy occurs (S0056 section 6.2).
            }

            return new SymbolRecent();
        }
    }
}
