using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0065 E-2, the containment half of S0061's D3 ((b)+(c), decided 2026-10-11: contain every
    /// handler fault, exit after 5 faults in 5 minutes): a contained fault is reported and the app
    /// lives on; the fifth fault in the window is fatal; faults older than the window stop counting;
    /// and the fatal box (E-1) is localized, restart-telling and path-free in all thirteen languages.
    /// </summary>
    public class FaultContainmentTests
    {
        public FaultContainmentTests()
        {
            FaultContainment.Reset();
        }

        [Fact]
        public void FourContainedFaultsReportAndTheFifthIsFatal()
        {
            DateTime now = new DateTime(2026, 10, 11, 12, 0, 0);
            var reported = new List<Exception>();
            var fatal = new List<Exception?>();
            var fault = new InvalidOperationException();

            for (int i = 1; i < FaultContainment.MaxFaults; i++)
            {
                FaultContainment.Contain(fault, reported.Add, fatal.Add, () => now);
                Assert.Equal(i, reported.Count);
                Assert.Empty(fatal);
            }

            FaultContainment.Contain(fault, reported.Add, fatal.Add, () => now);
            Assert.Single(fatal);
            Assert.Equal(FaultContainment.MaxFaults - 1, reported.Count); // the fatal one shows no balloon
        }

        [Fact]
        public void FaultsOlderThanTheWindowStopCounting()
        {
            DateTime fresh = new DateTime(2026, 10, 11, 12, 0, 0);
            DateTime stale = fresh.Add(-FaultContainment.Window - TimeSpan.FromSeconds(1));
            var reported = new List<Exception>();
            var fatal = new List<Exception?>();
            var fault = new InvalidOperationException();

            for (int i = 0; i < FaultContainment.MaxFaults - 1; i++)
                FaultContainment.Contain(fault, reported.Add, fatal.Add, () => stale);

            // The old faults are outside the window, so the fresh one is contained, not fatal.
            FaultContainment.Contain(fault, reported.Add, fatal.Add, () => fresh);
            Assert.Empty(fatal);
            Assert.Equal(FaultContainment.MaxFaults, reported.Count);
        }

        [Fact]
        public void FaultsSpreadWiderThanTheWindowNeverBecomeFatal()
        {
            DateTime now = new DateTime(2026, 10, 11, 12, 0, 0);
            var reported = new List<Exception>();
            var fatal = new List<Exception?>();
            var fault = new InvalidOperationException();

            for (int i = 0; i < FaultContainment.MaxFaults * 3; i++)
            {
                FaultContainment.Contain(fault, reported.Add, fatal.Add, () => now);
                now += FaultContainment.Window; // each fault a full window apart
            }
            Assert.Empty(fatal); // none of them is a loop
            Assert.Equal(FaultContainment.MaxFaults * 3, reported.Count);
        }

        [Fact]
        public void AReporterThatThrowsDoesNotPropagate()
        {
            DateTime now = new DateTime(2026, 10, 11, 12, 0, 0);
            var fatal = new List<Exception?>();
            FaultContainment.Contain(new InvalidOperationException(),
                _ => throw new InvalidOperationException("balloon"), fatal.Add, () => now);
            Assert.Empty(fatal); // the fault inside the balloon is contained too, not escalated
        }

        // ---- E-1: the fatal box ----

        [Theory]
        [InlineData("Русский", "запустите CyrFlip снова")]
        [InlineData("English", "start CyrFlip again")]
        [InlineData("Українська", "запустіть CyrFlip знову")]
        public void TheFatalBoxIsLocalizedAndTellsTheUserToStartCyrFlipAgain(string language, string expected)
        {
            string text = Program.FatalBoxText(language);
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        [Fact]
        public void TheFatalBoxCarriesNoPath()
        {
            string text = Program.FatalBoxText("English");
            Assert.DoesNotContain("\\", text);
            Assert.DoesNotContain("AppData", text);
            Assert.DoesNotContain("logs", text);
        }

        [Fact]
        public void TheFatalBoxSaysThatEveryHotkeyWentWithTheProcess()
        {
            Assert.Contains("hotkey", Program.FatalBoxText("English"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("клавиши", Program.FatalBoxText("Русский"), StringComparison.Ordinal);
        }

        [Fact]
        public void TheFatalBoxExistsInAllThirteenLanguages()
        {
            foreach (string language in Localization.Names)
            {
                string text = Program.FatalBoxText(language);
                Assert.False(string.IsNullOrWhiteSpace(text), "no fatal box for " + language);
                Assert.Contains("CyrFlip", text, StringComparison.Ordinal);
            }
        }
    }
}
