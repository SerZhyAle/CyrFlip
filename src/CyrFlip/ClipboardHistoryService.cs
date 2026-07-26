using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>Captures Unicode clipboard text and stores an encrypted append-only per-user log.</summary>
    internal sealed class ClipboardHistoryService : NativeWindow, IDisposable
    {
        private const int WmClipboardUpdate = 0x031D;
        private const int MaxTextBytes = 128 * 1024;
        private readonly List<ClipboardHistoryEntry> _entries = new List<ClipboardHistoryEntry>();
        // Uuid (the text's SHA-256) → its entry. The history is unbounded by design, so nothing may
        // cost O(history): a repeat copy is recognised through this index instead of sorting or
        // scanning the list, and replaying the log on startup stays linear rather than quadratic.
        private readonly Dictionary<string, ClipboardHistoryEntry> _byUuid =
            new Dictionary<string, ClipboardHistoryEntry>(StringComparer.Ordinal);
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly string _path;
        private List<ClipboardHistoryEntry>? _ordered; // display order; rebuilt only after a change
        private bool _enabled;
        private bool _paused;
        private bool _suppressNext;
        private DateTime _suppressUntilUtc;

        public event EventHandler? Changed;
        public event EventHandler? ItemTooLarge;

        /// <summary>
        /// Pinned first, then newest first. Cached because every repaint of the strip asks for it (and
        /// the list it sorts only grows) - <see cref="RaiseChanged"/> is the one place that drops it.
        /// </summary>
        public IReadOnlyList<ClipboardHistoryEntry> Entries => _ordered ??= _entries
            .OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CreatedAt).ToList();

        public ClipboardHistoryService(bool enabled, bool paused)
        {
            _enabled = enabled;
            _paused = paused;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyrFlip");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "clipboard-history.log");
            Load();
            CreateHandle(new CreateParams { Caption = "CyrFlip Clipboard History Listener" });
            AddClipboardFormatListener(Handle);
        }

        public void SetEnabled(bool value) { _enabled = value; if (value) Capture(); }
        public void SetPaused(bool value) { _paused = value; }
        /// <summary>Ignore CyrFlip's temporary copy/paste clipboard traffic.</summary>
        public void SuppressFor(TimeSpan duration) => _suppressUntilUtc = DateTime.UtcNow.Add(duration);

        public bool Restore(ClipboardHistoryEntry entry)
        {
            _suppressNext = true;
            bool written = Win32Clipboard.TrySetText(entry.Text);
            if (written) { SetCurrent(entry); RaiseChanged(); }
            return written;
        }

        public void TogglePin(ClipboardHistoryEntry entry)
        {
            entry.IsPinned = !entry.IsPinned;
            Append(entry.IsPinned ? "pin" : "unpin", entry);
            RaiseChanged();
        }

        public void Delete(ClipboardHistoryEntry entry)
        {
            _entries.Remove(entry);
            if (_byUuid.TryGetValue(entry.Uuid, out ClipboardHistoryEntry? indexed) && ReferenceEquals(indexed, entry))
                _byUuid.Remove(entry.Uuid);
            Append("delete", entry);
            RaiseChanged();
        }

        public void Clear()
        {
            _entries.Clear();
            _byUuid.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            RaiseChanged();
        }

        /// <summary>
        /// Drop the cached display order and tell the windows to repaint. Every change goes through
        /// here exactly once: the strip repaints on this event and each repaint re-sorts the history,
        /// so a second, redundant raise doubles the cost of every copy.
        /// </summary>
        private void RaiseChanged()
        {
            _ordered = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmClipboardUpdate) Capture();
            base.WndProc(ref m);
        }

        private void Capture()
        {
            if (!_enabled || _paused || DateTime.UtcNow < _suppressUntilUtc) return;
            if (_suppressNext) { _suppressNext = false; return; }
            if (!Win32Clipboard.TryGetText(out string text) || text.Length == 0) return;
            if (Encoding.Unicode.GetByteCount(text) > MaxTextBytes) { ItemTooLarge?.Invoke(this, EventArgs.Empty); return; }
            string uuid = Hash(text);
            // Identical text is one entry, however long ago it was first copied - the same rule the
            // log replay in Load has always applied, now also applied live and in O(1).
            if (_byUuid.TryGetValue(uuid, out ClipboardHistoryEntry? existing))
            {
                existing.CreatedAt = DateTime.UtcNow;
                Append("touch", existing);
            }
            else
            {
                ReadSource(out string sourceApp, out string sourceTitle);
                existing = new ClipboardHistoryEntry { Uuid = uuid, Text = text, CreatedAt = DateTime.UtcNow, SourceApp = sourceApp, SourceTitle = sourceTitle };
                _entries.Add(existing);
                _byUuid[uuid] = existing;
                Append("add", existing);
            }
            SetCurrent(existing);
            RaiseChanged();
        }

        /// <summary>Mark one entry as the clipboard's current content. Raises nothing on its own:
        /// it never changes the display order, and every caller raises the change itself.</summary>
        private void SetCurrent(ClipboardHistoryEntry entry)
        {
            foreach (ClipboardHistoryEntry item in _entries) item.IsCurrent = ReferenceEquals(item, entry);
        }

        /// <summary>The window owning the clipboard when it changed is, in practice, the app the text came from.</summary>
        private static void ReadSource(out string app, out string title)
        {
            app = ""; title = "";
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                var caption = new StringBuilder(256);
                if (GetWindowText(hwnd, caption, caption.Capacity) > 0) title = caption.ToString();
                if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return;
                IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return;
                try
                {
                    var buffer = new StringBuilder(1024);
                    uint size = (uint)buffer.Capacity;
                    if (!QueryFullProcessImageName(h, 0, buffer, ref size)) return;
                    string path = buffer.ToString();
                    int slash = path.LastIndexOf('\\');
                    string file = slash >= 0 ? path.Substring(slash + 1) : path;
                    int dot = file.LastIndexOf('.');
                    app = dot >= 0 ? file.Substring(0, dot) : file;
                }
                finally { CloseHandle(h); }
            }
            catch { /* source metadata is best-effort; never let it affect the clipboard */ }
        }

        private void Append(string action, ClipboardHistoryEntry entry)
        {
            try
            {
                var record = new HistoryRecord { Action = action, Uuid = entry.Uuid, CreatedAt = entry.CreatedAt.Ticks, IsPinned = entry.IsPinned };
                if (action == "add")
                {
                    record.Payload = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Text), null, DataProtectionScope.CurrentUser));
                    record.SourceApp = entry.SourceApp;
                    record.SourceTitle = entry.SourceTitle;
                }
                File.AppendAllText(_path, _json.Serialize(record) + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* history must never affect the clipboard */ }
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                foreach (string line in File.ReadLines(_path))
                {
                    HistoryRecord? r = _json.Deserialize<HistoryRecord>(line);
                    if (r == null || string.IsNullOrEmpty(r.Uuid)) continue;
                    _byUuid.TryGetValue(r.Uuid, out ClipboardHistoryEntry? e);
                    if (r.Action == "delete") { if (e != null) { _entries.Remove(e); _byUuid.Remove(r.Uuid); } continue; }
                    if (r.Action == "add")
                    {
                        if (e != null || string.IsNullOrEmpty(r.Payload)) continue;
                        string text = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(r.Payload), null, DataProtectionScope.CurrentUser));
                        var entry = new ClipboardHistoryEntry { Uuid = r.Uuid, Text = text, CreatedAt = new DateTime(r.CreatedAt, DateTimeKind.Utc), IsPinned = r.IsPinned, SourceApp = r.SourceApp ?? "", SourceTitle = r.SourceTitle ?? "" };
                        _entries.Add(entry);
                        _byUuid[r.Uuid] = entry;
                    }
                    else if (e != null) { e.CreatedAt = new DateTime(r.CreatedAt, DateTimeKind.Utc); e.IsPinned = r.IsPinned; }
                }
            }
            catch { _entries.Clear(); _byUuid.Clear(); }
        }

        private static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }

        private sealed class HistoryRecord { public string Action { get; set; } = ""; public string Uuid { get; set; } = ""; public long CreatedAt { get; set; } public bool IsPinned { get; set; } public string? Payload { get; set; } public string? SourceApp { get; set; } public string? SourceTitle { get; set; } }
    }
}
