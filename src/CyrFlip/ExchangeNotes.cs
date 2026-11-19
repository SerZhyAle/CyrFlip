using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The notes of an account on the exchange server (<c>DEVICE-EXCHANGE</c> 1.13, items FG to FM, ticket S0105):
    /// the constants the wire and the sync share. The packet shape, the merge rule and the deletion rule are the
    /// catalog's; nothing here is CyrFlip's own invention.
    /// </summary>
    internal static class ExchangeNotes
    {
        /// <summary>The box context of one note (items FG and DH): the reader opens a note's box only under it.</summary>
        public static string BoxContext(Guid noteId) => "note:" + NoteIdOf(noteId);

        /// <summary>A <c>noteId</c> on the wire: a UUID in the lower-case 8-4-4-4-12 form (item FG).</summary>
        public static string NoteIdOf(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

        private static readonly Regex UuidForm = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.CultureInvariant);

        /// <summary>Whether a wire id is in the contract's form - an upper-case or braced id is refused, not repaired.</summary>
        public static bool TryParseNoteId(string? text, out Guid id)
        {
            id = Guid.Empty;
            return text != null && UuidForm.IsMatch(text) && Guid.TryParse(text, out id);
        }

        /// <summary>
        /// The refusal a client resolves by item FJ and never shows as a failure; its message row is item FL's.
        /// </summary>
        public const string ReasonNoteConflict = ExchangeCodes.NoteConflict;

        /// <summary>The longest clear payload of one note (item FH): above it the note stays on its device.</summary>
        public const int MaxPayloadBytes = 131072;

        /// <summary>The most recipients of one box (item DH).</summary>
        public const int MaxRecipients = 40;
    }

    /// <summary>One record of the account's notes store, as <c>notes_changes</c> and a conflict's <c>current</c> carry it (item FG).</summary>
    internal sealed class ExchangeNoteRecord
    {
        public string NoteId = "";
        public long Revision;
        public string StoredAt = "";
        public string SourceDeviceId = "";
        public bool Deleted;
        /// <summary>The box as JSON text; empty on a tombstone.</summary>
        public string BoxJson = "";

        internal static ExchangeNoteRecord From(ExchangeFrame f) => new ExchangeNoteRecord
        {
            NoteId = f.Str("noteId"),
            Revision = f.Long("revision"),
            StoredAt = f.Str("storedAt"),
            SourceDeviceId = f.Str("sourceDeviceId"),
            Deleted = f.Bool("deleted"),
            BoxJson = f.JsonOf("sealed"),
        };
    }

    /// <summary>The answer to <c>notes_list</c> (item FI).</summary>
    internal sealed class ExchangeNotesChanges
    {
        public long NotesRevision;
        public long NotesHorizon;
        public List<ExchangeNoteRecord> Records = new List<ExchangeNoteRecord>();
        /// <summary>The frame could not hold every record: ask again from the last revision received.</summary>
        public bool More;
        /// <summary><c>since</c> was below the horizon: every record of the store is listed, and a note in none was deleted.</summary>
        public bool Full;

        internal static ExchangeNotesChanges From(ExchangeFrame f)
        {
            var c = new ExchangeNotesChanges
            {
                NotesRevision = f.Long("notesRevision"),
                NotesHorizon = f.Long("notesHorizon"),
                More = f.Bool("more"),
                Full = f.Bool("full"),
            };
            foreach (ExchangeFrame r in f.Objects("records")) c.Records.Add(ExchangeNoteRecord.From(r));
            return c;
        }
    }

    /// <summary>
    /// The answer to <c>notes_put</c>: stored, or a <c>note-conflict</c> carrying the stored record (item FJ). A conflict
    /// is a result, not a failure - the sync resolves it; every other refusal is a failure code.
    /// </summary>
    internal sealed class ExchangeNotePutResult
    {
        public string NoteId = "";
        public long Revision;
        public string StoredAt = "";
        /// <summary>Set on <c>note-conflict</c>: the record the server holds, box included. Nothing was stored.</summary>
        public ExchangeNoteRecord? Conflict;

        public bool Stored => Conflict == null;
    }

    /// <summary>
    /// The clear payload inside a note's box (item FH): the UTF-8 bytes of one JSON object, at most
    /// <see cref="ExchangeNotes.MaxPayloadBytes"/>. Pure - it knows no store, no box and no wire.
    ///
    /// <para>The member order of <see cref="Encode"/> is fixed (formatVersion, title, kind, text or items, createdAt,
    /// updatedAt; an item's itemId, text, checked, position), so the same note always gives the same bytes and the
    /// catalog's payload samples can be compared byte for byte. A text body travels with LF line ends (item CB's
    /// rule); nothing else of it is touched.</para>
    /// </summary>
    internal static class ExchangeNotePayload
    {
        public const int FormatVersion = 1;

        private const string KindText = "text";
        private const string KindChecklist = "checklist";

        /// <summary>The payload of a note. Says nothing about the limit - see <see cref="ExceedsLimit"/>.</summary>
        public static byte[] Encode(QuickNote note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            DateTime created = note.CreatedAtUtc == default ? note.UpdatedAtUtc : note.CreatedAtUtc;
            DateTime updated = note.UpdatedAtUtc == default ? created : note.UpdatedAtUtc;

            var root = new ExchangeMembers.Plain()
                .Add("formatVersion", FormatVersion)
                .Add("title", QuickNote.NormalizeTitle(note.Title));
            if (note.Kind == QuickNoteKind.Checklist)
            {
                var items = new List<QuickNoteItem>(note.Items);
                items.Sort((a, b) => a.Position.CompareTo(b.Position));
                var list = new List<object>();
                foreach (QuickNoteItem item in items)
                    list.Add(new ExchangeMembers.Plain()
                        .Add("itemId", ExchangeNotes.NoteIdOf(item.Id))
                        .Add("text", item.Text ?? "")
                        .Add("checked", item.IsChecked)
                        .Add("position", item.Position));
                root.Add("kind", KindChecklist).Add("items", list);
            }
            else
            {
                root.Add("kind", KindText).Add("text", ToLf(note.RawText ?? ""));
            }
            root.Add("createdAt", Rfc3339(created)).Add("updatedAt", Rfc3339(updated));

            var sb = new StringBuilder();
            ExchangeJson.WriteValue(sb, root);
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        public static bool ExceedsLimit(byte[] payload) => payload.Length > ExchangeNotes.MaxPayloadBytes;

        /// <summary>
        /// Whether the note's payload is above item FH's limit, for the quick-notes window's line (assumption A4). A note
        /// whose texts are well under the limit is answered without encoding it, so a keystroke costs a count, not a copy;
        /// the halving covers the doubling escapes (line breaks, tabs, quotes). The engine's check is the exact one.
        /// </summary>
        public static bool TooLargeToSync(QuickNote note)
        {
            long bytes = System.Text.Encoding.UTF8.GetByteCount(note.RawText ?? "") + System.Text.Encoding.UTF8.GetByteCount(note.Title ?? "");
            foreach (QuickNoteItem item in note.Items) bytes += System.Text.Encoding.UTF8.GetByteCount(item.Text ?? "") + 128;
            if (bytes < ExchangeNotes.MaxPayloadBytes / 2) return false;
            return ExceedsLimit(Encode(note));
        }

        /// <summary>
        /// Reads a payload into a note with the given id. <paramref name="failure"/> is
        /// <see cref="ExchangeCodes.FormatUnsupported"/> for a <c>formatVersion</c> or <c>kind</c> above what this
        /// build implements - the note is then neither changed nor deleted locally (item FH) - and
        /// <see cref="ExchangeCodes.Damaged"/> for anything that is not a payload. Unknown members are ignored.
        /// </summary>
        public static bool TryDecode(byte[]? payload, Guid noteId, out QuickNote? note, out string failure)
        {
            note = null;
            failure = ExchangeCodes.Damaged;
            if (payload == null || payload.Length == 0 || payload.Length > ExchangeNotes.MaxPayloadBytes) return false;

            Dictionary<string, object>? root;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(payload);
                root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is DecoderFallbackException)
            {
                return false;
            }
            if (root == null) return false;

            if (!root.TryGetValue("formatVersion", out object? fv) || !(fv is int version) || version < 1) return false;
            if (version > FormatVersion) { failure = ExchangeCodes.FormatUnsupported; return false; }

            string? title = null;
            if (root.TryGetValue("title", out object? t) && t != null)
            {
                if (!(t is string ts)) return false;
                title = QuickNote.NormalizeTitle(ts);
            }

            if (!TryDate(root, "createdAt", out DateTime created) || !TryDate(root, "updatedAt", out DateTime updated)) return false;

            if (!root.TryGetValue("kind", out object? k) || !(k is string kind)) return false;
            var result = new QuickNote { Id = noteId, Title = title, CreatedAtUtc = created, UpdatedAtUtc = updated };
            if (kind == KindText)
            {
                if (!root.TryGetValue("text", out object? body) || !(body is string text)) return false;
                result.Kind = QuickNoteKind.Text;
                result.RawText = text;
            }
            else if (kind == KindChecklist)
            {
                result.Kind = QuickNoteKind.Checklist;
                if (root.TryGetValue("items", out object? raw) && raw != null)
                {
                    if (!(raw is object[] arr)) return false;
                    foreach (object entry in arr)
                    {
                        if (!(entry is Dictionary<string, object> item)) return false;
                        if (!item.TryGetValue("itemId", out object? iid) || !(iid is string idText) || !ExchangeNotes.TryParseNoteId(idText, out Guid itemId)) return false;
                        if (!item.TryGetValue("text", out object? it) || !(it is string itemText)) return false;
                        if (!item.TryGetValue("checked", out object? ic) || !(ic is bool isChecked)) return false;
                        if (!item.TryGetValue("position", out object? ip) || !(ip is int position)) return false;
                        result.Items.Add(new QuickNoteItem { Id = itemId, Text = itemText, IsChecked = isChecked, Position = position });
                    }
                    result.Items.Sort((a, b) => a.Position.CompareTo(b.Position));
                }
            }
            else
            {
                failure = ExchangeCodes.FormatUnsupported; // a kind a later format version added
                return false;
            }

            note = result;
            failure = "";
            return true;
        }

        /// <summary>
        /// Whether two notes carry the same synced content - what decides "the local copy did not change" when a
        /// record comes back. Line ends of a text body are compared as LF, the way the payload carries them.
        /// </summary>
        public static bool SameContent(QuickNote a, QuickNote b)
        {
            if (a == null || b == null) return false;
            byte[] x = Encode(a), y = Encode(b);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        internal static string ToLf(string text)
            => text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');

        internal static string Rfc3339(DateTime utc)
            => DateTime.SpecifyKind(utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private static bool TryDate(Dictionary<string, object> root, string name, out DateTime utc)
        {
            utc = default;
            if (!root.TryGetValue(name, out object? v) || !(v is string s) || s.IndexOf('T') < 0) return false;
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)) return false;
            utc = dto.UtcDateTime;
            return true;
        }
    }
}
