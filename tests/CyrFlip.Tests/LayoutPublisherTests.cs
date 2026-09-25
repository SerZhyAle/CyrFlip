using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What <c>layout.txt</c> and <c>layout-klid.txt</c> hold, byte for byte - the producing half of
    /// <c>LAYOUT-SIGNAL</c>. Every installed copy of the VS Code extension reads these two files, and the
    /// extension ships on its own clock, so their shape is the one thing that cannot change quietly.
    /// Until this class existed the rules were held by reading the code alone.
    /// </summary>
    public sealed class LayoutPublisherTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "CyrFlipLayoutTests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string CodeFile => Path.Combine(_dir, LayoutPublisher.CodeFileName);
        private string KlidFile => Path.Combine(_dir, LayoutPublisher.KlidFileName);

        [Fact]
        public void TheTwoFilesHoldExactlyTheCodeAndTheKlid()
        {
            LayoutPublisher.WriteNow(_dir, "EN", "00000409");

            string[] names = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
            Assert.Equal(new[] { "layout-klid.txt", "layout.txt" }, names);
            // ASCII, no BOM, no newline: the extension trims, but an older copy may not have.
            Assert.Equal(new byte[] { 0x45, 0x4E }, File.ReadAllBytes(CodeFile));
            Assert.Equal(new byte[] { 0x30, 0x30, 0x30, 0x30, 0x30, 0x34, 0x30, 0x39 }, File.ReadAllBytes(KlidFile));

            // The optional fixture pair for the contract's conformance ladder - generated, never hand-edited.
            string? vectors = Environment.GetEnvironmentVariable("CYRFLIP_WRITE_LAYOUT_VECTORS");
            if (!string.IsNullOrEmpty(vectors))
            {
                Directory.CreateDirectory(vectors);
                File.Copy(CodeFile, Path.Combine(vectors, LayoutPublisher.CodeFileName), overwrite: true);
                File.Copy(KlidFile, Path.Combine(vectors, LayoutPublisher.KlidFileName), overwrite: true);
            }
        }

        [Fact]
        public void ANewValueReplacesNeverAppends()
        {
            LayoutPublisher.WriteNow(_dir, "RU", "00000419");
            LayoutPublisher.WriteNow(_dir, "EN", "00000409");

            Assert.Equal("EN", File.ReadAllText(CodeFile));
            Assert.Equal(2, new FileInfo(CodeFile).Length);
            Assert.Equal("00000409", File.ReadAllText(KlidFile));
        }

        /// <summary>A layout the app cannot resolve still gets a sidecar - an empty one, which the
        /// consumer treats exactly like a missing one (rung 2 of the palette ladder).</summary>
        [Fact]
        public void AnUnknownKlidWritesAnEmptySidecar()
        {
            LayoutPublisher.WriteNow(_dir, "PL", "");

            Assert.True(File.Exists(KlidFile));
            Assert.Equal(0, new FileInfo(KlidFile).Length);
        }

        /// <summary>
        /// The consumer keeps the first four characters. Windows names a few languages with three letters
        /// only (Hawaiian is <c>HAW</c>), and one it cannot name comes back as four hex digits - both fit.
        /// </summary>
        [Fact]
        public void EveryCodeTheProducerCanEmitFitsTheConsumersPrefix()
        {
            var lcids = new HashSet<int>(CultureInfo.GetCultures(CultureTypes.AllCultures).Select(c => c.LCID))
            {
                0, 0x7F, 0x2000, 0xFFFF,
            };

            foreach (int lcid in lcids)
            {
                string code = WorldLayouts.CodeForLangId(lcid);
                Assert.True(code.Length >= 2 && code.Length <= 4, "0x" + lcid.ToString("X4") + " -> '" + code + "'");
                Assert.True(code.All(ch => ch < 128), "0x" + lcid.ToString("X4") + " -> non-ASCII '" + code + "'");
                Assert.Equal(code.ToUpperInvariant(), code);
                Assert.NotEqual("??", code);
            }

            Assert.Equal("HAW", WorldLayouts.CodeForLangId(0x0475));
        }

        [Fact]
        public void TheFolderFollowsTheInstallMode()
        {
            Assert.Equal(@"C:\Users\a\AppData\Local\CyrFlip",
                DataFolder.For(false, @"C:\Users\a\AppData\Local", "SZA.CyrFlip_fdk7e19xt9z9j"));
            // Packaged: the package's own per-user folder by its real path - never the machine-wide
            // %ProgramData% (LAYOUT-SIGNAL 1.1 rule 1, ticket S0016).
            Assert.Equal(@"C:\Users\a\AppData\Local\Packages\SZA.CyrFlip_fdk7e19xt9z9j\LocalCache\Local\CyrFlip",
                DataFolder.For(true, @"C:\Users\a\AppData\Local", "SZA.CyrFlip_fdk7e19xt9z9j"));
            // A family name that could not be read stays per user rather than going machine-wide.
            Assert.Equal(@"C:\Users\a\AppData\Local\CyrFlip", DataFolder.For(true, @"C:\Users\a\AppData\Local", ""));
            Assert.Equal(@"C:\ProgramData\CyrFlip", DataFolder.LegacySharedFor(@"C:\ProgramData"));
        }

        [Fact]
        public void TheClaimIsReadFromTheSameFolder()
        {
            Assert.Equal(LayoutPublisher.Folder, Path.GetDirectoryName(EditorCaretSignal.FilePath));
            Assert.Equal(DataFolder.Current, LayoutPublisher.Folder);
            Assert.Equal(DataFolder.Current, DiagnosticLog.ProductionFolder);
            Assert.Equal(DataFolder.Current, SupportBundle.LogDirectory);
            // Unpackaged (the test runner) there is no mirror, so the claim is read from one place only.
            Assert.Equal(PackageInfo.IsPackaged, LayoutPublisher.MirrorFolder != null);
            Assert.Equal(EditorCaretSignal.FilePath, EditorCaretSignal.FilePaths[0]);
            Assert.Equal(LayoutPublisher.MirrorFolder == null ? 1 : 2, EditorCaretSignal.FilePaths.Length);
        }

        /// <summary>LAYOUT-SIGNAL 1.1 rule 1: the packaged build mirrors both files into the deprecated
        /// machine-wide folder so a pre-1.1 extension keeps working, and retracts the mirror with them.</summary>
        [Fact]
        public void TheMirrorCarriesBothFilesAndGoesWithThem()
        {
            string mirror = Path.Combine(_dir, "mirror");
            string primary = Path.Combine(_dir, "primary");
            var channel = new LayoutPublisher.Channel(primary, mirror);
            channel.Publish("UK", "00000422");
            Assert.True(channel.Flush(TimeSpan.FromSeconds(10)));

            foreach (string folder in new[] { primary, mirror })
            {
                Assert.Equal("UK", File.ReadAllText(Path.Combine(folder, LayoutPublisher.CodeFileName)));
                Assert.Equal("00000422", File.ReadAllText(Path.Combine(folder, LayoutPublisher.KlidFileName)));
            }

            channel.Retract();
            foreach (string folder in new[] { primary, mirror })
            {
                Assert.False(File.Exists(Path.Combine(folder, LayoutPublisher.CodeFileName)));
                Assert.False(File.Exists(Path.Combine(folder, LayoutPublisher.KlidFileName)));
            }
        }

        /// <summary>On a machine where another account created the mirror first, its writes fail - and the
        /// primary folder must not notice.</summary>
        [Fact]
        public void AMirrorThatCannotBeWrittenCostsOnlyItself()
        {
            Directory.CreateDirectory(_dir);
            string blocked = Path.Combine(_dir, "blocked");
            File.WriteAllText(blocked, "a file where the mirror folder should be");
            string primary = Path.Combine(_dir, "primary");
            var channel = new LayoutPublisher.Channel(primary, blocked);
            channel.Publish("EN", "00000409");
            Assert.True(channel.Flush(TimeSpan.FromSeconds(10)));

            Assert.Equal("EN", File.ReadAllText(Path.Combine(primary, LayoutPublisher.CodeFileName)));
        }

        /// <summary>The producer writes only on change, so an older value left on disk by two racing
        /// writes would never heal. Whatever the interleaving, the last publish is what stays.</summary>
        [Fact]
        public void TheLastPublishWins()
        {
            var channel = new LayoutPublisher.Channel(_dir);
            for (int i = 1; i <= 200; i++)
                channel.Publish(i % 2 == 0 ? "EN" : "RU", i.ToString("D8"));

            Assert.True(channel.Flush(TimeSpan.FromSeconds(10)), "the writer never settled");
            Assert.Equal("EN", File.ReadAllText(CodeFile));
            Assert.Equal("00000200", File.ReadAllText(KlidFile));
        }

        [Fact]
        public void AFailedWriteIsSwallowed()
        {
            Directory.CreateDirectory(_dir);
            string blocker = Path.Combine(_dir, "not-a-folder");
            File.WriteAllText(blocker, "x");

            LayoutPublisher.WriteNow(blocker, "EN", "00000409"); // the "folder" is a file: must not throw
        }

        /// <summary>After a clean exit, absence means "not running" - but only the producer's own files
        /// go. The claim file belongs to the extension, and nothing else in the folder is ours to touch.</summary>
        [Fact]
        public void ACleanExitRemovesBothFiles()
        {
            var channel = new LayoutPublisher.Channel(_dir);
            channel.Publish("EN", "00000409");
            Assert.True(channel.Flush(TimeSpan.FromSeconds(10)));
            string claim = Path.Combine(_dir, EditorCaretSignal.ClaimFileName);
            string other = Path.Combine(_dir, "launcher.log");
            File.WriteAllText(claim, "EN 2026-09-24T00:00:00Z");
            File.WriteAllText(other, "keep me");

            channel.Retract();
            channel.Publish("RU", "00000419"); // a late layout change after exit must not bring them back
            Assert.True(channel.Flush(TimeSpan.FromSeconds(10)));

            Assert.False(File.Exists(CodeFile));
            Assert.False(File.Exists(KlidFile));
            Assert.True(File.Exists(claim));
            Assert.True(File.Exists(other));
        }
    }
}
