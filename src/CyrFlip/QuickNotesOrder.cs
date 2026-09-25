using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The standard order of the notes list and the search over it - the whole of the feature's
    /// logic that owes nothing to WinForms, the registry or DPAPI, split out so it can be tested
    /// without any of them (spec §7, the same principle as <see cref="ClipboardHistoryOrder"/>).
    ///
    /// <para>The order is <b>newest first by creation time</b>, and creation time only: editing an
    /// old note must not move it, which is the one thing that makes a list of thoughts re-readable
    /// later (spec §3.5). Equal timestamps are broken by <c>Id</c> descending - two notes created in
    /// the same millisecond would otherwise land in whatever order the replay happened to produce,
    /// and a list that reshuffles itself between launches is indistinguishable from a bug.</para>
    /// </summary>
    internal static class QuickNotesOrder
    {
        /// <summary>Newest first; ties broken by <c>Id</c> descending, so the order is total.</summary>
        public static int Compare(QuickNote a, QuickNote b)
        {
            int byDate = b.CreatedAtUtc.CompareTo(a.CreatedAtUtc);
            return byDate != 0 ? byDate : b.Id.CompareTo(a.Id);
        }

        public static void Sort(List<QuickNote> notes) => notes.Sort(Compare);

        /// <summary>
        /// Put one note where it belongs in an already-ordered list. A binary search plus one insert
        /// rather than a re-sort of the whole list: a note is saved on every debounce tick, and
        /// nothing in this feature may cost O(n log n) per keystroke-and-a-bit.
        /// </summary>
        public static void Insert(List<QuickNote> notes, QuickNote note)
        {
            // Lower bound: the first position whose occupant sorts *after* the new note. The list is
            // ascending by Compare - which puts the newest first - so the test is "<= 0 means this
            // one still belongs above us".
            int low = 0, high = notes.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (Compare(notes[middle], note) <= 0) low = middle + 1;
                else high = middle;
            }
            notes.Insert(low, note);
        }

        /// <summary>
        /// The matching notes, still in the standard order. Search deliberately does not rank: a
        /// result list that reorders itself by some invisible score is harder to re-read than the
        /// same list always in creation order (spec §4).
        /// </summary>
        public static List<QuickNote> Filter(IEnumerable<QuickNote> notes, string? query)
        {
            var found = new List<QuickNote>();
            foreach (QuickNote note in notes)
                if (note.Matches(query)) found.Add(note);
            return found;
        }
    }
}
