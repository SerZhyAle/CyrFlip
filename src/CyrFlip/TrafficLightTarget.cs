using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The seven kinds of target the traffic light watches (ticket S0069, D13). The stored value is the
    /// lower-case token, never a translated word.
    /// </summary>
    internal static class TrafficLightKinds
    {
        public const string Ping = "ping";
        public const string Tcp = "tcp";
        public const string Http = "http";
        public const string Folder = "folder";
        public const string Drive = "drive";
        public const string Sftp = "sftp";
        public const string Exchange = "exchange";

        /// <summary>Every kind, in the order the editor offers them.</summary>
        public static readonly string[] All = { Ping, Tcp, Http, Folder, Drive, Sftp, Exchange };

        public static bool IsKnown(string? kind)
        {
            foreach (string known in All)
                if (string.Equals(known, kind, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The kinds whose address is <c>host:port</c> (the port is part of the address).</summary>
        public static bool HasPort(string? kind) => kind == Tcp || kind == Sftp || kind == Exchange;

        /// <summary>
        /// True for the kinds that need a network at all. <c>folder</c> and <c>drive</c> are network kinds only
        /// when their path is remote (<see cref="TrafficLightTarget.NeedsNetwork"/> decides, D23).
        /// </summary>
        public static bool IsNetwork(string? kind)
            => kind == Ping || kind == Tcp || kind == Http || kind == Sftp || kind == Exchange;

        /// <summary>Hard limit of one attempt for a kind, section 4 (ms).</summary>
        public static int DefaultTimeoutMs(string? kind)
        {
            switch (kind)
            {
                case Ping: return 1500;
                case Tcp: return 2000;
                case Http: return 5000;
                case Folder:
                case Drive: return 3000;
                case Sftp: return 3000;
                case Exchange: return 4000;
                default: return 3000;
            }
        }

        /// <summary>Latency above this is degraded, section 4 (ms).</summary>
        public static int DefaultYellowMs(string? kind)
        {
            switch (kind)
            {
                case Ping: return 500;
                case Tcp: return 1000;
                case Http: return 2000;
                case Folder:
                case Drive: return 1500;
                case Sftp: return 1500;
                case Exchange: return 2000;
                default: return 1500;
            }
        }
    }

    /// <summary>
    /// One thing the user asked the traffic light to watch (ticket S0069 section 4). Persisted as JSON in one
    /// registry value, the <c>LayoutConversionProfiles</c> pattern, so every member is a plain property.
    /// </summary>
    internal sealed class TrafficLightTarget
    {
        /// <summary>The shortest interval a target may poll at - a typo guard, not a speed limit (D4).</summary>
        public const int MinIntervalSeconds = 2;
        public const int DefaultIntervalSeconds = 60;
        /// <summary>The longest hard limit of one attempt.</summary>
        public const int MaxTimeoutMs = 10000;
        public const int MaxFailuresBeforeDown = 100;
        public const string DefaultAcceptedStatusCodes = "200-399";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = "";
        public string Kind { get; set; } = TrafficLightKinds.Ping;
        public string Address { get; set; } = "";
        /// <summary><c>exchange</c> only, optional: <c>SHA256:</c> + 43 base64 characters.</summary>
        public string Fingerprint { get; set; } = "";
        /// <summary>A free-text tag, not an object; empty = ungrouped.</summary>
        public string Group { get; set; } = "";
        public int Ordinal { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>Seconds between checks; <c>0</c> = manual only, otherwise at least <see cref="MinIntervalSeconds"/>.</summary>
        public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
        /// <summary>Hard limit of one attempt in ms; <c>0</c> = the kind's default (the value follows a change of kind).</summary>
        public int TimeoutMs { get; set; }
        /// <summary>Latency above this is degraded, in ms; <c>0</c> = the kind's default.</summary>
        public int YellowThresholdMs { get; set; }
        public int FailuresBeforeDown { get; set; } = 1;
        /// <summary><c>http</c> only, ranges and lists such as <c>200-399,401</c>.</summary>
        public string AcceptedStatusCodes { get; set; } = DefaultAcceptedStatusCodes;
        /// <summary><c>http</c> only: proceed past a certificate Windows does not trust, with a note (D8).</summary>
        public bool AllowUntrustedCertificate { get; set; }
        /// <summary>A tray balloon on a settled change into and out of <c>down</c> (D14).</summary>
        public bool Notify { get; set; }
        /// <summary>Write this target's settled changes to the text journal (D19).</summary>
        public bool RecordHistory { get; set; }

        [ScriptIgnore]
        public int EffectiveTimeoutMs
            => Math.Max(1, Math.Min(MaxTimeoutMs, TimeoutMs > 0 ? TimeoutMs : TrafficLightKinds.DefaultTimeoutMs(Kind)));

        [ScriptIgnore]
        public int EffectiveYellowMs => YellowThresholdMs > 0 ? YellowThresholdMs : TrafficLightKinds.DefaultYellowMs(Kind);

        [ScriptIgnore]
        public bool ManualOnly => IntervalSeconds <= 0;

        /// <summary>
        /// Whether a check of this target needs the network (D23): the five network kinds always, a folder or a
        /// drive only when its path is remote, decided from the text alone (<see cref="LaunchTargets.IsRemotePath"/>).
        /// </summary>
        public bool NeedsNetwork(Func<string, bool>? isRemotePath = null)
        {
            if (TrafficLightKinds.IsNetwork(Kind)) return true;
            Func<string, bool> remote = isRemotePath ?? (p => LaunchTargets.IsRemotePath(p));
            if (Kind == TrafficLightKinds.Folder) return remote(Address);
            // A drive letter that is a mapped share is somebody else's disk: asking it while offline only holds
            // a worker for the SMB timeout. The letter is looked at, not the disk (GetDriveType, no I/O).
            if (Kind == TrafficLightKinds.Drive) return remote(TrafficLightAddress.NormalizeDrive(Address));
            return false;
        }

        public TrafficLightTarget Clone() => new TrafficLightTarget
        {
            Id = Id, Label = Label, Kind = Kind, Address = Address, Fingerprint = Fingerprint, Group = Group,
            Ordinal = Ordinal, Enabled = Enabled, IntervalSeconds = IntervalSeconds, TimeoutMs = TimeoutMs,
            YellowThresholdMs = YellowThresholdMs, FailuresBeforeDown = FailuresBeforeDown,
            AcceptedStatusCodes = AcceptedStatusCodes, AllowUntrustedCertificate = AllowUntrustedCertificate,
            Notify = Notify, RecordHistory = RecordHistory,
        };

        /// <summary>
        /// A new target of <paramref name="kind"/> with that kind's defaults (what Enter adds, D24). The label
        /// is the caller's - it comes from the recognizer or the editor.
        /// </summary>
        public static TrafficLightTarget Create(string kind, string address, string label, int ordinal)
            => new TrafficLightTarget { Kind = kind, Address = address, Label = label, Ordinal = ordinal };
    }

    /// <summary>The persisted list: reading, repairing, ordering and the per-list rules.</summary>
    internal static class TrafficLightTargetList
    {
        /// <summary>The most targets one board holds. A board is a glance, not an inventory.</summary>
        public const int MaxTargets = 200;
        public const int MaxLabelLength = 64;
        public const int MaxGroupLength = 40;

        /// <summary>
        /// Makes a deserialized list safe to use (the <c>SanitizeConversionProfiles</c> contract): a null row is
        /// dropped, a null string becomes <c>""</c>, an id that is missing or repeated is regenerated, numbers are
        /// brought into range and a list past <see cref="MaxTargets"/> is cut. An address that fails validation is
        /// <b>not</b> repaired here - it is kept as the user (or the file) wrote it and refused at run time
        /// (section 7), so nothing is rewritten behind the user's back. Returns true when anything changed.
        /// </summary>
        public static bool Sanitize(List<TrafficLightTarget>? rows)
        {
            if (rows == null) return false;
            bool changed = rows.RemoveAll(r => r == null) > 0;
            if (rows.Count > MaxTargets)
            {
                rows.RemoveRange(MaxTargets, rows.Count - MaxTargets);
                changed = true;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TrafficLightTarget row in rows)
            {
                if (row.Label == null) { row.Label = ""; changed = true; }
                if (row.Kind == null) { row.Kind = ""; changed = true; }
                if (row.Address == null) { row.Address = ""; changed = true; }
                if (row.Fingerprint == null) { row.Fingerprint = ""; changed = true; }
                if (row.Group == null) { row.Group = ""; changed = true; }
                if (row.AcceptedStatusCodes == null) { row.AcceptedStatusCodes = TrafficLightTarget.DefaultAcceptedStatusCodes; changed = true; }

                // Numbers follow what the editor can take: 0 means manual only, anything else at least the floor.
                int interval = row.IntervalSeconds;
                if (interval < 0) interval = 0;
                else if (interval > 0 && interval < TrafficLightTarget.MinIntervalSeconds) interval = TrafficLightTarget.MinIntervalSeconds;
                else if (interval > 7 * 24 * 3600) interval = 7 * 24 * 3600;
                if (interval != row.IntervalSeconds) { row.IntervalSeconds = interval; changed = true; }

                int timeout = Math.Max(0, Math.Min(TrafficLightTarget.MaxTimeoutMs, row.TimeoutMs));
                if (timeout != row.TimeoutMs) { row.TimeoutMs = timeout; changed = true; }
                int yellow = Math.Max(0, Math.Min(10 * 60 * 1000, row.YellowThresholdMs));
                if (yellow != row.YellowThresholdMs) { row.YellowThresholdMs = yellow; changed = true; }
                int failures = Math.Max(1, Math.Min(TrafficLightTarget.MaxFailuresBeforeDown, row.FailuresBeforeDown));
                if (failures != row.FailuresBeforeDown) { row.FailuresBeforeDown = failures; changed = true; }

                if (string.IsNullOrEmpty(row.Id) || !ids.Add(row.Id))
                {
                    string id;
                    do { id = Guid.NewGuid().ToString("N"); } while (!ids.Add(id));
                    row.Id = id;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>The list in board order: <c>ordinal</c>, ties broken by the stored position (a stable sort).</summary>
        public static List<TrafficLightTarget> InOrder(IEnumerable<TrafficLightTarget> targets)
        {
            var indexed = new List<KeyValuePair<int, TrafficLightTarget>>();
            int position = 0;
            foreach (TrafficLightTarget target in targets)
                if (target != null) indexed.Add(new KeyValuePair<int, TrafficLightTarget>(position++, target));
            indexed.Sort((a, b) =>
            {
                int byOrdinal = a.Value.Ordinal.CompareTo(b.Value.Ordinal);
                return byOrdinal != 0 ? byOrdinal : a.Key.CompareTo(b.Key);
            });
            var ordered = new List<TrafficLightTarget>(indexed.Count);
            foreach (KeyValuePair<int, TrafficLightTarget> pair in indexed) ordered.Add(pair.Value);
            return ordered;
        }

        /// <summary>
        /// The groups in the order of their first member in <c>ordinal</c> order, each with its members in
        /// <c>ordinal</c> order (section 4). The ungrouped targets form the group with the empty name, placed
        /// where its first member is - like any other.
        /// </summary>
        public static List<KeyValuePair<string, List<TrafficLightTarget>>> Grouped(IEnumerable<TrafficLightTarget> targets)
        {
            var groups = new List<KeyValuePair<string, List<TrafficLightTarget>>>();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (TrafficLightTarget target in InOrder(targets))
            {
                string name = (target.Group ?? "").Trim();
                if (!index.TryGetValue(name, out int at))
                {
                    at = groups.Count;
                    index[name] = at;
                    groups.Add(new KeyValuePair<string, List<TrafficLightTarget>>(name, new List<TrafficLightTarget>()));
                }
                groups[at].Value.Add(target);
            }
            return groups;
        }

        /// <summary>Rewrites <c>ordinal</c> to 0..n-1 in the current order (after an add, a remove or a move).</summary>
        public static void Renumber(List<TrafficLightTarget> targets)
        {
            List<TrafficLightTarget> ordered = InOrder(targets);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Ordinal = i;
            targets.Clear();
            targets.AddRange(ordered);
        }

        /// <summary>
        /// Moves one target one place up or down in the board order (D12). Only <c>ordinal</c> changes, so a
        /// move across a group boundary changes the position, never the group. False at the end of the list.
        /// </summary>
        public static bool Move(List<TrafficLightTarget> targets, string id, int direction)
        {
            Renumber(targets);
            int from = targets.FindIndex(t => t.Id == id);
            int to = from + (direction < 0 ? -1 : 1);
            if (from < 0 || to < 0 || to >= targets.Count) return false;
            int ordinal = targets[from].Ordinal;
            targets[from].Ordinal = targets[to].Ordinal;
            targets[to].Ordinal = ordinal;
            Renumber(targets);
            return true;
        }

        public static string Serialize(IEnumerable<TrafficLightTarget> targets)
            => new JavaScriptSerializer().Serialize(new List<TrafficLightTarget>(targets));

        /// <summary>Malformed JSON is an empty list, never an exception.</summary>
        public static List<TrafficLightTarget> Read(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<TrafficLightTarget>();
            try
            {
                var rows = new JavaScriptSerializer().Deserialize<List<TrafficLightTarget>>(json!);
                if (rows == null) return new List<TrafficLightTarget>();
                Sanitize(rows);
                return rows;
            }
            catch { return new List<TrafficLightTarget>(); }
        }
    }
}
