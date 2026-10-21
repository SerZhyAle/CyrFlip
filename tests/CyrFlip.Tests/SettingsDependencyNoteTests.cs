using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Whether a control itself asks to be shown. <see cref="Control.Visible"/> answers for the whole parent
    /// chain, so every control of a window that is not on screen - or of a page that is not the selected one -
    /// reads false whatever the code set; the control's own flag is what the code decided.
    /// </summary>
    internal static class OwnVisibility
    {
        private static readonly System.Reflection.MethodInfo? GetState = typeof(Control).GetMethod("GetState",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

        private const int StateVisible = 0x00000002;   // Control.STATE_VISIBLE, frozen with net48

        public static bool Of(Control control)
            => GetState != null ? (bool)GetState.Invoke(control, new object[] { StateVisible })! : control.Visible;
    }

    /// <summary>
    /// S0060 M5 (WINDOWS-UI section 3 rule 6): a control that is unavailable says why and how to make it
    /// available. The window used to grey rows without a word; now a note stands next to each such row,
    /// follows the language, goes away with the cause, and reaches a screen reader as the control's
    /// accessible description.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsDependencyNoteTests
    {
        private const string Template = "Unavailable while «{0}» is off - turn that switch on first.";
        private const string NothingToRestore = "Nothing to restore yet: CyrFlip saves the original state just before the first change.";

        private static Control Field(object form, string name)
        {
            FieldInfo? field = typeof(SettingsForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(field != null, "no field " + name + " - the dependency table of the test is stale");
            return (Control)field!.GetValue(form)!;
        }

        /// <summary>The note that explains a control: the table entry of the control itself or of the block holding it.</summary>
        private static Label? NoteOf(SettingsForm form, Control control)
        {
            foreach (KeyValuePair<Control, Label> entry in form.DependencyTable)
            {
                if (ReferenceEquals(entry.Key, control)) return entry.Value;
                for (Control? up = control.Parent; up != null; up = up.Parent)
                    if (ReferenceEquals(up, entry.Key)) return entry.Value;
            }
            return null;
        }

        private static bool IsEditor(Control control)
            => control is ButtonBase || control is ComboBox || control is NumericUpDown || control is TrackBar
               || control is TextBoxBase || control is ListView;

        /// <summary>The editors and commands under a control. An editor is a leaf: the inner edit box of a
        /// numeric editor is the framework's, not a control a person lands on.</summary>
        private static IEnumerable<Control> EditorsIn(Control root)
        {
            if (IsEditor(root)) { yield return root; yield break; }
            foreach (Control child in root.Controls)
                foreach (Control deeper in EditorsIn(child))
                    yield return deeper;
        }

        /// <summary>One module: what turns it off and on, the controls that go with it, the caption its note names.</summary>
        private sealed class Module
        {
            public string Name = "";
            public Action<AppConfig> Off = _ => { };
            public Action<AppConfig> On = _ => { };
            public string[] Fields = Array.Empty<string>();
            public string Caption = "";
            public override string ToString() => Name;
        }

        private static readonly Module[] Modules =
        {
            new Module { Name = "clipboard history", Caption = "Enable clipboard history",
                Off = c => c.EnableClipboardHistory = false, On = c => c.EnableClipboardHistory = true,
                Fields = new[] { "_pause", "_historyStartup", "_opacity" } },
            new Module { Name = "hotkeys master", Caption = "Listen for global hotkeys",
                Off = c => c.EnableHotkeys = false, On = c => c.EnableHotkeys = true,
                Fields = new[] { "_caseEnabled", "_historyEnabled" } },
            new Module { Name = "selection menu", Caption = "CyrFlip's own context menu over the selected text",
                Off = c => c.EnableContextMenu = false, On = c => c.EnableContextMenu = true,
                Fields = new[] { "_contextMenuChord", "_textMenuHotkeyEnabled" } },
            new Module { Name = "screenshot saving", Caption = "Also save captures to a folder",
                Off = c => c.ScreenshotSaveEnabled = false, On = c => c.ScreenshotSaveEnabled = true,
                Fields = new[] { "_screenshotFolderBox", "_screenshotFolderHotkeyEnabled" } },
            new Module { Name = "translator", Caption = "Enable translation of the selected text",
                Off = c => c.EnableTranslate = false, On = c => c.EnableTranslate = true,
                Fields = new[] { "_translateAutoStart", "_translateCopy", "_translatePaste", "_translateShowSource",
                    "_translateEndpoint", "_translateModel", "_translateKeepAlive", "_translateTimeout", "_translateWindowTimeout" } },
            new Module { Name = "quick notes", Caption = "Enable the quick notes",
                Off = c => c.EnableQuickNotes = false, On = c => c.EnableQuickNotes = true,
                Fields = new[] { "_quickNotesHotkeyEnabled", "_quickNotesWrap", "_quickNotesExportMeta" } },
            new Module { Name = "symbols", Caption = "Enable symbols window",
                Off = c => c.EnableSymbols = false, On = c => c.EnableSymbols = true,
                Fields = new[] { "_symbolsHotkeyEnabled", "_symbolsHideTypable" } },
            new Module { Name = "traffic light", Caption = "Enable the availability traffic light",
                Off = c => c.EnableTrafficLight = false, On = c => c.EnableTrafficLight = true,
                Fields = new[] { "_tlHotkeyEnabled", "_tlPinned", "_tlTileSize", "_tlColumns", "_tlAddBox" } },
            new Module { Name = "quick launch", Caption = "Enable quick launch",
                Off = c => c.EnableScenarioLauncher = false, On = c => c.EnableScenarioLauncher = true,
                Fields = new[] { "_launcherList", "_launcherSearch" } },
            new Module { Name = "exchange clipboard", Caption = "Enable the clipboard on the server",
                Off = c => c.EnableExchangeClipboard = false, On = c => c.EnableExchangeClipboard = true,
                Fields = new[] { "_exchangeClipboardSendHotkeyEnabled", "_exchangeClipboardGetHotkeyEnabled", "_exchangeClipboardReceivedFolderBox" } },
        };

        public static IEnumerable<object[]> ModuleCases() => Modules.Select(m => new object[] { m.Name });

        [Theory]
        [MemberData(nameof(ModuleCases))]
        public void EveryControlAModuleSwitchGreysIsExplainedAndTheNoteGoesWhenItComesBack(string moduleName)
        {
            Module module = Modules.Single(m => m.Name == moduleName);
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config("English");
                module.Off(config);
                using SettingsForm form = TestForms.NewSettings(config);
                var problems = new List<string>();
                string expected = string.Format(Template, module.Caption);

                foreach (string name in module.Fields)
                {
                    Control control = Field(form, name);
                    if (control.Enabled) { problems.Add(name + " is enabled while its switch is off - the table of this test is stale"); continue; }
                    Label? note = NoteOf(form, control);
                    if (note == null) { problems.Add(name + ": no note explains it"); continue; }
                    if (!OwnVisibility.Of(note)) problems.Add(name + ": its note is hidden");
                    if (note.Text != expected) problems.Add(name + ": note says \"" + note.Text + "\"");
                    if (!note.Enabled) problems.Add(name + ": the note itself is drawn disabled - it must stand outside the greyed block");
                    foreach (Control editor in EditorsIn(control).DefaultIfEmpty(control))
                        if (editor.AccessibleDescription != expected)
                            problems.Add(name + ": accessible description of " + editor.GetType().Name + " is \"" + editor.AccessibleDescription + "\"");
                }
                Assert.True(problems.Count == 0, string.Join("\n", problems));

                // The cause goes away: every note is hidden and every description cleared.
                module.On(config);
                form.Reload();
                foreach (string name in module.Fields)
                {
                    Control control = Field(form, name);
                    Label note = NoteOf(form, control)!;
                    Assert.False(OwnVisibility.Of(note), name + ": the note stays after the switch is on");
                    Assert.Equal("", note.Text);
                    // A checkbox then reads its row's own hint (the visible explanation is its description too);
                    // what must be gone is the reason it was unavailable.
                    foreach (Control editor in EditorsIn(control).DefaultIfEmpty(control))
                        Assert.True(editor.AccessibleDescription != expected, name + ": the reason stays as the description");
                }
            });
        }

        [Fact]
        public void TheNoteNamesTheSwitchInTheLanguageOnScreenAndFollowsALiveChange()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config("English");
                config.EnableClipboardHistory = false;
                using SettingsForm form = TestForms.NewSettings(config);
                Label note = NoteOf(form, Field(form, "_pause"))!;
                Assert.Equal(string.Format(Template, "Enable clipboard history"), note.Text);

                config.UiLanguage = "Deutsch";
                form.Reload();
                string template = Localization.Translate("Deutsch", Template);
                string caption = Localization.Translate("Deutsch", "Enable clipboard history");
                Assert.Equal(string.Format(template, caption), note.Text);
                Assert.NotEqual(string.Format(Template, "Enable clipboard history"), note.Text);
                Assert.Equal(note.Text, Field(form, "_pause").AccessibleDescription);
            });
        }

        [Fact]
        public void TheThreeRestoreButtonsSayThereIsNothingToRestoreWhileNothingIsSaved()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config("English");
                config.InputLayoutsBackup = "";
                config.LanguageHotkeysBackup = "";
                using SettingsForm form = TestForms.NewSettings(config);

                foreach (string name in new[] { "_restoreLayouts", "_restoreLanguageHotkeys" })
                {
                    Control button = Field(form, name);
                    Assert.False(button.Enabled, name);
                    Label note = NoteOf(form, button)!;
                    Assert.True(OwnVisibility.Of(note), name);
                    Assert.Equal(NothingToRestore, note.Text);
                    Assert.Equal(NothingToRestore, button.AccessibleDescription);
                }
                // The settings snapshot lives on disk, so the machine decides: the note must agree with the button.
                Control settings = Field(form, "_restoreSettings");
                Assert.Equal(!settings.Enabled, OwnVisibility.Of(NoteOf(form, settings)!));

                config.InputLayoutsBackup = "{}";
                config.LanguageHotkeysBackup = "{}";
                form.Reload();
                foreach (string name in new[] { "_restoreLayouts", "_restoreLanguageHotkeys" })
                {
                    Assert.True(Field(form, name).Enabled, name);
                    Assert.False(OwnVisibility.Of(NoteOf(form, Field(form, name))!), name);
                }
            });
        }

        [Fact]
        public void ANoteIsNotASettingOfItsOwnAndIsFoundByNoSearch()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config("English");
                config.EnableTranslate = false;
                using SettingsForm form = TestForms.NewSettings(config);
                var notes = new HashSet<Label>(form.DependencyNotes);
                Assert.NotEmpty(notes);
                Assert.Contains(notes, n => OwnVisibility.Of(n));
                Assert.All(form.BuildSearchIndex(), hit => Assert.DoesNotContain(hit.Row, notes.Cast<Control>()));
                // A note is a plain muted label next to its row, tagged so nothing mistakes it for one.
                Assert.All(notes, n => Assert.Equal(SettingsForm.DependencyNoteTag, n.Tag));
            });
        }

        /// <summary>
        /// The gate that makes the pass complete: with every module off and nothing backed up, a disabled editor
        /// of the pages that is not explained by a note carries no reason - and has to be a declared exception.
        /// Rows rebuilt from a table (layouts, language shortcuts, conversions, the hotkey list) are greyed one
        /// by one for the row's own reasons and are not part of this.
        /// </summary>
        [Fact]
        public void NoEditorOfAModulePageIsGreyedWithoutItsReason()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                AppConfig config = RegroupingHarness.Config("English");
                config.EnableHotkeys = false;
                config.EnableClipboardHistory = false;
                config.EnableContextMenu = false;
                config.ScreenshotSaveEnabled = false;
                config.EnableTranslate = false;
                config.EnableQuickNotes = false;
                config.EnableSymbols = false;
                config.EnableTrafficLight = false;
                config.EnableScenarioLauncher = false;
                config.InputLayoutsBackup = "";
                config.LanguageHotkeysBackup = "";
                using SettingsForm form = TestForms.NewSettings(config);

                var tableOwned = new HashSet<Control>();
                foreach (string name in new[] { "_layoutRows", "_languageRows", "_conversionRows", "_overviewWindowsRows" })
                    tableOwned.Add(Field(form, name));
                foreach (CollapsibleGroup group in form.Groups.Where(g => g.Id.StartsWith("keys/", StringComparison.Ordinal)))
                    tableOwned.Add(group.Body);
                // Declared exceptions, each with its reason: the cancel of a running download is live only while one runs.
                var declared = new HashSet<Control> { Field(form, "_translateCancel") };

                var unexplained = new List<string>();
                int disabledSeen = 0;
                foreach (TabPage page in form.Tabs!.TabPages)
                    Visit(page, tableOwned, declared, ref disabledSeen, unexplained);

                Assert.True(disabledSeen >= 25, "only " + disabledSeen + " disabled editors were found - the walk is looking at the wrong thing");
                Assert.True(unexplained.Count == 0, "Greyed with no reason on the control:\n" + string.Join("\n", unexplained));
            });
        }

        private static void Visit(Control control, HashSet<Control> tableOwned, HashSet<Control> declared, ref int seen, List<string> unexplained)
        {
            if (tableOwned.Contains(control)) return;
            if (IsEditor(control))
            {
                if (!control.Enabled && !declared.Contains(control))
                {
                    seen++;
                    if (string.IsNullOrEmpty(control.AccessibleDescription))
                        unexplained.Add(control.GetType().Name + " \"" + control.Text + "\" (" + control.Name + ")");
                }
                return;   // a leaf: the framework's inner parts are not ours
            }
            foreach (Control child in control.Controls) Visit(child, tableOwned, declared, ref seen, unexplained);
        }
    }
}
