using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The decorated icon files of the Jump List tasks that stand for a meaning (ticket S0022 A6), and the
    /// resolver's fallbacks that use them (<c>ICON-EXTERNAL</c> rules 2 and 4). Every test writes into its
    /// own temporary folder - never the real <c>icons\</c> beside the user's <c>layout.txt</c>.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class LauncherShortcutIconsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "cyrflip-icons-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        [Fact]
        public void AnIconIsWrittenOnceAsAFiveSizeIcoAndReusedAfterwards()
        {
            string? path = LauncherShortcutIcons.PathFor(AppGlyphs.Settings, _folder);
            Assert.NotNull(path);
            Assert.StartsWith(_folder, path!);
            Assert.Contains("decorated" + LauncherShortcutIcons.Revision + "-app.settings", Path.GetFileName(path));

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(0, BitConverter.ToInt16(bytes, 0));     // reserved
            Assert.Equal(1, BitConverter.ToInt16(bytes, 2));     // icon
            Assert.Equal(LauncherShortcutIcons.Sizes.Length, BitConverter.ToInt16(bytes, 4));
            for (int i = 0; i < LauncherShortcutIcons.Sizes.Length; i++)
            {
                int entry = 6 + 16 * i;
                Assert.Equal(LauncherShortcutIcons.Sizes[i], bytes[entry]);
                Assert.Equal(LauncherShortcutIcons.Sizes[i], bytes[entry + 1]);
                int length = BitConverter.ToInt32(bytes, entry + 8), offset = BitConverter.ToInt32(bytes, entry + 12);
                Assert.Equal(0x89, bytes[offset]);               // a PNG payload
                Assert.Equal((byte)'P', bytes[offset + 1]);
                Assert.True(offset + length <= bytes.Length);
            }

            DateTime written = File.GetLastWriteTimeUtc(path);
            Assert.Equal(path, LauncherShortcutIcons.PathFor(AppGlyphs.Settings, _folder));
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));   // reused, not rewritten
        }

        [Fact]
        public void AMissingFileIsRecreated()
        {
            string path = LauncherShortcutIcons.PathFor(AppGlyphs.Exit, _folder)!;
            File.Delete(path);
            Assert.Equal(path, LauncherShortcutIcons.PathFor(AppGlyphs.Exit, _folder));
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void TheWrittenFileIsAnIconWindowsCanLoad()
        {
            string path = LauncherShortcutIcons.PathFor(AppGlyphs.Download, _folder)!;
            using var icon = new Icon(path, 32, 32);
            Assert.Equal(32, icon.Width);
            using var icon16 = new Icon(path, 16, 16);
            Assert.Equal(16, icon16.Width);
        }

        [Fact]
        public void TheGlyphSitsOnAPlateInTheAccentAndHoldsThreeToOne()
        {
            Assert.True(ThemePalette.Contrast(LauncherShortcutIcons.OnPlate, LauncherShortcutIcons.Plate) >= 3.0);
            using Bitmap? plated = GlyphRenderer.RenderOnPlate(AppGlyphs.Apps, 48, LauncherShortcutIcons.Plate, LauncherShortcutIcons.OnPlate);
            Assert.NotNull(plated);
            // Inside the circle but clear of the glyph (0.6 of the plate, centred): the plate colour.
            Color onPlate = plated!.GetPixel(6, 24);
            Assert.True(Math.Abs(onPlate.R - LauncherShortcutIcons.Plate.R) <= 2 && Math.Abs(onPlate.B - LauncherShortcutIcons.Plate.B) <= 2, onPlate.ToString());
            // The corner of the square is outside a round plate: transparent.
            Assert.Equal(0, plated.GetPixel(0, 0).A);
        }

        [Theory]
        [InlineData("no.such.glyph")]
        [InlineData("../evil")]
        [InlineData("")]
        public void AnUnknownOrUnsafeIdWritesNothing(string id)
        {
            Assert.Null(LauncherShortcutIcons.PathFor(id, _folder));
            Assert.False(Directory.Exists(_folder));
        }

        [Fact]
        public void AFolderThatCannotBeUsedGivesNoIconAndNoException()
        {
            Directory.CreateDirectory(_folder);
            string blocker = Path.Combine(_folder, "icons");
            File.WriteAllText(blocker, "a file where the folder should be");
            Assert.Null(LauncherShortcutIcons.PathFor(AppGlyphs.Settings, blocker));
        }

        // -- the resolver -------------------------------------------------------------------------------

        private static LauncherPathProbes Probes() => new LauncherPathProbes
        {
            FileExists = _ => false,
            ResolveOnPath = _ => null,
            IsRemote = path => path.StartsWith(@"\\", StringComparison.Ordinal),
            VocabularyIcon = id => @"C:\icons\" + id + ".ico",
        };

        [Fact]
        public void AYtDlpScenarioIsTheDownloadGlyph()
        {
            var scenario = new LauncherScenario { Name = "dl", Type = LauncherScenarioType.YtDlp };
            Assert.Equal(@"C:\icons\action.download.ico", LauncherIconResolver.Resolve(scenario, Probes()).Path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("missing.exe")]
        [InlineData(@"C:\gone\tool.exe")]
        [InlineData(@"\\nas\share\tool.exe")]
        [InlineData(@"C:\gone\script.ps1")]
        [InlineData(@"C:\gone\file.xyz")]
        public void AProgramWhoseIconCannotBeReadIsTheAppsGlyphNeverTheAppsOwnMark(string path)
        {
            LauncherIcon icon = LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = path }, Probes());
            Assert.Equal(@"C:\icons\content.apps.ico", icon.Path);
        }

        [Fact]
        public void AProgramThatHasAnIconKeepsItAsTheOsReportsIt()
        {
            var probes = Probes();
            probes.FileExists = p => p == @"C:\Tools\app.exe";
            Assert.Equal(@"C:\Tools\app.exe", LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = @"C:\Tools\app.exe" }, probes).Path);
        }

        [Fact]
        public void WhenTheIconFileCannotBeWrittenTheIconIsEmptyAndNothingThrows()
        {
            var probes = Probes();
            probes.VocabularyIcon = _ => null;
            LauncherIcon icon = LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = "missing.exe" }, probes);
            Assert.Equal("", icon.Path);
        }
    }
}
