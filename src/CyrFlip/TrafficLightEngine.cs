using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>The four lights: green, yellow, red and gray (ticket S0069 section 1).</summary>
    internal enum TrafficState
    {
        /// <summary>Gray: no completed check yet.</summary>
        Unknown,
        /// <summary>Green: reachable.</summary>
        Ok,
        /// <summary>Yellow: reachable, but not right (D7).</summary>
        Degraded,
        /// <summary>Red: not reachable.</summary>
        Down,
    }

    /// <summary>
    /// The invariant reason codes of section 4. They are what the journal writes and what the UI localizes;
    /// no localized word is ever stored or written.
    /// </summary>
    internal static class TrafficReasons
    {
        public const string Ok = "ok";
        public const string Slow = "slow";
        public const string Unverified = "unverified";
        public const string Timeout = "timeout";
        public const string NameNotResolved = "name-not-resolved";
        public const string ConnectionRefused = "connection-refused";
        public const string NetworkUnreachable = "network-unreachable";
        public const string AccessDenied = "access-denied";
        public const string PathNotFound = "path-not-found";
        public const string DrivePresentNot = "drive-not-present";
        public const string TlsFailed = "tls-failed";
        public const string CertificateNotTrusted = "certificate-not-trusted";
        public const string CertificateChanged = "certificate-changed";
        public const string NotSshServer = "not-ssh-server";
        public const string ComputerOffline = "computer-offline";
        public const string TooManyRedirects = "too-many-redirects";
        public const string InvalidAddress = "invalid-address";
        public const string Failed = "failed";

        public static string Http(int status) => "http-" + status.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>What one attempt produced, before the target's thresholds and streak are applied.</summary>
    internal sealed class ProbeOutcome
    {
        /// <summary>An answer was obtained. False is a failure: red once the failure streak is full.</summary>
        public bool Reached;
        public string Reason = TrafficReasons.Ok;
        public int LatencyMs;
        /// <summary>Reached, but not right: an HTTP status outside the accepted list, an unverified certificate.</summary>
        public bool Degraded;
        /// <summary>One short extra line for the tooltip: the pinned and the observed fingerprint, an accepted certificate.</summary>
        public string Note = "";

        public static ProbeOutcome Good(int latencyMs, string note = "")
            => new ProbeOutcome { Reached = true, Reason = TrafficReasons.Ok, LatencyMs = latencyMs, Note = note };

        public static ProbeOutcome Alive(int latencyMs, string reason, string note = "")
            => new ProbeOutcome { Reached = true, Degraded = true, Reason = reason, LatencyMs = latencyMs, Note = note };

        public static ProbeOutcome Fail(string reason, int latencyMs = 0, string note = "")
            => new ProbeOutcome { Reached = false, Reason = reason, LatencyMs = latencyMs, Note = note };
    }

    /// <summary>The verdict function: an outcome and the target's thresholds give a light and a reason.</summary>
    internal static class TrafficLightVerdict
    {
        public static void Judge(TrafficLightTarget target, ProbeOutcome outcome, out TrafficState state, out string reason)
        {
            if (!outcome.Reached) { state = TrafficState.Down; reason = outcome.Reason; return; }
            if (outcome.Degraded) { state = TrafficState.Degraded; reason = outcome.Reason; return; }
            if (outcome.LatencyMs > target.EffectiveYellowMs) { state = TrafficState.Degraded; reason = TrafficReasons.Slow; return; }
            state = TrafficState.Ok;
            reason = TrafficReasons.Ok;
        }
    }

    /// <summary>
    /// One target's last known result as the UI reads it - an immutable snapshot. Everything a tooltip, a tile
    /// and the tray summary need is here, so no consumer reaches into the engine's own records.
    /// </summary>
    internal sealed class TrafficLightTile
    {
        public TrafficLightTarget Target = null!;
        public TrafficState State;
        public string Reason = "";
        public string Note = "";
        public DateTime? CheckedAtUtc;
        public int? LatencyMs;
        public DateTime? LastOkAtUtc;
        public DateTime? StateSinceUtc;
        public DateTime? NextDueUtc;
        public int FailureStreak;
        /// <summary>A check is running right now.</summary>
        public bool Checking;
        /// <summary>Drawn dimmed: the result is older than three intervals, or the computer is offline (D17).</summary>
        public bool Stale;
        /// <summary>The target's address does not validate: it is shown as off and never probed (section 7).</summary>
        public bool InvalidAddress;
        /// <summary>Network kinds are paused because Windows reports no network (D23).</summary>
        public bool PausedOffline;
        /// <summary>The target is switched off: a muted "off" tile.</summary>
        public bool Disabled => !Target.Enabled;
        /// <summary>A failure streak is open but short of the threshold: "confirming k/N" (D2).</summary>
        public bool Confirming => FailureStreak > 0 && State != TrafficState.Down && FailureStreak < Target.FailuresBeforeDown;
    }

    /// <summary>A settled change of one target's light, handed to the notifier and the journal (D14, D19).</summary>
    internal sealed class TrafficLightChange
    {
        public TrafficLightTarget Target = null!;
        public TrafficState From;
        public TrafficState To;
        public string Reason = "";
        public int? LatencyMs;
        public DateTime AtUtc;
        /// <summary>The first completed result since start (or since the target was added): a baseline.</summary>
        public bool IsBaseline;
        /// <summary>How long the previous light had held, when known.</summary>
        public TimeSpan? WasFor;
        /// <summary>When the first failed check of the streak that ended in this change finished.</summary>
        public DateTime? FirstFailureUtc;
        /// <summary>The target needs the network and Windows reports none: no balloon (D23).</summary>
        public bool WhileOffline;
        /// <summary>
        /// The check that settled this change was asked for by a command alone (ticket S0117): the journal records it
        /// like any change, the board draws it, but no balloon is raised for it.
        /// </summary>
        public bool Silent;
    }

    /// <summary>How a check a command asked for ended (ticket S0117).</summary>
    internal enum TrafficCheckOutcome
    {
        /// <summary>The check finished and its result is in the engine.</summary>
        Completed,
        /// <summary>The check will never report: the target was edited, removed or switched off, or the module was stopped.</summary>
        Abandoned,
    }

    /// <summary>Whether a command's request for one check was taken, and if not why (ticket S0117).</summary>
    internal enum TrafficCheckStart
    {
        /// <summary>A check is waiting for its turn or already running; the ticket completes with it.</summary>
        Started,
        /// <summary>No target has this id.</summary>
        NotFound,
        /// <summary>The target is switched off: it is never probed.</summary>
        Disabled,
        /// <summary>The address does not validate: it is shown as invalid and never probed.</summary>
        InvalidAddress,
        /// <summary>A network target while Windows reports no network: paused, not red.</summary>
        Offline,
        /// <summary>
        /// The check in flight has already reported (a hard timeout abandoned its call) and only the slot is still held: nothing
        /// is coming for a command to wait for, and a new look cannot start until the call returns.
        /// </summary>
        Busy,
    }

    /// <summary>
    /// What a command holds for one check it asked for (ticket S0117): whether the engine took the request and, when it
    /// did, a task that completes when that very check has been applied - not when some later one has. <see cref="Cancel"/>
    /// lets go of the wait; the check itself, once asked for, still runs.
    /// </summary>
    internal sealed class TrafficCheckTicket
    {
        private readonly Action? _cancel;

        private TrafficCheckTicket(TrafficCheckStart start, Task<TrafficCheckOutcome>? completion, Action? cancel)
        {
            Start = start;
            Completion = completion ?? Task.FromResult(TrafficCheckOutcome.Abandoned);
            _cancel = cancel;
        }

        public TrafficCheckStart Start { get; }
        public Task<TrafficCheckOutcome> Completion { get; }
        public void Cancel() => _cancel?.Invoke();

        public static TrafficCheckTicket NotTaken(TrafficCheckStart reason) => new TrafficCheckTicket(reason, null, null);
        public static TrafficCheckTicket Taken(Task<TrafficCheckOutcome> completion, Action cancel)
            => new TrafficCheckTicket(TrafficCheckStart.Started, completion, cancel);
    }

    /// <summary>What the tray summary line says, as numbers.</summary>
    internal struct TrafficLightSummary
    {
        public int Total;
        public int Down;
        public int Degraded;
        public int Unknown;
        public bool Offline;
    }

    /// <summary>
    /// The state machine of the traffic light (ticket S0069 sections 4 and 5): per-target last known result,
    /// the failure streak, due times, staleness and the settled changes. <b>It owns no timer, no thread and no
    /// network</b> - the clock is injected and the scheduler drives it - so every rule is unit-tested without
    /// a desktop. All members are safe to call from any thread.
    /// </summary>
    internal sealed class TrafficLightEngine
    {
        /// <summary>The longest wait between checks while a failure streak is still being confirmed (D2).</summary>
        public static readonly TimeSpan ConfirmationDelay = TimeSpan.FromSeconds(10);
        /// <summary>A manual-only target is stale after this long (D17).</summary>
        public static readonly TimeSpan ManualOnlyStaleAfter = TimeSpan.FromHours(1);
        /// <summary>A result older than this many intervals is drawn stale (D17).</summary>
        public const int StaleIntervals = 3;

        private sealed class Entry
        {
            public TrafficLightTarget Target = null!;
            public int Generation;
            public TrafficState State = TrafficState.Unknown;
            public string Reason = "";
            public string Note = "";
            public DateTime? CheckedAtUtc;
            public int? LatencyMs;
            public DateTime? LastOkAtUtc;
            public DateTime? StateSinceUtc;
            public DateTime? FirstFailureUtc;
            public int FailureStreak;
            public bool InFlight;
            /// <summary>The flight in progress has already been applied (S0117): the slot is held by a call that will not report again.</summary>
            public bool FlightReported;
            public bool ForceDue;
            /// <summary>The only cause of the next check is a command (S0117): not an interval, a click, a start or a network change.</summary>
            public bool CommandOnlyDue;
            /// <summary>The check in flight was asked for by a command alone: its settled change raises no balloon (S0117).</summary>
            public bool FlightSilent;
            /// <summary>Commands waiting for the next completed check of this target (S0117).</summary>
            public List<Waiter>? Waiters;
        }

        /// <summary>One command's wait for one check of one target (ticket S0117).</summary>
        private sealed class Waiter
        {
            public readonly TaskCompletionSource<TrafficCheckOutcome> Source =
                new TaskCompletionSource<TrafficCheckOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            /// <summary>The flight this wait belongs to; -1 until the next dispatch binds it.</summary>
            public int Generation = -1;
        }

        private readonly object _gate = new object();
        private readonly Func<DateTime> _utcNow;
        private readonly Func<string, bool> _isRemotePath;
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _offline;

        public TrafficLightEngine(Func<DateTime>? utcNow = null, Func<string, bool>? isRemotePath = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _isRemotePath = isRemotePath ?? (p => LaunchTargets.IsRemotePath(p));
        }

        /// <summary>True while Windows reports no network (D23).</summary>
        public bool Offline
        {
            get { lock (_gate) return _offline; }
        }

        /// <summary>
        /// Sets the offline flag; true when it changed. Coming back online makes every network target due at
        /// once (D17: a network change is a full re-check).
        /// </summary>
        public bool SetOffline(bool offline)
        {
            var abandoned = new List<Waiter>();
            lock (_gate)
            {
                if (_offline == offline) return false;
                _offline = offline;
                foreach (Entry e in _entries)
                {
                    if (!offline)
                    {
                        e.ForceDue = true;
                        e.CommandOnlyDue = false;
                    }
                    // A network target is not dispatched while offline: a command still waiting for its turn would
                    // otherwise wait out its whole bound for a check that cannot start (S0117).
                    else if (e.Target.NeedsNetwork(_isRemotePath))
                    {
                        abandoned.AddRange(TakeWaiters(e, w => w.Generation < 0));
                    }
                }
            }
            Settle(abandoned, TrafficCheckOutcome.Abandoned);
            return true;
        }

        /// <summary>
        /// Replaces the target list. Results are kept for a target that is still there with the same identity
        /// (id, kind, address, fingerprint); one whose identity changed starts again from gray and is due at
        /// once; a new target is due at once.
        /// </summary>
        public void SetTargets(IEnumerable<TrafficLightTarget> targets)
        {
            // Commands waiting for a check that this edit makes void are let go once the lock is released (S0117).
            var abandoned = new List<Waiter>();
            lock (_gate)
            {
                var previous = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (Entry e in _entries) previous[e.Target.Id] = e;

                var next = new List<Entry>();
                foreach (TrafficLightTarget target in TrafficLightTargetList.InOrder(targets))
                {
                    TrafficLightTarget copy = target.Clone();
                    if (previous.TryGetValue(copy.Id, out Entry? old))
                    {
                        bool sameIdentity = old.Target.Kind == copy.Kind && old.Target.Address == copy.Address
                            && old.Target.Fingerprint == copy.Fingerprint
                            && old.Target.AllowUntrustedCertificate == copy.AllowUntrustedCertificate
                            && old.Target.AcceptedStatusCodes == copy.AcceptedStatusCodes;
                        bool switchedOn = !old.Target.Enabled && copy.Enabled;
                        if (!sameIdentity)
                        {
                            // A result about something else is not a result about this target.
                            old.Generation++;
                            old.State = TrafficState.Unknown;
                            old.Reason = ""; old.Note = "";
                            old.CheckedAtUtc = null; old.LatencyMs = null; old.LastOkAtUtc = null;
                            old.StateSinceUtc = null; old.FirstFailureUtc = null;
                            old.FailureStreak = 0;
                            old.ForceDue = true;
                            old.CommandOnlyDue = false;
                            old.FlightSilent = false;
                            abandoned.AddRange(TakeWaiters(old, _ => true));
                            // The call still out there is about the old identity: its answer is dropped by the
                            // generation, and its release no longer matches either - so the slot is freed here,
                            // or a target edited while it was being checked would never be checked again.
                            old.InFlight = false;
                            old.FlightReported = false;
                        }
                        else if (switchedOn)
                        {
                            old.ForceDue = true;
                            old.CommandOnlyDue = false;
                        }
                        // A target switched off is never dispatched: a command still waiting for its turn is let go.
                        if (!copy.Enabled) abandoned.AddRange(TakeWaiters(old, w => w.Generation < 0));
                        old.Target = copy;
                        next.Add(old);
                    }
                    else
                    {
                        next.Add(new Entry { Target = copy, ForceDue = true });
                    }
                }
                var kept = new HashSet<string>(StringComparer.Ordinal);
                foreach (Entry e in next) kept.Add(e.Target.Id);
                foreach (Entry gone in previous.Values)
                    if (!kept.Contains(gone.Target.Id)) abandoned.AddRange(TakeWaiters(gone, _ => true));
                _entries.Clear();
                _entries.AddRange(next);
            }
            Settle(abandoned, TrafficCheckOutcome.Abandoned);
        }

        /// <summary>
        /// Makes every enabled target due now: start, resume from sleep, a network change (D17). A person's
        /// "check everything" passes <paramref name="includeRunning"/> false: a check that is already out there
        /// is as fresh as the one they are asking for, and marking it would only run it a second time the
        /// moment it comes back. A network change keeps true - what is running began on the old network.
        /// </summary>
        public void RequestAll(bool includeRunning = true)
        {
            lock (_gate)
                foreach (Entry e in _entries)
                    if (includeRunning || !e.InFlight)
                    {
                        e.ForceDue = true;
                        e.CommandOnlyDue = false;
                    }
        }

        /// <summary>"Check now" on one target. Ignored while a check of it is running.</summary>
        public bool RequestNow(string id)
        {
            lock (_gate)
            {
                Entry? e = Find(id);
                if (e == null || e.InFlight) return false;
                e.ForceDue = true;
                e.CommandOnlyDue = false;
                return true;
            }
        }

        /// <summary>
        /// One check asked for by a command (ticket S0117) - the same path a click on a tile takes, with two additions:
        /// the ticket completes when that check has been applied, and a check that no one but the command asked for is
        /// <see cref="TrafficLightChange.Silent"/>. A check already out there is not started a second time: the command
        /// waits for it, since it is as fresh as the one asked for (the rule of <see cref="RequestAll"/>). A target that
        /// is never probed - switched off, an address that does not validate, a network target while offline - is
        /// refused with the reason, and nothing is marked due.
        /// </summary>
        public TrafficCheckTicket RequestCommandCheck(string id) => RequestCommandCheck(id, null);

        public TrafficCheckTicket RequestCommandCheck(TrafficLightTarget target) => RequestCommandCheck(target.Id, target);

        private TrafficCheckTicket RequestCommandCheck(string id, TrafficLightTarget? selected)
        {
            DateTime now = _utcNow();
            lock (_gate)
            {
                Entry? e = Find(id);
                if (e == null || (selected != null && !ReferenceEquals(e.Target, selected)))
                    return TrafficCheckTicket.NotTaken(TrafficCheckStart.NotFound);
                if (!e.Target.Enabled) return TrafficCheckTicket.NotTaken(TrafficCheckStart.Disabled);
                if (TrafficLightAddress.Validate(e.Target) != AddressIssue.None) return TrafficCheckTicket.NotTaken(TrafficCheckStart.InvalidAddress);
                if (_offline && e.Target.NeedsNetwork(_isRemotePath)) return TrafficCheckTicket.NotTaken(TrafficCheckStart.Offline);
                // The flight has reported and only its call is still out (a hard timeout abandoned it): joining it would wait
                // for a report that has been made, until the call returns - possibly past the command's whole bound.
                if (e.InFlight && e.FlightReported) return TrafficCheckTicket.NotTaken(TrafficCheckStart.Busy);

                var waiter = new Waiter();
                if (e.InFlight)
                {
                    waiter.Generation = e.Generation;
                }
                else
                {
                    // Only when nothing else already made the target due is the command the whole reason for the check.
                    bool alreadyDue = e.ForceDue || IsDue(e, now);
                    e.ForceDue = true;
                    if (!alreadyDue) e.CommandOnlyDue = true;
                }
                if (e.Waiters == null) e.Waiters = new List<Waiter>();
                e.Waiters.Add(waiter);
                return TrafficCheckTicket.Taken(waiter.Source.Task, () => Forget(id, waiter));
            }
        }

        /// <summary>A command stopped waiting (its bound passed): the wait is dropped, the check itself is not.</summary>
        private void Forget(string id, Waiter waiter)
        {
            lock (_gate)
            {
                Entry? e = Find(id);
                if (e == null || e.Waiters == null) return;
                e.Waiters.Remove(waiter);
                if (e.Waiters.Count == 0) e.Waiters = null;
            }
        }

        /// <summary>
        /// Lets every waiting command go: the module is being stopped and no check will report (ticket S0117). Without it a
        /// command asked a moment before the switch was turned off would wait out its whole bound.
        /// </summary>
        public void AbandonCommandChecks()
        {
            var all = new List<Waiter>();
            lock (_gate)
            {
                foreach (Entry e in _entries)
                {
                    all.AddRange(TakeWaiters(e, _ => true));
                    e.CommandOnlyDue = false;
                    e.FlightSilent = false;
                }
            }
            Settle(all, TrafficCheckOutcome.Abandoned);
        }

        /// <summary>Removes and returns the waiters of an entry that match. The caller settles them outside the lock.</summary>
        private static List<Waiter> TakeWaiters(Entry e, Func<Waiter, bool> match)
        {
            var taken = new List<Waiter>();
            if (e.Waiters == null) return taken;
            for (int i = e.Waiters.Count - 1; i >= 0; i--)
            {
                if (!match(e.Waiters[i])) continue;
                taken.Add(e.Waiters[i]);
                e.Waiters.RemoveAt(i);
            }
            if (e.Waiters.Count == 0) e.Waiters = null;
            return taken;
        }

        private static void Settle(List<Waiter>? waiters, TrafficCheckOutcome outcome)
        {
            if (waiters == null) return;
            foreach (Waiter w in waiters) w.Source.TrySetResult(outcome);
        }

        // ---- scheduling ----

        /// <summary>
        /// The targets to check now, in <c>ordinal</c> order (section 5): enabled, valid, not already running and
        /// - while offline - not needing the network. A target is due when it was never checked, when a check was
        /// requested, or when its interval has run from the <b>completion</b> of the previous one (so checks never
        /// overlap); an open failure streak shortens the wait to <c>min(interval, 10 s)</c> (D2). A manual-only
        /// target is due only at start and on request.
        /// </summary>
        public List<TrafficLightTarget> CollectDue()
        {
            DateTime now = _utcNow();
            var due = new List<TrafficLightTarget>();
            lock (_gate)
            {
                foreach (Entry e in _entries)
                {
                    if (!e.Target.Enabled || e.InFlight) continue;
                    if (TrafficLightAddress.Validate(e.Target) != AddressIssue.None) continue;
                    if (_offline && e.Target.NeedsNetwork(_isRemotePath)) continue;
                    if (IsDue(e, now)) due.Add(e.Target);
                }
            }
            return due;
        }

        private static bool IsDue(Entry e, DateTime now)
        {
            if (e.ForceDue || e.CheckedAtUtc == null) return true;
            if (e.Target.ManualOnly) return false;
            TimeSpan wait = TimeSpan.FromSeconds(e.Target.IntervalSeconds);
            if (e.FailureStreak > 0 && e.FailureStreak < e.Target.FailuresBeforeDown && wait > ConfirmationDelay)
                wait = ConfirmationDelay;
            return e.CheckedAtUtc.Value + wait <= now;
        }

        /// <summary>The run was handed to a worker. Returns the generation the result must come back with, or -1.</summary>
        public int MarkDispatched(string id) => MarkDispatched(id, null);

        public int MarkDispatched(TrafficLightTarget target) => MarkDispatched(target.Id, target);

        private int MarkDispatched(string id, TrafficLightTarget? selected)
        {
            lock (_gate)
            {
                Entry? e = Find(id);
                if (e == null || e.InFlight || (selected != null && !ReferenceEquals(e.Target, selected))) return -1;
                e.InFlight = true;
                e.FlightReported = false;
                e.ForceDue = false;
                // The flight is silent when a command alone asked for it; every command waiting for the next check
                // becomes the waiter of this very flight (S0117).
                e.FlightSilent = e.CommandOnlyDue;
                e.CommandOnlyDue = false;
                if (e.Waiters != null)
                    foreach (Waiter w in e.Waiters)
                        if (w.Generation < 0) w.Generation = e.Generation;
                return e.Generation;
            }
        }

        /// <summary>
        /// The call really returned (or was never started): the target may be dispatched again. A hard timeout
        /// completes the check long before this, which is the whole point of keeping the two apart (D22). A command
        /// still waiting for this flight at this point will not hear of it any more: it is let go (S0117).
        /// </summary>
        public void Release(string id, int generation)
        {
            List<Waiter>? left = null;
            lock (_gate)
            {
                Entry? e = Find(id);
                if (e != null && e.Generation == generation)
                {
                    e.InFlight = false;
                    e.FlightReported = false;
                    e.FlightSilent = false;
                    left = TakeWaiters(e, w => w.Generation == generation);
                }
            }
            Settle(left, TrafficCheckOutcome.Abandoned);
        }

        /// <summary>
        /// Applies one completed check. Returns the settled change of the light, or null when the light did not
        /// change - including a failure that has not yet reached <c>failuresBeforeDown</c> (the tile keeps its
        /// previous state, D2) and a result for a target that was edited or removed while it ran.
        /// </summary>
        public TrafficLightChange? Complete(string id, int generation, ProbeOutcome outcome)
        {
            TrafficLightChange? change;
            List<Waiter> done;
            lock (_gate) change = CompleteLocked(id, generation, outcome, out done);
            // The commands that waited for this very check hear of it only now, with the result already in place (S0117).
            Settle(done, TrafficCheckOutcome.Completed);
            return change;
        }

        private TrafficLightChange? CompleteLocked(string id, int generation, ProbeOutcome outcome, out List<Waiter> done)
        {
            done = new List<Waiter>();
            DateTime now = _utcNow();
            Entry? e = Find(id);
            if (e == null || e.Generation != generation) return null;
            e.FlightReported = true;

            Judge(e.Target, outcome, out TrafficState verdict, out string reason);
            e.CheckedAtUtc = now;
            e.LatencyMs = outcome.Reached ? outcome.LatencyMs : (int?)null;
            e.Reason = reason;
            e.Note = outcome.Note ?? "";
            done = TakeWaiters(e, w => w.Generation == generation);
            bool silent = e.FlightSilent;
            e.FlightSilent = false;

            TrafficState before = e.State;
            TrafficState settled = before;
            if (verdict == TrafficState.Down)
            {
                if (e.FailureStreak == 0) e.FirstFailureUtc = now;
                e.FailureStreak++;
                if (e.FailureStreak >= Math.Max(1, e.Target.FailuresBeforeDown)) settled = TrafficState.Down;
            }
            else
            {
                e.FailureStreak = 0;
                settled = verdict;
                if (verdict == TrafficState.Ok || verdict == TrafficState.Degraded) e.LastOkAtUtc = now;
            }

            if (settled == before)
            {
                return null;
            }

            var change = new TrafficLightChange
            {
                Target = e.Target,
                From = before,
                To = settled,
                Reason = reason,
                LatencyMs = e.LatencyMs,
                AtUtc = now,
                IsBaseline = before == TrafficState.Unknown,
                WasFor = e.StateSinceUtc.HasValue ? now - e.StateSinceUtc.Value : (TimeSpan?)null,
                FirstFailureUtc = settled == TrafficState.Down ? e.FirstFailureUtc : (DateTime?)null,
                WhileOffline = _offline && e.Target.NeedsNetwork(_isRemotePath),
                Silent = silent,
            };
            e.State = settled;
            e.StateSinceUtc = now;
            if (settled != TrafficState.Down) e.FirstFailureUtc = null;
            return change;
        }

        private static void Judge(TrafficLightTarget target, ProbeOutcome outcome, out TrafficState state, out string reason)
            => TrafficLightVerdict.Judge(target, outcome, out state, out reason);

        // ---- reading ----

        /// <summary>Every target in board order with its last known result.</summary>
        public List<TrafficLightTile> Snapshot()
        {
            DateTime now = _utcNow();
            var tiles = new List<TrafficLightTile>();
            lock (_gate)
            {
                foreach (Entry e in _entries)
                {
                    bool needsNetwork = e.Target.NeedsNetwork(_isRemotePath);
                    bool invalid = TrafficLightAddress.Validate(e.Target) != AddressIssue.None;
                    var tile = new TrafficLightTile
                    {
                        Target = e.Target,
                        State = e.State,
                        Reason = invalid && e.CheckedAtUtc == null ? TrafficReasons.InvalidAddress : e.Reason,
                        Note = e.Note,
                        CheckedAtUtc = e.CheckedAtUtc,
                        LatencyMs = e.LatencyMs,
                        LastOkAtUtc = e.LastOkAtUtc,
                        StateSinceUtc = e.StateSinceUtc,
                        FailureStreak = e.FailureStreak,
                        Checking = e.InFlight,
                        InvalidAddress = invalid,
                        PausedOffline = _offline && needsNetwork && e.Target.Enabled,
                        NextDueUtc = NextDue(e, now),
                    };
                    tile.Stale = IsStale(e, now, _offline && needsNetwork);
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        private static DateTime? NextDue(Entry e, DateTime now)
        {
            if (!e.Target.Enabled || e.Target.ManualOnly || e.CheckedAtUtc == null) return null;
            TimeSpan wait = TimeSpan.FromSeconds(e.Target.IntervalSeconds);
            if (e.FailureStreak > 0 && e.FailureStreak < e.Target.FailuresBeforeDown && wait > ConfirmationDelay)
                wait = ConfirmationDelay;
            return e.CheckedAtUtc.Value + wait;
        }

        /// <summary>
        /// Stale is derived, not stored (section 4): older than three intervals, a manual-only target older than an
        /// hour, or a network target while the computer is offline. A target never checked is gray, not stale.
        /// </summary>
        private static bool IsStale(Entry e, DateTime now, bool offlineForThisTarget)
        {
            if (e.CheckedAtUtc == null) return false;
            if (offlineForThisTarget) return true;
            TimeSpan limit = e.Target.ManualOnly
                ? ManualOnlyStaleAfter
                : TimeSpan.FromSeconds((double)e.Target.IntervalSeconds * StaleIntervals);
            return now - e.CheckedAtUtc.Value > limit;
        }

        public TrafficLightSummary Summary()
        {
            var summary = new TrafficLightSummary();
            lock (_gate)
            {
                summary.Offline = _offline;
                foreach (Entry e in _entries)
                {
                    if (!e.Target.Enabled) continue;
                    summary.Total++;
                    switch (e.State)
                    {
                        case TrafficState.Down: summary.Down++; break;
                        case TrafficState.Degraded: summary.Degraded++; break;
                        case TrafficState.Unknown: summary.Unknown++; break;
                    }
                }
            }
            return summary;
        }

        private Entry? Find(string id)
        {
            foreach (Entry e in _entries)
                if (e.Target.Id == id) return e;
            return null;
        }

        /// <summary>The target as the engine holds it (a copy of the list's row), or null.</summary>
        public TrafficLightTarget? TargetOf(string id)
        {
            lock (_gate) return Find(id)?.Target;
        }
    }

    /// <summary>
    /// The balloon rules of D14, pure. A settled change <b>into</b> and <b>out of</b> <c>down</c> on a target
    /// with <c>notify</c> counts; a baseline, a change while offline and anything between ok and degraded does
    /// not. Changes are coalesced: at most one balloon per interval, naming up to three labels per direction
    /// and "and N more". The text is the caller's - this class decides only <i>whether</i> and <i>who</i>.
    /// </summary>
    internal sealed class TrafficLightNotifier
    {
        public const int MaxNames = 3;
        public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(15);

        private readonly List<string> _down = new List<string>();
        private readonly List<string> _up = new List<string>();
        private readonly Func<TrafficLightTarget, string> _labelOf;
        private DateTime _lastShownUtc = DateTime.MinValue;

        /// <param name="labelOf">
        /// The name a balloon uses for a target - the label, and never the address (D9: a balloon is drawn
        /// by Windows and cannot be kept out of a screen share). The default is the label or a bare question
        /// mark; the context passes the localized "(no label)".
        /// </param>
        public TrafficLightNotifier(Func<TrafficLightTarget, string>? labelOf = null)
        {
            _labelOf = labelOf ?? (t => string.IsNullOrWhiteSpace(t.Label) ? "?" : t.Label.Trim());
        }

        /// <summary>
        /// True when this change should be remembered for a balloon. A change settled by a check that only a command asked
        /// for (<see cref="TrafficLightChange.Silent"/>, ticket S0117) never is: the journal and the board record it, nobody
        /// is told by a balloon.
        /// </summary>
        public static bool Counts(TrafficLightChange change)
        {
            if (change.Silent || !change.Target.Notify || change.IsBaseline || change.WhileOffline) return false;
            bool intoDown = change.To == TrafficState.Down && change.From != TrafficState.Down;
            bool outOfDown = change.From == TrafficState.Down && change.To != TrafficState.Down && change.To != TrafficState.Unknown;
            return intoDown || outOfDown;
        }

        /// <summary>Remembers the change if it counts. The label, never the address (D9).</summary>
        public bool Add(TrafficLightChange change)
        {
            if (!Counts(change)) return false;
            string label = _labelOf(change.Target);
            lock (_down)
            {
                if (change.To == TrafficState.Down) { _up.Remove(label); _down.Add(label); }
                else { _down.Remove(label); _up.Add(label); }
            }
            return true;
        }

        /// <summary>
        /// The balloon to show now, or null: something is pending and the gap since the last balloon has passed.
        /// Taking it clears the pending set.
        /// </summary>
        public TrafficLightBalloon? TryTake(DateTime nowUtc)
        {
            lock (_down)
            {
                if (_down.Count == 0 && _up.Count == 0) return null;
                if (nowUtc - _lastShownUtc < MinGap) return null;
                var balloon = new TrafficLightBalloon();
                Fill(_down, balloon.DownLabels, out balloon.DownMore);
                Fill(_up, balloon.UpLabels, out balloon.UpMore);
                _down.Clear();
                _up.Clear();
                _lastShownUtc = nowUtc;
                return balloon;
            }
        }

        private static void Fill(List<string> source, List<string> target, out int more)
        {
            int take = Math.Min(MaxNames, source.Count);
            for (int i = 0; i < take; i++) target.Add(source[i]);
            more = source.Count - take;
        }

        public void Clear()
        {
            lock (_down) { _down.Clear(); _up.Clear(); }
        }
    }

    /// <summary>One coalesced balloon: who went down, who came back, and how many more of each.</summary>
    internal sealed class TrafficLightBalloon
    {
        public readonly List<string> DownLabels = new List<string>();
        public int DownMore;
        public readonly List<string> UpLabels = new List<string>();
        public int UpMore;
    }
}
