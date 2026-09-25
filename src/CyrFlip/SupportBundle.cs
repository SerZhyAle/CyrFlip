using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// Packs CyrFlip's own diagnostic logs into one ZIP the user can mail to the author
    /// (Settings ▸ About ▸ "Send logs to the author.."). Nothing here sends anything: this class
    /// only produces a file on disk, <see cref="MailSender"/> hands it to the user's mail client,
    /// and the user presses Send.
    ///
    /// Two rules are load-bearing:
    ///
    /// 1. <b><see cref="ExcludedFiles"/> (clipboard-history.log, quick-notes.log) never go in.</b> They are literally
    ///    everything the user ever copied - passwords, messages, card numbers. It is DPAPI-protected
    ///    and unreadable off this account anyway, but that is not the reason: it is not our data.
    ///    The file list is therefore an explicit whitelist (<see cref="LogFiles"/>), never a
    ///    directory glob - a glob is exactly how that file would get in one day.
    ///    What the whitelisted files may hold is itself a rule (ticket S0010 TD-1): no scenario
    ///    command line and no window title - <see cref="Contents"/> is what the dialog tells the
    ///    user about each file, and it has to stay true.
    /// 2. <b>Nothing is truncated silently.</b> A file cut to its tail carries a marker line and the
    ///    report lists whatever had to be dropped, because a quietly shortened archive reads as
    ///    "the author got everything".
    ///
    /// The file work is deliberately separable from the live machine: <see cref="Create"/> takes the
    /// directories and the report text as arguments, so the tests run in a temp folder and never
    /// touch the real logs.
    /// </summary>
    internal static class SupportBundle
    {
        /// <summary>Per-file cap; the <b>tail</b> is kept, since the last session is the interesting one.</summary>
        public const int MaxFileBytes = 512 * 1024;

        /// <summary>
        /// Cap on the <b>collected (uncompressed)</b> bytes. Counting compressed bytes would mean
        /// packing, measuring and repacking; text compresses about tenfold, so this leaves the
        /// archive itself far below any mail provider's attachment limit.
        /// </summary>
        public const long MaxTotalBytes = 3L * 1024 * 1024;

        /// <summary>How many archives survive in the reports folder; a bundle is a derived artefact.</summary>
        public const int KeepArchives = 5;

        /// <summary>The generated report - never dropped, and the first thing the author reads.</summary>
        public const string ReportName = "report.txt";

        /// <summary>
        /// The whitelist, in the order a file is dropped when the total budget runs out: last entry
        /// goes first. <c>layout.txt</c> is last on purpose - it is one line and it is the contract
        /// with the VS Code extension.
        /// </summary>
        public static readonly string[] LogFiles =
        {
            "launcher.log", "context-menu.log", "translate.log", "quick-notes-diagnostics.log",
            "clipboard-history-diagnostics.log", "clipboard-flip.log", "caret-diagnostics.txt", "layout.txt",
        };

        /// <summary>
        /// The files that are never collected, whatever else is. Both hold the user's own text:
        /// <c>clipboard-history.log</c> is everything they ever copied, <c>quick-notes.log</c> is
        /// everything they deliberately kept. The two <i>diagnostics</i> files are in the whitelist
        /// above and hold no user text - only counts (records that failed to replay, marked copies skipped).
        /// </summary>
        public static readonly string[] ExcludedFiles =
        {
            "clipboard-history.log", "quick-notes.log",
        };

        private const string LauncherLogName = "launcher.log";

        /// <summary>
        /// What each collected file holds, as the pre-send dialog tells the user - one short line per
        /// file, a Russian source key for <see cref="Localization"/>. Every whitelisted file has an
        /// entry (<c>SupportBundleTests</c>), since a file the dialog cannot describe is a file the
        /// user is asked to send blind.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Contents = new Dictionary<string, string>
        {
            [ReportName] = "версия, Windows, раскладки, включённые функции и настройки CyrFlip",
            ["launcher.log"] = "запуски сценариев: имя, тип, результат - без путей и аргументов",
            ["context-menu.log"] = "контекстное меню: команды, классы окон и числа - без текста",
            ["translate.log"] = "переводы: языки, модель, длины и исход - без текста",
            ["quick-notes-diagnostics.log"] = "быстрые заметки: только счётчики",
            ["clipboard-history-diagnostics.log"] = "история буфера: только счётчики",
            ["clipboard-flip.log"] = "конвертации выделения: длины и имена процессов - без текста",
            ["caret-diagnostics.txt"] = "диагностика каретки: классы окон и процессы - заголовки только длиной",
            ["layout.txt"] = "код текущей раскладки",
        };

        /// <summary>
        /// Old <c>launcher.log</c> lines carry what the current code never writes (ticket S0010 TD-1):
        /// <c>Launched '&lt;name&gt;' (pid=.., admin=..): &lt;FileName&gt; &lt;Arguments&gt;</c>, and
        /// <c>Launch blocked/error for '&lt;name&gt;': &lt;message&gt;</c>, whose message could quote
        /// the path. Each is cut after its <c>": "</c> when the file is collected. The current shapes
        /// (<c>for 'name' [guid] (..)</c>) do not match: their quote is not followed by a colon.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex[] LegacyLauncherLines =
        {
            new System.Text.RegularExpressions.Regex(@"^(.*? - Launched '.*?' \(pid=[^)]*\)): .*$"),
            new System.Text.RegularExpressions.Regex(@"^(.*? - Launch (?:blocked|error) for '.*?'): .*$"),
        };

        internal const string RemovedMarker = "<removed from the log bundle>";

        /// <summary>
        /// <paramref name="bytes"/> with every legacy line cut (see <see cref="LegacyLauncherLines"/>);
        /// line breaks - CRLF or LF - are kept as they were.
        /// </summary>
        internal static byte[] ScrubLauncherLog(byte[] bytes)
        {
            string text = Encoding.UTF8.GetString(bytes);
            string[] lines = text.Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool cr = line.EndsWith("\r", StringComparison.Ordinal);
                string body = cr ? line.Substring(0, line.Length - 1) : line;
                foreach (var pattern in LegacyLauncherLines)
                {
                    var match = pattern.Match(body);
                    if (!match.Success) continue;
                    lines[i] = match.Groups[1].Value + ": " + RemovedMarker + (cr ? "\r" : "");
                    changed = true;
                    break;
                }
            }
            return changed ? Encoding.UTF8.GetBytes(string.Join("\n", lines)) : bytes;
        }

        /// <summary>One file inside the archive, as the pre-send dialog lists it.</summary>
        internal sealed class Entry
        {
            public string Name = "";
            public long Bytes;                 // what actually went into the archive
            public bool Truncated;
            public long OmittedBytes;          // 0 unless Truncated
        }

        internal sealed class Result
        {
            public string ArchivePath = "";
            public long ArchiveBytes;
            public List<Entry> Entries = new List<Entry>();
            /// <summary>Files that existed but did not fit the total budget - never silently forgotten.</summary>
            public List<string> Dropped = new List<string>();
        }

        /// <summary>
        /// The folder every CyrFlip log already lives in (<see cref="DataFolder.Current"/>). No second
        /// application folder for this feature.
        /// </summary>
        public static string LogDirectory => DataFolder.Current;

        /// <summary>
        /// Where the archives go. Under MSIX this has to be a path a foreign process can open: the mail
        /// client would find nothing behind a virtualized one. <see cref="DataFolder"/> addresses the
        /// package's per-user folder by its real path for exactly that reason - and, unlike the old
        /// machine-wide %ProgramData%, no other account can read or pre-plant the archive (ticket S0016).
        /// </summary>
        public static string ReportsDirectory => Path.Combine(LogDirectory, "reports");

        /// <summary>The whole job for the live app: build the report, collect the logs, write the ZIP.</summary>
        public static Result CreateDefault(AppConfig config, DateTime stamp)
        {
            string version = AppVersion();
            return Create(LogDirectory, ReportsDirectory, BuildReport(config, version, stamp), version, stamp);
        }

        /// <summary>
        /// Collect, truncate, pack. Pure with respect to the machine - everything it reads comes from
        /// <paramref name="logDir"/> and everything it writes lands in <paramref name="reportsDir"/>.
        /// </summary>
        public static Result Create(string logDir, string reportsDir, string reportText, string version,
            DateTime stamp, int maxFileBytes = MaxFileBytes, long maxTotalBytes = MaxTotalBytes,
            int keepArchives = KeepArchives)
        {
            Directory.CreateDirectory(reportsDir);
            var result = new Result
            {
                ArchivePath = Path.Combine(reportsDir, ArchiveName(version, stamp)),
            };

            byte[] report = Encoding.UTF8.GetBytes(reportText);
            var payloads = new List<KeyValuePair<Entry, byte[]>>();
            long total = report.Length;

            foreach (string name in LogFiles)
            {
                string path = Path.Combine(logDir, name);
                byte[]? bytes = ReadTail(path, maxFileBytes, out long omitted);
                if (bytes == null) continue;                       // absent file is not an error
                if (name == LauncherLogName) bytes = ScrubLauncherLog(bytes);
                if (total + bytes.Length > maxTotalBytes)
                {
                    result.Dropped.Add(name);
                    continue;
                }
                total += bytes.Length;
                payloads.Add(new KeyValuePair<Entry, byte[]>(
                    new Entry { Name = name, Bytes = bytes.Length, Truncated = omitted > 0, OmittedBytes = omitted },
                    bytes));
            }

            // The report is written last so it can name what was dropped, but listed first so the
            // author opens it first.
            if (result.Dropped.Count > 0)
            {
                report = Encoding.UTF8.GetBytes(reportText + Environment.NewLine
                    + "Dropped (total size limit): " + string.Join(", ", result.Dropped) + Environment.NewLine);
            }
            result.Entries.Add(new Entry { Name = ReportName, Bytes = report.Length });
            foreach (var payload in payloads) result.Entries.Add(payload.Key);

            using (var file = new FileStream(result.ArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(zip, ReportName, report);
                foreach (var payload in payloads) Write(zip, payload.Key.Name, payload.Value);
            }

            result.ArchiveBytes = new FileInfo(result.ArchivePath).Length;
            Prune(reportsDir, keepArchives);
            return result;
        }

        /// <summary>"CyrFlip-logs-26.7.29.2340-20260729-2340.zip" - the version is visible in the name.</summary>
        public static string ArchiveName(string version, DateTime stamp) =>
            "CyrFlip-logs-" + version + "-" + stamp.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip";

        /// <summary>
        /// What the author needs first and no log holds: version, OS, layouts, which features are on,
        /// the usage counters and the settings themselves. The registry block goes in whole - it is
        /// the state that explains the behaviour - which is why the dialog says outright that paths
        /// (and with them the Windows account name) can appear inside, and lets the user open the
        /// archive before any message exists.
        /// </summary>
        public static string BuildReport(AppConfig config, string version, DateTime stamp)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CyrFlip " + version + " diagnostic report");
            sb.AppendLine("Created:      " + stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Packaged:     " + PackageInfo.IsPackaged + " (MSIX/Store build)");
            sb.AppendLine("OS:           " + SafeText(() => Environment.OSVersion.VersionString)
                + "  64-bit process=" + Environment.Is64BitProcess + "  64-bit OS=" + Environment.Is64BitOperatingSystem);
            sb.AppendLine("Culture:      UI=" + SafeText(() => CultureInfo.CurrentUICulture.Name)
                + "  installed=" + SafeText(() => CultureInfo.InstalledUICulture.Name)
                + "  CyrFlip UI language=" + config.UiLanguage);
            sb.AppendLine("Layouts:      " + SafeText(DescribeLayouts));
            sb.AppendLine("Features:     caret overlay=" + On(config.EnableCaretOverlay)
                + ", dot mode=" + On(config.CaretDotMode)
                + ", cursor=" + On(config.EnableCursorChange)
                + ", hotkeys=" + On(config.EnableHotkeys)
                + ", case=" + On(config.EnableCaseHotkey)
                + ", history=" + On(config.EnableClipboardHistory)
                + ", launcher=" + On(config.EnableScenarioLauncher)
                + ", translate=" + On(config.EnableTranslate)
                + ", context menu=" + On(config.EnableContextMenu)
                + ", RDP defer=" + On(config.DeferToRemoteDesktop));
            sb.AppendLine("Counters:     flips=" + config.FlipCount + ", case flips=" + config.CaseFlipCount
                + ", translations=" + config.TranslateCount);
            sb.AppendLine("Scenarios:    " + SafeText(DescribeScenarios));
            sb.AppendLine();
            sb.AppendLine(@"Registry HKCU\Software\CyrFlip:");
            sb.Append(SafeText(DescribeRegistry));
            sb.AppendLine();
            sb.AppendLine("Clipboard history and quick notes are deliberately NOT part of this archive.");
            sb.AppendLine("Scenario paths and arguments, window titles and selected text are not recorded in any");
            sb.AppendLine("collected log; launcher.log lines written by older builds are cut when collected.");
            return sb.ToString();
        }

        /// <summary>The stamped assembly version ("26.7.29.2340"), or "unknown" if it cannot be read.</summary>
        public static string AppVersion()
        {
            try
            {
                var name = typeof(SupportBundle).Assembly.GetName();
                return name.Version == null ? "unknown" : name.Version.ToString();
            }
            catch { return "unknown"; }
        }

        /// <summary>Human-readable size for the dialog and the report.</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.CurrentCulture) + " MB";
        }

        private static void Write(ZipArchive zip, string name, byte[] bytes)
        {
            using (Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open())
                stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// The last <paramref name="maxBytes"/> of a file, or null when it does not exist. Two details
        /// that are not optional here:
        ///
        /// - <c>FileShare.ReadWrite</c>: these are our own logs and we hold them open for append, so
        ///   a plain <c>File.ReadAllBytes</c> would throw on the file we most want;
        /// - after seeking to the tail we skip to just past the first newline, so the archive never
        ///   opens on half a line, and the marker states how much was left out.
        /// </summary>
        private static byte[]? ReadTail(string path, int maxBytes, out long omitted)
        {
            omitted = 0;
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = stream.Length;
                    if (length <= maxBytes)
                    {
                        var whole = new byte[length];
                        ReadFully(stream, whole);
                        return whole;
                    }

                    stream.Seek(length - maxBytes, SeekOrigin.Begin);
                    var tail = new byte[maxBytes];
                    ReadFully(stream, tail);

                    int start = Array.IndexOf(tail, (byte)'\n');
                    start = start < 0 ? 0 : start + 1;
                    omitted = length - (maxBytes - start);

                    byte[] marker = Encoding.UTF8.GetBytes(
                        "--- truncated: first " + omitted + " bytes of " + length
                        + " omitted, tail follows ---" + Environment.NewLine);
                    var result = new byte[marker.Length + (maxBytes - start)];
                    Buffer.BlockCopy(marker, 0, result, 0, marker.Length);
                    Buffer.BlockCopy(tail, start, result, marker.Length, maxBytes - start);
                    return result;
                }
            }
            catch
            {
                // An unreadable log must not cost the user the rest of the archive.
                return null;
            }
        }

        private static void ReadFully(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk <= 0) break;
                read += chunk;
            }
        }

        /// <summary>Keep the newest <paramref name="keep"/> archives; the rest are derived artefacts.</summary>
        private static void Prune(string reportsDir, int keep)
        {
            if (keep <= 0) return;
            try
            {
                var stale = new DirectoryInfo(reportsDir)
                    .GetFiles("CyrFlip-logs-*.zip")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(keep)
                    .ToList();
                foreach (FileInfo file in stale)
                {
                    try { file.Delete(); } catch { /* locked by a mail client: leave it */ }
                }
            }
            catch { /* pruning must never fail the bundle */ }
        }

        private static string On(bool value) => value ? "on" : "off";

        private static string SafeText(Func<string> read)
        {
            try { return read(); }
            catch (Exception ex) { return "<unreadable: " + ex.GetType().Name + ">"; }
        }

        private static string DescribeLayouts()
        {
            var parts = new List<string>();
            foreach (InputLayouts.Installed layout in InputLayouts.ListInstalled())
                parts.Add(layout.Klid + " (" + layout.LanguageName + ")" + (layout.IsDefault ? " default" : ""));
            return parts.Count == 0 ? "<none reported>" : string.Join(", ", parts);
        }

        /// <summary>
        /// Count and types only. The scenario XMLs hold the user's own paths, arguments and working
        /// directories - those are not diagnostics. The report never carries them, launcher.log no
        /// longer records them (S0010 TD-1), and the lines older builds wrote are cut on collection
        /// (<see cref="ScrubLauncherLog"/>) - which is what makes "never leave the machine" true.
        /// </summary>
        private static string DescribeScenarios()
        {
            var store = new LauncherScenarioStore();
            List<LauncherScenario> scenarios = store.All;
            int ytdlp = scenarios.Count(s => s.IsYtDlp);
            int admin = scenarios.Count(s => s.RunAsAdmin);
            int chords = scenarios.Count(s => !string.IsNullOrEmpty(s.Hotkey));
            return scenarios.Count + " total (" + ytdlp + " yt-dlp, " + admin + " elevated, " + chords
                + " with a chord), " + store.LoadErrors.Count + " unreadable file(s)";
        }

        private static string DescribeRegistry()
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\CyrFlip"))
            {
                if (key == null) return "  <key absent - defaults in use>" + Environment.NewLine;
                var sb = new StringBuilder();
                foreach (string name in key.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine("  " + name + " = " + Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
