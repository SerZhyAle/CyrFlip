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
    /// The regrouping of the settings pages (ticket S0062): every setting is on the page of the feature it
    /// configures, exactly once, in the group the ticket names; the thirteen pages are in the pillar order;
    /// and the remembered navigation of an existing profile lands where the user left it.
    /// </summary>
    internal static class RegroupingHarness
    {
        public static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("EXCEPTION: " + failure);
        }

        /// <summary>The window in the source language, so a page title is the literal key the ticket quotes.</summary>
        public static AppConfig Config(string language = "Русский")
            => new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "", SettingsPageId = "", SettingsTab = 0 };

        public static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in Walk(child)) yield return deeper;
            }
        }

        public static TabPage PageOf(SettingsForm form, SettingsPageKey key)
            => form.Tabs!.TabPages.Cast<TabPage>().Single(p => Equals(p.Tag, key));

        /// <summary>The page a control sits on and the group (if any) that owns it, by walking up.</summary>
        public static (TabPage? Page, CollapsibleGroup? Group) Home(Control control)
        {
            CollapsibleGroup? group = null;
            for (Control? parent = control.Parent; parent != null; parent = parent.Parent)
            {
                if (group == null && parent is CollapsibleGroup g) group = g;
                if (parent is TabPage page) return (page, group);
            }
            return (null, group);
        }
    }

    /// <summary>S0062 section 5: the move ledger. Each row is looked up by its control's stable name.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsLedgerTests
    {
        // (ledger id, Control.Name, target page, target group id - null where the row is a direct child of the
        // page: "Индикаторы" has no groups, and a page left with one group has it flattened (graphics/capture)).
        private static readonly (string Id, string Name, SettingsPageKey Page, string? Group)[] Ledger =
        {
            ("L01", "setting.autostart", SettingsPageKey.General, "general/startup"),
            ("L02", "setting.shortcut-start-menu", SettingsPageKey.General, "general/startup"),
            ("L03", "setting.shortcut-desktop", SettingsPageKey.General, "general/startup"),
            ("L04", "setting.keep-system-awake", SettingsPageKey.General, "general/power"),
            ("L05", "setting.keep-screen-on", SettingsPageKey.General, "general/power"),
            ("L06", "setting.ui-language", SettingsPageKey.General, "general/language"),
            ("L07", "setting.theme", SettingsPageKey.General, "general/language"),
            ("L08", "group.transfer", SettingsPageKey.General, "general/transfer"),
            ("L09", "setting.cursor-marker", SettingsPageKey.Indicators, null),
            ("L10", "setting.caret-marker", SettingsPageKey.Indicators, null),
            ("L11", "setting.dot-mode", SettingsPageKey.Indicators, null),
            ("L12", "setting.marker-size", SettingsPageKey.Indicators, null),
            ("L13", "setting.layout-after-convert", SettingsPageKey.Conversions, "conversions/after"),
            ("L14", "setting.caps-sync", SettingsPageKey.Conversions, "conversions/case"),
            ("L15", "setting.hotkeys-master", SettingsPageKey.Hotkeys, "hotkeys/switches"),
            // The ledger calls it setting.rdp-defer; "deferRdp" is the AutomationId tools/uitest and the tests
            // have always found it by, so the name is kept.
            ("L16", "deferRdp", SettingsPageKey.Hotkeys, "hotkeys/switches"),
            ("L17", "setting.case-fix-chord", SettingsPageKey.Conversions, "conversions/case"),
            ("L18", "setting.history-chord", SettingsPageKey.Clipboard, "clipboard/window"),
            ("L19", "setting.context-menu", SettingsPageKey.SelectionMenu, "selection/mouse"),
            ("L20", "setting.mouse-chord", SettingsPageKey.SelectionMenu, "selection/mouse"),
            ("L21", "setting.keyboard-menu-chord", SettingsPageKey.SelectionMenu, "selection/keyboard"),
            ("L23", "setting.conversion-table", SettingsPageKey.Conversions, "conversions/table"),
            ("L24", "setting.convert-symbols", SettingsPageKey.Conversions, "conversions/symbols"),
            ("L25", "setting.history-on", SettingsPageKey.Clipboard, "clipboard/recording"),
            ("L26", "setting.history-pause", SettingsPageKey.Clipboard, "clipboard/recording"),
            ("L27", "setting.history-remember-window", SettingsPageKey.Clipboard, "clipboard/window"),
            ("L28", "setting.history-opacity", SettingsPageKey.Clipboard, "clipboard/window"),
            ("L29", "setting.history-search", SettingsPageKey.Clipboard, "clipboard/window"),
            ("L30", "setting.history-clear", SettingsPageKey.Clipboard, "clipboard/clear"),
            ("L31", "group.clipboard-transfer", SettingsPageKey.Clipboard, "clipboard/transfer"),
            ("L32", "group.graphics-capture", SettingsPageKey.Graphics, "graphics/capture"),
            // S0060 M4: "Reset all" is a command, so it stands in the page's first group like the switches it resets.
            ("L33", "group.overview", SettingsPageKey.Hotkeys, "hotkeys/switches"),
        };

        [Fact]
        public void EveryLedgerRowExistsExactlyOnceOnItsPageAndInItsGroup()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                var problems = new List<string>();
                foreach (var (id, name, page, group) in Ledger)
                {
                    Control[] found = form.Controls.Find(name, true);
                    if (found.Length != 1) { problems.Add($"{id} '{name}': found {found.Length} times"); continue; }
                    (TabPage? onPage, CollapsibleGroup? inGroup) = RegroupingHarness.Home(found[0]);
                    if (onPage == null || !Equals(onPage.Tag, page)) problems.Add($"{id} '{name}': on {onPage?.Tag}, wanted {page}");
                    if (group == null && inGroup != null) problems.Add($"{id} '{name}': in group {inGroup.Id}, wanted a direct child of the page");
                    if (group != null && inGroup?.Id != group) problems.Add($"{id} '{name}': in group {inGroup?.Id ?? "(none)"}, wanted {group}");
                }
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        [Fact]
        public void TheSixOverviewGroupsAreOnTheMergedPage()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                TabPage hotkeys = RegroupingHarness.PageOf(form, SettingsPageKey.Hotkeys);
                string[] ids = RegroupingHarness.Walk(hotkeys).OfType<CollapsibleGroup>().Select(g => g.Id).ToArray();
                foreach (string id in new[] { "keys/fixed", "keys/mouse", "keys/conversions", "keys/translations", "keys/launcher", "keys/windows" })
                    Assert.Contains(id, ids);
            });
        }

        [Fact]
        public void TheRetiredNoteAndTheDuplicateMasterAreGone()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                // L22 (D13): "Здесь только эти два хоткея..." stopped being true and is deleted.
                Assert.DoesNotContain(RegroupingHarness.Walk(form).OfType<Label>(), l => l.Text.StartsWith("Здесь только эти два хоткея", StringComparison.Ordinal));
                // D9: one master switch, one control.
                Assert.Single(RegroupingHarness.Walk(form).OfType<CheckBox>(), c => c.Name == "setting.hotkeys-master");
                Assert.Equal(1, RegroupingHarness.Walk(form).OfType<CheckBox>().Count(c => c.Text == "Слушать глобальные горячие клавиши"));
            });
        }
    }

    /// <summary>S0062 section 6: the behaviour that must not change - the dependency greying and the search.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsRegroupingBehaviourTests
    {
        private static object Field(object owner, string name)
            => typeof(SettingsForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;

        [Fact]
        public void TheMasterSwitchGreysTheCaseFixAndTheHistoryRowsOnTheirNewPages()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config();
                config.EnableHotkeys = false;
                using SettingsForm form = TestForms.NewSettings(config);
                var caseFix = (CheckBox)Field(form, "_caseEnabled");
                var history = (CheckBox)Field(form, "_historyEnabled");
                Assert.False(caseFix.Enabled);
                Assert.False(history.Enabled);
                Assert.Equal(SettingsPageKey.Conversions, RegroupingHarness.Home(caseFix).Page!.Tag);
                Assert.Equal(SettingsPageKey.Clipboard, RegroupingHarness.Home(history).Page!.Tag);

                config.EnableHotkeys = true;
                form.Reload();
                Assert.True(caseFix.Enabled);
                Assert.True(history.Enabled);
            });
        }

        [Fact]
        public void TheContextMenuSwitchGreysItsKeyboardChordAndTheMasterSwitchDoesNot()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config();
                config.EnableContextMenu = false;
                config.EnableHotkeys = false;     // the menu is a mouse chord: the keyboard master does not gate it
                using SettingsForm form = TestForms.NewSettings(config);
                var keyboardChord = (CheckBox)Field(form, "_textMenuHotkeyEnabled");
                Assert.False(keyboardChord.Enabled);
                Assert.Equal(SettingsPageKey.SelectionMenu, RegroupingHarness.Home(keyboardChord).Page!.Tag);

                config.EnableContextMenu = true;
                form.Reload();
                Assert.True(keyboardChord.Enabled);   // still on with the master switch off
            });
        }

        [Fact]
        public void ASearchForCapsLockAndForTheMenuChordLandOnTheirNewHomes()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                var hits = form.BuildSearchIndex();
                Assert.Contains(hits, h => Equals(h.Page.Tag, SettingsPageKey.Conversions) && h.GroupCaption == "Регистр"
                    && SettingsForm.SearchMatchesForTest(h.Texts, "CapsLock"));
                Assert.Contains(hits, h => Equals(h.Page.Tag, SettingsPageKey.SelectionMenu) && h.GroupCaption == "С клавиатуры"
                    && SettingsForm.SearchMatchesForTest(h.Texts, "меню с клавиатуры"));
                // And nothing about the case fix is left on the Indicators page.
                Assert.DoesNotContain(hits, h => Equals(h.Page.Tag, SettingsPageKey.Indicators)
                    && SettingsForm.SearchMatchesForTest(h.Texts, "CapsLock"));
            });
        }
    }

    /// <summary>S0062 section 4 and S0069: fourteen pages, in the pillar order, the About page last.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsPageOrderTests
    {
        private static readonly (SettingsPageKey Key, string Title)[] Expected =
        {
            (SettingsPageKey.General, "Общие"),
            (SettingsPageKey.Indicators, "Индикаторы"),
            (SettingsPageKey.WindowsLanguages, "Языки Windows"),
            (SettingsPageKey.Conversions, "Исправление текста"),
            (SettingsPageKey.SelectionMenu, "Меню для выделенного"),
            (SettingsPageKey.Symbols, "Символы"),
            (SettingsPageKey.Translate, "Перевод"),
            (SettingsPageKey.Clipboard, "Буфер обмена"),
            (SettingsPageKey.QuickNotes, "Быстрые заметки"),
            (SettingsPageKey.Graphics, "Снимки экрана"),
            (SettingsPageKey.Launcher, "Быстрый запуск"),
            (SettingsPageKey.TrafficLight, "Светофор доступности"),
            (SettingsPageKey.ExchangeServer, "Сервер обмена"),
            (SettingsPageKey.Hotkeys, "Горячие клавиши"),
            (SettingsPageKey.About, "О программе"),
        };

        [Fact]
        public void ThePagesAreInThePillarOrderWithTheirTitles()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                var pages = form.Tabs!.TabPages.Cast<TabPage>().ToList();
                Assert.Equal(15, pages.Count);
                Assert.Equal(Expected.Select(e => (object)e.Key), pages.Select(p => p.Tag!));
                Assert.Equal(Expected.Select(e => e.Title), pages.Select(p => p.Text));
                Assert.Equal(SettingsPageKey.About, pages[pages.Count - 1].Tag);
                // The order is one table, and the table is what the window was built from.
                Assert.Equal(SettingsPageKeys.DisplayOrder, pages.Select(p => (SettingsPageKey)p.Tag!).ToArray());
            });
        }

        [Fact]
        public void TheRetiredOverviewPageHasNoTab()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                Assert.DoesNotContain(form.Tabs!.TabPages.Cast<TabPage>(), p => Equals(p.Tag, SettingsPageKey.HotkeysOverview));
            });
        }
    }

    /// <summary>S0062 D1: a chord lives on the page of the feature it starts. The exception list is empty.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class ChordHomeTests
    {
        // The control that holds each fixed chord's switch, by its stable name.
        private static readonly Dictionary<ChordKind, string> Controls = new Dictionary<ChordKind, string>
        {
            [ChordKind.Case] = "setting.case-fix-chord",
            [ChordKind.History] = "setting.history-chord",
            [ChordKind.QuickNotes] = "setting.quick-notes-chord",
            [ChordKind.Symbols] = "setting.symbols-chord",
            [ChordKind.TrafficLight] = "setting.traffic-light-chord",
            [ChordKind.Screenshot] = "group.graphics-capture",
            [ChordKind.ScreenshotFull] = "group.graphics-full",
            [ChordKind.ScreenshotAnnotateArea] = "group.graphics-annotate",
            [ChordKind.ScreenshotFullAnnotate] = "group.graphics-annotate",
            [ChordKind.ScreenshotFolder] = "setting.screenshot-folder-chord",
            [ChordKind.TextMenu] = "setting.keyboard-menu-chord",
            [ChordKind.ExchangeSend] = "setting.exchange-clipboard-send-chord",
            [ChordKind.ExchangeGet] = "setting.exchange-clipboard-get-chord",
        };

        [Fact]
        public void EveryFixedChordIsHeldByAControlOnTheOwnersPage()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config();
                using SettingsForm form = TestForms.NewSettings(config);
                var problems = new List<string>();
                foreach (ChordOverviewRow row in ChordOverviewModel.Build(config, null, null, null, config.UiLanguage, k => k))
                {
                    if (row.ModulePage == null || row.ReadOnly) continue;
                    string? name = row.IsMouse ? "setting.mouse-chord" : row.Kind != null && Controls.TryGetValue(row.Kind.Value, out string? n) ? n : null;
                    if (name == null) continue;   // the table rows (conversion, translation, scenario) live in their own tables
                    Control[] found = form.Controls.Find(name, true);
                    if (found.Length != 1) { problems.Add($"{row.Caption}: control '{name}' found {found.Length} times"); continue; }
                    TabPage? page = RegroupingHarness.Home(found[0]).Page;
                    if (page == null || !Equals(page.Tag, row.ModulePage)) problems.Add($"{row.Caption}: on {page?.Tag}, the overview says {row.ModulePage}");
                }
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        [Fact]
        public void EveryFixedKindHasAControlSoTheGateCannotPassByOmission()
        {
            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)))
            {
                if (kind == ChordKind.Conversion || kind == ChordKind.Translation || kind == ChordKind.Launcher || kind == ChordKind.WindowsLanguage) continue;
                Assert.True(Controls.ContainsKey(kind), kind + " has no control mapped in ChordHomeTests");
            }
        }
    }

    public class SettingsPageKeyParseTests
    {
        [Fact]
        public void TheRetiredOverviewKeyReadsAsTheMergedPage()
            => Assert.Equal(SettingsPageKey.Hotkeys, SettingsPageKeys.Parse("HotkeysOverview"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("NoSuchPage")]
        [InlineData("5")]
        [InlineData("general")]
        public void AnUnknownNameReadsAsGeneral(string? name)
        {
            Assert.Equal(SettingsPageKey.General, SettingsPageKeys.Parse(name));
            Assert.False(SettingsPageKeys.TryParse(name, out _));
        }

        [Fact]
        public void EveryOtherKeyReadsAsItself()
        {
            foreach (SettingsPageKey key in Enum.GetValues(typeof(SettingsPageKey)))
            {
                if (key == SettingsPageKey.HotkeysOverview) continue;
                Assert.Equal(key, SettingsPageKeys.Parse(key.ToString()));
            }
        }
    }

    /// <summary>S0062 section 8 and D11: navigation is remembered by page, and the old numbers still mean what they meant.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsMigrationTests
    {
        [Theory]
        [InlineData(0, "General")]
        [InlineData(1, "Indicators")]
        [InlineData(2, "Hotkeys")]
        [InlineData(3, "Hotkeys")]
        [InlineData(4, "Conversions")]
        [InlineData(5, "WindowsLanguages")]
        [InlineData(6, "Clipboard")]
        [InlineData(7, "QuickNotes")]
        [InlineData(8, "Graphics")]
        [InlineData(9, "Translate")]
        [InlineData(10, "Launcher")]
        [InlineData(11, "Symbols")]
        [InlineData(12, "About")]
        public void ALegacyIndexIsTurnedIntoThePageItNamedBeforeTheReorder(int legacyIndex, string expectedName)
        {
            var expected = (SettingsPageKey)Enum.Parse(typeof(SettingsPageKey), expectedName);
            // Index 3 was the overview page: it merged into Hotkeys, so both land there.
            Assert.Equal(expected, SettingsPageKeys.FromLegacyIndex(legacyIndex));
            Assert.True(SettingsPageKeys.TryParseViewportPage(legacyIndex.ToString(), out SettingsPageKey viaViewport));
            Assert.Equal(expected, viaViewport);
        }

        [Fact]
        public void AnIndexOutsideTheOldListIsNotAPage()
        {
            Assert.Null(SettingsPageKeys.FromLegacyIndex(13));
            Assert.Null(SettingsPageKeys.FromLegacyIndex(-1));
            Assert.False(SettingsPageKeys.TryParseViewportPage("99", out _));
        }

        [Fact]
        public void ALegacyNumericViewportRowIsReadByThePageItNamed()
        {
            // "8|g:graphics/capture|40" was written when index 8 was Graphics; it stays Graphics's row.
            string raw = "8|g:graphics/capture|40;4|r:3|10";
            Assert.Equal(("g:graphics/capture", 40), SettingsForm.ReadViewportRow(raw, SettingsPageKey.Graphics));
            Assert.Equal(("r:3", 10), SettingsForm.ReadViewportRow(raw, SettingsPageKey.Conversions));
            Assert.Equal((null, 0), SettingsForm.ReadViewportRow(raw, SettingsPageKey.Clipboard));
        }

        [Fact]
        public void AWrittenViewportRowCarriesTheKeyAndReplacesTheLegacyRowOfThatPage()
        {
            string raw = SettingsForm.UpsertViewportRow("8|g:graphics/capture|40;4|r:3|10", SettingsPageKey.Graphics, "g:graphics/capture", 70);
            Assert.Contains("Graphics|g:graphics/capture|70", raw);
            Assert.DoesNotContain("8|g:graphics/capture", raw);
            Assert.Contains("4|r:3|10", raw);   // another page's legacy row is left alone
            Assert.Equal(raw, AppConfig.SanitizeViewport(raw));   // and the sanitizer keeps both shapes
        }

        [Fact]
        public void ARememberedOverviewPageOpensTheMergedPage()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config();
                config.SettingsPageId = "HotkeysOverview";
                using SettingsForm form = TestForms.NewSettings(config);
                Assert.Equal(SettingsPageKey.Hotkeys, form.Tabs!.SelectedTab!.Tag);
            });
        }

        [Fact]
        public void ALegacySettingsTabIndexLandsOnTheSamePageAsBefore()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                // Index 8 was "Графика": after the reorder position 8 is "Быстрые заметки", and must not be where it lands.
                AppConfig config = RegroupingHarness.Config();
                config.SettingsTab = 8;
                using SettingsForm form = TestForms.NewSettings(config);
                Assert.Equal(SettingsPageKey.Graphics, form.Tabs!.SelectedTab!.Tag);

                // Index 4 was the conversion table.
                config = RegroupingHarness.Config();
                config.SettingsTab = 4;
                using SettingsForm second = TestForms.NewSettings(config);
                Assert.Equal(SettingsPageKey.Conversions, second.Tabs!.SelectedTab!.Tag);
            });
        }

        [Fact]
        public void AStoredKeyWinsOverALegacyIndex()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config();
                config.SettingsPageId = "Launcher";
                config.SettingsTab = 4;
                using SettingsForm form = TestForms.NewSettings(config);
                Assert.Equal(SettingsPageKey.Launcher, form.Tabs!.SelectedTab!.Tag);
            });
        }
    }

    /// <summary>S0062 D12: the automatic "Параметры" group is keyed by the page, not by its (translated, renamed) title.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class AutomaticGroupTests
    {
        [Fact]
        public void OnlyTheIndicatorsPageKeepsLooseRowsAndNoPageIsFoundByItsTitle()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                // The automatic group of the one page with loose rows is flattened again (a single group is no
                // grouping), so what is left is the rows themselves and no "indicators/preferences" group.
                Assert.DoesNotContain(form.Groups, g => g.Id.EndsWith("/preferences", StringComparison.Ordinal));
                TabPage indicators = RegroupingHarness.PageOf(form, SettingsPageKey.Indicators);
                var panel = (FlowLayoutPanel)indicators.Controls[0];
                Assert.DoesNotContain(panel.Controls.Cast<Control>(), c => c is CollapsibleGroup);

                // Every other page is made of groups it built itself; none of them is an automatic one.
                foreach (TabPage page in form.Tabs!.TabPages)
                    foreach (CollapsibleGroup group in ((FlowLayoutPanel)page.Controls[0]).Controls.OfType<CollapsibleGroup>())
                        Assert.False(group.Id.EndsWith("/preferences", StringComparison.Ordinal), page.Text + ": " + group.Id);
            });
        }

        [Fact]
        public void TheRenamedPagesAreStillBuiltWhateverLanguageNamesThem()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                // A lookup by title would find nothing in a language whose title differs; the groups must not depend on it.
                foreach (string language in new[] { "English", "Русский", "Deutsch" })
                {
                    using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config(language));
                    Assert.Contains(form.Groups, g => g.Id == "conversions/case");
                    Assert.Contains(form.Groups, g => g.Id == "selection/keyboard");
                    Assert.Contains(form.Groups, g => g.Id == "general/startup");
                }
            });
        }
    }
}
