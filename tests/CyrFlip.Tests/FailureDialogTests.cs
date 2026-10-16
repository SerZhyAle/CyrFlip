using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// APP-BEHAVIOUR rule 6, "a failure is a set of actions" (ticket S0060 P7-g): the failure presentation of
    /// <see cref="ConfirmDialog"/> offers Retry (only when there is something to retry), the folder of the logs
    /// and Close; Escape is Close; and the settings window uses it at its failure sites - a cause named from the
    /// exception's type, never the warning box that only said OK.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class FailureDialogTests
    {
        [Fact]
        public void AFailureWithSomethingToRetryOffersRetryTheLogsAndClose()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using var dialog = new ConfirmDialog("English", "Could not save.", canRetry: true, owned: true);
                string[] words = dialog.Buttons.Select(b => b.Text).ToArray();
                Assert.Equal(new[] { "Retry", "Open log folder", "Close" }, words);
                Assert.Equal(DialogResult.Retry, dialog.Buttons[0].DialogResult);
                Assert.Equal(DialogResult.None, dialog.Buttons[1].DialogResult);   // opens the folder, the box stays
                Assert.Equal(DialogResult.Cancel, dialog.Buttons[2].DialogResult);
                Assert.Same(dialog.Buttons[0], dialog.AcceptButton);
                Assert.Same(dialog.Buttons[2], dialog.CancelButton);   // Escape and the close box are Close
            });
        }

        [Fact]
        public void AFailureWithNothingToRetryOffersNoRetry()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using var dialog = new ConfirmDialog("English", "Could not read.", canRetry: false, owned: true);
                Assert.Equal(new[] { "Open log folder", "Close" }, dialog.Buttons.Select(b => b.Text).ToArray());
                Assert.Same(dialog.Buttons[1], dialog.AcceptButton);
                Assert.Same(dialog.Buttons[1], dialog.CancelButton);
            });
        }

        [UiSweepFact]
        public void TheCaptionsFollowTheLanguage()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    using var dialog = new ConfirmDialog(language, "x", canRetry: true, owned: true);
                    foreach (Button button in dialog.Buttons)
                        Assert.False(button.Text.Any(c => c >= 'Ѐ' && c <= 'ӿ') && language != "Русский" && language != "Українська",
                            $"[{language}] \"{button.Text}\" is Cyrillic in a language that is not");
                }
                using var german = new ConfirmDialog("Deutsch", "x", canRetry: true, owned: true);
                Assert.Equal(Localization.Translate("Deutsch", "Retry"), german.Buttons[0].Text);
                Assert.NotEqual("Retry", german.Buttons[0].Text);
            });
        }

        [Fact]
        public void RetryRunsTheCallersRetryAfterTheBoxIsGoneAndCloseDoesNot()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                foreach ((int index, bool expected) in new[] { (0, true), (2, false) })
                {
                    int retried = 0;
                    var clicker = new Timer { Interval = 40 };
                    clicker.Tick += (_, _) =>
                    {
                        clicker.Stop();
                        foreach (Form open in Application.OpenForms.Cast<Form>().ToArray())
                            if (open is ConfirmDialog dialog) dialog.Buttons[index].PerformClick();
                    };
                    clicker.Start();
                    DialogResult answer = ConfirmDialog.ShowFailure(null, "English", "Could not save.", () => retried++);
                    clicker.Dispose();
                    Assert.Equal(expected ? DialogResult.Retry : DialogResult.Cancel, answer);
                    Assert.Equal(expected ? 1 : 0, retried);
                }
            });
        }

        // ---- the settings window's failure sites ----

        private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "SettingsForm.cs")));

        [Fact]
        public void NoCauseOfAFailureIsShownAsAPlainWarning()
        {
            // A message that is built from a failure's cause is a failure box with actions, never the OK-only warning.
            var plain = new Regex(@"\bWarn\([^;]*FailureCause\.Describe", RegexOptions.Singleline);
            Match match = plain.Match(Source);
            Assert.False(match.Success, "a failure cause is shown through Warn(...): " + match.Value);
            foreach (string wording in new[] { "Could not save the file", "Export failed", "Import failed", "Couldn't start", "Could not delete scenario",
                "Could not write the shortcut", "Could not change the shortcut", "could not be saved", "Could not build the log archive" })
            {
                var warned = new Regex(@"\bWarn\([^;]*" + Regex.Escape(wording), RegexOptions.Singleline);
                Assert.False(warned.IsMatch(Source), "'" + wording + "' is a failure of what the user asked for and goes through Fail(...)");
            }
            // And the actions are really offered where a retry is meaningful.
            int failures = Regex.Matches(Source, @"\bFail\(").Count;
            Assert.True(failures >= 15, "only " + failures + " Fail(...) sites - the failure sites were not converted");
        }

        [Fact]
        public void ValidationWarningsStayWarnings()
        {
            // A refusal the window explains - the last layout, an IME, a chord that is taken - has nothing to retry.
            foreach (string wording in new[] { "Windows must keep at least one layout", "Install at least two layouts", "is already used by the CyrFlip hotkey" })
                Assert.True(new Regex(@"\bWarn\([^;]*" + Regex.Escape(wording), RegexOptions.Singleline).IsMatch(Source),
                    "'" + wording + "' is no longer a plain warning");
        }
    }
}
