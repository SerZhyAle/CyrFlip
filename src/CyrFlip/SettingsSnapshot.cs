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
        internal static string Create(string folder, AppConfig config)
        {
            Directory.CreateDirectory(folder);
            string stem = "CyrFlip-settings-before-import-" + DateTime.Now.ToString("yy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, stem + ".json");
            for (int suffix = 2; File.Exists(path); suffix++)
                path = Path.Combine(folder, stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".json");
            SettingsFileWriter.Save(path, config); return path;
        }
        internal static void Prune(string folder)
        {
            foreach (string path in Files(folder).Skip(3))
                File.Delete(path);
        }
        internal static void Commit(AppConfig live, SettingsImportPlan plan, IConfigKey registry, Action refresh,
            Action<AppConfig> snapshot)
        {
            var current = MemoryConfigKey.Capture(live);
            if (SettingsFileInventory.Exported.Any(e => !Equals(current.Values[e.Key], plan.Baseline.Values[e.Key])))
                throw new SettingsFileException("stale");
            AppConfig before = MemoryConfigKey.Capture(live).Load();
            snapshot(before); // A failure here must precede even the first registry write.
            void Write(AppConfig config)
            {
                var key = MemoryConfigKey.Capture(config);
                foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
                    registry.SetValue(entry.Key, key.Values[entry.Key], entry.Property.PropertyType == typeof(bool)
                        || entry.Property.PropertyType == typeof(int) ? RegistryValueKind.DWord : RegistryValueKind.String);
                registry.SetValue("TranslateSeeded", config.TranslateSeeded ? 1 : 0, RegistryValueKind.DWord);
            }
            try
            {
                Write(plan.Candidate); SettingsFileInventory.Copy(plan.Candidate, live); refresh();
            }
            catch (Exception commitFailure)
            {
                SettingsFileInventory.Copy(before, live);
                try { Write(before); refresh(); }
                catch (Exception rollbackFailure) { throw new AggregateException("rollback", commitFailure, rollbackFailure); }
                throw new SettingsFileException("rolled-back");
            }
        }
    }
}
