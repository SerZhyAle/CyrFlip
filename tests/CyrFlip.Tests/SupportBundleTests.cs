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

        [Fact]
        public void ClipboardHistoryIsNeverCollected()
        {
            Write("launcher.log", "launch attempt\n");
            Write(SupportBundle.Excluded, "SECRET-CLIPBOARD-PAYLOAD\n");

            SupportBundle.Result result = Create();

            Assert.Contains("launcher.log", Names(result));
            Assert.DoesNotContain(SupportBundle.Excluded, Names(result));
            // Not only absent from the listing - absent from the file, and its content nowhere in it.
            Assert.DoesNotContain(SupportBundle.Excluded, EntryNames(result.ArchivePath));
            Assert.DoesNotContain("SECRET-CLIPBOARD-PAYLOAD", RawArchiveText(result.ArchivePath));
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
            Write("launcher.log", text.ToString());

            SupportBundle.Result result = Create(maxFileBytes: 1024);

            SupportBundle.Entry entry = result.Entries.Single(e => e.Name == "launcher.log");
            Assert.True(entry.Truncated);
            Assert.True(entry.OmittedBytes > 0);

            string content = ReadEntry(result.ArchivePath, "launcher.log");
            Assert.StartsWith("--- truncated:", content);
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
            string path = Path.Combine(_logs, "launcher.log");
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var text = new StreamWriter(writer))
            {
                text.Write("written while open\n");
                text.Flush();

                SupportBundle.Result result = Create();

                Assert.Equal("written while open\n", ReadEntry(result.ArchivePath, "launcher.log"));
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
        public void TheArchiveNameCarriesTheVersionAndTheMinute()
        {
            Assert.Equal("CyrFlip-logs-26.7.29.2340-20260729-2340.zip",
                SupportBundle.ArchiveName("26.7.29.2340", new DateTime(2026, 7, 29, 23, 40, 12)));
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
            Assert.Contains("Clipboard history is deliberately NOT part of this archive.", report);
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
