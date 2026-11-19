using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// What the two traffic light commands read and ask for (ticket S0117): the board's last known states and one check
    /// of one target. The seam exists so the handlers are tested with a fake engine and a fake scheduler; the running
    /// copy supplies <see cref="TrafficLightCommandHost"/> over the live ones, or nothing while the module is off.
    /// </summary>
    internal interface ITrafficLightCommandHost
    {
        /// <summary>Every target in board order with its last known result - the model the board draws, read without starting a check.</summary>
        List<TrafficLightTile> Snapshot();

        /// <summary>Asks for one check only while the selected target remains current.</summary>
        TrafficCheckTicket RequestCheck(TrafficLightTarget target);
    }

    /// <summary>The live engine and scheduler behind the two commands.</summary>
    internal sealed class TrafficLightCommandHost : ITrafficLightCommandHost
    {
        private readonly TrafficLightEngine _engine;
        private readonly TrafficLightScheduler _scheduler;

        public TrafficLightCommandHost(TrafficLightEngine engine, TrafficLightScheduler scheduler)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public List<TrafficLightTile> Snapshot() => _engine.Snapshot();
        public TrafficCheckTicket RequestCheck(TrafficLightTarget target) => _scheduler.RequestCommandCheck(target);
    }

    /// <summary>
    /// Whether a target's label may leave CyrFlip in the answer of a command (ticket S0117, owner decision of 2026-11-17).
    /// The board is excluded from every capture because it is a map of the person's network; a script's answer is the same
    /// map in a file, so it carries labels and never addresses. The recognizer and the editor often make the label out of the
    /// address (a host, an IP, the last folder of a path, a drive letter), and such a label <i>is</i> the address. A label is
    /// therefore withheld when any word of it is the target's own address, host, port, a segment of its path or its drive, when
    /// it holds the whole address or a fingerprint, when it is written like a URL, a path or a mailbox, or when a word of it
    /// reads as an IP address, a <c>host:port</c> or a web host name. The test is deliberately strict and deliberately blunt:
    /// a person who sees a target missing from an answer renames it once.
    /// </summary>
    internal static class TrafficLightLabels
    {
        private static readonly char[] Separators =
            { ' ', '\t', '\r', '\n', ',', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '<', '>', '|', '=', '!', '?', '*', '-', '_' };
        private static readonly char[] EdgeMarks = { '.', ':' };
        private static readonly char[] PathMarks = { '\\', '/', '@' };
        private static readonly char[] AddressMarks = { '.', ':', '-', '\\', '/' };

        /// <summary>True when the target's label says nothing its address says: it may be given to a script.</summary>
        public static bool IsSafe(TrafficLightTarget target)
        {
            return IsSafe(target.Label, Identifiers(target), new[] { target.Address ?? "" });
        }

        public static bool AnyUnsafe(IEnumerable<TrafficLightTile> selected, IEnumerable<TrafficLightTile> board)
        {
            var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new List<string>();
            foreach (TrafficLightTile tile in board)
            {
                identifiers.UnionWith(Identifiers(tile.Target));
                addresses.Add(tile.Target.Address ?? "");
            }
            return selected.Any(tile => !IsSafe(tile.Target.Label, identifiers, addresses));
        }

        private static bool IsSafe(string? value, HashSet<string> identifiers, IEnumerable<string> addresses)
        {
            string label = (value ?? "").Trim();
            if (label.Length == 0) return true;

            // What only an address, a URL, a path or a mailbox is written with.
            if (label.IndexOfAny(PathMarks) >= 0) return false;
            if (label.IndexOf("SHA256:", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // The whole address inside the label. A short plain word ("nas") is left to the word test below: as a substring
            // it would also be found inside an unrelated word.
            foreach (string item in addresses)
            {
                string address = item.Trim();
                if ((address.Length >= 5 || address.IndexOfAny(AddressMarks) >= 0) && address.Length >= 3
                    && label.IndexOf(address, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }

            foreach (string raw in label.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = raw.Trim(EdgeMarks);
                if (identifiers.Contains(raw) || (word.Length > 0 && identifiers.Contains(word))) return false;
                if (LooksLikeAddress(word)) return false;
            }
            return true;
        }

        private static bool LooksLikeAddress(string word)
        {
            if (word.Length == 0) return false;
            if (TrafficLightAddress.IsIpv4(word) || TrafficLightAddress.IsIpv6Literal(word)) return true;
            if (TrafficLightAddress.TryParseHostPort(word, out _, out _)) return true;
            return LaunchTargets.IsWebHost(word);
        }

        /// <summary>Every piece of the target's address that a label must not repeat: the text, the host, the port, the path's segments, the drive, the fingerprint.</summary>
        private static HashSet<string> Identifiers(TrafficLightTarget target)
        {
            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string address = (target.Address ?? "").Trim();
            Remember(own, address);

            string host = TrafficLightAddress.HostOf(target.Kind, address);
            Remember(own, host);
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Length > 4) Remember(own, host.Substring(4));
            Remember(own, target.Fingerprint);

            switch (target.Kind)
            {
                case TrafficLightKinds.Tcp:
                case TrafficLightKinds.Sftp:
                case TrafficLightKinds.Exchange:
                    if (TrafficLightAddress.TryParseHostPort(address, out string portHost, out int port))
                    {
                        Remember(own, portHost);
                        Remember(own, port.ToString(CultureInfo.InvariantCulture));
                    }
                    break;
                case TrafficLightKinds.Http:
                    if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri != null)
                    {
                        Remember(own, uri.Authority);
                        foreach (string segment in uri.AbsolutePath.Split('/')) Remember(own, segment);
                    }
                    break;
                case TrafficLightKinds.Folder:
                    foreach (string segment in address.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) Remember(own, segment);
                    break;
                case TrafficLightKinds.Drive:
                    string drive = TrafficLightAddress.NormalizeDrive(address);
                    Remember(own, drive);
                    if (drive.Length == 2) Remember(own, drive.Substring(0, 1));
                    break;
            }
            return own;
        }

        private static void Remember(HashSet<string> own, string? value)
        {
            string text = (value ?? "").Trim().Trim('\\', '/');
            if (text.Length > 0) own.Add(text);
        }
    }

    /// <summary>
    /// The two verbs of the availability traffic light (ticket S0117, APP-COMMANDS 0.2): <c>trafficlight status</c> reads
    /// what the board knows and <c>trafficlight check</c> asks for fresh looks and waits for them. Each is a pure function over
    /// the parsed invocation and a provider of the live engine and scheduler (null is a switched-off module, 11 <c>module-off</c>
    /// with nothing created, read or started), so the tests need no context and no window.
    ///
    /// <para>Three rules carry both. <b>Labels, never addresses</b>: a target is identified in an answer by its label alone,
    /// and a request that includes a target with an unsafe label (<see cref="TrafficLightLabels"/>) is refused without
    /// echoing the label. <b>A label selects a target</b>: <c>--target</c> is the whole label,
    /// compared without regard to case after trimming, as <c>scenario run --name</c> compares a name; an id or an address
    /// never selects. <b>Nothing is probed that the person did not list</b>, and a check is the engine's own: a switched-off
    /// target, an address that does not validate, a network target while offline and a target whose earlier call is still
    /// out after its hard timeout are not checked, and are named.</para>
    /// </summary>
    internal static class AppCommandsTrafficLight
    {
        /// <summary>The longest label an answer carries (the editor's own bound); a longer one imported by hand is cut.</summary>
        public const int LabelChars = TrafficLightTargetList.MaxLabelLength;
        /// <summary>How many labels one message names before it says "+N".</summary>
        public const int MaxNamed = 10;

        // ------------------------------------------------------------------ trafficlight status

        public static AppCommandResponse Status(AppCommandInvocation invocation, Func<ITrafficLightCommandHost?> provider, Func<string, string> T)
        {
            try
            {
                string? wanted = invocation.Option("target");
                if (wanted != null && wanted.Trim().Length == 0)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("Give the label of a target with --target."));

                ITrafficLightCommandHost? host = provider();
                if (host == null) return ModuleOff(T);
                List<TrafficLightTile> tiles = host.Snapshot();

                List<TrafficLightTile> shown;
                if (wanted != null)
                {
                    shown = Matching(tiles, wanted);
                }
                else
                {
                    shown = tiles;
                }
                if (TrafficLightLabels.AnyUnsafe(shown, tiles)) return Unsafe(T);

                object[] entries = Entries(shown);
                var data = new Dictionary<string, object>
                {
                    ["count"] = entries.Length,
                    ["tiles"] = entries,
                };

                string message = wanted != null && shown.Count == 0
                    ? T("No target has this label.")
                    : Localization.Format(T, "Traffic light targets: {0}.", entries.Length);
                return BoundedAnswer(message, data, T);
            }
            catch (Exception ex)
            {
                return Crashed("trafficlight status", ex, T);
            }
        }

        // ------------------------------------------------------------------ trafficlight check

        /// <summary>
        /// Order: the arguments (10) -> the module (11) -> the choice of targets (a refused label is 11) -> one request for
        /// each (what the engine will not probe is named, not asked again) -> the wait, under the token and never blocking the
        /// thread that pumps the keyboard hook -> the fresh tiles of the checks that completed. Past the token the answer is 2
        /// <c>timeout</c> and the waits are dropped; the checks themselves run on and the engine applies them as it always does.
        /// </summary>
        public static async Task<AppCommandResponse> Check(AppCommandInvocation invocation, Func<ITrafficLightCommandHost?> provider,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                string? wanted = invocation.Option("target");
                bool all = invocation.Flag("all");
                if (wanted != null && wanted.Trim().Length == 0)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("Give the label of a target with --target."));
                if (wanted == null && !all)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("Give --target with a label, or --all."));

                ITrafficLightCommandHost? host = provider();
                if (host == null) return ModuleOff(T);
                List<TrafficLightTile> tiles = host.Snapshot();

                List<TrafficLightTile> chosen;
                if (wanted != null)
                {
                    chosen = Matching(tiles, wanted);
                    if (chosen.Count == 0)
                        return AppCommandResponse.Done(T("No target has this label."), Answer(new List<TrafficLightTile>(), 0));
                }
                else
                {
                    chosen = tiles;
                }
                if (TrafficLightLabels.AnyUnsafe(chosen, tiles)) return Unsafe(T);

                var started = new List<KeyValuePair<TrafficLightTile, TrafficCheckTicket>>();
                var notChecked = new List<string>();
                foreach (TrafficLightTile tile in chosen)
                {
                    TrafficCheckTicket ticket = host.RequestCheck(tile.Target);
                    if (ticket.Start == TrafficCheckStart.Started) started.Add(new KeyValuePair<TrafficLightTile, TrafficCheckTicket>(tile, ticket));
                    else notChecked.Add(Shown(tile.Target.Label, T));
                }

                if (started.Count > 0 && !await AllDone(started.Select(p => (Task)p.Value.Completion), token).ConfigureAwait(false))
                {
                    foreach (KeyValuePair<TrafficLightTile, TrafficCheckTicket> pair in started) pair.Value.Cancel();
                    return TimedOut(T);
                }

                // The checks have been applied: what is read now is theirs, not an older look.
                var fresh = new Dictionary<string, TrafficLightTile>(StringComparer.Ordinal);
                List<TrafficLightTile> current = host.Snapshot();
                foreach (TrafficLightTile tile in current) fresh[tile.Target.Id] = tile;
                var done = new List<TrafficLightTile>();
                foreach (KeyValuePair<TrafficLightTile, TrafficCheckTicket> pair in started)
                {
                    if (pair.Value.Completion.Result == TrafficCheckOutcome.Completed
                        && fresh.TryGetValue(pair.Key.Target.Id, out TrafficLightTile? now))
                    {
                        done.Add(now);
                    }
                    else
                        notChecked.Add(Shown(pair.Key.Target.Label, T));
                }

                if (TrafficLightLabels.AnyUnsafe(done.Concat(chosen), tiles.Concat(current))) return Unsafe(T);
                string message = Localization.Format(T, "Checked {0} of {1}.", done.Count, chosen.Count);
                if (notChecked.Count > 0)
                    message += " " + Localization.Format(T, "Not checked (switched off, not valid, offline, changed or busy): {0}.", Named(notChecked));
                return BoundedAnswer(message, Answer(done, notChecked.Count), T);
            }
            catch (Exception ex)
            {
                return Crashed("trafficlight check", ex, T);
            }
        }

        // ------------------------------------------------------------------ the answer

        private static Dictionary<string, object> Answer(List<TrafficLightTile> done, int notChecked)
        {
            object[] entries = Entries(done);
            var data = new Dictionary<string, object>
            {
                ["count"] = entries.Length,
                ["tiles"] = entries,
            };
            if (notChecked > 0) data["notChecked"] = notChecked;
            return data;
        }

        /// <summary>
        /// The tiles as answer entries. Label, kind, state, stale and the time of the last completed check - nothing of
        /// the address, and not the reason or the note, which can hold a fingerprint.
        /// </summary>
        internal static object[] Entries(IReadOnlyList<TrafficLightTile> tiles)
        {
            var entries = new List<object>();
            foreach (TrafficLightTile tile in tiles)
            {
                entries.Add(Entry(tile));
            }
            return entries.ToArray();
        }

        private static AppCommandResponse BoundedAnswer(string message, Dictionary<string, object> data, Func<string, string> T)
        {
            AppCommandResponse response = AppCommandResponse.Done(message, data);
            response.RequestId = new string('0', 36);
            int bytes = Encoding.UTF8.GetByteCount(AppCommandProtocol.Serialize(response)) + 1;
            response.RequestId = "";
            return bytes <= AppCommandLine.MaxRequestBytes
                ? response
                : AppCommandResponse.Failed("response-too-large", AppCommandExit.Failed,
                    T("The answer is too large. Ask for one target with --target."));
        }

        internal static Dictionary<string, object> Entry(TrafficLightTile tile)
        {
            string label = (tile.Target.Label ?? "").Trim();
            var entry = new Dictionary<string, object>
            {
                ["label"] = label.Length <= LabelChars ? label : label.Substring(0, LabelChars),
                ["kind"] = TrafficLightKinds.IsKnown(tile.Target.Kind) ? tile.Target.Kind : "unknown",
                ["state"] = StateWord(tile),
                ["stale"] = tile.Stale,
                ["lastCheckedUtc"] = tile.CheckedAtUtc.HasValue ? (object)CyrFlipExchangeWriter.Date(tile.CheckedAtUtc.Value) : null!,
            };
            return entry;
        }

        /// <summary>
        /// The board's reading as one invariant word: <c>ok</c>, <c>degraded</c>, <c>down</c> or <c>unknown</c> (never checked),
        /// and two the board draws apart - <c>off</c> for a target the person switched off and <c>invalid</c> for an address
        /// that does not validate - so a script never mistakes a target that nobody keeps up to date for a light.
        /// </summary>
        internal static string StateWord(TrafficLightTile tile)
        {
            if (!tile.Target.Enabled) return "off";
            if (tile.InvalidAddress) return "invalid";
            switch (tile.State)
            {
                case TrafficState.Ok: return "ok";
                case TrafficState.Degraded: return "degraded";
                case TrafficState.Down: return "down";
                default: return "unknown";
            }
        }

        // ------------------------------------------------------------------ shared

        private static List<TrafficLightTile> Matching(List<TrafficLightTile> tiles, string wanted)
        {
            string label = wanted.Trim();
            return tiles.Where(t => string.Equals((t.Target.Label ?? "").Trim(), label, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static string Shown(string? label, Func<string, string> T)
        {
            string text = (label ?? "").Trim();
            return text.Length == 0 ? T("(no label)") : (text.Length <= LabelChars ? text : text.Substring(0, LabelChars));
        }

        private static string Named(List<string> labels)
        {
            string head = string.Join(", ", labels.Take(MaxNamed));
            return labels.Count > MaxNamed ? head + ", +" + (labels.Count - MaxNamed).ToString(CultureInfo.InvariantCulture) : head;
        }

        /// <summary>Waits - under the token, never blocking this thread - for every check; false when the token ended the wait first.</summary>
        private static async Task<bool> AllDone(IEnumerable<Task> checks, CancellationToken token)
        {
            Task all = Task.WhenAll(checks);
            if (all.IsCompleted) return true;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                return await Task.WhenAny(all, cancelled.Task).ConfigureAwait(false) == all;
            }
        }

        /// <summary>The label of a target the command named is also its address: refused (11) without a word of the label.</summary>
        private static AppCommandResponse Unsafe(Func<string, string> T)
            => AppCommandResponse.Refused(AppCommandReasons.LabelUnsafe, AppCommandExit.NotReady,
                T("The label of this target is also its address or a part of it. Give it a label of your own in the settings of CyrFlip, then ask again."));

        private static AppCommandResponse ModuleOff(Func<string, string> T)
            => AppCommandVerbs.ModuleOff(T("The traffic light is switched off. Turn it on in the settings of CyrFlip."));

        private static AppCommandResponse TimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("CyrFlip did not answer in time. It may still be working."));

        /// <summary>A fault after the first await would otherwise fault the task: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }
}
