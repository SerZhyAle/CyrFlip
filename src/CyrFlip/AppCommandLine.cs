using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CyrFlip
{
    /// <summary>What the command line of a start asks for.</summary>
    internal enum AppCommandLineKind
    {
        /// <summary>No arguments: a plain start (the empty legacy alias of <c>show</c> when a copy is running).</summary>
        Plain,
        /// <summary>One of the spellings shipped before APP-COMMANDS (<c>/exit</c>, <c>/launcher-run:{guid}</c>, ..).</summary>
        Legacy,
        /// <summary>A verb of the manifest with valid options.</summary>
        Command,
        /// <summary>Not a command this copy takes - exit 10, nothing executed.</summary>
        Refused,
    }

    /// <summary>A parsed verb: the spec, the options by name, the arguments after the verb as typed.</summary>
    internal sealed class AppCommandInvocation
    {
        public AppCommandInvocation(AppCommandSpec spec, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> options)
        {
            Spec = spec; Args = args; Options = options;
        }
        public AppCommandSpec Spec { get; }
        /// <summary>Everything after the verb's words, exactly as typed - what travels in the request (rule 7).</summary>
        public IReadOnlyList<string> Args { get; }
        /// <summary>Option name (as the manifest spells it) to its value; a bool option given bare is <c>true</c>.</summary>
        public IReadOnlyDictionary<string, string> Options { get; }
        public string? Option(string name) => Options.TryGetValue(name, out string? v) ? v : null;
        public bool Flag(string name)
            => Options.TryGetValue(name, out string? v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The outcome of reading a command line.</summary>
    internal sealed class AppCommandLineResult
    {
        private AppCommandLineResult(AppCommandLineKind kind, string? legacy, AppCommandInvocation? invocation,
            string reason, string message, string messageArgument)
        {
            Kind = kind; LegacyToken = legacy; Invocation = invocation; Reason = reason;
            Message = message; MessageArgument = messageArgument;
        }
        public AppCommandLineKind Kind { get; }
        /// <summary>The normalized legacy token for <see cref="AppCommandLineKind.Legacy"/>.</summary>
        public string? LegacyToken { get; }
        public AppCommandInvocation? Invocation { get; }
        /// <summary>The reason token of a refusal (<c>unknown-verb</c>, <c>bad-argument</c>, <c>too-large</c>).</summary>
        public string Reason { get; }
        /// <summary>An English source key of <see cref="Localization"/> with one <c>{0}</c> at most.</summary>
        public string Message { get; }
        public string MessageArgument { get; }

        internal static AppCommandLineResult Plain() => new AppCommandLineResult(AppCommandLineKind.Plain, null, null, "", "", "");
        internal static AppCommandLineResult Legacy(string token) => new AppCommandLineResult(AppCommandLineKind.Legacy, token, null, "", "", "");
        internal static AppCommandLineResult Command(AppCommandInvocation invocation)
            => new AppCommandLineResult(AppCommandLineKind.Command, null, invocation, "", "", "");
        internal static AppCommandLineResult Refuse(string reason, string message, string argument = "")
            => new AppCommandLineResult(AppCommandLineKind.Refused, null, null, reason, message, argument);
    }

    /// <summary>
    /// The command-line grammar of APP-COMMANDS rules 1 to 3 (ticket S0107), pure and shared: a cold start and the
    /// running copy read a command with this same parser, so they can never disagree about it. A verb is one or two
    /// words of the manifest, compared without regard to case; an option is <c>--kebab-case</c>, its value after
    /// <c>=</c> or as the next argument; <c>--</c> ends the options; values are never case-folded. Strict: an
    /// unknown verb, an unknown option, a missing or a wrong value, a request over 64 KiB or one argument over
    /// 32 KiB is refused, never truncated and never half-run.
    /// </summary>
    internal static class AppCommandLine
    {
        /// <summary>The bound of a whole request as UTF-8 (rule 3).</summary>
        public const int MaxRequestBytes = 64 * 1024;
        /// <summary>The bound of one argument as UTF-8 (rule 3).</summary>
        public const int MaxArgumentBytes = 32 * 1024;

        public static AppCommandLineResult Parse(string[] args) => Parse(args, AppCommandManifest.Shipped);

        internal static AppCommandLineResult Parse(string[] args, AppCommandManifest manifest)
        {
            if (args.Length == 0) return AppCommandLineResult.Plain();
            // A frozen spelling keeps its old reading, its arguments after it ignored as they always were (rule 2).
            string? legacy = LauncherIpc.ParseCommand(args);
            if (legacy != null) return AppCommandLineResult.Legacy(legacy);

            int total = 0;
            foreach (string arg in args)
            {
                int bytes = Encoding.UTF8.GetByteCount(arg ?? "");
                if (bytes > MaxArgumentBytes) return AppCommandLineResult.Refuse(AppCommandReasons.TooLarge, "The command is too long.");
                total += bytes + 1;
            }
            if (total > MaxRequestBytes) return AppCommandLineResult.Refuse(AppCommandReasons.TooLarge, "The command is too long.");

            AppCommandSpec? spec = null;
            int words = 0;
            if (args.Length >= 2) { spec = manifest.Find(args[0] + " " + args[1]); words = 2; }
            if (spec == null) { spec = manifest.Find(args[0]); words = 1; }
            if (spec == null)
                return AppCommandLineResult.Refuse(AppCommandReasons.UnknownVerb,
                    "Unknown command \"{0}\". Run \"CyrFlip.exe commands\" to see the list.", Clip(args[0]));

            string[] rest = args.Skip(words).ToArray();
            return ParseOptions(spec, rest);
        }

        /// <summary>The options of a known verb; also what the running copy reparses from a request's <c>args</c>.</summary>
        internal static AppCommandLineResult ParseOptions(AppCommandSpec spec, IReadOnlyList<string> rest)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool optionsEnded = false;
            for (int i = 0; i < rest.Count; i++)
            {
                string token = rest[i] ?? "";
                if (!optionsEnded && token == "--") { optionsEnded = true; continue; }
                if (optionsEnded || !token.StartsWith("--", StringComparison.Ordinal))
                    // No verb of this command set takes a value without an option name.
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "Unexpected argument \"{0}\".", Clip(token));

                string body = token.Substring(2);
                string? value = null;
                int equals = body.IndexOf('=');
                if (equals >= 0) { value = body.Substring(equals + 1); body = body.Substring(0, equals); }
                AppCommandArg? arg = spec.Arg(body);
                if (arg == null || !IsOptionName(body))
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "Unknown option \"{0}\".", Clip("--" + body));
                if (options.ContainsKey(arg.Name))
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "The option \"{0}\" is given twice.", "--" + arg.Name);

                if (arg.Kind == "bool")
                {
                    value ??= "true";
                    if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "The option \"{0}\" takes true or false.", "--" + arg.Name);
                    options[arg.Name] = value.ToLowerInvariant();
                    continue;
                }
                if (value == null)
                {
                    if (i + 1 >= rest.Count)
                        return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "The option \"{0}\" needs a value.", "--" + arg.Name);
                    value = rest[++i] ?? "";
                }
                if (!ValueFits(arg, value))
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "The option \"{0}\" does not take this value.", "--" + arg.Name);
                options[arg.Name] = arg.Kind == "guid" ? Guid.Parse(value).ToString("D") : value;
            }

            foreach (AppCommandArg arg in spec.Args)
                if (arg.Required && !options.ContainsKey(arg.Name))
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "The option \"{0}\" is required.", "--" + arg.Name);
            foreach (IReadOnlyList<string> group in spec.OneOf)
                if (group.Count(options.ContainsKey) != 1)
                    return AppCommandLineResult.Refuse(AppCommandReasons.BadArgument, "Give exactly one of {0}.",
                        string.Join(", ", group.Select(n => "--" + n)));

            return AppCommandLineResult.Command(new AppCommandInvocation(spec, rest.ToArray(), options));
        }

        private static bool IsOptionName(string name)
        {
            if (name.Length == 0 || !char.IsLetter(name[0])) return false;
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-') return false;
            return true;
        }

        private static bool ValueFits(AppCommandArg arg, string value)
        {
            switch (arg.Kind)
            {
                case "int": return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _);
                case "guid": return Guid.TryParse(value, out _);
                // An enum value is a fixed word of the manifest; it is the one value compared without regard to case.
                case "enum": return arg.Values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
                case "path": return value.Length > 0;
                default: return true;
            }
        }

        /// <summary>A refused token is echoed in the message; a long one is cut so the sentence stays one line.</summary>
        private static string Clip(string text) => text.Length <= 40 ? text : text.Substring(0, 40) + "..";
    }
}
