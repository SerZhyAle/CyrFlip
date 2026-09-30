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

        /// <summary>
        /// S0034 LS2-6: a file that cannot be deleted keeps its scenario - dropping it from the list
        /// only made the next reload bring it back, chord and all, without a word.
        /// </summary>
        [Fact]
        public void AScenarioWhoseFileCannotBeDeletedStaysAndSaysWhy()
        {
            var store = NewStore();
            LauncherScenario scenario = Sample("locked");
            store.Add(scenario);
            string file = Path.Combine(_folder, store.All[0].Filename);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                string? failure = store.Remove(scenario.Id);

                Assert.NotNull(failure);
                Assert.Single(store.All);
                Assert.True(File.Exists(file));
            }
            finally { File.SetAttributes(file, FileAttributes.Normal); }

            Assert.Null(store.Remove(scenario.Id));
            Assert.Empty(store.All);
        }

        /// <summary>
        /// S0034 LS2-9: the log bundle counts scenarios with a store of its own; that load must not
        /// re-identify, renumber or clean up anything - the live store would then hold ids no file has.
        /// </summary>
        [Fact]
        public void AReadOnlyStoreNeverWritesAFile()
        {
            var store = NewStore();
            store.Add(Sample("a"));
            store.Add(Sample("b"));
            string[] files = Directory.GetFiles(_folder, "*.xml");
            // A copied file (a duplicate id) and a gap in the order - both of which a normal load repairs.
            File.Copy(files[0], Path.Combine(_folder, "copy.xml"));
            File.WriteAllText(files[1], File.ReadAllText(files[1])
                .Replace("<Order>0</Order>", "<Order>5</Order>").Replace("<Order>1</Order>", "<Order>7</Order>"));
            var before = Directory.GetFiles(_folder).ToDictionary(f => f, f => File.ReadAllText(f));

            var readOnly = new LauncherScenarioStore(_folder, readOnly: true);

            Assert.Equal(3, readOnly.Count);
            var after = Directory.GetFiles(_folder).ToDictionary(f => f, f => File.ReadAllText(f));
            Assert.Equal(before, after);
            Assert.Throws<InvalidOperationException>(() => readOnly.Add(Sample("c")));
        }

        [Fact]
        public void RemovingAScenarioWhoseFileIsAlreadyGoneSucceeds()
        {
            var store = NewStore();
            LauncherScenario scenario = Sample("gone");
            store.Add(scenario);
            File.Delete(Path.Combine(_folder, store.All[0].Filename));

            Assert.Null(store.Remove(scenario.Id));
            Assert.Empty(store.All);
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

        [Fact]
        public void AnElementFromANewerWriterIsIgnoredRatherThanFatal()
        {
            // SCENARIO-FILE rule 2: elements are matched by name and an unknown one is ignored. This
            // is the forward tolerance the format has instead of a version field - a future writer
            // adds an element, and everything shipped keeps reading the file it understands.
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "newer.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<AppItem>\n  <Id>7f2c1a48-0b3e-4d5a-9c6b-1e8f4a2d7b30</Id>\n" +
                "  <Name>From a newer writer</Name>\n  <Path>calc.exe</Path>\n" +
                "  <RunAsAdmin>true</RunAsAdmin>\n" +
                "  <ElevationReason>because the contract says nothing about this</ElevationReason>\n" +
                "</AppItem>");

            LauncherScenario read = Assert.Single(NewStore().All);
            Assert.Equal("From a newer writer", read.Name);
            Assert.True(read.RunAsAdmin);
        }

        [Fact]
        public void AnUnknownTypeValueMakesTheFileUnreadableUnderRule9()
        {
            // SCENARIO-FILE rule 10 and rule 9 (S0014 B2): an unknown kind is an action this reader does
            // not understand, and degrading it to Executable would run its Path as a program - the one
            // outcome its author did not mean. So the file is refused whole, skipped, named and counted.
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "futuretype.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<AppItem>\n  <Id>1d9e6c74-5b2a-4f38-8e7d-3c0a5b9f1e26</Id>\n" +
                "  <Name>Some future kind</Name>\n  <Path>calc.exe</Path>\n" +
                "  <Type>SomethingNobodyHasShippedYet</Type>\n</AppItem>");

            var store = NewStore();
            Assert.Empty(store.All);
            Assert.Equal("futuretype.xml", Assert.Single(store.LoadErrors));
        }

        // ---- The version carrier and the renamed root (S0014 B1, A6) ----

        private string WriteScenario(string name, string root)
        {
            Directory.CreateDirectory(_folder);
            string file = Path.Combine(_folder, name);
            File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root);
            return file;
        }

        [Theory]
        [InlineData("<AppItem2><Name>x</Name></AppItem2>")]
        [InlineData("<AppItem xmlns=\"urn:x\"><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"2\"><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"2.1\"><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"two\"><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"\"><Name>x</Name></AppItem>")]
        public void AFileInAFormatThisBuildDoesNotReadIsRefusedWholeAndSaysSo(string xml)
        {
            string file = WriteScenario("future.xml", xml);

            Assert.Null(LauncherScenarioStore.TryRead(file, out Exception? error));
            Assert.IsType<ScenarioFormatException>(error);

            var store = NewStore();
            Assert.Empty(store.All);
            Assert.Equal("future.xml", Assert.Single(store.LoadErrors));
        }

        [Theory]
        [InlineData("<AppItem><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"1\"><Name>x</Name></AppItem>")]
        [InlineData("<AppItem schemaVersion=\"1.4\"><Name>x</Name></AppItem>")]
        public void AnAbsentOrSupportedSchemaVersionReadsNormally(string xml)
        {
            string file = WriteScenario("ok.xml", xml);

            LauncherScenario? read = LauncherScenarioStore.TryRead(file, out Exception? error);

            Assert.Null(error);
            Assert.Equal("x", read!.Name);
        }

        [Fact]
        public void ADamagedFileIsStillADamagedFileNotAFormatRefusal()
        {
            string file = WriteScenario("broken.xml", "<AppItem><Id>oops");

            Assert.Null(LauncherScenarioStore.TryRead(file, out Exception? error));
            Assert.NotNull(error);
            Assert.IsNotType<ScenarioFormatException>(error);
        }

        [Fact]
        public void OurOwnWriterOmitsTheVersionCarrier()
        {
            // Writers keep omitting it until the contract's 1.0 says otherwise (B1).
            string file = Path.Combine(_folder, "exported.xml");
            Directory.CreateDirectory(_folder);
            NewStore().Export(new LauncherScenario { Name = "x", Path = "calc.exe" }, file);

            Assert.DoesNotContain("schemaVersion", File.ReadAllText(file));
        }

        // ---- Atomic save and identity (ticket S0008, LS-9 and LS-10) ----

        private static string Xml(string? id, string name)
            => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<AppItem>\n"
               + (id == null ? "" : "  <Id>" + id + "</Id>\n")
               + "  <Name>" + name + "</Name>\n  <Path>calc.exe</Path>\n</AppItem>";

        [Fact]
        public void ASaveOverAnExistingFileLeavesExactlyThatFile()
        {
            var store = NewStore();
            LauncherScenario item = Sample("one");
            store.Add(item);
            item.Name = "renamed";
            store.Update(item);

            string only = Assert.Single(Directory.GetFiles(_folder));
            Assert.Equal(item.Id + ".xml", Path.GetFileName(only));
            Assert.Equal("renamed", Assert.Single(new LauncherScenarioStore(_folder).All).Name);
        }

        [Fact]
        public void TempNamesAreUniquePerSave()
        {
            string path = Path.Combine(_folder, "x.xml");
            string a = LauncherScenarioStore.TempPathFor(path), b = LauncherScenarioStore.TempPathFor(path);
            Assert.NotEqual(a, b);
            Assert.EndsWith(".tmp", a);
            Assert.StartsWith(Path.Combine(_folder, "x."), a);
        }

        [Fact]
        public void ALeftoverTempFileIsNotAScenarioAndAnOldOneIsRemoved()
        {
            var store = NewStore();
            store.Add(Sample("real"));
            string fresh = Path.Combine(_folder, "a.123.1.tmp");
            string stale = Path.Combine(_folder, "b.123.2.tmp");
            File.WriteAllText(fresh, Xml(Guid.NewGuid().ToString(), "half-written"));
            File.WriteAllText(stale, Xml(Guid.NewGuid().ToString(), "abandoned"));
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));

            var reloaded = new LauncherScenarioStore(_folder);
            Assert.Equal("real", Assert.Single(reloaded.All).Name);
            Assert.Empty(reloaded.LoadErrors);
            Assert.True(File.Exists(fresh));
            Assert.False(File.Exists(stale));
        }

        /// <summary>A file with no &lt;Id&gt; gets one - and keeps it, so a Jump List task can find it again.</summary>
        [Fact]
        public void AFileWithoutAnIdGetsOneThatSurvivesTheNextLoad()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "noid.xml"), Xml(null, "anonymous"));

            Guid first = Assert.Single(NewStore().All).Id;
            Guid second = Assert.Single(NewStore().All).Id;
            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);
        }

        /// <summary>A copied file no longer shares its original's identity, so editing it edits it.</summary>
        [Fact]
        public void ADuplicateIdIsReplacedInTheSecondFileOnly()
        {
            Directory.CreateDirectory(_folder);
            string id = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(_folder, "a.xml"), Xml(id, "original"));
            File.WriteAllText(Path.Combine(_folder, "b.xml"), Xml(id, "copy"));

            var store = NewStore();
            Assert.Equal(2, store.Count);
            LauncherScenario original = store.All.Single(s => s.Name == "original");
            LauncherScenario copy = store.All.Single(s => s.Name == "copy");
            Assert.Equal(Guid.Parse(id), original.Id);
            Assert.NotEqual(original.Id, copy.Id);

            copy.Name = "copy edited";
            store.Update(copy);
            var reloaded = new LauncherScenarioStore(_folder);
            Assert.Equal("original", reloaded.Find(Guid.Parse(id))!.Name);
            Assert.Equal("copy edited", reloaded.Find(copy.Id)!.Name);
        }
    }
}
