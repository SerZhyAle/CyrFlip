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
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly string _path;
        private bool _enabled;
        private bool _paused;
        private bool _suppressNext;
        private DateTime _suppressUntilUtc;

        public event EventHandler? Changed;
        public event EventHandler? ItemTooLarge;
        public IReadOnlyList<ClipboardHistoryEntry> Entries => _entries
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
            if (written) SetCurrent(entry);
            return written;
        }

        public void TogglePin(ClipboardHistoryEntry entry)
        {
            entry.IsPinned = !entry.IsPinned;
            Append(entry.IsPinned ? "pin" : "unpin", entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(ClipboardHistoryEntry entry)
        {
            _entries.Remove(entry);
            Append("delete", entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _entries.Clear();
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
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
            ClipboardHistoryEntry? existing = _entries.OrderByDescending(x => x.CreatedAt).Take(1000).FirstOrDefault(x => x.Uuid == uuid);
            if (existing != null)
            {
                existing.CreatedAt = DateTime.UtcNow;
                Append("touch", existing);
            }
            else
            {
                existing = new ClipboardHistoryEntry { Uuid = uuid, Text = text, CreatedAt = DateTime.UtcNow };
                _entries.Add(existing);
                Append("add", existing);
            }
            SetCurrent(existing);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetCurrent(ClipboardHistoryEntry entry)
        {
            foreach (ClipboardHistoryEntry item in _entries) item.IsCurrent = ReferenceEquals(item, entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Append(string action, ClipboardHistoryEntry entry)
        {
            try
            {
                var record = new HistoryRecord { Action = action, Uuid = entry.Uuid, CreatedAt = entry.CreatedAt.Ticks, IsPinned = entry.IsPinned };
                if (action == "add") record.Payload = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Text), null, DataProtectionScope.CurrentUser));
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
                    ClipboardHistoryEntry? e = _entries.FirstOrDefault(x => x.Uuid == r.Uuid);
                    if (r.Action == "delete") { if (e != null) _entries.Remove(e); continue; }
                    if (r.Action == "add")
                    {
                        if (e != null || string.IsNullOrEmpty(r.Payload)) continue;
                        string text = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(r.Payload), null, DataProtectionScope.CurrentUser));
                        _entries.Add(new ClipboardHistoryEntry { Uuid = r.Uuid, Text = text, CreatedAt = new DateTime(r.CreatedAt, DateTimeKind.Utc), IsPinned = r.IsPinned });
                    }
                    else if (e != null) { e.CreatedAt = new DateTime(r.CreatedAt, DateTimeKind.Utc); e.IsPinned = r.IsPinned; }
                }
            }
            catch { _entries.Clear(); }
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

        private sealed class HistoryRecord { public string Action { get; set; } = ""; public string Uuid { get; set; } = ""; public long CreatedAt { get; set; } public bool IsPinned { get; set; } public string? Payload { get; set; } }
    }
}
