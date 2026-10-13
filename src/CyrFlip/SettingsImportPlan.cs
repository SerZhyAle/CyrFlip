using System;
using System.Collections.Generic;
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
                else if (Kind(pair.Key).HasValue && !Hotkey.TryParse((string)pair.Value, out _)) refusal = "chord";
                else if (pair.Key == "ContextMenuChord" && !MouseChord.TryParse((string)pair.Value, out _)) refusal = "chord";
                else if (pair.Key == "TranslateEndpoint" && ((string)pair.Value).Length > 0)
                {
                    if (!Uri.TryCreate((string)pair.Value, UriKind.Absolute, out Uri? uri)
                        || (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0
                        || uri.Query.Length > 0 || uri.Fragment.Length > 0) refusal = "endpoint";
                }
                if (refusal != null) { plan.Issues.Add(new SettingsFileIssue(pair.Key, refusal)); continue; }
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
            var launcher = (scenarios ?? Array.Empty<LauncherScenario>()).ToArray();
            var retainedIds = new HashSet<string>(StringComparer.Ordinal);
            if (!imported.Contains("LayoutConversionProfiles")) foreach (var row in candidate.LayoutConversionProfiles) retainedIds.Add(row.Id);
            if (!imported.Contains("TranslateProfiles")) foreach (var row in candidate.TranslateProfiles) retainedIds.Add(row.Id);
            foreach (var scenario in launcher) retainedIds.Add(scenario.Id.ToString("N"));
            string UniqueId(string id)
            {
                if (retainedIds.Add(id)) return id;
                string next; do { next = Guid.NewGuid().ToString("N"); } while (!retainedIds.Add(next)); return next;
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
                var registry = ChordRegistry.Build(candidate, launcher, win);
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
                    if (fixedKind.HasValue && Refused((string)entry.Property.GetValue(candidate)!, fixedKind.Value, ""))
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
            } while (changed);
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
                    || entry.Key == "TrafficLightTargets"
                    || entry.Key == "TranslateEndpoint" && RemoteEndpoint((string)after) || windowsConsent.Contains(entry.Key);
                plan.Changes.Add(new SettingsImportChange(entry, before, after, optIn));
            }
            plan.Candidate = candidate; return plan;
        }
        private static ChordKind? Kind(string name)
        {
            switch (name)
            {
                case "CaseHotkey": return ChordKind.Case;
                case "ClipboardHistoryHotkey": return ChordKind.History;
                case "QuickNotesHotkey": return ChordKind.QuickNotes;
                case "ScreenshotHotkey": return ChordKind.Screenshot;
                case "TextMenuHotkey": return ChordKind.TextMenu;
                default: return null;
            }
        }
    }
}
