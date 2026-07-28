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
        public IList<TextMenuRow> Conversions { get; set; } = new List<TextMenuRow>();
        public IList<TextMenuRow> Translations { get; set; } = new List<TextMenuRow>();
        public string CaseShortcut { get; set; } = "";
        public bool ShowLauncher { get; set; }
        public bool ShowHistory { get; set; }
    }

    /// <summary>
    /// CyrFlip's own context menu over the selected text (spec §7): Cut/Copy/Paste first, then what
    /// CyrFlip can do with the selection, then what it can start.
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
            Action<string> convert,
            Action caseFlip,
            Action<string> translateSelection,
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
                TextGroup(state, translate, convert, caseFlip, translateSelection, hasSelection),
                LaunchGroup(state, translate, fillLauncher, showHistory),
                new List<ToolStripItem>
                {
                    new ToolStripMenuItem(translate("Настройки"), null, (_, _) => showSettings()),
                },
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

        private static List<ToolStripItem> TextGroup(
            TextContextMenuState state, Func<string, string> translate,
            Action<string> convert, Action caseFlip, Action<string> translateSelection, bool hasSelection)
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
