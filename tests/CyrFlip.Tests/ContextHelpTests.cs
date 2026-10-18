using System;
using System.IO;
using System.Linq;
using System.Reflection;
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

        /// <summary>CONTEXT-HELP rule 1 (S0075 section 5): bare F1 is a window key, never a chord this product captures.</summary>
        [Fact]
        public void NoShippedOrSeededChordIsBareF1()
        {
            var tokens = Enum.GetValues(typeof(ChordKind)).Cast<ChordKind>().Where(ChordDefaults.IsFixed).Select(ChordDefaults.FixedChord)
                .Concat(new[] { ChordDefaults.SeededConversionChord, ChordDefaults.SeededTranslationChord, AppConfig.DefaultFlipHotkey });
            int checkedChords = 0;
            foreach (string token in tokens.Where(t => t.Length > 0))
            {
                Assert.True(Hotkey.TryParse(token, out Hotkey chord), token);
                Assert.False(chord.Vk == 0x70 && !chord.Ctrl && !chord.Shift && !chord.Alt && !chord.Win, "bare F1 is shipped as " + token);
                checkedChords++;
            }
            Assert.True(checkedChords >= 10, "the walk must reach the shipped chords, not an empty list");
        }

        /// <summary>CONTEXT-HELP rule 3 and 7: F1 and the corner button are one ask with one answer, and the modal dialog stays open.</summary>
        [Fact]
        public void F1AndTheHelpButtonOpenTheSameTargetAndLeaveTheDialogOpen()
        {
            string cache = Path.Combine(Path.GetTempPath(), "CyrFlipHelpTests", Guid.NewGuid().ToString("N"));
            var launched = new System.Collections.Generic.List<string>();
            Action<string> previousLaunch = ContextHelp.Launch;
            string? previousRoot = ContextHelp.CacheRoot;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    ContextHelp.Launch = launched.Add;
                    ContextHelp.CacheRoot = cache;
                    using var dialog = new HotkeyDialog("Ctrl+Shift+F12", "Shortcut", "uk");
                    ContextHelpUi.Attach(dialog);
                    Button button = Assert.Single(HelpElements.Walk(dialog).OfType<Button>(), b => b.Name == "contextHelp");

                    // PerformClick refuses a control whose parent was never shown; the handler is what is under test.
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
                    Pump(() => launched.Count >= 1);

                    var keyArguments = new object[] { new Message(), Keys.F1 };
                    bool handled = (bool)typeof(ThemedForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                        .Invoke(dialog, keyArguments)!;
                    Assert.True(handled);
                    Pump(() => launched.Count >= 2);

                    Assert.Equal(2, launched.Count);
                    Assert.Equal(launched[0], launched[1]);
                    Assert.Contains("/uk/hotkey-dialog.html", launched[0].Replace('\\', '/'));
                    Assert.Equal(DialogResult.None, dialog.DialogResult);
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            try
            {
                thread.Start();
                Assert.True(thread.Join(120000));
            }
            finally
            {
                ContextHelp.Launch = previousLaunch;
                ContextHelp.CacheRoot = previousRoot;
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
            }
            if (error != null) throw error;
        }

        /// <summary>The help call is async: its continuation arrives on the form's message loop, so the test pumps one.</summary>
        private static void Pump(Func<bool> done)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(60);
            while (!done() && DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
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
