using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// SCENARIO-FILE 0.10, section 4 "Negative vectors" and section 7 items B, C and D: the six authored
    /// files of the catalog, pinned by SHA-256 (the catalog is not in git and CI has no copy of it), and
    /// the edges of "a value that does not parse makes the file unreadable" that the vectors do not
    /// cover. Five of the six must be skipped, counted and named - in the store and in the migration -
    /// and one must read. The files are authored, not generated, so the hash and the outcome are the
    /// whole definition.
    /// </summary>
    public class ScenarioFileNegativeVectorTests : IDisposable
    {
        private static readonly string Negative = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "OneClickRunner", "negative");

        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static readonly string[] Refused =
        {
            "unknown_type.xml", "renamed_root.xml", "bool_titlecase.xml", "empty_id.xml", "truncated.xml",
        };

        [Theory]
        [InlineData("unknown_type.xml", "a528f6c30c08d8317a990a56d0d5d84addc816ef6827f019ea49c40d6647ac08")]
        [InlineData("renamed_root.xml", "a84ee61bf531d39b24db877e66fd218185b577c29b431fa5c2e622b37a8b1dc7")]
        [InlineData("bool_titlecase.xml", "30518c4891192256883bb0201fc2ac43b9684fb27329d60f57b6784fa819caf2")]
        [InlineData("empty_id.xml", "0be8e0f380e08606d419c732d51af56bd226d548907af296fc264cf4af1b83a6")]
        [InlineData("truncated.xml", "69282e9e4ad64974eff29f0dbccf030eb378f711e6042ee07df405ffce26dbe6")]
        [InlineData("unknown_element.xml", "c1ad29d64d38337bab6ac90c449ef51f0ad68a1ea1cd52e7b83a1b8b82b18235")]
        public void TheNegativeFixturesAreByteIdenticalToTheCatalogVectors(string name, string sha256)
        {
            Assert.Equal(sha256, Sha256(Path.Combine(Negative, name)));
        }

        [Fact]
        public void ExactlySixNegativeFixturesAreHeld()
        {
            Assert.Equal(6, Directory.GetFiles(Negative, "*.xml").Length);
        }

        [Theory]
        [InlineData("unknown_type.xml")]
        [InlineData("renamed_root.xml")]
        [InlineData("bool_titlecase.xml")]
        [InlineData("empty_id.xml")]
        [InlineData("truncated.xml")]
        public void EachRefusedVectorIsRefusedByTheReader(string name)
        {
            Assert.Null(LauncherScenarioStore.TryRead(Path.Combine(Negative, name), out Exception? error));
            Assert.NotNull(error);
        }

        [Fact]
        public void TheRenamedRootIsAFormatRefusalAndTheTruncatedFileIsDamage()
        {
            LauncherScenarioStore.TryRead(Path.Combine(Negative, "renamed_root.xml"), out Exception? renamed);
            LauncherScenarioStore.TryRead(Path.Combine(Negative, "truncated.xml"), out Exception? truncated);

            Assert.IsType<ScenarioFormatException>(renamed);
            Assert.IsNotType<ScenarioFormatException>(truncated);
        }

        [Fact]
        public void TheUnknownElementVectorReads()
        {
            LauncherScenario? read = LauncherScenarioStore.TryRead(Path.Combine(Negative, "unknown_element.xml"), out Exception? error);

            Assert.Null(error);
            Assert.NotNull(read);
            Assert.Equal("Calculator", read!.Name);
            Assert.Equal("calc.exe", read.Path);
            Assert.Equal(Guid.Parse("a4d70b92-6e38-4c15-b9f0-3e8c1d5a7062"), read.Id);
        }

        [Fact]
        public void TheStoreSkipsCountsAndNamesTheFiveAndKeepsTheOne()
        {
            string folder = Stage("store");

            var store = new LauncherScenarioStore(folder);

            Assert.Equal(new[] { "unknown_element.xml" },
                store.All.Select(s => s.Filename).ToArray());
            Assert.Equal(Refused.OrderBy(n => n, StringComparer.Ordinal),
                store.LoadErrors.OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void TheMigrationSkipsCountsAndNamesTheFiveAndLeavesTheSourceAlone()
        {
            string source = Stage("source");
            var before = Directory.GetFiles(source).OrderBy(f => f, StringComparer.Ordinal)
                .ToDictionary(f => f, Sha256);

            LauncherMigration.Result result = LauncherMigration.Import(
                new LauncherScenarioStore(Path.Combine(_root, "target")), source);

            Assert.Equal(1, result.Imported);
            Assert.Equal(Refused.OrderBy(n => n, StringComparer.Ordinal),
                result.Skipped.OrderBy(n => n, StringComparer.Ordinal));
            foreach (var pair in before) Assert.Equal(pair.Value, Sha256(pair.Key));
        }

        // ---- Section 7 item C: a present value that does not parse, and the edges around it ----

        [Theory]
        [InlineData("<RunAsAdmin></RunAsAdmin>")]
        [InlineData("<RunAsAdmin>yes</RunAsAdmin>")]
        [InlineData("<Order></Order>")]
        [InlineData("<Order>first</Order>")]
        [InlineData("<Type></Type>")]
        [InlineData("<Type>2</Type>")]
        [InlineData("<Id></Id>")]
        [InlineData("<Id>not-a-guid</Id>")]
        public void AValueThatIsPresentButDoesNotParseMakesTheFileUnreadable(string element)
        {
            Assert.Null(LauncherScenarioStore.TryRead(WriteItem(element), out Exception? error));
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("<RunAsAdmin>true</RunAsAdmin>", true)]
        [InlineData("<RunAsAdmin>false</RunAsAdmin>", false)]
        [InlineData("<RunAsAdmin>1</RunAsAdmin>", true)]
        [InlineData("<RunAsAdmin>0</RunAsAdmin>", false)]
        public void ABooleanIsExactlyTrueFalseOneOrZero(string element, bool expected)
        {
            LauncherScenario? read = LauncherScenarioStore.TryRead(WriteItem(element), out Exception? error);

            Assert.Null(error);
            Assert.Equal(expected, read!.RunAsAdmin);
        }

        [Fact]
        public void AnEmptyElementIsAbsenceForStringElementsOnly()
        {
            LauncherScenario? read = LauncherScenarioStore.TryRead(
                WriteItem("<Arguments></Arguments><WorkingDirectory></WorkingDirectory><Hotkey></Hotkey>"),
                out Exception? error);

            Assert.Null(error);
            Assert.Equal(string.Empty, read!.Arguments);
            Assert.Equal(string.Empty, read.WorkingDirectory);
            Assert.Equal(string.Empty, read.Hotkey);
        }

        [Fact]
        public void ANegativeOrderReads()
        {
            LauncherScenario? read = LauncherScenarioStore.TryRead(WriteItem("<Order>-5</Order>"), out Exception? error);

            Assert.Null(error);
            Assert.Equal(-5, read!.Order);
        }

        [Fact]
        public void ARepeatedElementTheFirstOccurrenceWins()
        {
            LauncherScenario? read = LauncherScenarioStore.TryRead(
                WriteItem("<Name>second</Name><Name>third</Name>"), out Exception? error);

            Assert.Null(error);
            Assert.Equal("first", read!.Name);
        }

        // ---- Section 7 item D: any hyphenated 8-4-4-4-12 hexadecimal GUID, no version nibble check ----

        [Theory]
        [InlineData("c7d3a9e2-4b5f-6c8d-9e0f-1a2b3c4d5e6f")]   // the vectors' shape: version nibble 6
        [InlineData("00000000-0000-0000-0000-000000000001")]
        [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
        [InlineData("12345678-1234-1234-1234-123456789abc")]
        public void AnyHyphenatedHexadecimalGuidIsAnIdentity(string id)
        {
            string file = WriteRaw("<AppItem><Id>" + id + "</Id><Name>x</Name></AppItem>");

            LauncherScenario? read = LauncherScenarioStore.TryRead(file, out Exception? error);

            Assert.Null(error);
            Assert.Equal(Guid.Parse(id), read!.Id);
        }

        // ---- helpers ----

        /// <summary>A scenario whose Name is "first" followed by the given elements, in a fresh file.</summary>
        private string WriteItem(string elements)
            => WriteRaw("<AppItem><Name>first</Name>" + elements + "</AppItem>");

        private string WriteRaw(string xml)
        {
            Directory.CreateDirectory(_root);
            string file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + xml);
            return file;
        }

        /// <summary>The six negative vectors copied into a folder of their own, so a store or a migration can read them.</summary>
        private string Stage(string name)
        {
            string folder = Path.Combine(_root, name);
            Directory.CreateDirectory(folder);
            foreach (string file in Directory.GetFiles(Negative, "*.xml"))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            return folder;
        }

        private static string Sha256(string file)
        {
            using var sha = SHA256.Create();
            using FileStream stream = File.OpenRead(file);
            return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
    }
}
