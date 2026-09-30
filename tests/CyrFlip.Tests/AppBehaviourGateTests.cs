using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Gate tests enforcing APP-BEHAVIOUR contract rules across the codebase:
    /// - Rule 6: No raw ex.Message reaches user-facing dialogs.
    /// - Rule 7: Localized formatting never throws on broken format strings.
    /// - Rule 8: Right-to-left direction applied consistently for Arabic and Urdu.
    /// - Rule 9: Glyph-only controls carry accessible names.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class AppBehaviourGateTests
    {
        private static readonly string SourceFolder = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private static readonly HashSet<string> DiagnosticExceptionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CaretDiagnostics.cs",
            "Ia2Caret.cs",
            "UiaCaretCom.cs",
        };

        private static readonly Regex ExceptionMessageRegex = new Regex(@"\bex(ception)?\.Message\b", RegexOptions.Compiled);
        private static readonly Regex AllowedLogLineRegex = new Regex(@"^\s*(///?|\*|Log\(|LauncherLog\.Log|TranslateLog\.Log|TextMenuLog\.Log|QuickNotesLog\.Log|DiagnosticLog\.Log)", RegexOptions.Compiled);

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
            if (failure != null) throw new Exception("UI thread execution failed: " + failure.Message, failure);
        }

        [Fact]
        public void NoExceptionTextReachesTheUser()
        {
            var violations = ScanForExceptionMessageViolations(SourceFolder);
            Assert.True(violations.Count == 0,
                "Raw exception text reaching user or unlogged outside diagnostic sinks (APP-BEHAVIOUR rule 6):\n"
                + string.Join("\n", violations));
        }

        [Fact]
        public void NoExceptionTextGateFailsOnSyntheticViolation()
        {
            string[] testLines = new[]
            {
                "MessageBox.Show(\"Error: \" + ex.Message);",
                "LauncherLog.Log(\"Failed: \" + ex.Message);", // allowed
            };

            var violations = ScanLinesForViolations("FakeFile.cs", testLines);
            Assert.Single(violations);
            Assert.Contains("FakeFile.cs:1", violations[0]);
        }

        private static List<string> ScanForExceptionMessageViolations(string rootDir)
        {
            var violations = new List<string>();
            foreach (string file in Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);
                if (DiagnosticExceptionFiles.Contains(fileName)) continue;

                string[] lines = File.ReadAllLines(file);
                violations.AddRange(ScanLinesForViolations(fileName, lines));
            }
            return violations;
        }

        private static List<string> ScanLinesForViolations(string fileName, string[] lines)
        {
            var violations = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!ExceptionMessageRegex.IsMatch(line)) continue;
                if (AllowedLogLineRegex.IsMatch(line)) continue;

                violations.Add($"{fileName}:{i + 1}: {line.Trim()}");
            }
            return violations;
        }

        [Fact]
        public void LocalizationFormatSafelyHandlesBrokenTemplates()
        {
            // Normal formatting
            Assert.Equal("Hello World", Localization.Format("English", "Hello {0}", "World"));

            // Broken templates must never throw
            Assert.Equal("Unclosed {0 bracket", Localization.Format("English", "Unclosed {0 bracket", "arg"));
            Assert.Equal("Index out of bounds {99}", Localization.Format("English", "Index out of bounds {99}", "arg"));
            Assert.Equal("Broken {notanumber}", Localization.Format("English", "Broken {notanumber}", "arg"));
            Assert.Equal("", Localization.Format("English", null!, "arg"));
        }

        [Fact]
        public void DirectionIsDeclaredExclusivelyForArabicAndUrdu()
        {
            var rtlLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ar", "ur", "Арабский", "Урду", "العربية", "اردو" };

            for (int i = 0; i < Localization.Names.Length; i++)
            {
                string name = Localization.Names[i];
                string code = Localization.Codes[i];
                bool expectedRtl = rtlLanguages.Contains(name) || rtlLanguages.Contains(code);
                Assert.Equal(expectedRtl, Localization.IsRightToLeft(name));
                Assert.Equal(expectedRtl, Localization.IsRightToLeft(code));
            }
        }

        [Fact]
        public void WindowsSetRightToLeftInArabic()
        {
            OnUiThread(() =>
            {
                using var settings = TestForms.NewSettings(new AppConfig { UiLanguage = "ar" });
                Assert.Equal(RightToLeft.Yes, settings.RightToLeft);

                using var hotkey = new HotkeyDialog("Ctrl+Shift+F12", "T", "ar");
                Assert.Equal(RightToLeft.Yes, hotkey.RightToLeft);

                using var layoutPicker = new LayoutPickerDialog(new string[0], "ar");
                Assert.Equal(RightToLeft.Yes, layoutPicker.RightToLeft);

                using var translate = new TranslationDialog(null, "ar");
                Assert.Equal(RightToLeft.Yes, translate.RightToLeft);

                using var historySearch = new ClipboardHistorySearchWindow(new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(Path.GetTempPath(), "cyrflip-test-" + Guid.NewGuid().ToString("N"))), "ar");
                Assert.Equal(RightToLeft.Yes, historySearch.RightToLeft);
            });
        }

        [Fact]
        public void GlyphOnlyButtonsCarryAccessibleNamesAcrossAllLanguages()
        {
            var problems = new List<string>();
            int inspected = 0;

            OnUiThread(() =>
            {
                foreach (string lang in Localization.Names)
                {
                    using var settings = TestForms.NewSettings(new AppConfig { UiLanguage = lang });
                    inspected += InspectGlyphButtons(settings, lang, "SettingsForm", problems);
                }
            });

            Assert.True(problems.Count == 0, "Glyph buttons missing AccessibleName (APP-BEHAVIOUR rule 9):\n" + string.Join("\n", problems));
            Assert.True(inspected > 0, "No glyph buttons were inspected.");
        }

        [Fact]
        public void GlyphButtonInspectionCatchesMissingAccessibleName()
        {
            var problems = new List<string>();
            using var dummyForm = new Form();
            var button = new Button { Text = "↑", AccessibleName = "" };
            dummyForm.Controls.Add(button);

            InspectGlyphButtons(dummyForm, "English", "DummyForm", problems);
            Assert.Single(problems);
            Assert.Contains("DummyForm", problems[0]);
        }

        private static int InspectGlyphButtons(Control parent, string language, string formName, List<string> problems)
        {
            int inspected = 0;
            foreach (Control control in parent.Controls)
            {
                if (control is Button btn)
                {
                    string text = btn.Text?.Trim() ?? string.Empty;
                    // Button has only glyphs/symbols and no letters or digits
                    bool isGlyphOnly = text.Length > 0 && !text.Any(char.IsLetterOrDigit);
                    if (isGlyphOnly)
                    {
                        inspected++;
                        if (string.IsNullOrWhiteSpace(btn.AccessibleName))
                            problems.Add($"{formName}[{language}]: button '{text}' has no AccessibleName");
                    }
                }
                inspected += InspectGlyphButtons(control, language, formName, problems);
            }
            return inspected;
        }
    }
}
