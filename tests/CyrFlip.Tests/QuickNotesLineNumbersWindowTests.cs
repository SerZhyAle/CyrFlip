using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0078 at the level of the quick-notes window: the numbers beside a real note, what they do
    /// not touch (the stored bytes, the journal, the modification date), the checklist that never gets
    /// them, switching notes, a right-to-left interface, and the setting reaching the editor.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class QuickNotesLineNumbersWindowTests : IDisposable
    {
        private readonly string _root;

        public QuickNotesLineNumbersWindowTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-linenum-win-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static void OnUiThread(Action body)
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
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        /// <summary>
        /// A window of a usable size: opened at its measured minimum the editor is a few rows high, and a row
        /// that is not on the screen has - rightly - no number.
        /// </summary>
        private static AppConfig Config(bool numbers = false, string? language = null)
        {
            var config = new AppConfig { QuickNotesLineNumbers = numbers, QuickNotesWidth = 900, QuickNotesHeight = 640 };
            if (language != null) config.UiLanguage = language;
            return config;
        }

        private string StorePath(string name) => Path.Combine(_root, name + "-" + QuickNotesStore.FileName);

        private QuickNotesService Service(string name) => new QuickNotesService(new QuickNotesStore(StorePath(name), new FakeCipher()));

        private static T Field<T>(object owner, string name)
            => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

        private static void PumpFor(int milliseconds)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        private static QuickNotesTextEditor EditorOf(QuickNotesWindow window) => Field<QuickNotesTextEditor>(window, "_textEditor");

        private static void ScrollToEnd(QuickNotesTextEditor editor)
        {
            WindowInterop.SendMessage(editor.TextBox.Handle, WindowInterop.EM_LINESCROLL, IntPtr.Zero, (IntPtr)1000000);
            Application.DoEvents();
        }

        // =========================================================================
        // A note is shown, scrolled and numbered - and stays exactly what it was
        // =========================================================================

        [Fact]
        public void ShowingScrollingAndTogglingTheNumbersWritesNothingAndKeepsTheExactBytes()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("bytes");
                using var window = new QuickNotesWindow(service, Config(), () => { });
                // Bare LF line endings and a trailing break: what the context menu hands over from a Unix file.
                string lf = "alpha\nbeta\n\ngamma\n" + string.Concat(Enumerable.Range(0, 80).Select(i => "line " + i + "\n"));
                window.ShowWithText(lf);
                window.CommitCurrent();
                service.Flush();
                Application.DoEvents();

                var list = Field<ListView>(window, "_list");
                var note = (QuickNote)list.Items[0].Tag;
                Assert.Equal(lf, note.RawText);
                DateTime changed = note.UpdatedAtUtc;
                byte[] before = File.ReadAllBytes(StorePath("bytes"));
                int recordsBefore = Encoding.UTF8.GetString(before).Split('\n').Count(l => l.Trim().Length > 0);

                QuickNotesTextEditor editor = EditorOf(window);
                string editorText = editor.TextBox.Text;
                foreach (bool on in new[] { true, false, true, true, false, true })
                {
                    window.ApplyLineNumbers(on);
                    window.ApplyWordWrap(on);
                    Application.DoEvents();
                    ScrollToEnd(editor);
                    WindowInterop.SendMessage(editor.TextBox.Handle, WindowInterop.EM_LINESCROLL, IntPtr.Zero, (IntPtr)(-1000000));
                    Application.DoEvents();
                }
                window.CommitCurrent();

                // Longer than the debounce: a save, had anything asked for one, would have happened by now.
                PumpFor(QuickNotesService.DebounceMs + 600);
                service.Flush();

                Assert.Equal(lf, note.RawText);                       // still the bare LF bytes, never the edit control's CRLF
                Assert.Equal(changed, note.UpdatedAtUtc);
                Assert.Equal(before, File.ReadAllBytes(StorePath("bytes")));
                Assert.Equal(recordsBefore, Encoding.UTF8.GetString(File.ReadAllBytes(StorePath("bytes"))).Split('\n').Count(l => l.Trim().Length > 0));
                Assert.Equal(editorText, editor.TextBox.Text);        // the edit holds the text and only the text
                Assert.DoesNotContain("↳", editor.TextBox.Text);
            });
        }

        [Fact]
        public void ALfNoteIsNumberedByLogicalLinesAndTheLastNumberIsTheCounters()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("lines");
                var config = Config(true);
                using var window = new QuickNotesWindow(service, config, () => { });
                string text = "alpha\nbeta\r\n\rgamma\r\n" + string.Concat(Enumerable.Range(0, 60).Select(i => "row\n"));
                window.ShowWithText(text);
                Application.DoEvents();

                QuickNotesTextEditor editor = EditorOf(window);
                Assert.True(editor.ShowLineNumbers);
                Assert.True(editor.Gutter.Visible);
                var note = Field<QuickNote>(window, "_current");

                Assert.Equal(new[] { "1", "2", "3", "4" }, editor.LastLabels.Take(4).Select(l => l.Text).ToArray());   // LF, CRLF and a lone CR are one break each
                ScrollToEnd(editor);
                Assert.Equal(note.LineCount.ToString(), editor.LastLabels.Last().Text);
                Assert.Equal(note.LineCount, editor.LineIndex.LineCount);
            });
        }

        // =========================================================================
        // The checklist keeps its own look; notes keep their own numbers
        // =========================================================================

        [Fact]
        public void TheChecklistHasNoNumbersAndTheTextComesBackWithThem()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("kinds");
                using var window = new QuickNotesWindow(service, Config(true), () => { });
                window.ShowWithText("one\r\ntwo\r\nthree");
                Application.DoEvents();
                QuickNotesTextEditor editor = EditorOf(window);
                var host = Field<Control>(window, "_itemsHost");
                Assert.True(editor.Visible);
                Assert.Equal(new[] { "1", "2", "3" }, editor.LastLabels.Select(l => l.Text).ToArray());

                Field<Button>(window, "_newList").PerformClick();
                Application.DoEvents();
                Assert.False(editor.Visible, "A checklist shows its own list and not the text editor.");
                Assert.True(host.Visible);
                Assert.DoesNotContain(host.Controls.Cast<Control>(), c => c is QuickNotesLineGutter || c is QuickNotesTextEditor);
                Assert.True(editor.ShowLineNumbers, "The option is the app's, not the note's: a checklist does not reset it.");

                Field<Button>(window, "_newText").PerformClick();
                Application.DoEvents();
                Assert.True(editor.Visible);
                Assert.True(editor.ShowLineNumbers);
                Assert.True(editor.Gutter.Visible);
                editor.TextBox.Text = "a\r\nb";
                Application.DoEvents();
                Assert.Equal(new[] { "1", "2" }, editor.LastLabels.Select(l => l.Text).ToArray());
            });
        }

        [Fact]
        public void SwitchingNotesRebuildsTheStripForTheNewDocument()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("switch");
                using var window = new QuickNotesWindow(service, Config(true), () => { });
                window.ShowWithText("short\r\nnote\r\nhere");
                window.CommitCurrent();
                window.ShowWithText(string.Join("\r\n", Enumerable.Range(1, 150).Select(i => "long " + i)));
                window.CommitCurrent();
                Application.DoEvents();

                var list = Field<ListView>(window, "_list");
                Assert.Equal(2, list.Items.Count);
                QuickNotesTextEditor editor = EditorOf(window);

                list.Items[1].Selected = true;   // the three-line note
                Application.DoEvents();
                int narrow = editor.Gutter.Width;
                Assert.Equal(new[] { "1", "2", "3" }, editor.LastLabels.Select(l => l.Text).ToArray());

                list.Items[0].Selected = true;   // the 150-line one: three digits now
                Application.DoEvents();
                Assert.True(editor.Gutter.Width > narrow, "A note with 150 lines needs a wider strip than one with 3.");
                Assert.Equal("1", editor.LastLabels[0].Text);
                Assert.Equal(editor.Gutter.Right, editor.TextBox.Left);
                Assert.Equal(150, editor.LineIndex.LineCount);

                list.Items[1].Selected = true;
                Application.DoEvents();
                Assert.Equal(narrow, editor.Gutter.Width);
            });
        }

        // =========================================================================
        // Right-to-left interface, and the setting itself
        // =========================================================================

        [Fact]
        public void AnArabicWindowKeepsTheNumbersOnThePhysicalLeft()
        {
            OnUiThread(() =>
            {
                string arabic = Localization.Names.First(Localization.IsRightToLeft);
                using QuickNotesService service = Service("rtl");
                using var window = new QuickNotesWindow(service, Config(true, arabic), () => { });
                window.ShowWithText(string.Join("\r\n", Enumerable.Range(1, 40).Select(i => "سطر " + i)));
                Application.DoEvents();
                Assert.Equal(RightToLeft.Yes, window.RightToLeft);

                QuickNotesTextEditor editor = EditorOf(window);
                WindowInterop.GetWindowRect(editor.Handle, out WindowInterop.RECT whole);
                WindowInterop.GetWindowRect(editor.Gutter.Handle, out WindowInterop.RECT strip);
                WindowInterop.GetWindowRect(editor.TextBox.Handle, out WindowInterop.RECT text);
                Assert.Equal(whole.Left, strip.Left);
                Assert.Equal(strip.Right, text.Left);
                Assert.Equal(whole.Right, text.Right);
                Assert.Equal("1", editor.LastLabels[0].Text);
                Assert.Equal("أرقام الأسطر", editor.Gutter.AccessibleName);   // the strip's name in the window's own language
            });
        }

        [Fact]
        public void EveryUiLanguageNamesTheStripAndExplainsTheMarker()
        {
            // No window needed: the two strings the strip carries, in all thirteen languages.
            foreach (string language in Localization.Names)
            {
                string name = Localization.Translate(language, "Line numbers");
                string tip = Localization.Translate(language, "↳ Continuation of a wrapped line from above");
                Assert.False(string.IsNullOrWhiteSpace(name), language);
                Assert.Contains("↳", tip);
                if (language != "English")
                {
                    Assert.NotEqual("Line numbers", name);
                    Assert.NotEqual("↳ Continuation of a wrapped line from above", tip);
                }
            }
        }

        [Fact]
        public void TheOptionReachesAnOpenEditorAndIsOffByDefault()
        {
            OnUiThread(() =>
            {
                Assert.False(new AppConfig().QuickNotesLineNumbers);
                using QuickNotesService service = Service("live");
                using var window = new QuickNotesWindow(service, Config(), () => { });
                window.ShowWithText("a\r\nb\r\nc");
                Application.DoEvents();
                QuickNotesTextEditor editor = EditorOf(window);
                Assert.False(editor.ShowLineNumbers);
                Assert.False(editor.Gutter.Visible);
                Assert.Equal(0, editor.Gutter.Width);
                int plainTextLeft = editor.TextBox.Left;

                window.ApplyLineNumbers(true);   // what the settings page's checkbox ends in
                Application.DoEvents();
                Assert.True(editor.Gutter.Visible);
                Assert.True(editor.Gutter.Width > 0);
                Assert.True(editor.TextBox.Left > plainTextLeft);
                Assert.Equal(new[] { "1", "2", "3" }, editor.LastLabels.Select(l => l.Text).ToArray());

                window.ApplyLineNumbers(false);
                Application.DoEvents();
                Assert.False(editor.Gutter.Visible);
                Assert.Equal(plainTextLeft, editor.TextBox.Left);
            });
        }

        // =========================================================================
        // The settings file: an old file has no key, a new one has it
        // =========================================================================

        [Fact]
        public void AnOldSettingsFileLeavesTheOptionAloneAndANewOneSetsIt()
        {
            string Json(string settings) => "{\"format\":\"CyrFlip Settings\",\"schemaVersion\":2,\"settings\":{" + settings + "}}";

            // An older file never heard of the key: whatever this machine has stays.
            foreach (bool local in new[] { false, true })
            {
                var plan = SettingsImportPlan.Build(new AppConfig { QuickNotesLineNumbers = local },
                    SettingsFileReader.Read(Encoding.UTF8.GetBytes(Json("\"QuickNotesWordWrap\":true"))));
                Assert.Equal(local, plan.Candidate.QuickNotesLineNumbers);
                Assert.True(plan.Candidate.QuickNotesWordWrap);
            }

            var on = SettingsImportPlan.Build(new AppConfig(), SettingsFileReader.Read(Encoding.UTF8.GetBytes(Json("\"QuickNotesLineNumbers\":true"))));
            Assert.True(on.Candidate.QuickNotesLineNumbers);
            Assert.Contains(on.Changes, c => c.Entry.Key == "QuickNotesLineNumbers");

            var off = SettingsImportPlan.Build(new AppConfig { QuickNotesLineNumbers = true }, SettingsFileReader.Read(Encoding.UTF8.GetBytes(Json("\"QuickNotesLineNumbers\":false"))));
            Assert.False(off.Candidate.QuickNotesLineNumbers);

            // The checked-in files: the old one has no such key, the current one carries it.
            string fixtures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile");
            Assert.False(SettingsFileReader.ReadFile(Path.Combine(fixtures, "golden-v1.json")).Values.ContainsKey("QuickNotesLineNumbers"));
            Assert.True(SettingsFileReader.ReadFile(Path.Combine(fixtures, "golden-v2.json")).Values.ContainsKey("QuickNotesLineNumbers"));
        }
    }
}
