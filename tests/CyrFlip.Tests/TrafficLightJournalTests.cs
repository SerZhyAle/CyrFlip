using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>The change journal and the hover text (ticket S0069 D19 and section 6).</summary>
    public class TrafficLightJournalTests : IDisposable
    {
        private static readonly TimeZoneInfo Plus3 = TimeZoneInfo.CreateCustomTimeZone("plus3", TimeSpan.FromHours(3), "plus3", "plus3");
        private static readonly DateTime T0 = new DateTime(2026, 10, 13, 11, 5, 12, DateTimeKind.Utc); // 14:05:12 +03:00

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "CyrFlip-tl-journal-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        private static TrafficLightTarget Target(bool record = true, string label = "Office NAS", string kind = "sftp", string address = "nas.local:22")
            => new TrafficLightTarget { Id = "t", Label = label, Kind = kind, Address = address, RecordHistory = record };

        // ---- the format ----

        [Fact]
        public void ADownLineCarriesTheReasonHowLongItHeldAndTheFirstFailure()
        {
            var change = new TrafficLightChange
            {
                Target = Target(), From = TrafficState.Ok, To = TrafficState.Down, Reason = "timeout", AtUtc = T0,
                WasFor = new TimeSpan(3, 4, 12, 0), FirstFailureUtc = T0.AddSeconds(-10),
            };

            Assert.Equal(
                "2026-10-13 14:05:12 +03:00  DOWN      [Office NAS] sftp nas.local:22  reason=timeout  was=OK for 3d 4h 12m  first-failure=14:05:02",
                TrafficLightJournal.FormatLine(change, Plus3));
        }

        [Fact]
        public void AnOkLineCarriesTheLatencyAndWhatItWas()
        {
            var change = new TrafficLightChange
            {
                Target = Target(), From = TrafficState.Down, To = TrafficState.Ok, Reason = "ok", AtUtc = T0.AddSeconds(148),
                LatencyMs = 41, WasFor = TimeSpan.FromSeconds(148),
            };

            Assert.Equal(
                "2026-10-13 14:07:40 +03:00  OK        [Office NAS] sftp nas.local:22  latency=41 ms  was=DOWN for 2m 28s",
                TrafficLightJournal.FormatLine(change, Plus3));
        }

        [Fact]
        public void ADegradedLineNamesItsReasonAndAFirstResultIsAStart()
        {
            var degraded = new TrafficLightChange
            {
                Target = Target(), From = TrafficState.Ok, To = TrafficState.Degraded, Reason = "slow", AtUtc = T0,
                LatencyMs = 2100, WasFor = TimeSpan.FromSeconds(45),
            };
            Assert.Contains("DEGRADED  [Office NAS]", TrafficLightJournal.FormatLine(degraded, Plus3));
            Assert.Contains("reason=slow", TrafficLightJournal.FormatLine(degraded, Plus3));
            Assert.Contains("was=OK for 45s", TrafficLightJournal.FormatLine(degraded, Plus3));

            var start = new TrafficLightChange
            {
                Target = Target(), From = TrafficState.Unknown, To = TrafficState.Ok, Reason = "ok", AtUtc = T0,
                LatencyMs = 12, IsBaseline = true,
            };
            string line = TrafficLightJournal.FormatLine(start, Plus3);
            Assert.Contains("  START     [Office NAS]", line);
            Assert.Contains("state=OK", line);
            Assert.DoesNotContain("was=", line);
        }

        [Fact]
        public void TheLineIsTheSameInEveryUiLanguageAndNeverHasABreak()
        {
            var change = new TrafficLightChange
            {
                Target = Target(label: "Бухгалтерия\r\nсервер"), From = TrafficState.Ok, To = TrafficState.Down,
                Reason = "connection-refused", AtUtc = T0,
            };
            string line = TrafficLightJournal.FormatLine(change, Plus3);

            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
            Assert.Contains("[Бухгалтерия  сервер]", line);
            Assert.Contains("reason=connection-refused", line);
        }

        [Theory]
        [InlineData(0, "0s")]
        [InlineData(45, "45s")]
        [InlineData(148, "2m 28s")]
        [InlineData(3 * 3600 + 5 * 60, "3h 5m")]
        [InlineData(3 * 86400 + 4 * 3600 + 12 * 60, "3d 4h 12m")]
        public void DurationsUseTheTwoLargestUnits(int seconds, string expected)
        {
            Assert.Equal(expected, TrafficLightJournal.FormatDuration(TimeSpan.FromSeconds(seconds)));
        }

        // ---- the file ----

        private sealed class Files : ITrafficJournalFiles
        {
            private readonly ITrafficJournalFiles _inner = new DiskTrafficJournalFiles();
            public int FailAppends;
            public long ReportedLength = -1;
            public long LengthOf(string path) => ReportedLength >= 0 ? ReportedLength : _inner.LengthOf(path);
            public void Append(string path, string text)
            {
                if (FailAppends > 0) { FailAppends--; throw new IOException("disk full"); }
                _inner.Append(path, text);
            }
            public void Rename(string from, string to) { _inner.Rename(from, to); ReportedLength = -1; }
            public bool Exists(string path) => _inner.Exists(path);
            public void Delete(string path) => _inner.Delete(path);
            public IEnumerable<string> Enumerate(string folder, string pattern) => _inner.Enumerate(folder, pattern);
        }

        private static TrafficLightChange Down(string id, bool record) => new TrafficLightChange
        {
            Target = new TrafficLightTarget { Id = id, Label = id, Kind = "ping", Address = "10.0.0.1", RecordHistory = record },
            From = TrafficState.Ok, To = TrafficState.Down, Reason = "timeout", AtUtc = T0,
        };

        [Fact]
        public void OnlyTargetsThatAskedAreWrittenAndEachLineEndsInCrLf()
        {
            using var journal = new TrafficLightJournal(_folder, zone: Plus3);
            journal.Record(Down("recorded", true));
            journal.Record(Down("silent", false));
            journal.Marker(TrafficJournalMarker.Offline);
            Assert.True(journal.Flush(3000));

            string text = File.ReadAllText(journal.FilePath);
            Assert.Contains("[recorded]", text);
            Assert.DoesNotContain("[silent]", text);
            Assert.Contains("-- computer offline, checks paused --", text);
            Assert.EndsWith("\r\n", text);
            Assert.Equal(2, text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.NotEqual('﻿', text[0]); // no byte-order mark
        }

        [Fact]
        public void AFailedWriteIsKeptRetriedWithTheNextRecordAndReportedOnce()
        {
            var files = new Files { FailAppends = 2 };
            var reports = new List<string>();
            using var journal = new TrafficLightJournal(_folder, files, zone: Plus3);
            journal.WriteFailed += reports.Add;

            journal.Record(Down("a", true));
            Assert.False(journal.Flush(300), "the first record could not be written");
            journal.Record(Down("b", true));
            Assert.False(journal.Flush(300));
            journal.Record(Down("c", true));
            Assert.True(journal.Flush(3000));

            string[] lines = File.ReadAllLines(journal.FilePath);
            Assert.Equal(3, lines.Length);
            Assert.Contains("[a]", lines[0]); // in order, nothing lost
            Assert.Contains("[c]", lines[2]);
            Assert.Single(reports);            // once per session, not once per failure
        }

        [Fact]
        public void AFilePastFiveMegabytesIsRenamedWithTheDateAndANewOneStarts()
        {
            var files = new Files();
            using var journal = new TrafficLightJournal(_folder, files, () => T0, Plus3);
            journal.Record(Down("first", true));
            Assert.True(journal.Flush(3000));

            files.ReportedLength = TrafficLightJournal.SplitAtBytes + 1;
            journal.Record(Down("second", true));
            Assert.True(journal.Flush(3000));

            string rotated = Path.Combine(_folder, "traffic-light-history-20261013.txt");
            Assert.True(File.Exists(rotated));
            Assert.Contains("[first]", File.ReadAllText(rotated));
            Assert.Contains("[second]", File.ReadAllText(journal.FilePath));
            Assert.DoesNotContain("[first]", File.ReadAllText(journal.FilePath));
        }

        [Fact]
        public void TheQueueIsBoundedAtAThousandLines()
        {
            var files = new Files { FailAppends = int.MaxValue };
            using var journal = new TrafficLightJournal(_folder, files, zone: Plus3);
            for (int i = 0; i < 1100; i++) journal.Record(Down("n" + i, true));
            journal.Flush(200);
            files.FailAppends = 0;
            journal.Record(Down("last", true));
            Assert.True(journal.Flush(5000));

            string[] lines = File.ReadAllLines(journal.FilePath);
            Assert.True(lines.Length <= TrafficLightJournal.MaxQueuedLines + 1);
            Assert.Contains("[last]", lines.Last());
        }

        [Fact]
        public void ClearingRemovesTheLiveFileAndTheRotatedOnes()
        {
            var files = new Files();
            using var journal = new TrafficLightJournal(_folder, files, () => T0, Plus3);
            journal.Record(Down("a", true));
            Assert.True(journal.Flush(3000));
            files.ReportedLength = TrafficLightJournal.SplitAtBytes + 1;
            journal.Record(Down("b", true));
            Assert.True(journal.Flush(3000));
            Assert.Equal(2, Directory.GetFiles(_folder, "traffic-light-history*.txt").Length);

            Assert.True(journal.Clear());

            Assert.Empty(Directory.GetFiles(_folder, "traffic-light-history*.txt"));
        }

        [Fact]
        public void TheJournalIsNeverInASupportBundle()
        {
            Assert.Contains(TrafficLightJournal.FileName, SupportBundle.ExcludedFiles);
            Assert.DoesNotContain(TrafficLightJournal.FileName, SupportBundle.LogFiles);
            foreach (string name in SupportBundle.LogFiles)
                Assert.False(name.StartsWith("traffic-light-history", StringComparison.Ordinal), name);
        }

        // ---- the hover text ----

        private static TrafficLightTile Tile(TrafficState state, string reason = "ok", Action<TrafficLightTile>? change = null)
        {
            var tile = new TrafficLightTile
            {
                Target = new TrafficLightTarget { Id = "t", Label = "Router", Kind = "ping", Address = "192.168.1.1", IntervalSeconds = 60, FailuresBeforeDown = 3 },
                State = state, Reason = reason, CheckedAtUtc = T0, NextDueUtc = T0.AddSeconds(60),
            };
            change?.Invoke(tile);
            return tile;
        }

        private static string Tip(TrafficLightTile tile) => TrafficLightText.Tooltip(tile, s => s, T0.AddSeconds(5), Plus3);

        [Fact]
        public void TheTooltipSaysWhatTheTileIs()
        {
            string tip = Tip(Tile(TrafficState.Ok, "ok", t => t.LatencyMs = 4));

            Assert.StartsWith("Router", tip);
            Assert.Contains("Host (ping) - 192.168.1.1", tip);
            Assert.Contains("State: Available", tip);
            Assert.Contains("Latency: 4 ms", tip);
            Assert.Contains("Next check:", tip);
        }

        [Fact]
        public void ARedTooltipSaysWhyAndWhenItLastWorked()
        {
            string tip = Tip(Tile(TrafficState.Down, "timeout", t => t.LastOkAtUtc = T0.AddMinutes(-30)));

            Assert.Contains("State: Unavailable - no answer within the time limit", tip);
            Assert.Contains("Last worked:", tip);
            Assert.DoesNotContain("Latency:", tip);
        }

        [Fact]
        public void AnOpenStreakStaleAndOfflineAreAllSaid()
        {
            string confirming = Tip(Tile(TrafficState.Ok, "timeout", t => t.FailureStreak = 2));
            Assert.Contains("Confirming: 2 of 3", confirming);

            string stale = Tip(Tile(TrafficState.Ok, "ok", t => t.Stale = true));
            Assert.Contains("The result is out of date.", stale);

            string offline = Tip(Tile(TrafficState.Ok, "ok", t => { t.Stale = true; t.PausedOffline = true; }));
            Assert.Contains("This computer is offline - checking is paused.", offline);
            Assert.DoesNotContain("The result is out of date.", offline);
        }

        [Fact]
        public void AManualOnlyTargetSaysSoAndADisabledOneIsJustOff()
        {
            string manual = Tip(Tile(TrafficState.Ok, "ok", t => t.Target.IntervalSeconds = 0));
            Assert.Contains("manual only", manual);

            string off = Tip(Tile(TrafficState.Ok, "ok", t => t.Target.Enabled = false));
            Assert.Contains("State: Off", off);
            Assert.DoesNotContain("Checked:", off);
        }

        [Fact]
        public void AnHttpReasonCarriesTheStatusAndTheSummaryLineFollowsTheCounts()
        {
            Assert.Equal("server answered 401", TrafficLightText.ReasonWord("http-401", s => s));

            Func<string, string> same = s => s;
            Assert.Equal("unavailable: 1 of 12", TrafficLightText.SummaryLine(new TrafficLightSummary { Total = 12, Down = 1 }, same));
            Assert.Equal("all available: 12", TrafficLightText.SummaryLine(new TrafficLightSummary { Total = 12 }, same));
            Assert.Equal("this computer is offline", TrafficLightText.SummaryLine(new TrafficLightSummary { Total = 12, Offline = true }, same));
            Assert.Null(TrafficLightText.SummaryLine(new TrafficLightSummary(), same));
        }
    }
}
