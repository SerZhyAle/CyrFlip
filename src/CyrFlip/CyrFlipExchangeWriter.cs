using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Writes "CyrFlip Exchange Text v1" (ticket S0023, spec section 5). The file is open on purpose:
    /// UTF-8 without a BOM, LF between the lines the writer itself produces, every payload verbatim
    /// inside a fence - no encryption, no base64, no escaping. It is what the user asked to carry to
    /// another machine or read on a phone, and a format that needed CyrFlip to be read would defeat
    /// the point.
    ///
    /// <para><b>The text is never touched.</b> A payload's own line endings (a CRLF copied from
    /// Notepad) go into the file as they are; only the framing lines are LF. The fence is a run of
    /// backticks longer than any run in the text (spec 5.4), so Markdown and code with their own
    /// <c>```</c> survive, and nothing inside a fence can end it.</para>
    ///
    /// <para><b>Dates are truncated to the second</b>, never rounded up: an export must not claim a
    /// note is newer than the note it came from, or re-importing it would "update" the original with
    /// itself.</para>
    /// </summary>
    internal static class CyrFlipExchangeWriter
    {
        /// <summary>UTF-8 without the BOM (spec 5.1) - a BOM is an invisible character on a phone.</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// The whole file as a string. A null list means "not part of this export" and leaves its
        /// section out; an empty one is written as an empty section, which says "chosen, nothing there".
        /// </summary>
        public static string ToText(DateTime exportedAtUtc, IEnumerable<QuickNote>? notes, IEnumerable<ClipboardHistoryEntry>? clipboard)
        {
            var text = new StringBuilder();
            Line(text, CyrFlipExchange.Marker);
            Line(text, "Exported-At: " + Date(exportedAtUtc));
            Line(text, "Application: CyrFlip");
            var includes = new List<string>();
            if (notes != null) includes.Add("quick-notes");
            if (clipboard != null) includes.Add("clipboard-history");
            Line(text, "Includes: " + string.Join(", ", includes));

            if (notes != null)
            {
                Line(text, "");
                Line(text, CyrFlipExchange.NotesSection);
                foreach (QuickNote note in notes)
                {
                    Line(text, "");
                    Line(text, CyrFlipExchange.NoteHeading);
                    Line(text, "Id: " + note.Id.ToString("D"));
                    if (note.Title != null) Field(text, "Title", note.Title);
                    Line(text, "Kind: " + (note.Kind == QuickNoteKind.Checklist ? "checklist" : "text"));
                    Line(text, "Created-At: " + Date(note.CreatedAtUtc));
                    Line(text, "Updated-At: " + Date(note.UpdatedAtUtc));
                    Line(text, "");
                    Fenced(text, note.Body());
                }
            }

            if (clipboard != null)
            {
                Line(text, "");
                Line(text, CyrFlipExchange.ClipboardSection);
                foreach (ClipboardHistoryEntry entry in clipboard)
                {
                    Line(text, "");
                    Line(text, CyrFlipExchange.ClipboardHeading);
                    // Recomputed rather than copied from the entry: the file's promise is that the id
                    // is the hash of the text beside it, and that is checked on import.
                    Line(text, "Id: " + CyrFlipExchange.FileId(CyrFlipExchange.HistoryId(entry.Text)));
                    Line(text, "Copied-At: " + Date(entry.CreatedAt));
                    Line(text, "Pinned: " + (entry.IsPinned ? "true" : "false"));
                    if (entry.SourceApp.Length > 0) Field(text, "Source-Application", entry.SourceApp);
                    if (entry.SourceTitle.Length > 0) Field(text, "Source-Window", entry.SourceTitle);
                    Line(text, "");
                    Fenced(text, entry.Text);
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// The history entries an export takes (spec 6.1), in the history's own display order - pinned
        /// first, then newest first - which is also the order the file is meant to be read in.
        /// <paramref name="entries"/> is expected in that order already (it is <c>ClipboardHistoryService.Entries</c>).
        /// </summary>
        public static List<ClipboardHistoryEntry> SelectHistory(IEnumerable<ClipboardHistoryEntry> entries,
            bool pinned, bool rest, ExchangeHistoryScope scope, DateTime nowUtc)
        {
            var chosen = new List<ClipboardHistoryEntry>();
            int unpinned = 0;
            int limit = scope == ExchangeHistoryScope.Last50 ? 50 : scope == ExchangeHistoryScope.Last100 ? 100 : int.MaxValue;
            DateTime since = scope == ExchangeHistoryScope.Last7Days ? nowUtc.AddDays(-7) : DateTime.MinValue;
            foreach (ClipboardHistoryEntry entry in entries)
            {
                if (entry.IsPinned)
                {
                    if (pinned) chosen.Add(entry);
                    continue;
                }
                if (!rest || unpinned >= limit || entry.CreatedAt < since) continue;
                chosen.Add(entry);
                unpinned++;
            }
            return chosen;
        }

        /// <summary>
        /// Write the file atomically (spec 6.2): the whole text goes into a temporary file in the
        /// target folder, which then takes the final name in one step. A failure leaves an existing
        /// file at that name exactly as it was.
        /// </summary>
        public static void WriteFile(string path, string content)
        {
            string full = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(full) ?? ".";
            string temp = Path.Combine(folder, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, content, Utf8);
                if (File.Exists(full)) File.Replace(temp, full, null);
                else File.Move(temp, full);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* a leftover temp is harmless */ }
            }
        }

        /// <summary>
        /// The opening/closing fence for <paramref name="payload"/>: three backticks, or one more than
        /// the longest run of backticks anywhere in the text.
        /// </summary>
        internal static string Fence(string payload)
        {
            int longest = 0, run = 0;
            foreach (char c in payload)
            {
                run = c == '`' ? run + 1 : 0;
                if (run > longest) longest = run;
            }
            return new string('`', Math.Max(3, longest + 1));
        }

        /// <summary>
        /// A payload between fences. Every line of the text is followed by one LF, so the lines
        /// between the fences, joined with LF, are the text - an empty text is one empty line, a text
        /// ending in a newline ends in an empty line.
        /// </summary>
        private static void Fenced(StringBuilder text, string payload)
        {
            string fence = Fence(payload);
            Line(text, fence + "text");
            text.Append(payload).Append('\n');
            Line(text, fence);
        }

        /// <summary>
        /// A metadata line. Its value is one line by definition (spec 5.4): any line break becomes a
        /// space, and the value is cut to what the reader accepts.
        /// </summary>
        private static void Field(StringBuilder text, string name, string value)
        {
            var clean = new StringBuilder(value.Length);
            foreach (char c in value)
                clean.Append(c == '\r' || c == '\n' || c == '\u0085' || c == (char)0x2028 || c == (char)0x2029 ? ' ' : c);
            string line = clean.ToString().Trim();
            int room = CyrFlipExchange.MaxMetadataChars - name.Length - 2;
            if (line.Length > room) line = line.Substring(0, room);
            Line(text, name + ": " + line);
        }

        private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

        internal static string Date(DateTime utc)
        {
            DateTime value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
            value = new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }
}
