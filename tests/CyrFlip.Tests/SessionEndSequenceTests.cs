using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The sign-out sequence (ticket S0003 LC-2, S0035 QN2-6): what carries data comes before the
    /// optional compaction, a compaction already running is never waited for or repeated, and every
    /// wait is bounded by what is left of the budget. A fake clock stands in for the seconds Windows
    /// gives, so the rules are tested without a session to end.
    /// </summary>
    public class SessionEndSequenceTests
    {
        private sealed class Recorder
        {
            public readonly List<string> Steps = new List<string>();
            public readonly Dictionary<string, TimeSpan> Waits = new Dictionary<string, TimeSpan>();
            public TimeSpan Now;
            public bool Running;

            public SessionEndSequence Sequence(TimeSpan historyCost = default, TimeSpan compactionCost = default) => new SessionEndSequence
            {
                Retract = () => Steps.Add("retract"),
                FlushNotes = () => Steps.Add("notes"),
                DrainHistory = wait => { Steps.Add("history"); Waits["history"] = wait; Now += historyCost; },
                RestoreCursor = () => Steps.Add("cursor"),
                CompactionRunning = () => Running,
                Compact = () => { Steps.Add("compact"); Now += compactionCost; },
                FlushLogs = wait => { Steps.Add("logs"); Waits["logs"] = wait; },
            };
        }

        [Fact]
        public void DataComesFirstAndTheCompactionLast()
        {
            var r = new Recorder();
            r.Sequence().Run(() => r.Now);

            Assert.Equal(new[] { "retract", "notes", "history", "cursor", "compact", "logs" }, r.Steps);
        }

        [Fact]
        public void ACompactionAlreadyRunningIsNeitherWaitedForNorRepeated()
        {
            var r = new Recorder { Running = true };
            r.Sequence().Run(() => r.Now);

            Assert.DoesNotContain("compact", r.Steps);
            Assert.Contains("history", r.Steps);
            Assert.Contains("logs", r.Steps);
        }

        [Fact]
        public void ASlowHistoryDrainCostsTheCompactionNotTheLogs()
        {
            var r = new Recorder();
            r.Sequence(historyCost: SessionEndSequence.HistoryDrain).Run(() => r.Now);

            Assert.DoesNotContain("compact", r.Steps);
            Assert.Equal(SessionEndSequence.HistoryDrain, r.Waits["history"]);
            Assert.Equal(SessionEndSequence.LogDrain, r.Waits["logs"]);
        }

        [Fact]
        public void EveryWaitIsBoundedByWhatIsLeft()
        {
            var r = new Recorder { Now = SessionEndSequence.Budget - TimeSpan.FromMilliseconds(200) };
            r.Sequence().Run(() => r.Now);

            Assert.Equal(TimeSpan.FromMilliseconds(200), r.Waits["history"]);
            Assert.Equal(TimeSpan.FromMilliseconds(200), r.Waits["logs"]);
            Assert.DoesNotContain("compact", r.Steps);
        }

        [Fact]
        public void AStepThatThrowsDoesNotCostTheNext()
        {
            var r = new Recorder();
            SessionEndSequence sequence = r.Sequence();
            sequence.FlushNotes = () => throw new InvalidOperationException("disk full");
            sequence.Run(() => r.Now);

            Assert.Equal(new[] { "retract", "history", "cursor", "compact", "logs" }, r.Steps);
        }
    }
}
