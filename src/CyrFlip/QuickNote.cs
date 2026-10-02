using System;
using System.Collections.Generic;
using System.Text;

namespace CyrFlip
{
    /// <summary>A note is either a block of raw text or a flat list of tickable items. Nothing else.</summary>
    internal enum QuickNoteKind
    {
        Text,
        Checklist,
    }

    /// <summary>One line of a checklist note.</summary>
    internal sealed class QuickNoteItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = "";
        public bool IsChecked { get; set; }
        public int Position { get; set; }

        public QuickNoteItem Clone() => new QuickNoteItem
        {
            Id = Id,
            Text = Text,
            IsChecked = IsChecked,
            Position = Position,
        };
    }

    /// <summary>
    /// One quick note (spec §5.1). The whole point of the feature is that a fragment of code comes
    /// back out exactly as it went in, so this type deliberately has <b>no</b> automatic title, no
    /// rich text, no formatter and no normalization of <see cref="RawText"/>: what the user typed or
    /// pasted is what is stored, tabs, line endings and all.
    ///
    /// <para>Two invariants are worth naming because both were decisions rather than omissions.
    /// <see cref="CreatedAtUtc"/> is assigned once, at the first successful save, and never changes -
    /// it is what the standard list order is built on, so editing an old note must not move it.
    /// <see cref="Title"/> may be <c>null</c>, and a blank one is stored as <c>null</c> rather than
    /// as the caption «Без имени»: the list's preview is derived on the way to the screen
    /// (<see cref="Preview"/>) and never written back, so changing the first line later cannot
    /// corrupt either the model or the search.</para>
    /// </summary>
    internal sealed class QuickNote
    {
        /// <summary>
        /// The size cap for one note (spec §5.1). A paste over this is refused with a message rather
        /// than truncated: half a code fragment that looks whole is the worse outcome.
        /// </summary>
        public const int MaxBytes = 512 * 1024;

        public Guid Id { get; set; } = Guid.NewGuid();
        /// <summary>Assigned at the first save and immutable afterwards - the standard list order.</summary>
        public DateTime CreatedAtUtc { get; set; }
        /// <summary>Metadata only: it never influences the standard order (spec §3.5).</summary>
        public DateTime UpdatedAtUtc { get; set; }
        /// <summary>Null means the note genuinely has no name - not the string «Без имени».</summary>
        public string? Title { get; set; }
        public QuickNoteKind Kind { get; set; } = QuickNoteKind.Text;
        /// <summary>Only for <see cref="QuickNoteKind.Text"/>, stored exactly as entered.</summary>
        public string RawText { get; set; } = "";
        /// <summary>Only for <see cref="QuickNoteKind.Checklist"/>.</summary>
        public List<QuickNoteItem> Items { get; set; } = new List<QuickNoteItem>();

        /// <summary>
        /// A note nobody has put anything into. Such a note is never created when the window closes -
        /// opening the editor and changing your mind must not leave a row behind.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return false;
                if (Kind == QuickNoteKind.Text) return RawText.Length == 0;
                foreach (QuickNoteItem item in Items)
                    if (item.Text.Length > 0) return false;
                return true;
            }
        }

        public bool HasCheckedItems
        {
            get
            {
                foreach (QuickNoteItem item in Items)
                    if (item.IsChecked) return true;
                return false;
            }
        }

        /// <summary>
        /// Blank is not a name. A title of spaces would otherwise sort, search and display as a real
        /// one while being invisible on screen.
        /// </summary>
        public static string? NormalizeTitle(string? title)
            => string.IsNullOrWhiteSpace(title) ? null : title!.Trim();

        /// <summary>
        /// What the list shows for a note: its name, else the first non-empty line of the body. This
        /// is <b>display only</b> and is never written into <see cref="Title"/> (spec §3.3), so
        /// editing the first line later changes the preview and nothing else.
        /// </summary>
        public string Preview(int maxLength = 80)
        {
            string source = Title ?? FirstNonEmptyLine();
            source = source.Replace('\t', ' ').Trim();
            if (source.Length <= maxLength) return source;
            return source.Substring(0, Math.Max(1, maxLength - 1)).TrimEnd() + "…";
        }

        private string FirstNonEmptyLine()
        {
            if (Kind == QuickNoteKind.Checklist)
            {
                foreach (QuickNoteItem item in Items)
                    if (item.Text.Trim().Length > 0) return item.Text;
                return "";
            }
            foreach (string line in SplitLines(RawText))
                if (line.Trim().Length > 0) return line;
            return "";
        }

        /// <summary>
        /// Case-insensitive substring search over the name, the raw text and every item (spec §4).
        /// Ordinal-ignore-case rather than a culture comparison: the haystack is as often code as it
        /// is prose, and a culture-aware match would make "HttpClient" depend on the current locale.
        /// </summary>
        public bool Matches(string? query)
        {
            string needle = query?.Trim() ?? "";
            if (needle.Length == 0) return true;
            if (Title != null && Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (RawText.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (QuickNoteItem item in Items)
                if (item.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Whether switching to <paramref name="target"/> would lose something the user can see
        /// (spec §3.2). Both answers are facts about this note, not guesses:
        /// <list type="bullet">
        /// <item>to <see cref="QuickNoteKind.Text"/>: the tick marks have nowhere to go;</item>
        /// <item>to <see cref="QuickNoteKind.Checklist"/>: items hold no line endings, so text that
        /// carries a carriage return comes back joined with plain newlines.</item>
        /// </list>
        /// </summary>
        public bool LosesDataConvertingTo(QuickNoteKind target)
        {
            if (target == Kind) return false;
            return target == QuickNoteKind.Text
                ? HasCheckedItems
                : RawText.IndexOf('\r') >= 0;
        }

        /// <summary>Switch the note's type, carrying the body across (see <see cref="LosesDataConvertingTo"/>).</summary>
        public void ConvertTo(QuickNoteKind target)
        {
            if (target == Kind) return;
            if (target == QuickNoteKind.Checklist)
            {
                Items = SplitIntoItems(RawText);
                RawText = "";
            }
            else
            {
                RawText = JoinItems(Items);
                Items = new List<QuickNoteItem>();
            }
            Kind = target;
        }

        /// <summary>One item per line, in order. Blank lines survive as blank items.</summary>
        public static List<QuickNoteItem> SplitIntoItems(string raw)
        {
            var items = new List<QuickNoteItem>();
            if (raw.Length == 0) return items;
            string[] lines = SplitLines(raw);
            for (int i = 0; i < lines.Length; i++)
                items.Add(new QuickNoteItem { Text = lines[i], Position = i });
            return items;
        }

        /// <summary>
        /// One line per item. Joined with "\n" and not with the platform's newline on purpose: the
        /// items never held a line ending, so inventing CRLF here would be a claim about the original
        /// text that nothing supports.
        /// </summary>
        public static string JoinItems(IEnumerable<QuickNoteItem> items)
        {
            var text = new StringBuilder();
            bool first = true;
            foreach (QuickNoteItem item in items)
            {
                if (!first) text.Append('\n');
                text.Append(item.Text);
                first = false;
            }
            return text.ToString();
        }

        /// <summary>Renumber <see cref="QuickNoteItem.Position"/> to 0..n-1 in the current list order.</summary>
        public void RenumberItems()
        {
            for (int i = 0; i < Items.Count; i++) Items[i].Position = i;
        }

        /// <summary>Order the items by their stored position - what the store hands back after a replay.</summary>
        public void SortItems()
        {
            Items.Sort((a, b) => a.Position.CompareTo(b.Position));
            RenumberItems();
        }

        /// <summary>
        /// The note as plain text for Copy and for the .txt export (spec §6.1): the name when there is
        /// one, then the body. Dates are left out unless asked for, so a code fragment can be pasted
        /// straight back where it came from.
        /// </summary>
        public string ToPlainText(bool metadata = false)
        {
            var text = new StringBuilder();
            if (Title != null) text.Append(Title).Append('\n');
            if (metadata)
                text.Append(CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                    .Append(" / ").Append(UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append('\n');
            if (text.Length > 0) text.Append('\n');
            text.Append(Body());
            return text.ToString();
        }

        /// <summary>The body alone: the exact raw text, or one Markdown task line per item.</summary>
        public string Body()
        {
            if (Kind == QuickNoteKind.Text) return RawText;
            var text = new StringBuilder();
            bool first = true;
            foreach (QuickNoteItem item in Items)
            {
                if (!first) text.Append('\n');
                text.Append(item.IsChecked ? "- [x] " : "- [ ] ").Append(item.Text);
                first = false;
            }
            return text.ToString();
        }

        /// <summary>
        /// One note as a Markdown section for the "export everything" file. The name becomes a
        /// heading; an unnamed note gets no invented one, only the separator above it - the model
        /// has no automatic title and the export must not create one either.
        /// </summary>
        public string ToMarkdown(bool metadata = false)
        {
            var text = new StringBuilder();
            if (Title != null) text.Append("## ").Append(Title).Append('\n').Append('\n');
            if (metadata)
                text.Append('_').Append(CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append('_')
                    .Append('\n').Append('\n');
            text.Append(Body());
            return text.ToString();
        }

        /// <summary>
        /// What goes to the clipboard for a manual transfer into Google Keep (spec §6.2). Checklist
        /// items travel as plain lines, because that is what Keep turns into list items on paste -
        /// the tick marks do not survive, which is why the UI says so out loud rather than letting
        /// the user discover it in Keep.
        /// </summary>
        public string ToKeepText()
        {
            var text = new StringBuilder();
            if (Title != null) text.Append(Title).Append('\n').Append('\n');
            text.Append(Kind == QuickNoteKind.Text ? RawText : JoinItems(Items));
            return text.ToString();
        }

        /// <summary>UTF-8 size of the whole body, against <see cref="MaxBytes"/>.</summary>
        public static bool ExceedsLimit(string text) => Encoding.UTF8.GetByteCount(text) > MaxBytes;

        public QuickNote Clone()
        {
            var copy = new QuickNote
            {
                Id = Id,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                Title = Title,
                Kind = Kind,
                RawText = RawText,
                Items = new List<QuickNoteItem>(Items.Count),
            };
            foreach (QuickNoteItem item in Items) copy.Items.Add(item.Clone());
            return copy;
        }

        /// <summary>Split on CRLF, LF or a lone CR without touching anything else in the text.</summary>
        internal static string[] SplitLines(string raw)
            => raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>Returns the number of lines in the note body.</summary>
        public int LineCount
        {
            get
            {
                if (Kind == QuickNoteKind.Text)
                    return SplitLines(RawText).Length;
                return Items.Count;
            }
        }

        /// <summary>Returns the total number of characters in the note body.</summary>
        public int CharacterCount
        {
            get
            {
                if (Kind == QuickNoteKind.Text)
                    return RawText.Length;
                int count = 0;
                foreach (QuickNoteItem item in Items)
                    count += item.Text.Length;
                return count;
            }
        }
    }
}
