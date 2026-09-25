using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// What importing an exchange file would change (ticket S0023, spec 7.2), decided without touching
    /// anything - so the preview the user confirms and the import that follows are the same
    /// computation. The services re-plan against their live state at apply time rather than trusting
    /// a plan made before a modal dialog.
    ///
    /// <para>The rules are few and all additive: nothing is ever deleted, a note is replaced only by a
    /// strictly newer version of itself (a tie keeps the local one), a pin is never taken away, and a
    /// clipboard text already in the history is raised rather than duplicated. A file that names the
    /// same object twice is folded as if its blocks arrived one after another.</para>
    /// </summary>
    internal static class CyrFlipExchangeMerge
    {
        internal sealed class NoteAction
        {
            /// <summary>The local note to update, or null for a note to add.</summary>
            public QuickNote? Existing;
            /// <summary>The note as it should be after the import.</summary>
            public QuickNote Note = new QuickNote();
        }

        internal sealed class ClipboardAction
        {
            /// <summary>The local entry to raise, or null for an entry to add.</summary>
            public ClipboardHistoryEntry? Existing;
            public ExchangeClipboardItem Item = new ExchangeClipboardItem();
            public DateTime CopiedAtUtc;
            public bool Pinned;
        }

        /// <param name="includeNew">Take the hand-written notes without an id (spec 7.3) - only after the user agreed.</param>
        public static List<NoteAction> PlanNotes(IEnumerable<QuickNote> local, IEnumerable<ExchangeNote> incoming,
            bool includeNew, DateTime nowUtc, ExchangeMergeReport report)
        {
            var byId = new Dictionary<Guid, QuickNote>();
            foreach (QuickNote note in local) byId[note.Id] = note;
            var planned = new Dictionary<Guid, NoteAction>();
            var actions = new List<NoteAction>();
            int withId = 0;

            foreach (ExchangeNote block in incoming)
            {
                if (block.Id == null)
                {
                    if (!includeNew) continue;
                    var fresh = new NoteAction { Note = block.ToNote(Guid.NewGuid(), nowUtc) };
                    actions.Add(fresh);
                    report.NotesNew++;
                    continue;
                }
                withId++;
                Guid id = block.Id.Value;
                QuickNote candidate = block.ToNote(id, nowUtc);
                if (planned.TryGetValue(id, out NoteAction? earlier))
                {
                    // The same id twice in one file: the later block wins only when it is newer.
                    if (candidate.UpdatedAtUtc > earlier.Note.UpdatedAtUtc)
                    {
                        if (earlier.Existing != null) candidate.CreatedAtUtc = earlier.Existing.CreatedAtUtc;
                        earlier.Note = candidate;
                    }
                    continue;
                }
                if (byId.TryGetValue(id, out QuickNote? existing))
                {
                    if (candidate.UpdatedAtUtc <= existing.UpdatedAtUtc) continue;   // a tie keeps the local version
                    // The creation date is the list order and is never rewritten (quick-notes spec 3.5).
                    candidate.CreatedAtUtc = existing.CreatedAtUtc;
                    var update = new NoteAction { Existing = existing, Note = candidate };
                    planned[id] = update;
                    actions.Add(update);
                    continue;
                }
                var add = new NoteAction { Note = candidate };
                planned[id] = add;
                actions.Add(add);
            }

            int added = 0, updated = 0;
            foreach (NoteAction action in actions)
            {
                if (action.Existing != null) updated++;
                else if (planned.ContainsKey(action.Note.Id)) added++;
            }
            report.NotesAdded += added;
            report.NotesUpdated += updated;
            report.NotesSkipped += withId - added - updated;
            return actions;
        }

        public static List<ClipboardAction> PlanClipboard(Func<string, ClipboardHistoryEntry?> find,
            IEnumerable<ExchangeClipboardItem> incoming, ExchangeMergeReport report)
        {
            var planned = new Dictionary<string, ClipboardAction>(StringComparer.Ordinal);
            var order = new List<ClipboardAction>();
            foreach (ExchangeClipboardItem item in incoming)
            {
                if (!planned.TryGetValue(item.Uuid, out ClipboardAction? action))
                {
                    ClipboardHistoryEntry? existing = find(item.Uuid);
                    action = new ClipboardAction
                    {
                        Existing = existing,
                        Item = item,
                        CopiedAtUtc = existing?.CreatedAt ?? item.CopiedAtUtc,
                        Pinned = existing?.IsPinned ?? false,
                    };
                    planned[item.Uuid] = action;
                    order.Add(action);
                }
                // A newer copy raises the entry; "true" wins over "false" for the pin (spec 7.2).
                if (item.CopiedAtUtc > action.CopiedAtUtc) action.CopiedAtUtc = item.CopiedAtUtc;
                action.Pinned |= item.Pinned;
            }

            var actions = new List<ClipboardAction>();
            foreach (ClipboardAction action in order)
            {
                if (action.Existing == null)
                {
                    report.ClipboardAdded++;
                    actions.Add(action);
                    continue;
                }
                report.ClipboardExisting++;
                if (action.CopiedAtUtc > action.Existing.CreatedAt || action.Pinned != action.Existing.IsPinned)
                {
                    report.ClipboardRaised++;
                    actions.Add(action);
                }
            }
            return actions;
        }
    }
}
