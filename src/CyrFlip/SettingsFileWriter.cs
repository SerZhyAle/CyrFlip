using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    internal static class SettingsFileWriter
    {
        internal const string Format = "CyrFlip Settings";
        /// <summary>The schema this build writes (S0072 5.5). A reader is kept for every version that ever wrote user data.</summary>
        internal const int SchemaVersion = 2;
        internal static string DefaultFileName(DateTime savedAtLocal)
            => "CyrFlip-settings-" + savedAtLocal.ToString("yy-MM-dd_HHmm", CultureInfo.InvariantCulture) + ".json";
        /// <summary>
        /// The defaults of every exported key, with the symbols and traffic-light blocks forced on for
        /// the capture: <see cref="AppConfig.SaveTo"/> writes those blocks only once the module has been
        /// enabled, but since v2 the file carries them whatever the switch says - an off module exports
        /// its defaults, and importing them keeps it off.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, object> Defaults =
            MemoryConfigKey.Capture(new AppConfig { EnableSymbols = true, EnableTrafficLight = true, EnableExchangeClipboard = true }).Values;
        /// <param name="scenarios">
        /// The verbatim scenario documents of the quick-launch store (S0072 5.3); an empty store writes
        /// an empty array. Null keeps the section empty too - only tests and the v1 regression may pass null.
        /// </param>
        internal static string Write(AppConfig config, DateTime utc, string app, IReadOnlyList<string>? scenarios = null)
        {
            // Refuse an export that would silently lose a scenario when our reader imports it.
            if (scenarios != null && (scenarios.Count > SettingsFileReader.MaxScenarioCount
                || scenarios.Any(xml => string.IsNullOrEmpty(xml) || Encoding.UTF8.GetByteCount(xml) > SettingsFileReader.MaxScenarioBytes)))
                throw new SettingsFileException("limit");
            var settings = new Dictionary<string, object>();
            var tables = new Dictionary<string, object>();
            var registry = MemoryConfigKey.Capture(config);
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
            {
                object value = registry.Values.TryGetValue(entry.Key, out object? stored) ? stored : Defaults[entry.Key];
                if (entry.Key == "TranslateEndpoint" && ((string)value).Length > 0
                    && (!Uri.TryCreate((string)value, UriKind.Absolute, out Uri? endpoint)
                        || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0))
                    throw new SettingsFileException("endpoint");
                if (entry.IsTable) tables.Add(entry.Key, new JavaScriptSerializer().DeserializeObject((string)value));
                else settings.Add(entry.Key, entry.Property!.PropertyType == typeof(bool) ? (object)((int)value != 0) : value);
            }
            tables.Add("scenarios", (scenarios ?? Array.Empty<string>()).ToArray());
            return Json(new Dictionary<string, object> {
                { "format", Format }, { "schemaVersion", SchemaVersion },
                { "exportedAtUtc", utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                { "app", app }, { "settings", settings }, { "tables", tables }
            }, 0) + "\n";
        }
        // The v1 inventory, frozen by the files people already hold (canon invariant 10): the 49
        // preferences and tables of S0055 plus the screenshot chords S0071 shipped into v1. Written
        // only by WriteV1, for the writer-v1.json byte regression and the v1-reader tests.
        private static readonly HashSet<string> V1Keys = new HashSet<string>(new[] {
            "UiLanguage", "Theme", "KeepSystemAwake", "KeepScreenOn", "EnableCursorChange", "EnableCaretOverlay",
            "CaretDotMode", "CursorSize", "EnableLanguageSwitch", "FlipCapsLockAfter", "EnableHotkeys", "EnableCaseHotkey",
            "CaseHotkey", "EnableHistoryHotkey", "ClipboardHistoryHotkey", "DeferToRemoteDesktop", "ConvertSymbols",
            "LayoutConversionProfiles", "EnableClipboardHistory", "PauseClipboardHistory", "ShowClipboardHistoryOnStartup",
            "ClipboardHistoryOpacity", "EnableContextMenu", "ContextMenuChord", "EnableTextMenuHotkey", "TextMenuHotkey",
            "EnableTranslate", "TranslateEndpoint", "TranslateModel", "TranslateTimeoutSeconds", "TranslateKeepAliveMinutes",
            "TranslateAutoStartServer", "TranslateProfiles", "TranslateCopyResult", "TranslatePasteResult",
            "TranslateShowSource", "TranslateWindowTimeout", "TranslateWindowOpacity", "EnableQuickNotes",
            "EnableQuickNotesHotkey", "QuickNotesHotkey", "QuickNotesWordWrap", "ExchangeNotesWarningOff",
            "EnableScreenshotHotkey", "ScreenshotHotkey", "EnableScreenshotFullHotkey", "ScreenshotFullHotkey",
            "EnableScreenshotAnnotate", "EnableScreenshotAnnotateAreaHotkey", "ScreenshotAnnotateAreaHotkey",
            "EnableScreenshotFullAnnotateHotkey", "ScreenshotFullAnnotateHotkey", "EnableScreenshotFolderHotkey",
            "ScreenshotFolderHotkey", "ScreenshotSaveEnabled", "TrafficLightTargets"
        });
        /// <summary>The v1 writer, kept only for the writer-v1.json regression (S0072 5.5).</summary>
        internal static string WriteV1(AppConfig config, DateTime utc, string app, bool includeTrafficLightTargets = false)
        {
            var settings = new Dictionary<string, object>();
            var tables = new Dictionary<string, object>();
            var registry = MemoryConfigKey.Capture(config);
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported.Where(e => V1Keys.Contains(e.Key)))
            {
                if (entry.Key == "TrafficLightTargets" && !includeTrafficLightTargets) continue;
                object value = registry.Values[entry.Key];
                if (entry.IsTable) tables.Add(entry.Key, new JavaScriptSerializer().DeserializeObject((string)value));
                else settings.Add(entry.Key, entry.Property!.PropertyType == typeof(bool) ? (object)((int)value != 0) : value);
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
        internal static void Save(string path, AppConfig config, IReadOnlyList<string>? scenarios = null)
            => SaveText(path, Write(config, DateTime.UtcNow, typeof(AppConfig).Assembly.GetName().Version!.ToString(), scenarios));
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
