using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// The decisions of the commands that do not depend on where they run (ticket S0107): the gate of
    /// APP-COMMANDS rule 13 with the legacy exemption of item F, the module gate of item M, and the choice of a
    /// scenario. The running copy and a one-shot cold start call the same functions, so <c>scenario run</c> answers
    /// the same whether CyrFlip was running or not.
    /// </summary>
    internal static class AppCommandVerbs
    {
        /// <summary>The caption of the one setting of rule 13 - also what a refusal names.</summary>
        public const string SettingCaption = "Let other programs ask CyrFlip to work with my files, screen, clipboard or the network";

        /// <summary>
        /// The refusal of rule 13, or null when the verb may run: a <c>safe</c> verb always may; any other only while
        /// the setting is on, unless the caller typed a spelling shipped before the adoption (item F paragraph 2).
        /// </summary>
        public static AppCommandResponse? RiskGate(AppCommandSpec spec, bool settingOn, bool legacy, Func<string, string> T)
        {
            if (spec.IsSafe || settingOn) return null;
            if (legacy && spec.HasLegacyAlias) return null;
            return AppCommandResponse.Refused(AppCommandReasons.SwitchOff, AppCommandExit.Permission,
                Localization.Format(T, "This command is off. Turn on \"{0}\" on the General page of the CyrFlip settings.", T(SettingCaption)));
        }

        /// <summary>The refusal of a switched-off module (item M, <c>module-off</c>, exit 11).</summary>
        public static AppCommandResponse ModuleOff(string message)
            => AppCommandResponse.Refused(AppCommandReasons.ModuleOff, AppCommandExit.NotReady, message);

        /// <summary>
        /// The scenario <c>scenario run</c> names, or the refusal that says why it cannot run from a command: none has
        /// the id or the name (11), two share the name (11), a yt-dlp scenario without <c>--link</c> (10 - rule 4: an
        /// option would supply it), one that runs as administrator (12 <c>needs-person</c> - nothing a command can give
        /// confirms an elevation prompt).
        /// </summary>
        public static AppCommandResponse? PickScenario(IReadOnlyList<LauncherScenario> scenarios, AppCommandInvocation invocation,
            Func<string, string> T, out LauncherScenario? scenario)
        {
            scenario = null;
            string? id = invocation.Option("id");
            string? name = invocation.Option("name");
            List<LauncherScenario> matches = id != null
                ? scenarios.Where(s => s.Id.ToString("D").Equals(id, StringComparison.OrdinalIgnoreCase)).ToList()
                : scenarios.Where(s => string.Equals(s.Name.Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady, T("No scenario has this id or name."));
            if (matches.Count > 1)
                return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady, T("Two scenarios share this name. Use --id instead."));
            LauncherScenario found = matches[0];
            if (found.IsYtDlp && string.IsNullOrWhiteSpace(invocation.Option("link")))
                return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, T("This scenario downloads with yt-dlp. Give the link with --link."));
            if (found.RunAsAdmin && !found.IsYtDlp)
                return AppCommandResponse.Refused(AppCommandReasons.NeedsPerson, AppCommandExit.Permission, T("This scenario runs as administrator, which a command cannot confirm."));
            scenario = found;
            return null;
        }

        /// <summary>The answer to a finished launch.</summary>
        public static AppCommandResponse Launched(LauncherScenario scenario, LauncherLaunchResult result, Func<string, string> T)
        {
            if (result.Success)
                return AppCommandResponse.Done(Localization.Format(T, "Started \"{0}\".", scenario.Name),
                    new Dictionary<string, object> { ["name"] = scenario.Name });
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                Localization.Format(T, "Couldn't start \"{0}\": {1}", scenario.Name, result.ErrorMessage ?? ""));
        }

        /// <summary>The link a yt-dlp scenario gets from <c>--link</c>, in place of the prompt a person would see.</summary>
        public static Func<string?> LinkFrom(AppCommandInvocation invocation)
        {
            string? link = invocation.Option("link");
            return () => link;
        }

        /// <summary>The <c>show --page</c> word to a settings page; null when the option was not given.</summary>
        public static SettingsPageKey? PageOf(string? word)
        {
            switch ((word ?? "").ToLowerInvariant())
            {
                case "general": return SettingsPageKey.General;
                case "indicators": return SettingsPageKey.Indicators;
                case "hotkeys": return SettingsPageKey.Hotkeys;
                case "conversions": return SettingsPageKey.Conversions;
                case "windows-languages": return SettingsPageKey.WindowsLanguages;
                case "clipboard": return SettingsPageKey.Clipboard;
                case "quick-notes": return SettingsPageKey.QuickNotes;
                case "screenshots": return SettingsPageKey.Graphics;
                case "translate": return SettingsPageKey.Translate;
                case "launcher": return SettingsPageKey.Launcher;
                case "about": return SettingsPageKey.About;
                case "symbols": return SettingsPageKey.Symbols;
                case "selection-menu": return SettingsPageKey.SelectionMenu;
                case "traffic-light": return SettingsPageKey.TrafficLight;
                case "exchange-server": return SettingsPageKey.ExchangeServer;
                default: return null;
            }
        }

        /// <summary>The one-line token an older copy understands for this command, or null when the verb has none (item K).</summary>
        public static string? LegacyFormOf(AppCommandInvocation invocation)
        {
            switch (invocation.Spec.Name)
            {
                case "show": return LauncherIpc.SettingsCommand;
                case "quit": return LauncherIpc.ExitCommand;
                case "clipboard send": return LauncherIpc.ClipboardSendCommand;
                case "clipboard get": return LauncherIpc.ClipboardGetCommand;
                case "scenario run":
                    string? id = invocation.Option("id");
                    return id != null ? LauncherIpc.RunPrefix + id : null;
                default: return null;
            }
        }
    }
}
