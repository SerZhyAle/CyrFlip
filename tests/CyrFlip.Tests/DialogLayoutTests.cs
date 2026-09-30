using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The two modal dialogs are built in code, shown in 13 languages and drawn at whatever the display
    /// scaling makes of the UI font - the combination that had "Assign" rendering as "Assig" and
    /// "Cancel" as "Canc". Build each dialog in each language and check that every caption fits the
    /// control drawing it, which is what "laid out by content, not by pixel coordinates" has to mean.
    /// </summary>
    public class DialogLayoutTests
    {
        private static readonly List<InputLayouts.Installed> Layouts = new List<InputLayouts.Installed>
        {
            new InputLayouts.Installed { Klid = "00000409", LangId = 0x0409, LanguageName = "English (United States)", DisplayName = "US", IsDefault = true },
            new InputLayouts.Installed { Klid = "00000419", LangId = 0x0419, LanguageName = "русский (Россия)", DisplayName = "Russian" },
        };

        private static readonly List<InputLayouts.Available> Available = new List<InputLayouts.Available>
        {
            new InputLayouts.Available { Klid = "00000422", LangId = 0x0422, LanguageName = "українська (Україна)", DisplayName = "Ukrainian (Enhanced)" },
        };

        /// <summary>A conversion row pointing at a Polish layout that has since been uninstalled.</summary>
        private static readonly LayoutConversionProfile StaleRow = new LayoutConversionProfile
        {
            SourceKlid = "00000415", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F8",
        };

        /// <summary>A collected bundle with one truncated and one dropped file - every row shape at once.</summary>
        private static readonly SupportBundle.Result Bundle = new SupportBundle.Result
        {
            ArchivePath = @"C:\Users\u\AppData\Local\CyrFlip\reports\CyrFlip-logs-26.7.29.2340-20260729-2340.zip",
            ArchiveBytes = 41_000,
            Entries = new List<SupportBundle.Entry>
            {
                new SupportBundle.Entry { Name = "report.txt", Bytes = 2_100 },
                new SupportBundle.Entry { Name = "launcher.log", Bytes = 524_288, Truncated = true, OmittedBytes = 9_000_000 },
            },
            Dropped = new List<string> { "caret-diagnostics.txt" },
        };

        /// <summary>A message long enough to wrap, with a path in it that cannot.</summary>
        private const string LongMessage = "Не удалось открыть почтовую программу. Отправьте архив вручную на адрес: sza@ukr.net\n\n"
            + @"C:\Users\u\AppData\Local\CyrFlip\reports\CyrFlip-logs-26.7.29.2340-20260729-2340.zip";

        /// <summary>
        /// Escape leaves every dialog with nothing changed (<c>APP-BEHAVIOUR</c> rule 1), and a
        /// destructive question answers "no" to Enter as well as to Escape (ticket S0020 v0.2 item 6).
        /// </summary>
        [Fact]
        public void EveryDialogHasAnEscapeAndADestructiveQuestionDefaultsToNo()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                var dialogs = new List<Form>
                {
                    new HotkeyDialog("Ctrl+Shift+F12", "T", "English"),
                    new LayoutPickerDialog(new string[0], "English", Available),
                    new LayoutConversionDialog(Layouts, null, "English"),
                    new LauncherScenarioDialog(null, "English"),
                    new YtDlpLinkDialog("English"),
                    new TranslationDialog(null, "English"),
                    new SupportBundleDialog(Bundle, "English"),
                    new ExchangeExportDialog("English", notesAvailable: true, notesWarningOff: false),
                    new ClipboardHistorySearchWindow(new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(Path.GetTempPath(), "cyrflip-test-" + Guid.NewGuid().ToString("N"))), "English"),
                    new ConfirmDialog("English", "?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, false, owned: true),
                };
                foreach (Form dialog in dialogs)
                {
                    if (dialog.CancelButton == null) problems.Add(dialog.GetType().Name + " has no Escape");
                    dialog.Dispose();
                }

                using var destructive = new ConfirmDialog("English", "Delete?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true, owned: true);
                if (((Button)destructive.CancelButton!).DialogResult != DialogResult.No) problems.Add("Escape is not No");
                if (((Button)destructive.AcceptButton!).DialogResult != DialogResult.No) problems.Add("Enter is not No");
                Button yes = destructive.Buttons[0];
                if (yes.DialogResult != DialogResult.Yes || yes.BackColor != ThemePalette.Light.Danger)
                    problems.Add("the destructive answer is not on the danger role");

                using var plain = new ConfirmDialog("English", "Import?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, danger: false, owned: true);
                if (((Button)plain.AcceptButton!).DialogResult != DialogResult.Yes) problems.Add("a plain question does not default to Yes");
                if (((Button)plain.CancelButton!).DialogResult != DialogResult.No) problems.Add("a plain question's Escape is not No");
            }, problems);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void NoCaptionIsClippedInAnyLanguage()
        {
            var problems = new List<string>();
            int inspected = 0;
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    using (var dialog = new HotkeyDialog("Ctrl+Shift+F12", "T", language))
                        inspected += Check(dialog, language, "HotkeyDialog", problems);
                    // The widest chord the capture can produce, drawn 1.4x bold (ticket S0007, DL-5).
                    using (var dialog = new HotkeyDialog(HotkeyDialog.LongestChord, "T", language))
                        inspected += Check(dialog, language, "HotkeyDialog(longest)", problems);
                    // The add-layout picker, off pixel geometry since S0007 DL-3.
                    using (var dialog = new LayoutPickerDialog(new string[0], language, Available))
                        inspected += Check(dialog, language, "LayoutPickerDialog", problems);
                    // A row whose layout is no longer installed keeps it, marked (DL-4).
                    using (var dialog = new LayoutConversionDialog(Layouts, StaleRow, language))
                        inspected += Check(dialog, language, "LayoutConversionDialog(stale)", problems);
                    using (var dialog = new LayoutConversionDialog(Layouts, null, language))
                        inspected += Check(dialog, language, "LayoutConversionDialog", problems);
                    // The launcher dialogs, once per scenario type: the inactive type's section is
                    // detached from the tree, so each construction covers exactly what is laid out.
                    using (var dialog = new LauncherScenarioDialog(null, language))
                        inspected += Check(dialog, language, "LauncherScenarioDialog(exe)", problems);
                    using (var dialog = new LauncherScenarioDialog(
                        new LauncherScenario { Name = "x", Type = LauncherScenarioType.YtDlp }, language))
                        inspected += Check(dialog, language, "LauncherScenarioDialog(ytdlp)", problems);
                    using (var dialog = new YtDlpLinkDialog(language))
                        inspected += Check(dialog, language, "YtDlpLinkDialog", problems);
                    using (var dialog = new TranslationDialog(null, language))
                        inspected += Check(dialog, language, "TranslationDialog", problems);
                    // The translation popup hosts real buttons, so it is measurable here - and it is
                    // the one window whose buttons sit in a row that has to survive 13 languages.
                    using (var window = new TranslationResultWindow(new AppConfig { UiLanguage = language }, language))
                        inspected += Check(window, language, "TranslationResultWindow", problems);
                    // The log bundle dialog: three buttons in a row plus two wrapped paragraphs, in
                    // 13 languages - the exact shape that used to clip.
                    // The exchange export (ticket S0023): every row visible, then the notes-only shape.
                    using (var dialog = new ExchangeExportDialog(language, notesAvailable: true, notesWarningOff: false))
                        inspected += Check(dialog, language, "ExchangeExportDialog", problems);
                    using (var dialog = new ExchangeExportDialog(language, notesAvailable: false, notesWarningOff: true))
                        inspected += Check(dialog, language, "ExchangeExportDialog(no notes)", problems);
                    using (var dialog = new SupportBundleDialog(Bundle, language))
                        inspected += Check(dialog, language, "SupportBundleDialog", problems);
                    using (var search = new ClipboardHistorySearchWindow(new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(Path.GetTempPath(), "cyrflip-test-" + Guid.NewGuid().ToString("N"))), language))
                        inspected += Check(search, language, "ClipboardHistorySearchWindow", problems);
                    using (var search = new ClipboardHistorySearchWindow(new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(Path.GetTempPath(), "cyrflip-test-" + Guid.NewGuid().ToString("N"))), language, (_, _) => { }))
                        inspected += Check(search, language, "ClipboardHistorySearchWindow(exchange)", problems);
                    // The "please wait" window of the off-thread imports and exports (ticket S0030 HT-2).
                    using (var dialog = new BusyDialog(language, System.Threading.Tasks.Task.CompletedTask))
                        inspected += Check(dialog, language, "BusyDialog", problems);
                    // CyrFlip's own message box (ticket S0020): every button set it is asked for, the
                    // destructive one included - "Отмена" / "Abbrechen" / "إلغاء" measured, not assumed.
                    foreach (MessageBoxButtons buttons in new[] { MessageBoxButtons.OK, MessageBoxButtons.YesNo, MessageBoxButtons.OKCancel })
                        foreach (bool danger in new[] { false, true })
                            using (var dialog = new ConfirmDialog(language, LongMessage, buttons, MessageBoxIcon.Warning, danger, owned: true))
                                inspected += Check(dialog, language, "ConfirmDialog(" + buttons + (danger ? ",danger" : "") + ")", problems);
                }
            }, problems);

            Assert.True(problems.Count == 0, "Clipped dialog captions:\n" + string.Join("\n", problems));
            // A walk that reached nothing would pass just as quietly as a clean one.
            Assert.True(inspected >= 12 * Localization.Names.Length, "Only " + inspected + " captions were inspected");
        }

        /// <summary>
        /// The caption walk skips combo boxes, because one legitimately scrolls its items - but the two
        /// language pickers are the width of their own longest entry, and "Язык активной раскладки" in
        /// German or Hindi is exactly the string a fixed width used to cut. Measure them separately.
        /// </summary>
        [Fact]
        public void NoLanguagePickerIsNarrowerThanItsWidestEntry()
        {
            var problems = new List<string>();
            int inspected = 0;
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    using (var dialog = new TranslationDialog(null, language))
                        inspected += CheckCombos(dialog, language, "TranslationDialog", problems);
                    using (var window = new TranslationResultWindow(new AppConfig { UiLanguage = language }, language))
                        inspected += CheckCombos(window, language, "TranslationResultWindow", problems);
                }
            }, problems);

            Assert.True(problems.Count == 0, "Clipped language pickers:\n" + string.Join("\n", problems));
            Assert.True(inspected >= 2 * Localization.Names.Length, "Only " + inspected + " combo boxes were inspected");
        }

        private static int CheckCombos(Form form, string language, string name, List<string> problems)
        {
            form.CreateControl();
            form.PerformLayout();
            return WalkCombos(form, language, name, problems);
        }

        private static int WalkCombos(Control control, string language, string name, List<string> problems)
        {
            int inspected = 0;
            if (control is ComboBox combo && combo.Items.Count > 0)
            {
                inspected++;
                int widest = 0;
                string longest = "";
                foreach (object item in combo.Items)
                {
                    string text = item.ToString() ?? "";
                    int width = TextRenderer.MeasureText(text, combo.Font).Width;
                    if (width <= widest) continue;
                    widest = width;
                    longest = text;
                }
                int needed = widest + SystemInformation.VerticalScrollBarWidth;
                if (combo.Width < needed)
                    problems.Add($"[{language}] {name}.ComboBox \"{longest}\": needs {needed}, got {combo.Width}");
            }
            foreach (Control child in control.Controls) inspected += WalkCombos(child, language, name, problems);
            return inspected;
        }

        /// <summary>
        /// Ticket S0007 DL-4: editing only the chord of a row whose source layout is gone must not
        /// quietly turn the row into "&lt;first installed layout&gt; ⇄ ..".
        /// </summary>
        [Fact]
        public void AnUninstalledLayoutStaysSelectedInTheRowEditor()
        {
            var problems = new List<string>();
            string? source = null, target = null;
            OnUiThread(() =>
            {
                using var dialog = new LayoutConversionDialog(Layouts, StaleRow, "English");
                FieldInfo field(string name) => typeof(LayoutConversionDialog).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
                source = ((ComboBox)field("_source").GetValue(dialog)!).SelectedItem?.ToString();
                target = ((ComboBox)field("_target").GetValue(dialog)!).SelectedItem?.ToString();
            }, problems);

            Assert.Empty(problems);
            Assert.Contains("00000415", source);
            Assert.Contains("(not installed)", source);
            Assert.Contains("00000419", target);
        }

        /// <summary>
        /// The guard above is only worth having if it can fail: a caption that does not fit its control
        /// has to be reported. Fixed geometry is exactly what the dialogs no longer use.
        /// </summary>
        [Fact]
        public void TheGuardReportsACaptionThatDoesNotFit()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                using var form = new Form();
                form.Controls.Add(new Button { Text = "Assign", AutoSize = false, Size = new Size(20, 8) });
                Check(form, "test", "Fixed", problems);
            }, problems);

            string report = Assert.Single(problems);
            Assert.Contains("Assign", report);
        }

        private static void OnUiThread(Action body, List<string> problems)
        {
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { problems.Add("EXCEPTION: " + ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        /// <summary>
        /// Forcing the handle runs the layout, after which a control's preferred size is what it needs and
        /// its actual size is what it got. Only labels and buttons are checked: those carry the captions,
        /// and a combo box legitimately scrolls its items instead of growing.
        /// </summary>
        private static int Check(Form dialog, string language, string name, List<string> problems)
        {
            dialog.CreateControl();
            dialog.PerformLayout();
            return Walk(dialog, language, name, problems);
        }

        private static int Walk(Control control, string language, string name, List<string> problems)
        {
            int inspected = 0;
            if ((control is Label label && !label.AutoEllipsis) || control is Button)
            {
                inspected++;
                Size wanted = control.GetPreferredSize(Size.Empty);
                if (wanted.Width > control.Width || wanted.Height > control.Height)
                    problems.Add($"[{language}] {name}.{control.GetType().Name} \"{Trim(control.Text)}\": "
                        + $"needs {wanted.Width}x{wanted.Height}, got {control.Width}x{control.Height}");
            }
            foreach (Control child in control.Controls) inspected += Walk(child, language, name, problems);
            return inspected;
        }

        private static string Trim(string s) => s.Length <= 40 ? s : s.Substring(0, 37) + "...";
    }
}
