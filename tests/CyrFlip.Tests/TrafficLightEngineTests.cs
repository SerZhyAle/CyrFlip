using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The traffic light's state machine (ticket S0069 sections 4 and 5), against an injected clock: last known
    /// state, the failure streak, due times, staleness, offline and the balloon rules. No timer, no network.
    /// </summary>
    public class TrafficLightEngineTests
    {
        private sealed class Clock
        {
            public DateTime Now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
        }

        private static TrafficLightTarget Target(string id, string kind = "ping", string address = "192.168.1.1",
            int interval = 60, int failures = 1, bool notify = false)
            => new TrafficLightTarget
            {
                Id = id, Label = id, Kind = kind, Address = address, IntervalSeconds = interval,
                FailuresBeforeDown = failures, Notify = notify,
            };

        private static TrafficLightEngine Engine(Clock clock, params TrafficLightTarget[] targets)
        {
            var engine = new TrafficLightEngine(() => clock.Now, path => path.StartsWith(@"\\", StringComparison.Ordinal));
            engine.SetTargets(targets);
            return engine;
        }

        private static TrafficLightChange? Run(TrafficLightEngine engine, string id, ProbeOutcome outcome)
        {
            int generation = engine.MarkDispatched(id);
            Assert.True(generation >= 0, "the target was not dispatchable");
            TrafficLightChange? change = engine.Complete(id, generation, outcome);
            engine.Release(id, generation);
            return change;
        }

        private static TrafficLightTile Tile(TrafficLightEngine engine, string id) => engine.Snapshot().Single(t => t.Target.Id == id);

        // ---- last known state ----

        [Fact]
        public void ANewTargetIsGrayAndDueAtOnce()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));

            Assert.Equal(TrafficState.Unknown, Tile(engine, "a").State);
            Assert.Single(engine.CollectDue());
        }

        [Fact]
        public void TheTileKeepsItsStateWhileACheckIsInFlight()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));
            Run(engine, "a", ProbeOutcome.Good(10));
            clock.Advance(61);

            int generation = engine.MarkDispatched("a");
            TrafficLightTile running = Tile(engine, "a");

            Assert.True(running.Checking);
            Assert.Equal(TrafficState.Ok, running.State); // not gray, not pending: the last known state
            Assert.Empty(engine.CollectDue());            // one in-flight check per target
            engine.Release("a", generation);
        }

        [Fact]
        public void TheIntervalCountsFromTheCompletionOfThePreviousCheck()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", interval: 60));
            int generation = engine.MarkDispatched("a");
            clock.Advance(5); // the check took five seconds
            engine.Complete("a", generation, ProbeOutcome.Good(5));
            engine.Release("a", generation);

            clock.Advance(59);
            Assert.Empty(engine.CollectDue());
            clock.Advance(1);
            Assert.Single(engine.CollectDue());
        }

        [Fact]
        public void ATwoSecondTargetAndAnHourlyOneShareOneBoardAsEquals()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("fast", interval: 2), Target("slow", interval: 3600));
            Run(engine, "fast", ProbeOutcome.Good(1));
            Run(engine, "slow", ProbeOutcome.Good(1));

            clock.Advance(2);
            Assert.Equal(new[] { "fast" }, engine.CollectDue().Select(t => t.Id).ToArray());
            clock.Advance(3598);
            Assert.Equal(new[] { "fast", "slow" }, engine.CollectDue().Select(t => t.Id).ToArray());
        }

        [Fact]
        public void AManualOnlyTargetIsDueAtStartAndOnRequestOnly()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("m", interval: 0));
            Assert.Single(engine.CollectDue());
            Run(engine, "m", ProbeOutcome.Good(1));

            clock.Advance(10 * 3600);
            Assert.Empty(engine.CollectDue());
            Assert.True(engine.RequestNow("m"));
            Assert.Single(engine.CollectDue());
        }

        // ---- failure streak ----

        [Fact]
        public void AFailureBelowTheStreakKeepsTheStateAndOneSuccessClearsIt()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", failures: 3));
            Run(engine, "a", ProbeOutcome.Good(5));

            Assert.Null(Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout)));
            TrafficLightTile first = Tile(engine, "a");
            Assert.Equal(TrafficState.Ok, first.State);
            Assert.True(first.Confirming);
            Assert.Equal(1, first.FailureStreak);

            Assert.Null(Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout)));
            Assert.Null(Run(engine, "a", ProbeOutcome.Good(5))); // the streak is gone: no change at all
            Assert.Equal(0, Tile(engine, "a").FailureStreak);
            Assert.Equal(TrafficState.Ok, Tile(engine, "a").State);
        }

        [Fact]
        public void TheStreakReachesTheThresholdAndTheTileTurnsRed()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", failures: 3));
            Run(engine, "a", ProbeOutcome.Good(5));

            Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));
            Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));
            TrafficLightChange? change = Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.ConnectionRefused));

            Assert.NotNull(change);
            Assert.Equal(TrafficState.Ok, change!.From);
            Assert.Equal(TrafficState.Down, change.To);
            Assert.Equal(TrafficReasons.ConnectionRefused, change.Reason);
            Assert.NotNull(change.FirstFailureUtc);
            Assert.Equal(TrafficState.Down, Tile(engine, "a").State);
        }

        [Fact]
        public void WhileAStreakIsOpenTheNextCheckIsDueAfterTenSecondsNotAnHour()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", interval: 3600, failures: 3));
            Run(engine, "a", ProbeOutcome.Good(5));
            clock.Advance(3600);
            Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));

            clock.Advance(9);
            Assert.Empty(engine.CollectDue());
            clock.Advance(1);
            Assert.Single(engine.CollectDue());
        }

        [Fact]
        public void ARecoveryIsImmediateAndKnowsHowLongTheLightHeld()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));
            Run(engine, "a", ProbeOutcome.Good(5));
            clock.Advance(120);
            Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));
            clock.Advance(90);

            TrafficLightChange? back = Run(engine, "a", ProbeOutcome.Good(7));

            Assert.NotNull(back);
            Assert.Equal(TrafficState.Down, back!.From);
            Assert.Equal(TrafficState.Ok, back.To);
            Assert.Equal(TimeSpan.FromSeconds(90), back.WasFor);
        }

        // ---- the verdict ----

        [Fact]
        public void ASlowAnswerIsYellowAndAnAcceptedOneIsGreen()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a")); // ping: 500 ms threshold

            Run(engine, "a", ProbeOutcome.Good(499));
            Assert.Equal(TrafficState.Ok, Tile(engine, "a").State);

            TrafficLightChange? slow = Run(engine, "a", ProbeOutcome.Good(501));
            Assert.Equal(TrafficState.Degraded, slow!.To);
            Assert.Equal(TrafficReasons.Slow, slow.Reason);
        }

        [Fact]
        public void AnHttpStatusOutsideTheListIsYellowNotRed()
        {
            TrafficLightStatusCodes codes = TrafficLightStatusCodes.Parse("200-399");
            ProbeOutcome unauthorized = HttpProbe.Judge(401, codes, 40, "");
            Assert.True(unauthorized.Reached);
            Assert.True(unauthorized.Degraded);
            Assert.Equal("http-401", unauthorized.Reason);

            ProbeOutcome accepted = HttpProbe.Judge(401, TrafficLightStatusCodes.Parse("200-399,401"), 40, "");
            Assert.False(accepted.Degraded);
        }

        [Fact]
        public void TheExchangeVerdictMatrix()
        {
            const string pin = "SHA256:Ul2rC03A44auS8B7ibLjJaAApHooIx/4R+SSgDTXUtY";
            const string other = "SHA256:++++////NA+xR01JbXXB5CJm2pgMG7SRVTB8msjcGFw";

            // a given pin: the leaf matches, or the security class "certificate changed"
            Assert.True(ExchangeProbe.Verdict(pin, pin, SslPolicyErrors.RemoteCertificateChainErrors, 10).Reached);
            ProbeOutcome changed = ExchangeProbe.Verdict(pin, other, SslPolicyErrors.None, 10);
            Assert.False(changed.Reached);
            Assert.Equal(TrafficReasons.CertificateChanged, changed.Reason);
            Assert.Contains(pin, changed.Note);
            Assert.Contains(other, changed.Note);

            // no pin: trusted by Windows is green, untrusted is "alive, not verified"
            Assert.False(ExchangeProbe.Verdict("", other, SslPolicyErrors.None, 10).Degraded);
            ProbeOutcome unverified = ExchangeProbe.Verdict("", other, SslPolicyErrors.RemoteCertificateChainErrors, 10);
            Assert.True(unverified.Reached);
            Assert.True(unverified.Degraded);
            Assert.Equal(TrafficReasons.Unverified, unverified.Reason);
        }

        [Theory]
        [InlineData("SSH-2.0-OpenSSH_9.6\r\n", true)]
        [InlineData("SSH-1.99-Server\r\n", true)]
        [InlineData("SSH-2.0-Go", true)]
        [InlineData("SSH-1.5-Old\r\n", false)]
        [InlineData("HTTP/1.1 400 Bad Request\r\n", false)]
        [InlineData("", false)]
        public void TheSftpIdentificationLineMatrix(string line, bool ssh)
        {
            Assert.Equal(ssh, SftpProbe.IsIdentification(line));
        }

        // ---- stale, offline, edits ----

        [Fact]
        public void AResultOlderThanThreeIntervalsIsStaleAndKeepsItsColour()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", interval: 10));
            Run(engine, "a", ProbeOutcome.Good(5));

            clock.Advance(30);
            Assert.False(Tile(engine, "a").Stale);
            clock.Advance(1);
            TrafficLightTile stale = Tile(engine, "a");
            Assert.True(stale.Stale);
            Assert.Equal(TrafficState.Ok, stale.State);
        }

        [Fact]
        public void AManualOnlyResultIsStaleAfterAnHour()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("m", interval: 0));
            Run(engine, "m", ProbeOutcome.Good(5));

            clock.Advance(3599);
            Assert.False(Tile(engine, "m").Stale);
            clock.Advance(2);
            Assert.True(Tile(engine, "m").Stale);
        }

        [Fact]
        public void OfflinePausesNetworkKindsButNotLocalFolders()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock,
                Target("net", "ping"),
                Target("share", "folder", @"\\nas\share"),
                new TrafficLightTarget { Id = "local", Kind = "folder", Address = @"C:\Data", Label = "local" },
                new TrafficLightTarget { Id = "usb", Kind = "drive", Address = "E:", Label = "usb" });
            foreach (string id in new[] { "net", "share", "local", "usb" }) Run(engine, id, ProbeOutcome.Good(1));
            clock.Advance(61);

            engine.SetOffline(true);

            Assert.Equal(new[] { "local", "usb" }, engine.CollectDue().Select(t => t.Id).ToArray());
            TrafficLightTile paused = Tile(engine, "net");
            Assert.True(paused.PausedOffline);
            Assert.True(paused.Stale);
            Assert.Equal(TrafficState.Ok, paused.State); // not red: the machine is offline, not the target down

            engine.SetOffline(false); // back online: a full re-check
            Assert.Equal(4, engine.CollectDue().Count);
        }

        [Fact]
        public void ADisabledOrInvalidTargetIsNeverDispatched()
        {
            var clock = new Clock();
            var off = Target("off");
            off.Enabled = false;
            TrafficLightEngine engine = Engine(clock, off, Target("bad", "tcp", "no-port"));

            Assert.Empty(engine.CollectDue());
            Assert.True(Tile(engine, "bad").InvalidAddress);
            Assert.True(Tile(engine, "off").Disabled);
        }

        [Fact]
        public void AResultForATargetEditedWhileItRanIsDropped()
        {
            var clock = new Clock();
            TrafficLightTarget original = Target("a", address: "10.0.0.1");
            TrafficLightEngine engine = Engine(clock, original);
            int generation = engine.MarkDispatched("a");

            TrafficLightTarget edited = original.Clone();
            edited.Address = "10.0.0.2";
            engine.SetTargets(new[] { edited });

            Assert.Null(engine.Complete("a", generation, ProbeOutcome.Good(5)));
            Assert.Equal(TrafficState.Unknown, Tile(engine, "a").State);
        }

        [Fact]
        public void AnEditThatKeepsTheIdentityKeepsTheResult()
        {
            var clock = new Clock();
            TrafficLightTarget original = Target("a");
            TrafficLightEngine engine = Engine(clock, original);
            Run(engine, "a", ProbeOutcome.Good(5));

            TrafficLightTarget renamed = original.Clone();
            renamed.Label = "Renamed";
            renamed.IntervalSeconds = 5;
            engine.SetTargets(new[] { renamed });

            Assert.Equal(TrafficState.Ok, Tile(engine, "a").State);
            Assert.Equal("Renamed", Tile(engine, "a").Target.Label);
        }

        [Fact]
        public void ATargetEditedWhileItsCheckWasOutIsCheckedAgainAndTheOldReleaseDoesNotFreeTheNewSlot()
        {
            var clock = new Clock();
            TrafficLightTarget original = Target("a", address: "10.0.0.1");
            TrafficLightEngine engine = Engine(clock, original);
            int oldGeneration = engine.MarkDispatched("a");     // a dead host: the check hangs for its whole limit

            TrafficLightTarget typoFixed = original.Clone();
            typoFixed.Address = "10.0.0.2";
            engine.SetTargets(new[] { typoFixed });

            // The new identity is due and can be dispatched - it used to stay "checking" for ever.
            Assert.Equal(new[] { "a" }, engine.CollectDue().Select(t => t.Id).ToArray());
            int newGeneration = engine.MarkDispatched("a");
            Assert.True(newGeneration > oldGeneration);

            // The old call coming back must not free the slot of the new one.
            engine.Release("a", oldGeneration);
            Assert.True(Tile(engine, "a").Checking);
            Assert.Equal(-1, engine.MarkDispatched("a"));
            Assert.Null(engine.Complete("a", oldGeneration, ProbeOutcome.Good(1)));

            Assert.NotNull(engine.Complete("a", newGeneration, ProbeOutcome.Good(1)));
            engine.Release("a", newGeneration);
            Assert.False(Tile(engine, "a").Checking);
        }

        [Fact]
        public void APersonsCheckEverythingLeavesWhatIsRunningAloneButANetworkChangeDoesNot()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"), Target("b"));
            Run(engine, "a", ProbeOutcome.Good(1));
            Run(engine, "b", ProbeOutcome.Good(1));
            int running = engine.MarkDispatched("a");

            engine.RequestAll(includeRunning: false);            // "Check everything now"
            engine.Release("a", running);
            engine.Complete("a", running, ProbeOutcome.Good(1));
            Assert.Equal(new[] { "b" }, engine.CollectDue().Select(t => t.Id).ToArray());   // a is not run a second time

            Run(engine, "b", ProbeOutcome.Good(1));
            int again = engine.MarkDispatched("a");
            engine.RequestAll();                                 // the network changed under it
            engine.Complete("a", again, ProbeOutcome.Good(1));
            engine.Release("a", again);
            Assert.Equal(new[] { "a", "b" }, engine.CollectDue().Select(t => t.Id).ToArray());
        }

        [Fact]
        public void AMappedDriveIsPausedOfflineLikeAShareAndALocalOneKeepsBeingChecked()
        {
            var clock = new Clock();
            var engine = new TrafficLightEngine(() => clock.Now, path => path == "Z:");
            engine.SetTargets(new[]
            {
                Target("mapped", "drive", "Z:"), Target("local", "drive", "C:"), Target("usb", "drive", "E:"),
            });
            engine.SetOffline(true);

            Assert.Equal(new[] { "local", "usb" }, engine.CollectDue().Select(t => t.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray());
            Assert.True(Tile(engine, "mapped").PausedOffline);
            Assert.False(Tile(engine, "local").PausedOffline);
        }

        // ---- notifications ----

        private static TrafficLightChange Change(string label, TrafficState from, TrafficState to, bool notify = true,
            bool baseline = false, bool offline = false)
            => new TrafficLightChange
            {
                Target = new TrafficLightTarget { Id = label, Label = label, Address = "secret.internal.example", Notify = notify },
                From = from, To = to, IsBaseline = baseline, WhileOffline = offline,
            };

        [Fact]
        public void OnlyASettledChangeIntoOrOutOfDownNotifies()
        {
            Assert.True(TrafficLightNotifier.Counts(Change("a", TrafficState.Ok, TrafficState.Down)));
            Assert.True(TrafficLightNotifier.Counts(Change("a", TrafficState.Degraded, TrafficState.Down)));
            Assert.True(TrafficLightNotifier.Counts(Change("a", TrafficState.Down, TrafficState.Ok)));
            Assert.True(TrafficLightNotifier.Counts(Change("a", TrafficState.Down, TrafficState.Degraded)));

            Assert.False(TrafficLightNotifier.Counts(Change("a", TrafficState.Ok, TrafficState.Degraded)));
            Assert.False(TrafficLightNotifier.Counts(Change("a", TrafficState.Degraded, TrafficState.Ok)));
            Assert.False(TrafficLightNotifier.Counts(Change("a", TrafficState.Unknown, TrafficState.Down, baseline: true)));
            Assert.False(TrafficLightNotifier.Counts(Change("a", TrafficState.Ok, TrafficState.Down, notify: false)));
            Assert.False(TrafficLightNotifier.Counts(Change("a", TrafficState.Ok, TrafficState.Down, offline: true)));
        }

        [Fact]
        public void BalloonsAreCoalescedNamedByLabelAndCappedAtThreeNames()
        {
            var notifier = new TrafficLightNotifier();
            var at = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            foreach (string label in new[] { "One", "Two", "Three", "Four", "Five" })
                notifier.Add(Change(label, TrafficState.Ok, TrafficState.Down));
            notifier.Add(Change("Back", TrafficState.Down, TrafficState.Ok));

            TrafficLightBalloon? balloon = notifier.TryTake(at);

            Assert.NotNull(balloon);
            Assert.Equal(new[] { "One", "Two", "Three" }, balloon!.DownLabels.ToArray());
            Assert.Equal(2, balloon.DownMore);
            Assert.Equal(new[] { "Back" }, balloon.UpLabels.ToArray());
            Assert.DoesNotContain("secret", string.Join("|", balloon.DownLabels.Concat(balloon.UpLabels))); // labels, never addresses
            Assert.Null(notifier.TryTake(at.AddSeconds(100))); // nothing pending any more
        }

        [Fact]
        public void AtMostOneBalloonPerFifteenSeconds()
        {
            var notifier = new TrafficLightNotifier();
            var at = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            notifier.Add(Change("a", TrafficState.Ok, TrafficState.Down));
            Assert.NotNull(notifier.TryTake(at));

            notifier.Add(Change("b", TrafficState.Ok, TrafficState.Down));
            Assert.Null(notifier.TryTake(at.AddSeconds(14)));
            TrafficLightBalloon? later = notifier.TryTake(at.AddSeconds(15));
            Assert.NotNull(later);
            Assert.Equal(new[] { "b" }, later!.DownLabels.ToArray());
        }

        [Fact]
        public void AFlappingTargetIsOneNameNotTwo()
        {
            var notifier = new TrafficLightNotifier();
            notifier.Add(Change("a", TrafficState.Ok, TrafficState.Down));
            notifier.Add(Change("a", TrafficState.Down, TrafficState.Ok));
            notifier.Add(Change("a", TrafficState.Ok, TrafficState.Down));

            TrafficLightBalloon balloon = notifier.TryTake(DateTime.UtcNow)!;
            Assert.Equal(new[] { "a" }, balloon.DownLabels.ToArray());
            Assert.Empty(balloon.UpLabels);
        }

        [Fact]
        public void TheSummaryCountsEnabledTargetsOnly()
        {
            var clock = new Clock();
            var off = Target("off");
            off.Enabled = false;
            TrafficLightEngine engine = Engine(clock, Target("a"), Target("b"), Target("c"), off);
            Run(engine, "a", ProbeOutcome.Good(1));
            Run(engine, "b", ProbeOutcome.Fail(TrafficReasons.Timeout));

            TrafficLightSummary summary = engine.Summary();
            Assert.Equal(3, summary.Total);
            Assert.Equal(1, summary.Down);
            Assert.Equal(1, summary.Unknown);
        }
    }
}
