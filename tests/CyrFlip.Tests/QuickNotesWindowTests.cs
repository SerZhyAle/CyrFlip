using System;
using System.Collections.Generic;
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
    /// The notes window as a control tree. It is built in <b>every</b> supported language and walked
    /// for surviving Cyrillic, exactly as the settings window is - this window lives outside the
    /// settings tree, so nothing else would catch an untranslated caption in it. The construction
    /// itself is half the value: a WinForms window that throws while being built takes the whole
    /// feature with it, and the only way to find out is to build one.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds real windows - see SharedGdiCollection
    public class QuickNotesWindowTests : IDisposable
    {
        private readonly string _root;

        public QuickNotesWindowTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-noteswin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
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

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private QuickNotesService Service(string name)
            => new QuickNotesService(new QuickNotesStore(
                Path.Combine(_root, name + "-" + QuickNotesStore.FileName), new FakeCipher()));

        [Fact]
        public void AGlyphOnlyButtonCarriesTheRecordsNameInEveryLanguage()
        {
            // ICON-RENDER rule 8 (ticket S0022 A4/A5): the two move buttons show the vocabulary glyph and no
            // text, so what a screen reader says is the accessible name - never empty, in the UI language.
            var problems = new List<string>();
            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    using QuickNotesService service = Service("glyph" + i);
                    using var window = new QuickNotesWindow(service, new AppConfig { UiLanguage = Localization.Names[i] }, () => { }, exchange: (_, _) => { });
                    int found = 0;
                    foreach (Control control in All(window))
                    {
                        if (!(control is ButtonBase button) || button.Image == null || button.Text.Length > 0) continue;
                        found++;
                        if (string.IsNullOrWhiteSpace(button.AccessibleName)) problems.Add(Localization.Names[i] + ": an image-only button has no accessible name");
                    }
                    if (found != 2) problems.Add(Localization.Names[i] + ": expected the 2 move buttons to be image-only, found " + found);
                }
            });
            Assert.Empty(problems);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in All(child)) yield return deeper;
            }
        }
        [Fact]
        public void TheWindowBuildsInEveryLanguageWithNoCyrillicLeftInALatinBuild()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    string code = Localization.Codes[i];
                    var config = new AppConfig { UiLanguage = language };
                    using QuickNotesService service = Service("lang" + i);
                    using var window = new QuickNotesWindow(service, config, () => { }, exchange: (_, _) => { });

                    // Russian and Ukrainian are written in Cyrillic, so surviving Cyrillic proves
                    // nothing there - the same exemption the settings-window walk makes.
                    if (code == "ru" || code == "uk") continue;
                    var seen = new HashSet<object>();
                    Walk(window, language, problems, seen);
                    // Menus that live in a field and are only shown on a click (S0029 RB-4): they are
                    // in no control tree, so the walk above would never reach them.
                    foreach (FieldInfo field in typeof(QuickNotesWindow).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                        if (field.GetValue(window) is ToolStrip strip)
                            WalkItems(strip.Items, language, problems, seen);
                }
            });

            Assert.True(problems.Count == 0, "Untranslated captions:\n" + string.Join("\n", problems));
        }

        private static void Walk(Control control, string language, List<string> problems, HashSet<object> seen)
        {
            if (!seen.Add(control)) return;
            Report(control.GetType().Name, control.Text, language, problems);
            if (control is ToolStrip strip) WalkItems(strip.Items, language, problems, seen);
            if (control.ContextMenuStrip != null) WalkItems(control.ContextMenuStrip.Items, language, problems, seen);
            foreach (Control child in control.Controls) Walk(child, language, problems, seen);
        }

        private static void WalkItems(ToolStripItemCollection items, string language, List<string> problems, HashSet<object> seen)
        {
            foreach (ToolStripItem item in items)
            {
                if (!seen.Add(item)) continue;
                Report(item.GetType().Name, item.Text, language, problems);
                if (item is ToolStripDropDownItem drop) WalkItems(drop.DropDownItems, language, problems, seen);
            }
        }

        private static void Report(string kind, string? text, string language, List<string> problems)
        {
            foreach (char c in text ?? "")
                if (c >= 'Ѐ' && c <= 'ӿ')
                {
                    problems.Add("[" + language + "] " + kind + ": \"" + text + "\"");
                    break;
                }
        }

        /// <summary>
        /// The list caption follows the note and is never written into it - the model rule, proven
        /// on the surface that would break it. A window that stored its own preview would show the
        /// same thing while quietly corrupting the note.
        /// </summary>
        [Fact]
        public void TheListCaptionIsDerivedAndTheNoteKeepsNoTitle()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "English" };
                using QuickNotesService service = Service("caption");
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "TODO: check the race";
                service.Commit(note);

                using var window = new QuickNotesWindow(service, config, () => { });
                ListView list = (ListView)typeof(QuickNotesWindow)
                    .GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(window)!;

                Assert.Equal("TODO: check the race", Assert.Single(list.Items.Cast<ListViewItem>()).Text);
                Assert.Null(note.Title);
            });
        }

        /// <summary>
        /// A file name built from an unnamed note's first line, with everything Windows refuses
        /// taken out - the export dialog offers it, so a stray slash would make the save fail.
        /// </summary>
        [Fact]
        public void TheSuggestedFileNameHasNothingWindowsRefusesInIt()
        {
            var note = new QuickNote { Kind = QuickNoteKind.Text, RawText = "src/CyrFlip: fix the *race*?" };

            string name = QuickNotesWindow.SafeFileName(note);

            Assert.EndsWith(".txt", name);
            foreach (char bad in Path.GetInvalidFileNameChars())
                Assert.DoesNotContain(bad.ToString(), name);
        }

        /// <summary>
        /// The window may not open smaller than its own content in any language. The minimum used to
        /// be a hard-coded 640x420 measured against nothing, which clipped the bottom row of buttons
        /// wherever the captions are longer than the Russian they were eyeballed against - and, since
        /// a stored size is only ever grown to the minimum, the first open was that clipped window.
        /// </summary>
        [Fact]
        public void TheWindowIsNeverSmallerThanItsOwnContentInAnyLanguage()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    // The stored size a fresh install carries - the minimum has to win over it.
                    var config = new AppConfig { UiLanguage = language };
                    using QuickNotesService service = Service("min" + i);
                    using var window = new QuickNotesWindow(service, config, () => { }, exchange: (_, _) => { });

                    var bottom = (FlowLayoutPanel)typeof(QuickNotesWindow)
                        .GetField("_bottomRow", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(window)!;

                    if (window.MinimumSize.Width < bottom.PreferredSize.Width)
                        problems.Add("[" + language + "] minimum " + window.MinimumSize.Width
                            + " px is narrower than the button row's " + bottom.PreferredSize.Width);
                    if (window.Width < window.MinimumSize.Width || window.Height < window.MinimumSize.Height)
                        problems.Add("[" + language + "] opens at " + window.Size + ", below its own minimum "
                            + window.MinimumSize);
                }
            });

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void AnEmptyNoteStillYieldsAUsableFileName()
            => Assert.Equal("note.txt", QuickNotesWindow.SafeFileName(new QuickNote()));

        // ---- Ticket S0006 ----

        private static string JournalOf(QuickNotesService service) => service.JournalPath;

        /// <summary>
        /// QN-1: the note the window was showing when "delete all" ran used to be written back by
        /// the window's next commit - closing it, the chord, switching the feature off.
        /// </summary>
        [Fact]
        public void AfterDeleteAllTheWindowCommitsNothingAndForgetsTheRememberedNote()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("cleared");
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "private fragment";
                service.Commit(note);
                var config = new AppConfig { UiLanguage = "English", QuickNotesSelected = note.Id.ToString("D") };
                int persisted = 0;
                using var window = new QuickNotesWindow(service, config, () => { }, () => persisted++);

                service.DeleteAll();
                window.CommitCurrent();

                Assert.False(File.Exists(JournalOf(service)), "the deleted note was written back");
                Assert.Equal("", config.QuickNotesSelected);
                Assert.True(persisted > 0, "the cleared selection was never persisted");
            });
        }

        /// <summary>QN-3: a note that was only looked at is neither rewritten nor re-dated.</summary>
        [Fact]
        public void CommittingANoteThatWasOnlyLookedAtWritesNothing()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("untouched");
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "line one\nline two";   // bare LF: the editor displays it as CRLF
                note.Title = "named";
                service.Commit(note);
                DateTime modified = note.UpdatedAtUtc;
                string journal = File.ReadAllText(JournalOf(service));
                var config = new AppConfig { UiLanguage = "English", QuickNotesSelected = note.Id.ToString("D") };
                using var window = new QuickNotesWindow(service, config, () => { }, () => { });

                window.CommitCurrent();
                window.CommitCurrent();

                Assert.Equal(journal, File.ReadAllText(JournalOf(service)));
                Assert.Equal(modified, note.UpdatedAtUtc);
                Assert.Equal("line one\nline two", note.RawText);
            });
        }

        /// <summary>QN-9: the open note is remembered when it is opened, not only when the window moves.</summary>
        [Fact]
        public void OpeningANoteRemembersItOnceWithoutAMoveOrResize()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("remember");
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "x";
                service.Commit(note);
                var config = new AppConfig { UiLanguage = "English" };
                var saved = new List<string>();
                using var window = new QuickNotesWindow(service, config, () => { }, () => saved.Add(config.QuickNotesSelected));

                typeof(QuickNotesWindow).GetMethod("LoadNote", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, new object[] { note });
                Assert.Equal(new[] { note.Id.ToString("D") }, saved);

                // Opening it again is not a change, and costs no registry write.
                typeof(QuickNotesWindow).GetMethod("LoadNote", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, new object[] { note });
                Assert.Single(saved);
            });
        }

        /// <summary>QN-10: switching from an Indic UI back to a Latin one gives the default face back.</summary>
        [Fact]
        public void SwitchingBackFromHindiRestoresTheDefaultFont()
        {
            OnUiThread(() =>
            {
                string hindi = Localization.Names[Array.IndexOf(Localization.Codes, "hi")];
                string english = Localization.Names[Array.IndexOf(Localization.Codes, "en")];
                var config = new AppConfig { UiLanguage = hindi };
                using QuickNotesService service = Service("font");
                using var window = new QuickNotesWindow(service, config, () => { }, () => { });
                Assert.Equal(Localization.FontFamily(hindi), window.Font.FontFamily.Name);

                window.ApplyLanguage(english);

                Assert.Equal(System.Drawing.SystemFonts.MessageBoxFont.FontFamily.Name, window.Font.FontFamily.Name);
            });
        }
    }
}
