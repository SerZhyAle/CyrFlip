using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The text context menu as a pure function of its state: what appears, what is greyed, and what
    /// takes its separator with it when the module behind it is off. As with the tray submenu, the
    /// load-bearing rebuild is the <b>second</b> one - the first starts from an empty collection and
    /// can never fail.
    /// </summary>
    public class TextContextMenuTests
    {
        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private static TextContextMenuState FullState(SelectionState selection) => new TextContextMenuState
        {
            Selection = selection,
            ClipboardHasText = true,
            Conversions = new List<TextMenuRow> { new TextMenuRow("EN ⇄ RU", "Ctrl+Shift+F12", "conv-1") },
            Translations = new List<TextMenuRow> { new TextMenuRow("English", "Ctrl+Shift+F9", "tr-1") },
            CaseShortcut = "Ctrl+Shift+F11",
            ShowLauncher = true,
            ShowHistory = true,
        };

        private static void Build(ContextMenuStrip menu, TextContextMenuState state,
            Action<ClipboardHandler.EditCommand>? edit = null,
            Action<string>? convert = null, Action? caseFlip = null, Action<string>? translateRow = null,
            Action? history = null, Action? settings = null, bool launcherItems = true)
            => TextContextMenu.Rebuild(menu, state, ru => ru,
                edit ?? (_ => { }), convert ?? (_ => { }), caseFlip ?? (() => { }),
                translateRow ?? (_ => { }),
                launcherItems
                    ? parent => LauncherTrayMenu.Rebuild(parent,
                        new List<LauncherScenario> { new LauncherScenario { Name = "Calc", Path = "calc.exe" } },
                        _ => { }, () => { }, null, ru => ru)
                    : (Action<ToolStripMenuItem>?)null,
                history ?? (() => { }), settings ?? (() => { }));

        private static string[] Captions(ContextMenuStrip menu)
            => menu.Items.Cast<ToolStripItem>()
                .Select(i => i is ToolStripSeparator ? "---" : i.Text).ToArray();

        private static ToolStripItem Item(ContextMenuStrip menu, string caption)
            => menu.Items.Cast<ToolStripItem>().First(i => i.Text == caption);

        [Fact]
        public void EverythingOnGivesEditCommandsFirstAndSettingsLast()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present));

                Assert.Equal(new[]
                {
                    "Копировать", "Вырезать", "Вставить",
                    "---",
                    "EN ⇄ RU", "Исправить CapsLock", "Перевести на English",
                    "---",
                    "Быстрый запуск", "Менеджер буфера",
                    "---",
                    "Настройки",
                }, Captions(menu));
            });
        }

        [Fact]
        public void RepeatedRebuildsDoNotThrowAndLeaveNoStaleItems()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                for (int pass = 0; pass < 3; pass++)
                    Build(menu, FullState(SelectionState.Present));

                Assert.Equal(12, menu.Items.Count); // one menu's worth after three passes
            });
        }

        [Fact]
        public void WithoutASelectionTheTextCommandsAreGreyedNotHidden()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Absent));

                Assert.False(Item(menu, "Копировать").Enabled);
                Assert.False(Item(menu, "Вырезать").Enabled);
                Assert.False(Item(menu, "EN ⇄ RU").Enabled);
                Assert.False(Item(menu, "Исправить CapsLock").Enabled);
                Assert.False(Item(menu, "Перевести на English").Enabled);

                // Present, just unavailable - and the rest of the menu is unaffected.
                Assert.Equal(12, menu.Items.Count);
                Assert.True(Item(menu, "Вставить").Enabled);
                Assert.True(Item(menu, "Менеджер буфера").Enabled);
                Assert.True(Item(menu, "Настройки").Enabled);
            });
        }

        /// <summary>
        /// The deliberate fail-open rule: no source could tell whether anything is selected, so the
        /// commands stay live. A greyed command next to a live selection is the failure the user sees;
        /// a command that runs and quietly does nothing is what every CyrFlip operation already does.
        /// </summary>
        [Fact]
        public void UnknownSelectionEnablesEverything()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Unknown));

                Assert.True(Item(menu, "Копировать").Enabled);
                Assert.True(Item(menu, "Вырезать").Enabled);
                Assert.True(Item(menu, "EN ⇄ RU").Enabled);
                Assert.True(Item(menu, "Исправить CapsLock").Enabled);
                Assert.True(Item(menu, "Перевести на English").Enabled);
            });
        }

        [Fact]
        public void PasteFollowsTheClipboardNotTheSelection()
        {
            OnUiThread(() =>
            {
                using var withText = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Absent);
                Build(withText, state);
                Assert.True(Item(withText, "Вставить").Enabled);

                using var empty = new ContextMenuStrip();
                state.ClipboardHasText = false;
                state.Selection = SelectionState.Present;
                Build(empty, state);
                Assert.False(Item(empty, "Вставить").Enabled);
            });
        }

        [Fact]
        public void DisabledModulesLeaveNoItemsAndNoDanglingSeparator()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, new TextContextMenuState
                {
                    Selection = SelectionState.Present,
                    ClipboardHasText = true,
                    CaseShortcut = "Ctrl+Shift+F11",
                    // no conversion rows, no translation rows, launcher and history both off
                }, launcherItems: false);

                Assert.Equal(new[]
                {
                    "Копировать", "Вырезать", "Вставить",
                    "---",
                    "Исправить CapsLock",
                    "---",
                    "Настройки",
                }, Captions(menu));
            });
        }

        [Fact]
        public void TranslationRowsAppearOnlyWhenTheTableHasThem()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.Translations = new List<TextMenuRow>();
                Build(menu, state);

                Assert.DoesNotContain(Captions(menu), c => c.StartsWith("Перевести"));
                Assert.Contains("EN ⇄ RU", Captions(menu));
            });
        }

        [Fact]
        public void EachRowRunsItsOwnProfileNotTheLastOne()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                var converted = new List<string>();
                var translated = new List<string>();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.Conversions = new List<TextMenuRow>
                {
                    new TextMenuRow("EN ⇄ RU", "Ctrl+Shift+F12", "conv-1"),
                    new TextMenuRow("EN ⇄ UK", "Alt+Shift+F12", "conv-2"),
                };
                state.Translations = new List<TextMenuRow>
                {
                    new TextMenuRow("English", "", "tr-1"),
                    new TextMenuRow("Deutsch", "", "tr-2"),
                };
                Build(menu, state, convert: converted.Add, translateRow: translated.Add);

                Item(menu, "EN ⇄ RU").PerformClick();
                Item(menu, "EN ⇄ UK").PerformClick();
                Item(menu, "Перевести на English").PerformClick();
                Item(menu, "Перевести на Deutsch").PerformClick();

                Assert.Equal(new[] { "conv-1", "conv-2" }, converted);
                Assert.Equal(new[] { "tr-1", "tr-2" }, translated);
            });
        }

        [Fact]
        public void EditCommandsAreWiredToTheirOwnCommand()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                var sent = new List<ClipboardHandler.EditCommand>();
                Build(menu, FullState(SelectionState.Present), edit: sent.Add);

                Item(menu, "Копировать").PerformClick();
                Item(menu, "Вырезать").PerformClick();
                Item(menu, "Вставить").PerformClick();

                Assert.Equal(new[]
                {
                    ClipboardHandler.EditCommand.Copy,
                    ClipboardHandler.EditCommand.Cut,
                    ClipboardHandler.EditCommand.Paste,
                }, sent);
            });
        }

        [Fact]
        public void ChordsAreShownBesideTheCommandsTheyTrigger()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present));

                Assert.Equal("Ctrl+C", ((ToolStripMenuItem)Item(menu, "Копировать")).ShortcutKeyDisplayString);
                Assert.Equal("Ctrl+Shift+F12", ((ToolStripMenuItem)Item(menu, "EN ⇄ RU")).ShortcutKeyDisplayString);
                Assert.Equal("Ctrl+Shift+F11", ((ToolStripMenuItem)Item(menu, "Исправить CapsLock")).ShortcutKeyDisplayString);
            });
        }

        /// <summary>A row whose chord is empty (or switched off) must not print an empty gap.</summary>
        [Fact]
        public void ARowWithoutAChordShowsNoShortcutColumn()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.CaseShortcut = "";
                Build(menu, state);

                var item = (ToolStripMenuItem)Item(menu, "Исправить CapsLock");
                Assert.True(string.IsNullOrEmpty(item.ShortcutKeyDisplayString));
            });
        }

        [Fact]
        public void LauncherSubmenuCarriesTheSameListAsTheTray()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present));

                var launcher = (ToolStripMenuItem)Item(menu, "Быстрый запуск");
                Assert.Equal(new[] { "Calc", "---", "Управление сценариями..." },
                    launcher.DropDownItems.Cast<ToolStripItem>()
                        .Select(i => i is ToolStripSeparator ? "---" : i.Text).ToArray());
            });
        }

        [Fact]
        public void HistoryAndSettingsRunTheirActions()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                int historyShown = 0, settingsShown = 0;
                Build(menu, FullState(SelectionState.Present),
                    history: () => historyShown++, settings: () => settingsShown++);

                Item(menu, "Менеджер буфера").PerformClick();
                Item(menu, "Настройки").PerformClick();

                Assert.Equal(1, historyShown);
                Assert.Equal(1, settingsShown);
            });
        }
    }
}
