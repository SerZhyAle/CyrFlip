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
            // Orders are contiguous, in source (alphabetical file) order.
            Assert.Equal(Enumerable.Range(0, 9), store.All.Select(s => s.Order));
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
            var before = Directory.GetFiles(Fixtures, "*").OrderBy(f => f, StringComparer.Ordinal)
                .ToDictionary(f => f, File.ReadAllBytes);

            LauncherMigration.Import(new LauncherScenarioStore(_target), Fixtures);

            var after = Directory.GetFiles(Fixtures, "*").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.Equal(before.Count, after.Length);
            foreach (string file in after)
                Assert.Equal(before[file], File.ReadAllBytes(file));
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
