using System;
using System.IO;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The OneClickRunner migration, run against the byte-exact fixtures copied from the source
    /// repository (Фаза 0): Guids survive, collisions get fresh ids, the legacy
    /// <c>SPECIAL_YTDLP</c> sentinel becomes the yt-dlp type, corrupt files are skipped, and the
    /// source directory is never modified.
    /// </summary>
    public class LauncherMigrationTests : IDisposable
    {
        private static readonly string Fixtures = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "OneClickRunner");

        private readonly string _target = Path.Combine(
            Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_target, recursive: true); } catch { }
        }

        [Fact]
        public void FixturesArePresent()
        {
            Assert.True(Directory.Exists(Fixtures), "Fixture folder missing: " + Fixtures);
            Assert.Equal(9, Directory.GetFiles(Fixtures, "*.xml").Length);
        }

        [Fact]
        public void ImportBringsEveryFixtureAndPreservesGuids()
        {
            var store = new LauncherScenarioStore(_target);
            LauncherMigration.Result result = LauncherMigration.Import(store, Fixtures);

            Assert.Equal(9, result.Imported);
            Assert.Empty(result.Skipped);
            Assert.Equal(0, result.NewIds);
            Assert.Equal(9, store.Count);

            // The real Calculator fixture id survives the move.
            Assert.Contains(store.All, s => s.Id == Guid.Parse("12345678-1234-1234-1234-123456789abc"));
            // Orders are contiguous.
            Assert.Equal(Enumerable.Range(0, 9), store.All.Select(s => s.Order));
        }

        /// <summary>
        /// SCENARIO-FILE §4: the nine catalog vectors, SHA-256 as printed in the contract. The literals
        /// are the point - the catalog is not in git and CI has no copy of it, so a fixture that drifts
        /// from the vectors has to fail here. A contract re-issue of the vectors updates this table in
        /// the same change.
        /// </summary>
        [Theory]
        [InlineData("rdp_p7.xml", "d0a5228e23412f6964f72af82ac976e68d1d557fbf18a9b21a792cfe4d0ec679")]
        [InlineData("resume_hyperv_p7.xml", "415a3c4bf43f10c02b849dcb38b3df2276dd6cbe669aaf7fe6b91305c543f608")]
        [InlineData("resume_hyperv_p7_import.xml", "107e394e437b6eb3f1eed28be44894beb16bf4cc982d3e9b81e0de1bb2bbe9e9")]
        [InlineData("run_windows_calculator.xml", "c2775e9acbe022343bbc28cea3775e6e62e8d5c6e0b3b28bcb6eec4375c0fcb5")]
        [InlineData("save_hyperv_p7.xml", "2e11f7fbca54b565cd6a5d88c6f84a7852e108056c63db89321d10f70f575e2d")]
        [InlineData("sim_swap_ph.xml", "f46f29f606466353f12b4155ed34f9a9faf32f85b6eb68a6870b2300db5f79da")]
        [InlineData("sim_swap_video.xml", "f7399f22bb5cebf0e2ccf7792a01d4eedf12fab9d9120326c8946f75b6a05b7a")]
        [InlineData("start_hyperv_p7.xml", "f58a01270b4674a580c354089ac2850c82acfb76168a2720a62370efa9594dce")]
        [InlineData("ytdlp_download.xml", "111362b8d07508684c8b3c8377a05deb38bce55a1312129d9d401dafde1f2bb5")]
        public void FixturesAreByteIdenticalToTheCatalogVectors(string name, string sha256)
        {
            Assert.Equal(sha256, Sha256(Path.Combine(Fixtures, name)));
        }

        [Fact]
        public void RdpVectorArrivesAsExecutableWithNoHotkey()
        {
            // Read directly, before any store renumbers it - the §4 claim is about the file.
            LauncherScenario? rdp = LauncherScenarioStore.TryRead(Path.Combine(Fixtures, "rdp_p7.xml"), out Exception? error);
            Assert.Null(error);
            Assert.NotNull(rdp);
            Assert.Equal("RDP Connect P7", rdp!.Name);
            Assert.Equal(LauncherScenarioType.Executable, rdp.Type);
            Assert.Equal(0, rdp.Order);
            Assert.Equal(string.Empty, rdp.Hotkey);
            Assert.False(rdp.RunAsAdmin);
            Assert.False(rdp.IsYtDlp);
        }

        [Fact]
        public void MigrationAppendsInTheSourceProductsOrder()
        {
            var store = new LauncherScenarioStore(_target);
            LauncherMigration.Import(store, Fixtures);

            // Rule 5: all-zero Order, so by name ignoring case; the case-only tie between the two
            // "Resume" vectors falls to the ordinal name ("P7" before "p7"). File-name order would
            // have put "RDP Connect P7" (rdp_p7.xml) first.
            Assert.Equal(new[]
            {
                "Calculator",
                "Hibernate Hyper-V VM P7",
                "RDP Connect P7",
                "Resume Hyper-V VM P7",
                "Resume Hyper-V VM p7",
                "sim-swap ph",
                "sim-swap video",
                "Start Hyper-V VM p7",
                "yt-dlp - download media",
            }, store.All.Select(s => s.Name));
        }

        [Fact]
        public void MigrationKeepsExplicitSourceOrderValuesRelativeToEachOther()
        {
            string source = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(source);
            try
            {
                // File names sort a, b, c; the stored Order says c, a, b.
                File.WriteAllText(Path.Combine(source, "a.xml"), "<AppItem><Name>Alpha</Name><Order>5</Order></AppItem>");
                File.WriteAllText(Path.Combine(source, "b.xml"), "<AppItem><Name>Beta</Name><Order>9</Order></AppItem>");
                File.WriteAllText(Path.Combine(source, "c.xml"), "<AppItem><Name>Gamma</Name><Order>1</Order></AppItem>");

                var store = new LauncherScenarioStore(_target);
                store.Add(new LauncherScenario { Name = "Mine", Path = "calc.exe" });
                LauncherMigration.Import(store, source);

                Assert.Equal(new[] { "Mine", "Gamma", "Alpha", "Beta" }, store.All.Select(s => s.Name));
                Assert.Equal(Enumerable.Range(0, 4), store.All.Select(s => s.Order));
            }
            finally { Directory.Delete(source, recursive: true); }
        }

        [Fact]
        public void SourceOrderIsTotalEvenOnAFullTie()
        {
            var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var items = new[]
            {
                new LauncherScenario { Id = high, Name = "Same" },
                new LauncherScenario { Id = low, Name = "Same" },
            };
            Assert.Equal(new[] { low, high }, LauncherMigration.InSourceOrder(items).Select(s => s.Id));
            Assert.Equal(new[] { low, high }, LauncherMigration.InSourceOrder(items.Reverse()).Select(s => s.Id));
        }

        private static string Sha256(string file)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return string.Concat(sha.ComputeHash(File.ReadAllBytes(file)).Select(b => b.ToString("x2")));
        }

        [Fact]
        public void LegacyYtDlpSentinelArrivesAsTheYtDlpType()
        {
            var store = new LauncherScenarioStore(_target);
            LauncherMigration.Import(store, Fixtures);
            LauncherScenario ytdlp = Assert.Single(store.All, s => s.Path == LauncherScenario.LegacyYtDlpSentinel);
            Assert.Equal(LauncherScenarioType.YtDlp, ytdlp.Type);
            Assert.True(ytdlp.IsYtDlp);
        }

        [Fact]
        public void GuidCollisionGetsAFreshIdAndIsCounted()
        {
            var store = new LauncherScenarioStore(_target);
            var occupying = new LauncherScenario
            {
                Id = Guid.Parse("12345678-1234-1234-1234-123456789abc"), // the Calculator fixture's id
                Name = "already here",
                Path = "calc.exe",
            };
            store.Add(occupying);

            LauncherMigration.Result result = LauncherMigration.Import(store, Fixtures);
            Assert.Equal(9, result.Imported);
            Assert.Equal(1, result.NewIds);
            Assert.Equal(10, store.Count);
            // Exactly one scenario kept the occupied guid - the pre-existing one.
            Assert.Equal("already here",
                Assert.Single(store.All, s => s.Id == occupying.Id).Name);
        }

        [Fact]
        public void CorruptSourceFileIsSkippedByName()
        {
            string source = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(source);
            try
            {
                foreach (string file in Directory.GetFiles(Fixtures, "*.xml"))
                    File.Copy(file, Path.Combine(source, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(source, "damaged.xml"), "<AppItem><Id>oops");

                var store = new LauncherScenarioStore(_target);
                LauncherMigration.Result result = LauncherMigration.Import(store, source);
                Assert.Equal(9, result.Imported);
                Assert.Equal("damaged.xml", Assert.Single(result.Skipped));
            }
            finally { Directory.Delete(source, recursive: true); }
        }

        [Fact]
        public void TheSourceDirectoryIsNeverModified()
        {
            // SCENARIO-FILE rule 7, proved the way the contract words it: hash, run, hash again.
            var before = Directory.GetFiles(Fixtures, "*").OrderBy(f => f, StringComparer.Ordinal)
                .ToDictionary(f => f, Sha256);

            LauncherMigration.Import(new LauncherScenarioStore(_target), Fixtures);

            var after = Directory.GetFiles(Fixtures, "*").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.Equal(before.Count, after.Length);
            foreach (string file in after)
                Assert.Equal(before[file], Sha256(file));
        }

        [Fact]
        public void SourceExistsAndCountSeeTheFixtureFolder()
        {
            Assert.True(LauncherMigration.SourceExists(Fixtures));
            Assert.Equal(9, LauncherMigration.SourceCount(Fixtures));
            string missing = Path.Combine(Path.GetTempPath(), "CyrFlipTests", "definitely-missing");
            Assert.False(LauncherMigration.SourceExists(missing));
            Assert.Equal(0, LauncherMigration.SourceCount(missing));
        }
    }
}
