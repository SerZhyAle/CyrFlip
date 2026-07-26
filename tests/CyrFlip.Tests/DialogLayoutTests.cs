using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
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
