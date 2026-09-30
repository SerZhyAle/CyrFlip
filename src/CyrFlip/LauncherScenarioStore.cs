using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// A scenario file in a format this build does not read (renamed root, newer <c>schemaVersion</c>).
    /// Not damage: the single-file import tells the user to update CyrFlip.
    /// </summary>
    internal sealed class ScenarioFormatException : Exception
    {
        public ScenarioFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// File-per-scenario storage for the launcher: one <c>{guid}.xml</c> per scenario under
    /// <c>%APPDATA%\CyrFlip\Scenarios</c> (the OneClickRunner storage model, moved to CyrFlip's
    /// folder). A corrupted file is skipped and counted, never fatal - user XML is user data
    /// (spec §9). All operations run on the UI thread (IPC commands are marshalled there first),
    /// so there is no locking; the one-shot <c>/launcher-run</c> process builds its own instance.
    ///
    /// The folder is created lazily on the first write, so merely opening the settings window on a
    /// machine that never enabled the launcher creates nothing.
    /// </summary>
    internal sealed class LauncherScenarioStore
    {
        private readonly string _folder;
        private List<LauncherScenario> _items = new List<LauncherScenario>();

        /// <summary>Files that failed to parse on the last <see cref="Reload"/> (name only, for the UI count).</summary>
        public List<string> LoadErrors { get; } = new List<string>();

        /// <summary>The production folder. Roaming AppData, exactly like OneClickRunner's own store.</summary>
        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CyrFlip", "Scenarios");

        /// <summary>OneClickRunner's scenario folder - the migration source. Read-only, never written.</summary>
        public static string OneClickRunnerFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OneClickRunner", "Scenarios");

        public string Folder => _folder;

        // A store that only looks (S0034 LS2-9): the log bundle counts scenarios, and a second store
        // that re-identified, renumbered or cleaned up on its own load used to rewrite files behind
        // the live store's back - Jump List tasks and chords then named an id no file had.
        private readonly bool _readOnly;

        public LauncherScenarioStore(string? folder = null, bool readOnly = false)
        {
            _folder = folder ?? DefaultFolder;
            _readOnly = readOnly;
            Reload();
        }

        /// <summary>Ordered snapshot of the current list (callers cannot corrupt the store's order).</summary>
        public List<LauncherScenario> All => new List<LauncherScenario>(_items);

        public int Count => _items.Count;

        /// <summary>
        /// Bumped on every change to the list (a reload, an add, an edit, a removal, a move), so a
        /// surface that mirrors it can tell "nothing changed" without comparing scenarios (S0030 HT-1).
        /// </summary>
        public int Version { get; private set; }

        public LauncherScenario? Find(Guid id) => _items.Find(s => s.Id == id);

        /// <summary>Re-read every XML in the folder. Corrupt files land in <see cref="LoadErrors"/>.</summary>
        public void Reload()
        {
            Version++;
            _items.Clear();
            LoadErrors.Clear();
            if (!Directory.Exists(_folder))
                return;

            if (!_readOnly) DeleteStaleTempFiles();

            // Sorted, so "which of two same-Id files keeps the Id" is the same answer on every load.
            string[] files = Directory.GetFiles(_folder, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<Guid>();
            var reidentified = new List<LauncherScenario>();
            foreach (string file in files)
            {
                // "*.xml" also matches through 8.3 aliases; only a real .xml is a scenario.
                if (!file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

                LauncherScenario? item = TryRead(file, out _);
                if (item == null)
                {
                    LoadErrors.Add(Path.GetFileName(file));
                    if (!_readOnly) LauncherLog.Log("Store: failed to read " + Path.GetFileName(file));
                    continue;
                }
                item.Filename = Path.GetFileName(file);

                // One identity per scenario (S0008 LS-10). A file with no <Id> got a fresh one from
                // the reader and would get another on the next load; a copied file shares its
                // original's, and Update/Remove would then act on the wrong file. Both get a new Guid,
                // written to their own file at once.
                if (!_readOnly && (!item.IdWasAssigned || item.Id == Guid.Empty || !seen.Add(item.Id)))
                {
                    item.Id = Guid.NewGuid();
                    seen.Add(item.Id);
                    reidentified.Add(item);
                    LauncherLog.Log("Store: assigned a new identity to " + item.Filename);
                }
                _items.Add(item);
            }
            if (_readOnly) SortOnly();
            else NormalizeOrder(reidentified);
        }

        private void SortOnly()
            => _items = _items.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// A save that died between writing its temp file and moving it into place leaves the temp
        /// file behind. It is never read as a scenario; one older than a day is removed.
        /// </summary>
        private void DeleteStaleTempFiles()
        {
            try
            {
                foreach (string temp in Directory.GetFiles(_folder, "*.tmp"))
                {
                    try
                    {
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(temp) > TimeSpan.FromDays(1))
                            File.Delete(temp);
                    }
                    catch { /* another writer's, or locked - next load */ }
                }
            }
            catch { /* the folder listing itself failed - Reload reports what matters */ }
        }

        /// <summary>
        /// Parse one scenario XML; null when unreadable. The legacy magic path is surfaced as the
        /// yt-dlp type so the editor reflects reality (the execution path handles the sentinel too) -
        /// <c>SCENARIO-FILE</c> rule 4, normalized in our own store only.
        ///
        /// <para>An unknown element is ignored (rule 2). What is refused, whole and never partly read, is
        /// a file this build cannot honestly understand (rule 9, counted and named by the callers): a root
        /// that is not <c>AppItem</c>, a <c>schemaVersion</c> above <see cref="LauncherScenario.SupportedSchemaMajor"/>
        /// or one that is not a number, and an unknown <c>Type</c> value - an action this reader does not
        /// understand must not run its <c>Path</c> as a program (rule 10; S0014 B1, B2). The first two
        /// surface as <see cref="ScenarioFormatException"/>, so the single-file import can say "newer
        /// format" instead of printing the serializer's text.</para>
        /// </summary>
        public static LauncherScenario? TryRead(string file, out Exception? error)
        {
            error = null;
            try
            {
                var serializer = new XmlSerializer(typeof(LauncherScenario));
                using FileStream stream = File.OpenRead(file);
                using XmlReader reader = XmlReader.Create(stream);
                reader.MoveToContent();
                CheckRoot(reader);
                var item = (LauncherScenario?)serializer.Deserialize(reader);
                if (item != null && item.Path == LauncherScenario.LegacyYtDlpSentinel
                    && item.Type == LauncherScenarioType.Executable)
                    item.Type = LauncherScenarioType.YtDlp;
                return item;
            }
            catch (Exception ex)
            {
                error = ex;
                return null;
            }
        }

        /// <summary>
        /// The two refusals that are about the file's format rather than its damage: the root is not
        /// <c>AppItem</c> (a breaking change renames it, <c>SCENARIO-FILE</c> section 3) or the declared
        /// <c>schemaVersion</c> is above what this build reads.
        /// </summary>
        private static void CheckRoot(XmlReader reader)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "AppItem" || reader.NamespaceURI.Length != 0)
                throw new ScenarioFormatException("the root element is not AppItem");

            string? declared = reader.GetAttribute("schemaVersion");
            if (declared == null) return;
            string major = declared.Trim().Split('.')[0];
            if (!int.TryParse(major, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int version))
                throw new ScenarioFormatException("schemaVersion is not a number");
            if (version > LauncherScenario.SupportedSchemaMajor)
                throw new ScenarioFormatException("schemaVersion " + declared.Trim() + " is newer than this build reads");
        }

        /// <summary>Append a scenario: fresh filename when empty, order = end of list, then persist.</summary>
        public void Add(LauncherScenario item)
        {
            if (string.IsNullOrEmpty(item.Filename))
                item.Filename = item.Id + ".xml";
            item.Order = _items.Count == 0 ? 0 : _items.Max(s => s.Order) + 1;
            _items.Add(item);
            Version++;
            SaveItem(item);
        }

        public void Update(LauncherScenario item)
        {
            int index = _items.FindIndex(s => s.Id == item.Id);
            if (index < 0) return;
            item.Filename = _items[index].Filename;
            item.Order = _items[index].Order;
            _items[index] = item;
            Version++;
            SaveItem(item);
        }

        /// <summary>
        /// Delete a scenario's file, and only then the scenario (S0034 LS2-6). A file an antivirus
        /// scan or a sync client holds, or one marked read-only, used to be logged and dropped from
        /// the list anyway - and the next reload brought the scenario and its global chord back
        /// without a word.
        /// </summary>
        /// <returns>null when the scenario is gone (its file deleted, or already absent); the reason otherwise, and the scenario stays.</returns>
        public string? Remove(Guid id)
        {
            LauncherScenario? item = _items.Find(s => s.Id == id);
            if (item == null) return null;
            string path = Path.Combine(_folder, item.Filename);
            try { File.Delete(path); }
            catch (Exception ex)
            {
                bool gone;
                try { gone = !File.Exists(path); }
                catch { gone = false; }
                if (!gone)
                {
                    LauncherLog.Log("Store: delete failed for " + item.Filename + ": " + ex.GetType().Name);
                    return FailureCause.Describe(ex, "Русский");
                }
            }
            _items.Remove(item);
            Version++;
            return null;
        }

        /// <summary>
        /// Move a scenario one slot up (-1) or down (+1), keeping the persisted orders contiguous.
        /// Saves only the two affected files.
        /// </summary>
        public void Move(Guid id, int direction)
        {
            int index = _items.FindIndex(s => s.Id == id);
            if (index < 0) return;
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _items.Count) return;

            LauncherScenario a = _items[index];
            _items[index] = _items[newIndex];
            _items[newIndex] = a;
            _items[index].Order = index;
            _items[newIndex].Order = newIndex;
            Version++;
            SaveItem(_items[index]);
            SaveItem(_items[newIndex]);
        }

        /// <summary>
        /// Import a portable XML: always a fresh Guid and filename, appended to the end, the source
        /// file untouched. Returns null (with <paramref name="error"/>) when the file is unreadable.
        /// </summary>
        public LauncherScenario? Import(string file, out Exception? error)
        {
            LauncherScenario? item = TryRead(file, out error);
            if (item == null) return null;
            item.Id = Guid.NewGuid();
            item.Filename = string.Empty; // Add assigns {new-guid}.xml
            Add(item);
            return item;
        }

        /// <summary>Export one scenario to a portable XML of the same contract.</summary>
        public void Export(LauncherScenario item, string file)
        {
            var serializer = new XmlSerializer(typeof(LauncherScenario));
            using FileStream stream = File.Create(file);
            serializer.Serialize(stream, item);
        }

        /// <summary>
        /// The clean-first-enable sample (spec §5.2): created only when the list is empty, deletable
        /// forever - the caller guards with the one-time <c>LauncherFirstEnableDone</c> marker, so an
        /// emptied list is never refilled.
        /// </summary>
        public void SeedSample(string name)
        {
            if (_items.Count > 0) return;
            Add(new LauncherScenario { Name = name, Path = "calc.exe" });
        }

        /// <summary>
        /// Sort by stored order (name breaks the legacy all-zero ties), then reassign contiguous
        /// 0..n-1 ordinals, persisting only rows that actually changed - and every row in
        /// <paramref name="mustSave"/>, whose identity was just assigned.
        /// </summary>
        private void NormalizeOrder(ICollection<LauncherScenario> mustSave)
        {
            _items = _items
                .OrderBy(s => s.Order)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Order == i && !mustSave.Contains(_items[i])) continue;
                _items[i].Order = i;
                SaveItem(_items[i]);
            }
        }

        private void SaveItem(LauncherScenario item)
        {
            if (_readOnly) throw new InvalidOperationException("A read-only scenario store is never written.");
            string? temp = null;
            try
            {
                Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, item.Filename);
                var serializer = new XmlSerializer(typeof(LauncherScenario));
                // Write to a sibling temp file, then replace: a crash mid-write can't leave a
                // half-written scenario that the next load counts as corrupt. The replace is one
                // atomic File.Replace - a delete followed by a move lost the scenario to a crash in
                // between (S0008 LS-9) - and the temp name is this writer's own, since the live
                // instance and a one-shot /launcher-run process can save the same scenario.
                temp = TempPathFor(path);
                using (FileStream stream = File.Create(temp))
                    serializer.Serialize(stream, item);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                temp = null;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Store: save failed for " + item.Filename + ": " + ex.Message);
            }
            finally
            {
                if (temp != null)
                {
                    try { File.Delete(temp); } catch { /* removed as stale on a later load */ }
                }
            }
        }

        private static readonly Random TempNames = new Random();

        /// <summary><c>{name}.{pid}.{random}.tmp</c> beside the scenario - unique per writer and per save.</summary>
        internal static string TempPathFor(string path)
        {
            int random;
            lock (TempNames) random = TempNames.Next();
            int pid;
            using (System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess())
                pid = self.Id;
            return Path.Combine(Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + "." + pid + "." + random.ToString("x8") + ".tmp");
        }
    }
}
