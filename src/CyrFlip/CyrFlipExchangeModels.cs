using System;
using System.Collections.Generic;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The DTOs of the open exchange file, "CyrFlip Exchange Text v1" (ticket S0023). The file is a
    /// <b>portable snapshot</b>, not a storage format: <c>clipboard-history.log</c> and
    /// <c>quick-notes.log</c> stay what they are and may change on their own, while v1 is frozen by
    /// what people already have on their phones. Nothing here knows about WinForms, the registry or
    /// DPAPI - the reader, the writer and the merge planner are unit-tested on strings.
    /// </summary>
    internal static class CyrFlipExchange
    {
        /// <summary>The first non-empty line of every v1 file.</summary>
        public const string Marker = "# CyrFlip Exchange Text v1";
        /// <summary>What every version's marker starts with - how a v2 file is told apart from a stranger.</summary>
        public const string MarkerPrefix = "# CyrFlip Exchange Text v";
        public const string NoteHeading = "## Note";
        public const string ClipboardHeading = "## Clipboard";
        public const string NotesSection = "# Quick notes";
        public const string ClipboardSection = "# Clipboard history";
        /// <summary>Clipboard ids carry their algorithm, so a later version can change it without guessing.</summary>
        public const string HashPrefix = "sha256:";

        /// <summary>Spec section 7.4: the largest file the importer will open.</summary>
        public const long MaxFileBytes = 25L * 1024 * 1024;
        /// <summary>The history's own cap, measured the history's way (UTF-16 bytes).</summary>
        public const int MaxClipboardBytes = 128 * 1024;
        /// <summary>One metadata line, a note title included.</summary>
        public const int MaxMetadataChars = 4096;
        /// <summary>Objects imported by one operation.</summary>
        public const int MaxObjects = 10000;

        /// <summary>
        /// The clipboard history's id of a text: SHA-256 of its UTF-8 bytes, upper-case hex - exactly
        /// what <see cref="ClipboardHistoryService"/> has always stored, so an imported entry and a
        /// live copy of the same text are the same entry.
        /// </summary>
        public static string HistoryId(string text) => ClipboardHistoryService.Hash(text);

        /// <summary>The id as the file writes it: <c>sha256:</c> and lower-case hex.</summary>
        public static string FileId(string historyId) => HashPrefix + historyId.ToLowerInvariant();

        /// <summary>Whether a text fits the history, by the history's own measure.</summary>
        public static bool FitsHistory(string text) => Encoding.Unicode.GetByteCount(text) <= MaxClipboardBytes;
    }

    /// <summary>One <c>## Note</c> block as read from a file.</summary>
    internal sealed class ExchangeNote
    {
        /// <summary>Null for a block written by hand without an id - imported only on confirmation (spec 7.3).</summary>
        public Guid? Id { get; set; }
        public string? Title { get; set; }
        public QuickNoteKind Kind { get; set; } = QuickNoteKind.Text;
        public string RawText { get; set; } = "";
        public List<QuickNoteItem> Items { get; set; } = new List<QuickNoteItem>();
        /// <summary>Null only on a hand-written block without an id.</summary>
        public DateTime? CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        /// <summary>1-based line of the block heading, for the report.</summary>
        public int Line { get; set; }

        /// <summary>The note this block describes, with <paramref name="id"/> and the dates filled in.</summary>
        public QuickNote ToNote(Guid id, DateTime nowUtc)
        {
            DateTime created = CreatedAtUtc ?? UpdatedAtUtc ?? nowUtc;
            var note = new QuickNote
            {
                Id = id,
                Title = QuickNote.NormalizeTitle(Title),
                Kind = Kind,
                RawText = Kind == QuickNoteKind.Text ? RawText : "",
                CreatedAtUtc = created,
                UpdatedAtUtc = UpdatedAtUtc ?? created,
            };
            if (Kind == QuickNoteKind.Checklist)
                foreach (QuickNoteItem item in Items) note.Items.Add(item.Clone());
            note.RenumberItems();
            return note;
        }
    }

    /// <summary>One <c>## Clipboard</c> block as read from a file. Its id has been checked against its text.</summary>
    internal sealed class ExchangeClipboardItem
    {
        /// <summary>The history's own id: upper-case hex, no prefix.</summary>
        public string Uuid { get; set; } = "";
        public DateTime CopiedAtUtc { get; set; }
        public bool Pinned { get; set; }
        public string Text { get; set; } = "";
        public string SourceApp { get; set; } = "";
        public string SourceTitle { get; set; } = "";
        public int Line { get; set; }
    }

    /// <summary>Why a block, or the whole file, was not taken. The UI turns these into words.</summary>
    internal enum ExchangeProblemKind
    {
        /// <summary>The first non-empty line is not a CyrFlip exchange marker.</summary>
        NoMarker,
        /// <summary>A marker of another version (<c>v2</c>...).</summary>
        UnknownVersion,
        /// <summary>The file is over <see cref="CyrFlipExchange.MaxFileBytes"/>.</summary>
        FileTooLarge,
        /// <summary>A block has no text fence before the next heading or the end of the file.</summary>
        NoBody,
        /// <summary>A fence was opened and never closed - everything after it is lost.</summary>
        UnclosedFence,
        /// <summary>A required field is missing or does not parse (an id, a date, a kind).</summary>
        BadField,
        /// <summary>A clipboard block whose text does not hash to its id - edited, or damaged.</summary>
        HashMismatch,
        /// <summary>A text over its cap (history 128 KB, note 512 KB).</summary>
        TooLarge,
        /// <summary>A metadata line over <see cref="CyrFlipExchange.MaxMetadataChars"/>.</summary>
        MetadataTooLong,
        /// <summary>Past <see cref="CyrFlipExchange.MaxObjects"/> in one file.</summary>
        TooManyObjects,
    }

    internal sealed class ExchangeProblem
    {
        public int Line { get; set; }
        public ExchangeProblemKind Kind { get; set; }
        /// <summary>The field involved, when there is one (not translated - it is a name from the file).</summary>
        public string Detail { get; set; } = "";
    }

    /// <summary>Everything one read of a file produced.</summary>
    internal sealed class ExchangeReadResult
    {
        /// <summary>Set when the file as a whole was refused; nothing else is filled then.</summary>
        public ExchangeProblemKind? Fatal { get; set; }
        public List<ExchangeNote> Notes { get; } = new List<ExchangeNote>();
        public List<ExchangeClipboardItem> Clipboard { get; } = new List<ExchangeClipboardItem>();
        public List<ExchangeProblem> Problems { get; } = new List<ExchangeProblem>();

        /// <summary>Hand-written notes with no id: imported only after the user agrees.</summary>
        public int NotesWithoutId
        {
            get
            {
                int count = 0;
                foreach (ExchangeNote note in Notes) if (note.Id == null) count++;
                return count;
            }
        }

        public int ValidBlocks => Notes.Count + Clipboard.Count;
    }

    /// <summary>What an import would do, before it does it (spec 7.1), and what it did afterwards.</summary>
    internal sealed class ExchangeMergeReport
    {
        public int NotesAdded { get; set; }
        public int NotesUpdated { get; set; }
        public int NotesSkipped { get; set; }
        /// <summary>Hand-written notes without an id, counted apart: they are added only on confirmation.</summary>
        public int NotesNew { get; set; }
        public int ClipboardAdded { get; set; }
        /// <summary>Already there; raised to the newer date or pinned where the file says so.</summary>
        public int ClipboardExisting { get; set; }
        /// <summary>Of those, the entries the file moves: a newer date or a pin the local entry lacks.</summary>
        public int ClipboardRaised { get; set; }
    }
}
