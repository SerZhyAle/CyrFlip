using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace CyrFlip
{
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

        public LauncherScenarioStore(string? folder = null)
        {
            _folder = folder ?? DefaultFolder;
            Reload();
        }

        /// <summary>Ordered snapshot of the current list (callers cannot corrupt the store's order).</summary>
        public List<LauncherScenario> All => new List<LauncherScenario>(_items);

        public int Count => _items.Count;

        public LauncherScenario? Find(Guid id) => _items.Find(s => s.Id == id);

        /// <summary>Re-read every XML in the folder. Corrupt files land in <see cref="LoadErrors"/>.</summary>
        public void Reload()
        {
            _items.Clear();
            LoadErrors.Clear();
            if (!Directory.Exists(_folder))
                return;

            foreach (string file in Directory.GetFiles(_folder, "*.xml"))
            {
                LauncherScenario? item = TryRead(file, out _);
                if (item == null)
                {
                    LoadErrors.Add(Path.GetFileName(file));
                    LauncherLog.Log("Store: failed to read " + Path.GetFileName(file));
                    continue;
                }
                item.Filename = Path.GetFileName(file);
                _items.Add(item);
            }
            NormalizeOrder();
        }

        /// <summary>
        /// Parse one scenario XML; null when unreadable. The legacy magic path is surfaced as the
        /// yt-dlp type so the editor reflects reality (the execution path handles the sentinel too).
        /// </summary>
        public static LauncherScenario? TryRead(string file, out Exception? error)
        {
            error = null;
            try
            {
                var serializer = new XmlSerializer(typeof(LauncherScenario));
                using FileStream stream = File.OpenRead(file);
                var item = (LauncherScenario?)serializer.Deserialize(stream);
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

        /// <summary>Append a scenario: fresh filename when empty, order = end of list, then persist.</summary>
        public void Add(LauncherScenario item)
        {
            if (string.IsNullOrEmpty(item.Filename))
                item.Filename = item.Id + ".xml";
            item.Order = _items.Count == 0 ? 0 : _items.Max(s => s.Order) + 1;
            _items.Add(item);
            SaveItem(item);
        }

        public void Update(LauncherScenario item)
        {
            int index = _items.FindIndex(s => s.Id == item.Id);
            if (index < 0) return;
            item.Filename = _items[index].Filename;
            item.Order = _items[index].Order;
            _items[index] = item;
            SaveItem(item);
        }

        public void Remove(Guid id)
        {
            LauncherScenario? item = _items.Find(s => s.Id == id);
            if (item == null) return;
            try { File.Delete(Path.Combine(_folder, item.Filename)); }
            catch (Exception ex) { LauncherLog.Log("Store: delete failed for " + item.Filename + ": " + ex.Message); }
            _items.Remove(item);
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
        /// 0..n-1 ordinals, persisting only rows that actually changed.
        /// </summary>
        private void NormalizeOrder()
        {
            _items = _items
                .OrderBy(s => s.Order)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Order == i) continue;
                _items[i].Order = i;
                SaveItem(_items[i]);
            }
        }

        private void SaveItem(LauncherScenario item)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, item.Filename);
                var serializer = new XmlSerializer(typeof(LauncherScenario));
                // Write to a sibling temp file, then replace: a crash mid-write can't leave a
                // half-written scenario that the next load counts as corrupt.
                string temp = path + ".tmp";
                using (FileStream stream = File.Create(temp))
                    serializer.Serialize(stream, item);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Store: save failed for " + item.Filename + ": " + ex.Message);
            }
        }
    }
}
