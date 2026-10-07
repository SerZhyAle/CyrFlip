using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure resolver of the screenshots-folder chord (S0057 section 3.2): no disk, no clock -
    /// the probe and the remoteness test are arguments, and a remote path must never reach the
    /// probe (counted, the way the launcher's path tests count theirs).
    /// </summary>
    public class ScreenshotFolderTargetTests
    {
        private const string Screenshots = @"C:\Users\me\Pictures\Screenshots";
        private const string Downloads = @"C:\Users\me\Downloads";

        private sealed class NoDisk : IScreenshotFileSystem
        {
            public bool DirectoryExists(string path) => throw new NotSupportedException();
            public void CreateDirectory(string path) => throw new NotSupportedException();
            public bool FileExists(string path) => throw new NotSupportedException();
            public void WriteAllBytes(string path, byte[] bytes) => throw new NotSupportedException();
            public bool MoveNoReplace(string from, string to) => throw new NotSupportedException();
            public void DeleteFile(string path) => throw new NotSupportedException();
        }

        /// <summary>Counting probe: a set of folders that "exist", plus every path it was asked about.</summary>
        private sealed class Probes
        {
            public readonly List<string> Asked = new List<string>();
            public readonly HashSet<string> Existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool Exists(string path) { Asked.Add(path); return Existing.Contains(path); }
        }

        private static ScreenshotSaver Saver()
            => new ScreenshotSaver(new NoDisk(), () => Screenshots, () => Downloads, TimeSpan.FromSeconds(1));

        private static ScreenshotFolderTarget.Resolution Resolve(string configured, string? fallback,
            Probes probes, Func<string?, bool> isRemote)
            => ScreenshotFolderTarget.Resolve(configured, fallback, Saver().DisplayFolder, probes.Exists, isRemote);

        private static bool NeverRemote(string? path) => false;

        [Fact]
        public void AConfiguredFolderThatExistsOpensItself()
        {
            var probes = new Probes { Existing = { @"E:\shots" } };
            ScreenshotFolderTarget.Resolution resolution = Resolve(@"E:\shots", null, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, resolution.Outcome);
            Assert.Equal(@"E:\shots", resolution.Folder);
            Assert.Equal(@"E:\shots", resolution.Configured);
            Assert.Equal(new[] { @"E:\shots" }, probes.Asked);
        }

        [Fact]
        public void AGoneConfiguredFolderOpensTheRememberedFallback()
        {
            // D4: the folder of the last fallback save of this session, when it still exists.
            var probes = new Probes { Existing = { Screenshots } };
            ScreenshotFolderTarget.Resolution resolution = Resolve(@"E:\shots", Screenshots, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, resolution.Outcome);
            Assert.Equal(Screenshots, resolution.Folder);
        }

        [Fact]
        public void WithNoFallbackRememberedAGoneConfiguredFolderIsNamed()
        {
            // The decision off (D4) and nothing remembered are the same input: no fallback at all.
            var probes = new Probes();
            ScreenshotFolderTarget.Resolution resolution = Resolve(@"E:\shots", null, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Unavailable, resolution.Outcome);
            Assert.Equal(@"E:\shots", resolution.Configured);
            Assert.Null(resolution.Folder);
        }

        [Fact]
        public void AGoneConfiguredFolderWithAVanishedFallbackIsNamedToo()
        {
            var probes = new Probes();
            ScreenshotFolderTarget.Resolution resolution = Resolve(@"E:\shots", Downloads, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Unavailable, resolution.Outcome);
            Assert.Equal(@"E:\shots", resolution.Configured);
            Assert.Equal(new[] { @"E:\shots", Downloads }, probes.Asked);
        }

        [Fact]
        public void AnEmptyConfiguredFolderResolvesToTheScreenshotsKnownFolder()
        {
            var probes = new Probes { Existing = { Screenshots } };
            ScreenshotFolderTarget.Resolution resolution = Resolve("", null, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, resolution.Outcome);
            Assert.Equal(Screenshots, resolution.Folder);
        }

        [Fact]
        public void ADefaultFolderThatDoesNotExistYetIsTheNormalFirstRunState()
        {
            var probes = new Probes();
            ScreenshotFolderTarget.Resolution resolution = Resolve("", null, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.NotCreatedYet, resolution.Outcome);
            Assert.Null(resolution.Folder);
        }

        [Fact]
        public void ARemotePathIsNeverPassedToTheProbe()
        {
            var probes = new Probes { Existing = { Screenshots } };
            // A configured UNC share: handed to the shell as written, not probed at all.
            ScreenshotFolderTarget.Resolution remote = Resolve(@"\\server\shots", null, probes, path => true);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, remote.Outcome);
            Assert.Equal(@"\\server\shots", remote.Folder);
            Assert.Empty(probes.Asked);

            // A gone local folder with a remote fallback: only the local candidate is probed.
            ScreenshotFolderTarget.Resolution fallback = Resolve(@"E:\shots", @"\\server\downloads", probes, path => path!.StartsWith(@"\\"));
            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, fallback.Outcome);
            Assert.Equal(@"\\server\downloads", fallback.Folder);
            Assert.Equal(new[] { @"E:\shots" }, probes.Asked);
        }

        [Theory]
        [InlineData("E:\\shots\\", "E:\\shots")]
        [InlineData("E:\\", "E:\\")]
        public void TrailingSeparatorsAndDriveRootsComeOutAsDisplayFolderWritesThem(string stored, string shown)
        {
            var probes = new Probes { Existing = { shown } };
            ScreenshotFolderTarget.Resolution resolution = Resolve(stored, null, probes, NeverRemote);

            Assert.Equal(ScreenshotFolderTarget.Outcome.Open, resolution.Outcome);
            Assert.Equal(shown, resolution.Folder);
        }
    }
}
