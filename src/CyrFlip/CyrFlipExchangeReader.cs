using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// Reads "CyrFlip Exchange Text v1" (ticket S0023, spec sections 5 and 7.3-7.4). The file may have
    /// been opened, read and edited by a person on any device since CyrFlip wrote it, so the reader is
    /// strict about what it <b>takes</b> and lenient about what it <b>ignores</b>:
    /// <list type="bullet">
    /// <item>only complete <c>## Note</c> / <c>## Clipboard</c> blocks are taken - a valid id, the
    /// dates, a closed fence; free text, unknown headings and unknown fields are skipped silently;</item>
    /// <item>a broken block costs itself and is reported with its line; the blocks after it still
    /// import - except after a fence that is never closed, which swallows the rest of the file;</item>
    /// <item>a clipboard block's id must be the hash of its text, or it is refused - that is how an
    /// edited or damaged payload is told apart from the original.</item>
    /// </list>
    ///
    /// <para><b>Line endings.</b> The file is split on LF only, so a CR inside a payload survives. A
    /// file whose marker line ends in CR was converted to CRLF as a whole (an editor, a mail client),
    /// and there every line's trailing CR is taken off: the payload's own endings are then unknowable,
    /// so a clipboard text is accepted in whichever form - LF or CRLF - matches its hash.</para>
    ///
    /// <para>Nothing read here is ever executed, opened or interpreted: it is text, and it stays text.</para>
    /// </summary>
    internal static class CyrFlipExchangeReader
    {
        /// <summary>
        /// Read a file from disk. The size is checked before a byte is decoded; the encoding is UTF-8,
        /// with a BOM tolerated (Notepad adds one when the user saves an edit).
        /// </summary>
        public static ExchangeReadResult ReadFile(string path)
        {
            if (new FileInfo(path).Length > CyrFlipExchange.MaxFileBytes)
                return new ExchangeReadResult { Fatal = ExchangeProblemKind.FileTooLarge };
            using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return Read(reader);
        }

        public static ExchangeReadResult Parse(string content)
        {
            using var reader = new StringReader(content);
            return Read(reader);
        }

        public static ExchangeReadResult Read(TextReader input)
        {
            var result = new ExchangeReadResult();
            var lines = new LineSource(input);

            // The marker: the first non-empty line, and nothing else will do.
            string? first;
            while ((first = lines.Next()) != null && Structural(first).Trim().Length == 0) { }
            if (first == null) { result.Fatal = ExchangeProblemKind.NoMarker; return result; }
            string marker = Structural(first).Trim().TrimStart('﻿');
            if (marker != CyrFlipExchange.Marker)
            {
                result.Fatal = marker.StartsWith(CyrFlipExchange.MarkerPrefix, StringComparison.Ordinal)
                    ? ExchangeProblemKind.UnknownVersion : ExchangeProblemKind.NoMarker;
                return result;
            }
            bool crlf = first.EndsWith("\r", StringComparison.Ordinal);

            string? line;
            while ((line = lines.Next()) != null)
            {
                string heading = Structural(line).Trim();
                bool note = heading == CyrFlipExchange.NoteHeading;
                if (!note && heading != CyrFlipExchange.ClipboardHeading) continue;   // free text, a section, a field of nothing
                if (result.ValidBlocks >= CyrFlipExchange.MaxObjects)
                {
                    result.Problems.Add(new ExchangeProblem { Line = lines.Number, Kind = ExchangeProblemKind.TooManyObjects });
                    break;
                }
                if (!ReadBlock(lines, note, crlf, result)) break;
            }
            return result;
        }

        /// <summary>
        /// One block from its heading to its closing fence. Returns false only when the rest of the
        /// file cannot be read any more (a fence that never closes).
        /// </summary>
        private static bool ReadBlock(LineSource lines, bool isNote, bool crlf, ExchangeReadResult result)
        {
            int start = lines.Number;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ExchangeProblem? problem = null;
            string? line;
            while ((line = lines.Next()) != null)
            {
                string s = Structural(line);
                string t = s.Trim();
                if (t.Length == 0) continue;
                if (IsFenceOpen(t, out int fenceLength))
                {
                    List<string>? body = ReadBody(lines, fenceLength, crlf);
                    if (body == null)
                    {
                        result.Problems.Add(new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.UnclosedFence });
                        return false;
                    }
                    if (problem != null) { result.Problems.Add(problem); return true; }
                    problem = isNote ? TakeNote(fields, body, start, result) : TakeClipboard(fields, body, crlf, start, result);
                    if (problem != null) result.Problems.Add(problem);
                    return true;
                }
                if (t.StartsWith("#", StringComparison.Ordinal))
                {
                    // The next heading came before any text: this block has no body. The heading is
                    // handed back so the block it opens is read in its own right.
                    lines.PushBack(line);
                    result.Problems.Add(new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.NoBody });
                    return true;
                }
                int colon = s.IndexOf(':');
                if (colon <= 0) continue;   // a stray line among the fields: ignored, like any free text
                if (s.Length > CyrFlipExchange.MaxMetadataChars)
                {
                    problem ??= new ExchangeProblem { Line = lines.Number, Kind = ExchangeProblemKind.MetadataTooLong, Detail = s.Substring(0, colon).Trim() };
                    continue;
                }
                fields[s.Substring(0, colon).Trim()] = s.Substring(colon + 1).Trim();
            }
            result.Problems.Add(new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.NoBody });
            return true;
        }

        /// <summary>The lines between the fences, or null when the end of the file came first.</summary>
        private static List<string>? ReadBody(LineSource lines, int fenceLength, bool crlf)
        {
            var body = new List<string>();
            string? line;
            while ((line = lines.Next()) != null)
            {
                if (IsFenceClose(Structural(line).Trim(), fenceLength)) return body;
                body.Add(crlf ? Structural(line) : line);
            }
            return null;
        }

        private static ExchangeProblem? TakeNote(Dictionary<string, string> fields, List<string> body, int start, ExchangeReadResult result)
        {
            var note = new ExchangeNote { Line = start };
            if (fields.TryGetValue("Id", out string? id) && id.Length > 0)
            {
                if (!Guid.TryParse(id, out Guid guid)) return Bad(start, "Id");
                note.Id = guid;
            }
            if (fields.TryGetValue("Kind", out string? kind) && kind.Length > 0)
            {
                if (string.Equals(kind, "text", StringComparison.OrdinalIgnoreCase)) note.Kind = QuickNoteKind.Text;
                else if (string.Equals(kind, "checklist", StringComparison.OrdinalIgnoreCase)) note.Kind = QuickNoteKind.Checklist;
                else return Bad(start, "Kind");
            }
            // A note with an id is an existing note, and merging it needs both dates (spec 7.2). A
            // hand-written one without an id may leave them out - it becomes a new note anyway.
            if (!Date(fields, "Created-At", note.Id != null, out DateTime? created)) return Bad(start, "Created-At");
            if (!Date(fields, "Updated-At", note.Id != null, out DateTime? updated)) return Bad(start, "Updated-At");
            note.CreatedAtUtc = created;
            note.UpdatedAtUtc = updated;
            if (fields.TryGetValue("Title", out string? title)) note.Title = QuickNote.NormalizeTitle(title);

            string text = string.Join("\n", body);
            if (QuickNote.ExceedsLimit(text)) return new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.TooLarge };
            if (note.Kind == QuickNoteKind.Text) note.RawText = text;
            else note.Items = ParseItems(body);
            result.Notes.Add(note);
            return null;
        }

        private static ExchangeProblem? TakeClipboard(Dictionary<string, string> fields, List<string> body, bool crlf, int start, ExchangeReadResult result)
        {
            if (!fields.TryGetValue("Id", out string? id) || !id.StartsWith(CyrFlipExchange.HashPrefix, StringComparison.OrdinalIgnoreCase))
                return Bad(start, "Id");
            string hex = id.Substring(CyrFlipExchange.HashPrefix.Length).Trim();
            if (hex.Length != 64 || !IsHex(hex)) return Bad(start, "Id");
            if (!Date(fields, "Copied-At", required: true, out DateTime? copied)) return Bad(start, "Copied-At");
            bool pinned = false;
            if (fields.TryGetValue("Pinned", out string? pin) && pin.Length > 0)
            {
                if (string.Equals(pin, "true", StringComparison.OrdinalIgnoreCase)) pinned = true;
                else if (!string.Equals(pin, "false", StringComparison.OrdinalIgnoreCase)) return Bad(start, "Pinned");
            }

            // The payload's own line endings: verbatim first, then the two forms a conversion could
            // have produced. Whichever hashes to the id is the original.
            string uuid = hex.ToUpperInvariant();
            string? text = null;
            foreach (string candidate in Candidates(body, crlf))
                if (CyrFlipExchange.HistoryId(candidate) == uuid) { text = candidate; break; }
            if (text == null) return new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.HashMismatch };
            if (!CyrFlipExchange.FitsHistory(text)) return new ExchangeProblem { Line = start, Kind = ExchangeProblemKind.TooLarge };

            result.Clipboard.Add(new ExchangeClipboardItem
            {
                Uuid = uuid,
                CopiedAtUtc = copied!.Value,
                Pinned = pinned,
                Text = text,
                SourceApp = fields.TryGetValue("Source-Application", out string? app) ? app : "",
                SourceTitle = fields.TryGetValue("Source-Window", out string? window) ? window : "",
                Line = start,
            });
            return null;
        }

        private static IEnumerable<string> Candidates(List<string> body, bool crlf)
        {
            if (!crlf) yield return string.Join("\n", body);
            var bare = new List<string>(body.Count);
            foreach (string line in body) bare.Add(Structural(line));
            yield return string.Join("\n", bare);
            yield return string.Join("\r\n", bare);
        }

        /// <summary>
        /// <c>- [ ] text</c> / <c>- [x] text</c>, one item per line (spec 5.3). A line of another shape
        /// is still an item, unticked, with the whole line as its text: a person adding a line to a
        /// list on a phone should get an item, not a refusal.
        /// </summary>
        internal static List<QuickNoteItem> ParseItems(List<string> body)
        {
            var items = new List<QuickNoteItem>();
            if (body.Count == 1 && Structural(body[0]).Length == 0) return items;   // an empty list
            foreach (string raw in body)
            {
                string line = Structural(raw);
                var item = new QuickNoteItem { Position = items.Count, Text = line };
                if (line.Length >= 5 && line[0] == '-' && line[1] == ' ' && line[2] == '[' && line[4] == ']')
                {
                    char mark = line[3];
                    if (mark == ' ' || mark == 'x' || mark == 'X')
                    {
                        item.IsChecked = mark != ' ';
                        // "- [ ] " carries one space before the text; a bare "- [ ]" is an empty item.
                        item.Text = line.Length > 5 && line[5] == ' ' ? line.Substring(6) : line.Substring(5);
                    }
                }
                items.Add(item);
            }
            return items;
        }

        private static ExchangeProblem Bad(int line, string field)
            => new ExchangeProblem { Line = line, Kind = ExchangeProblemKind.BadField, Detail = field };

        private static bool Date(Dictionary<string, string> fields, string name, bool required, out DateTime? value)
        {
            value = null;
            if (!fields.TryGetValue(name, out string? text) || text.Length == 0) return !required;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed))
                return false;
            value = parsed.UtcDateTime;
            return true;
        }

        private static bool IsFenceOpen(string trimmed, out int length)
        {
            length = 0;
            while (length < trimmed.Length && trimmed[length] == '`') length++;
            // Markdown: three or more backticks, and no backtick in the info string after them.
            return length >= 3 && trimmed.IndexOf('`', length) < 0;
        }

        private static bool IsFenceClose(string trimmed, int length)
        {
            if (trimmed.Length < length) return false;
            foreach (char c in trimmed) if (c != '`') return false;
            return true;
        }

        private static bool IsHex(string text)
        {
            foreach (char c in text)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
            return true;
        }

        /// <summary>A line without its trailing CR - what every structural check looks at.</summary>
        private static string Structural(string line)
            => line.Length > 0 && line[line.Length - 1] == '\r' ? line.Substring(0, line.Length - 1) : line;

        /// <summary>
        /// Lines split on LF alone. <see cref="TextReader.ReadLine"/> would also split on a lone CR and
        /// eat the CR of a CRLF - both are part of a payload here.
        /// </summary>
        private sealed class LineSource
        {
            private readonly TextReader _input;
            private readonly StringBuilder _line = new StringBuilder();
            private string? _pushedBack;
            private bool _ended;

            public LineSource(TextReader input) { _input = input; }

            /// <summary>1-based number of the line last returned.</summary>
            public int Number { get; private set; }

            public void PushBack(string line) { _pushedBack = line; Number--; }

            public string? Next()
            {
                if (_pushedBack != null)
                {
                    string back = _pushedBack;
                    _pushedBack = null;
                    Number++;
                    return back;
                }
                if (_ended) return null;
                _line.Clear();
                int c;
                while ((c = _input.Read()) >= 0)
                {
                    if (c == '\n') { Number++; return _line.ToString(); }
                    _line.Append((char)c);
                }
                _ended = true;
                // The last line: returned when it holds something. A file ending in LF has no line after it.
                if (_line.Length == 0) return null;
                Number++;
                return _line.ToString();
            }
        }
    }
}
