using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>What the listener asks of the program around it. Every member is called on a thread of the listener.</summary>
    internal interface IRemoteCommandHost
    {
        /// <summary>The switch of REMOTE-CONTROL rule 8, read now.</summary>
        bool Listening { get; }

        /// <summary>The grant of a controller on an entry, a snapshot; null when it is not ticked.</summary>
        RemoteControlGrant? GrantFor(string entryId, string deviceId);

        /// <summary>
        /// Runs a decided verb (asking the person first when <paramref name="ask"/>) and returns its answer; blocks until the
        /// answer is known. <see cref="RemoteCommandRun.Declined"/> when the person said no or did not answer in time.
        /// </summary>
        RemoteCommandRun Run(RemoteControlVerdict verdict, RemoteControlRequest request, string controllerName, bool ask);

        /// <summary>Trusted keys or an entry's server features changed: save the accounts.</summary>
        void AccountsChanged();

        /// <summary>An entry's directory was read: <paramref name="controllers"/> are the other devices whose key is trusted.</summary>
        void DirectoryListed(string entryId, IReadOnlyList<ExchangePeer> controllers);

        /// <summary>An entry's stream state changed (for the status line of the settings page).</summary>
        void StateChanged();
    }

    internal sealed class RemoteCommandRun
    {
        public AppCommandResponse? Response { get; set; }
        public bool Declined { get; set; }
    }

    /// <summary>The state of one entry's resident stream, for the page.</summary>
    internal enum RemoteStreamState
    {
        Connecting,
        Online,
        /// <summary>An on-demand stream of the entry is open; the resident one waits for it (A10).</summary>
        Yielded,
        /// <summary>The server's <c>welcome</c> lists no <c>command</c> (or no <c>sealed</c>): nothing is held.</summary>
        ServerWithout,
        /// <summary>The server asks for the password: the person signs in again.</summary>
        NeedsSignIn,
        /// <summary>The server did not answer; the next attempt is on its way.</summary>
        Retrying,
    }

    /// <summary>
    /// The target's transport of REMOTE-CONTROL (ticket CYRFLP-0140): one resident control stream per account entry that
    /// declared <c>command-v1</c> (DEVICE-EXCHANGE 1.18 item GC), with keepalive (7.3), and one worker that takes the
    /// <c>command-request</c> frames of every entry one at a time in arrival order (rule 12). A request is opened with the
    /// key trusted for the box's <c>from</c>, never by the clear <c>fromDeviceId</c> (rule 4), decided by
    /// <see cref="RemoteControlPolicy"/>, run by the host, and answered with a <c>command-result</c> sealed to the box's
    /// <c>from</c> alone. The ring of handled requests is written before a verb runs. Nothing here touches a window.
    /// </summary>
    internal sealed class ExchangeCommandListener : IDisposable
    {
        /// <summary>A stream whose server lacks the feature is asked again after this long, or when the accounts change.</summary>
        internal static readonly TimeSpan ServerWithoutRetry = TimeSpan.FromHours(6);
        internal static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan RelistEvery = TimeSpan.FromMinutes(10);

        private readonly ExchangeClient _client;
        private readonly IRemoteCommandHost _host;
        private readonly AppCommandManifest _manifest;
        private readonly RemoteControlRingFile _ringFile;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Worker> _workers = new Dictionary<string, Worker>(StringComparer.Ordinal);
        private readonly BlockingCollection<Pending> _queue = new BlockingCollection<Pending>(new ConcurrentQueue<Pending>(), 64);
        private RemoteControlRing? _ring;
        private Thread? _processor;
        private volatile bool _disposed;

        public ExchangeCommandListener(ExchangeClient client, IRemoteCommandHost host, AppCommandManifest manifest, RemoteControlRingFile ringFile)
        {
            _client = client;
            _host = host;
            _manifest = manifest;
            _ringFile = ringFile;
            ExchangeControlLease.Changed += OnLeaseChanged;
        }

        /// <summary>The entries that should hold a stream now; a worker starts for a new one and stops for one that went.</summary>
        public void Update(IReadOnlyList<ExchangeAccountEntry> candidates)
        {
            if (_disposed) return;
            var stop = new List<Worker>();
            lock (_gate)
            {
                var wanted = new HashSet<string>(candidates.Select(e => e.Id), StringComparer.Ordinal);
                foreach (KeyValuePair<string, Worker> pair in _workers.ToList())
                    if (!wanted.Contains(pair.Key)) { stop.Add(pair.Value); _workers.Remove(pair.Key); }
                foreach (ExchangeAccountEntry entry in candidates)
                {
                    if (_workers.TryGetValue(entry.Id, out Worker? running))
                    {
                        running.Entry = entry;
                        running.Kick();
                        continue;
                    }
                    var worker = new Worker(this, entry);
                    _workers[entry.Id] = worker;
                    worker.Start();
                }
                if (_workers.Count > 0 && _processor == null)
                {
                    _processor = new Thread(ProcessLoop) { IsBackground = true, Name = "CyrFlip remote commands" };
                    _processor.Start();
                }
            }
            foreach (Worker w in stop) w.Stop();
            _host.StateChanged();
        }

        /// <summary>Each entry's stream state, by entry id.</summary>
        public Dictionary<string, RemoteStreamState> States()
        {
            lock (_gate) return _workers.ToDictionary(p => p.Key, p => p.Value.State, StringComparer.Ordinal);
        }

        /// <summary>The other devices of an entry whose key is trusted, as the last directory listed them.</summary>
        public IReadOnlyList<ExchangePeer> Controllers(string entryId)
        {
            lock (_gate) return _workers.TryGetValue(entryId, out Worker? w) ? w.Controllers : new List<ExchangePeer>();
        }

        /// <summary>Ask every live stream for its directory again (the page's "refresh the list").</summary>
        public void Relist()
        {
            List<Worker> all;
            lock (_gate) all = _workers.Values.ToList();
            foreach (Worker w in all) w.RequestList();
        }

        private void OnLeaseChanged(string entryId)
        {
            Worker? w;
            lock (_gate) _workers.TryGetValue(entryId, out w);
            w?.LeaseChanged();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExchangeControlLease.Changed -= OnLeaseChanged;
            List<Worker> all;
            lock (_gate) { all = _workers.Values.ToList(); _workers.Clear(); }
            foreach (Worker w in all) w.Stop();
            _queue.CompleteAdding();
        }

        // ------------------------------------------------------------------ the requests, one at a time (rule 12)

        private sealed class Pending
        {
            public Worker Worker = null!;
            public ExchangeSession Session = null!;
            public string ClearFrom = "";
            public string RequestId = "";
            public string BoxJson = "";
        }

        private void ProcessLoop()
        {
            try
            {
                foreach (Pending p in _queue.GetConsumingEnumerable())
                {
                    try { Handle(p); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        RemoteCommandsLog.Fault("handle", ex);
                    }
                }
            }
            catch (ObjectDisposedException) { }
        }

        private RemoteControlRing Ring()
        {
            if (_ring != null) return _ring;
            _ring = _ringFile.Load(out bool unreadable);
            if (unreadable) RemoteCommandsLog.Fault("ring-load", null);
            return _ring;
        }

        private void Handle(Pending p)
        {
            ExchangeAccountEntry entry = p.Worker.Entry;
            using ExchangeDeviceKey? key = ExchangeClipboardService.LoadDeviceKey(entry);
            if (key == null) { RemoteCommandsLog.SealedFailed(p.ClearFrom, "no-device-key"); return; }

            string context = RemoteControl.RequestContext(p.RequestId);
            byte[]? payload = null;
            byte[]? contentKey = null;
            string failure = "";
            bool opened = TryOpen(entry, key, context, p.BoxJson, out payload, out contentKey, out failure);
            if (!opened && failure == "untrusted-key" && p.Worker.ListAndWait(TimeSpan.FromSeconds(5)))
                opened = TryOpen(entry, key, context, p.BoxJson, out payload, out contentKey, out failure);
            ExchangeE2e.Wipe(contentKey);
            if (!opened || payload == null)
            {
                // Rule 4: a box that does not open is never answered; the controller's wait ends it.
                RemoteCommandsLog.SealedFailed(p.ClearFrom, failure);
                return;
            }
            if (!ExchangeE2e.TryReadSender(p.BoxJson, out string? from, out _) || string.IsNullOrEmpty(from)) return;
            string controller = from!;
            string controllerName = p.Worker.NameOf(controller);
            DateTime now = DateTime.UtcNow;

            RemoteControlRing ring = Ring();
            string? stored = ring.Find(entry.Id, controller, p.RequestId, now);
            if (stored != null)
            {
                RemoteControlAnswer.TryReadOutcome(stored, out string storedStatus, out int storedExit);
                RemoteCommandsLog.Request(controllerName, controller, "(repeat)", "-", storedStatus, storedExit, new string[0]);
                Answer(p, entry, key, controller, stored);
                return;
            }
            if (!ring.HasRoom(now))
            {
                Answer(p, entry, key, controller, RemoteControlAnswer.Refused(AppCommandReasons.Busy, AppCommandExit.NotReady,
                    "Too many commands are being remembered; try again later.").ToJson());
                return;
            }

            RemoteControlRequest? request = RemoteControlRequest.TryParse(payload, out string parseReason);
            Array.Clear(payload, 0, payload.Length);
            DateTime keepUntil = request?.WindowEndUtc ?? now.AddSeconds(RemoteControl.MaxTtlSeconds + 2 * RemoteControl.ClockSkewSeconds);
            if (keepUntil < now) keepUntil = now.AddSeconds(RemoteControl.ClockSkewSeconds);
            RemoteControlVerdict verdict = RemoteControlPolicy.Decide(request, parseReason, _host.Listening,
                _host.GrantFor(entry.Id, controller), _manifest, now);
            string verb = request?.Verb ?? "?";
            string risk = verdict.Spec?.Risk ?? "-";
            IReadOnlyList<string> argNames = request?.Args.Keys.ToList() ?? new List<string>();

            if (verdict.Kind == RemoteControlVerdictKind.Refuse || request == null)
            {
                RemoteControlAnswer refusal = verdict.Refusal ?? RemoteControlAnswer.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, "The request does not read.");
                string json = refusal.ToJson();
                ring.Put(entry.Id, controller, p.RequestId, keepUntil, json);
                _ringFile.Save(ring);
                RemoteCommandsLog.Request(controllerName, controller, verb, risk, refusal.Status + ":" + refusal.Reason, refusal.Exit, argNames);
                Answer(p, entry, key, controller, json);
                return;
            }

            // Written before the verb runs: a crash or a repeated box after this point never runs it a second time.
            ring.Put(entry.Id, controller, p.RequestId, keepUntil, RemoteControlRing.InterruptedAnswer);
            if (!_ringFile.Save(ring))
            {
                string json = RemoteControlAnswer.Refused(AppCommandReasons.IoError, AppCommandExit.IoError,
                    "The list of handled commands could not be written, so nothing was run.").ToJson();
                RemoteCommandsLog.Request(controllerName, controller, verb, risk, "refused:io-error", AppCommandExit.IoError, argNames);
                Answer(p, entry, key, controller, json);
                return;
            }

            RemoteCommandRun run = _host.Run(verdict, request, controllerName, verdict.Kind == RemoteControlVerdictKind.Ask);
            RemoteControlAnswer answer = run.Declined || run.Response == null
                ? RemoteControlAnswer.Refused(RemoteControl.Declined, AppCommandExit.Cancelled, "The person on the other device did not accept the command.")
                : RemoteControlAnswer.From(run.Response);
            string answerJson = answer.ToJson();
            ring.Put(entry.Id, controller, p.RequestId, keepUntil, answerJson);
            _ringFile.Save(ring);
            RemoteCommandsLog.Request(controllerName, controller, verb, risk,
                answer.Status + (string.IsNullOrEmpty(answer.Reason) ? "" : ":" + answer.Reason), answer.Exit, argNames);
            Answer(p, entry, key, controller, answerJson);
            run.Response?.AfterSent?.Invoke();
        }

        private static bool TryOpen(ExchangeAccountEntry entry, ExchangeDeviceKey key, string context, string boxJson,
            out byte[]? payload, out byte[]? contentKey, out string failure)
        {
            Dictionary<string, string> trusted;
            lock (entry) trusted = new Dictionary<string, string>(entry.TrustedKeys, StringComparer.Ordinal);
            return ExchangeE2e.TryOpenBox(context, boxJson, entry.DeviceId, key,
                from => ExchangeRecipients.TrustedKemOf(trusted, from, ExchangeClipboardService.DecodeKem),
                out payload, out contentKey, out failure);
        }

        /// <summary>Seals <paramref name="answerJson"/> to the controller alone and sends it on the stream the request came on.</summary>
        private void Answer(Pending p, ExchangeAccountEntry entry, ExchangeDeviceKey key, string controller, string answerJson)
        {
            byte[]? kem;
            lock (entry) kem = ExchangeRecipients.TrustedKemOf(entry.TrustedKeys, controller, ExchangeClipboardService.DecodeKem);
            if (kem == null) { RemoteCommandsLog.Fault("answer-no-key", null); return; }
            string box;
            try
            {
                box = ExchangeE2e.SealBox(RemoteControl.ResultContext(p.RequestId), Encoding.UTF8.GetBytes(answerJson), entry.DeviceId, key,
                    new[] { new ExchangeE2eRecipient(controller, kem) }, out byte[] ck);
                ExchangeE2e.Wipe(ck);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Security.Cryptography.CryptographicException)
            {
                RemoteCommandsLog.Fault("answer-seal", ex);
                return;
            }
            p.Worker.Send(p.Session, ExchangeMembers.CommandResult(controller, p.RequestId, box));
        }

        // ------------------------------------------------------------------ one entry's resident stream

        private sealed class Worker
        {
            private readonly ExchangeCommandListener _owner;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private readonly SemaphoreSlim _write = new SemaphoreSlim(1, 1);
            private readonly AutoResetEvent _wake = new AutoResetEvent(false);
            private readonly ManualResetEventSlim _listed = new ManualResetEventSlim(false);
            private ExchangeSession? _session;
            private List<ExchangePeer> _controllers = new List<ExchangePeer>();
            private List<ExchangePeer> _allPeers = new List<ExchangePeer>();
            private volatile RemoteStreamState _state = RemoteStreamState.Connecting;
            private long _lastFrameTicks;

            public Worker(ExchangeCommandListener owner, ExchangeAccountEntry entry) { _owner = owner; Entry = entry; }

            public volatile ExchangeAccountEntry Entry;
            public RemoteStreamState State => _state;
            public List<ExchangePeer> Controllers { get { lock (this) return _controllers.ToList(); } }

            public void Start() => Task.Run(RunAsync);

            /// <summary>Ends the worker. Called on the UI thread, which the hooks share, so the <c>bye</c> goes from the pool.</summary>
            public void Stop()
            {
                if (_stop.IsCancellationRequested) return;
                ExchangeSession? s = _session;
                _stop.Cancel();
                _wake.Set();
                if (s == null) return;
                Task.Run(() =>
                {
                    TrySend(s, ExchangeMembers.Bye(), 2000);
                    s.Dispose();
                });
            }

            /// <summary>Retry now instead of after the back-off (the accounts or the module changed).</summary>
            public void Kick() => _wake.Set();

            public void LeaseChanged()
            {
                // A lease taken: let the stream go before the on-demand one replaces it. Released: come back at once.
                if (ExchangeControlLease.IsHeld(Entry.Id)) _session?.Dispose();
                _wake.Set();
            }

            public string NameOf(string deviceId)
            {
                lock (this)
                {
                    ExchangePeer? p = _allPeers.FirstOrDefault(x => x.DeviceId == deviceId);
                    return p != null ? ExchangeRecipients.NameOf(p) : deviceId.Substring(0, Math.Min(6, deviceId.Length));
                }
            }

            public void RequestList()
            {
                ExchangeSession? s = _session;
                if (s != null) Send(s, ExchangeMembers.List());
            }

            /// <summary>Asks for the directory and waits for it (a controller whose key this entry has not learned yet).</summary>
            public bool ListAndWait(TimeSpan timeout)
            {
                ExchangeSession? s = _session;
                if (s == null) return false;
                _listed.Reset();
                Send(s, ExchangeMembers.List());
                return _listed.Wait(timeout);
            }

            public void Send(ExchangeSession session, ExchangeMembers frame) => TrySend(session, frame, 10000);

            private bool TrySend(ExchangeSession session, ExchangeMembers frame, int timeoutMs)
            {
                try
                {
                    if (!_write.Wait(timeoutMs)) return false;
                    try
                    {
                        using var cts = new CancellationTokenSource(timeoutMs);
                        session.Connection.SendAsync(frame, cts.Token).GetAwaiter().GetResult();
                        return true;
                    }
                    finally { _write.Release(); }
                }
                catch (Exception ex) when (ExchangeClient.IsNetworkFailure(ex) || ex is ExchangeFailure || ex is InvalidOperationException)
                {
                    return false;
                }
            }

            private void SetState(RemoteStreamState state)
            {
                if (_state == state) return;
                _state = state;
                _owner._host.StateChanged();
            }

            private void Pause(TimeSpan delay)
            {
                if (_stop.IsCancellationRequested) return;
                WaitHandle.WaitAny(new[] { _wake, _stop.Token.WaitHandle }, delay);
            }

            private async Task RunAsync()
            {
                TimeSpan backoff = FirstBackoff;
                while (!_stop.IsCancellationRequested && !_owner._disposed)
                {
                    ExchangeAccountEntry entry = Entry;
                    if (ExchangeControlLease.IsHeld(entry.Id))
                    {
                        SetState(RemoteStreamState.Yielded);
                        Pause(TimeSpan.FromMinutes(1));
                        continue;
                    }
                    SetState(RemoteStreamState.Connecting);
                    DateTime started = DateTime.UtcNow;
                    string outcome;
                    try { outcome = await HoldOnceAsync(entry).ConfigureAwait(false); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        // A fault nobody foresaw costs one stream, never the worker: log it and come back after the back-off.
                        RemoteCommandsLog.Fault("hold", ex);
                        outcome = "retry";
                    }
                    if (_stop.IsCancellationRequested) break;
                    switch (outcome)
                    {
                        case "server-without":
                            SetState(RemoteStreamState.ServerWithout);
                            Pause(ServerWithoutRetry);
                            continue;
                        case "sign-in":
                            SetState(RemoteStreamState.NeedsSignIn);
                            Pause(ServerWithoutRetry);
                            continue;
                    }
                    if (ExchangeControlLease.IsHeld(entry.Id)) continue;
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(1)) backoff = FirstBackoff;
                    SetState(RemoteStreamState.Retrying);
                    Pause(backoff);
                    backoff = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, backoff.Ticks * 2));
                }
                _session?.Dispose();
            }

            /// <summary>One life of the resident stream; the code says why it ended.</summary>
            private async Task<string> HoldOnceAsync(ExchangeAccountEntry entry)
            {
                ExchangeOutcome<ExchangeSession> opened;
                try { opened = await _owner._client.OpenResidentAsync(entry, ExchangeWire.ProposedKeepaliveSeconds, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return "stopped"; }
                if (!opened.Ok || opened.Value == null)
                    return ExchangeCodes.AsksForPassword(opened.Code) ? "sign-in" : "retry";

                using ExchangeSession session = opened.Value; // closed on every way out, a thrown one included
                if (ExchangeClipboardService.LearnFrom(entry, null, session)) _owner._host.AccountsChanged();
                ExchangeWelcome w = session.Welcome;
                if (w.IsGuest || !w.Has(ExchangeWire.FeatureCommand) || !w.Has(ExchangeWire.FeatureSealed))
                {
                    session.Dispose();
                    return "server-without";
                }
                if (ExchangeControlLease.IsHeld(entry.Id)) { session.Dispose(); return "yielded"; }

                _session = session;
                int interval = (int)(w.KeepaliveSeconds >= 10 && w.KeepaliveSeconds <= 120 ? w.KeepaliveSeconds : ExchangeWire.ProposedKeepaliveSeconds);
                interval = Math.Min(interval, ExchangeWire.ProposedKeepaliveSeconds);
                Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
                using var alive = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                Task pulse = Task.Run(() => PulseAsync(session, interval, alive.Token));
                try
                {
                    // The first envelope after welcome makes this the control stream (7.2); list also teaches the keys.
                    if (!TrySend(session, ExchangeMembers.List(), 10000)) return "retry";
                    SetState(RemoteStreamState.Online);
                    DateTime lastList = DateTime.UtcNow;
                    while (!_stop.IsCancellationRequested)
                    {
                        ExchangeFrame? frame;
                        try { frame = await session.Connection.ReceiveAsync(true, _stop.Token).ConfigureAwait(false); }
                        catch (Exception ex) when (ExchangeClient.IsNetworkFailure(ex) || ex is ExchangeFailure || ex is InvalidOperationException)
                        {
                            return "retry";
                        }
                        if (frame == null) return "retry";
                        Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
                        switch (frame.Type)
                        {
                            case "directory":
                                OnDirectory(entry, frame);
                                break;
                            case "command-request":
                                OnRequest(session, frame);
                                break;
                        }
                        if (DateTime.UtcNow - lastList > RelistEvery)
                        {
                            lastList = DateTime.UtcNow;
                            Send(session, ExchangeMembers.List());
                        }
                    }
                    return "stopped";
                }
                finally
                {
                    alive.Cancel();
                    _session = null;
                    session.Dispose();
                    try { await pulse.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }

            /// <summary>Keepalive every interval; a stream that brought nothing for three intervals is closed (7.3).</summary>
            private async Task PulseAsync(ExchangeSession session, int intervalSeconds, CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    long quietTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastFrameTicks);
                    if (quietTicks > TimeSpan.FromSeconds(3 * intervalSeconds).Ticks || !TrySend(session, ExchangeMembers.Keepalive(), 5000))
                    {
                        session.Dispose(); // the receive loop sees the end and the worker reopens
                        return;
                    }
                }
            }

            private void OnDirectory(ExchangeAccountEntry entry, ExchangeFrame frame)
            {
                List<ExchangePeer> peers = ExchangeClient.PeersOf(frame);
                bool learned;
                List<ExchangePeer> controllers;
                lock (entry)
                {
                    learned = ExchangeRecipients.Learn(entry.DeviceId, peers, entry.TrustedKeys);
                    controllers = peers.Where(p => p.DeviceId.Length > 0 && p.DeviceId != entry.DeviceId
                            && ExchangeRecipients.StateOf(p, entry.TrustedKeys.TryGetValue(p.DeviceId, out string? k) ? k : null) == ExchangePeerState.Trusted)
                        .ToList();
                }
                lock (this) { _allPeers = peers; _controllers = controllers; }
                if (learned) _owner._host.AccountsChanged();
                _owner._host.DirectoryListed(entry.Id, controllers);
                _listed.Set();
            }

            private void OnRequest(ExchangeSession session, ExchangeFrame frame)
            {
                string requestId = frame.Str("requestId");
                string box = frame.JsonOf("box");
                string clearFrom = frame.Str("fromDeviceId");
                if (!RemoteControl.IsRequestId(requestId) || box.Length == 0)
                {
                    RemoteCommandsLog.SealedFailed(clearFrom, "malformed");
                    return;
                }
                var pending = new Pending { Worker = this, Session = session, ClearFrom = clearFrom, RequestId = requestId, BoxJson = box };
                // The queue is bounded: a flood waits for nothing and is dropped unanswered, like a box that does not open.
                bool queued;
                try { queued = _owner._queue.TryAdd(pending); }
                catch (InvalidOperationException) { queued = false; } // the listener is being disposed
                if (!queued) RemoteCommandsLog.SealedFailed(clearFrom, "queue-full");
            }
        }
    }
}
