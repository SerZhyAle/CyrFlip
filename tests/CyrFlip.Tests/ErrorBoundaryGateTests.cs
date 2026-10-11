using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0061 (ER-1) - the ratchet behind the catch census. On 2026-10-11 the product held 446 `catch`
    /// sites, 209 of them with an empty or comment-only body (PLAN/S0061.../research/ER-1_census_*.csv
    /// classifies every one: handled by design, nothing lost, or a user action lost without a word).
    /// This gate does not ask for any of them to go; it asks that **no new one arrives unreviewed**.
    ///
    /// A file may not hold more silent catches than its reviewed count below. A new swallow is either
    /// logged / surfaced (preferred - see FailureCause and the DiagnosticLog wrappers), or its reason is
    /// written next to it AND the file's count here is raised in the same change, which is the review.
    /// Lowering a count after removing a swallow is the same one-line edit.
    /// </summary>
    public class ErrorBoundaryGateTests
    {
        private static readonly string Src = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        // catch [ (type [name]) ] [ when (...) ] { <whitespace and comments only> }
        private static readonly Regex Silent = new Regex(
            @"\bcatch\b\s*(\([^)]*\))?\s*(when\s*\([^)]*\))?\s*\{(\s|//[^\r\n]*|/\*.*?\*/)*\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>The number of silent catch clauses in a piece of C# source.</summary>
        internal static int CountSilent(string source) => Silent.Matches(source).Count;

        // Reviewed 2026-10-11 (S0061 ER-1); +6 raised by S0066 L-4/L-7/L-8 (a watchdog timer
        // callback, a kill-loop tail, an exit-time courtesy and erase progress guards - each with
        // its reason written beside it). File name -> silent catches.
        private static readonly Dictionary<string, int> Reviewed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "AppConfig.cs", 18 },
            { "AppShortcuts.cs", 2 },
            { "BusyDialog.cs", 3 },
            { "ButtonGlyphs.cs", 1 },
            { "CaretDiagnostics.cs", 2 },
            { "CaretOverlay.cs", 6 },
            { "ChordMatcher.cs", 1 },
            { "ClipboardFlipLog.cs", 1 },
            { "ClipboardHistoryJournal.cs", 7 },
            { "ClipboardHistoryLog.cs", 1 },
            { "ClipboardHistorySearchWindow.cs", 3 },
            { "ClipboardHistoryService.cs", 4 },
            { "ClipboardHistoryWindow.cs", 1 },
            { "ClipboardOwner.cs", 2 },
            { "CollapsibleGroup.cs", 1 },
            { "ConfirmDialog.cs", 2 },
            { "CyrFlipContext.cs", 18 },
            { "CyrFlipExchangeWriter.cs", 1 },
            { "DataFolderMigration.cs", 1 },
            { "DiagnosticLog.cs", 2 },
            { "DiagnosticRedactor.cs", 1 },
            { "EditorCaretSignal.cs", 1 },
            { "ExchangeExportDialog.cs", 1 },
            { "FaultContainment.cs", 2 },
            { "HotkeyDialog.cs", 1 },
            { "Ia2Caret.cs", 3 },
            { "InputLayouts.cs", 6 },
            { "KeyboardHook.cs", 1 },
            { "LanguageHotkeys.cs", 4 },
            { "LauncherBrand.cs", 3 },
            { "LauncherExecution.cs", 3 },
            { "LauncherIconResolver.cs", 1 },
            { "LauncherIpc.cs", 5 },
            { "LauncherJumpList.cs", 2 },
            { "LauncherScenarioDialog.cs", 1 },
            { "LauncherScenarioStore.cs", 3 },
            { "LauncherShortcutIcons.cs", 1 },
            { "LaunchTarget.cs", 2 },
            { "LayoutConversionDialog.cs", 1 },
            { "LayoutIdentity.cs", 1 },
            { "LayoutPickerDialog.cs", 1 },
            { "LayoutPublisher.cs", 2 },
            { "MailSender.cs", 1 },
            { "MarkerSize.cs", 3 },
            { "MouseHook.cs", 1 },
            { "OllamaClient.cs", 4 },
            { "OllamaManager.cs", 7 },
            // S0065: +1 the crash.log line of the unobserved-task observer, +1 the fatal box's
            // language fallback - both comment-only by the same reason as Fatal's own catches.
            { "Program.cs", 4 },
            { "QuickNotesService.cs", 3 },
            { "QuickNotesStore.cs", 4 },
            { "QuickNotesWindow.cs", 7 },
            { "ScreenCapture.cs", 1 },
            { "ScreenshotFolderOpener.cs", 1 },
            { "ScreenshotLog.cs", 1 },
            { "ScreenshotSaver.cs", 2 },
            { "SelectionProbe.cs", 4 },
            { "SessionEndWatcher.cs", 7 },
            { "SettingsForm.cs", 13 },
            { "SupportBundle.cs", 2 },
            { "SupportBundleDialog.cs", 2 },
            { "ThemeApply.cs", 1 },
            { "ThemeManager.cs", 1 },
            { "ThemeWin32.cs", 9 },
            { "TlsPolicy.cs", 1 },
            { "TranslationDialog.cs", 1 },
            { "TranslationProfile.cs", 1 },
            { "TranslationResultWindow.cs", 1 },
            { "TranslationService.cs", 2 },
            { "TrayLog.cs", 1 },
            { "UiaCaretCom.cs", 1 },
            // S0067 follow-up: +1 LoadAppIcon - a failed read of the embedded app-ico resource is
            // decoration lost, the tour reads fine without the mark (the same best-effort as
            // LauncherBrand's three).
            { "WelcomeDialog.cs", 2 },
            { "YtDlpLinkDialog.cs", 1 },
        };

        [Fact]
        public void NoFileGainsASilentCatchWithoutReview()
        {
            var problems = new List<string>();
            foreach (string file in Directory.GetFiles(Src, "*.cs"))
            {
                string name = Path.GetFileName(file);
                int actual = CountSilent(File.ReadAllText(file));
                int reviewed = Reviewed.TryGetValue(name, out int n) ? n : 0;
                if (actual > reviewed)
                    problems.Add(name + ": " + actual + " silent catch bodies, " + reviewed + " reviewed");
            }
            Assert.True(problems.Count == 0,
                "A catch with an empty or comment-only body was added. Log it or tell the user (FailureCause, the DiagnosticLog wrappers), "
                + "or state why it is right and raise the file's reviewed count in ErrorBoundaryGateTests:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// The guard fact (a gate that cannot fail is a finding): the scanner counts what it claims to,
        /// including the shapes the census met - an empty body, a comment-only body, a typed catch, a
        /// filtered one - and does not count a body that does something.
        /// </summary>
        [Theory]
        [InlineData("try { Run(); } catch { }", 1)]
        [InlineData("try { Run(); } catch (Exception) { /* best effort */ }", 1)]
        [InlineData("try { Run(); } catch (IOException ex) { // the file is gone" + "\n" + "}", 1)]
        [InlineData("try { Run(); } catch (IOException ex) when (ex.HResult == 32) { }", 1)]
        [InlineData("try { Run(); } catch { Log(); }", 0)]
        [InlineData("try { Run(); } catch (Exception ex) { return false; }", 0)]
        [InlineData("try { A(); } catch { } try { B(); } catch (Exception) { }", 2)]
        public void TheScannerCountsSilentCatches(string source, int expected)
        {
            Assert.Equal(expected, CountSilent(source));
        }

        /// <summary>
        /// The reviewed table itself must not drift into a file that no longer exists or a count the tree
        /// does not reach any more - a stale entry is headroom for the next unreviewed swallow.
        /// </summary>
        [Fact]
        public void TheReviewedTableHasNoHeadroomLeft()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, int> pair in Reviewed)
            {
                string path = Path.Combine(Src, pair.Key);
                int actual = File.Exists(path) ? CountSilent(File.ReadAllText(path)) : 0;
                if (actual < pair.Value) stale.Add(pair.Key + ": reviewed " + pair.Value + ", now " + actual);
            }
            Assert.True(stale.Count == 0,
                "Silent catches were removed - lower these counts so the headroom is not reused:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
        }
    }
}
