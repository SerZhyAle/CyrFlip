using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(SharedGdiCollection.Name)]
    public class ContextHelpTests
    {
        [Fact]
        public void EveryFormPageAndTourStepHasExactlyOneMapRow()
        {
            var rows = ContextHelp.Inventory.surfaces;
            Assert.Equal(rows.Length, rows.Select(s => s.id).Distinct().Count());
            var forms = typeof(AppConfig).Assembly.GetTypes().Where(t => typeof(Form).IsAssignableFrom(t) && t != typeof(ThemedForm));
            Assert.Equal(forms.Select(t => t.FullName).OrderBy(t => t), rows.Where(r => r.type.Length > 0).Select(r => r.type).OrderBy(t => t));
            Assert.Equal(SettingsPageKeys.DisplayOrder.Select(k => "settings." + k).OrderBy(s => s), rows.Where(r => r.id.StartsWith("settings.")).Select(r => r.id).OrderBy(s => s));
            Assert.Equal(Enumerable.Range(0, WelcomeDialog.PageCount).Select(i => "welcome." + i), rows.Where(r => r.id.StartsWith("welcome.")).Select(r => r.id));
            foreach (var row in rows.Where(r => r.form == "none" || r.form == "inherit")) Assert.False(string.IsNullOrWhiteSpace(row.reason), row.id);
        }

        [Fact]
        public void ResolverSelectsUiLanguageAndRejectsArbitraryAnchors()
        {
            Assert.Equal("uk/index.html", ContextHelp.Resolve("index", "uk"));
            Assert.Equal("en/index.html", ContextHelp.Resolve("index", "unknown"));
            Assert.Equal("ar/settings-general.html#element-2", ContextHelp.Resolve("settings.General", "ar", "element-2"));
            Assert.Equal("en/index.html", ContextHelp.Resolve("index", "en", "../../outside"));
            Assert.Null(ContextHelp.Resolve("confirm-dialog", "en"));
            Assert.Throws<InvalidDataException>(() => ContextHelp.ReadMap("{\"schemaVersion\":2}"));
            Assert.Equal(1, ContextHelp.ReadMap("{\"schemaVersion\":1,\"optionalFutureField\":true}").schemaVersion);
        }

        [Fact]
        public void ExtractedFilesRepairCorruptionAndKeepTheElementFragment()
        {
            string root = Path.Combine(Path.GetTempPath(), "CyrFlipHelpTests", Guid.NewGuid().ToString("N"));
            try
            {
                string uri = ContextHelp.Extract("en/index.html#element-1", root);
                Assert.Equal("#element-1", new Uri(uri).Fragment);
                string path = new Uri(uri).LocalPath;
                string original = File.ReadAllText(path);
                Assert.Contains("<html lang=\"en\"", original);
                File.WriteAllText(path, "corrupt");
                ContextHelp.Extract("en/index.html", root);
                Assert.Equal(original, File.ReadAllText(path));
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "..", "help.css")));
                Assert.Throws<InvalidDataException>(() => ContextHelp.Extract("../../../outside.html", root));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void AHelpButtonUsesTheVocabularyAndDoesNotCommitTheDialog()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var dialog = new HotkeyDialog("Ctrl+Shift+F12", "Shortcut", "uk");
                    ContextHelpUi.Attach(dialog);
                    Button button = Assert.Single(HelpElements.Walk(dialog).OfType<Button>(), b => b.Name == "contextHelp");
                    Assert.Equal(Localization.Translate("uk", "Help (F1)"), button.AccessibleName);
                    Assert.True(button.Width >= 44 && button.Height >= 44);
                    Assert.Equal(DialogResult.None, button.DialogResult);
                    Assert.Equal(DialogResult.None, dialog.DialogResult);
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(30000));
            if (error != null) throw error;
        }
    }
}
