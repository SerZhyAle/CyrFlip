using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What the send reads and what the get writes (<c>CLIPBOARD-GUARD</c> items M to R). Everything runs over
    /// <see cref="FakeClipboard"/>: no test ever opens the real clipboard.
    /// </summary>
    public class ExchangeClipboardIoTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cyrflip-exio-" + Guid.NewGuid().ToString("N"));

        public ExchangeClipboardIoTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s + "\0");

        private string File1(string name, string content = "hello")
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static ExchangeOutcome<ExchangeClipboardPayload> Read(FakeClipboard c, long cap = 1024 * 1024) => ExchangeClipboardSource.Read(c, cap);

        // ---------------------------------------------------------------- the read

        [Fact]
        public void ATextIsSentAsUtf8WithLineFeedsAlone()
        {
            var c = new FakeClipboard().With(WindowInterop.CF_UNICODETEXT, Utf16("one\r\ntwo\rthree\nfour"));
            ExchangeOutcome<ExchangeClipboardPayload> r = Read(c);
            Assert.True(r.Ok);
            ExchangeClipboardPayload p = r.Value!;
            Assert.Equal("text", p.Kind);
            Assert.Equal("one\ntwo\nthree\nfour", Encoding.UTF8.GetString(p.Bytes!));
            Assert.Equal(p.Bytes!.Length, p.ClearSize);
            Assert.Equal("", p.MimeType);
            Assert.Equal("", p.Name);
        }

        [Fact]
        public void ATextWithNonLatinLettersKeepsEveryCharacter()
        {
            string text = "Привіт, світе 你好 🙂";
            ExchangeClipboardPayload p = Read(new FakeClipboard().With(WindowInterop.CF_UNICODETEXT, Utf16(text))).Value!;
            Assert.Equal(text, Encoding.UTF8.GetString(p.Bytes!));
        }

        [Fact]
        public void TheClipboardIsOpenedOnceAndClosedBeforeAnyFileIsLookedAt()
        {
            string path = File1("a.txt");
            var c = new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(path));
            ExchangeOutcome<ExchangeClipboardPayload> r = Read(c);
            Assert.True(r.Ok);
            Assert.Equal(1, c.Opens);
            Assert.False(c.IsOpen);
        }

        [Fact]
        public void AOneFileSelectionIsTheFileByItsPathAndItsContentIsNotReadHere()
        {
            string path = File1("report.txt", "12345");
            ExchangeClipboardPayload p = Read(new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(path))).Value!;
            Assert.Equal("file", p.Kind);
            Assert.Equal("report.txt", p.Name);
            Assert.Equal("text/plain", p.MimeType);
            Assert.Equal(5, p.ClearSize);
            Assert.Equal(path, p.FilePath);
            Assert.Null(p.Bytes); // streamed from disk after the clipboard is closed
            using (Stream s = p.OpenClear())
                Assert.Equal(5, s.Length);
        }

        [Fact]
        public void AFileWinsOverTheTextAndTheTextOverThePicture()
        {
            string path = File1("f.bin");
            var all = new FakeClipboard()
                .With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(path))
                .With(WindowInterop.CF_UNICODETEXT, Utf16("a text"))
                .With(WindowInterop.CF_DIB, new byte[100]);
            Assert.Equal("file", Read(all).Value!.Kind);

            var textAndPicture = new FakeClipboard()
                .With(WindowInterop.CF_UNICODETEXT, Utf16("a text"))
                .With(WindowInterop.CF_DIB, new byte[100]);
            Assert.Equal("text", Read(textAndPicture).Value!.Kind);
        }

        [Fact]
        public void SeveralFilesAreRefusedAsSeveralObjects()
        {
            string a = File1("a.txt"), b = File1("b.txt");
            var c = new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(a, b));
            ExchangeOutcome<ExchangeClipboardPayload> r = Read(c);
            Assert.False(r.Ok);
            Assert.Equal(ExchangeCodes.SeveralObjects, r.Code);
        }

        [Fact]
        public void AFolderIsRefusedAsSeveralObjects()
        {
            var c = new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(_dir));
            Assert.Equal(ExchangeCodes.SeveralObjects, Read(c).Code);
        }

        [Fact]
        public void AFileThatIsGoneOrEmptyIsNothingToSendAndOneOverTheCapNamesTheLimit()
        {
            string empty = File1("empty.txt", "");
            Assert.Equal(ExchangeCodes.NothingToSend, Read(new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(empty))).Code);
            Assert.Equal(ExchangeCodes.NothingToSend, Read(new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(Path.Combine(_dir, "gone.txt")))).Code);
            string big = File1("big.txt", new string('x', 500));
            ExchangeOutcome<ExchangeClipboardPayload> over = Read(new FakeClipboard().With(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(big)), cap: 100);
            Assert.Equal(ExchangeCodes.LimitReached, over.Code);
        }

        [Fact]
        public void ATextOverTheCapIsRefusedBeforeItIsMarshalled()
        {
            var c = new FakeClipboard().With(WindowInterop.CF_UNICODETEXT, Utf16(new string('A', 500)));
            Assert.Equal(ExchangeCodes.LimitReached, Read(c, cap: 100).Code);
        }

        [Fact]
        public void ThePngFormatIsTheFirstPlaceOfAPicture()
        {
            byte[] png = TinyPng();
            var c = new FakeClipboard().With(ClipboardFormats.Png, png).With(WindowInterop.CF_DIB, ClipboardImage.EncodeDib(new Bitmap(2, 2)));
            ExchangeClipboardPayload p = Read(c).Value!;
            Assert.Equal("image", p.Kind);
            Assert.Equal("image/png", p.MimeType);
            Assert.Equal(png, p.Bytes);
        }

        [Fact]
        public void ADibIsConvertedToPngAndV5IsTriedBeforeThePlainDib()
        {
            byte[] dib = ClipboardImage.EncodeDib(new Bitmap(3, 2));
            ExchangeClipboardPayload p = Read(new FakeClipboard().With(WindowInterop.CF_DIB, dib)).Value!;
            Assert.Equal("image", p.Kind);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, p.Bytes!.Take(4).ToArray());
            using (var bmp = new Bitmap(new MemoryStream(p.Bytes!)))
                Assert.Equal(new Size(3, 2), bmp.Size);

            // a V5 that cannot be converted falls through to the DIB that can
            var both = new FakeClipboard().With(WindowInterop.CF_DIBV5, new byte[] { 1, 2, 3 }).With(WindowInterop.CF_DIB, dib);
            Assert.Equal("image", Read(both).Value!.Kind);
        }

        [Fact]
        public void NothingThatCanBeSentIsNothingToSendNotAnError()
        {
            Assert.Equal(ExchangeCodes.NothingToSend, Read(new FakeClipboard()).Code);
            var rtf = new FakeClipboard().With(ClipboardFormats.Register("Rich Text Format"), new byte[] { 1, 2, 3 });
            Assert.Equal(ExchangeCodes.NothingToSend, Read(rtf).Code);
        }

        [Fact]
        public void ABusyClipboardIsSaidAndNotConfusedWithAnEmptyOne()
        {
            Assert.Equal(ExchangeCodes.ClipboardBusy, Read(new FakeClipboard { Locked = true }).Code);
        }

        [Fact]
        public void NoPrivacyMarkerStopsASend()
        {
            // item M: the person's press is the decision - a password manager's copy goes if they send it
            var c = new FakeClipboard().With(WindowInterop.CF_UNICODETEXT, Utf16("hunter2"))
                .With(ClipboardFormats.ExcludeFromMonitor, BitConverter.GetBytes(1))
                .With(ClipboardFormats.CanIncludeInHistory, BitConverter.GetBytes(0));
            Assert.True(Read(c).Ok);
        }

        // ---------------------------------------------------------------- names and drop lists

        [Theory]
        [InlineData("report.pdf", "report.pdf")]
        [InlineData("a/b\\c.txt", "a_b_c.txt")]
        [InlineData("..", "file")]
        [InlineData(".", "file")]
        [InlineData("", "file")]
        public void AWireNameHasNoSlashAndIsNotADotName(string given, string expected)
        {
            Assert.Equal(expected, ExchangeClipboardSource.WireName(given));
        }

        [Fact]
        public void AWireNameIsAtMost255BytesAndKeepsItsExtension()
        {
            string name = new string('я', 200) + ".docx"; // 405 bytes of UTF-8
            string wire = ExchangeClipboardSource.WireName(name);
            Assert.True(Encoding.UTF8.GetByteCount(wire) <= 255);
            Assert.EndsWith(".docx", wire);
        }

        [Fact]
        public void ADropListRoundTrips()
        {
            string[] paths = { @"C:\Users\User\Downloads\one.png", @"D:\two words\two.txt" };
            Assert.Equal(paths, ExchangeClipboardSource.ParseDropFiles(ExchangeClipboardSource.BuildDropFiles(paths)).ToArray());
            Assert.Empty(ExchangeClipboardSource.ParseDropFiles(new byte[10]));
            Assert.Empty(ExchangeClipboardSource.ParseDropFiles(new byte[40]));
        }

        [Fact]
        public void TheMimeTypeIsGuessedFromTheExtensionAndFallsBackToOctetStream()
        {
            Assert.Equal("application/pdf", MimeTypes.Guess(".PDF"));
            Assert.Equal("image/jpeg", MimeTypes.Guess(".jpg"));
            Assert.Equal("application/octet-stream", MimeTypes.Guess(".qwerty"));
            Assert.Equal("application/octet-stream", MimeTypes.Guess(""));
        }

        // ---------------------------------------------------------------- the received file's place

        [Fact]
        public void AReceivedNameNeverOverwritesAndGetsANumberedSuffix()
        {
            File.WriteAllText(Path.Combine(_dir, "doc.txt"), "x");
            File.WriteAllText(Path.Combine(_dir, "doc (1).txt"), "x");
            Assert.Equal(Path.Combine(_dir, "doc (2).txt"), ExchangeClipboardSink.UniquePath(_dir, "doc.txt"));
            Assert.Equal(Path.Combine(_dir, "fresh.txt"), ExchangeClipboardSink.UniquePath(_dir, "fresh.txt"));
        }

        [Theory]
        [InlineData("../../etc/passwd", "passwd")]
        [InlineData("..\\..\\evil.exe", "evil.exe")]
        [InlineData("a?b*c.txt", "a_b_c.txt")]
        [InlineData("CON", "_CON")]
        [InlineData("nul.txt", "_nul.txt")]
        [InlineData("..", "received-file")]
        [InlineData("   ", "received-file")]
        [InlineData("name. ", "name")]
        public void AReceivedNameIsOnlyABaseNameWindowsAccepts(string given, string expected)
        {
            Assert.Equal(expected, ExchangeClipboardSink.SafeName(given));
        }

        [Fact]
        public void TheFolderIsTheConfiguredOneOrDownloadsCyrFlip()
        {
            var cfg = new AppConfig();
            Assert.EndsWith(Path.Combine("Downloads", "CyrFlip"), ExchangeClipboardSink.FolderOf(cfg));
            cfg.ExchangeClipboardReceivedFolder = @"D:\Received";
            Assert.Equal(@"D:\Received", ExchangeClipboardSink.FolderOf(cfg));
        }

        [Fact]
        public void AStalePartIsSweptAndAFreshOneIsLeftAlone()
        {
            string stale = Path.Combine(_dir, "old" + ExchangeClipboardSink.PartSuffix);
            string fresh = Path.Combine(_dir, "new" + ExchangeClipboardSink.PartSuffix);
            File.WriteAllText(stale, "x");
            File.WriteAllText(fresh, "x");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
            ExchangeClipboardSink.SweepStaleParts(_dir);
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(fresh));
        }

        // ---------------------------------------------------------------- pictures

        [Fact]
        public void APictureSurvivesAPngToDibToPngRoundTripInSizeAndPixels()
        {
            using (var bmp = new Bitmap(4, 3))
            {
                for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) bmp.SetPixel(x, y, Color.FromArgb(255, x * 60, y * 80, 200));
                byte[] png = ClipboardImage.EncodePng(bmp);
                byte[] dib = ClipboardImage.PngToDib(png)!;
                byte[] back = ClipboardImage.DibToPng(dib)!;
                using (var again = new Bitmap(new MemoryStream(back)))
                {
                    Assert.Equal(new Size(4, 3), again.Size);
                    Assert.Equal(Color.FromArgb(255, 60, 80, 200).ToArgb(), again.GetPixel(1, 1).ToArgb());
                }
            }
        }

        [Fact]
        public void GarbageIsNotAPictureAndABitmapBombIsNotDecoded()
        {
            Assert.Null(ClipboardImage.PngToDib(new byte[] { 1, 2, 3, 4 }));
            Assert.Null(ClipboardImage.DibToPng(new byte[10]));
            var bomb = new byte[40 + 16];
            BitConverter.GetBytes(40).CopyTo(bomb, 0);
            BitConverter.GetBytes(int.MaxValue).CopyTo(bomb, 4);
            BitConverter.GetBytes(int.MaxValue).CopyTo(bomb, 8);
            BitConverter.GetBytes((short)1).CopyTo(bomb, 12);
            BitConverter.GetBytes((short)32).CopyTo(bomb, 14);
            Assert.Null(ClipboardImage.DibToPng(bomb));
        }

        private static byte[] TinyPng()
        {
            using (var bmp = new Bitmap(2, 2)) return ClipboardImage.EncodePng(bmp);
        }
    }
}
