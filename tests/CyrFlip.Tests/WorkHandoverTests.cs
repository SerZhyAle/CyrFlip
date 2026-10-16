using System;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Hiding the settings window must not silently kill a running Ollama download or model pull
    /// (APP-BEHAVIOUR rule 3, ticket S0060 P7-i): it is handed over, and its end is told once through
    /// the tray; the real end of the window - or the Cancel button - still stops it.
    /// </summary>
    public class WorkHandoverTests
    {
        [Fact]
        public void HidingWhileTheWorkRunsHandsItOverInsteadOfCancellingIt()
        {
            var handover = new WorkHandover();
            bool cancel = handover.OnHidden(workRunning: true, windowEnding: false);

            Assert.False(cancel, "a multi-gigabyte download was cancelled because the user went back to their editor");
            Assert.True(handover.HandedOver);
        }

        [Fact]
        public void TheEndIsToldOnceAndOnlyWhileTheWindowIsStillHidden()
        {
            var handover = new WorkHandover();
            handover.OnHidden(workRunning: true, windowEnding: false);

            // Progress text arrives while it runs: nothing to say yet.
            Assert.False(handover.ShouldAnnounce(workRunning: true, windowVisible: false));
            Assert.False(handover.ShouldAnnounce(workRunning: true, windowVisible: false));

            // The work ended and the status line changed: now, once.
            Assert.True(handover.ShouldAnnounce(workRunning: false, windowVisible: false));
            Assert.False(handover.ShouldAnnounce(workRunning: false, windowVisible: false));
            Assert.False(handover.HandedOver);
        }

        [Fact]
        public void AUserWhoCameBackIsWatchingAndIsToldNothing()
        {
            var handover = new WorkHandover();
            handover.OnHidden(workRunning: true, windowEnding: false);
            handover.OnShown();

            Assert.False(handover.HandedOver);
            Assert.False(handover.ShouldAnnounce(workRunning: false, windowVisible: true));
            Assert.False(handover.ShouldAnnounce(workRunning: false, windowVisible: false));
        }

        [Fact]
        public void TheWindowEndingStopsTheWorkForGood()
        {
            var handover = new WorkHandover();
            Assert.True(handover.OnHidden(workRunning: true, windowEnding: true));
            Assert.False(handover.HandedOver);
            Assert.False(handover.ShouldAnnounce(workRunning: false, windowVisible: false));
        }

        [Fact]
        public void HidingWithNothingRunningCancelsAStaleTokenAndHandsNothingOver()
        {
            var handover = new WorkHandover();
            Assert.True(handover.OnHidden(workRunning: false, windowEnding: false));
            Assert.False(handover.HandedOver);
            Assert.False(handover.ShouldAnnounce(workRunning: false, windowVisible: false));
        }

        [Fact]
        public void ASecondHideAfterAnAnnouncementStartsFromScratch()
        {
            var handover = new WorkHandover();
            handover.OnHidden(true, false);
            Assert.True(handover.ShouldAnnounce(false, false));

            handover.OnShown();
            handover.OnHidden(true, false);
            Assert.True(handover.HandedOver);
            Assert.True(handover.ShouldAnnounce(false, false));
        }

        // ---- the wiring ----

        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        [Fact]
        public void HidingNoLongerCancelsAndDisposalDoes()
        {
            string form = File.ReadAllText(Path.Combine(Source, "SettingsForm.cs"));

            // The hide handler hands over; the old "cancel on every hide" line is gone.
            Assert.Contains("HandOverTranslateWork()", form);
            Assert.DoesNotContain("CommitTranslateText(); CancelTranslateWork(); }", form);
            // The real end of the window cancels, and the Cancel button is still wired to the same method.
            Assert.Contains("Disposed += (_, _) => CancelTranslateWork();", form);
            Assert.Matches(@"_translateCancel\s*=\s*Button\([^;]*CancelTranslateWork\)", form);
        }

        [Fact]
        public void TheContextTurnsTheEventIntoATrayBalloon()
        {
            string context = File.ReadAllText(Path.Combine(Source, "CyrFlipContext.cs"));
            Assert.Contains("settings.TranslateWorkFinishedInBackground += (_, text) => ShowInfoBalloon(", context);
        }
    }
}
