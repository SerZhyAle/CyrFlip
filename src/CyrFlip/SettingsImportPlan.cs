using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    internal sealed class SettingsImportChange
    {
        internal SettingsImportChange(SettingsFileEntry entry, object before, object after, bool optIn)
        { Entry = entry; Before = before; After = after; OptIn = optIn; }
        internal SettingsFileEntry Entry { get; }
        internal object Before { get; }
        internal object After { get; }
        internal bool OptIn { get; }
    }

    internal sealed class SettingsImportPlan
    {
        internal AppConfig Candidate = null!;
        internal MemoryConfigKey Baseline = null!;
        internal readonly List<SettingsImportChange> Changes = new List<SettingsImportChange>();
        internal readonly List<SettingsFileIssue> Issues = new List<SettingsFileIssue>();
        internal int Unchanged;
        /// <summary>
        /// The scenario set to apply at commit (S0072 5.3): null when the file carries no
        /// <c>scenarios</c> section (a v1 file), the group is unselected, or the imported set already
        /// matches the local store - in every one of those the store is left alone.
        /// </summary>
        internal List<LauncherScenario>? Scenarios;
        internal static bool RemoteEndpoint(string text)
            => text.Length > 0 && (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback);

        internal static SettingsImportPlan Build(AppConfig local, SettingsFileDocument document,
            IEnumerable<LauncherScenario>? scenarios = null, IEnumerable<LanguageHotkeys.Entry>? windows = null,
            Func<Hotkey, HotkeyRules.Verdict>? rules = null, ISet<string>? installed = null, ISet<string>? selected = null)
        {
            var plan = new SettingsImportPlan(); plan.Issues.AddRange(document.Issues);
            var original = MemoryConfigKey.Capture(local); var key = MemoryConfigKey.Capture(local);
            plan.Baseline = original;
            var imported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in document.Values)
            {
                if (selected != null && !selected.Contains(pair.Key)) continue;
                string? refusal = null;
                if (pair.Key == "UiLanguage" && !Localization.Names.Contains((string)pair.Value)) refusal = "language";
                else if (Kind(pair.Key).HasValue && ((string)pair.Value).Length > 0 && !Hotkey.TryParse((string)pair.Value, out _)) refusal = "chord";
                else if (pair.Key == "ContextMenuChord" && !MouseChord.TryParse((string)pair.Value, out _)) refusal = "chord";
                else if (pair.Key == "TranslateEndpoint" && ((string)pair.Value).Length > 0)
                {
                    if (!Uri.TryCreate((string)pair.Value, UriKind.Absolute, out Uri? uri)
                        || (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0
                        || uri.Query.Length > 0 || uri.Fragment.Length > 0) refusal = "endpoint";
                }
                if (refusal != null) { plan.Issues.Add(new SettingsFileIssue(pair.Key, refusal)); continue; }
                // A folder this PC has no such directory under still imports (it is a user choice, and a
                // relative or %USERPROFILE%-rooted one may yet resolve here); the preview says so, the
                // way a missing keyboard layout is flagged. A remote path is never probed (S0057 D4).
                if (pair.Key == "ScreenshotFolder" && ((string)pair.Value).Length > 0 && LocalMissing((string)pair.Value, directory: true))
                    plan.Issues.Add(new SettingsFileIssue("ScreenshotFolder", "path"));
                key.Values[pair.Key] = pair.Value; imported.Add(pair.Key);
            }
            AppConfig candidate = key.Load();
            // The real loader owns table shape, sanitization and numeric bounds.
            foreach (string name in candidate.UnreadableValues.Where(imported.Contains).ToArray())
            {
                key.Values[name] = original.Values[name]; imported.Remove(name);
                plan.Issues.Add(new SettingsFileIssue(name, "kind"));
            }
            candidate = key.Load();
            var normalized = MemoryConfigKey.Capture(candidate);
            foreach (string name in imported)
                if (typeof(AppConfig).GetProperty(name)!.PropertyType == typeof(int) && !Equals(key.Values[name], normalized.Values[name]))
                    plan.Issues.Add(new SettingsFileIssue(name, "clamped"));
            if (imported.Contains("TranslateProfiles")) candidate.TranslateSeeded = true;

            // The scenarios section (S0072 5.3): each document judged by the store's own reader, an
            // unrecognized one reported and skipped whole - its Path is never run (SCENARIO-FILE rule 10).
            var launcherLocal = (scenarios ?? Array.Empty<LauncherScenario>()).ToArray();
            List<LauncherScenario>? importedScenarios = null;
            if (document.Scenarios != null && (selected == null || selected.Contains("scenarios")))
            {
                importedScenarios = new List<LauncherScenario>();
                foreach (string xml in document.Scenarios)
                {
                    LauncherScenario? scenario = LauncherScenarioStore.TryReadXml(xml, out _);
                    if (scenario == null) plan.Issues.Add(new SettingsFileIssue("scenarios", "scenario"));
                    else importedScenarios.Add(scenario);
                }
            }
            IEnumerable<LauncherScenario> registryScenarios = (IEnumerable<LauncherScenario>?)importedScenarios ?? launcherLocal;
            var retainedIds = new HashSet<string>(StringComparer.Ordinal);
            if (!imported.Contains("LayoutConversionProfiles")) foreach (var row in candidate.LayoutConversionProfiles) retainedIds.Add(row.Id);
            if (!imported.Contains("TranslateProfiles")) foreach (var row in candidate.TranslateProfiles) retainedIds.Add(row.Id);
            // Local scenario ids stay reserved only while the local set is kept; an import replaces it.
            if (importedScenarios == null)
                foreach (var scenario in launcherLocal) retainedIds.Add(scenario.Id.ToString("N"));
            string UniqueId(string id)
            {
                if (retainedIds.Add(id)) return id;
                string next; do { next = Guid.NewGuid().ToString("N"); } while (!retainedIds.Add(next)); return next;
            }
            if (importedScenarios != null)
                foreach (var scenario in importedScenarios)
                {
                    if (scenario.Id == Guid.Empty) scenario.Id = Guid.NewGuid();
                    scenario.Id = Guid.Parse(UniqueId(scenario.Id.ToString("N")));
                }
            if (imported.Contains("LayoutConversionProfiles")) foreach (var row in candidate.LayoutConversionProfiles) row.Id = UniqueId(row.Id);
            if (imported.Contains("TranslateProfiles")) foreach (var row in candidate.TranslateProfiles) row.Id = UniqueId(row.Id);
            var win = (windows ?? Array.Empty<LanguageHotkeys.Entry>()).ToArray();
            var windowsConsent = new HashSet<string>();
            rules ??= h => HotkeyRules.Check(h, Array.Empty<IntPtr>(), (_, __) => null);
            // Repeat after restoring refused fixed chords. A restoration can expose a conflict that
            // was absent in the first candidate (including a partial group selection).
            bool changed;
            do
            {
                changed = false;
                var registry = ChordRegistry.Build(candidate, registryScenarios, win);
                bool ScenarioRefused(string token, string id)
                {
                    if (!Hotkey.TryParse(token, out Hotkey chord)) return token.Length > 0;
                    if (registry.WindowsOwnerOf(chord) != null && windowsConsent.Add("scenarios"))
                        plan.Issues.Add(new SettingsFileIssue("scenarios", "windows"));
                    return rules(chord).Refused || registry.CyrFlipOwnerOf(chord, ChordKind.Launcher, id) != null;
                }
                foreach (SettingsFileEntry entry in SettingsFileInventory.Exported.Where(e => imported.Contains(e.Key)))
                {
                    bool Refused(string token, ChordKind kind, string id)
                    {
                        if (!Hotkey.TryParse(token, out Hotkey chord)) return token.Length > 0;
                        if (registry.WindowsOwnerOf(chord) != null && windowsConsent.Add(entry.Key))
                            plan.Issues.Add(new SettingsFileIssue(entry.Key, "windows"));
                        return rules(chord).Refused || registry.CyrFlipOwnerOf(chord, kind, id) != null;
                    }
                    ChordKind? fixedKind = Kind(entry.Key);
                    if (fixedKind.HasValue && Refused((string)entry.Property!.GetValue(candidate)!, fixedKind.Value, ""))
                    {
                        entry.Property.SetValue(candidate, entry.Property.GetValue(local)); imported.Remove(entry.Key);
                        plan.Issues.Add(new SettingsFileIssue(entry.Key, "chord")); changed = true;
                    }
                    if (entry.Key == "LayoutConversionProfiles")
                    {
                        foreach (var row in candidate.LayoutConversionProfiles)
                            if (Refused(row.Hotkey, ChordKind.Conversion, row.Id))
                            { row.Hotkey = ""; row.Enabled = false; plan.Issues.Add(new SettingsFileIssue(entry.Key, "chord")); changed = true; }
                    }
                    if (entry.Key == "TranslateProfiles")
                        foreach (var row in candidate.TranslateProfiles)
                            if (Refused(row.Hotkey, ChordKind.Translation, row.Id))
                            { row.Hotkey = ""; row.Enabled = false; plan.Issues.Add(new SettingsFileIssue(entry.Key, "chord")); changed = true; }
                }
                if (importedScenarios != null)
                    foreach (var scenario in importedScenarios)
                        if (ScenarioRefused(scenario.Hotkey, scenario.Id.ToString()))
                        { scenario.Hotkey = ""; plan.Issues.Add(new SettingsFileIssue("scenarios", "chord")); changed = true; }
            } while (changed);
            if (importedScenarios != null)
                foreach (var scenario in importedScenarios)
                    if (scenario.Type == LauncherScenarioType.Executable && scenario.Path.Length > 0
                        && LocalMissing(scenario.Path, directory: false))
                        plan.Issues.Add(new SettingsFileIssue("scenarios", "path"));
            if (installed != null && imported.Contains("LayoutConversionProfiles"))
                foreach (var row in candidate.LayoutConversionProfiles)
                    if (!installed.Contains(row.SourceKlid) || !installed.Contains(row.TargetKlid))
                        plan.Issues.Add(new SettingsFileIssue("LayoutConversionProfiles", "layout"));
            normalized = MemoryConfigKey.Capture(candidate);
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported.Where(e => imported.Contains(e.Key)))
            {
                object before = original.Values[entry.Key], after = normalized.Values[entry.Key];
                if (Equals(before, after)) { plan.Unchanged++; continue; }
                bool optIn = (entry.Key == "EnableClipboardHistory" || entry.Key == "EnableQuickNotes") && (int)after != 0
                    || entry.Key == "TranslateEndpoint" && RemoteEndpoint((string)after) || windowsConsent.Contains(entry.Key);
                plan.Changes.Add(new SettingsImportChange(entry, before, after, optIn));
            }
            if (importedScenarios != null)
            {
                bool same = importedScenarios.Count == launcherLocal.Length
                    && !importedScenarios.Where((s, i) => ScenarioSignature(s) != ScenarioSignature(launcherLocal[i])
                        || s.TransferXml != null && launcherLocal[i].TransferXml != null
                            && s.TransferXml != launcherLocal[i].TransferXml).Any();
                if (same) plan.Unchanged++;
                else
                {
                    plan.Changes.Add(new SettingsImportChange(SettingsFileInventory.Scenarios,
                        launcherLocal.Length, importedScenarios.Count, windowsConsent.Contains("scenarios")));
                    plan.Scenarios = importedScenarios;
                }
            }
            plan.Candidate = candidate; return plan;
        }
        private static string ScenarioSignature(LauncherScenario s)
            => string.Join("\n", s.Id.ToString("N"), s.Name, s.Path, s.Arguments, s.WorkingDirectory,
                s.Type, s.RunAsAdmin, s.Hotkey, s.Order, s.YtDlpOutputFolder, s.YtDlpFormat, s.SchemaVersion);
        /// <summary>A rooted path on this PC that names nothing. A remote path is never probed (S0057 D4): an
        /// unreachable share would hang the preview for the SMB timeout.</summary>
        private static bool LocalMissing(string path, bool directory)
        {
            try
            {
                if (!Path.IsPathRooted(path) || LaunchTargets.IsRemotePath(path)) return false;
                return directory ? !Directory.Exists(path) : !File.Exists(path);
            }
            catch { return false; }
        }
        private static ChordKind? Kind(string name)
        {
            switch (name)
            {
                case "CaseHotkey": return ChordKind.Case;
                case "ClipboardHistoryHotkey": return ChordKind.History;
                case "QuickNotesHotkey": return ChordKind.QuickNotes;
                case "ScreenshotHotkey": return ChordKind.Screenshot;
                case "ScreenshotFullHotkey": return ChordKind.ScreenshotFull;
                case "ScreenshotAnnotateAreaHotkey": return ChordKind.ScreenshotAnnotateArea;
                case "ScreenshotFullAnnotateHotkey": return ChordKind.ScreenshotFullAnnotate;
                case "SymbolsHotkey": return ChordKind.Symbols;
                case "TrafficLightHotkey": return ChordKind.TrafficLight;
                case "TextMenuHotkey": return ChordKind.TextMenu;
                case "ExchangeClipboardSendHotkey": return ChordKind.ExchangeSend;
                case "ExchangeClipboardGetHotkey": return ChordKind.ExchangeGet;
                default: return null;
            }
        }
    }
}
