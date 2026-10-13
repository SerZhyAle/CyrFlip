using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    internal static class SettingsFileReader
    {
        internal const int MaxBytes = 1024 * 1024, MaxRows = 256, MaxStringBytes = 4096, MaxKeys = 2000;
        internal static SettingsFileDocument ReadFile(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new SettingsFileException("limit");
                using (var bytes = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    { if (bytes.Length + n > MaxBytes) throw new SettingsFileException("limit"); bytes.Write(buffer, 0, n); }
                    return Read(bytes.ToArray());
                }
            }
        }
        internal static SettingsFileDocument Read(byte[] bytes)
        {
            if (bytes.Length > MaxBytes) throw new SettingsFileException("limit");
            try
            {
                string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
                ScanLimits(json);
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue("format", out object? format) || !Equals(format, SettingsFileWriter.Format)
                    || !root.TryGetValue("schemaVersion", out object? version) || !(version is int number) || number < 1)
                    throw new SettingsFileException("format");
                if (number > 1) throw new SettingsFileException("version");
                CheckLimits(root, refKeyCount: new int[1]);
                var doc = new SettingsFileDocument();
                if (root.TryGetValue("app", out object? app) && app is string a) doc.App = a;
                if (root.TryGetValue("exportedAtUtc", out object? date) && date is string d) doc.ExportedAtUtc = d;
                foreach (string section in new[] { "settings", "tables" })
                {
                    if (!root.TryGetValue(section, out object? raw) || raw == null) continue;
                    if (!(raw is Dictionary<string, object> map)) throw new SettingsFileException("format");
                    foreach (var pair in map)
                    {
                        var entry = SettingsFileInventory.Exported.FirstOrDefault(e => e.Key == pair.Key && e.IsTable == (section == "tables"));
                        if (entry == null) { doc.UnknownKeys++; continue; }
                        if (pair.Value == null) continue;
                        object? value = null;
                        if (entry.IsTable)
                        {
                            if (pair.Value is object[] rows)
                            {
                                if (rows.Length > MaxRows) throw new SettingsFileException("limit");
                                // A malformed row costs the entire table key, not the other settings.
                                if (rows.All(r => ValidRow(r, entry.Key))) value = serializer.Serialize(rows);
                            }
                        }
                        else if (entry.Property.PropertyType == typeof(bool) && pair.Value is bool b) value = b ? 1 : 0;
                        else if (entry.Property.PropertyType == typeof(int) && pair.Value is int i) value = i;
                        else if (entry.Property.PropertyType == typeof(string) && pair.Value is string s) value = s;
                        if (value == null) doc.Issues.Add(new SettingsFileIssue(pair.Key, "kind"));
                        else doc.Values.Add(pair.Key, value);
                    }
                }
                return doc;
            }
            catch (SettingsFileException) { throw; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is DecoderFallbackException)
            { throw new SettingsFileException("format"); }
        }
        private static readonly string[] StringRowKeys =
        {
            "Id", "Hotkey", "SourceKlid", "TargetKlid", "TargetLang",
            "Label", "Kind", "Address", "Fingerprint", "Group", "AcceptedStatusCodes",
        };
        private static readonly string[] IntRowKeys = { "Ordinal", "IntervalSeconds", "TimeoutMs", "YellowThresholdMs", "FailuresBeforeDown" };
        private static readonly string[] BoolRowKeys = { "AllowUntrustedCertificate", "Notify", "RecordHistory" };

        private static bool ValidRow(object raw, string table)
        {
            if (!(raw is Dictionary<string, object> row)) return false;
            // Only the keys the row's own table knows are judged here; the loader sanitizes the rest. A wrong
            // kind of value costs the whole table key, never the other settings.
            foreach (var pair in row)
            {
                if (pair.Key == "Enabled" || BoolRowKeys.Contains(pair.Key)) { if (!(pair.Value is bool)) return false; }
                else if (IntRowKeys.Contains(pair.Key)) { if (!(pair.Value is int)) return false; }
                else if (StringRowKeys.Contains(pair.Key) && pair.Value != null && !(pair.Value is string)) return false;
            }
            return true;
        }
        private static void ScanLimits(string json)
        {
            bool quoted = false, escape = false; int keys = 0, length = 0;
            foreach (char c in json)
            {
                if (quoted)
                {
                    if (++length > MaxStringBytes * 6 + 2) throw new SettingsFileException("limit");
                    if (escape) escape = false; else if (c == '\\') escape = true; else if (c == '"') quoted = false;
                }
                else if (c == '"') { quoted = true; length = 0; }
                else if (c == ':' && ++keys > MaxKeys) throw new SettingsFileException("limit");
            }
        }
        private static void CheckLimits(object? value, int[] refKeyCount)
        {
            if (value is string text && Encoding.UTF8.GetByteCount(text) > MaxStringBytes) throw new SettingsFileException("limit");
            if (value is Dictionary<string, object> map)
            {
                foreach (var pair in map)
                { if (++refKeyCount[0] > MaxKeys) throw new SettingsFileException("limit"); CheckLimits(pair.Key, refKeyCount); CheckLimits(pair.Value, refKeyCount); }
            }
            else if (value is object[] rows)
            {
                if (rows.Length > MaxRows) throw new SettingsFileException("limit");
                foreach (object row in rows) CheckLimits(row, refKeyCount);
            }
        }
    }
}
