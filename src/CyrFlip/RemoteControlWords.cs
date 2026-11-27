using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// The fixed words of the External control window that are values rather than captions (REMOTE-CONTROL rule 18,
    /// `WINDOW.md` sections 4 and 6, ticket CYRFLP-0142): the caption of a risk class, the word of a log line's outcome,
    /// the short summary of each remote verb in CyrFlip's own words, a program's name for a person (`DEVICE-EXCHANGE`
    /// item CX) and the T5 line made from a line of <c>remote-commands.log</c>. Pure: every value is an English key that
    /// the caller translates, so the whole table is testable without a window.
    /// </summary>
    internal static class RemoteControlWords
    {
        /// <summary>The caption key of a risk class (`class-*`).</summary>
        public static string ClassCaption(string riskClass)
        {
            switch (riskClass)
            {
                case "safe": return "Safe commands";
                case "reads-user-data": return "Commands that read your data";
                case "writes-user-data": return "Commands that change your data";
                case "leaves-machine": return "Commands that send your data off this device";
                default: return riskClass;
            }
        }

        /// <summary>The outcome words of a log line (`WINDOW.md` section 6), keyed by the answer's reason or status.</summary>
        private static readonly Dictionary<string, string> Outcomes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["done"] = "done",
            ["accepted"] = "accepted",
            ["busy"] = "busy",
            ["failed"] = "failed",
            ["not-permitted"] = "not allowed",
            ["declined"] = "declined",
            ["expired"] = "too late",
            ["unknown-verb"] = "no such command",
            ["bad-argument"] = "wrong arguments",
            ["sealed-failed"] = "could not be opened",
        };

        /// <summary>The English outcome word for a logged <c>status</c> or <c>status:reason</c>, and whether it is a key of the table.</summary>
        public static string OutcomeKey(string logged, out bool known)
        {
            string status = logged, reason = "";
            int colon = logged.IndexOf(':');
            if (colon >= 0) { status = logged.Substring(0, colon); reason = logged.Substring(colon + 1); }
            if (reason.Length > 0 && Outcomes.TryGetValue(reason, out string? byReason)) { known = true; return byReason; }
            if (Outcomes.TryGetValue(status, out string? byStatus)) { known = true; return byStatus; }
            // Any other reason is shown with the word of its answer's status (section 6, last line).
            known = false;
            return status;
        }

        /// <summary>The short summary of a remote verb, in CyrFlip's words (an English key); the verb itself when it has none.</summary>
        public static string VerbSummary(string verb)
        {
            switch (verb)
            {
                case "commands": return "List the commands it accepts";
                case "version": return "Tell the version";
                case "status": return "Tell whether it is ready";
                case "show": return "Open the settings";
                case "quit": return "Close CyrFlip";
                case "layout list": return "List the keyboard layouts";
                case "layout current": return "Tell the current keyboard layout";
                case "hotkeys pause": return "Pause or resume the key combinations";
                case "keepawake set": return "Keep this PC awake";
                default: return verb;
            }
        }

        /// <summary>Whether <see cref="VerbSummary"/> has a summary of its own for <paramref name="verb"/> (then it is translated).</summary>
        public static bool HasSummary(string verb) => VerbSummary(verb) != verb;

        /// <summary>A program's name for a person (`DEVICE-EXCHANGE` item CX): never translated; an unknown value shown raw.</summary>
        public static string ProgramName(string product)
        {
            switch (product)
            {
                case "fms-android": return "FastMediaSorter";
                case "fms-wear": return "FastMediaSorter";
                case "fms-windows": return "Fast Media Sorter & Sharing for Windows";
                case "fms-web": return "FastMediaSorter for Web";
                case "streamsplayer-windows":
                case "streamsplayer-android":
                case "streamsplayer-wear": return "StreamsPlayer";
                case "cyrflip-windows": return "CyrFlip";
                default:
                    string raw = RemoteCommandsLog.Clean(product, 40);
                    return raw == "?" ? "" : raw;
            }
        }

        /// <summary>
        /// A controller's name as the window and the prompt show it: one line, no control or format character - the
        /// bidi overrides and marks included, which could reorder the words around it - at most 40 characters.
        /// </summary>
        public static string CleanName(string? name) => RemoteCommandsLog.Clean(name, 40);

        /// <summary>
        /// The T5 line of one line of the log (`WINDOW.md` section 4 T5): <c>{time} {deviceName} - {verb summary}: {outcome}</c>,
        /// the outcome in the words of section 6. A box that did not open shows its clear sender marked unverified. A line
        /// of another shape is shown as it is.
        /// </summary>
        public static string Display(string line, Func<string, string> translate)
        {
            string[] parts = line.Split(new[] { " | " }, StringSplitOptions.None);
            if (parts.Length < 2) return line;
            string time = parts[0];
            if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at))
                time = at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (parts[1].StartsWith("sealed-failed", StringComparison.Ordinal))
            {
                string from = parts.Length > 2 ? parts[2] : "";
                if (from.StartsWith("from ", StringComparison.Ordinal)) from = from.Substring(5);
                int mark = from.IndexOf(" (unverified)", StringComparison.Ordinal);
                if (mark >= 0) from = from.Substring(0, mark);
                return time + " " + from + " (" + translate("unverified") + ") - ?: " + translate("could not be opened");
            }
            if (parts.Length < 5) return line;
            string device = parts[1];
            int idOpen = device.LastIndexOf(" (", StringComparison.Ordinal);
            if (idOpen > 0 && device.EndsWith(")", StringComparison.Ordinal)) device = device.Substring(0, idOpen);
            string verb = parts[2];
            string summary = HasSummary(verb) ? translate(VerbSummary(verb)) : verb;
            string outcome = OutcomeKey(parts[4], out bool known);
            return time + " " + device + " - " + summary + ": " + (known ? translate(outcome) : outcome);
        }

        /// <summary>The T5 lines, newest first.</summary>
        public static List<string> Recent(IReadOnlyList<string> oldestFirst, Func<string, string> translate)
            => oldestFirst.Reverse().Select(l => Display(l, translate)).ToList();

        /// <summary>The verbs of a manifest another device may ask, in the manifest's order (T4).</summary>
        public static List<AppCommandSpec> RemoteVerbs(AppCommandManifest manifest)
            => manifest.Commands.Where(c => c.RemoteConfirm != null).ToList();

        /// <summary>The risk classes in which <paramref name="manifest"/> has a remote verb, in the fixed order of section 4 T3.</summary>
        public static List<string> ShownClasses(AppCommandManifest manifest)
        {
            var risks = new HashSet<string>(RemoteVerbs(manifest).Select(c => c.Risk), StringComparer.Ordinal);
            return RemoteControl.Classes.Where(risks.Contains).ToList();
        }
    }
}
