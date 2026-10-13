using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// The runtime of the traffic light (ticket S0069 sections 5 and 8): <b>one</b> timer for the whole feature,
    /// a bounded set of workers, and nothing per target. A 1-second tick asks the engine which targets are due
    /// and dispatches them in <c>ordinal</c> order:
    /// <list type="bullet">
    /// <item><description>network probes are asynchronous and capped at <see cref="MaxAsyncInFlight"/> in flight -
    /// a due target beyond the cap simply waits for the next tick, which is what staggers a cold start;</description></item>
    /// <item><description>folder and drive probes are synchronous calls that cannot be cancelled, so they run on
    /// <see cref="BlockingWorkers"/> dedicated threads, never the shared pool. The hard timeout <b>abandons</b>
    /// the call: the check completes as <c>timeout</c> at once, while the worker and the target's slot stay
    /// occupied until the call really returns (D22).</description></item>
    /// </list>
    /// Nothing here touches the UI: results are reported through events on whichever thread finished them, and
    /// the subscriber posts to its own.
    /// </summary>
    internal sealed class TrafficLightScheduler : IDisposable
    {
        public const int MaxAsyncInFlight = 16;
        public const int BlockingWorkers = 4;
        /// <summary>How long an abandoned asynchronous call may still hold its slot before it is let go anyway.</summary>
        private static readonly TimeSpan AbandonedGrace = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan NetworkSettle = TimeSpan.FromSeconds(2);

        private readonly TrafficLightEngine _engine;
        private readonly TrafficLightProbeSet _probes;
        private readonly Func<bool> _isOnline;
        private readonly object _gate = new object();
        private BlockingCollection<Action>? _blocking;
        private Thread[]? _workers;
        private Timer? _tick;
        private Timer? _settle;
        private int _asyncInFlight;
        private int _ticking;
        private bool _started;
        private bool _disposed;
        // Set by Stop: queued blocking checks that have not begun are dropped instead of run.
        private volatile bool _retired;
        private bool _lastOnline = true;

        public TrafficLightScheduler(TrafficLightEngine engine, TrafficLightProbeSet probes, Func<bool>? isOnline = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _probes = probes ?? throw new ArgumentNullException(nameof(probes));
            _isOnline = isOnline ?? NetworkAvailable;
        }

        /// <summary>One completed check was applied (a repaint may be due). Raised on the finishing thread.</summary>
        public event Action? ResultsChanged;
        /// <summary>A light changed for good (a failure streak confirmed, a recovery). Raised on the finishing thread.</summary>
        public event Action<TrafficLightChange>? StateChanged;
        /// <summary>The computer went offline (true) or came back (false). Raised on the thread that noticed.</summary>
        public event Action<bool>? OfflineChanged;

        public int AsyncInFlight => Volatile.Read(ref _asyncInFlight);

        private static bool NetworkAvailable()
        {
            try { return NetworkInterface.GetIsNetworkAvailable(); }
            catch { return true; } // unknown is "try the probes", never "pretend we are offline"
        }

        // ---- lifetime ----

        /// <summary>
        /// Starts the tick and the event subscriptions, and makes every target due (start, D17). The
        /// subscriptions exist only between <see cref="Start"/> and <see cref="Stop"/>.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_started || _disposed) return;
                _started = true;
                _retired = false;

                _lastOnline = _isOnline();
                _engine.SetOffline(!_lastOnline);
                _engine.RequestAll();

                NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                _settle = new Timer(_ => OnSettled(), null, Timeout.Infinite, Timeout.Infinite);
                _tick = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>Stops the tick and drops the subscriptions. Calls still in flight finish and are discarded.</summary>
        public void Stop()
        {
            Timer? tick, settle;
            BlockingCollection<Action>? queue;
            // Whatever is still queued behind the four workers is never started: a module switched off must not
            // go on touching folders and drives for as long as the queue is long.
            _retired = true;
            lock (_gate)
            {
                // Also without a Start: a scheduler driven by Tick alone may still own blocking workers.
                if (_started)
                {
                    _started = false;
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
                    NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                }
                tick = _tick; _tick = null;
                settle = _settle; _settle = null;
                queue = _blocking; _blocking = null;
                _workers = null;
            }
            tick?.Dispose();
            settle?.Dispose();
            try { queue?.CompleteAdding(); } catch (Exception ex) { TrafficLightLog.Fault("queue-complete", ex); }
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
        }

        // ---- requests ----

        /// <summary>"Check now" on one target; ignored while one is running. Runs without waiting for the tick.</summary>
        public bool RequestCheck(string id)
        {
            bool accepted = _engine.RequestNow(id);
            if (accepted) KickTick();
            return accepted;
        }

        /// <summary>"Check all now": every target is due at once, then paced by the caps.</summary>
        public void RequestAll()
        {
            // A person asked: what is already running is not run a second time behind itself.
            _engine.RequestAll(includeRunning: false);
            KickTick();
        }

        private void KickTick()
        {
            if (_started) ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        // ---- the tick ----

        /// <summary>
        /// One pass: dispatches what is due, within the caps. Public for the tests, which drive the scheduler
        /// without a timer. A pass that is still running makes the next one return at once.
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return;
            try
            {
                List<TrafficLightTarget> due = _engine.CollectDue();
                foreach (TrafficLightTarget target in due)
                {
                    bool blocking = _probes.IsBlocking(target.Kind);
                    // Over the cap: leave it due. The next tick tries again, which is the stagger.
                    if (!blocking && Volatile.Read(ref _asyncInFlight) >= MaxAsyncInFlight) continue;

                    int generation = _engine.MarkDispatched(target.Id);
                    if (generation < 0) continue;
                    if (blocking) EnqueueBlocking(target, generation);
                    else StartAsync(target, generation);
                }
            }
            catch (Exception ex) { TrafficLightLog.Fault("tick", ex); } // retried by the next tick; the timer must never die
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        // ---- asynchronous probes ----

        private void StartAsync(TrafficLightTarget target, int generation)
        {
            Interlocked.Increment(ref _asyncInFlight);
            Task.Run(() => RunAsyncProbe(target, generation));
        }

        private async Task RunAsyncProbe(TrafficLightTarget target, int generation)
        {
            var cts = new CancellationTokenSource();
            Task<ProbeOutcome>? probeTask = null;
            ProbeOutcome outcome;
            try
            {
                IAsyncTrafficProbe? probe = _probes.Async(target.Kind);
                if (probe == null)
                {
                    outcome = ProbeOutcome.Fail(TrafficReasons.Failed);
                }
                else
                {
                    probeTask = Task.Run(() => probe.RunAsync(target, cts.Token));
                    Task winner = await Task.WhenAny(probeTask, Task.Delay(target.EffectiveTimeoutMs)).ConfigureAwait(false);
                    if (winner == probeTask)
                    {
                        outcome = await probeTask.ConfigureAwait(false);
                    }
                    else
                    {
                        // The hard timeout: the check completes now. Cancelling closes the socket (on the pool),
                        // which is what lets the abandoned call end.
                        cts.Cancel();
                        outcome = ProbeOutcome.Fail(TrafficReasons.Timeout);
                    }
                }
            }
            catch
            {
                outcome = ProbeOutcome.Fail(TrafficReasons.Failed);
            }

            Publish(target.Id, generation, outcome);

            // The slot is held until the call really returned - bounded, so a probe that never comes back
            // cannot keep its target from ever being checked again.
            if (probeTask != null && !probeTask.IsCompleted)
            {
                try { await Task.WhenAny(probeTask, Task.Delay(AbandonedGrace)).ConfigureAwait(false); }
                catch (Exception ex) { TrafficLightLog.Fault("probe-abandoned", ex); }
            }
            try { cts.Dispose(); } catch (Exception ex) { TrafficLightLog.Fault("cts-dispose", ex); }
            _engine.Release(target.Id, generation);
            Interlocked.Decrement(ref _asyncInFlight);
        }

        // ---- blocking probes ----

        private void EnqueueBlocking(TrafficLightTarget target, int generation)
        {
            BlockingCollection<Action>? queue = EnsureWorkers();
            if (queue == null)
            {
                _engine.Release(target.Id, generation); // stopped between the pass and here
                return;
            }
            try { queue.Add(() => RunBlockingProbe(target, generation)); }
            catch (InvalidOperationException) { _engine.Release(target.Id, generation); }
        }

        // The four threads exist only once a folder or a drive is watched at all - a board of websites and
        // hosts never pays for them - and go away with Stop.
        private BlockingCollection<Action>? EnsureWorkers()
        {
            lock (_gate)
            {
                if (_disposed) return null;
                if (_blocking != null) return _blocking;
                BlockingCollection<Action> queue = new BlockingCollection<Action>();
                _blocking = queue;
                _workers = new Thread[BlockingWorkers];
                for (int i = 0; i < _workers.Length; i++)
                {
                    var worker = new Thread(() => Work(queue)) { IsBackground = true, Name = "CyrFlip traffic light " + (i + 1) };
                    _workers[i] = worker;
                    worker.Start();
                }
                return queue;
            }
        }

        private static void Work(BlockingCollection<Action> queue)
        {
            try
            {
                foreach (Action job in queue.GetConsumingEnumerable())
                {
                    try { job(); }
                    catch (Exception ex) { TrafficLightLog.Fault("blocking-job", ex); }
                }
            }
            catch (ObjectDisposedException ex) { TrafficLightLog.Fault("worker-stopped", ex); }
        }

        private void RunBlockingProbe(TrafficLightTarget target, int generation)
        {
            if (_retired)
            {
                _engine.Release(target.Id, generation);
                return;
            }
            IBlockingTrafficProbe? probe = _probes.Blocking(target.Kind);
            int completed = 0;
            // The hard timeout starts when the worker starts the call - not when the job was queued behind
            // other hung ones - and abandons the call rather than waiting for it.
            var watchdog = new Timer(_ =>
            {
                if (Interlocked.CompareExchange(ref completed, 1, 0) == 0)
                    Publish(target.Id, generation, ProbeOutcome.Fail(TrafficReasons.Timeout));
            }, null, target.EffectiveTimeoutMs, Timeout.Infinite);

            ProbeOutcome outcome;
            try { outcome = probe == null ? ProbeOutcome.Fail(TrafficReasons.Failed) : probe.Run(target); }
            catch { outcome = ProbeOutcome.Fail(TrafficReasons.Failed); }

            try { watchdog.Dispose(); } catch (Exception ex) { TrafficLightLog.Fault("watchdog-dispose", ex); }
            if (Interlocked.CompareExchange(ref completed, 1, 0) == 0)
                Publish(target.Id, generation, outcome);
            // else: the check was abandoned at the timeout and this late answer is discarded.

            _engine.Release(target.Id, generation);
        }

        // ---- results ----

        private void Publish(string id, int generation, ProbeOutcome outcome)
        {
            TrafficLightChange? change = null;
            try { change = _engine.Complete(id, generation, outcome); }
            catch (Exception ex) { TrafficLightLog.Fault("engine-complete", ex); }
            try
            {
                if (change != null) StateChanged?.Invoke(change);
                ResultsChanged?.Invoke();
            }
            catch (Exception ex) { TrafficLightLog.Fault("subscriber", ex); }
        }

        // ---- network and power ----

        private void OnNetworkChanged(object? sender, EventArgs e) => ArmSettle();

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ArmSettle();
        }

        // The network stack announces a change several times and a resume comes before the adapter is back:
        // one re-check, two seconds after the last event.
        private void ArmSettle()
        {
            Timer? settle;
            lock (_gate) settle = _settle;
            try { settle?.Change(NetworkSettle, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException ex) { TrafficLightLog.Fault("settle-timer", ex); }
        }

        private void OnSettled()
        {
            try
            {
                bool online = _isOnline();
                bool changed = online != _lastOnline;
                _lastOnline = online;
                _engine.SetOffline(!online);
                if (online) _engine.RequestAll();
                if (changed) OfflineChanged?.Invoke(!online);
                ResultsChanged?.Invoke();
                KickTick();
            }
            catch (Exception ex) { TrafficLightLog.Fault("settled", ex); }
        }
    }
}
