using System.Text;
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
    }
}
