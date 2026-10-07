using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    internal static class SettingsFileWriter
    {
        internal const string Format = "CyrFlip Settings";
        internal static string DefaultFileName(DateTime savedAtLocal)
            => "CyrFlip-settings-" + savedAtLocal.ToString("yy-MM-dd_HHmm", CultureInfo.InvariantCulture) + ".json";
        internal static string Write(AppConfig config, DateTime utc, string app)
        {
            var settings = new Dictionary<string, object>();
            var tables = new Dictionary<string, object>();
            var registry = MemoryConfigKey.Capture(config);
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
            {
                object value = registry.Values[entry.Key];
                if (entry.Key == "TranslateEndpoint" && ((string)value).Length > 0
                    && (!Uri.TryCreate((string)value, UriKind.Absolute, out Uri? endpoint)
                        || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0))
                    throw new SettingsFileException("endpoint");
                if (entry.IsTable) tables.Add(entry.Key, new JavaScriptSerializer().DeserializeObject((string)value));
                else settings.Add(entry.Key, entry.Property.PropertyType == typeof(bool) ? (object)((int)value != 0) : value);
            }
            return Json(new Dictionary<string, object> {
                { "format", Format }, { "schemaVersion", 1 },
                { "exportedAtUtc", utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                { "app", app }, { "settings", settings }, { "tables", tables }
            }, 0) + "\n";
        }
        internal static string Json(object? value, int indent = 0)
        {
            if (value == null) return "null";
            if (value is string text) return Quote(text);
            if (value is bool flag) return flag ? "true" : "false";
            string pad = new string(' ', indent), next = pad + "  ";
            var items = new List<string>();
            if (value is IDictionary<string, object> map)
            {
                foreach (var pair in map) items.Add(next + Quote(pair.Key) + ": " + Json(pair.Value, indent + 2));
                return items.Count == 0 ? "{}" : "{\n" + string.Join(",\n", items) + "\n" + pad + "}";
            }
            if (value is IEnumerable rows)
            {
                foreach (object row in rows) items.Add(next + Json(row, indent + 2));
                return items.Count == 0 ? "[]" : "[\n" + string.Join(",\n", items) + "\n" + pad + "]";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }
        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
        internal static void Save(string path, AppConfig config)
            => SaveText(path, Write(config, DateTime.UtcNow, typeof(AppConfig).Assembly.GetName().Version!.ToString()));
        internal static void SaveText(string path, string text)
        {
            // Validate our own output as well: never offer an export our reader would refuse.
            SettingsFileReader.Read(Encoding.UTF8.GetBytes(text));
            string target = Path.GetFullPath(path);
            string temp = Path.Combine(Path.GetDirectoryName(target)!, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
