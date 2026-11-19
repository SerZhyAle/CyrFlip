using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The engine's and the scheduler's half of <c>trafficlight check</c> (ticket S0117): a command's request for a check is
    /// correlated with that very check, joins one that is already running instead of starting a second, is let go when the
    /// check can never report, and settles a silent change when no one but the command asked. An injected clock and fake
    /// probes - no timer, no socket, no folder.
    /// </summary>
    public class TrafficLightCommandCheckTests
    {
        private sealed class Clock
        {
            public DateTime Now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
            public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
        }

        private static TrafficLightTarget Target(string id, string kind = "ping", string address = "192.168.1.1",
            int interval = 60, bool enabled = true, bool notify = true)
            => new TrafficLightTarget
            {
                Id = id, Label = id, Kind = kind, Address = address, IntervalSeconds = interval, Enabled = enabled, Notify = notify,
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

        /// <summary>The target checked once (a green baseline) and then left alone for a second: nothing is due.</summary>
        private static TrafficLightEngine Idle(Clock clock, params TrafficLightTarget[] targets)
        {
            TrafficLightEngine engine = Engine(clock, targets);
            foreach (TrafficLightTarget target in targets)
                if (target.Enabled && TrafficLightAddress.Validate(target) == AddressIssue.None)
                    Run(engine, target.Id, ProbeOutcome.Good(5));
            clock.Advance(1);
            return engine;
        }

        // ---- correlation ----

        [Fact]
        public void AnEditedTargetCannotDispatchThePreviouslySelectedAddress()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));
            TrafficLightTarget selected = engine.CollectDue().Single();

            engine.SetTargets(new[] { Target("a", address: "192.168.1.2") });

            Assert.Equal(-1, engine.MarkDispatched(selected));
            Assert.True(engine.MarkDispatched(engine.CollectDue().Single()) >= 0);
        }

        [Fact]
        public void AnEditedTargetCannotBeRequestedThroughAnOldSnapshot()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));
            TrafficLightTarget selected = engine.Snapshot().Single().Target;

            engine.SetTargets(new[] { Target("a", address: "192.168.1.2") });

            Assert.Equal(TrafficCheckStart.NotFound, engine.RequestCommandCheck(selected).Start);
        }

        [Fact]
        public void ARequestIsTakenMakesTheTargetDueAndCompletesWithThatVeryCheck()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            Assert.Empty(engine.CollectDue());

            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            Assert.Equal(TrafficCheckStart.Started, ticket.Start);
            Assert.False(ticket.Completion.IsCompleted);
            Assert.Single(engine.CollectDue());
            int generation = engine.MarkDispatched("a");
            Assert.False(ticket.Completion.IsCompleted); // dispatched is not done
            engine.Complete("a", generation, ProbeOutcome.Good(3));
            Assert.True(ticket.Completion.IsCompleted);
            Assert.Equal(TrafficCheckOutcome.Completed, ticket.Completion.Result);
        }

        [Fact]
        public void TheTicketCompletesOnlyAfterTheResultIsInTheEngine()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");
            int generation = engine.MarkDispatched("a");
            clock.Advance(2);

            engine.Complete("a", generation, ProbeOutcome.Fail(TrafficReasons.Timeout));

            Assert.True(ticket.Completion.IsCompleted);
            TrafficLightTile tile = engine.Snapshot().Single();
            Assert.Equal(TrafficState.Down, tile.State);
            Assert.Equal(clock.Now, tile.CheckedAtUtc);
        }

        [Fact]
        public void ACheckAlreadyRunningIsNotStartedASecondTimeAndTheCommandWaitsForIt()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            clock.Advance(61); // due by its own interval
            Assert.Single(engine.CollectDue());
            int generation = engine.MarkDispatched("a"); // the scheduler's own check is out

            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            Assert.Equal(TrafficCheckStart.Started, ticket.Start);
            Assert.Empty(engine.CollectDue());
            Assert.Equal(-1, engine.MarkDispatched("a"));
            Assert.False(ticket.Completion.IsCompleted);
            TrafficLightChange? change = engine.Complete("a", generation, ProbeOutcome.Fail(TrafficReasons.Timeout));
            Assert.True(ticket.Completion.IsCompleted);
            Assert.Equal(TrafficCheckOutcome.Completed, ticket.Completion.Result);
            // It was not the command's check: its change is the board's, balloon and all.
            Assert.NotNull(change);
            Assert.False(change!.Silent);
        }

        [Fact]
        public void TwoCommandsWaitingForOneCheckBothHearOfIt()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            TrafficCheckTicket first = engine.RequestCommandCheck("a");
            TrafficCheckTicket second = engine.RequestCommandCheck("a");

            Assert.Single(engine.CollectDue());
            Run(engine, "a", ProbeOutcome.Good(1));

            Assert.Equal(TrafficCheckOutcome.Completed, first.Completion.Result);
            Assert.Equal(TrafficCheckOutcome.Completed, second.Completion.Result);
        }

        [Fact]
        public void ACheckAfterTheOneCommandsWaitedForIsNotTheirs()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");
            Run(engine, "a", ProbeOutcome.Good(1));
            Assert.True(ticket.Completion.IsCompleted);

            TrafficCheckTicket again = engine.RequestCommandCheck("a");

            Assert.False(again.Completion.IsCompleted);
            Run(engine, "a", ProbeOutcome.Good(1));
            Assert.True(again.Completion.IsCompleted);
        }

        [Fact]
        public void ACheckThatHasReportedButWhoseCallIsStillOutIsNotJoinedAndTheReturnFreesTheSlot()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            int generation = engine.MarkDispatched("a");
            // The hard timeout: the check is applied at once while the call, and so the slot, is still out there.
            engine.Complete("a", generation, ProbeOutcome.Fail(TrafficReasons.Timeout));

            TrafficCheckTicket held = engine.RequestCommandCheck("a");

            Assert.Equal(TrafficCheckStart.Busy, held.Start);
            Assert.Equal(TrafficCheckOutcome.Abandoned, held.Completion.Result);
            Assert.Empty(engine.CollectDue());
            engine.Release("a", generation); // the call came back
            TrafficCheckTicket next = engine.RequestCommandCheck("a");
            Assert.Equal(TrafficCheckStart.Started, next.Start);
            Assert.False(next.Completion.IsCompleted);
        }

        // ---- what is never probed ----

        [Fact]
        public void ASwitchedOffTargetIsRefusedAndNothingIsMarkedDue()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a", enabled: false));

            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            Assert.Equal(TrafficCheckStart.Disabled, ticket.Start);
            Assert.Empty(engine.CollectDue());
        }

        [Fact]
        public void AnAddressThatDoesNotValidateIsRefusedAndNothingIsMarkedDue()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a", address: "not a host with spaces"));

            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            Assert.Equal(TrafficCheckStart.InvalidAddress, ticket.Start);
            Assert.Empty(engine.CollectDue());
        }

        [Fact]
        public void AnUnknownIdIsRefused()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Engine(clock, Target("a"));

            Assert.Equal(TrafficCheckStart.NotFound, engine.RequestCommandCheck("zzz").Start);
        }

        [Fact]
        public void WhileOfflineANetworkTargetIsRefusedAndALocalFolderIsNot()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("net"), Target("local", "folder", @"C:\Data"));
            engine.SetOffline(true);

            Assert.Equal(TrafficCheckStart.Offline, engine.RequestCommandCheck("net").Start);
            Assert.Equal(TrafficCheckStart.Started, engine.RequestCommandCheck("local").Start);
        }

        // ---- being let go ----

        [Fact]
        public void GoingOfflineLetsGoTheNetworkTargetsStillWaitingForTheirTurnButNotARunningCheck()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("waiting"), Target("running"));
            TrafficCheckTicket waiting = engine.RequestCommandCheck("waiting");
            int generation = engine.MarkDispatched("running");
            TrafficCheckTicket running = engine.RequestCommandCheck("running");

            engine.SetOffline(true);

            Assert.Equal(TrafficCheckOutcome.Abandoned, waiting.Completion.Result);
            Assert.False(running.Completion.IsCompleted);
            engine.Complete("running", generation, ProbeOutcome.Good(1));
            Assert.Equal(TrafficCheckOutcome.Completed, running.Completion.Result);
        }

        [Fact]
        public void EditingTheAddressOfATargetLetsGoTheCheckThatWasAboutIt()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            int generation = engine.MarkDispatched("a");
            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            engine.SetTargets(new[] { Target("a", address: "10.9.9.9") });

            Assert.Equal(TrafficCheckOutcome.Abandoned, ticket.Completion.Result);
            // The old answer, when it comes, is about something else and is dropped.
            Assert.Null(engine.Complete("a", generation, ProbeOutcome.Fail(TrafficReasons.Timeout)));
            Assert.Equal(TrafficState.Unknown, engine.Snapshot().Single().State);
        }

        [Fact]
        public void RemovingOrSwitchingOffATargetLetsGoWhatWaitedForIt()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("gone"), Target("off"), Target("kept"));
            TrafficCheckTicket gone = engine.RequestCommandCheck("gone");
            TrafficCheckTicket off = engine.RequestCommandCheck("off");
            TrafficCheckTicket kept = engine.RequestCommandCheck("kept");

            engine.SetTargets(new[] { Target("off", enabled: false), Target("kept") });

            Assert.Equal(TrafficCheckOutcome.Abandoned, gone.Completion.Result);
            Assert.Equal(TrafficCheckOutcome.Abandoned, off.Completion.Result);
            Assert.False(kept.Completion.IsCompleted);
        }

        [Fact]
        public void AReleaseWithNoResultLetsGoTheCommandsOfThatFlight()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            int generation = engine.MarkDispatched("a");
            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            engine.Release("a", generation); // the scheduler was stopped between the dispatch and the probe

            Assert.Equal(TrafficCheckOutcome.Abandoned, ticket.Completion.Result);
        }

        [Fact]
        public void AbandonCommandChecksLetsGoEveryWait()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"), Target("b"));
            TrafficCheckTicket a = engine.RequestCommandCheck("a");
            engine.MarkDispatched("b");
            TrafficCheckTicket b = engine.RequestCommandCheck("b");

            engine.AbandonCommandChecks();

            Assert.Equal(TrafficCheckOutcome.Abandoned, a.Completion.Result);
            Assert.Equal(TrafficCheckOutcome.Abandoned, b.Completion.Result);
        }

        [Fact]
        public void CancellingATicketDropsTheWaitButNotTheCheck()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            TrafficCheckTicket ticket = engine.RequestCommandCheck("a");

            ticket.Cancel();

            Assert.Single(engine.CollectDue()); // asked for once, it still runs
            Run(engine, "a", ProbeOutcome.Good(1));
            Assert.False(ticket.Completion.IsCompleted);
        }

        // ---- silence ----

        [Fact]
        public void ACheckOnlyACommandAskedForSettlesASilentChange()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            engine.RequestCommandCheck("a");

            TrafficLightChange? change = Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));

            Assert.NotNull(change);
            Assert.Equal(TrafficState.Down, change!.To);
            Assert.True(change.Silent);
            Assert.False(TrafficLightNotifier.Counts(change)); // the target asks for balloons, the command's check raises none
        }

        [Fact]
        public void TheSameChangeFromTheBoardsOwnCheckStillRaisesItsBalloon()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            engine.RequestNow("a");

            TrafficLightChange? change = Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));

            Assert.NotNull(change);
            Assert.False(change!.Silent);
            Assert.True(TrafficLightNotifier.Counts(change));
        }

        [Fact]
        public void ATileClickBeforeOrAfterTheCommandMakesTheCheckTheBoardsAsWell()
        {
            var clock = new Clock();
            TrafficLightEngine before = Idle(clock, Target("a"));
            before.RequestNow("a");
            before.RequestCommandCheck("a");
            TrafficLightEngine after = Idle(clock, Target("a"));
            after.RequestCommandCheck("a");
            after.RequestNow("a");

            Assert.False(Run(before, "a", ProbeOutcome.Fail(TrafficReasons.Timeout))!.Silent);
            Assert.False(Run(after, "a", ProbeOutcome.Fail(TrafficReasons.Timeout))!.Silent);
        }

        [Fact]
        public void ATargetThatWasDueByItsIntervalAnywayIsNotSilent()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            clock.Advance(61);
            engine.RequestCommandCheck("a");

            Assert.False(Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout))!.Silent);
        }

        [Fact]
        public void SilenceEndsWithTheCheckItBelongedTo()
        {
            var clock = new Clock();
            TrafficLightEngine engine = Idle(clock, Target("a"));
            engine.RequestCommandCheck("a");
            Run(engine, "a", ProbeOutcome.Fail(TrafficReasons.Timeout));
            clock.Advance(61);

            TrafficLightChange? next = Run(engine, "a", ProbeOutcome.Good(1));

            Assert.NotNull(next);
            Assert.False(next!.Silent);
        }

        // ---- the scheduler's half ----

        private sealed class SwitchableProbe : IAsyncTrafficProbe
        {
            public volatile bool Fail;
            public Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
                => Task.FromResult(Fail ? ProbeOutcome.Fail(TrafficReasons.Timeout) : ProbeOutcome.Good(1));
        }

        private static TrafficLightProbeSet Probes(IAsyncTrafficProbe ping)
            => new TrafficLightProbeSet(new Dictionary<string, IAsyncTrafficProbe> { [TrafficLightKinds.Ping] = ping }, null);

        private static bool WaitFor(Func<bool> condition, int ms = 5000)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        [Fact]
        public void TheSchedulerRunsACommandsCheckThroughItsOwnPathAndTheTicketCompletes()
        {
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Target("a") });
            using var scheduler = new TrafficLightScheduler(engine, Probes(new SwitchableProbe()), () => true);

            TrafficCheckTicket ticket = scheduler.RequestCommandCheck("a");
            scheduler.Tick();

            Assert.True(ticket.Completion.Wait(5000));
            Assert.Equal(TrafficCheckOutcome.Completed, ticket.Completion.Result);
            Assert.Equal(TrafficState.Ok, engine.Snapshot().Single().State);
        }

        [Fact]
        public void ACommandsCheckThroughTheSchedulerRaisesAChangeMarkedSilent()
        {
            var probe = new SwitchableProbe();
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Target("a") });
            using var scheduler = new TrafficLightScheduler(engine, Probes(probe), () => true);
            var changes = new List<TrafficLightChange>();
            scheduler.StateChanged += change => { lock (changes) changes.Add(change); };

            scheduler.Tick(); // the board's own first check: a baseline
            Assert.True(WaitFor(() =>
            {
                TrafficLightTile tile = engine.Snapshot().Single();
                return tile.CheckedAtUtc.HasValue && !tile.Checking;
            }));
            probe.Fail = true;
            TrafficCheckTicket ticket = scheduler.RequestCommandCheck("a");
            scheduler.Tick();

            Assert.True(ticket.Completion.Wait(5000));
            Assert.True(WaitFor(() => { lock (changes) return changes.Count == 2; }));
            lock (changes)
            {
                Assert.False(changes[0].Silent); // the baseline, not the command's
                Assert.Equal(TrafficState.Down, changes[1].To);
                Assert.True(changes[1].Silent);
            }
        }

        [Fact]
        public void AStoppedSchedulerLetsGoWhatWaitedForATurnThatWillNotCome()
        {
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Target("a") });
            var scheduler = new TrafficLightScheduler(engine, Probes(new SwitchableProbe()), () => true);
            TrafficCheckTicket ticket = scheduler.RequestCommandCheck("a");

            scheduler.Stop();

            Assert.Equal(TrafficCheckOutcome.Abandoned, ticket.Completion.Result);
            scheduler.Dispose();
        }
    }
}
