using System;
using System.Text;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// caret-diagnostics.txt goes into the log bundle the user mails to the author (S0010 TD-1), and
    /// a window title is a mail subject, a document name or a URL: the report keeps the class and the
    /// process, and reduces the title to its length.
    /// </summary>
    public class CaretDiagnosticsTests
    {
        [Fact]
        public void TheWindowIsRecordedWithoutItsTitle()
        {
            var sb = new StringBuilder();

            CaretDiagnostics.AppendWindow(sb, "Chrome_WidgetWin_1", "Re: salary review - Outlook", "olk", 1234);

            string text = sb.ToString();
            Assert.DoesNotContain("salary", text);
            Assert.DoesNotContain("Outlook", text);
            Assert.Contains("class:   Chrome_WidgetWin_1", text);
            Assert.Contains("title:   27 chars", text);
            Assert.Contains("process: olk (pid 1234)", text);
        }

        [Fact]
        public void AnUnreadableTitleSaysSo()
            => Assert.Equal("<unreadable>", CaretDiagnostics.TextLength(null));

        /// <summary>
        /// S0065 E-6: the failure callback takes the exception, never its raw message - the OS text
        /// can carry the data-folder path (the account name) into a balloon, and the file no longer
        /// sits in the AppBehaviourGate exemption list. The caller classifies it through
        /// FailureCause, whose sentence never contains the path.
        /// </summary>
        [Fact]
        public void TheFailureCallbackCarriesTheExceptionNotItsMessage()
        {
            Exception? seen = null;
            var done = new ManualResetEventSlim(false);
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool started = CaretDiagnostics.Run(
                onDone: _ => done.Set(),
                onError: ex => { seen = ex; done.Set(); },
                capture: () => throw new UnauthorizedAccessException(
                    "Access to the path '" + folder + "\\CyrFlip\\caret-diagnostics.txt' is denied."));
            Assert.True(started);
            Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "the capture never answered");
            Assert.IsType<UnauthorizedAccessException>(seen);

            string described = FailureCause.Describe(seen, "English");
            Assert.Equal("Access to file or folder is denied.", described);
            Assert.DoesNotContain("Users", described);
            Assert.DoesNotContain(folder, described);
        }
    }
}
