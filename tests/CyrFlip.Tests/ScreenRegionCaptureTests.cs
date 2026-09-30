using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0026: the rectangle math of the region capture - four drag directions, a virtual screen
    /// with a negative origin, click versus drag, and a selection across two monitors of different
    /// size. Pure; no desktop.
    /// </summary>
    public class ScreenCaptureTests
    {
        [Theory]
        [InlineData(10, 20, 110, 70)]   // down-right
        [InlineData(110, 70, 10, 20)]   // up-left
        [InlineData(110, 20, 10, 70)]   // down-left
        [InlineData(10, 70, 110, 20)]   // up-right
        public void EveryDragDirectionGivesTheSameRectangle(int x1, int y1, int x2, int y2)
        {
            Rectangle rect = ScreenCapture.Normalize(new Point(x1, y1), new Point(x2, y2));
            Assert.Equal(Rectangle.FromLTRB(10, 20, 110, 70), rect);
        }

        [Fact]
        public void ClampingHonoursANegativeOrigin()
        {
            // A monitor left of and above the primary: the virtual screen starts at (-1920, -300).
            var virtualScreen = new Rectangle(-1920, -300, 1920 + 2560, 1440 + 300);
            Rectangle clamped = ScreenCapture.ClampToVirtualScreen(Rectangle.FromLTRB(-2000, -400, -1800, -200), virtualScreen);
            Assert.Equal(Rectangle.FromLTRB(-1920, -300, -1800, -200), clamped);
            Assert.Equal(Rectangle.Empty, ScreenCapture.ClampToVirtualScreen(new Rectangle(-3000, 0, 50, 50), virtualScreen));
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(1, 1, true)]
        [InlineData(2, 2, true)]
        [InlineData(3, 3, false)]
        [InlineData(2, 40, false)]
        [InlineData(40, 1, false)]
        public void OnlyATinyRectangleIsAClick(int width, int height, bool click)
            => Assert.Equal(click, ScreenCapture.IsClick(new Rectangle(5, 5, width, height)));

        [Fact]
        public void ASelectionAcrossTwoMonitorsIsExactlyTheirTwoSlices()
        {
            var left = new Rectangle(-1920, 0, 1920, 1080);   // 100 %
            var right = new Rectangle(0, -200, 3840, 2160);   // a 4K monitor at 150 %, taller and offset
            var selection = Rectangle.FromLTRB(-500, 100, 700, 900);

            Rectangle a = ScreenCapture.MonitorSlice(selection, left);
            Rectangle b = ScreenCapture.MonitorSlice(selection, right);

            Assert.Equal(Rectangle.FromLTRB(1420, 100, 1920, 900), a); // local to the left monitor
            Assert.Equal(Rectangle.FromLTRB(0, 300, 700, 1100), b);    // local to the right monitor
            Assert.Equal(selection.Width * selection.Height, a.Width * a.Height + b.Width * b.Height);
            Assert.Equal(Rectangle.Empty, ScreenCapture.MonitorSlice(selection, new Rectangle(5000, 0, 100, 100)));
        }

        [Fact]
        public void TheCropIsAnExactCopyOfTheRegion()
        {
            var virtualScreen = new Rectangle(-100, -50, 300, 200);
            using Bitmap frame = Pattern(virtualScreen.Width, virtualScreen.Height);
            // The region in virtual-screen coordinates: frame pixel (x, y) is screen (x - 100, y - 50).
            var region = new Rectangle(-80, -40, 37, 11);
            using Bitmap crop = ScreenCapture.Crop(frame, virtualScreen, region);

            Assert.Equal(new Size(37, 11), crop.Size);
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    Assert.Equal(frame.GetPixel(x + 20, y + 10).ToArgb(), crop.GetPixel(x, y).ToArgb());
        }

        /// <summary>Every pixel distinct: red = x, green = y, blue = a mix of both.</summary>
        internal static Bitmap Pattern(int width, int height)
        {
            var image = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    image.SetPixel(x, y, Color.FromArgb(255, x & 0xFF, y & 0xFF, (x * 7 + y * 13) & 0xFF));
            return image;
        }
    }

    /// <summary>Ticket S0026 5.2: the PNG decodes to the same pixels, the DIB is a bottom-up 32bpp BI_RGB.</summary>
    public class ClipboardImageTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 9)]    // a one-pixel-wide region survives both encodings
        [InlineData(33, 17)]
        public void ThePngDecodesBackToTheSamePixels(int width, int height)
        {
            using Bitmap image = ScreenCaptureTests.Pattern(width, height);
            byte[] png = ClipboardImage.EncodePng(image);

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
            using var stream = new MemoryStream(png);
            using var decoded = new Bitmap(stream);
            Assert.Equal(image.Size, decoded.Size);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color c = decoded.GetPixel(x, y);
                    Assert.Equal(255, c.A);
                    Assert.Equal(image.GetPixel(x, y).ToArgb(), c.ToArgb());
                }
        }

        [Theory]
        [InlineData(1, 9)]
        [InlineData(33, 17)]
        public void TheDibIsBottomUpWithAlignedRows(int width, int height)
        {
            using Bitmap image = ScreenCaptureTests.Pattern(width, height);
            byte[] dib = ClipboardImage.EncodeDib(image);

            Assert.Equal(ClipboardImage.HeaderSize, BitConverter.ToInt32(dib, 0));
            Assert.Equal(width, BitConverter.ToInt32(dib, 4));
            Assert.Equal(height, BitConverter.ToInt32(dib, 8));        // positive: bottom-up
            Assert.Equal(1, BitConverter.ToInt16(dib, 12));
            Assert.Equal(32, BitConverter.ToInt16(dib, 14));
            Assert.Equal(0, BitConverter.ToInt32(dib, 16));            // BI_RGB
            int stride = width * 4;
            Assert.Equal(0, stride % 4);
            Assert.Equal(ClipboardImage.HeaderSize + stride * height, dib.Length);

            // The first row in memory is the image's bottom row; bytes are B, G, R, A.
            Color bottomLeft = image.GetPixel(0, height - 1);
            int first = ClipboardImage.HeaderSize;
            Assert.Equal(bottomLeft.B, dib[first]);
            Assert.Equal(bottomLeft.G, dib[first + 1]);
            Assert.Equal(bottomLeft.R, dib[first + 2]);
            Assert.Equal(0xFF, dib[first + 3]);
            Color topRight = image.GetPixel(width - 1, 0);
            int last = ClipboardImage.HeaderSize + (height - 1) * stride + (width - 1) * 4;
            Assert.Equal(topRight.B, dib[last]);
            Assert.Equal(topRight.G, dib[last + 1]);
            Assert.Equal(topRight.R, dib[last + 2]);
        }
    }

    /// <summary>
    /// Ticket S0026 5.4 against <c>CAPTURE-OUTPUT</c> 0.1 (kind <c>screenshot</c>). The first test is the
    /// contract's conformance step 1: the name at a fixed instant in a non-Latin-digit locale, the
    /// ordinal included. The rest hold rules 5, 6, 9-12 on an in-memory file system.
    /// </summary>
    public class ScreenshotSaverTests
    {
        private static readonly DateTime Instant = new DateTime(2026, 9, 26, 14, 5, 33, DateTimeKind.Local);
        private const string Screenshots = @"C:\Users\u\Pictures\Screenshots";
        private const string Downloads = @"C:\Users\u\Downloads";
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        private sealed class FakeFs : IScreenshotFileSystem
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Created = new List<string>();
            /// <summary>Names another writer takes between our check and our rename (a lost race).</summary>
            public readonly HashSet<string> RaceFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool FailWrite;

            private void Reach(string path)
            {
                foreach (string bad in Unreachable)
                    if (path.StartsWith(bad, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("unreachable");
            }

            public bool DirectoryExists(string path) { Reach(path); return Folders.Contains(path); }
            public void CreateDirectory(string path) { Reach(path); Folders.Add(path); Created.Add(path); }
            public bool FileExists(string path) { Reach(path); return Files.ContainsKey(path); }
            public void WriteAllBytes(string path, byte[] bytes)
            {
                Reach(path);
                if (Files.ContainsKey(path)) throw new IOException("exists");
                Files[path] = new byte[] { 0 }; // a fragment first, as a real write would leave it
                if (FailWrite) throw new IOException("disk full");
                Files[path] = (byte[])bytes.Clone();
            }
            public bool MoveNoReplace(string from, string to)
            {
                Reach(to);
                if (RaceFor.Remove(to)) Files[to] = new byte[] { 9 };
                if (Files.ContainsKey(to)) return false;
                Files[to] = Files[from];
                Files.Remove(from);
                return true;
            }
            public void DeleteFile(string path) => Files.Remove(path);
        }

        private static ScreenshotSaver Saver(FakeFs fs, string? screenshots = Screenshots)
            => new ScreenshotSaver(fs, () => screenshots, () => Downloads, TimeSpan.FromSeconds(5));

        [Theory]
        [InlineData("ar-SA")]
        [InlineData("hi-IN")]
        [InlineData("bn-BD")]
        [InlineData("ru-RU")]
        public void TheNameIsTheContractsPatternWithAsciiDigitsInAnyCulture(string culture)
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture, beforeUi = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var target = CultureInfo.GetCultureInfo(culture);
                Thread.CurrentThread.CurrentCulture = target;
                Thread.CurrentThread.CurrentUICulture = target;

                Assert.Equal("screenshot_260926_140533", ScreenshotSaver.Stem(Instant));
                Assert.Equal("screenshot_260926_140533.png", ScreenshotSaver.FileName(ScreenshotSaver.Stem(Instant), 1));
                Assert.Equal("screenshot_260926_140533 (2).png", ScreenshotSaver.FileName(ScreenshotSaver.Stem(Instant), 2));
                Assert.Equal("screenshot_260926_140533 (12).png", ScreenshotSaver.FileName(ScreenshotSaver.Stem(Instant), 12));

                var fs = new FakeFs();
                ScreenshotSaver.Result result = Saver(fs).Save(Png, Instant, "");
                Assert.Equal(Path.Combine(Screenshots, "screenshot_260926_140533.png"), result.FilePath);
                Assert.Matches("^[\\x20-\\x7E]+$", Path.GetFileName(result.FilePath));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
                Thread.CurrentThread.CurrentUICulture = beforeUi;
            }
        }

        [Fact]
        public void TheInstantIsTheFreezeNotTheWrite()
        {
            // The caller hands the freeze time in; nothing in the saver reads the clock for the name.
            var fs = new FakeFs();
            ScreenshotSaver.Result result = Saver(fs).Save(Png, new DateTime(2020, 1, 2, 3, 4, 5), "");
            Assert.EndsWith("screenshot_200102_030405.png", result.FilePath);
        }

        [Fact]
        public void AnExistingNameThisProcessNeverWroteGetsTheNextOrdinal()
        {
            var fs = new FakeFs();
            fs.Folders.Add(Screenshots);
            fs.Files[Path.Combine(Screenshots, "screenshot_260926_140533.png")] = new byte[] { 7 };
            ScreenshotSaver saver = Saver(fs);

            Assert.Equal(Path.Combine(Screenshots, "screenshot_260926_140533 (2).png"), saver.Save(Png, Instant, "").FilePath);
            Assert.Equal(Path.Combine(Screenshots, "screenshot_260926_140533 (3).png"), saver.Save(Png, Instant, "").FilePath);
            // The file somebody else wrote is untouched.
            Assert.Equal(new byte[] { 7 }, fs.Files[Path.Combine(Screenshots, "screenshot_260926_140533.png")]);
        }

        [Fact]
        public void ALostRenameRaceMovesToTheNextOrdinalAndOverwritesNothing()
        {
            var fs = new FakeFs();
            string first = Path.Combine(Screenshots, "screenshot_260926_140533.png");
            fs.RaceFor.Add(first);
            ScreenshotSaver.Result result = Saver(fs).Save(Png, Instant, "");

            Assert.Equal(Path.Combine(Screenshots, "screenshot_260926_140533 (2).png"), result.FilePath);
            Assert.Equal(new byte[] { 9 }, fs.Files[first]);
            Assert.Equal(Png, fs.Files[result.FilePath!]);
        }

        [Fact]
        public void AnEmptySettingResolvesToTheScreenshotsKnownFolder()
        {
            var fs = new FakeFs();
            ScreenshotSaver saver = Saver(fs);
            Assert.Equal(Screenshots, saver.DisplayFolder(""));
            Assert.Equal(Screenshots, saver.DisplayFolder("   "));
            ScreenshotSaver.Result result = saver.Save(Png, Instant, "");
            Assert.Equal(ScreenshotSaver.Outcome.Saved, result.Outcome);
            Assert.Equal(Screenshots, result.Folder);
            Assert.Contains(Screenshots, fs.Created); // created on the first save
        }

        [Fact]
        public void TheUsersChoiceWins()
        {
            var fs = new FakeFs();
            ScreenshotSaver.Result result = Saver(fs).Save(Png, Instant, @"E:\Shots");
            Assert.Equal(ScreenshotSaver.Outcome.Saved, result.Outcome);
            Assert.Equal(@"E:\Shots", result.Folder);
        }

        [Fact]
        public void AnUnreachableChoiceFallsBackToScreenshotsThenDownloadsAndSaysWhere()
        {
            var fs = new FakeFs();
            fs.Unreachable.Add(@"E:\");
            ScreenshotSaver.Result toScreenshots = Saver(fs).Save(Png, Instant, @"E:\Shots");
            Assert.Equal(ScreenshotSaver.Outcome.SavedToFallback, toScreenshots.Outcome);
            Assert.Equal(Screenshots, toScreenshots.Folder);

            fs.Unreachable.Add(Screenshots);
            ScreenshotSaver.Result toDownloads = Saver(fs).Save(Png, Instant, @"E:\Shots");
            Assert.Equal(ScreenshotSaver.Outcome.SavedToFallback, toDownloads.Outcome);
            Assert.Equal(Downloads, toDownloads.Folder);

            // The default itself unreachable is a fallback too - to Downloads, and nowhere else.
            ScreenshotSaver.Result fromDefault = Saver(fs).Save(Png, Instant, "");
            Assert.Equal(ScreenshotSaver.Outcome.SavedToFallback, fromDefault.Outcome);
            Assert.Equal(Downloads, fromDefault.Folder);

            fs.Unreachable.Add(Downloads);
            ScreenshotSaver.Result none = Saver(fs).Save(Png, Instant, @"E:\Shots");
            Assert.Equal(ScreenshotSaver.Outcome.Failed, none.Outcome);
            Assert.Null(none.FilePath);
        }

        [Fact]
        public void TheFileHoldsExactlyTheClipboardPngBytes()
        {
            var fs = new FakeFs();
            ScreenshotSaver.Result result = Saver(fs).Save(Png, Instant, "");
            Assert.Equal(Png, fs.Files[result.FilePath!]);
        }

        [Fact]
        public void AFailedWriteLeavesNoTemporaryAndNoFinalFile()
        {
            var fs = new FakeFs { FailWrite = true };
            ScreenshotSaver.Result result = Saver(fs).Save(Png, Instant, "");
            Assert.Equal(ScreenshotSaver.Outcome.Failed, result.Outcome);
            Assert.Empty(fs.Files);
        }

        [Fact]
        public void NoTemporaryNameSurvivesASuccessfulSave()
        {
            var fs = new FakeFs();
            Saver(fs).Save(Png, Instant, "");
            Assert.Single(fs.Files);
            Assert.EndsWith(".png", fs.Files.Keys.Single());
        }

        [Fact]
        public void TheChainHasNoDuplicatesAndNothingElse()
        {
            ScreenshotSaver saver = Saver(new FakeFs());
            Assert.Equal(new[] { Screenshots, Downloads }, saver.Chain(Screenshots + "\\"));
            Assert.Equal(new[] { @"D:\x", Screenshots, Downloads }, saver.Chain(@"D:\x"));
        }

        [Fact]
        public void NothingIsWrittenOrCreatedWhileSavingIsOff()
        {
            // Saving off means the saver is never called: the config says so by default, and the
            // settings page only shows a folder, it never creates one.
            var cfg = AppConfig.LoadFrom(new EmptyKey(), out _);
            Assert.False(cfg.ScreenshotSaveEnabled);
            Assert.Equal("", cfg.ScreenshotFolder);
        }

        private sealed class EmptyKey : IConfigKey
        {
            public object? GetValue(string name) => null;
            public void SetValue(string name, object value, RegistryValueKind kind) { }
        }
    }

    /// <summary>
    /// Ticket S0026 sections 3.1, 6 and 10: no module switch (owner, 2026-09-26) - the chord has its own
    /// switch, saving is off on a fresh install, and the chord parses.
    /// </summary>
    public class GraphicsModuleConfigTests
    {
        private sealed class MemoryKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        [Fact]
        public void AFreshInstallHasTheChordOnAndSavingOff()
        {
            AppConfig cfg = AppConfig.LoadFrom(new MemoryKey(), out _);
            Assert.False(cfg.ScreenshotSaveEnabled);
            Assert.Equal("Ctrl+Shift+PrintScreen", cfg.ScreenshotHotkey);
            Assert.True(cfg.EnableScreenshotHotkey);
            Assert.Equal(0, cfg.ScreenshotCount);
        }

        [Fact]
        public void TheFiveValuesRoundTrip()
        {
            var key = new MemoryKey();
            AppConfig cfg = AppConfig.LoadFrom(key, out _);
            cfg.ScreenshotHotkey = "Ctrl+Shift+F9";
            cfg.EnableScreenshotHotkey = false;
            cfg.ScreenshotSaveEnabled = true;
            cfg.ScreenshotFolder = @"D:\Shots";
            cfg.ScreenshotCount = 4;
            cfg.SaveTo(key);

            AppConfig back = AppConfig.LoadFrom(key, out _);
            Assert.False(key.Values.ContainsKey("EnableGraphics"));
            Assert.Equal("Ctrl+Shift+F9", back.ScreenshotHotkey);
            Assert.False(back.EnableScreenshotHotkey);
            Assert.True(back.ScreenshotSaveEnabled);
            Assert.Equal(@"D:\Shots", back.ScreenshotFolder);
            Assert.Equal(4, back.ScreenshotCount);
        }

        [Theory]
        [InlineData("Ctrl+Shift+PrintScreen")]
        [InlineData("Ctrl+Shift+PrtSc")]
        [InlineData("ctrl+shift+prtscn")]
        public void PrintScreenParsesAndRoundTrips(string text)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey chord));
            Assert.Equal(0x2C, chord.Vk);
            Assert.Equal("Ctrl+Shift+PrintScreen", chord.Display);
            Assert.True(Hotkey.TryParse(chord.Display, out Hotkey again));
            Assert.True(chord.SameChord(again));
            Assert.Equal("PrintScreen", Hotkey.NameForVk(0x2C));
        }

        [Fact]
        public void TheCaptureChordIsOwnedInTheRegistry()
        {
            AppConfig cfg = AppConfig.LoadFrom(new MemoryKey(), out _);
            ChordRegistry registry = ChordRegistry.Build(cfg, new LauncherScenario[0], new LanguageHotkeys.Entry[0]);
            Hotkey chord = Hotkey.Parse("Ctrl+Shift+PrintScreen");
            ChordOwner? owner = registry.CyrFlipOwnerOf(chord, ChordKind.Case);
            Assert.NotNull(owner);
            Assert.Equal(ChordKind.Screenshot, owner!.Kind);
            Assert.Null(registry.CyrFlipOwnerOf(chord, ChordKind.Screenshot));
        }
    }
}
