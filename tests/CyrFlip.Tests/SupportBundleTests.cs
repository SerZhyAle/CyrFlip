using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The log bundle the user mails to the author. The first test here is the reason this file
    /// exists: <b>clipboard-history.log must never be collected</b>, and the cheapest way to break
    /// that is to replace the whitelist with a directory glob. Everything else guards the second
    /// rule - nothing is shortened or dropped silently.
    /// </summary>
    public class SupportBundleTests : IDisposable
    {
        private readonly string _root;
        private readonly string _logs;
        private readonly string _reports;

        public SupportBundleTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "CyrFlipBundle-" + Guid.NewGuid().ToString("N"));
            _logs = Path.Combine(_root, "logs");
            _reports = Path.Combine(_root, "reports");
            Directory.CreateDirectory(_logs);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        /// <summary>
        /// The two files holding the user's own text - everything they ever copied, and everything
        /// they deliberately kept - are absent from the archive, and so is their content. This is the
        /// test the whole class exists for; it names both files rather than one so that adding a
        /// second store of user text could never quietly re-open the first hole.
        /// </summary>
        [Fact]
        public void ClipboardHistoryAndQuickNotesAreNeverCollected()
        {
            Write("launcher.log", "launch attempt\n");
            foreach (string excluded in SupportBundle.ExcludedFiles)
                Write(excluded, "SECRET-PAYLOAD-" + excluded + "\n");

            SupportBundle.Result result = Create();

            Assert.Contains("launcher.log", Names(result));
            Assert.Equal(2, SupportBundle.ExcludedFiles.Length);
            foreach (string excluded in SupportBundle.ExcludedFiles)
            {
                Assert.DoesNotContain(excluded, Names(result));
                // Not only absent from the listing - absent from the file, and its content nowhere in it.
                Assert.DoesNotContain(excluded, EntryNames(result.ArchivePath));
                Assert.DoesNotContain("SECRET-PAYLOAD-" + excluded, RawArchiveText(result.ArchivePath));
            }
        }

        [Fact]
        public void TheReportIsAlwaysThereAndComesFirst()
        {
            SupportBundle.Result result = Create();   // no logs at all

            Assert.Equal(SupportBundle.ReportName, result.Entries[0].Name);
            Assert.Contains(SupportBundle.ReportName, EntryNames(result.ArchivePath));
        }

        [Fact]
        public void AbsentLogsAreSkippedRatherThanFatal()
        {
            Write("translate.log", "one line\n");

            SupportBundle.Result result = Create();

            Assert.Equal(new[] { SupportBundle.ReportName, "translate.log" }, Names(result).ToArray());
            Assert.Empty(result.Dropped);
        }

        [Fact]
        public void ALongLogIsKeptByItsTailWithAMarkerLine()
        {
            var text = new StringBuilder();
            text.AppendLine("FIRST-LINE-OF-THE-LOG");
            for (int i = 0; i < 400; i++) text.AppendLine("filler line " + i);
            text.AppendLine("LAST-LINE-OF-THE-LOG");
            // Not launcher.log: that one is also scrubbed to its record shapes, which is its own test.
            Write("context-menu.log", text.ToString());

            SupportBundle.Result result = Create(maxFileBytes: 1024);

            SupportBundle.Entry entry = result.Entries.Single(e => e.Name == "context-menu.log");
            Assert.True(entry.Truncated);
            Assert.True(entry.OmittedBytes > 0);

            string content = ReadEntry(result.ArchivePath, "context-menu.log");
            Assert.StartsWith("[Diag] LOG TRUNCATED | dropped_middle_bytes=" + entry.OmittedBytes + " | ", content);
            Assert.Contains("LAST-LINE-OF-THE-LOG", content);
            Assert.DoesNotContain("FIRST-LINE-OF-THE-LOG", content);
            // The tail starts on a line boundary, so the first line after the marker is whole.
            string[] lines = content.Split('\n');
            Assert.StartsWith("filler line ", lines[1]);
        }

        [Fact]
        public void AShortLogGoesInUntouched()
        {
            Write("context-menu.log", "exactly this\n");

            SupportBundle.Result result = Create(maxFileBytes: 1024);

            Assert.False(result.Entries.Single(e => e.Name == "context-menu.log").Truncated);
            Assert.Equal("exactly this\n", ReadEntry(result.ArchivePath, "context-menu.log"));
        }

        /// <summary>
        /// These are our own logs and the app holds them open for append, so the file we most want is
        /// exactly the one a plain File.ReadAllBytes would refuse.
        /// </summary>
        [Fact]
        public void ALogHeldOpenForWritingIsStillCollected()
        {
            string path = Path.Combine(_logs, "context-menu.log");
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var text = new StreamWriter(writer))
            {
                text.Write("written while open\n");
                text.Flush();

                SupportBundle.Result result = Create();

                Assert.Equal("written while open\n", ReadEntry(result.ArchivePath, "context-menu.log"));
            }
        }

        [Fact]
        public void TheTotalBudgetDropsFromTheEndAndSaysSo()
        {
            // Every log 400 bytes, budget only large enough for the report plus the first two.
            foreach (string name in new[] { "launcher.log", "context-menu.log", "translate.log", "caret-diagnostics.txt", "layout.txt" })
                Write(name, new string('x', 400));

            SupportBundle.Result result = Create(maxFileBytes: 1024, maxTotalBytes: 1200);

            Assert.Contains("launcher.log", Names(result));
            Assert.Contains("caret-diagnostics.txt", result.Dropped);
            Assert.Contains("layout.txt", result.Dropped);
            // Silent truncation is what reads as "the author got everything".
            Assert.Contains("Dropped (total size limit)", ReadEntry(result.ArchivePath, SupportBundle.ReportName));
        }

        [Fact]
        public void TheArchiveNameCarriesTheVersionAndTheSecond()
        {
            // DIAGNOSTIC-REPORT rule 1 (S0040): to the minute, a second bundle in the same minute replaced the first.
            Assert.Equal("CyrFlip-logs-26.7.29.2340-20260729-234012.zip",
                SupportBundle.ArchiveName("26.7.29.2340", new DateTime(2026, 7, 29, 23, 40, 12)));
        }

        /// <summary>
        /// DIAGNOSTIC-REPORT rules 1-2 (S0040): the key=value summary goes in right after the report,
        /// is never dropped, and passes through the redactor like everything else.
        /// </summary>
        [Fact]
        public void TheEnvironmentSummaryFollowsTheReportAndIsRedacted()
        {
            Write("layout.txt", "EN");
            SupportBundle.Result result = SupportBundle.Create(_logs, _reports, "REPORT-BODY", "26.7.29.2340",
                new DateTime(2026, 7, 29, 23, 40, 0), environmentText: "app_version=26.7.29.2340\nnote=C:\\Users\\SECRET-ANN\\x\n");

            Assert.Equal(new[] { SupportBundle.ReportName, SupportBundle.EnvironmentName, "layout.txt" }, EntryNames(result.ArchivePath));
            Assert.Equal(new[] { SupportBundle.ReportName, SupportBundle.EnvironmentName, "layout.txt" }, Names(result));
            Assert.Equal("app_version=26.7.29.2340\nnote=<USER>\\x\n", ReadEntry(result.ArchivePath, SupportBundle.EnvironmentName));
        }

        [Fact]
        public void AnEnvironmentValueCanNeverOpenASecondLine()
        {
            string text = SupportBundle.FormatEnvironment(new[]
            {
                new KeyValuePair<string, string>("os", "Windows\r\nfake_key=1"),
                new KeyValuePair<string, string>("app_version", "26.9.26.0930"),
            });
            Assert.Equal("os=Windows  fake_key=1\napp_version=26.9.26.0930\n", text);
        }

        [Fact]
        public void OnlyTheFiveNewestArchivesSurvive()
        {
            Directory.CreateDirectory(_reports);
            for (int i = 0; i < 8; i++)
            {
                string path = Path.Combine(_reports, "CyrFlip-logs-26.7.1." + (1000 + i) + "-2026070" + i + "-1200.zip");
                File.WriteAllText(path, "old");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 7, 1).AddHours(i));
            }

            SupportBundle.Result result = Create();

            string[] left = Directory.GetFiles(_reports, "CyrFlip-logs-*.zip");
            Assert.Equal(SupportBundle.KeepArchives, left.Length);
            Assert.Contains(result.ArchivePath, left);   // the one we just made is the newest
        }

        [Fact]
        public void TheReportNamesTheVersionTheSwitchesAndTheCounters()
        {
            var config = new AppConfig
            {
                UiLanguage = "Русский", FlipCount = 1843, CaseFlipCount = 57, TranslateCount = 3,
                EnableContextMenu = true, EnableTranslate = false,
            };

            string report = SupportBundle.BuildReport(config, "26.7.29.2340", new DateTime(2026, 7, 29, 23, 40, 0));

            Assert.Contains("CyrFlip 26.7.29.2340", report);
            Assert.Contains("flips=1843", report);
            Assert.Contains("case flips=57", report);
            Assert.Contains("translations=3", report);
            Assert.Contains("context menu=on", report);
            Assert.Contains("translate=off", report);
            // The report is where the promise is written down, so it is also asserted here.
            Assert.Contains("Clipboard history and quick notes are deliberately NOT part of this archive.", report);
        }

        /// <summary>
        /// S0010 TD-1: a launcher.log written by an older build carries the command line of every
        /// launch - and "--token=..." is routine there. The secret must be nowhere in the archive,
        /// while the parts of the line that are diagnostics (time, name, pid) survive.
        /// </summary>
        [Fact]
        public void AnOldLauncherLogLosesItsCommandLinesOnCollection()
        {
            Write("launcher.log",
                "2026-09-01 10:00:00 - Launched 'Deploy' (pid=42, admin=False): C:\\tools\\deploy.exe --token=SECRET-TOKEN\r\n"
                + "2026-09-01 10:00:01 - Launch blocked for 'Backup': Файл не найден: C:\\Users\\me\\SECRET-PATH\\b.cmd\r\n"
                + "2026-09-01 10:00:02 - Launch error for 'Sync': access denied to SECRET-ERROR\n"
                + "2026-09-01 10:00:03 - Launched 'Calc' [0b6c3f0e-0000-0000-0000-000000000001] (exe, pid=7, admin=False)\n");

            string text = ReadEntry(Create().ArchivePath, "launcher.log");

            Assert.DoesNotContain("SECRET", text);
            Assert.Contains("2026-09-01 10:00:00 - Launched 'Deploy' (pid=42, admin=False): " + SupportBundle.RemovedMarker + "\r\n", text);
            Assert.Contains("Launch blocked for 'Backup': " + SupportBundle.RemovedMarker, text);
            Assert.Contains("Launch error for 'Sync': " + SupportBundle.RemovedMarker, text);
            // A line of today's shape carries nothing to cut and is left exactly as written.
            Assert.Contains("Launched 'Calc' [0b6c3f0e-0000-0000-0000-000000000001] (exe, pid=7, admin=False)\n", text);
        }

        /// <summary>
        /// S0034 LS2-3: every release up to v26.9.16.2132 logged the yt-dlp download folder, and a
        /// record whose name or arguments held a line break went on on a second physical line that no
        /// pattern of the old scrub matched. Neither may reach the archive.
        /// </summary>
        [Fact]
        public void LegacyYtDlpLinesAndContinuationLinesDoNotReachTheArchive()
        {
            Write("launcher.log",
                "2026-09-01 10:00:00 - Started yt-dlp (pid=42) in 'D:\\SECRET-FOLDER\\Video'\r\n"
                + "2026-09-01 10:00:01 - yt-dlp launch error: could not start in D:\\SECRET-ERROR\r\n"
                + "2026-09-01 10:00:02 - Launched 'Two\n"
                + "lines' (pid=5, admin=False): C:\\t\\x.exe --token=SECRET-TOKEN\n"
                + "2026-09-01 10:00:03 - Store: save failed for a.xml: Access to the path 'C:\\Users\\SECRET-USER\\a.xml' is denied.\n"
                + "2026-09-01 10:00:04 - IPC: received: /launcher-settings\n");

            string text = ReadEntry(Create().ArchivePath, "launcher.log");

            Assert.DoesNotContain("SECRET", text);
            Assert.Contains("2026-09-01 10:00:00 - Started yt-dlp (pid=42): " + SupportBundle.RemovedMarker + "\r\n", text);
            Assert.Contains("2026-09-01 10:00:01 - yt-dlp launch error: " + SupportBundle.RemovedMarker + "\r\n", text);
            Assert.Contains("2026-09-01 10:00:02 - Launched: " + SupportBundle.RemovedMarker + "\n", text);
            Assert.Contains("2026-09-01 10:00:03 - Store: save failed for a.xml: " + SupportBundle.RemovedMarker + "\n", text);
            // Today's shapes that carry nothing to cut stay exactly as written.
            Assert.Contains("2026-09-01 10:00:04 - IPC: received: /launcher-settings\n", text);
        }

        /// <summary>
        /// S0040: a marker line is let through only when it is a marker, whole - a scenario name with a
        /// line break must not smuggle its second line past the scrub by starting it like one.
        /// </summary>
        [Fact]
        public void OnlyWholeMarkerLinesPassTheLauncherScrub()
        {
            string compacted = DiagnosticLog.CompactedMarker(1000, 200);
            Write("launcher.log",
                compacted + "\n"
                + "--- truncated: first 10 bytes of 20 omitted, tail follows ---\n"
                + "2026-09-01 10:00:00 - Launched 'Two\n"
                + "[Diag] LOG COMPACTED C:\\SECRET-PATH\n"
                + "--- rotated C:\\SECRET-OLD\n");

            string text = ReadEntry(Create().ArchivePath, "launcher.log");

            Assert.DoesNotContain("SECRET", text);
            Assert.StartsWith(compacted + "\n--- truncated: first 10 bytes of 20 omitted, tail follows ---\n", text);
        }

        [Fact]
        public void TheScrubLeavesOtherLogsAlone()
        {
            const string line = "2026-09-01 10:00:00 - Launched 'X' (pid=1, admin=False): kept.exe --because-not-launcher-log\n";
            Write("translate.log", line);

            Assert.Equal(line, ReadEntry(Create().ArchivePath, "translate.log"));
        }

        /// <summary>
        /// The dialog tells the user what each file holds (TD-1): every file the bundle can collect
        /// has a line, and every line is a registered, translated string - a file the dialog cannot
        /// describe is one the user is asked to send blind.
        /// </summary>
        [Fact]
        public void EveryCollectedFileHasATranslatedDescription()
        {
            var registered = new HashSet<string>(Localization.All.Select(e => e.Key));
            foreach (string name in SupportBundle.LogFiles.Concat(new[] { SupportBundle.ReportName, SupportBundle.EnvironmentName }))
            {
                Assert.True(SupportBundle.Contents.TryGetValue(name, out string? ru), name + " has no description");
                Assert.Contains(ru!, registered);
            }
        }

        /// <summary>
        /// DIAGNOSTIC-REPORT rule 3 (S0040): no personal directory leaves by name - not in the report,
        /// not in a log, not as a doubled-backslash JSON value, not in a URL's userinfo - while the
        /// rest of each path, which is the diagnostic part, survives.
        /// </summary>
        [Fact]
        public void PersonalPathsAndUrlCredentialsAreRedactedEverywhereInTheArchive()
        {
            Write("context-menu.log", "opened in C:\\Users\\SECRET-ANN\\AppData\\Local\\CyrFlip\\x.log\n");
            Write("caret-diagnostics.txt", "process D:/Users/SECRET-BOB/tools/app.exe\n");
            var redactor = new DiagnosticRedactor(new[]
            {
                new KeyValuePair<string, string>("C:\\Users\\SECRET-ANN\\AppData\\Local\\CyrFlip", DiagnosticRedactor.AppDataToken),
                new KeyValuePair<string, string>("C:\\Users\\SECRET-ANN", DiagnosticRedactor.UserToken),
            });
            string report = "ScreenshotFolder = C:\\Users\\SECRET-ANN\\Pictures\n"
                + "Backup = {\"p\":\"C:\\\\Users\\\\SECRET-ANN\\\\x\"}\n"
                + "TranslateEndpoint = http://SECRET-USER:SECRET-PASS@host:11434\n";

            SupportBundle.Result result = SupportBundle.Create(_logs, _reports, report, "26.7.29.2340",
                new DateTime(2026, 7, 29, 23, 40, 0), redactor: redactor);

            string all = RawArchiveText(result.ArchivePath);
            Assert.DoesNotContain("SECRET", all);
            Assert.Contains("opened in <APP_DATA>\\x.log", ReadEntry(result.ArchivePath, "context-menu.log"));
            Assert.Contains("process <USER>/tools/app.exe", ReadEntry(result.ArchivePath, "caret-diagnostics.txt"));
            string text = ReadEntry(result.ArchivePath, SupportBundle.ReportName);
            Assert.Contains("ScreenshotFolder = <USER>\\Pictures", text);
            Assert.Contains("TranslateEndpoint = http://host:11434", text);
        }

        private SupportBundle.Result Create(int maxFileBytes = SupportBundle.MaxFileBytes,
            long maxTotalBytes = SupportBundle.MaxTotalBytes) =>
            SupportBundle.Create(_logs, _reports, "REPORT-BODY", "26.7.29.2340",
                new DateTime(2026, 7, 29, 23, 40, 0), maxFileBytes, maxTotalBytes);

        private void Write(string name, string content) =>
            File.WriteAllText(Path.Combine(_logs, name), content);

        private static IEnumerable<string> Names(SupportBundle.Result result) =>
            result.Entries.Select(e => e.Name);

        private static List<string> EntryNames(string archive)
        {
            using (ZipArchive zip = ZipFile.OpenRead(archive))
                return zip.Entries.Select(e => e.FullName).ToList();
        }

        private static string ReadEntry(string archive, string name)
        {
            using (ZipArchive zip = ZipFile.OpenRead(archive))
            using (var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>Every entry's text concatenated - so "the payload is nowhere in the archive" is provable.</summary>
        private static string RawArchiveText(string archive)
        {
            var all = new StringBuilder();
            using (ZipArchive zip = ZipFile.OpenRead(archive))
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    all.AppendLine(entry.FullName);
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        all.AppendLine(reader.ReadToEnd());
                }
            return all.ToString();
        }
    }
}
