using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// <c>screenshot region</c> and <c>screenshot full</c> (ticket S0110): the two handlers over a scripted host and an
    /// in-memory file system - no window, no real screen, no clipboard, no context. The cases that need a real tray
    /// copy (the overlay on two monitors of different scaling, a chord pressed while it is up, a real share) are
    /// <c>tools/uitest/Test-ScreenRegion.ps1 -Commands</c>'s and the release's.
    /// </summary>
    public class AppCommandsScreenshotsTests
    {
        private const string Work = @"C:\work";
        private const string Marker = "SECRET-4711";
        private static readonly DateTime Start = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Local);

        private static string T(string en) => en;

        private sealed class FakePicture : ICapturedPicture
        {
            private int _disposed;

            public FakePicture(int width = 40, int height = 30)
            {
                Width = width; Height = height; Png = ScreenshotSaverCommandTests.TinyPng();
            }

            public int Width { get; }
            public int Height { get; }
            public byte[] Png { get; }
            public byte[] Dib { get; } = new byte[] { 9, 8, 7 };
            /// <summary>1 once disposed, however many times it was asked.</summary>
            public int Disposed => Volatile.Read(ref _disposed);
            public byte[] EncodePng() => Png;
            public byte[] EncodeDib() => Dib;
            public void Dispose() => Interlocked.CompareExchange(ref _disposed, 1, 0);
        }

        private enum RegionMode
        {
            /// <summary>The person drags a region at once.</summary>
            Select,
            /// <summary>The person presses Escape at once.</summary>
            Cancel,
            /// <summary>Nothing happens until the overlay is closed; closing it reports a cancel.</summary>
            WaitReportsCancel,
            /// <summary>Nothing happens until the overlay is closed; the person's drag finishes in that same moment.</summary>
            WaitReportsPicture,
            /// <summary>The overlay never reports, however it is closed.</summary>
            WaitSilent,
        }

        private sealed class FakeHost : IScreenshotCommandHost
        {
            public readonly ScreenshotSaverCommandTests.CommandFs Fs = new ScreenshotSaverCommandTests.CommandFs();
            public readonly FakePicture Picture = new FakePicture();
            public DateTime Clock = Start;
            public CaptureStart RegionStart = CaptureStart.Started;
            public CaptureStart FullStart = CaptureStart.Started;
            public RegionMode Region = RegionMode.Select;
            public bool ClipboardResult = true;
            public bool Remote;
            public Action? OnRegionStarted;
            public readonly List<string> Events = new List<string>();
            public readonly List<byte[]> ClipboardPngs = new List<byte[]>();
            public readonly List<byte[]> ClipboardDibs = new List<byte[]>();
            public readonly List<int> Delays = new List<int>();
            public int RegionStarts, FullStarts, Cancels, Counted, Compacts;
            private Action<CaptureResult>? _pending;

            public FakeHost(int budgetMs = 5000)
            {
                Saver = new ScreenshotSaver(Fs, () => null, () => null, TimeSpan.FromMilliseconds(budgetMs));
            }

            public ScreenshotSaver Saver { get; }

            public CaptureStart StartRegion(Action<CaptureResult> done)
            {
                RegionStarts++;
                if (RegionStart != CaptureStart.Started) return RegionStart;
                _pending = done;
                if (Region == RegionMode.Select) done(new CaptureResult(Picture, Clock));
                else if (Region == RegionMode.Cancel) done(new CaptureResult(null, Clock));
                OnRegionStarted?.Invoke();
                return CaptureStart.Started;
            }

            public CaptureStart StartFull(Action<CaptureResult> done)
            {
                FullStarts++;
                if (FullStart != CaptureStart.Started) return FullStart;
                // The freeze is now: whatever the clock reads after the delay.
                done(new CaptureResult(Picture, Clock));
                return CaptureStart.Started;
            }

            public void CancelRegion()
            {
                Cancels++;
                if (_pending == null) return;
                if (Region == RegionMode.WaitReportsCancel) _pending(new CaptureResult(null, Clock));
                else if (Region == RegionMode.WaitReportsPicture) _pending(new CaptureResult(Picture, Clock));
            }

            public bool PutOnClipboard(byte[] png, byte[] dib)
            {
                Events.Add("clipboard; files=" + Fs.Files.Keys.Count(k => !Path.GetFileName(k).StartsWith("~", StringComparison.Ordinal)));
                ClipboardPngs.Add(png);
                ClipboardDibs.Add(dib);
                return ClipboardResult;
            }

            public void CountScreenshot() => Counted++;

            public void Compact() => Compacts++;

            public Task DelayAsync(int milliseconds, CancellationToken token)
            {
                Delays.Add(milliseconds);
                Clock = Clock.AddMilliseconds(milliseconds);
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task<TResult> RunOnUi<TResult>(Func<TResult> work)
            {
                try { return Task.FromResult(work()); }
                catch (Exception ex) { return Task.FromException<TResult>(ex); }
            }

            public bool IsRemote(string path) => Remote;
        }

        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.True(parsed.Kind == AppCommandLineKind.Command, "the parser refused: " + parsed.Message);
            return parsed.Invocation!;
        }

        private static Task<AppCommandResponse> Region(IScreenshotCommandHost host, string[] options, CancellationToken token = default, string workingDir = Work)
            => AppCommandsScreenshots.ScreenshotRegion(Inv(new[] { "screenshot", "region" }.Concat(options).ToArray()), workingDir, host, T, token);

        private static Task<AppCommandResponse> Full(IScreenshotCommandHost host, string[] options, CancellationToken token = default, string workingDir = Work)
            => AppCommandsScreenshots.ScreenshotFull(Inv(new[] { "screenshot", "full" }.Concat(options).ToArray()), workingDir, host, T, token);

        private static string[] Out(string path, params string[] more) => new[] { "--out", path }.Concat(more).ToArray();

        private static void AssertNothingWasCaptured(FakeHost host)
        {
            Assert.Equal(0, host.RegionStarts);
            Assert.Equal(0, host.FullStarts);
            Assert.Empty(host.Events);
            Assert.Empty(host.Fs.Files);
        }

        // ------------------------------------------------------------------ region

        [Fact]
        public async Task RegionSavesAtExactlyThePathAndAnswersPathWidthAndHeight()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"));

            Assert.Equal("done", r.Status);
            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"C:\x\a.png", r.Data!["path"]);
            Assert.Equal(40, r.Data["width"]);
            Assert.Equal(30, r.Data["height"]);
            Assert.Equal(PngTime.Inject(host.Picture.Png, Start), host.Fs.Read(@"C:\x\a.png"));
            Assert.Equal(Start, ScreenshotSaverCommandTests.TimeChunk(host.Fs.Read(@"C:\x\a.png")!));
            Assert.False(host.Fs.AnyTemp());
            Assert.Equal(1, host.Picture.Disposed);
            Assert.Equal(1, host.Compacts);
        }

        [Fact]
        public async Task ByDefaultTheClipboardIsNotTouchedAndTheCountDoesNotRise()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"));

            Assert.Equal(0, r.ExitCode);
            Assert.Empty(host.Events);
            Assert.Equal(0, host.Counted);
        }

        [Fact]
        public async Task EscapeIsExit16WithNoFileAndNoClipboardWrite()
        {
            var host = new FakeHost { Region = RegionMode.Cancel };

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png", "--clipboard"));

            Assert.Equal("failed", r.Status);
            Assert.Equal(AppCommandReasons.Cancelled, r.Reason);
            Assert.Equal(16, r.ExitCode);
            Assert.Empty(host.Fs.Files);
            Assert.Empty(host.Events);
            Assert.Equal(0, host.Counted);
        }

        [Fact]
        public async Task TheBoundClosesTheOverlayAndWritesNothing()
        {
            using var bound = new CancellationTokenSource();
            var host = new FakeHost { Region = RegionMode.WaitReportsCancel };
            host.OnRegionStarted = bound.Cancel; // the verb's bound passes while the person is still choosing

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"), bound.Token);

            Assert.Equal(AppCommandReasons.Timeout, r.Reason);
            Assert.Equal(2, r.ExitCode);
            Assert.Equal(1, host.Cancels);
            Assert.Empty(host.Fs.Files);
            Assert.Empty(host.Events);
        }

        [Fact]
        public async Task AnOverlayThatNeverReportsIsClosedAndTheVerbStopsWaiting()
        {
            using var bound = new CancellationTokenSource();
            var host = new FakeHost { Region = RegionMode.WaitSilent };
            host.OnRegionStarted = bound.Cancel;
            int before = AppCommandsScreenshots.CancelGraceMs;
            AppCommandsScreenshots.CancelGraceMs = 50;
            try
            {
                AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"), bound.Token);

                Assert.Equal(AppCommandReasons.Timeout, r.Reason);
                Assert.Equal(1, host.Cancels);
                Assert.Empty(host.Fs.Files);
            }
            finally { AppCommandsScreenshots.CancelGraceMs = before; }
        }

        [Fact]
        public async Task ASelectionFinishedAtTheBoundIsDroppedAndItsPictureIsReleased()
        {
            using var bound = new CancellationTokenSource();
            var host = new FakeHost { Region = RegionMode.WaitReportsPicture };
            host.OnRegionStarted = bound.Cancel;

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png", "--clipboard"), bound.Token);

            Assert.Equal(AppCommandReasons.Timeout, r.Reason);
            Assert.Empty(host.Fs.Files);
            Assert.Empty(host.Events);
            Assert.Equal(1, host.Picture.Disposed);
        }

        // ------------------------------------------------------------------ full and the delay

        [Fact]
        public async Task FullAnswersTheWholeScreensSize()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Full(host, Out(@"C:\x\all.png"));

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"C:\x\all.png", r.Data!["path"]);
            Assert.Equal(40, r.Data["width"]);
            Assert.Equal(30, r.Data["height"]);
            Assert.Empty(host.Delays);
            Assert.Equal(0, host.RegionStarts);
        }

        [Fact]
        public async Task TheFreezeInstantIsTheClockAfterTheDelayAndIsWhatTheNameAndTheTimeChunkCarry()
        {
            var host = new FakeHost();
            host.Fs.Folders.Add(@"C:\shots");

            AppCommandResponse r = await Full(host, Out(@"C:\shots\", "--delay-ms", "3000"));

            DateTime frozen = Start.AddSeconds(3);
            Assert.Equal(new[] { 3000 }, host.Delays);
            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"C:\shots\screenshot_261115_100003.png", r.Data!["path"]);
            Assert.Equal(frozen, ScreenshotSaverCommandTests.TimeChunk(host.Fs.Read((string)r.Data["path"])!));
        }

        [Theory]
        [InlineData("30001")]
        [InlineData("-1")]
        [InlineData("99999999")]
        public async Task ADelayOutsideTheRangeIsExit10BeforeAnyCapture(string delay)
        {
            var host = new FakeHost();

            AppCommandResponse r = await Full(host, Out(@"C:\x\a.png", "--delay-ms", delay));

            Assert.Equal("refused", r.Status);
            Assert.Equal(AppCommandReasons.BadArgument, r.Reason);
            Assert.Equal(10, r.ExitCode);
            Assert.Empty(host.Delays);
            AssertNothingWasCaptured(host);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("30000", 1)]
        public async Task TheEdgesOfTheDelayRangeAreAccepted(string delay, int delays)
        {
            var host = new FakeHost();

            AppCommandResponse r = await Full(host, Out(@"C:\x\a.png", "--delay-ms", delay));

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(delays, host.Delays.Count);
        }

        // ------------------------------------------------------------------ the capture itself failing or busy

        [Fact]
        public async Task ACaptureAlreadyUnderWayIsBusy11AndNothingIsEncodedOrWritten()
        {
            foreach (bool region in new[] { true, false })
            {
                var host = new FakeHost { RegionStart = CaptureStart.Busy, FullStart = CaptureStart.Busy };

                AppCommandResponse r = region ? await Region(host, Out(@"C:\x\a.png")) : await Full(host, Out(@"C:\x\a.png"));

                Assert.Equal("busy", r.Status);
                Assert.Equal(AppCommandReasons.Busy, r.Reason);
                Assert.Equal(11, r.ExitCode);
                Assert.Empty(host.Fs.Files);
                Assert.Equal(0, host.Picture.Disposed);
                Assert.Equal(0, host.Compacts);
            }
        }

        [Theory]
        [InlineData("GrabFailed")]
        [InlineData("OverlayFailed")]
        public async Task AFailedGrabIsExit1CaptureFailed(string kind)
        {
            // The enum is internal, so the theory carries its name.
            var failure = (CaptureStart)Enum.Parse(typeof(CaptureStart), kind);
            var host = new FakeHost { RegionStart = failure, FullStart = failure };

            AppCommandResponse region = await Region(host, Out(@"C:\x\a.png"));
            AppCommandResponse full = await Full(host, Out(@"C:\x\a.png"));

            foreach (AppCommandResponse r in new[] { region, full })
            {
                Assert.Equal("failed", r.Status);
                Assert.Equal(AppCommandReasons.CaptureFailed, r.Reason);
                Assert.Equal(1, r.ExitCode);
            }
            Assert.Empty(host.Fs.Files);
        }

        [Fact]
        public async Task AFaultInTheHostIsAnAnswerNeverAnException()
        {
            var host = new ThrowingHost();

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"));

            Assert.Equal("failed", r.Status);
            Assert.Equal(AppCommandReasons.Error, r.Reason);
            Assert.Equal(1, r.ExitCode);
            Assert.DoesNotContain("boom", r.Message);
        }

        private sealed class ThrowingHost : IScreenshotCommandHost
        {
            private readonly FakeHost _inner = new FakeHost();
            public ScreenshotSaver Saver => _inner.Saver;
            public CaptureStart StartRegion(Action<CaptureResult> done) => throw new InvalidOperationException("boom");
            public CaptureStart StartFull(Action<CaptureResult> done) => throw new InvalidOperationException("boom");
            public void CancelRegion() { }
            public bool PutOnClipboard(byte[] png, byte[] dib) => false;
            public void CountScreenshot() { }
            public void Compact() { }
            public Task DelayAsync(int milliseconds, CancellationToken token) => Task.CompletedTask;
            public Task<TResult> RunOnUi<TResult>(Func<TResult> work) => _inner.RunOnUi(work);
            public bool IsRemote(string path) => false;
        }

        // ------------------------------------------------------------------ the clipboard

        [Fact]
        public async Task TheClipboardIsWrittenAfterTheFileIsOnDiskWithThePlainPngAndTheDib()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png", "--clipboard"));

            Assert.Equal(0, r.ExitCode);
            Assert.Equal("Saved the capture and put it on the clipboard.", r.Message);
            // Written when exactly one finished file (no temporary one) was already there.
            Assert.Equal(new[] { "clipboard; files=1" }, host.Events);
            Assert.Equal(host.Picture.Png, host.ClipboardPngs.Single()); // the chord's PNG: no tIME chunk
            Assert.Equal(host.Picture.Dib, host.ClipboardDibs.Single());
            Assert.Equal(1, host.Counted);
        }

        [Fact]
        public async Task ClipboardFalseWritesNothing()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Full(host, Out(@"C:\x\a.png", "--clipboard=false"));

            Assert.Equal(0, r.ExitCode);
            Assert.Empty(host.Events);
        }

        [Fact]
        public async Task AFailedFileLeavesTheClipboardAlone()
        {
            var host = new FakeHost();
            host.Fs.WriteFailure = new IOException("full", 112);

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png", "--clipboard"));

            Assert.Equal(13, r.ExitCode);
            Assert.Empty(host.Events);
            Assert.Equal(0, host.Counted);
            Assert.Equal(1, host.Picture.Disposed);
        }

        [Fact]
        public async Task AClipboardAnotherProgramHoldsAfterASavedFileIsExit1AndTheAnswerStillNamesTheFile()
        {
            var host = new FakeHost { ClipboardResult = false };

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png", "--clipboard"));

            Assert.Equal("failed", r.Status);
            Assert.Equal(AppCommandReasons.ClipboardFailed, r.Reason);
            Assert.Equal(1, r.ExitCode);
            Assert.Equal(@"C:\x\a.png", r.Data!["path"]);
            Assert.Equal(40, r.Data["width"]);
            Assert.NotNull(host.Fs.Read(@"C:\x\a.png"));
            Assert.Equal(0, host.Counted);
        }

        // ------------------------------------------------------------------ the target

        [Fact]
        public async Task AnExistingFileWithoutOverwriteIsExit11ExistsUntouchedAndNothingIsCaptured()
        {
            var host = new FakeHost();
            host.Fs.Files[@"C:\x\a.png"] = new byte[] { 1, 2, 3 };

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"));

            Assert.Equal("refused", r.Status);
            Assert.Equal(AppCommandReasons.Exists, r.Reason);
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(new byte[] { 1, 2, 3 }, host.Fs.Read(@"C:\x\a.png"));
            Assert.Equal(0, host.RegionStarts);
        }

        [Fact]
        public async Task OverwriteReplacesTheFile()
        {
            var host = new FakeHost();
            host.Fs.Files[@"C:\x\a.png"] = new byte[] { 1, 2, 3 };

            AppCommandResponse r = await Full(host, Out(@"C:\x\a.png", "--overwrite"));

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(PngTime.Inject(host.Picture.Png, Start), host.Fs.Read(@"C:\x\a.png"));
            Assert.False(host.Fs.AnyTemp());
        }

        [Fact]
        public async Task AFileThatAppearsWhileThePersonChoosesIsNotReplacedAndThePictureIsDropped()
        {
            var host = new FakeHost();
            host.OnRegionStarted = () => host.Fs.Files[@"C:\x\a.png"] = new byte[] { 4, 4 };

            AppCommandResponse r = await Region(host, Out(@"C:\x\a.png"));

            // The first look found the name free; the file appeared while the person chose, so the late check at write time decides.
            Assert.Equal("refused", r.Status);
            Assert.Equal(AppCommandReasons.Exists, r.Reason);
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(new byte[] { 4, 4 }, host.Fs.Read(@"C:\x\a.png"));
            Assert.False(host.Fs.AnyTemp());
            Assert.Equal(1, host.Picture.Disposed);
            Assert.Empty(host.Events);
        }

        [Fact]
        public async Task AMissingFolderIsExit11TargetMissingAndIsNeverCreated()
        {
            var host = new FakeHost();

            AppCommandResponse r = await Region(host, Out(@"C:\nowhere\a.png"));

            Assert.Equal("failed", r.Status);
            Assert.Equal(AppCommandReasons.TargetMissing, r.Reason);
            Assert.Equal(11, r.ExitCode);
            Assert.Empty(host.Fs.Created);
            AssertNothingWasCaptured(host);
        }

        [Theory]
        [InlineData(@"C:\x\a.jpg")]
        [InlineData(@"C:\x\a")]
        [InlineData(@"C:\x\nul.png")]
        [InlineData(@"C:\x\a<b.png")]
        [InlineData(@"C:\x\a:b.png")]
        [InlineData(@"\\?\C:\x\a.png")]
        public async Task ANameThatIsNotAUsablePngFileOrFolderIsExit10BeforeAnyCapture(string path)
        {
            var host = new FakeHost();

            AppCommandResponse region = await Region(host, Out(path));
            AppCommandResponse full = await Full(host, Out(path));

            foreach (AppCommandResponse r in new[] { region, full })
            {
                Assert.Equal("refused", r.Status);
                Assert.Equal(AppCommandReasons.BadArgument, r.Reason);
                Assert.Equal(10, r.ExitCode);
            }
            AssertNothingWasCaptured(host);
        }

        [Fact]
        public async Task NoMessageHoldsAnythingOfThePathTheCallerGave()
        {
            var host = new FakeHost();
            host.Fs.Folders.Add(@"C:\" + Marker);
            host.Fs.Files[@"C:\" + Marker + @"\taken.png"] = new byte[] { 1 };
            string[] paths =
            {
                @"C:\nowhere-" + Marker + @"\a.png",       // missing folder
                @"C:\x\" + Marker + ".jpg",                 // not a png
                @"C:\x\" + Marker + "<.png",                // forbidden character
                @"C:\" + Marker + @"\taken.png",            // exists
                @"C:\x\" + Marker + @"\COM1\a.png",         // reserved name
            };
            foreach (string path in paths)
            {
                AppCommandResponse r = await Region(host, Out(path));

                Assert.False(r.Message.Contains(Marker), path + " -> " + r.Message);
                Assert.Null(r.Data);
                Assert.False(string.IsNullOrEmpty(r.Message));
            }
        }

        [Fact]
        public async Task ARelativePathIsTheCallersWorkingDirectorys()
        {
            var host = new FakeHost();
            host.Fs.Folders.Add(@"D:\caller");

            AppCommandResponse r = await Full(host, Out("shot.png"), workingDir: @"D:\caller");

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"D:\caller\shot.png", r.Data!["path"]);
        }

        [Fact]
        public async Task AFolderGetsTheAutomaticNameAndATakenOneGetsTheOrdinalWhateverOverwriteSays()
        {
            var host = new FakeHost();
            host.Fs.Folders.Add(@"C:\shots");
            host.Fs.Files[@"C:\shots\screenshot_261115_100000.png"] = new byte[] { 5 };

            AppCommandResponse r = await Full(host, Out(@"C:\shots\", "--overwrite"));

            Assert.Equal(@"C:\shots\screenshot_261115_100000 (2).png", r.Data!["path"]);
            Assert.Equal(new byte[] { 5 }, host.Fs.Read(@"C:\shots\screenshot_261115_100000.png"));
        }

        [Fact]
        public async Task ADiskThatIsFullIsExit13NoSpaceAndThePictureIsReleased()
        {
            var host = new FakeHost();
            host.Fs.WriteFailure = new IOException("full", unchecked((int)0x80070070));

            AppCommandResponse r = await Full(host, Out(@"C:\x\a.png"));

            Assert.Equal("failed", r.Status);
            Assert.Equal(AppCommandReasons.NoSpace, r.Reason);
            Assert.Equal(13, r.ExitCode);
            Assert.Equal(1, host.Picture.Disposed);
            Assert.Equal(1, host.Compacts);
            Assert.False(host.Fs.AnyTemp());
        }

        [Fact]
        public async Task AnyOtherWriteFailureIsExit14IoErrorAndTheFolderVanishingIsExit11()
        {
            var host = new FakeHost();
            host.Fs.WriteFailure = new UnauthorizedAccessException(@"denied: C:\x\" + Marker);

            AppCommandResponse denied = await Full(host, Out(@"C:\x\a.png"));
            Assert.Equal(AppCommandReasons.IoError, denied.Reason);
            Assert.Equal(14, denied.ExitCode);
            Assert.False(denied.Message.Contains(Marker));

            var gone = new FakeHost();
            gone.OnRegionStarted = () => gone.Fs.Folders.Remove(@"C:\x");
            AppCommandResponse missing = await Region(gone, Out(@"C:\x\a.png"));
            Assert.Equal(AppCommandReasons.TargetMissing, missing.Reason);
            Assert.Equal(11, missing.ExitCode);
            Assert.Empty(gone.Fs.Created);
        }

        // ------------------------------------------------------------------ a share that does not answer

        [Theory]
        [InlineData(true, AppCommandReasons.Network, 15)]
        [InlineData(false, AppCommandReasons.IoError, 14)]
        public async Task ATargetThatDoesNotAnswerInTimeIsToldApartByWhetherItIsARemotePath(bool remote, string reason, int exit)
        {
            using var gate = new ManualResetEvent(false);
            var host = new FakeHost(budgetMs: 100) { Remote = remote };
            host.Fs.ProbeGate = gate;
            try
            {
                AppCommandResponse r = await Region(host, Out(@"\\srv\share\a.png"));

                Assert.Equal("failed", r.Status);
                Assert.Equal(reason, r.Reason);
                Assert.Equal(exit, r.ExitCode);
                Assert.Equal(0, host.RegionStarts);
                Assert.Empty(host.Fs.Files);
            }
            finally { gate.Set(); } // let the abandoned probe finish
        }

        // ------------------------------------------------------------------ the gate and the parser the verbs sit behind

        [Theory]
        [InlineData("screenshot region")]
        [InlineData("screenshot full")]
        public void WithTheSettingOffBothVerbsAreRefusedAtTheClassGate(string verb)
        {
            AppCommandSpec spec = AppCommandManifest.Shipped.Find(verb)!;

            AppCommandResponse? off = AppCommandVerbs.RiskGate(spec, settingOn: false, legacy: false, T);
            AppCommandResponse? on = AppCommandVerbs.RiskGate(spec, settingOn: true, legacy: false, T);

            Assert.NotNull(off);
            Assert.Equal(AppCommandReasons.SwitchOff, off!.Reason);
            Assert.Equal(12, off.ExitCode);
            Assert.Null(on);
        }

        [Theory]
        [InlineData("screenshot|region")]                                        // no --out
        [InlineData("screenshot|region|--out|a.png|--delay-ms|5")]               // the delay belongs to full
        [InlineData("screenshot|region|--out|a.png|--clipboard=maybe")]
        [InlineData("screenshot|full|--out|a.png|--annotate")]                   // the region verb's option (S0130), not the full one's
        [InlineData("screenshot|full|--out|a.png|--delay-ms|soon")]
        [InlineData("screenshot|full|--out|a.png|--out|b.png")]
        public void TheParserRefusesWhatTheVerbsDoNotTake(string line)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(line.Split('|'));

            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, parsed.Reason);
        }

        // ------------------------------------------------------------------ the manifest

        [Fact]
        public void BothVerbsAreInTheManifestAsReadsUserDataAndRefuseWhenNothingRuns()
        {
            AppCommandSpec region = AppCommandManifest.Shipped.Find("screenshot region")!;
            AppCommandSpec full = AppCommandManifest.Shipped.Find("screenshot full")!;

            foreach (AppCommandSpec spec in new[] { region, full })
            {
                Assert.Equal("reads-user-data", spec.Risk);
                Assert.Equal("refuse", spec.ColdStart);
                Assert.Equal("done", spec.Wait);
                Assert.False(spec.IsSafe);
                Assert.True(spec.Arg("out")!.Required);
                Assert.Equal("path", spec.Arg("out")!.Kind);
                Assert.Equal("bool", spec.Arg("overwrite")!.Kind);
                Assert.Equal("bool", spec.Arg("clipboard")!.Kind);
            }
            // S0130 made the region entry carry the 10-minute run of --annotate; the plain selection's own
            // 120 seconds is AppCommandsScreenshotExtras.SelectionBoundMs, held by AppCommandsScreenshotExtrasTests.
            Assert.Equal(600000, region.TimeoutMs);
            Assert.Equal(60000, full.TimeoutMs);
            Assert.Null(region.Arg("delay-ms"));
            Assert.Equal("int", full.Arg("delay-ms")!.Kind);
        }

        [Fact]
        public void TheRegionVerbIsInteractiveAndTheFullOneIsNot()
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(AppCommandManifest.Shipped.Raw));
            Dictionary<string, object> Entry(string name) => ((object[])root["commands"]).Cast<Dictionary<string, object>>().Single(c => (string)c["name"] == name);

            Assert.Equal(true, Entry("screenshot region")["interactive"]);
            Assert.Equal(false, Entry("screenshot full")["interactive"]);
            Assert.Equal(false, Entry("screenshot region")["idempotent"]);
            Assert.Equal(false, Entry("screenshot full")["idempotent"]);
        }

        [Fact]
        public void TheRenderedGuideNamesBothVerbs()
        {
            string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
            string doc = File.ReadAllText(Path.Combine(repo, "docs", "CLI.md"));
            Assert.Contains("## screenshot region", doc);
            Assert.Contains("## screenshot full", doc);
        }

        [Fact]
        public void EveryMessageTheVerbsSayIsRegisteredInAllThirteenLanguages()
        {
            string[] keys =
            {
                "The delay must be a whole number of milliseconds from {0} to {1}.",
                "A screen capture is already under way. Finish or cancel it, then try again.",
                "The screen could not be captured. Nothing was saved.",
                "The selection was cancelled. Nothing was saved.",
                "--out must be a file name ending in .png, an existing folder, or a path ending in a backslash.",
                "The folder in --out does not exist. CyrFlip does not create it.",
                "A file with this name already exists. Choose another name, or add --overwrite to replace it.",
                "The share in --out did not answer in time. Nothing was captured.",
                "The folder in --out could not be checked. Nothing was captured.",
                "Saved the capture.",
                "Saved the capture and put it on the clipboard.",
                "The capture is saved, but another program holds the clipboard, so it is not on it.",
                "The disk is full. The capture was not saved.",
                "The share did not answer in time. The capture was not saved.",
                "The file could not be written. The capture was not saved.",
                "No area was chosen in time. The capture was closed and nothing was saved.",
            };
            foreach (string key in keys)
                foreach (string language in Localization.Names)
                    Assert.False(string.IsNullOrWhiteSpace(Localization.Translate(language, key)), language + ": " + key);
        }
    }
}
