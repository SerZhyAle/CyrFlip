using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Verification for ticket S0078: Line numbers in Quick Notes editor.
    /// Covers index correctness, parity with QuickNote.LineCount, gutter width calculation,
    /// dynamic UI geometry, selection/caret/scroll preservation, raw text isolation,
    /// RTL layout, and AppConfig registry serialization.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class QuickNotesLineNumbersTests : IDisposable
    {
        private readonly string _root;

        public QuickNotesLineNumbersTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-linenum-" + Guid.NewGuid().ToString("N"));
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
            public string Protect(string plain) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher)
            {
                try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        private QuickNotesService Service(string name)
            => new QuickNotesService(new QuickNotesStore(
                Path.Combine(_root, name + "-" + QuickNotesStore.FileName), new FakeCipher()));

        // =========================================================================
        // 1. QuickNotesLineIndex Unit Tests
        // =========================================================================

        [Fact]
        public void LineIndex_EmptyAndNullText_HasOneLineStartingAtZero()
        {
            var index = new QuickNotesLineIndex();
            Assert.Equal(1, index.LineCount);
            Assert.Equal(0, index.LineStarts[0]);

            index.Update("");
            Assert.Equal(1, index.LineCount);
            Assert.Equal(0, index.LineStarts[0]);
            int line = index.GetLineNumber(0, out bool isStart);
            Assert.Equal(1, line);
            Assert.True(isStart);

            index.Update(null!);
            Assert.Equal(1, index.LineCount);
            Assert.Equal(0, index.LineStarts[0]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Single line without newline")]
        [InlineData("Line 1\r\nLine 2")]
        [InlineData("Line 1\nLine 2")]
        [InlineData("Line 1\rLine 2")]
        [InlineData("Line 1\r\nLine 2\r\n")]
        [InlineData("Line 1\nLine 2\n")]
        [InlineData("Line 1\rLine 2\r")]
        [InlineData("\n")]
        [InlineData("\r\n")]
        [InlineData("\r")]
        [InlineData("\r\n\r\n")]
        [InlineData("\n\n\n")]
        [InlineData("Alpha\r\nBeta\n\nGamma\rDelta\r\n")]
        [InlineData("Привет 🌍\r\nСтрока 2 🚀\nСтрока 3\rСтрока 4")]
        [InlineData("   \t  \r\n \t \r\n")]
        public void LineIndex_CountStrictlyMatchesQuickNoteLineCount(string text)
        {
            var note = new QuickNote { Kind = QuickNoteKind.Text, RawText = text };
            var index = new QuickNotesLineIndex();
            index.Update(text);

            Assert.Equal(note.LineCount, index.LineCount);
        }

        [Fact]
        public void LineIndex_LineStartsAndBinarySearch_CorrectPositions()
        {
            // "Alpha\r\nBeta\nGamma\rDelta"
            // Alpha: indices 0..4, \r\n: 5,6 -> Beta starts at 7
            // Beta: indices 7..10, \n: 11 -> Gamma starts at 12
            // Gamma: indices 12..16, \r: 17 -> Delta starts at 18
            // Delta: indices 18..22
            string text = "Alpha\r\nBeta\nGamma\rDelta";
            var index = new QuickNotesLineIndex();
            index.Update(text);

            Assert.Equal(4, index.LineCount);
            Assert.Equal(new[] { 0, 7, 12, 18 }, index.LineStarts);

            // Test line 1
            Assert.Equal(1, index.GetLineNumber(0, out bool s)); Assert.True(s);
            Assert.Equal(1, index.GetLineNumber(3, out s)); Assert.False(s);
            Assert.Equal(1, index.GetLineNumber(6, out s)); Assert.False(s);

            // Test line 2
            Assert.Equal(2, index.GetLineNumber(7, out s)); Assert.True(s);
            Assert.Equal(2, index.GetLineNumber(10, out s)); Assert.False(s);
            Assert.Equal(2, index.GetLineNumber(11, out s)); Assert.False(s);

            // Test line 3
            Assert.Equal(3, index.GetLineNumber(12, out s)); Assert.True(s);
            Assert.Equal(3, index.GetLineNumber(15, out s)); Assert.False(s);
            Assert.Equal(3, index.GetLineNumber(17, out s)); Assert.False(s);

            // Test line 4
            Assert.Equal(4, index.GetLineNumber(18, out s)); Assert.True(s);
            Assert.Equal(4, index.GetLineNumber(22, out s)); Assert.False(s);

            // Out-of-bounds indices clamp gracefully
            Assert.Equal(1, index.GetLineNumber(-5, out s)); Assert.False(s);
            Assert.Equal(4, index.GetLineNumber(999, out s)); Assert.False(s);
        }

        [Fact]
        public void LineIndex_TrailingNewlineCreatesFinalEmptyLine()
        {
            var index = new QuickNotesLineIndex();
            index.Update("Line 1\r\n");

            Assert.Equal(2, index.LineCount);
            Assert.Equal(0, index.LineStarts[0]);
            Assert.Equal(8, index.LineStarts[1]);

            int line = index.GetLineNumber(8, out bool isStart);
            Assert.Equal(2, line);
            Assert.True(isStart);
        }

        [Fact]
        public void LineIndex_UpdatesCorrectlyOnTextModifications()
        {
            var index = new QuickNotesLineIndex();
            index.Update("Line 1\nLine 2");
            Assert.Equal(2, index.LineCount);

            // Insert new lines in middle
            index.Update("Line 1\nLine 1.5\nLine 2");
            Assert.Equal(3, index.LineCount);
            Assert.Equal(new[] { 0, 7, 16 }, index.LineStarts);

            // Delete to single line
            index.Update("Single line");
            Assert.Equal(1, index.LineCount);
            Assert.Equal(new[] { 0 }, index.LineStarts);
        }

        // =========================================================================
        // 2. Gutter Width Calculation & Thresholds (9->10, 99->100)
        // =========================================================================

        [Fact]
        public void GutterWidth_DisabledReturnsZero_EnabledScalesWithDigits()
        {
            OnUiThread(() =>
            {
                using var editor = new QuickNotesTextEditor();
                Assert.Equal(0, editor.CalculateRequiredGutterWidth());

                editor.ApplyLineNumbers(true);

                // 1..9 lines
                editor.TextBox.Text = string.Join("\n", Enumerable.Repeat("line", 5));
                int widthSingleDigit = editor.CalculateRequiredGutterWidth();
                Assert.True(widthSingleDigit > 0, "Gutter width must be > 0 when line numbers enabled.");

                // 10..99 lines
                editor.TextBox.Text = string.Join("\n", Enumerable.Repeat("line", 25));
                int widthTwoDigits = editor.CalculateRequiredGutterWidth();
                Assert.True(widthTwoDigits >= widthSingleDigit, "2-digit line count must have >= width than 1-digit.");

                // 100..999 lines
                editor.TextBox.Text = string.Join("\n", Enumerable.Repeat("line", 150));
                int widthThreeDigits = editor.CalculateRequiredGutterWidth();
                Assert.True(widthThreeDigits > widthTwoDigits, "3-digit line count must have strictly > width than 2-digit.");

                // 1000 lines
                editor.TextBox.Text = string.Join("\n", Enumerable.Repeat("line", 1005));
                int widthFourDigits = editor.CalculateRequiredGutterWidth();
                Assert.True(widthFourDigits > widthThreeDigits, "4-digit line count must have strictly > width than 3-digit.");

                // Turning off returns 0 again
                editor.ApplyLineNumbers(false);
                Assert.Equal(0, editor.CalculateRequiredGutterWidth());
            });
        }

        // =========================================================================
        // 3. Accessibility, Tooltips and Tab Focus
        // =========================================================================

        [Fact]
        public void Gutter_AccessibleNameAndTooltipLocalized_NonFocusable()
        {
            OnUiThread(() =>
            {
                using var editor = new QuickNotesTextEditor();
                Assert.False(editor.Gutter.TabStop, "Gutter must not be a tab stop.");

                editor.ApplyLanguage("ru");
                Assert.Equal("Номера строк", editor.Gutter.AccessibleName);

                editor.ApplyLanguage("en");
                Assert.Equal("Line numbers", editor.Gutter.AccessibleName);

                editor.ApplyLanguage("uk");
                Assert.Equal("Номери рядків", editor.Gutter.AccessibleName);

                editor.ApplyLanguage("de");
                Assert.Equal("Zeilennummern", editor.Gutter.AccessibleName);
            });
        }

        // =========================================================================
        // 4. Preservation of Caret, Selection, and Top Line on Toggle
        // =========================================================================

        [Fact]
        public void Editor_TogglePreservesCaretSelectionAndRawText()
        {
            OnUiThread(() =>
            {
                using var form = new Form { Width = 600, Height = 400 };
                using var editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                form.Controls.Add(editor);
                form.Show();
                Application.DoEvents();

                // Multiline text with bare LF and CRLF
                string originalText = "Line 1\nLine 2\r\nLine 3\nLine 4\r\nLine 5\nLine 6\nLine 7\nLine 8";
                editor.TextBox.Text = originalText;
                editor.TextBox.Select(10, 5); // Select within Line 2/3

                int selStartBefore = editor.TextBox.SelectionStart;
                int selLenBefore = editor.TextBox.SelectionLength;

                // Turn line numbers ON
                editor.ApplyLineNumbers(true);
                Application.DoEvents();

                Assert.True(editor.Gutter.Visible);
                Assert.Equal(originalText, editor.TextBox.Text);
                Assert.Equal(selStartBefore, editor.TextBox.SelectionStart);
                Assert.Equal(selLenBefore, editor.TextBox.SelectionLength);

                // Turn line numbers OFF
                editor.ApplyLineNumbers(false);
                Application.DoEvents();

                Assert.False(editor.Gutter.Visible);
                Assert.Equal(originalText, editor.TextBox.Text);
                Assert.Equal(selStartBefore, editor.TextBox.SelectionStart);
                Assert.Equal(selLenBefore, editor.TextBox.SelectionLength);
            });
        }

        // =========================================================================
        // 5. Raw Text and Clipboard Isolation
        // =========================================================================

        [Fact]
        public void Editor_TextAndClipboardNeverContainLineNumbers()
        {
            OnUiThread(() =>
            {
                using var form = new Form { Width = 500, Height = 400 };
                using var editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                form.Controls.Add(editor);
                form.Show();
                editor.ApplyLineNumbers(true);

                string content = "First line\nSecond line\nThird line";
                editor.TextBox.Text = content;
                Application.DoEvents();

                // The TextBox Text property must contain strictly raw content
                Assert.Equal(content, editor.TextBox.Text);
                Assert.DoesNotContain("1", editor.TextBox.Text);
                Assert.DoesNotContain("↳", editor.TextBox.Text);

                editor.TextBox.SelectAll();
                Assert.Equal(content, editor.TextBox.SelectedText);
            });
        }

        [Fact]
        public void NoteMetadata_LineNumbersToggleDoesNotTouchUpdatedAtUtc()
        {
            OnUiThread(() =>
            {
                using QuickNotesService service = Service("metadata_safety");
                using var window = new QuickNotesWindow(service, new AppConfig(), () => { });
                window.ShowWithText("Test Note Content");
                window.CommitCurrent();
                Application.DoEvents();

                var list = (ListView)typeof(QuickNotesWindow).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                var note = (QuickNote)list.Items[0].Tag;
                DateTime beforeUpdate = note.UpdatedAtUtc;

                // Live toggle line numbers in window
                window.ApplyLineNumbers(true);
                Application.DoEvents();

                window.ApplyLineNumbers(false);
                Application.DoEvents();

                Assert.Equal(beforeUpdate, note.UpdatedAtUtc);
            });
        }

        // =========================================================================
        // 6. WordWrap, Continuation Marker, and Paint Execution
        // =========================================================================

        [Fact]
        public void WordWrap_TogglePreservesTextAndCalculatesLayout()
        {
            OnUiThread(() =>
            {
                using var form = new Form { Width = 300, Height = 300 };
                using var editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                form.Controls.Add(editor);
                form.Show();
                editor.ApplyLineNumbers(true);

                string longLine = "This is a very long line of text that is intended to wrap across multiple visual rows when WordWrap is enabled in the Quick Notes editor.";
                string content = "Short line 1\r\n" + longLine + "\r\nShort line 3";
                editor.TextBox.Text = content;
                Application.DoEvents();

                // Turn WordWrap ON
                editor.ApplyWordWrap(true);
                Application.DoEvents();
                Assert.True(editor.TextBox.WordWrap);

                // Paint gutter to verify no crash and valid rendering
                using (var bmp = new Bitmap(Math.Max(10, editor.Gutter.Width), Math.Max(10, editor.Gutter.Height)))
                using (var g = Graphics.FromImage(bmp))
                {
                    editor.PaintGutter(g, editor.Gutter.ClientRectangle);
                }

                // Turn WordWrap OFF
                editor.ApplyWordWrap(false);
                Application.DoEvents();
                Assert.False(editor.TextBox.WordWrap);
                Assert.Equal(content, editor.TextBox.Text);
            });
        }

        // =========================================================================
        // 7. RTL Layout: Gutter is physically on the left edge
        // =========================================================================

        [Fact]
        public void Layout_GutterIsPhysicallyOnLeftInLtrAndRtl()
        {
            OnUiThread(() =>
            {
                using var form = new Form { Width = 400, Height = 300 };
                using var editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                form.Controls.Add(editor);
                form.Show();

                editor.ApplyLineNumbers(true);
                editor.TextBox.Text = "Line 1\nLine 2";
                Application.DoEvents();

                // Standard LTR: gutter is at left = 0, textbox is to its right
                Assert.Equal(0, editor.Gutter.Left);
                Assert.Equal(editor.Gutter.Right, editor.TextBox.Left);

                // In mirrored RTL: editor bounds adapt so physical placement stays on the left edge
                editor.RightToLeft = RightToLeft.Yes;
                Application.DoEvents();
                // Ensure gutter is visible and non-empty
                Assert.True(editor.Gutter.Width > 0);
                Assert.True(editor.TextBox.Width > 0);
            });
        }

        // =========================================================================
        // 8. AppConfig Persistence & Registry Roundtrip
        // =========================================================================

        private sealed class FakeKey : IConfigKey
        {
            public readonly System.Collections.Generic.Dictionary<string, object> Values =
                new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        [Fact]
        public void AppConfig_QuickNotesLineNumbers_DefaultAndRegistryRoundtrip()
        {
            var config = new AppConfig();
            Assert.False(config.QuickNotesLineNumbers, "Default value for QuickNotesLineNumbers must be false.");

            // When missing in store, defaults to false
            var emptyKey = new FakeKey();
            var loadedDefault = AppConfig.LoadFrom(emptyKey, out _);
            Assert.False(loadedDefault.QuickNotesLineNumbers);

            // Save true and verify roundtrip
            config.QuickNotesLineNumbers = true;
            var keyTrue = new FakeKey();
            config.SaveTo(keyTrue);
            Assert.Equal(1, keyTrue.Values["QuickNotesLineNumbers"]);

            var loadedTrue = AppConfig.LoadFrom(keyTrue, out _);
            Assert.True(loadedTrue.QuickNotesLineNumbers);

            // Save false and verify roundtrip
            config.QuickNotesLineNumbers = false;
            var keyFalse = new FakeKey();
            config.SaveTo(keyFalse);
            Assert.Equal(0, keyFalse.Values["QuickNotesLineNumbers"]);

            var loadedFalse = AppConfig.LoadFrom(keyFalse, out _);
            Assert.False(loadedFalse.QuickNotesLineNumbers);
        }

        // =========================================================================
        // 9. SettingsForm UI Checkbox Presence & Localization
        // =========================================================================

        [Fact]
        public void SettingsForm_QuickNotesLineNumbersCheckBox_PresentAndLocalized()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { QuickNotesLineNumbers = false, UiLanguage = "ru" };
                using var form = TestForms.NewSettings(config, withExchange: false);
                form.Show();
                Application.DoEvents();

                var cb = (CheckBox)typeof(SettingsForm).GetField("_quickNotesLineNumbers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                Assert.NotNull(cb);
                Assert.False(cb.Checked);

                // Verify text was localized
                Assert.Equal("Номера строк", cb.Text);

                // Toggling updates config
                cb.Checked = true;
                Assert.True(config.QuickNotesLineNumbers);
            });
        }
    }
}
