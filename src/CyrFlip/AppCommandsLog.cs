using System;

namespace CyrFlip
{
    /// <summary>
    /// <c>app-commands.log</c> (APP-COMMANDS rule 16, ticket S0107): one line per command CyrFlip took - the verb,
    /// where it came from, the caller's process id and the outcome. The arguments are never written: a scenario
    /// name, a link or a path is the caller's, and this file goes into the log bundle. In the bundle's whitelist.
    /// </summary>
    internal static class AppCommandsLog
    {
        public const string FileName = "app-commands.log";

        public static void Log(string verb, string origin, uint callerPid, AppCommandResponse outcome)
            => DiagnosticLog.Append(DiagnosticLog.Path(FileName), Line(DateTime.Now, verb, origin, callerPid, outcome));

        /// <summary>The line itself - testable without a file. Only manifest words and closed tokens go in.</summary>
        internal static string Line(DateTime at, string verb, string origin, uint callerPid, AppCommandResponse outcome)
            => at.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
               + " - " + verb + " origin=" + Token(origin) + " pid=" + callerPid
               + " status=" + outcome.Status + " exit=" + outcome.ExitCode
               + (string.IsNullOrEmpty(outcome.Reason) ? "" : " reason=" + Token(outcome.Reason!))
               + CountOf(outcome);

        /// <summary>
        /// The one number an answer may leave in the log: how many items it returned (<c>count</c> for a list, <c>notes</c> for the
        /// notes export - S0120), an integer. Never a name or a text.
        /// </summary>
        private static string CountOf(AppCommandResponse outcome)
        {
            if (outcome.Data == null) return "";
            if (outcome.Data.TryGetValue("count", out object? count) && count is int n && n >= 0) return " count=" + n;
            if (outcome.Data.TryGetValue("notes", out object? notes) && notes is int m && m >= 0) return " count=" + m;
            return StateOf(outcome);
        }

        /// <summary>
        /// The state tokens the switch verbs answer (ticket S0125): the fixed on/off members plus the theme's stored
        /// token - a closed set, written only as the value each member is (never a date, a path or the caller's words,
        /// so <c>expiresUtc</c> in particular never reaches this file). The paused token is S0121's member, logged on
        /// the same whitelist.
        /// </summary>
        private static string StateOf(AppCommandResponse outcome)
        {
            if (outcome.Data == null) return "";
            var text = new System.Text.StringBuilder();
            if (outcome.Data.TryGetValue("system", out object? system) && system is bool systemOn)
                text.Append(" system=").Append(On(systemOn));
            if (outcome.Data.TryGetValue("screen", out object? screen) && screen is bool screenOn)
                text.Append(" screen=").Append(On(screenOn));
            if (outcome.Data.TryGetValue("paused", out object? paused) && paused is bool pausedOn)
                text.Append(" paused=").Append(On(pausedOn));
            if (outcome.Data.TryGetValue("enabled", out object? enabled) && enabled is bool enabledOn)
                text.Append(" enabled=").Append(On(enabledOn));
            if (outcome.Data.TryGetValue("restored", out object? restored) && restored is bool restoredOn)
                text.Append(" restored=").Append(On(restoredOn));
            if (outcome.Data.TryGetValue("theme", out object? theme) && theme is string token
                && (token == ThemeModes.SystemToken || token == ThemeModes.LightToken || token == ThemeModes.DarkToken))
                text.Append(" theme=").Append(token);
            return text.Length == 0 ? "" : " " + text.ToString();
        }

        /// <summary>The value a member's token is written as - the parser's own lower-case bool words.</summary>
        private static string On(bool value) => value ? "true" : "false";

        /// <summary>The caller names its origin: anything but a short lower-case token is written as <c>other</c>.</summary>
        private static string Token(string text)
        {
            if (text.Length == 0 || text.Length > 24) return "other";
            foreach (char c in text)
                if (!(c >= 'a' && c <= 'z') && c != '-') return "other";
            return text;
        }
    }
}
