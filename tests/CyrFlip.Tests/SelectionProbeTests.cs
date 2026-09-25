using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The selection probe's decision logic, exercised without touching a real window: which source
    /// wins, that "cannot tell" never becomes "nothing selected", and - the rule this menu's worst
    /// bug came from - that one source's "nothing here" cannot overrule another's sighting.
    /// </summary>
    public class SelectionProbeTests
    {
        private static Func<SelectionAnswer> Source(SelectionAnswer answer, List<string>? log = null, string name = "")
            => () => { log?.Add(name); return answer; };

        private static Func<SelectionAnswer> Present(string? text = null, List<string>? log = null, string name = "")
            => Source(SelectionAnswer.Present(text), log, name);

        private static Func<SelectionAnswer> Absent(List<string>? log = null, string name = "")
            => Source(SelectionAnswer.Absent, log, name);

        private static Func<SelectionAnswer> Unknown(List<string>? log = null, string name = "")
            => Source(SelectionAnswer.Unknown, log, name);

        [Fact]
        public void EverySourceIsAskedAndASightingWins()
        {
            var calls = new List<string>();
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[]
            {
                Unknown(calls, "edit"),
                Present("https://example.com", calls, "ia2"),
                Absent(calls, "uia"),
            });

            Assert.Equal(SelectionState.Present, snapshot.State);
            Assert.Equal("https://example.com", snapshot.Text);
            Assert.Equal(new[] { "edit", "ia2", "uia" }, calls);
        }

        /// <summary>
        /// The Chromium case, measured live: IA2 answers about the element under the pointer, which
        /// in a page is one node of the selection - a styled word, a highlighted run - so the first
        /// text to arrive was a single character of a selected paragraph. No source says it is giving
        /// a fragment, so length is the only honest tie-break.
        /// </summary>
        [Fact]
        public void TheLongestTextWinsBecauseASourceMayReturnOneNodeOfTheSelection()
        {
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[]
            {
                Present("м", null, "ia2 under the pointer"),
                Present("открой https://example.com/ потом", null, "uia over the document"),
                Present("com", null, "a later fragment"),
            });

            Assert.Equal("открой https://example.com/ потом", snapshot.Text);
        }

        /// <summary>
        /// The bug this rule exists for: in read-only text - a page, a chat transcript, a log - the
        /// selection is under the pointer while the focus sits in some other control, so the first
        /// source answers confidently about the wrong element and Copy went grey beside a live
        /// selection. A sighting by any source is therefore final.
        /// </summary>
        [Fact]
        public void NoSourceCanVetoAnotherSourcesSighting()
        {
            var calls = new List<string>();
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[]
            {
                Absent(calls, "focused edit says empty"),
                Present("selected", calls, "the element under the pointer"),
            });

            Assert.Equal(SelectionState.Present, snapshot.State);
            Assert.Equal("selected", snapshot.Text);
        }

        /// <summary>A sighting without text keeps looking - some sources answer the state only.</summary>
        [Fact]
        public void TheTextMayComeFromALaterSourceThanTheVerdict()
        {
            var calls = new List<string>();
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[]
            {
                Present(null, calls, "state only"),
                Absent(calls, "a stale element"),
                Present("C:\\tools\\notes.txt", calls, "text at last"),
            });

            Assert.Equal(SelectionState.Present, snapshot.State);
            Assert.Equal("C:\\tools\\notes.txt", snapshot.Text);
            Assert.Equal(3, calls.Count);
        }

        [Fact]
        public void AbsentIsTheVerdictOnlyWhenEverySourceAgrees()
        {
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[]
            {
                Unknown(), Absent(), Unknown(),
            });

            Assert.Equal(SelectionState.Absent, snapshot.State);
            Assert.Null(snapshot.Text);
        }

        [Fact]
        public void AllSilentMeansUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new[]
            {
                Unknown(), Unknown(), Unknown(),
            }).State);
        }

        [Fact]
        public void NoSourcesAtAllMeansUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new Func<SelectionAnswer>[0]).State);
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(null!).State);
        }

        /// <summary>A cross-process call that blows up is a source that does not know, not a verdict.</summary>
        [Fact]
        public void AThrowingSourceIsSkippedNotFatal()
        {
            var calls = new List<string>();
            SelectionSnapshot snapshot = SelectionProbe.Decide(new Func<SelectionAnswer>[]
            {
                () => throw new InvalidOperationException("COM said no"),
                Absent(calls, "ia2"),
            });

            Assert.Equal(SelectionState.Absent, snapshot.State);
            Assert.Equal(new[] { "ia2" }, calls);
        }

        [Fact]
        public void EveryThrowingSourceStillEndsAtUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new Func<SelectionAnswer>[]
            {
                () => throw new InvalidOperationException(),
                () => throw new NotSupportedException(),
            }).State);
        }

        /// <summary>An empty string is not text; it must not read as "the selection is empty".</summary>
        [Fact]
        public void AnEmptyTextIsTreatedAsNoTextAtAll()
        {
            SelectionSnapshot snapshot = SelectionProbe.Decide(new[] { Present("") });

            Assert.Equal(SelectionState.Present, snapshot.State);
            Assert.Null(snapshot.Text);
        }

        /// <summary>
        /// EM_GETSEL sent to a window that is not an edit control reaches DefWindowProc and comes back
        /// as 0 - a confident, wrong "nothing selected". Hence the class gate.
        /// </summary>
        [Theory]
        [InlineData("Edit", true)]
        [InlineData("edit", true)]
        [InlineData("RichEdit20W", true)]
        [InlineData("RICHEDIT50W", true)]
        [InlineData("RichEditD2DPT", true)]   // Windows 11 Notepad / WordPad
        [InlineData("Chrome_RenderWidgetHostHWND", false)]
        [InlineData("Notepad", false)]
        [InlineData("Microsoft.UI.Content.DesktopChildSiteBridge", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyEditClassesAreTrustedWithEmGetSel(string? className, bool trusted)
            => Assert.Equal(trusted, SelectionProbe.IsEditClass(className));

        /// <summary>
        /// The budget is what the user's own button-hold pays for, and the wait is what the UI thread
        /// - which the keyboard hook shares - may lose to it. Windows drops a low-level hook that
        /// overruns ~300 ms, so the second number has to stay well clear of it.
        /// </summary>
        [Fact]
        public void TheProbeNeverCostsTheHookItsDeadline()
        {
            Assert.InRange(SelectionProbe.BudgetMs, 50, 400);
            Assert.InRange(SelectionProbe.MaxWaitMs, 20, 150);
            Assert.True(SelectionProbe.MaxWaitMs < SelectionProbe.BudgetMs);
        }
    }
}
