using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CyrFlip
{
    internal static class SettingsSnapshot
    {
        internal static string Folder => Path.Combine(DataFolder.Current, "backups");
        private static IEnumerable<string> Files(string folder) => Directory.GetFiles(folder, "*settings-before-import-*.json")
            .Where(path => Path.GetFileName(path).StartsWith("settings-before-import-", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("CyrFlip-settings-before-import-", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(Path.GetFileName, StringComparer.Ordinal);
        internal static string? Newest(string folder) => Directory.Exists(folder) ? Files(folder).FirstOrDefault() : null;
        private static IEnumerable<string> ScenarioFolders(string folder) => Directory.Exists(folder)
            ? Directory.GetDirectories(folder, "scenarios-before-import-*")
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
        internal static string? NewestScenarios(string folder) => ScenarioFolders(folder).FirstOrDefault();
        internal static string Create(string folder, AppConfig config, IReadOnlyList<string>? scenarios = null)
        {
            Directory.CreateDirectory(folder);
            string stem = "CyrFlip-settings-before-import-" + DateTime.Now.ToString("yy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, stem + ".json");
            for (int suffix = 2; File.Exists(path); suffix++)
                path = Path.Combine(folder, stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".json");
            SettingsFileWriter.Save(path, config, scenarios); return path;
        }
        /// <summary>
        /// The scenario half of the import snapshot (S0072 6.3): a byte-faithful copy of the store's
        /// files into <c>scenarios-before-import-&lt;stamp&gt;/</c> beside the settings snapshot. Returns
        /// the undo - putting the copied files back and taking everything else of the store away - for
        /// Commit's rollback. A failure here must precede even the first registry write, so it throws.
        /// </summary>
        internal static Action CreateScenarios(string folder, LauncherScenarioStore store)
        {
            Directory.CreateDirectory(folder);
            string stem = "scenarios-before-import-" + DateTime.Now.ToString("yy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, stem);
            for (int suffix = 2; Directory.Exists(path); suffix++)
                path = Path.Combine(folder, stem + "-" + suffix.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(path);
            if (Directory.Exists(store.Folder))
                foreach (string source in Directory.GetFiles(store.Folder, "*.xml"))
                    File.Copy(source, Path.Combine(path, Path.GetFileName(source)));
            string storeFolder = store.Folder;
            return () =>
            {
                Directory.CreateDirectory(storeFolder);
                foreach (string file in Directory.GetFiles(storeFolder, "*.xml"))
                    File.Delete(file);
                foreach (string file in Directory.GetFiles(path, "*.xml"))
                    File.Copy(file, Path.Combine(storeFolder, Path.GetFileName(file)));
                store.Reload(); // the store's in-memory list follows the restored files
            };
        }
        internal static void Prune(string folder)
        {
            foreach (string path in Files(folder).Skip(3))
                File.Delete(path);
            foreach (string scenarios in ScenarioFolders(folder).Skip(3))
                Directory.Delete(scenarios, true);
        }
        internal static void Commit(AppConfig live, SettingsImportPlan plan, IConfigKey registry, Action refresh,
            Action<AppConfig> snapshot, Func<Action>? snapshotScenarios = null, Action? applyScenarios = null)
        {
            var current = MemoryConfigKey.Capture(live);
            if (SettingsFileInventory.Exported.Any(e => !Equals(current.Values[e.Key], plan.Baseline.Values[e.Key])))
                throw new SettingsFileException("stale");
            AppConfig before = MemoryConfigKey.Capture(live).Load();
            snapshot(before); // A failure here must precede even the first registry write.
            // The scenario store joins the snapshot before its first write too (S0072 6.3).
            var undo = new List<Action>();
            // Enabling the launcher can seed/migrate scenarios during refresh even when the file
            // has no scenarios section. Protect the folder before every production import.
            Action? restoreScenarios = snapshotScenarios?.Invoke();
            if (restoreScenarios != null) undo.Add(restoreScenarios);
            void Write(AppConfig config)
            {
                var key = MemoryConfigKey.Capture(config);
                foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
                    registry.SetValue(entry.Key, key.Values[entry.Key], entry.Property!.PropertyType == typeof(bool)
                        || entry.Property.PropertyType == typeof(int) ? RegistryValueKind.DWord : RegistryValueKind.String);
                registry.SetValue("TranslateSeeded", config.TranslateSeeded ? 1 : 0, RegistryValueKind.DWord);
            }
            try
            {
                Write(plan.Candidate);
                if (plan.Scenarios != null) applyScenarios?.Invoke();
                SettingsFileInventory.Copy(plan.Candidate, live); refresh();
            }
            catch (Exception commitFailure)
            {
                SettingsFileInventory.Copy(before, live);
                try
                {
                    Write(before);
                    for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                    refresh();
                }
                catch (Exception rollbackFailure) { throw new AggregateException("rollback", commitFailure, rollbackFailure); }
                throw new SettingsFileException("rolled-back");
            }
        }
    }
}
