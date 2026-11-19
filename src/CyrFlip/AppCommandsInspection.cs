using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// The four read-only verbs of ticket S0111 (APP-COMMANDS 0.2): <c>scenario list</c>, <c>chords list</c>,
    /// <c>diagnostics paths</c> and <c>diagnostics bundle</c>. Each takes what it reads as an argument - the configuration,
    /// the scenarios, Windows' chord records, the working directory, the log folder - so a cold start and the running copy
    /// answer from the same code, and the tests need no window. The gate of rule 13 is the caller's: it is applied before
    /// the verb runs, as for every other verb of its risk class.
    /// </summary>
    internal static class AppCommandsInspection
    {
        /// <summary>
        /// The answer of a cold start (<c>coldStart: oneshot</c>): reads the configuration, applies the gate of rule 13, then
        /// answers. <paramref name="workingDir"/> is the caller's directory, which a relative <c>--out</c> is resolved against.
        /// </summary>
        public static AppCommandResponse OneShot(AppCommandInvocation invocation, string workingDir, DateTime stamp)
        {
            AppConfig config = AppConfig.Load();
            Func<string, string> T = en => Localization.Translate(config.UiLanguage, en);
            AppCommandResponse? refusal = AppCommandVerbs.RiskGate(invocation.Spec, config.EnableExternalCommands, legacy: false, T);
            return refusal ?? Run(invocation, config, workingDir, stamp, T, SupportBundle.LogDirectory);
        }

        /// <summary>
        /// The verb's answer once the gate of rule 13 has passed. <paramref name="logDir"/> is the folder the bundle reads.
        /// Every failure the verb meets is answered here, so no exception reaches the caller.
        /// </summary>
        public static AppCommandResponse Run(AppCommandInvocation invocation, AppConfig config, string workingDir,
            DateTime stamp, Func<string, string> T, string logDir)
        {
            try
            {
                switch (invocation.Spec.Name)
                {
                    case "scenario list":
                        // A switched-off launcher reads no scenario file at all.
                        if (!config.EnableScenarioLauncher)
                            return AppCommandVerbs.ModuleOff(T("The scenario launcher is switched off. Turn it on in the settings of CyrFlip."));
                        return ScenarioList(ReadScenarios(), T);
                    case "chords list":
                        return ChordsList(config, ReadScenarios(), LanguageHotkeys.ReadAll(), LanguageHotkeys.ToggleCode(), T);
                    case "diagnostics paths":
                        return DiagnosticsPaths(T);
                    case "diagnostics bundle":
                        return DiagnosticsBundle(invocation.Option("out"), invocation.Flag("overwrite"), config, workingDir, stamp, logDir, T);
                    default:
                        return AppCommandResponse.Refused(AppCommandReasons.UnknownVerb, AppCommandExit.Usage,
                            Localization.Format(T, "Unknown command \"{0}\". Run \"CyrFlip.exe commands\" to see the list.", invocation.Spec.Name));
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Command: " + invocation.Spec.Name + " failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                    T("The command failed. The log bundle on the About page has the details."));
            }
        }

        // ------------------------------------------------------------------ scenario list

        /// <summary>The scenario folder, read and never written: a file without an id keeps the id it has on disk.</summary>
        private static List<LauncherScenario> ReadScenarios() => new LauncherScenarioStore(readOnly: true).All;

        /// <summary>The answer of <c>scenario list</c>: one entry per scenario, in the launcher's own order.</summary>
        public static AppCommandResponse ScenarioList(IEnumerable<LauncherScenario> scenarios, Func<string, string> T)
        {
            object[] items = scenarios.Select(ScenarioEntry).Cast<object>().ToArray();
            var data = new Dictionary<string, object> { ["count"] = items.Length, ["scenarios"] = items };
            return AppCommandResponse.Done(Localization.Format(T, "Scenarios listed: {0}.", items.Length), data);
        }

        /// <summary>
        /// One scenario as the answer shows it. The program path and the arguments are never in it: the arguments are where
        /// a token sits (APP-COMMANDS 0.2).
        /// </summary>
        internal static Dictionary<string, object> ScenarioEntry(LauncherScenario scenario) => new Dictionary<string, object>
        {
            ["id"] = scenario.Id.ToString("D"),
            ["name"] = scenario.Name ?? "",
            ["kind"] = ScenarioKind(scenario),
            ["runAsAdmin"] = scenario.RunAsAdmin,
            ["hasHotkey"] = !string.IsNullOrEmpty(scenario.Hotkey),
        };

        /// <summary>The kind word of the manifest: <c>yt-dlp</c>, <c>script</c> (a .ps1, .bat or .cmd file) or <c>program</c>.</summary>
        internal static string ScenarioKind(LauncherScenario scenario)
        {
            if (scenario.IsYtDlp) return "yt-dlp";
            return LauncherScriptInterpreter.IsScript(scenario.Path ?? "") ? "script" : "program";
        }

        // ------------------------------------------------------------------ chords list

        /// <summary>
        /// The answer of <c>chords list</c>: every row of the hotkeys overview, built by the overview's own model, so the reason
        /// a chord does not fire is the reason the page and the hook give (<see cref="ChordBinding.Evaluate"/>).
        /// </summary>
        public static AppCommandResponse ChordsList(AppConfig config, IEnumerable<LauncherScenario> scenarios,
            IEnumerable<LanguageHotkeys.Entry> windowsHotkeys, string? windowsToggleCode, Func<string, string> T)
        {
            List<ChordOverviewRow> rows = ChordOverviewModel.Build(config, scenarios, windowsHotkeys, windowsToggleCode,
                config.UiLanguage, LayoutNameOf);
            object[] items = rows.Select(ChordEntry).Cast<object>().ToArray();
            var data = new Dictionary<string, object> { ["count"] = items.Length, ["chords"] = items };
            return AppCommandResponse.Done(Localization.Format(T, "Chords listed: {0}.", items.Length), data);
        }

        /// <summary>
        /// One row of the overview as the answer shows it. <c>live</c> is the predicate the hook uses (active or shared), so a
        /// chord that is shared reads live with the reason <c>clash</c> beside it.
        /// </summary>
        internal static Dictionary<string, object> ChordEntry(ChordOverviewRow row)
        {
            bool live = row.State == ChordBindingState.Active || row.State == ChordBindingState.Clash;
            return new Dictionary<string, object>
            {
                ["id"] = row.Key,
                ["kind"] = row.Kind.HasValue ? row.Kind.Value.ToString() : row.Group.ToString(),
                ["chord"] = row.StoredChord,
                ["enabled"] = !row.HasSwitch || row.SwitchOn,
                ["live"] = live,
                // Null on purpose while the chord fires: the member is present and null (the closed token is only for a chord that does not).
                ["reason"] = ChordReason(row.State, row.ClashWith != null, live)!,
            };
        }

        /// <summary>
        /// Why a chord does not fire, as the manifest's closed token; null when it fires. A shared chord fires, so its reason is
        /// <c>clash</c> and no other.
        /// </summary>
        internal static string? ChordReason(ChordBindingState state, bool sharedWithAnother, bool live)
        {
            if (live) return sharedWithAnother || state == ChordBindingState.Clash ? "clash" : null;
            switch (state)
            {
                case ChordBindingState.OffByRow: return "own-switch";
                case ChordBindingState.OffByModule: return "module-off";
                case ChordBindingState.OffByMaster: return "master-off";
                case ChordBindingState.NoChord: return "no-chord";
                case ChordBindingState.Unparsable: return "unparsable";
                case ChordBindingState.Incomplete: return "incomplete";
                default: return null;
            }
        }

        /// <summary>A layout's name for a caption the verb does not print: its curated code, else the identifier itself.</summary>
        private static string LayoutNameOf(string klid)
        {
            string code = WorldLayouts.CodeForKlid(klid);
            return code.Length > 0 ? code : klid;
        }

        // ------------------------------------------------------------------ diagnostics paths

        /// <summary>
        /// The answer of <c>diagnostics paths</c>: the three folders, each from the code that owns it. They are named, never
        /// created - the scenario folder appears on its first write and the reports folder on the first bundle.
        /// </summary>
        public static AppCommandResponse DiagnosticsPaths(Func<string, string> T)
        {
            var data = new Dictionary<string, object>
            {
                ["dataFolder"] = DataFolder.Current,
                ["scenariosFolder"] = LauncherScenarioStore.DefaultFolder,
                ["reportsFolder"] = SupportBundle.ReportsDirectory,
            };
            return AppCommandResponse.Done(T("Done."), data);
        }

        // ------------------------------------------------------------------ diagnostics bundle

        /// <summary>
        /// <c>diagnostics bundle --out</c> (APP-COMMANDS 0.2): the About page's archive, written and not mailed. It is built by
        /// <see cref="SupportBundle.Create"/> - the same whitelist, redaction and budgets - under a temporary name beside the
        /// target and moved into place, so a failed write leaves nothing under the name the caller gave, and an overwrite never
        /// destroys the old archive before the new one is complete. The caller's folder is not pruned: the five-archive rule
        /// belongs to the reports folder alone.
        /// </summary>
        public static AppCommandResponse DiagnosticsBundle(string? raw, bool overwrite, AppConfig config, string workingDir,
            DateTime stamp, string logDir, Func<string, string> T)
        {
            string version = SupportBundle.AppVersion();
            AppCommandResponse? refusal = PlanBundle(raw ?? "", overwrite, workingDir, version, stamp, T,
                out string folder, out string target);
            if (refusal != null) return refusal;

            string tempName = "CyrFlip-logs-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".part";
            string tempPath = Path.Combine(folder, tempName);
            try
            {
                // The background writer is drained first, so a line queued a moment ago is in the archive too.
                DiagnosticLog.Flush(TimeSpan.FromSeconds(2));
                SupportBundle.Result result = SupportBundle.Create(logDir, folder, SupportBundle.BuildReport(config, version, stamp),
                    version, stamp, keepArchives: int.MaxValue, redactor: DiagnosticRedactor.ForThisMachine(),
                    environmentText: SupportBundle.BuildEnvironment(config, version, stamp), archiveName: tempName);
                if (File.Exists(target)) File.Replace(tempPath, target, null);
                else File.Move(tempPath, target);
                var data = new Dictionary<string, object>
                {
                    ["path"] = target,
                    ["sizeBytes"] = result.ArchiveBytes,
                    ["files"] = result.Entries.Count + result.Dropped.Count,
                };
                return AppCommandResponse.Done(Localization.Format(T, "Wrote the log bundle to {0}.", target), data);
            }
            catch (Exception ex)
            {
                DeleteQuietly(tempPath);
                if (IsDiskFull(ex))
                    return AppCommandResponse.Failed(AppCommandReasons.NoSpace, AppCommandExit.NoSpace,
                        T("The disk is full. The log bundle was not saved."));
                LauncherLog.Log("Command: diagnostics bundle not written: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.IoError, AppCommandExit.Failed,
                    T("The log bundle could not be written. Nothing was saved."));
            }
        }

        /// <summary>
        /// Where the bundle goes, or the refusal that says why it cannot go there: a missing folder, or a file that exists without
        /// <c>--overwrite</c> (11), or a name Windows does not accept (10). A folder - an existing one, or any path ending in a
        /// separator - gets the About page's name, made from the moment of the command. Nothing is created here.
        /// </summary>
        internal static AppCommandResponse? PlanBundle(string raw, bool overwrite, string workingDir, string version, DateTime stamp,
            Func<string, string> T, out string folder, out string target)
        {
            folder = "";
            target = "";
            if (raw.Trim().Length == 0)
                return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                    T("--out must be a file name, an existing folder, or a path ending in a backslash."));

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(workingDir, raw));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return InvalidPath(T);
            }

            bool asFolder = raw.EndsWith("\\", StringComparison.Ordinal) || raw.EndsWith("/", StringComparison.Ordinal)
                || Directory.Exists(full);
            if (asFolder)
            {
                folder = full;
                if (!Directory.Exists(folder)) return TargetMissing(T);
                target = Path.Combine(folder, SupportBundle.ArchiveName(version, stamp));
            }
            else
            {
                folder = Path.GetDirectoryName(full) ?? "";
                if (folder.Length == 0 || !Directory.Exists(folder)) return TargetMissing(T);
                string name = Path.GetFileName(full);
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
                    return InvalidPath(T);
                target = full;
            }

            if (File.Exists(target) && !overwrite)
                return AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady,
                    T("A file with this name already exists. Choose another name, or add --overwrite to replace it."));
            return null;
        }

        private static AppCommandResponse TargetMissing(Func<string, string> T)
            => AppCommandResponse.Refused(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                T("The folder in --out does not exist. CyrFlip does not create it."));

        private static AppCommandResponse InvalidPath(Func<string, string> T)
            => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                T("--out is not a path Windows accepts here: no device names, no control characters, none of < > \" | * ?, no name ending in a space or a dot (or with a space before its extension), a colon only after the drive letter."));

        /// <summary>
        /// ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL as the I/O layer reports them - the same test <see cref="FailureCause"/> makes.
        /// </summary>
        internal static bool IsDiskFull(Exception ex)
            => ex is IOException && (ex.HResult == unchecked((int)0x80070070) || ex.HResult == unchecked((int)0x80070027));

        private static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception cleanup) { LauncherLog.Log("Command: temporary bundle left: " + cleanup.GetType().Name); }
        }
    }
}
