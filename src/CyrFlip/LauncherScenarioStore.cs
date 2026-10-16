using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        // Read the actual files so comments, whitespace and extension elements travel unchanged.
        internal string[] ReadDocuments() => All.Select(s => File.ReadAllText(Path.Combine(_folder, s.Filename))).ToArray();

        public LauncherScenario? Find(Guid id) => _items.Find(s => s.Id == id);

        /// <summary>Re-read every XML in the folder. Corrupt files land in <see cref="LoadErrors"/>.</summary>
        internal Func<string, string[]> ListScenarioFiles = folder => Directory.GetFiles(folder, "*.xml");

        public void Reload()
        {
            Version++;
            LoadErrors.Clear();
            if (!Directory.Exists(_folder))
            {
                _items.Clear();
                return;
            }

            if (!_readOnly) DeleteStaleTempFiles();

            // Sorted, so "which of two same-Id files keeps the Id" is the same answer on every load.
            string[] files;
            try { files = ListScenarioFiles(_folder); }
            catch (Exception ex)
            {
                LoadErrors.Add(Path.GetFileName(_folder));
                LauncherLog.Log("Store: listing failed: " + ex.GetType().Name);
                return;
            }
            _items.Clear();
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
            try { return TryReadXml(File.ReadAllText(file), out error); }
            catch (Exception ex) { error = ex; return null; }
        }

        /// <summary>
        /// The <see cref="TryRead"/> validator over a string - the settings file v2 carries scenario
        /// documents verbatim (S0072 5.3), and the import judges each with the same one parser. What is
        /// refused is refused whole: a document this build does not understand never runs its
        /// <c>Path</c> (<c>SCENARIO-FILE</c> rule 10).
        /// </summary>
        public static LauncherScenario? TryReadXml(string xml, out Exception? error)
        {
            error = null;
            try
            {
                var serializer = new XmlSerializer(typeof(LauncherScenario));
                using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { CheckCharacters = false });
                reader.MoveToContent();
                CheckRoot(reader);
                var item = (LauncherScenario?)serializer.Deserialize(reader);
                if (item != null && item.Path == LauncherScenario.LegacyYtDlpSentinel
                    && item.Type == LauncherScenarioType.Executable)
                    item.Type = LauncherScenarioType.YtDlp;
                if (item != null)
                {
                    item.TransferXml = xml;
                    item.TransferSignature = TransferSignature(item);
                }
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
        public Exception? Add(LauncherScenario item)
        {
            if (string.IsNullOrEmpty(item.Filename))
                item.Filename = item.Id + ".xml";
            item.Order = _items.Count == 0 ? 0 : _items.Max(s => s.Order) + 1;
            Exception? failure = SaveItem(item);
            if (failure != null) return failure;
            _items.Add(item);
            Version++;
            return null;
        }

        public Exception? Update(LauncherScenario item)
        {
            int index = _items.FindIndex(s => s.Id == item.Id);
            if (index < 0) return new InvalidOperationException("Scenario not found.");
            item.Filename = _items[index].Filename;
            item.Order = _items[index].Order;
            Exception? failure = SaveItem(item);
            if (failure != null) return failure;
            _items[index] = item;
            Version++;
            return null;
        }

        /// <summary>
        /// Delete a scenario's file, and only then the scenario (S0034 LS2-6). A file an antivirus
        /// scan or a sync client holds, or one marked read-only, used to be logged and dropped from
        /// the list anyway - and the next reload brought the scenario and its global chord back
        /// without a word.
        /// </summary>
        /// <returns>null when the scenario is gone (its file deleted, or already absent); the reason otherwise, and the scenario stays.</returns>
        /// <param name="translate">
        /// The caller's translating function (S0065 E-5): the reason used to be the Russian key in
        /// every UI language, so an English user read "Could not delete: Не удалось запустить процесс."
        /// </param>
        public string? Remove(Guid id, Func<string, string>? translate = null)
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
                    return FailureCause.Describe(ex, translate);
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
        public Exception? Move(Guid id, int direction)
        {
            int index = _items.FindIndex(s => s.Id == id);
            int next = index + direction;
            if (index < 0 || next < 0 || next >= _items.Count) return null;
            LauncherScenario a = _items[index].Clone(), b = _items[next].Clone();
            int aOrder = a.Order, bOrder = b.Order;
            a.Order = bOrder;
            b.Order = aOrder;
            Exception? failure = SaveItem(a);
            if (failure != null) return failure;
            failure = SaveItem(b);
            if (failure != null)
            {
                a.Order = aOrder;
                Exception? rollback = SaveItem(a);
                return rollback == null ? failure : new AggregateException(failure, rollback);
            }
            _items[index] = b;
            _items[next] = a;
            Version++;
            return null;
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
            error = Add(item);
            return error == null ? item : null;
        }

        /// <summary>Export one scenario to a portable XML of the same contract.</summary>
        public void Export(LauncherScenario item, string file)
        {
            using FileStream stream = File.Create(file);
            SerializeScenario(stream, item);
        }

        /// <summary>The verbatim document the settings file v2 carries in its <c>scenarios</c> section (S0072 5.3).</summary>
        public static string ToXml(LauncherScenario item)
        {
            using var stream = new MemoryStream();
            SerializeScenario(stream, item);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Import-replace of the whole set (S0072 5.3, the tables' I6 parity): scenarios whose id the
        /// set no longer holds are deleted, the rest are written (a scenario without its own file yet
        /// gets <c>{id}.xml</c>), colliding ids are regenerated first. The caller must have made the
        /// scenario snapshot first - this deletes files, and I1 puts them back on rollback.
        /// </summary>
        public Exception? ReplaceSet(IEnumerable<LauncherScenario> items)
        {
            if (_readOnly) throw new InvalidOperationException("A read-only scenario store is never written.");
            var incoming = items.Select(s => s.Clone()).ToList();
            var seen = new HashSet<Guid>();
            foreach (LauncherScenario scenario in incoming)
            { while (scenario.Id == Guid.Empty || !seen.Add(scenario.Id)) { scenario.Id = Guid.NewGuid(); scenario.Filename = string.Empty; } }
            // Array order is the export order (the store's order); make it the stored order.
            // Filename in a document is untrusted. Always choose a local name from its validated Id.
            for (int i = 0; i < incoming.Count; i++) { incoming[i].Order = i; incoming[i].Filename = incoming[i].Id + ".xml"; }
            var keepFiles = new HashSet<string>(incoming.Select(s => s.Filename), StringComparer.OrdinalIgnoreCase);
            try
            {
                Directory.CreateDirectory(_folder);
                foreach (LauncherScenario existing in _items)
                {
                    if (keepFiles.Contains(existing.Filename)) continue;
                    string path = Path.Combine(_folder, existing.Filename);
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch (Exception ex) { LauncherLog.Log("Store: replace-set delete failed for " + existing.Filename + ": " + ex.GetType().Name); return ex; }
                }
                // A file named for a scenario that is gone but was never deleted above (already absent)
                // stays orphaned only if no incoming scenario reuses its name. Rewriting a document the
                // file already holds is harmless: SaveItem replaces atomically.
                foreach (LauncherScenario scenario in incoming)
                {
                    Exception? failure = SaveItem(scenario);
                    if (failure != null) return failure;
                }
            }
            catch (Exception ex) { return ex; }
            Reload();
            return null;
        }

        /// <summary>
        /// The clean-first-enable sample (spec §5.2): created only when the list is empty, deletable
        /// forever - the caller guards with the one-time <c>LauncherFirstEnableDone</c> marker, so an
        /// emptied list is never refilled.
        /// </summary>
        public Exception? SeedSample(string name)
        {
            if (_items.Count > 0) return null;
            return Add(new LauncherScenario { Name = name, Path = "calc.exe" });
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
                if (SaveItem(_items[i]) != null) LoadErrors.Add(_items[i].Filename);
            }
        }

        private Exception? SaveItem(LauncherScenario item)
        {
            if (_readOnly) throw new InvalidOperationException("A read-only scenario store is never written.");
            string? temp = null;
            try
            {
                Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, item.Filename);
                // Write to a sibling temp file, then replace: a crash mid-write can't leave a
                // half-written scenario that the next load counts as corrupt. The replace is one
                // atomic File.Replace - a delete followed by a move lost the scenario to a crash in
                // between (S0008 LS-9) - and the temp name is this writer's own, since the live
                // instance and a one-shot /launcher-run process can save the same scenario.
                temp = TempPathFor(path);
                using (FileStream stream = File.Create(temp))
                {
                    if (item.TransferXml != null && item.TransferSignature == TransferSignature(item))
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(item.TransferXml);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    else SerializeScenario(stream, item);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                temp = null;
                return null;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Store: save failed for " + item.Filename + ": " + ex.GetType().Name);
                return ex;
            }
            finally
            {
                if (temp != null)
                {
                    try { File.Delete(temp); } catch { /* removed as stale on a later load */ }
                }
            }
        }

        private static string TransferSignature(LauncherScenario item)
        {
            var copy = item.Clone();
            copy.Filename = ""; // destination filenames are local bookkeeping
            return ToXml(copy);
        }

        private static void SerializeScenario(Stream stream, LauncherScenario item)
        {
            // Keep the established writer shape, including its declaration and namespaces.
            using var writer = new ScenarioXmlWriter(stream) { Formatting = Formatting.Indented };
            new XmlSerializer(typeof(LauncherScenario)).Serialize(writer, item);
        }

        private sealed class ScenarioXmlWriter : XmlTextWriter
        {
            internal ScenarioXmlWriter(Stream stream) : base(stream, null) { }
            public override void WriteString(string? text)
            {
                // XML normalizes literal CR/CRLF on read. Entity references preserve the user's
                // original string, while XmlTextWriter also entitizes control characters.
                if (text == null) { base.WriteString(text); return; }
                int start = 0;
                for (int at = 0; at < text.Length; at++)
                    if (text[at] == '\r')
                    {
                        base.WriteString(text.Substring(start, at - start));
                        WriteCharEntity('\r');
                        start = at + 1;
                    }
                base.WriteString(text.Substring(start));
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
