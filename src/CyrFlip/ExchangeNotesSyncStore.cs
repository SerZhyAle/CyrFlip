using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// <c>quick-notes-sync.json</c> in the data folder: the <see cref="ExchangeNotesSyncState"/> of the one entry the
    /// quick notes sync with (<c>DEVICE-EXCHANGE</c> 1.13 item FM, ticket S0105 assumption A2). It holds ids, revisions
    /// and flags only - no title, no text - so it is plain JSON; it still lists which notes exist, so it never enters a
    /// support bundle or the settings file.
    ///
    /// <para>A file that does not read gives an empty state bound to no entry. That is safe, not lossy: the next pass
    /// lists the store from 0, adopts every record whose content equals the local note and keeps both versions of every
    /// other one (<see cref="ExchangeNotesSync"/>, "a lost state file").</para>
    /// </summary>
    internal static class ExchangeNotesSyncStore
    {
        public const string FileName = "quick-notes-sync.json";
        private const int Version = 1;

        public static string DefaultPath => DiagnosticLog.Path(FileName);

        public static ExchangeNotesSyncState Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new ExchangeNotesSyncState();
                return Parse(File.ReadAllText(path, Encoding.UTF8)) ?? new ExchangeNotesSyncState();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ExchangeNotesSyncState();
            }
        }

        /// <summary>Writes through a temp file, so a kill mid-write leaves the previous state. False when the disk refused.</summary>
        public static bool Save(string path, ExchangeNotesSyncState state) => SaveText(path, Serialize(state));

        /// <summary><see cref="Save"/> of a state serialized earlier (under its owner's lock).</summary>
        public static bool SaveText(string path, string json)
        {
            string temp = path + ".tmp";
            try
            {
                string? folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { File.Delete(temp); }
                catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
                {
                    // The save itself is reported by the false below; this is only the leftover half-file (ids, no text).
                    QuickNotesLog.Log("sync state temp file not removed: " + cleanup.GetType().Name);
                }
                return false;
            }
        }

        public static void Delete(string path)
        {
            foreach (string p in new[] { path, path + ".tmp" })
            {
                try { File.Delete(p); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    QuickNotesLog.Log("sync state file not removed: " + ex.GetType().Name);
                }
            }
        }

        internal static string Serialize(ExchangeNotesSyncState state)
        {
            var notes = new List<object>();
            foreach (KeyValuePair<Guid, ExchangeNoteSyncState> pair in state.Notes)
            {
                ExchangeNoteSyncState st = pair.Value;
                var n = new Dictionary<string, object> { ["id"] = ExchangeNotes.NoteIdOf(pair.Key), ["rev"] = st.Revision };
                if (st.Changed) n["changed"] = true;
                if (st.DeletedLocally) n["deleted"] = true;
                if (st.Unsupported) n["unsupported"] = true;
                if (st.TooLarge) n["tooLarge"] = true;
                if (st.Recipients.Count > 0) n["to"] = st.Recipients.ToArray();
                notes.Add(n);
            }
            var root = new Dictionary<string, object>
            {
                ["v"] = Version,
                ["entryId"] = state.EntryId,
                ["notesRevision"] = state.NotesRevision,
                ["notes"] = notes,
            };
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(root);
        }

        /// <summary>The state a text holds, or null when it is not this file's format. A bad row is dropped, not the file.</summary>
        internal static ExchangeNotesSyncState? Parse(string json)
        {
            Dictionary<string, object>? root;
            try { root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return null; }
            if (root == null || !(root.TryGetValue("v", out object? v) && v is int version && version == Version)) return null;

            var state = new ExchangeNotesSyncState
            {
                EntryId = root.TryGetValue("entryId", out object? e) && e is string id ? id : "",
                NotesRevision = LongOf(root, "notesRevision"),
            };
            if (root.TryGetValue("notes", out object? raw) && raw is object[] rows)
            {
                foreach (object row in rows)
                {
                    if (!(row is Dictionary<string, object> n)) continue;
                    if (!(n.TryGetValue("id", out object? nid) && nid is string text && ExchangeNotes.TryParseNoteId(text, out Guid noteId))) continue;
                    var st = new ExchangeNoteSyncState
                    {
                        Revision = LongOf(n, "rev"),
                        Changed = FlagOf(n, "changed"),
                        DeletedLocally = FlagOf(n, "deleted"),
                        Unsupported = FlagOf(n, "unsupported"),
                        TooLarge = FlagOf(n, "tooLarge"),
                    };
                    if (n.TryGetValue("to", out object? to) && to is object[] ids)
                        foreach (object d in ids)
                            if (d is string device && device.Length > 0) st.Recipients.Add(device);
                    state.Notes[noteId] = st;
                }
            }
            return state;
        }

        private static long LongOf(Dictionary<string, object> m, string name)
        {
            if (!m.TryGetValue(name, out object? v)) return 0;
            long n = v is int i ? i : v is long l ? l : 0;
            return n < 0 ? 0 : n;
        }

        private static bool FlagOf(Dictionary<string, object> m, string name)
            => m.TryGetValue(name, out object? v) && v is bool b && b;
    }
}
