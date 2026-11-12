using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>One group of the settings standard: its wire id (never translated) and the page caption it is shown under.</summary>
    internal sealed class SettingsStandardGroup
    {
        internal SettingsStandardGroup(string id, string caption, string[] keys) { Id = id; Caption = caption; Keys = keys; }
        /// <summary>The wire id (<c>DEVICE-EXCHANGE</c> item EQ): lowercase ASCII letters, digits and hyphens.</summary>
        internal string Id { get; }
        /// <summary>The English caption, the key of its translation.</summary>
        internal string Caption { get; }
        /// <summary>The registry values the group carries, in the order of <see cref="SettingsFileInventory.Exported"/>.</summary>
        internal string[] Keys { get; }
    }

    /// <summary>What a read of the standard understood: the values to apply, or the code of why nothing applies.</summary>
    internal sealed class SettingsStandardRead
    {
        /// <summary>Empty when the payload was understood; else a code of <see cref="ExchangeCodes"/>.</summary>
        internal string Code = "";
        internal SettingsFileDocument Document = new SettingsFileDocument();
        internal bool Ok => Code.Length == 0;
    }

    /// <summary>
    /// The application settings standard of CyrFlip on an exchange server (<c>DEVICE-EXCHANGE</c> 1.12, items EN to
    /// ER, ticket S0095): which settings are exchangeable and in which group, the payload a save writes and the
    /// reading of one a get returned. Pure - no network, no registry, no WinForms.
    ///
    /// <para>Every exchangeable setting is a value of the settings file's inventory, so a read applies through the
    /// same validation and transaction as a file import. A value that is not declared here is local-only (item EQ):
    /// the serializer never writes it and the reader ignores it in an inbound payload.</para>
    /// </summary>
    internal static class SettingsStandard
    {
        /// <summary>The <c>appId</c> of CyrFlip (item EN), bound to the <c>product</c> <c>cyrflip-windows</c>.</summary>
        internal const string AppId = "cyrflip";

        /// <summary>The one <c>formatVersion</c> 1.12 defines.</summary>
        internal const int FormatVersion = 1;

        /// <summary>The bound of <c>settingsPayload</c>, in bytes of UTF-8 (item EO).</summary>
        internal const int MaxPayloadBytes = 65536;

        /// <summary>
        /// The inventory values that stay on this computer although a settings file carries them, each with its
        /// category of item EQ. Everything of <see cref="SettingsFileInventory.Excluded"/> stays too, and so does
        /// every window rectangle and navigation value (the <c>navigation/</c> groups) and the quick-launch module,
        /// whose scenarios name programs by their paths.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> LocalOnly = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ScreenshotFolder"] = "machine binding",
            ["ExchangeClipboardReceivedFolder"] = "machine binding",
            ["TranslateEndpoint"] = "network binding",
            ["TrafficLightTargets"] = "machine and network binding",
            ["EnableScenarioLauncher"] = "machine binding",
            ["scenarios"] = "machine binding",
        };

        private static readonly (string Prefix, string Id, string Caption)[] GroupOfPrefix =
        {
            ("general/", "general", "General"),
            ("indicators/", "indicators", "Indicators"),
            ("hotkeys/", "shortcuts", "Hotkeys and text correction"),
            ("conversions/", "shortcuts", "Hotkeys and text correction"),
            ("selection/", "shortcuts", "Hotkeys and text correction"),
            ("clipboard/", "clipboard", "Clipboard"),
            ("translate/", "translate", "Translation"),
            ("notes/", "quick-notes", "Quick notes"),
            ("graphics/", "graphics", "Screenshots"),
            ("symbols/", "symbols", "Symbols"),
            ("trafficlight/", "traffic-light", "Availability traffic light"),
            ("exchangeserver/", "exchange-server", "Exchange server"),
        };

        /// <summary>The groups, in the page order of the settings window.</summary>
        internal static readonly IReadOnlyList<SettingsStandardGroup> Groups = BuildGroups();

        private static IReadOnlyList<SettingsStandardGroup> BuildGroups()
        {
            var order = new List<string>();
            var captions = new Dictionary<string, string>(StringComparer.Ordinal);
            var keys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var g in GroupOfPrefix)
            {
                if (keys.ContainsKey(g.Id)) continue;
                order.Add(g.Id); captions[g.Id] = g.Caption; keys[g.Id] = new List<string>();
            }
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
            {
                string? id = GroupIdOf(entry);
                if (id != null) keys[id].Add(entry.Key);
            }
            return order.Select(id => new SettingsStandardGroup(id, captions[id], keys[id].ToArray())).ToArray();
        }

        /// <summary>The group a value of the inventory belongs to, or null when it is local-only.</summary>
        internal static string? GroupIdOf(SettingsFileEntry entry)
        {
            if (LocalOnly.ContainsKey(entry.Key) || SettingsFileInventory.Excluded.ContainsKey(entry.Key)) return null;
            foreach (var g in GroupOfPrefix)
                if (entry.Group.StartsWith(g.Prefix, StringComparison.Ordinal)) return g.Id;
            return null;
        }

        internal static SettingsStandardGroup? Find(string id) => Groups.FirstOrDefault(g => g.Id == id);

        /// <summary>Whether a group id has the shape item EQ fixes: lowercase ASCII letters, digits and hyphens, a letter first, at most 32.</summary>
        internal static bool IsValidGroupId(string id)
        {
            if (id.Length == 0 || id.Length > 32 || id[0] < 'a' || id[0] > 'z') return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) return false;
            return true;
        }

        /// <summary>
        /// The payload of a save: <c>{"version":1,"groups":{..}}</c> holding the chosen groups, in the order of
        /// <see cref="Groups"/>, each with every exchangeable value of <paramref name="config"/>. A group not chosen
        /// is absent. Null when the text would exceed <see cref="MaxPayloadBytes"/>.
        /// </summary>
        internal static string? Serialize(AppConfig config, IEnumerable<string> groupIds)
        {
            var chosen = new HashSet<string>(groupIds, StringComparer.Ordinal);
            MemoryConfigKey registry = MemoryConfigKey.Capture(config);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var groups = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (SettingsStandardGroup group in Groups)
            {
                if (!chosen.Contains(group.Id)) continue;
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string key in group.Keys)
                {
                    SettingsFileEntry entry = SettingsFileInventory.Exported.First(e => e.Key == key);
                    object value = registry.Values.TryGetValue(key, out object? stored) ? stored : SettingsFileWriter.Defaults[key];
                    if (entry.IsTable) values[key] = serializer.DeserializeObject((string)value) ?? Array.Empty<object>();
                    else values[key] = entry.Property!.PropertyType == typeof(bool) ? (object)((int)value != 0) : value;
                }
                groups[group.Id] = values;
            }
            var root = new Dictionary<string, object>(StringComparer.Ordinal) { ["version"] = FormatVersion, ["groups"] = groups };
            string text = serializer.Serialize(root);
            return Encoding.UTF8.GetByteCount(text) <= MaxPayloadBytes ? text : null;
        }

        /// <summary>
        /// Reads what <c>settings_standard_payload</c> carried (item ER): nothing is taken until the whole answer is
        /// understood - one object, a <c>version</c> equal to <paramref name="formatVersion"/>, a <c>formatVersion</c>
        /// this program implements and a <c>groups</c> object of objects. Then only the chosen groups, and in each only
        /// the values this program declares in that group; an unknown group or key is ignored and an absent key keeps
        /// the local value. A value of the wrong kind is an issue of the document, as in a settings file.
        /// </summary>
        internal static SettingsStandardRead Parse(string payload, long formatVersion, IEnumerable<string> groupIds)
        {
            if (formatVersion > FormatVersion) return Fail(ExchangeCodes.FormatUnsupported);
            if (formatVersion != FormatVersion) return Fail(ExchangeCodes.InvalidPayload);
            if (payload == null || Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes) return Fail(ExchangeCodes.InvalidPayload);

            var serializer = new JavaScriptSerializer { MaxJsonLength = SettingsFileReader.MaxBytes, RecursionLimit = 32 };
            Dictionary<string, object>? root;
            try { root = serializer.DeserializeObject(payload) as Dictionary<string, object>; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Fail(ExchangeCodes.InvalidPayload); }
            if (root == null || !root.TryGetValue("version", out object? version) || !(version is int v) || v != formatVersion
                || !root.TryGetValue("groups", out object? rawGroups) || !(rawGroups is Dictionary<string, object> groups)
                || groups.Values.Any(g => !(g is Dictionary<string, object>)))
                return Fail(ExchangeCodes.InvalidPayload);

            var chosen = new HashSet<string>(groupIds, StringComparer.Ordinal);
            var read = new SettingsStandardRead();
            try
            {
                foreach (var pair in groups)
                {
                    SettingsStandardGroup? group = Find(pair.Key);
                    if (group == null || !chosen.Contains(group.Id)) continue;
                    foreach (var value in (Dictionary<string, object>)pair.Value)
                    {
                        if (!group.Keys.Contains(value.Key)) { read.Document.UnknownKeys++; continue; }
                        if (value.Value == null) continue;
                        SettingsFileEntry entry = SettingsFileInventory.Exported.First(e => e.Key == value.Key);
                        object? converted = SettingsFileReader.ConvertValue(entry, value.Value, serializer);
                        if (converted == null) read.Document.Issues.Add(new SettingsFileIssue(value.Key, "kind"));
                        else read.Document.Values[value.Key] = converted;
                    }
                }
            }
            catch (SettingsFileException) { return Fail(ExchangeCodes.InvalidPayload); }
            return read;
        }

        // The updatedAt this program last saw per entry, for "last saved" / "last read" and for nothing else (item ER).
        // In memory only: it describes this run, and a standard is never compared with it.
        private static readonly Dictionary<string, KeyValuePair<bool, string>> Seen = new Dictionary<string, KeyValuePair<bool, string>>(StringComparer.Ordinal);

        internal static void RememberSeen(string entryId, bool saved, string updatedAt)
        {
            lock (Seen) Seen[entryId] = new KeyValuePair<bool, string>(saved, updatedAt);
        }

        /// <summary>Whether the last act on the entry was a save (else a read) and the server's time of it; null before either.</summary>
        internal static KeyValuePair<bool, string>? LastSeen(string entryId)
        {
            lock (Seen) return Seen.TryGetValue(entryId, out var seen) ? seen : (KeyValuePair<bool, string>?)null;
        }

        private static SettingsStandardRead Fail(string code) => new SettingsStandardRead { Code = code };
    }
}
