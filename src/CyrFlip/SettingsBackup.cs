using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The rolling settings backup (S0072 section 4): the full settings file v2 - targets and scenario
    /// documents included - into <c>%USERPROFILE%\Documents\CyrFlip\Backups</c>, the one silent-write
    /// location a packaged full-trust process has that Windows never redirects, so it survives a Store
    /// uninstall. Debounced: one write per quiet minute after the last <see cref="AppConfig.Saved"/>,
    /// a flush on a clean exit and on session end, keep-last-3 pruned like the import snapshots.
    ///
    /// <para>The writer answers one gate before every write: the restore question must be settled and
    /// the switch on. While the question is open the app writes no backup at all - a defaults-only
    /// profile would otherwise evict the pre-uninstall backup, the one file this whole mechanism
    /// exists to keep, inside its first quiet minute (the seeding save of a fresh profile happens
    /// before this object even starts, so it never produces a backup either).</para>
    /// </summary>
    internal sealed class SettingsBackup
    {
        private readonly AppConfig _config;
        private readonly Func<IReadOnlyList<string>> _scenarios;
        private readonly Timer _idle = new Timer();
        private bool _dirty;
        private bool _started;
        internal event Action? Written;

        /// <summary>Test seam: the real folder is Documents\CyrFlip\Backups.</summary>
        internal Func<string>? FolderOverride;

        /// <summary>Test seam: the real idle window is one quiet minute.</summary>
        internal int IdleMilliseconds = 60_000;

        public SettingsBackup(AppConfig config, Func<IReadOnlyList<string>> scenarios)
        {
            _config = config;
            _scenarios = scenarios;
            _idle.Tick += (_, _) => OnIdle();
        }

        internal string Folder => FolderOverride?.Invoke() ?? DefaultFolder;

        /// <summary>The production backup folder - the General page shows it and opens it.</summary>
        internal static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CyrFlip", "Backups");

        /// <summary>The newest backup in the production folder, for the General page's state row.</summary>
        internal static DateTime? NewestBackupTime()
        {
            try
            {
                return Directory.GetFiles(DefaultFolder, "CyrFlip-settings-*.json")
                    .Select(File.GetLastWriteTime)
                    .OrderByDescending(t => t)
                    .FirstOrDefault() is { } time && time != default ? time : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// True when a backup may be written now: the switch is on and the restore question is
        /// settled - answered, declined, or never asked because no backup existed to offer (S0072 4).
        /// </summary>
        internal bool QuestionSettled { get; set; }
        internal bool MayWrite => _config.SettingsBackupEnabled && !_config.BackupOfferOpen && QuestionSettled;

        public void Start()
        {
            if (_started) return;
            _started = true;
            _idle.Interval = IdleMilliseconds;
            AppConfig.Saved += OnSaved;
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            AppConfig.Saved -= OnSaved;
            _idle.Stop();
            _idle.Dispose();
        }

        /// <summary>A settings change: dirty, and the quiet-minute timer restarts - one write per quiet window.</summary>
        private void OnSaved()
        {
            if (!MayWrite) return;
            _dirty = true;
            _idle.Stop();
            _idle.Interval = IdleMilliseconds;
            _idle.Start();
        }

        /// <summary>Test seams for the debounce pair; production goes through <see cref="AppConfig.Saved"/> and the timer.</summary>
        internal void SavedForTest() => OnSaved();
        internal void NotifyChanged() => OnSaved();
        internal void IdleElapsedForTest() => OnIdle();

        private void OnIdle()
        {
            _idle.Stop();
            if (!_dirty || !MayWrite) return;
            _dirty = false;
            WriteNow();
        }

        /// <summary>The clean-exit and session-end flush: an explicit write past the debounce, gated the same way.</summary>
        public void Flush()
        {
            _idle.Stop();
            _dirty = false;
            if (MayWrite) WriteNow();
        }

        /// <summary>The newest backup file, newest first over the writer's own naming.</summary>
        private IEnumerable<string> Files() => Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "CyrFlip-settings-*.json")
                .Where(path => Path.GetFileName(path).StartsWith("CyrFlip-settings-", StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        internal string? NewestReadable()
        {
            foreach (string file in Files())
                try { SettingsFileReader.ReadFile(file); return file; }
                catch { /* an unreadable file is not the newest thing we can offer */ }
            return null;
        }

        /// <summary>One backup file: the full v2 export by the transfer writer, byte for byte.</summary>
        internal void WriteNow()
        {
            if (!MayWrite) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, SettingsFileWriter.DefaultFileName(DateTime.Now));
                SettingsFileWriter.Save(file, _config, _scenarios());
                Prune();
                Written?.Invoke();
            }
            catch (Exception ex)
            {
                // Reason codes only, never contents: the backup names the user's infrastructure.
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup failed " + ex.GetType().Name);
            }
        }

        internal void Prune()
        {
            foreach (string path in Files().Skip(3))
                File.Delete(path);
        }
    }

    /// <summary>The restore-offer decision, pure for the gate tests (S0072 G5): the context's
    /// <c>OfferBackupRestore</c> asks exactly when this says Ask, and settles the writer otherwise.</summary>
    internal enum BackupOfferDecision
    {
        /// <summary>Not a fresh profile, or the question was answered before: never asked.</summary>
        NeverAsked,
        /// <summary>Fresh with a readable backup: ask.</summary>
        Ask,
        /// <summary>Fresh, no backup to offer: the writer starts silently.</summary>
        Settle,
    }
    internal static class BackupOffer
    {
        public static BackupOfferDecision Decide(bool freshAtLoad, bool questionOpen, bool declined, bool readableBackup)
        {
            if (declined) return BackupOfferDecision.NeverAsked;
            return freshAtLoad || questionOpen
                ? readableBackup ? BackupOfferDecision.Ask : BackupOfferDecision.Settle
                : BackupOfferDecision.NeverAsked;
        }
    }
}
