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
            Action<ClipboardHandler.EditCommand>? edit = null, Action<LaunchTarget>? launch = null,
            Action<string>? convert = null, Action? caseFlip = null, Action<string>? translateRow = null,
            Action? saveToNotes = null,
            Action? history = null, Action? settings = null, bool launcherItems = true)
            => TextContextMenu.Rebuild(menu, state, ru => ru,
                edit ?? (_ => { }), launch ?? (_ => { }), convert ?? (_ => { }), caseFlip ?? (() => { }),
                translateRow ?? (_ => { }), saveToNotes ?? (() => { }),
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

        /// <summary>
        /// The quick-notes command follows both rules of this menu at once: absent while the module
        /// is off, and - once on - greyed rather than hidden when there is nothing selected. It is
        /// the last thing in the group that acts on the selection, so it must not displace the case
        /// flip or the translation rows above it.
        /// </summary>
        [Fact]
        public void TheQuickNotesCommandIsAbsentWhileTheModuleIsOff()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present));   // ShowQuickNotes defaults to false

                Assert.DoesNotContain("Сохранить выделение в быстрые заметки", Captions(menu));
            });
        }

        [Fact]
        public void TheQuickNotesCommandClosesTheSelectionGroupAndIsGreyedWithoutOne()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Absent);
                state.ShowQuickNotes = true;
                bool saved = false;
                Build(menu, state, saveToNotes: () => saved = true);

                string[] captions = Captions(menu);
                int notes = Array.IndexOf(captions, "Сохранить выделение в быстрые заметки");
                Assert.True(notes >= 0, "the command is there while the module is on");
                Assert.Equal(notes - 1, Array.IndexOf(captions, "Перевести на English"));

                ToolStripItem item = Item(menu, "Сохранить выделение в быстрые заметки");
                Assert.False(item.Enabled);   // greyed, not hidden - nothing is selected

                // Enabled with a selection, and it runs the action it was handed.
                using var live = new ContextMenuStrip();
                TextContextMenuState present = FullState(SelectionState.Present);
                present.ShowQuickNotes = true;
                Build(live, present, saveToNotes: () => saved = true);
                ToolStripItem liveItem = Item(live, "Сохранить выделение в быстрые заметки");
                Assert.True(liveItem.Enabled);
                liveItem.PerformClick();
                Assert.True(saved);
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

        // ---- "Open what I selected" -------------------------------------------------------

        /// <summary>
        /// The launch item is the one entry that is <b>absent</b> rather than greyed when it does not
        /// apply: it can only exist once the selection has been read and parsed, and over ordinary
        /// prose - the common case - there is nothing to name.
        /// </summary>
        // ---- "How much is selected" -------------------------------------------------------

        /// <summary>
        /// The two report lines are <b>not commands</b>: a label reports CanSelect = false, so it
        /// never highlights, never takes the keyboard and cannot be clicked.
        ///
        /// They go <b>last</b>, and the first item stays Copy. The menu opens under the pointer, so
        /// whatever is at the top is what the hand is already on - that place belongs to the command
        /// everyone reaches for, not to a caption nobody aims at.
        /// </summary>
        [Fact]
        public void TheSelectionSizeIsPrintedLastAndCannotBeClicked()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.SelectionLines = 3;
                state.SelectionChars = 128;
                Build(menu, state);

                Assert.Equal("Копировать", Captions(menu)[0]);
                Assert.Equal(new[] { "Настройки", "---", "Выделено строк: 3", "символов: 128" },
                    Captions(menu).Skip(Captions(menu).Length - 4).ToArray());
                Assert.False(Item(menu, "Выделено строк: 3").CanSelect);
                Assert.False(Item(menu, "символов: 128").CanSelect);
            });
        }

        /// <summary>
        /// The selection could not be read - which is routine, and exactly the case where the
        /// commands below still work. A confident "0 characters" over a live selection would be
        /// worse than saying nothing.
        /// </summary>
        [Fact]
        public void NothingIsPrintedWhenTheSelectionCouldNotBeRead()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present)); // no counts set

                Assert.DoesNotContain(Captions(menu), c => c.StartsWith("Выделено"));
                Assert.Equal("Настройки", Captions(menu)[Captions(menu).Length - 1]); // no dangling separator
            });
        }

        [Fact]
        public void ACappedReadIsReportedAsAtLeast()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.SelectionLines = 900;
                state.SelectionChars = 65536;
                state.SelectionTruncated = true;
                Build(menu, state);

                Assert.Contains("Выделено строк: 900+", Captions(menu));
                Assert.Contains("символов: 65536+", Captions(menu));
            });
        }

        [Fact]
        public void NoLaunchTargetMeansNoLaunchItemAndNoExtraSeparator()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                Build(menu, FullState(SelectionState.Present));

                Assert.DoesNotContain(Captions(menu), c => c.StartsWith("Запустить «"));
                Assert.Equal(12, menu.Items.Count);
            });
        }

        [Fact]
        public void ALaunchTargetSitsRightUnderCopyCutPaste()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                TextContextMenuState state = FullState(SelectionState.Present);
                state.Launch = new LaunchTarget("https://example.com/", "example.com", LaunchKind.Url);
                Build(menu, state);

                Assert.Equal(new[]
                {
                    "Копировать", "Вырезать", "Вставить",
                    "---",
                    "Запустить «example.com»",
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
        public void TheLaunchItemHandsBackTheTargetItNamed()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                var launched = new List<string>();
                TextContextMenuState state = FullState(SelectionState.Present);
                var target = new LaunchTarget(@"C:\tools\setup.exe", "setup.exe", LaunchKind.Program);
                state.Launch = target;
                Build(menu, state, launch: t => launched.Add(t.Target + "|" + t.Kind));

                Item(menu, "Запустить «setup.exe»").PerformClick();

                Assert.Equal(new[] { @"C:\tools\setup.exe|Program" }, launched);
            });
        }

        /// <summary>
        /// A selection CyrFlip could read is a selection that exists, so the item is live even where
        /// the probe's verdict was the fail-open Unknown - and it never greys with the others.
        /// </summary>
        [Fact]
        public void TheLaunchItemIsEnabledWhateverTheProbeConcluded()
        {
            OnUiThread(() =>
            {
                foreach (SelectionState state in new[]
                    { SelectionState.Present, SelectionState.Unknown, SelectionState.Absent })
                {
                    using var menu = new ContextMenuStrip();
                    TextContextMenuState menuState = FullState(state);
                    menuState.Launch = new LaunchTarget("https://example.com/", "example.com", LaunchKind.Url);
                    Build(menu, menuState);

                    Assert.True(Item(menu, "Запустить «example.com»").Enabled);
                }
            });
        }
    }
}
