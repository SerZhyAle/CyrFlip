using System;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0065 E-4: a flip chord that returned <c>NoChange</c> or <c>Cancelled</c> was consumed without
    /// a word - indistinguishable from a dead hotkey, except this time the hotkey had just fired.
    /// <see cref="CyrFlipContext.FlipResultNotice"/> is the pure core that gives every outcome its
    /// sentence; E-7's <see cref="ClipboardHistoryWindow.RestoreNotice"/> is the same discipline for
    /// a restore the clipboard refused.
    /// </summary>
    public class FlipResultNoticeTests
    {
        private static string English(string ru) => Localization.Translate("English", ru);

        [Fact]
        public void NoChangeAndCancelledHaveASentence()
        {
            Assert.Equal("The text is already in this layout - nothing to convert.",
                CyrFlipContext.FlipResultNotice(ClipboardHandler.FlipResult.NoChange, English)!.Value.text);
            Assert.Equal("The window changed - the conversion was cancelled.",
                CyrFlipContext.FlipResultNotice(ClipboardHandler.FlipResult.Cancelled, English)!.Value.text);
        }

        [Fact]
        public void AFlipThatFlippedSaysNothing()
        {
            Assert.Null(CyrFlipContext.FlipResultNotice(ClipboardHandler.FlipResult.Flipped, English));
        }

        [Fact]
        public void EveryOutcomeExceptFlippedHasASentenceInAllThirteenLanguages()
        {
            foreach (string language in Localization.Names)
            {
                string T(string ru) => Localization.Translate(language, ru);
                foreach (ClipboardHandler.FlipResult result in Enum.GetValues(typeof(ClipboardHandler.FlipResult)))
                {
                    (string text, ToolTipIcon icon, int timeout)? notice = CyrFlipContext.FlipResultNotice(result, T);
                    if (result == ClipboardHandler.FlipResult.Flipped)
                    {
                        Assert.Null(notice);
                        continue;
                    }
                    Assert.NotNull(notice);
                    Assert.False(string.IsNullOrWhiteSpace(notice!.Value.text),
                        result + " has no sentence in " + language);
                    Assert.NotEqual(0, notice.Value.timeout);
                }
            }
        }

        // ---- E-7: the history strip and the search window ----

        [Fact]
        public void ARefusedRestoreSaysSo()
        {
            Assert.Equal("Не удалось скопировать текст в буфер обмена. Повторите.",
                ClipboardHistoryWindow.RestoreNotice(false, ru => ru));
            Assert.Equal("Could not copy the text to the clipboard. Try again.",
                ClipboardHistoryWindow.RestoreNotice(false, English));
        }

        [Fact]
        public void ARestoreThatWorkedSaysNothing()
        {
            Assert.Null(ClipboardHistoryWindow.RestoreNotice(true, English));
        }
    }
}
