using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>One option of a command, as the manifest declares it (APP-COMMANDS rule 18).</summary>
    internal sealed class AppCommandArg
    {
        public AppCommandArg(string name, string kind, bool required, IReadOnlyList<string> values)
        {
            Name = name; Kind = kind; Required = required; Values = values;
        }
        public string Name { get; }
        /// <summary><c>string</c>, <c>path</c>, <c>int</c>, <c>bool</c>, <c>enum</c> or <c>guid</c>.</summary>
        public string Kind { get; }
        public bool Required { get; }
        /// <summary>The allowed values of an <c>enum</c>; empty for every other kind.</summary>
        public IReadOnlyList<string> Values { get; }
    }

    /// <summary>One command of the manifest - what the parser, the gate and the dispatcher read.</summary>
    internal sealed class AppCommandSpec
    {
        public AppCommandSpec(string name, string risk, string coldStart, string forward, string wait, int timeoutMs,
            IReadOnlyList<AppCommandArg> args, IReadOnlyList<IReadOnlyList<string>> oneOf, bool hasLegacyAlias, string summary,
            bool idempotent, bool interactive)
        {
            Name = name; Risk = risk; ColdStart = coldStart; Forward = forward; Wait = wait; TimeoutMs = timeoutMs;
            Args = args; OneOf = oneOf; HasLegacyAlias = hasLegacyAlias; Summary = summary;
            Idempotent = idempotent; Interactive = interactive;
        }
        /// <summary>The canonical verb, its words joined by one space.</summary>
        public string Name { get; }
        public string Risk { get; }
        public string ColdStart { get; }
        /// <summary><c>always</c> or <c>never</c> (APP-COMMANDS 0.2 item C).</summary>
        public string Forward { get; }
        public string Wait { get; }
        public int TimeoutMs { get; }
        public IReadOnlyList<AppCommandArg> Args { get; }
        public IReadOnlyList<IReadOnlyList<string>> OneOf { get; }
        /// <summary>A spelling of this verb shipped before the adoption (item F): it is gated by its module only.</summary>
        public bool HasLegacyAlias { get; }
        public string Summary { get; }
        /// <summary>Rule 18: repeating the verb with the same arguments answers the same thing and changes nothing further.</summary>
        public bool Idempotent { get; }
        /// <summary>Rule 18: the verb opens or needs a person-facing interaction while it runs.</summary>
        public bool Interactive { get; }
        public bool IsSafe => Risk == "safe";
        /// <summary>
        /// <c>remote.confirm</c> of REMOTE-CONTROL rule 7 - <c>grant</c> or <c>device</c> - when another device of the
        /// account may ask for this verb; null when it may not (ticket CYRFLP-0140).
        /// </summary>
        public string? RemoteConfirm { get; internal set; }
        public AppCommandArg? Arg(string name)
            => Args.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The command manifest (APP-COMMANDS rule 18, ticket S0107): <c>docs/cli-commands.json</c>, embedded in the exe
    /// byte for byte. It is the one source the parser, the cold-start decision, the risk gate and <c>commands --json</c>
    /// all read, so the list a tool sees and the commands the program takes cannot drift apart. Parsing it touches no
    /// configuration, no window and no worker - <c>commands</c> answers from it before the process decides its role.
    /// </summary>
    internal sealed class AppCommandManifest
    {
        internal const string ResourceName = "CyrFlip.cli-commands.json";
        private static AppCommandManifest? s_shipped;

        private AppCommandManifest(byte[] raw, string cliVersion, IReadOnlyList<AppCommandSpec> commands)
        {
            Raw = raw; CliVersion = cliVersion; Commands = commands;
        }

        /// <summary>The manifest exactly as shipped - what <c>commands --json</c> prints.</summary>
        public byte[] Raw { get; }
        public string CliVersion { get; }
        public IReadOnlyList<AppCommandSpec> Commands { get; }

        /// <summary>The verb of that name (case-insensitive), or null.</summary>
        public AppCommandSpec? Find(string name)
            => Commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The manifest embedded in this executable.</summary>
        public static AppCommandManifest Shipped
        {
            get
            {
                if (s_shipped != null) return s_shipped;
                using Stream? stream = typeof(AppCommandManifest).Assembly.GetManifestResourceStream(ResourceName);
                if (stream == null) throw new InvalidOperationException("The command manifest is not embedded.");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return s_shipped = Parse(buffer.ToArray());
            }
        }

        /// <summary>Reads a manifest; throws <see cref="FormatException"/> on one a reader cannot use.</summary>
        internal static AppCommandManifest Parse(byte[] raw)
        {
            string text = Encoding.UTF8.GetString(raw);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            var root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>
                ?? throw new FormatException("The manifest is not a JSON object.");
            string cliVersion = Str(root, "cliVersion");
            var commands = new List<AppCommandSpec>();
            foreach (object item in Arr(root, "commands"))
            {
                if (!(item is Dictionary<string, object> c)) throw new FormatException("A command entry is not an object.");
                string name = Str(c, "name");
                string coldStart = Str(c, "coldStart");
                if (name.Length == 0 || coldStart.Length == 0) throw new FormatException("A command has no name or coldStart.");
                var args = new List<AppCommandArg>();
                foreach (object a in Arr(c, "args"))
                {
                    if (!(a is Dictionary<string, object> arg)) continue;
                    var values = Arr(arg, "values").Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
                    args.Add(new AppCommandArg(Str(arg, "name"), Str(arg, "kind"), Bool(arg, "required"), values));
                }
                var oneOf = new List<IReadOnlyList<string>>();
                foreach (object g in Arr(c, "oneOf"))
                    if (g is object[] group) oneOf.Add(group.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList());
                bool legacy = Arr(c, "aliases").OfType<Dictionary<string, object>>().Any(alias => Bool(alias, "legacy"));
                string forward = Str(c, "forward");
                int timeout = c.TryGetValue("timeoutMs", out object? t) && t is int ms ? ms : 10000;
                var spec = new AppCommandSpec(name, Str(c, "risk"), coldStart, forward.Length == 0 ? "always" : forward,
                    Str(c, "wait").Length == 0 ? "done" : Str(c, "wait"), Math.Max(0, Math.Min(timeout, 600000)),
                    args, oneOf, legacy, Str(c, "summary"), Bool(c, "idempotent"), Bool(c, "interactive"));
                // REMOTE-CONTROL rule 7: a confirm this reader does not know makes the verb not remote rather than guessed.
                if (c.TryGetValue("remote", out object? remote) && remote is Dictionary<string, object> r)
                {
                    string confirm = Str(r, "confirm");
                    if (confirm == RemoteControl.ConfirmGrant || confirm == RemoteControl.ConfirmDevice) spec.RemoteConfirm = confirm;
                }
                commands.Add(spec);
            }
            return new AppCommandManifest(raw, cliVersion, commands);
        }

        private static string Str(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object? v) && v is string s ? s : "";

        private static bool Bool(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object? v) && v is bool b && b;

        private static object[] Arr(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out object? v) && v is object[] a ? a : new object[0];

        /// <summary>
        /// What <c>commands</c> answers to another device (REMOTE-CONTROL rule 7): the manifest with only the entries that
        /// carry <c>remote</c>, and in each only the options another device may send. Built from the shipped bytes, so the
        /// members a controller reads are the manifest's own.
        /// </summary>
        public Dictionary<string, object> RemoteView()
        {
            string text = Encoding.UTF8.GetString(Raw);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text);
            var kept = new List<object>();
            foreach (object item in Arr(root, "commands"))
            {
                if (!(item is Dictionary<string, object> c) || !(c.TryGetValue("remote", out object? r) && r is Dictionary<string, object>)) continue;
                AppCommandSpec? spec = Find(Str(c, "name"));
                if (spec == null || spec.RemoteConfirm == null) continue;
                c["args"] = Arr(c, "args").OfType<Dictionary<string, object>>()
                    .Where(a => spec.Arg(Str(a, "name")) is AppCommandArg arg && RemoteControl.IsRemoteOption(arg))
                    .Cast<object>().ToArray();
                c.Remove("examples");
                c.Remove("synopsis");
                kept.Add(c);
            }
            return new Dictionary<string, object>
            {
                ["product"] = Str(root, "product"),
                ["cliVersion"] = CliVersion,
                ["commands"] = kept.ToArray(),
            };
        }

        /// <summary>The readable list <c>commands</c> prints without <c>--json</c>: one verb and its summary a line.</summary>
        public string ReadableList()
        {
            var text = new StringBuilder();
            text.Append("CyrFlip commands (").Append(CliVersion).Append("):").Append('\n');
            int width = Commands.Max(c => c.Name.Length);
            foreach (AppCommandSpec c in Commands)
                text.Append("  ").Append(c.Name.PadRight(width)).Append("  ").Append(c.Summary).Append('\n');
            text.Append("Run \"CyrFlip.exe commands --json\" for the whole manifest.").Append('\n');
            return text.ToString();
        }
    }
}
