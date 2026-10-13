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
    /// The scheduler's promises (ticket S0069 D22 and section 8): bounded probe concurrency, a staggered cold
    /// start, and a hung blocking call that is abandoned at its timeout without freeing its slot. Probes are
    /// fakes - no test pings a host, opens a socket or touches a folder.
    /// </summary>
    public class TrafficLightSchedulerTests
    {
        private sealed class HeldAsyncProbe : IAsyncTrafficProbe
        {
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _started;
            public int Started => Volatile.Read(ref _started);
            public void ReleaseAll() => _release.TrySetResult(true);

            public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
            {
                Interlocked.Increment(ref _started);
                await _release.Task.ConfigureAwait(false);
                return ProbeOutcome.Good(1);
            }
        }

        private sealed class DeafAsyncProbe : IAsyncTrafficProbe
        {
            public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
            {
                await Task.Delay(5000).ConfigureAwait(false); // ignores the token on purpose
                return ProbeOutcome.Good(1);
            }
        }

        private sealed class HungBlockingProbe : IBlockingTrafficProbe
        {
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
            private int _running, _peak, _entered;
            public int Peak => Volatile.Read(ref _peak);
            public int Entered => Volatile.Read(ref _entered);
            public void ReleaseAll() => _release.Set();

            public ProbeOutcome Run(TrafficLightTarget target)
            {
                Interlocked.Increment(ref _entered);
                int now = Interlocked.Increment(ref _running);
                int peak;
                while ((peak = Volatile.Read(ref _peak)) < now && Interlocked.CompareExchange(ref _peak, now, peak) != peak) { }
                _release.Wait();
                Interlocked.Decrement(ref _running);
                return ProbeOutcome.Good(1);
            }
        }

        private static TrafficLightTarget Ping(int n, int timeoutMs = 5000)
            => new TrafficLightTarget { Id = "p" + n, Label = "p" + n, Kind = "ping", Address = "10.0.0." + (n % 250 + 1), TimeoutMs = timeoutMs };

        private static TrafficLightTarget Folder(int n, int timeoutMs = 5000)
            => new TrafficLightTarget { Id = "f" + n, Label = "f" + n, Kind = "folder", Address = @"C:\Data" + n, TimeoutMs = timeoutMs };

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

        private static TrafficLightProbeSet Set(IAsyncTrafficProbe? ping, IBlockingTrafficProbe? folder = null)
        {
            var async = new Dictionary<string, IAsyncTrafficProbe>();
            var blocking = new Dictionary<string, IBlockingTrafficProbe>();
            if (ping != null) async[TrafficLightKinds.Ping] = ping;
            if (folder != null) blocking[TrafficLightKinds.Folder] = folder;
            return new TrafficLightProbeSet(async, blocking);
        }

        [Fact]
        public void TwentyTargetsStartSixteenAtOnceAndTheRestOnTheNextTicks()
        {
            var probe = new HeldAsyncProbe();
            var engine = new TrafficLightEngine();
            engine.SetTargets(Enumerable.Range(1, 20).Select(i => Ping(i)));
            using var scheduler = new TrafficLightScheduler(engine, Set(probe), () => true);

            scheduler.Tick();
            Assert.True(WaitFor(() => probe.Started == TrafficLightScheduler.MaxAsyncInFlight));
            Thread.Sleep(100);
            Assert.Equal(16, probe.Started);
            Assert.Equal(16, scheduler.AsyncInFlight);

            scheduler.Tick(); // still full: nothing more is started
            Thread.Sleep(50);
            Assert.Equal(16, probe.Started);

            probe.ReleaseAll();
            Assert.True(WaitFor(() => scheduler.AsyncInFlight == 0));
            scheduler.Tick();
            Assert.True(WaitFor(() => probe.Started == 20)); // the other four, one tick later
        }

        [Fact]
        public void AnAsyncProbeThatIgnoresItsTokenCompletesAsATimeoutAtTheHardLimit()
        {
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Ping(1, timeoutMs: 150) });
            using var scheduler = new TrafficLightScheduler(engine, Set(new DeafAsyncProbe()), () => true);

            scheduler.Tick();

            Assert.True(WaitFor(() => engine.Snapshot()[0].State == TrafficState.Down));
            TrafficLightTile tile = engine.Snapshot()[0];
            Assert.Equal(TrafficReasons.Timeout, tile.Reason);
            Assert.True(tile.Checking); // the call has not returned: the slot is still held
        }

        [Fact]
        public void ABlockingCallThatNeverReturnsIsAbandonedButKeepsItsSlot()
        {
            var probe = new HungBlockingProbe();
            var engine = new TrafficLightEngine((() => DateTime.UtcNow), p => false);
            engine.SetTargets(new[] { Folder(1, timeoutMs: 150) });
            using var scheduler = new TrafficLightScheduler(engine, Set(null, probe), () => true);
            scheduler.Tick();

            Assert.True(WaitFor(() => engine.Snapshot()[0].State == TrafficState.Down), "the check completed as a timeout");
            Assert.Equal(TrafficReasons.Timeout, engine.Snapshot()[0].Reason);
            Assert.True(engine.Snapshot()[0].Checking);

            // not dispatched again while the call is still out there
            engine.RequestAll();
            Assert.Empty(engine.CollectDue());
            scheduler.Tick();
            Thread.Sleep(100);
            Assert.Equal(1, probe.Entered);

            probe.ReleaseAll();
            Assert.True(WaitFor(() => !engine.Snapshot()[0].Checking), "the slot is released when the call returns");
            engine.RequestAll();
            Assert.Single(engine.CollectDue());
        }

        [Fact]
        public void HungBlockingCallsStarveOnlyTheFourBlockingWorkers()
        {
            var folder = new HungBlockingProbe();
            var ping = new HeldAsyncProbe();
            var engine = new TrafficLightEngine((() => DateTime.UtcNow), p => false);
            engine.SetTargets(Enumerable.Range(1, 6).Select(i => Folder(i, timeoutMs: 100)).Concat(new[] { Ping(1) }));
            using var scheduler = new TrafficLightScheduler(engine, Set(ping, folder), () => true);
            scheduler.Start();
            try
            {
                // four hung calls run; the other two wait for a worker; the ping is not held up by any of them
                Assert.True(WaitFor(() => folder.Entered == TrafficLightScheduler.BlockingWorkers));
                Thread.Sleep(150);
                Assert.Equal(4, folder.Entered);
                Assert.Equal(4, folder.Peak);
                Assert.True(WaitFor(() => ping.Started == 1), "the network probe still ran");
            }
            finally
            {
                folder.ReleaseAll();
                ping.ReleaseAll();
                scheduler.Stop();
            }
        }

        [Fact]
        public void BlockingChecksStillQueuedWhenTheModuleStopsAreNeverStarted()
        {
            var folder = new HungBlockingProbe();
            var engine = new TrafficLightEngine((() => DateTime.UtcNow), p => false);
            engine.SetTargets(Enumerable.Range(1, 9).Select(i => Folder(i, timeoutMs: 100)));
            var scheduler = new TrafficLightScheduler(engine, Set(null, folder), () => true);
            scheduler.Start();
            try
            {
                Assert.True(WaitFor(() => folder.Entered == TrafficLightScheduler.BlockingWorkers));
                scheduler.Dispose();          // the module is switched off with five checks still queued
                folder.ReleaseAll();
                Thread.Sleep(300);
                Assert.Equal(TrafficLightScheduler.BlockingWorkers, folder.Entered);
            }
            finally { folder.ReleaseAll(); scheduler.Dispose(); }
        }

        [Fact]
        public void APersonsCheckEverythingDoesNotRunAnAlreadyRunningCheckAgainBehindItself()
        {
            var probe = new HeldAsyncProbe();
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Ping(1) });
            using var scheduler = new TrafficLightScheduler(engine, Set(probe), () => true);

            scheduler.Tick();
            Assert.True(WaitFor(() => probe.Started == 1));
            scheduler.RequestAll();              // F5 while the first check is still out there
            probe.ReleaseAll();
            Assert.True(WaitFor(() => !engine.Snapshot()[0].Checking));

            Assert.Empty(engine.CollectDue());   // fresh already: nothing is owed
        }

        [Fact]
        public void AResultReachesTheSubscribersAndTheEngine()
        {
            var probe = new HeldAsyncProbe();
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Ping(1) });
            using var scheduler = new TrafficLightScheduler(engine, Set(probe), () => true);
            TrafficLightChange? seen = null;
            int results = 0;
            scheduler.StateChanged += c => seen = c;
            scheduler.ResultsChanged += () => Interlocked.Increment(ref results);

            scheduler.Tick();
            probe.ReleaseAll();

            Assert.True(WaitFor(() => seen != null));
            Assert.Equal(TrafficState.Ok, seen!.To);
            Assert.True(seen.IsBaseline);
            Assert.True(results >= 1);
        }

        [Fact]
        public void OfflineTargetsAreNotDispatchedAndNoTimerRunsBeforeStart()
        {
            var probe = new HeldAsyncProbe();
            var engine = new TrafficLightEngine();
            engine.SetTargets(new[] { Ping(1) });
            engine.SetOffline(true);
            using var scheduler = new TrafficLightScheduler(engine, Set(probe), () => false);

            scheduler.Tick();
            Thread.Sleep(100);

            Assert.Equal(0, probe.Started);
            Assert.Equal(0, scheduler.AsyncInFlight);
        }
    }
}
