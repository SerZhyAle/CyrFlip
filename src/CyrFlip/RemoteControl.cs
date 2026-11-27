using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The target half of <c>REMOTE-CONTROL</c> 0.4 that knows no wire and no window (ticket CYRFLP-0140): the clear
    /// payload of a request and of an answer (rule 5), the ring of handled requests and the time window (rule 12), the
    /// grants (rules 9 and 10) and the order in which a request is decided (rules 6 to 13). The sealing is
    /// <see cref="ExchangeE2e"/>'s and the stream <see cref="ExchangeCommandListener"/>'s; this file decides only.
    /// </summary>
    internal static class RemoteControl
    {
        /// <summary>The bound of a clear payload, request or answer (rule 5, item FR).</summary>
        public const int MaxPayloadBytes = 65536;
        public const int MinTtlSeconds = 10;
        public const int MaxTtlSeconds = 600;
        public const int DefaultTtlSeconds = 120;
        /// <summary>The clock difference two devices of one person are allowed (rule 12).</summary>
        public const int ClockSkewSeconds = 300;
        /// <summary>How long a person has to answer a confirmation on the target (rule 10).</summary>
        public const int ConsentSeconds = 60;

        /// <summary>The box contexts of item GD.</summary>
        public static string RequestContext(string requestId) => "command:" + requestId;
        public static string ResultContext(string requestId) => "command-result:" + requestId;

        /// <summary>Item GC: 1 to 64 characters of <c>[A-Za-z0-9_-]</c>.</summary>
        public static bool IsRequestId(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 64) return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        /// <summary>The target's own refusals inside the box (rule 13), beside the tokens of APP-COMMANDS rule 12.</summary>
        public const string NotPermitted = "not-permitted";
        public const string Declined = "declined";
        public const string Expired = "expired";

        /// <summary>The four risk classes of APP-COMMANDS rule 13, the unit a grant is made of (rule 9).</summary>
        public static readonly IReadOnlyList<string> Classes = new[] { "safe", "reads-user-data", "writes-user-data", "leaves-machine" };

        public const string ConsentAlways = "always";
        public const string ConsentAsk = "ask";

        /// <summary>The <c>remote.confirm</c> values of rule 7.</summary>
        public const string ConfirmGrant = "grant";
        public const string ConfirmDevice = "device";

        /// <summary>
        /// Whether an option of a remote verb may be sent from another device: never a path (it names a file of this
        /// PC), never <c>--json</c> or <c>--result</c> (they shape a local caller's console and write a local file).
        /// </summary>
        public static bool IsRemoteOption(AppCommandArg arg)
            => arg.Kind != "path" && !string.Equals(arg.Name, "json", StringComparison.Ordinal)
               && !string.Equals(arg.Name, "result", StringComparison.Ordinal);

        internal static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
    }

    /// <summary>The clear payload of a <c>command-request</c> (rule 5), read strictly: an unknown member is never ignored.</summary>
    internal sealed class RemoteControlRequest
    {
        private static readonly HashSet<string> Members = new HashSet<string>(StringComparer.Ordinal)
            { "v", "verb", "args", "issuedAt", "ttlSeconds" };

        public string Verb { get; private set; } = "";
        public Dictionary<string, object> Args { get; private set; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public DateTime IssuedAtUtc { get; private set; }
        public int TtlSeconds { get; private set; } = RemoteControl.DefaultTtlSeconds;

        /// <summary>The instant from which rule 12's time check refuses this request, and until which the ring keeps it.</summary>
        public DateTime WindowEndUtc => IssuedAtUtc.AddSeconds(TtlSeconds + RemoteControl.ClockSkewSeconds);

        /// <summary>
        /// The request a payload carries; null with <paramref name="reason"/> set to <c>too-large</c>, <c>schema</c> (a
        /// <c>v</c> above 1) or <c>bad-argument</c> (anything else that does not read).
        /// </summary>
        public static RemoteControlRequest? TryParse(byte[] utf8, out string reason)
        {
            reason = AppCommandReasons.BadArgument;
            if (utf8 == null) return null;
            if (utf8.Length > RemoteControl.MaxPayloadBytes) { reason = AppCommandReasons.TooLarge; return null; }
            Dictionary<string, object>? root;
            try { root = RemoteControl.Json().DeserializeObject(new UTF8Encoding(false, true).GetString(utf8)) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (root == null) return null;

            if (!root.TryGetValue("v", out object? v) || !(v is int version)) return null;
            if (version > 1) { reason = AppCommandReasons.Schema; return null; }
            if (version < 1) return null;
            if (root.Keys.Any(k => !Members.Contains(k))) return null;

            if (!(root.TryGetValue("verb", out object? verb) && verb is string name) || name.Length == 0 || name.Length > 64) return null;
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            if (root.TryGetValue("args", out object? rawArgs))
            {
                if (!(rawArgs is Dictionary<string, object> map)) return null;
                foreach (KeyValuePair<string, object> pair in map) args[pair.Key] = pair.Value;
            }
            if (!(root.TryGetValue("issuedAt", out object? issued) && issued is string issuedText)
                || !TryParseUtc(issuedText, out DateTime issuedUtc)) return null;
            int ttl = RemoteControl.DefaultTtlSeconds;
            if (root.TryGetValue("ttlSeconds", out object? rawTtl))
            {
                if (!(rawTtl is int t) || t < RemoteControl.MinTtlSeconds || t > RemoteControl.MaxTtlSeconds) return null;
                ttl = t;
            }
            reason = "";
            return new RemoteControlRequest { Verb = name, Args = args, IssuedAtUtc = issuedUtc, TtlSeconds = ttl };
        }

        /// <summary>RFC 3339 with a zone designator, read as UTC.</summary>
        internal static bool TryParseUtc(string text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(text) || text.Length > 40 || text.IndexOf('T') < 0) return false;
            char last = text[text.Length - 1];
            bool zoned = last == 'Z' || last == 'z' || (text.Length > 6 && (text[text.Length - 6] == '+' || text[text.Length - 6] == '-'));
            if (!zoned) return false;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)) return false;
            utc = parsed.UtcDateTime;
            return true;
        }

        /// <summary>
        /// The time check of rule 12: refused when the window ended before <paramref name="nowUtc"/>, or when the request
        /// was issued more than the allowed clock difference ahead of it.
        /// </summary>
        public bool IsExpired(DateTime nowUtc)
            => WindowEndUtc < nowUtc || IssuedAtUtc > nowUtc.AddSeconds(RemoteControl.ClockSkewSeconds);
    }

    /// <summary>The clear payload of a <c>command-result</c> (rule 5).</summary>
    internal sealed class RemoteControlAnswer
    {
        public string Status { get; set; } = "done";
        public int Exit { get; set; }
        public string? Reason { get; set; }
        /// <summary>One English sentence for the log; a controller shows the words of its own table, never this.</summary>
        public string Message { get; set; } = "";
        public Dictionary<string, object>? Data { get; set; }

        public static RemoteControlAnswer Refused(string reason, int exit, string message)
            => new RemoteControlAnswer { Status = "refused", Exit = exit, Reason = reason, Message = message };

        /// <summary>
        /// The answer of a verb that ran. The response's own message is in the interface language of this PC, so the
        /// answer carries an English sentence of its own made of the status and the reason (rule 5).
        /// </summary>
        public static RemoteControlAnswer From(AppCommandResponse response)
            => new RemoteControlAnswer
            {
                Status = response.Status,
                Exit = response.ExitCode,
                Reason = response.Reason,
                Message = EnglishSentence(response.Status, response.Reason),
                Data = response.Data,
            };

        internal static string EnglishSentence(string status, string? reason)
        {
            switch (status)
            {
                case "done": return "Done.";
                case "accepted": return "Accepted; the work has started.";
                default: return "The command was " + (status == "busy" ? "not taken: busy" : status) + (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ").");
            }
        }

        /// <summary>
        /// The JSON text of the answer, within <see cref="RemoteControl.MaxPayloadBytes"/>: when the data would pass the
        /// bound it is dropped and <c>truncated</c> says so; the other members are bounded by construction.
        /// </summary>
        public string ToJson()
        {
            string full = Serialize(Data, false);
            if (Encoding.UTF8.GetByteCount(full) <= RemoteControl.MaxPayloadBytes) return full;
            return Serialize(null, true);
        }

        private string Serialize(Dictionary<string, object>? data, bool truncated)
        {
            var map = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["status"] = Status,
                ["exit"] = Exit,
                ["message"] = Message.Length <= 300 ? Message : Message.Substring(0, 300),
                ["data"] = (object?)data ?? new Dictionary<string, object>(),
                ["truncated"] = truncated,
            };
            if (!string.IsNullOrEmpty(Reason)) map["reason"] = Reason!;
            return RemoteControl.Json().Serialize(map);
        }

        /// <summary>The <c>status</c> and <c>exit</c> of a stored answer, for the log (null when it does not read).</summary>
        public static bool TryReadOutcome(string json, out string status, out int exit)
        {
            status = ""; exit = -1;
            try
            {
                if (!(RemoteControl.Json().DeserializeObject(json) is Dictionary<string, object> map)) return false;
                status = map.TryGetValue("status", out object? s) && s is string text ? text : "";
                exit = map.TryGetValue("exit", out object? e) && e is int code ? code : -1;
                return status.Length > 0;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    /// <summary>
    /// The requests this target handled, per account entry and controller, with the answer it gave (rule 12): an entry
    /// stays until its request's own window has ended, so a box repeated inside the window is answered from here and
    /// never run again, and the time check refuses it after. Entries leave by time only; a full ring refuses new
    /// requests instead of dropping one early.
    /// </summary>
    internal sealed class RemoteControlRing
    {
        public const int MaxLiveEntries = 10000;
        /// <summary>The answer a request carries in the ring while its verb runs; a crash leaves exactly this.</summary>
        public static readonly string InterruptedAnswer = new RemoteControlAnswer
        {
            Status = "failed", Exit = AppCommandExit.NotProven, Reason = AppCommandReasons.Error,
            Message = "The command was interrupted before it answered.",
        }.ToJson();

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private sealed class Entry
        {
            public long ExpiresUtcTicks;
            public string Answer = "";
        }

        public int Count => _entries.Count;

        private static string KeyOf(string entryId, string controller, string requestId)
            => entryId + "|" + controller + "|" + requestId;

        /// <summary>The stored answer of a request still inside its window, or null.</summary>
        public string? Find(string entryId, string controller, string requestId, DateTime nowUtc)
            => _entries.TryGetValue(KeyOf(entryId, controller, requestId), out Entry? e) && e.ExpiresUtcTicks >= nowUtc.Ticks ? e.Answer : null;

        /// <summary>Whether a new request has room once the ended windows are gone.</summary>
        public bool HasRoom(DateTime nowUtc)
        {
            Prune(nowUtc);
            return _entries.Count < MaxLiveEntries;
        }

        public void Put(string entryId, string controller, string requestId, DateTime expiresUtc, string answer)
            => _entries[KeyOf(entryId, controller, requestId)] = new Entry { ExpiresUtcTicks = expiresUtc.Ticks, Answer = answer };

        /// <summary>Drops the entries whose window ended before <paramref name="nowUtc"/>; returns whether any went.</summary>
        public bool Prune(DateTime nowUtc)
        {
            List<string> gone = _entries.Where(p => p.Value.ExpiresUtcTicks < nowUtc.Ticks).Select(p => p.Key).ToList();
            foreach (string key in gone) _entries.Remove(key);
            return gone.Count > 0;
        }

        public string Serialize()
        {
            var list = _entries.Select(p => (object)new Dictionary<string, object>
            {
                ["k"] = p.Key,
                ["x"] = p.Value.ExpiresUtcTicks.ToString(CultureInfo.InvariantCulture),
                ["a"] = p.Value.Answer,
            }).ToArray();
            return RemoteControl.Json().Serialize(new Dictionary<string, object> { ["v"] = 1, ["entries"] = list });
        }

        /// <summary>The ring a text holds; an empty ring for anything that does not read (never a throw).</summary>
        public static RemoteControlRing Parse(string? json)
        {
            var ring = new RemoteControlRing();
            if (string.IsNullOrEmpty(json)) return ring;
            try
            {
                if (!(RemoteControl.Json().DeserializeObject(json) is Dictionary<string, object> root)) return ring;
                if (!(root.TryGetValue("entries", out object? raw) && raw is object[] items)) return ring;
                foreach (object item in items)
                {
                    if (!(item is Dictionary<string, object> e)) continue;
                    if (!(e.TryGetValue("k", out object? k) && k is string key) || key.Length == 0) continue;
                    if (!(e.TryGetValue("x", out object? x) && x is string ticksText
                          && long.TryParse(ticksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))) continue;
                    string answer = e.TryGetValue("a", out object? a) && a is string text ? text : InterruptedAnswer;
                    ring._entries[key] = new Entry { ExpiresUtcTicks = ticks, Answer = answer };
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return ring;
        }
    }

    /// <summary>
    /// The ring on disk (rule 12: a restart does not reopen it): one DPAPI-protected file of the data folder, written
    /// whole through a temporary file. A file that does not open reads as an empty ring and is counted in the log.
    /// </summary>
    internal sealed class RemoteControlRingFile
    {
        public const string FileName = "remote-commands-ring.dat";
        private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("CyrFlip.RemoteControl.Ring.v1");
        private readonly string _path;

        public RemoteControlRingFile(string path) { _path = path; }

        public static RemoteControlRingFile Default() => new RemoteControlRingFile(Path.Combine(DataFolder.Current, FileName));

        public RemoteControlRing Load(out bool unreadable)
        {
            unreadable = false;
            try
            {
                if (!File.Exists(_path)) return new RemoteControlRing();
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
                return RemoteControlRing.Parse(Encoding.UTF8.GetString(clear));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
            {
                unreadable = true;
                return new RemoteControlRing();
            }
        }

        /// <summary>Writes the ring; false when the file could not be written (the caller refuses to run without it).</summary>
        public bool Save(RemoteControlRing ring)
        {
            string temp = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                byte[] sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(ring.Serialize()), Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(temp, sealedBytes);
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
            {
                RemoteCommandsLog.Fault("ring-save", ex);
                return false;
            }
        }
    }

    /// <summary>One controller this target accepts on one account entry, with the classes it may ask (rules 9 and 10).</summary>
    internal sealed class RemoteControlGrant
    {
        /// <summary>The local id of the account entry the controller is a device of.</summary>
        public string Entry { get; set; } = "";
        public string DeviceId { get; set; } = "";
        /// <summary>The controller's name as the directory last listed it - what a confirmation and the log show.</summary>
        public string Name { get; set; } = "";
        /// <summary>Risk class to consent: <c>always</c> or <c>ask</c>. A class that is absent is not granted.</summary>
        public Dictionary<string, string> Classes { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The consent for a class, or null when the class is not granted.</summary>
        public string? ConsentFor(string riskClass)
            => Classes.TryGetValue(riskClass, out string? c) ? c : null;

        /// <summary>A newly ticked controller: <c>safe</c> and nothing else, unconfirmed (rules 9 and 10).</summary>
        public static RemoteControlGrant Ticked(string entry, string deviceId, string name)
            => new RemoteControlGrant
            {
                Entry = entry, DeviceId = deviceId, Name = name,
                Classes = new Dictionary<string, string>(StringComparer.Ordinal) { ["safe"] = RemoteControl.ConsentAlways },
            };
    }

    /// <summary>The grants as one registry value (a JSON array); kept on this PC only and never exported.</summary>
    internal static class RemoteControlGrants
    {
        public const int MaxGrants = 256;

        public static List<RemoteControlGrant> Parse(string? json)
        {
            var list = new List<RemoteControlGrant>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                if (!(RemoteControl.Json().DeserializeObject(json) is object[] items)) return list;
                foreach (object item in items)
                {
                    if (!(item is Dictionary<string, object> g) || list.Count >= MaxGrants) continue;
                    string entry = g.TryGetValue("entry", out object? e) && e is string es ? es : "";
                    string device = g.TryGetValue("deviceId", out object? d) && d is string ds ? ds : "";
                    if (entry.Length == 0 || device.Length == 0 || Find(list, entry, device) != null) continue;
                    var grant = new RemoteControlGrant
                    {
                        Entry = entry, DeviceId = device,
                        Name = g.TryGetValue("name", out object? n) && n is string ns ? ns : "",
                    };
                    if (g.TryGetValue("classes", out object? c) && c is Dictionary<string, object> classes)
                        foreach (KeyValuePair<string, object> pair in classes)
                            if (RemoteControl.Classes.Contains(pair.Key) && pair.Value is string consent
                                && (consent == RemoteControl.ConsentAlways || consent == RemoteControl.ConsentAsk))
                                grant.Classes[pair.Key] = consent;
                    if (grant.Classes.Count > 0) list.Add(grant);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return list;
        }

        public static string Serialize(IEnumerable<RemoteControlGrant> grants)
            => RemoteControl.Json().Serialize(grants.Where(g => g.Classes.Count > 0).Take(MaxGrants).Select(g => (object)new Dictionary<string, object>
            {
                ["entry"] = g.Entry,
                ["deviceId"] = g.DeviceId,
                ["name"] = g.Name,
                ["classes"] = g.Classes.ToDictionary(p => p.Key, p => (object)p.Value),
            }).ToArray());

        public static RemoteControlGrant? Find(IEnumerable<RemoteControlGrant> grants, string entry, string deviceId)
            => grants.FirstOrDefault(g => string.Equals(g.Entry, entry, StringComparison.Ordinal)
                                          && string.Equals(g.DeviceId, deviceId, StringComparison.Ordinal));

        /// <summary>
        /// Rule 9: a controller that left the account's directory, or whose key changed, drops out with its grants.
        /// <paramref name="stillTrusted"/> holds the device ids the entry's directory lists with the key it trusts.
        /// Returns whether a grant went.
        /// </summary>
        public static bool DropGone(List<RemoteControlGrant> grants, string entry, ICollection<string> stillTrusted)
            => grants.RemoveAll(g => string.Equals(g.Entry, entry, StringComparison.Ordinal) && !stillTrusted.Contains(g.DeviceId)) > 0;
    }

    internal enum RemoteControlVerdictKind
    {
        /// <summary>Answer <see cref="RemoteControlVerdict.Refusal"/> and run nothing.</summary>
        Refuse,
        /// <summary>Ask the person on this PC first (rule 10); No or no answer in 60 s is <c>declined</c>.</summary>
        Ask,
        /// <summary>Run the verb now.</summary>
        Run,
    }

    internal sealed class RemoteControlVerdict
    {
        public RemoteControlVerdictKind Kind { get; private set; }
        public RemoteControlAnswer? Refusal { get; private set; }
        public AppCommandSpec? Spec { get; private set; }
        /// <summary>The arguments as command-line tokens, ready for the same parser a local caller meets.</summary>
        public string[] Tokens { get; private set; } = new string[0];

        public static RemoteControlVerdict Refuse(RemoteControlAnswer answer) => new RemoteControlVerdict { Kind = RemoteControlVerdictKind.Refuse, Refusal = answer };
        public static RemoteControlVerdict Go(RemoteControlVerdictKind kind, AppCommandSpec spec, string[] tokens)
            => new RemoteControlVerdict { Kind = kind, Spec = spec, Tokens = tokens };
    }

    /// <summary>The order a request is decided in, pure (rules 6 to 13).</summary>
    internal static class RemoteControlPolicy
    {
        /// <summary>
        /// The verdict on one opened request. <paramref name="parseReason"/> is the reason a payload did not read (then
        /// <paramref name="request"/> is null). The order: the payload, the switch (rule 8 - while off nothing else is
        /// looked at), the controller's grant, the time window, the verb (a verb that is not <c>remote</c> is
        /// <c>unknown-verb</c>, so a controller cannot list hidden verbs), its arguments, the class, then the consent.
        /// </summary>
        public static RemoteControlVerdict Decide(RemoteControlRequest? request, string parseReason, bool listening,
            RemoteControlGrant? grant, AppCommandManifest manifest, DateTime nowUtc)
        {
            if (request == null)
                return RemoteControlVerdict.Refuse(parseReason == AppCommandReasons.Schema
                    ? RemoteControlAnswer.Refused(AppCommandReasons.Schema, AppCommandExit.Usage, "The request is of a newer version than this program reads.")
                    : parseReason == AppCommandReasons.TooLarge
                        ? RemoteControlAnswer.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage, "The request is larger than 65536 bytes.")
                        : RemoteControlAnswer.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, "The request does not read."));
            if (!listening || grant == null || grant.Classes.Count == 0)
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(RemoteControl.NotPermitted, AppCommandExit.Permission,
                    "This program does not take commands from this device."));
            if (request.IsExpired(nowUtc))
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(RemoteControl.Expired, AppCommandExit.NotProven,
                    "The command arrived outside its time window."));

            AppCommandSpec? spec = manifest.Find(request.Verb);
            if (spec == null || spec.RemoteConfirm == null || !string.Equals(spec.Name, request.Verb, StringComparison.Ordinal))
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(AppCommandReasons.UnknownVerb, AppCommandExit.Usage,
                    "This program has no such command for other devices."));

            if (!TryTokens(spec, request.Args, out string[] tokens, out string problem))
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, problem));
            AppCommandLineResult parsed = AppCommandLine.ParseOptions(spec, tokens);
            if (parsed.Kind != AppCommandLineKind.Command)
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                    "An argument of the command does not fit it."));

            string? consent = grant.ConsentFor(spec.Risk);
            if (consent == null)
                return RemoteControlVerdict.Refuse(RemoteControlAnswer.Refused(RemoteControl.NotPermitted, AppCommandExit.Permission,
                    "This device may not ask for this kind of command."));
            bool ask = spec.RemoteConfirm == RemoteControl.ConfirmDevice || consent == RemoteControl.ConsentAsk;
            return RemoteControlVerdict.Go(ask ? RemoteControlVerdictKind.Ask : RemoteControlVerdictKind.Run, spec, tokens);
        }

        /// <summary>
        /// The <c>args</c> object as <c>--name=value</c> tokens. A member that names no option, names one that is never
        /// remote, or carries a JSON type its option does not take is refused; nothing is ignored (section 3).
        /// </summary>
        internal static bool TryTokens(AppCommandSpec spec, IReadOnlyDictionary<string, object> args, out string[] tokens, out string problem)
        {
            tokens = new string[0];
            problem = "";
            var list = new List<string>();
            foreach (KeyValuePair<string, object> pair in args)
            {
                AppCommandArg? arg = spec.Args.FirstOrDefault(a => string.Equals(a.Name, pair.Key, StringComparison.Ordinal));
                if (arg == null || !RemoteControl.IsRemoteOption(arg))
                {
                    problem = "The command takes no argument \"" + Clip(pair.Key) + "\" from another device.";
                    return false;
                }
                string? value = ValueText(arg, pair.Value);
                if (value == null)
                {
                    problem = "The argument \"" + arg.Name + "\" has a value of the wrong type.";
                    return false;
                }
                list.Add("--" + arg.Name + "=" + value);
            }
            tokens = list.ToArray();
            return true;
        }

        private static string? ValueText(AppCommandArg arg, object? value)
        {
            switch (arg.Kind)
            {
                case "bool": return value is bool b ? (b ? "true" : "false") : null;
                case "int":
                    if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
                    if (value is long l && l >= int.MinValue && l <= int.MaxValue) return l.ToString(CultureInfo.InvariantCulture);
                    return null;
                default:
                    return value is string s && s.IndexOf('\0') < 0 ? s : null;
            }
        }

        private static string Clip(string text) => text.Length <= 40 ? text : text.Substring(0, 40) + "..";
    }
}
