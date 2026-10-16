using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class SettingsApplyCoverageTests
    {
        [Fact]
        public void EveryExportedKeyHasALiveApplyPath()
        {
            Assert.Equal(SettingsFileInventory.Exported.Select(e => e.Key).OrderBy(k => k), SettingsApplyCoverage.Paths.Keys.OrderBy(k => k));
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "CyrFlip", "CyrFlipContext.SettingsTransfer.cs")));
            foreach (string path in SettingsApplyCoverage.Paths.Values.Where(v => !v.StartsWith("live config")))
                Assert.Contains(path, source);
        }
    }
    [Collection(SharedGdiCollection.Name)]
    public class SettingsTransferDialogTests
    {
        [Fact]
        public void ScenariosAreSelectedAndCanBeDeselectedInThePreview()
        {
            var local = new AppConfig();
            var doc = SettingsFileReaderTests.Read(version: 2);
            doc.Scenarios = new List<string> { LauncherScenarioStore.ToXml(new LauncherScenario { Name = "Example", Path = "calc.exe" }) };
            using var form = new SettingsTransferDialog("English", doc,
                selected => SettingsImportPlan.Build(local, doc, selected: selected));
            Assert.Single(form.Plan.Scenarios!);
            Assert.Contains("scenarios", form.SelectedKeys);
            var choice = Walk(form).OfType<CheckBox>().Single(c => c.Text.IndexOf("scenarios", StringComparison.OrdinalIgnoreCase) >= 0);
            choice.Checked = false;
            Assert.Null(form.Plan.Scenarios);
            Assert.DoesNotContain("scenarios", form.SelectedKeys);
        }
        internal static SettingsTransferDialog Make(string language)
        {
            var local = new AppConfig { UiLanguage = language };
            var imported = new AppConfig { UiLanguage = "English", EnableClipboardHistory = true, EnableQuickNotes = true,
                Theme = "dark", CursorSize = 48, TranslateEndpoint = "http://another-machine:11434", TranslateModel = new string('m', 180),
                TranslateTimeoutSeconds = 5, KeepSystemAwake = true };
            var doc = SettingsFileReader.Read(Encoding.UTF8.GetBytes(SettingsFileWriter.Write(imported, DateTime.UtcNow, "99.1.1.1234")));
            return new SettingsTransferDialog(language, doc, selected => SettingsImportPlan.Build(local, doc, selected: selected));
        }
        [UiSweepFact]
        public void AllThirteenLanguagesKeepSafeDefaultAndFitCaptions()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    foreach (string language in Localization.Names)
                    {
                        using var form = Make(language); form.Show(); form.PerformLayout();
                        Assert.Same(form.CancelButton, form.AcceptButton); Assert.NotNull(form.CancelButton);
                        Assert.False(form.Plan.Candidate.EnableClipboardHistory); Assert.False(form.Plan.Candidate.EnableQuickNotes);
                        Assert.DoesNotContain(form.Plan.Changes, c => c.Entry.Key == "TranslateEndpoint");
                        foreach (Control control in Walk(form).Where(c => c is Button || c is CheckBox))
                            Assert.True(control.Width >= TextRenderer.MeasureText(control.Text, control.Font).Width, language + ": " + control.Text);
                        Assert.True(form.Height <= Screen.FromControl(form).WorkingArea.Height, language + ": dialog taller than screen");
                        form.Close();
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(30000));
            if (failure != null) throw failure;
        }
        [Fact]
        public void EveryExportedCaptionAndReasonHasAllTranslations()
        {
            var table = Localization.All.ToDictionary(e => e.Key, e => e.Value);
            foreach (string caption in SettingsFileInventory.Exported.Select(e => e.Caption)
                .Concat(new[] { "Settings transfer", "Export settings...", "Import settings...", "Restore settings before import..." }))
            {
                Assert.True(table.ContainsKey(caption), caption);
                Assert.Equal(Localization.Names.Length, table[caption].Length); Assert.All(table[caption], value => Assert.NotEmpty(value));
            }
        }
        [Fact]
        public void NoChangesOffersOnlyCloseAndNoApply()
        {
            var local = new AppConfig(); var document = SettingsFileReaderTests.Read();
            using var form = new SettingsTransferDialog("English", document, selected => SettingsImportPlan.Build(local, document, selected: selected));
            Assert.Empty(form.Plan.Changes); Assert.DoesNotContain(Walk(form).OfType<Button>(), b => b.Text == "Apply" && b.Visible);
        }
        private static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (var nested in Walk(child)) yield return nested; }
        }
    }
}
