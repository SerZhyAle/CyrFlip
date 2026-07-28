using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The selection probe's decision logic, exercised without touching a real window: which source
    /// wins, and - the load-bearing part - that "cannot tell" never becomes "nothing selected".
    /// </summary>
    public class SelectionProbeTests
    {
        private static Func<SelectionState> Source(SelectionState state, List<string>? log = null, string name = "")
            => () => { log?.Add(name); return state; };

        [Fact]
        public void FirstDefiniteAnswerWins()
        {
            var calls = new List<string>();
            SelectionState state = SelectionProbe.Decide(new[]
            {
                Source(SelectionState.Unknown, calls, "edit"),
                Source(SelectionState.Present, calls, "ia2"),
                Source(SelectionState.Absent, calls, "uia"),
            });

            Assert.Equal(SelectionState.Present, state);
            Assert.Equal(new[] { "edit", "ia2" }, calls); // the third source is never asked
        }

        [Fact]
        public void AnAbsentAnswerIsAlsoDefinite()
        {
            var calls = new List<string>();
            SelectionState state = SelectionProbe.Decide(new[]
            {
                Source(SelectionState.Absent, calls, "edit"),
                Source(SelectionState.Present, calls, "ia2"),
            });

            Assert.Equal(SelectionState.Absent, state);
            Assert.Equal(new[] { "edit" }, calls);
        }

        [Fact]
        public void AllSilentMeansUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new[]
            {
                Source(SelectionState.Unknown),
                Source(SelectionState.Unknown),
                Source(SelectionState.Unknown),
            }));
        }

        [Fact]
        public void NoSourcesAtAllMeansUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new Func<SelectionState>[0]));
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(null!));
        }

        /// <summary>A cross-process call that blows up is a source that does not know, not a verdict.</summary>
        [Fact]
        public void AThrowingSourceIsSkippedNotFatal()
        {
            var calls = new List<string>();
            SelectionState state = SelectionProbe.Decide(new Func<SelectionState>[]
            {
                () => throw new InvalidOperationException("COM said no"),
                Source(SelectionState.Absent, calls, "ia2"),
            });

            Assert.Equal(SelectionState.Absent, state);
            Assert.Equal(new[] { "ia2" }, calls);
        }

        [Fact]
        public void EveryThrowingSourceStillEndsAtUnknown()
        {
            Assert.Equal(SelectionState.Unknown, SelectionProbe.Decide(new Func<SelectionState>[]
            {
                () => throw new InvalidOperationException(),
                () => throw new NotSupportedException(),
            }));
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

        /// <summary>The budget is what the user's own button-hold pays for; keep it visible in a test.</summary>
        [Fact]
        public void BudgetIsShortEnoughToFeelInstant()
            => Assert.InRange(SelectionProbe.BudgetMs, 50, 250);
    }
}
