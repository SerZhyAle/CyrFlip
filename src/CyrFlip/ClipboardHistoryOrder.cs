using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The clipboard history's display order - pinned first (newest first), then the rest (newest
    /// first) - maintained <b>incrementally</b>, plus the Uuid index and the "this is what the
    /// clipboard holds now" flag.
    ///
    /// <para><b>Why this is its own class.</b> The history is unbounded by deliberate decision
    /// (2026-07-26), and that decision obliges everything around it: nothing may cost O(history) per
    /// copy or per repaint. The order used to be produced by re-sorting the whole list
    /// (<c>OrderByDescending(IsPinned).ThenByDescending(CreatedAt)</c>) after every change, and
    /// <c>IsCurrent</c> by walking every entry - so a year of copying made each Ctrl+C measurably
    /// slower, on the UI thread. Here every operation is a binary search plus one list move: no
    /// comparers, no delegates, no allocation. Splitting it out of
    /// <see cref="ClipboardHistoryService"/> is also what makes the order testable without a
    /// <c>NativeWindow</c>, the registry or DPAPI - see <c>ClipboardHistoryOrderTests</c>, which
    /// checks it against that very reference sort.</para>
    ///
    /// <para><b>The invariant:</b> <c>[0, PinnedCount)</c> are the pinned entries by
    /// <see cref="ClipboardHistoryEntry.CreatedAt"/> descending, <c>[PinnedCount, Count)</c> are the
    /// rest by <c>CreatedAt</c> descending. Every mutation goes through
    /// <see cref="Detach"/> + <see cref="Insert"/>, so the position is always derived from the
    /// entry's own state rather than assumed - which is what lets the log replay in
    /// <c>ClipboardHistoryService.Load</c> use the same three methods in whatever order the log
    /// happens to carry.</para>
    ///
    /// <para>Not thread-safe: every caller is the UI thread (the clipboard listener's WndProc and
    /// the two history windows).</para>
    /// </summary>
    internal sealed class ClipboardHistoryOrder
    {
        private readonly List<ClipboardHistoryEntry> _entries = new List<ClipboardHistoryEntry>();
        private readonly Dictionary<string, ClipboardHistoryEntry> _byUuid =
            new Dictionary<string, ClipboardHistoryEntry>(StringComparer.Ordinal);
        private int _pinnedCount;
        private ClipboardHistoryEntry? _current;

        /// <summary>The entries in display order - the list the strip and the search window paint from.</summary>
        public IReadOnlyList<ClipboardHistoryEntry> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>Size of the pinned prefix; exposed so the tests can assert the invariant itself.</summary>
        internal int PinnedCount => _pinnedCount;

        /// <summary>The entry with this text hash, or null. O(1) - a repeat copy must not scan.</summary>
        public ClipboardHistoryEntry? Find(string? uuid)
            => uuid != null && _byUuid.TryGetValue(uuid, out ClipboardHistoryEntry? entry) ? entry : null;

        /// <summary>
        /// Index and place a new entry. False when its Uuid is already known - the caller
        /// (a live copy, or the log replay) is expected to have asked <see cref="Find"/> first, and
        /// silently adding a duplicate would leave the older one in the list but out of the index.
        /// </summary>
        public bool Add(ClipboardHistoryEntry entry)
        {
            if (entry == null || entry.Uuid.Length == 0 || _byUuid.ContainsKey(entry.Uuid))
                return false;
            _byUuid[entry.Uuid] = entry;
            Insert(entry);
            return true;
        }

        /// <summary>
        /// Change an entry's date and/or pinned state and move it to where that puts it. One method
        /// for all three log actions (touch / pin / unpin) because they carry the same two fields,
        /// and because doing it in one detach-modify-insert pass cannot leave a half-moved entry.
        /// </summary>
        public void Update(ClipboardHistoryEntry entry, DateTime createdAt, bool pinned)
        {
            if (entry == null) return;
            Detach(entry);
            entry.CreatedAt = createdAt;
            entry.IsPinned = pinned;
            Insert(entry);
        }

        public void Remove(ClipboardHistoryEntry entry)
        {
            if (entry == null) return;
            Detach(entry);
            if (_byUuid.TryGetValue(entry.Uuid, out ClipboardHistoryEntry? indexed) && ReferenceEquals(indexed, entry))
                _byUuid.Remove(entry.Uuid);
            // Deleting what the clipboard currently holds leaves no current entry - and never a
            // dangling reference to something the user asked us to forget.
            if (ReferenceEquals(_current, entry)) _current = null;
        }

        public void Clear()
        {
            _entries.Clear();
            _byUuid.Clear();
            _pinnedCount = 0;
            _current = null;
        }

        /// <summary>
        /// Mark one entry as the clipboard's current content, in O(1): only the entry losing the flag
        /// and the one gaining it are touched. This never changes the display order, so it raises
        /// nothing - every caller raises the change itself.
        /// </summary>
        public void SetCurrent(ClipboardHistoryEntry? entry)
        {
            if (ReferenceEquals(_current, entry)) return;
            if (_current != null) _current.IsCurrent = false;
            _current = entry;
            if (entry != null) entry.IsCurrent = true;
        }

        /// <summary>
        /// Place <paramref name="entry"/> inside its own group by descending date. The search is
        /// bounded to the group the entry belongs to, which is what keeps the two groups from ever
        /// interleaving.
        /// </summary>
        private void Insert(ClipboardHistoryEntry entry)
        {
            int low = entry.IsPinned ? 0 : _pinnedCount;
            int high = entry.IsPinned ? _pinnedCount : _entries.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_entries[middle].CreatedAt > entry.CreatedAt) low = middle + 1;
                else high = middle;
            }
            _entries.Insert(low, entry);
            if (entry.IsPinned) _pinnedCount++;
        }

        /// <summary>
        /// Take the entry out of the list, keeping <see cref="_pinnedCount"/> honest. The lookup is
        /// a reference scan rather than a binary search on purpose: <see cref="Update"/> may have to
        /// find an entry whose date is about to change, and a scan over an array of references costs
        /// no comparers and no allocation.
        /// </summary>
        private void Detach(ClipboardHistoryEntry entry)
        {
            int index = _entries.IndexOf(entry);
            if (index < 0) return;
            _entries.RemoveAt(index);
            if (index < _pinnedCount) _pinnedCount--;
        }
    }
}
