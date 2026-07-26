using System;
using System.IO;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The launcher store: XML round trip (the OneClickRunner contract plus the CyrFlip hotkey),
    /// corrupted-file isolation, contiguous ordering, clone/import semantics and the seed rule.
    /// Every test runs against a disposable temp directory, never the developer's AppData.
    /// </summary>
    public class LauncherScenarioStoreTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); } catch { }
        }

        private LauncherScenarioStore NewStore() => new LauncherScenarioStore(_folder);

        private static LauncherScenario Sample(string name = "Test") => new LauncherScenario
        {
            Name = name,
            Path = @"C:\Windows\System32\calc.exe",
            Arguments = "-a \"b c\"",
            WorkingDirectory = @"C:\Windows",
            RunAsAdmin = true,
            Hotkey = "Ctrl+Alt+F9",
        };

        [Fact]
        public void RoundTripPreservesEveryField()
        {
            var store = NewStore();
            LauncherScenario original = Sample();
            original.Type = LauncherScenarioType.YtDlp;
            original.YtDlpOutputFolder = @"D:\Video";
            original.YtDlpFormat = "-f best";
            store.Add(original);

            var reloaded = new LauncherScenarioStore(_folder);
            LauncherScenario read = Assert.Single(reloaded.All);
            Assert.Equal(original.Id, read.Id);
            Assert.Equal(original.Name, read.Name);
            Assert.Equal(original.Path, read.Path);
            Assert.Equal(original.Arguments, read.Arguments);
            Assert.Equal(original.WorkingDirectory, read.WorkingDirectory);
            Assert.True(read.RunAsAdmin);
            Assert.Equal(LauncherScenarioType.YtDlp, read.Type);
            Assert.Equal(@"D:\Video", read.YtDlpOutputFolder);
            Assert.Equal("-f best", read.YtDlpFormat);
            Assert.Equal("Ctrl+Alt+F9", read.Hotkey);
        }

        [Fact]
        public void LegacySentinelPathReadsAsYtDlpType()
        {
            var store = NewStore();
            store.Add(new LauncherScenario { Name = "legacy", Path = LauncherScenario.LegacyYtDlpSentinel });

            var reloaded = new LauncherScenarioStore(_folder);
            Assert.Equal(LauncherScenarioType.YtDlp, Assert.Single(reloaded.All).Type);
        }

        [Fact]
        public void OneClickRunnerXmlWithoutTheNewFieldsStillLoads()
        {
            // A file exactly as OneClickRunner writes it: no Order, Type, yt-dlp or Hotkey elements.
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "legacy.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<AppItem xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n" +
                "  <Id>c7d3a9e2-4b5f-6c8d-9e1f-2a3b4c5d6e7f</Id>\n" +
                "  <Name>RDP</Name>\n  <Path>mstsc.exe</Path>\n  <Arguments>\"x.rdp\"</Arguments>\n" +
                "  <WorkingDirectory></WorkingDirectory>\n  <Filename>legacy.xml</Filename>\n</AppItem>");

            LauncherScenario read = Assert.Single(NewStore().All);
            Assert.Equal("RDP", read.Name);
            Assert.Equal(LauncherScenarioType.Executable, read.Type);
            Assert.Equal("", read.Hotkey);
            Assert.False(read.RunAsAdmin);
        }

        [Fact]
        public void CorruptFileIsSkippedAndCountedWithoutBreakingNeighbours()
        {
            var store = NewStore();
            store.Add(Sample("good"));
            File.WriteAllText(Path.Combine(_folder, "broken.xml"), "<AppItem><unclosed");

            var reloaded = new LauncherScenarioStore(_folder);
            Assert.Equal("good", Assert.Single(reloaded.All).Name);
            Assert.Equal("broken.xml", Assert.Single(reloaded.LoadErrors));
        }

        [Fact]
        public void OrdersAreContiguousAndMovePersists()
        {
            var store = NewStore();
            store.Add(Sample("a"));
            store.Add(Sample("b"));
            store.Add(Sample("c"));
            Assert.Equal(new[] { 0, 1, 2 }, store.All.Select(s => s.Order));

            Guid bottom = store.All[2].Id;
            store.Move(bottom, -1);
            Assert.Equal(new[] { "a", "c", "b" }, store.All.Select(s => s.Name));

            // The change survives a full reload, and the ordinals stay contiguous.
            var reloaded = new LauncherScenarioStore(_folder);
            Assert.Equal(new[] { "a", "c", "b" }, reloaded.All.Select(s => s.Name));
            Assert.Equal(new[] { 0, 1, 2 }, reloaded.All.Select(s => s.Order));
        }

        [Fact]
        public void MoveBeyondTheEdgesIsANoOp()
        {
            var store = NewStore();
            store.Add(Sample("a"));
            store.Add(Sample("b"));
            store.Move(store.All[0].Id, -1);
            store.Move(store.All[1].Id, +1);
            Assert.Equal(new[] { "a", "b" }, store.All.Select(s => s.Name));
        }

        [Fact]
        public void RemoveDeletesTheFileAndTheListStaysEmptyAfterDeletingAll()
        {
            var store = NewStore();
            store.Add(Sample("a"));
            store.Add(Sample("b"));
            foreach (LauncherScenario scenario in store.All)
                store.Remove(scenario.Id);

            Assert.Empty(store.All);
            Assert.Empty(Directory.GetFiles(_folder, "*.xml"));
            // T0018: an emptied list stays empty - reload never resurrects or reseeds anything.
            Assert.Empty(new LauncherScenarioStore(_folder).All);
        }

        [Fact]
        public void ImportAssignsAFreshGuidAndAppends()
        {
            var store = NewStore();
            LauncherScenario original = Sample("exported");
            store.Add(original);

            string exported = Path.Combine(_folder, "portable-export.bin");
            store.Export(original, exported);
            LauncherScenario? imported = store.Import(exported, out Exception? error);

            Assert.Null(error);
            Assert.NotNull(imported);
            Assert.NotEqual(original.Id, imported!.Id);
            Assert.Equal("exported", imported.Name);
            Assert.Equal(2, store.Count);
            Assert.Equal(1, imported.Order); // appended to the end
        }

        [Fact]
        public void ImportOfUnreadableFileReportsTheError()
        {
            var store = NewStore();
            string bad = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N") + ".xml");
            Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
            File.WriteAllText(bad, "not xml at all");
            try
            {
                Assert.Null(store.Import(bad, out Exception? error));
                Assert.NotNull(error);
                Assert.Empty(store.All);
            }
            finally { File.Delete(bad); }
        }

        [Fact]
        public void SeedSampleCreatesCalculatorOnlyIntoAnEmptyStore()
        {
            var store = NewStore();
            store.SeedSample("Калькулятор");
            LauncherScenario seeded = Assert.Single(store.All);
            Assert.Equal("Калькулятор", seeded.Name);
            Assert.Equal("calc.exe", seeded.Path);

            store.SeedSample("Калькулятор"); // a non-empty store is never reseeded
            Assert.Single(store.All);
        }

        [Fact]
        public void CloneCopiesEveryFieldIncludingTheHotkey()
        {
            LauncherScenario original = Sample();
            original.Type = LauncherScenarioType.YtDlp;
            original.YtDlpOutputFolder = "X";
            original.YtDlpFormat = "Y";
            original.Order = 7;

            LauncherScenario clone = original.Clone();
            Assert.Equal(original.Id, clone.Id);
            Assert.Equal(original.Name, clone.Name);
            Assert.Equal(original.Path, clone.Path);
            Assert.Equal(original.Arguments, clone.Arguments);
            Assert.Equal(original.WorkingDirectory, clone.WorkingDirectory);
            Assert.Equal(original.RunAsAdmin, clone.RunAsAdmin);
            Assert.Equal(original.Order, clone.Order);
            Assert.Equal(original.Type, clone.Type);
            Assert.Equal(original.YtDlpOutputFolder, clone.YtDlpOutputFolder);
            Assert.Equal(original.YtDlpFormat, clone.YtDlpFormat);
            Assert.Equal(original.Hotkey, clone.Hotkey);
        }

        [Fact]
        public void AStoreOverAMissingFolderIsEmptyAndCreatesNothing()
        {
            var store = NewStore();
            Assert.Empty(store.All);
            // Merely reading must not create the folder (opening settings on a machine that never
            // enabled the launcher leaves no trace).
            Assert.False(Directory.Exists(_folder));
        }
    }
}
