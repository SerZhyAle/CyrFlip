using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>One row of the menu that comes from a user-editable table (a conversion, a translation).</summary>
    internal readonly struct TextMenuRow
    {
        /// <summary>What the settings table calls it - "EN ⇄ RU", "Українська".</summary>
        public readonly string Label;
        /// <summary>The row's chord, shown right-aligned so the menu also teaches the hotkeys.</summary>
        public readonly string Shortcut;
        /// <summary>The profile id handed back to the same handler the keyboard hook calls.</summary>
        public readonly string Id;

        public TextMenuRow(string label, string shortcut, string id)
        {
            Label = label; Shortcut = shortcut; Id = id;
        }
    }

    /// <summary>Everything the menu's shape depends on, so building it stays a pure function.</summary>
    internal sealed class TextContextMenuState
    {
        public SelectionState Selection { get; set; } = SelectionState.Unknown;
        public bool ClipboardHasText { get; set; }
        /// <summary>
        /// Set when the selection itself could be read and turned out to be something openable - a
        /// link, a file, a folder. Null is the ordinary case, and then the menu has no launch item:
        /// an entry that appears over every piece of plain text and fails would be worse than absent.
        /// </summary>
        public LaunchTarget? Launch { get; set; }
        /// <summary>
        /// Lines and characters of the selection, as read (never copied). Zero characters means the
        /// selection could not be read at all, and then the menu prints nothing rather than a zero.
        /// </summary>
        public int SelectionLines { get; set; }
        public int SelectionChars { get; set; }
        /// <summary>The read hit the probe's cap, so both counts are printed as "at least".</summary>
        public bool SelectionTruncated { get; set; }
        public IList<TextMenuRow> Conversions { get; set; } = new List<TextMenuRow>();
        public IList<TextMenuRow> Translations { get; set; } = new List<TextMenuRow>();
        public string CaseShortcut { get; set; } = "";
        public bool ShowLauncher { get; set; }
        public bool ShowHistory { get; set; }
        /// <summary>True while the quick notes are on - the "save the selection" item's own switch.</summary>
        public bool ShowQuickNotes { get; set; }
    }

    /// <summary>
    /// CyrFlip's own context menu over the selected text (spec §7): Cut/Copy/Paste first, then -
    /// when the selection turns out to be a link or a path - opening it, then what CyrFlip can do
    /// with the selection, then what it can start.
    ///
    /// It is an ordinary <see cref="ContextMenuStrip"/> on purpose. <c>ToolStripDropDown</c> already
    /// overrides <c>ShowWithoutActivation</c> to true, so the drop-down <b>never takes the focus</b> -
    /// the user's window stays active and, crucially, keeps its selection, which is the whole point.
    /// Sub-menus, icons, DPI scaling and RightToLeft for Arabic/Urdu come for free.
    ///
    /// Two rules govern the shape (spec §7.3): a module that is switched off contributes <b>no items
    /// at all</b>, and a command that needs a selection is <b>greyed, not hidden</b>, when there is
    /// none. <see cref="SelectionState.Unknown"/> counts as a selection - see <see cref="SelectionProbe"/>.
    ///
    /// Like <see cref="LauncherTrayMenu"/> this is a static seam so the rebuild can be unit-tested,
    /// and for the same hazard: <see cref="ToolStripItem.Dispose"/> removes the item from its owner's
    /// collection, so the old items are copied out before being disposed - disposing while
    /// enumerating the live collection throws "Collection was modified", and only on the *second*
    /// rebuild, since the first starts from an empty menu.
    /// </summary>
    internal static class TextContextMenu
    {
        public static void Rebuild(
            ContextMenuStrip menu,
            TextContextMenuState state,
            Func<string, string> translate,
            Action<ClipboardHandler.EditCommand> edit,
            Action<LaunchTarget> launch,
            Action<string> convert,
            Action caseFlip,
            Action<string> translateSelection,
            Action saveToQuickNotes,
            Action<ToolStripMenuItem>? fillLauncher,
            Action showHistory,
            Action showSettings)
        {
            Clear(menu.Items);

            // Unknown means "no source could tell" - and a greyed command next to a live selection is
            // the one failure the user sees, so it enables rather than disables.
            bool hasSelection = state.Selection != SelectionState.Absent;

            var groups = new List<List<ToolStripItem>>
            {
                EditGroup(state, translate, edit, hasSelection),
                OpenSelectionGroup(state, translate, launch),
                TextGroup(state, translate, convert, caseFlip, translateSelection, saveToQuickNotes, hasSelection),
                LaunchGroup(state, translate, fillLauncher, showHistory),
                new List<ToolStripItem>
                {
                    new ToolStripMenuItem(translate("Настройки"), null, (_, _) => showSettings()),
                },
                // Last, and deliberately so: the menu opens under the pointer, so whatever is at the
                // top is what the hand is already on. That place belongs to Copy, which is where
                // every other context menu in Windows puts it and where the muscle memory goes.
                // The report is read, not aimed at.
                StatsGroup(state, translate),
            };

            bool first = true;
            foreach (List<ToolStripItem> group in groups)
            {
                if (group.Count == 0) continue; // an empty group takes its separator with it
                if (!first) menu.Items.Add(new ToolStripSeparator());
                first = false;
                foreach (ToolStripItem item in group) menu.Items.Add(item);
            }
        }

        /// <summary>
        /// How much is selected, printed at the foot of the menu so the user knows roughly how big
        /// the clipboard is about to get. It is the one part of this menu that is <b>not a command</b>:
        /// <see cref="ToolStripLabel"/> (not a disabled menu item) because a label reports
        /// <c>CanSelect = false</c> - it never highlights on hover, never takes the keyboard, and
        /// cannot be clicked, while a disabled item still reads as "a command you may not use".
        ///
        /// Nothing is printed when the selection could not be read: a confident "0 characters" over a
        /// live selection is worse than saying nothing, and this is exactly the case where the
        /// commands above still work (see <see cref="SelectionProbe"/>'s fail-open rule).
        /// </summary>
        private static List<ToolStripItem> StatsGroup(TextContextMenuState state, Func<string, string> translate)
        {
            var items = new List<ToolStripItem>();
            if (state.SelectionChars <= 0) return items;

            items.Add(Caption(string.Format(translate("Выделено строк: {0}"),
                SelectionStats.Format(state.SelectionLines, state.SelectionTruncated))));
            items.Add(Caption(string.Format(translate("символов: {0}"),
                SelectionStats.Format(state.SelectionChars, state.SelectionTruncated))));
            return items;
        }

        private static ToolStripLabel Caption(string text) => new ToolStripLabel(text);

        private static List<ToolStripItem> EditGroup(
            TextContextMenuState state, Func<string, string> translate,
            Action<ClipboardHandler.EditCommand> edit, bool hasSelection)
            => new List<ToolStripItem>
            {
                Command(translate("Копировать"), "Ctrl+C", hasSelection,
                    () => edit(ClipboardHandler.EditCommand.Copy)),
                Command(translate("Вырезать"), "Ctrl+X", hasSelection,
                    () => edit(ClipboardHandler.EditCommand.Cut)),
                // Pasting does not care about the selection, only about the clipboard.
                Command(translate("Вставить"), "Ctrl+V", state.ClipboardHasText,
                    () => edit(ClipboardHandler.EditCommand.Paste)),
            };

        /// <summary>
        /// "Open what I selected" - the only item whose <b>presence</b> depends on the text rather
        /// than on a switch, because it is the only one that can name its target ("Launch
        /// «example.com»"). It sits in its own group under Copy/Cut/Paste so those three never move.
        /// </summary>
        private static List<ToolStripItem> OpenSelectionGroup(
            TextContextMenuState state, Func<string, string> translate, Action<LaunchTarget> launch)
        {
            var items = new List<ToolStripItem>();
            LaunchTarget? target = state.Launch;
            if (target == null) return items;

            items.Add(Command(string.Format(translate("Запустить «{0}»"), target.Display), "", true,
                () => launch(target)));
            return items;
        }

        private static List<ToolStripItem> TextGroup(
            TextContextMenuState state, Func<string, string> translate,
            Action<string> convert, Action caseFlip, Action<string> translateSelection,
            Action saveToQuickNotes, bool hasSelection)
        {
            var items = new List<ToolStripItem>();

            foreach (TextMenuRow row in state.Conversions)
            {
                string id = row.Id; // captured per item - a shared loop variable would bind them all to the last row
                items.Add(Command(row.Label, row.Shortcut, hasSelection, () => convert(id)));
            }

            items.Add(Command(translate("Исправить CapsLock"), state.CaseShortcut, hasSelection, caseFlip));

            foreach (TextMenuRow row in state.Translations)
            {
                string id = row.Id;
                items.Add(Command(
                    string.Format(translate("Перевести на {0}"), row.Label),
                    row.Shortcut, hasSelection, () => translateSelection(id)));
            }

            // The last thing you do *with* a selection: keep it. Like the rest of this group it is
            // greyed rather than hidden when there is nothing selected, and absent entirely while
            // the notes are switched off - a module that is off contributes no items at all.
            if (state.ShowQuickNotes)
                items.Add(Command(translate("Сохранить выделение в быстрые заметки"), "", hasSelection, saveToQuickNotes));

            return items;
        }

        private static List<ToolStripItem> LaunchGroup(
            TextContextMenuState state, Func<string, string> translate,
            Action<ToolStripMenuItem>? fillLauncher, Action showHistory)
        {
            var items = new List<ToolStripItem>();

            if (state.ShowLauncher && fillLauncher != null)
            {
                // The very same LauncherTrayMenu.Fill that builds the tray submenu and the taskbar
                // button's menu, so the three surfaces cannot drift apart.
                var launcher = new ToolStripMenuItem(translate("Быстрый запуск"));
                fillLauncher(launcher);
                items.Add(launcher);
            }

            if (state.ShowHistory)
                items.Add(new ToolStripMenuItem(translate("Менеджер буфера"), null, (_, _) => showHistory()));

            return items;
        }

        private static ToolStripMenuItem Command(string text, string shortcut, bool enabled, Action run)
        {
            var item = new ToolStripMenuItem(text, null, (_, _) => run()) { Enabled = enabled };
            if (!string.IsNullOrEmpty(shortcut))
            {
                item.ShortcutKeyDisplayString = shortcut;
                item.ShowShortcutKeys = true;
            }
            return item;
        }

        /// <summary>Drop every item (see the class remarks for why the copy comes first).</summary>
        private static void Clear(ToolStripItemCollection items)
        {
            var previous = new ToolStripItem[items.Count];
            items.CopyTo(previous, 0);
            items.Clear();
            foreach (ToolStripItem old in previous) old.Dispose();
        }
    }
}
