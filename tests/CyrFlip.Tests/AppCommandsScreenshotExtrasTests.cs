using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// `screenshot window` and `screenshot region --annotate` (ticket S0130): the two handlers over a scripted host and
    /// an in-memory file system - no window, no real screen, no editor, no clipboard, no context. The manifest rows and
    /// the parser lines are read back from the shipped manifest's own bytes; the live runs that need a real screen and
    /// the real editor (a window on two monitors of different scaling, a drawn annotation delivered by OK) are the
    /// release's and the owner's desk checks, named in the ticket.
    /// </summary>
    public class AppCommandsScreenshotExtrasTests
    {
        private const string Work = @"C:\work";
        private static readonly DateTime Start = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Local);

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

        /// <summary>What the scripted annotate start does once the verb calls it.</summary>
        private enum AnnotateMode
        {
            /// <summary>The person chooses, the editor opens, and OK delivers the composed picture - all before the verb waits.</summary>
            SelectEditOk,
            /// <summary>The person presses Escape during the selection.</summary>
            EscapeAtSelection,
            /// <summary>The person chooses and the editor opens, then Cancel in the editor.</summary>
            EditorCancel,
            /// <summary>The person chooses, but the editor cannot be opened.</summary>
            EditorFails,
            /// <summary>Nothing reports and nothing opens - the selection's own budget closes the run.</summary>
            SilentSelection,
            /// <summary>The person chooses and the editor opens, then nothing reports - the run's own bound closes the run.</summary>
            SilentEditor,
        }

        private sealed class FakeExtrasHost : IScreenshotExtrasCommandHost
        {
            public readonly ScreenshotSaverCommandTests.CommandFs Fs = new ScreenshotSaverCommandTests.CommandFs();
            public readonly FakePicture Picture = new FakePicture();
            public readonly List<SelectionWindow> Windows = new List<SelectionWindow>();
            public readonly List<string> Events = new List<string>();
            public readonly List<byte[]> ClipboardPngs = new List<byte[]>();
            public readonly List<byte[]> ClipboardDibs = new List<byte[]>();
            public DateTime Clock = Start;
            public CaptureStart WindowStart = CaptureStart.Started;
            public CaptureStart AnnotateStart = CaptureStart.Started;
            public AnnotateMode Annotate = AnnotateMode.SelectEditOk;
            public bool AnnotateOn = true;
            public bool ClipboardResult = true;
            public bool Remote;
            /// <summary>Run once from inside the annotate start, after the callbacks are recorded - the test's door into the run.</summary>
            public Action? OnAnnotateStarted;
            public int WindowStarts, AnnotateStarts, RegionCancels, AnnotateCancels, Counted, Compacts;

            public FakeExtrasHost(int budgetMs = 5000)
            {
                Saver = new ScreenshotSaver(Fs, () => null, () => null, TimeSpan.FromMilliseconds(budgetMs));
            }

            public ScreenshotSaver Saver { get; }

            public IReadOnlyList<SelectionWindow> TopLevelWindows() => Windows;

            public bool AnnotateEnabled => AnnotateOn;

            public CaptureStart StartWindow(IntPtr window, Action<CaptureResult> done)
            {
                WindowStarts++;
                if (WindowStart != CaptureStart.Started) return WindowStart;
                done(new CaptureResult(Picture, Clock));
                return CaptureStart.Started;
            }

            public CaptureStart StartRegionAnnotate(Action<CaptureResult> done, Action<bool> editing)
            {
                AnnotateStarts++;
                if (AnnotateStart != CaptureStart.Started) return AnnotateStart;
                switch (Annotate)
                {
                    case AnnotateMode.SelectEditOk:
                        editing(true);
                        done(new CaptureResult(Picture, Clock));
                        break;
                    case AnnotateMode.EscapeAtSelection:
                        done(new CaptureResult(null, Clock));
                        break;
                    case AnnotateMode.EditorCancel:
                        editing(true);
                        done(new CaptureResult(null, Clock));
                        break;
                    case AnnotateMode.EditorFails:
                        editing(false);
                        done(new CaptureResult(null, Clock));
                        break;
                    case AnnotateMode.SilentEditor:
                        editing(true);
                        break;
                    default:
                        break; // SilentSelection: nothing happens until the verb's own budget closes the overlay.
                }
                OnAnnotateStarted?.Invoke();
                return CaptureStart.Started;
            }

            public void CancelAnnotateRun() => AnnotateCancels++;

            public CaptureStart StartRegion(Action<CaptureResult> done) => throw new NotSupportedException("the wave-1 tests own the plain region verb");

            public CaptureStart StartFull(Action<CaptureResult> done) => throw new NotSupportedException("the wave-1 tests own the full verb");

            public void CancelRegion() => RegionCancels++;

            public bool PutOnClipboard(byte[] png, byte[] dib)
            {
                Events.Add("clipboard; files=" + Fs.Files.Keys.Count(k => !Path.GetFileName(k).StartsWith("~", StringComparison.Ordinal)));
                ClipboardPngs.Add(png);
                ClipboardDibs.Add(dib);
                return ClipboardResult;
            }

            public void CountScreenshot() => Counted++;

            public void Compact() => Compacts++;

            public Task DelayAsync(int milliseconds, CancellationToken token) => Task.Delay(milliseconds, token);

            public Task<T> RunOnUi<T>(Func<T> work)
            {
                try { return Task.FromResult(work()); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            }

            public bool IsRemote(string path) => Remote;
        }

        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.True(parsed.Kind == AppCommandLineKind.Command, "the parser refused: " + parsed.Message);
            return parsed.Invocation!;
        }

        private static Task<AppCommandResponse> Window(FakeExtrasHost host, string[] options,
            CancellationToken token = default, string workingDir = Work)
            => AppCommandsScreenshotExtras.ScreenshotWindow(
                Inv(new[] { "screenshot", "window" }.Concat(options).ToArray()), workingDir, host, T, token);

        private static Task<AppCommandResponse> Annotate(FakeExtrasHost host, string[] options,
            CancellationToken token = default, string workingDir = Work)
            => AppCommandsScreenshotExtras.ScreenshotRegionAnnotate(
                Inv(new[] { "screenshot", "region" }.Concat(options).ToArray()), workingDir, host, T, token);

        private static string[] Out(string path, params string[] more) => new[] { "--out", path }.Concat(more).ToArray();

        private static SelectionWindow Win(long handle, uint pid, string title) => new SelectionWindow(new IntPtr(handle), pid, title);

        private static void AssertNothingWasWritten(FakeExtrasHost host)
        {
            Assert.Equal(0, host.Counted);
            Assert.Empty(host.Fs.Files);
        }

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheManifestCarriesTheWindowVerbWithTheRowFieldsAndTheRegionAnnotateOption()
        {
            AppCommandSpec? window = AppCommandManifest.Shipped.Find("screenshot window");
            Assert.NotNull(window);
            Assert.Equal("reads-user-data", window!.Risk);
            Assert.Equal("refuse", window.ColdStart);
            Assert.Equal("done", window.Wait);
            Assert.Equal(30000, window.TimeoutMs);
            Assert.True(window.Arg("out")!.Required);
            Assert.Equal("path", window.Arg("out")!.Kind);
            Assert.Equal("int", window.Arg("window-pid")!.Kind);
            Assert.Equal("string", window.Arg("window-title")!.Kind);
            Assert.False(window.Arg("window-pid")!.Required);
            Assert.False(window.Arg("window-title")!.Required);

            AppCommandSpec? region = AppCommandManifest.Shipped.Find("screenshot region");
            Assert.NotNull(region);
            Assert.Equal(600000, region!.TimeoutMs);
            Assert.NotNull(region.Arg("annotate"));
        }

        [Fact]
        public void TheParserRefusesWhatItMustForTheWindowVerb()
        {
            // --out is required - the parser's own exit 10.
            AppCommandLineResult noOut = AppCommandLine.Parse(new[] { "screenshot", "window", "--window-pid", "4012" });
            Assert.Equal(AppCommandLineKind.Refused, noOut.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, noOut.Reason);

            // A pid that is not a number is the int kind's own refusal.
            AppCommandLineResult badPid = AppCommandLine.Parse(new[] { "screenshot", "window", "--out", @"C:\x\w.png", "--window-pid", "abc" });
            Assert.Equal(AppCommandLineKind.Refused, badPid.Kind);

            // An unknown option is refused, never half-run.
            AppCommandLineResult unknown = AppCommandLine.Parse(new[] { "screenshot", "window", "--out", @"C:\x\w.png", "--pid", "4012" });
            Assert.Equal(AppCommandLineKind.Refused, unknown.Kind);

            // The --annotate flag of the region verb parses beside the wave-1 options.
            AppCommandLineResult annotate = AppCommandLine.Parse(new[] { "screenshot", "region", "--annotate", "--out", @"C:\x\a.png" });
            Assert.Equal(AppCommandLineKind.Command, annotate.Kind);
            Assert.True(annotate.Invocation!.Flag("annotate"));
        }

        // ------------------------------------------------------------------ screenshot window

        [Fact]
        public async Task WindowSavesTheNamedWindowsRectangleAndAnswersPathWidthAndHeight()
        {
            var host = new FakeExtrasHost();
            host.Windows.Add(Win(11, 4012, "CyrFlip - Settings"));
            host.Windows.Add(Win(12, 5000, "Something else"));

            AppCommandResponse r = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012"));

            Assert.Equal("done", r.Status);
            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"C:\x\w.png", r.Data!["path"]);
            Assert.Equal(40, r.Data["width"]);
            Assert.Equal(30, r.Data["height"]);
            Assert.Equal(1, host.WindowStarts);
            Assert.Equal(Start, ScreenshotSaverCommandTests.TimeChunk(host.Fs.Read(@"C:\x\w.png")!));
            Assert.False(host.Fs.AnyTemp());
            Assert.Equal(1, host.Picture.Disposed);
            Assert.Equal(1, host.Compacts);
            // The row lists no --clipboard: the clipboard is never touched and the count never rises.
            Assert.Empty(host.ClipboardPngs);
            Assert.Equal(0, host.Counted);
        }

        [Fact]
        public async Task WindowNamesByTitleAndBothNarrow()
        {
            var host = new FakeExtrasHost();
            host.Windows.Add(Win(11, 4012, "CyrFlip - Settings"));
            host.Windows.Add(Win(12, 4012, "CyrFlip - About"));

            // Two windows of one pid share the short title part: bad-argument with the "be more exact" sentence.
            AppCommandResponse vague = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012", "--window-title", "CyrFlip"));
            Assert.Equal("refused", vague.Status);
            Assert.Equal(10, vague.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, vague.Reason);
            Assert.Equal(0, host.WindowStarts);

            // The same pid with a longer title part narrows to the one window.
            AppCommandResponse exact = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012", "--window-title", "Settings"));
            Assert.Equal(0, exact.ExitCode);
            Assert.Equal(1, host.WindowStarts);
        }

        [Fact]
        public async Task WindowRefusesNeitherOptionGivenNoMatchAndAMinimizedWindow()
        {
            var host = new FakeExtrasHost();
            host.Windows.Add(Win(11, 4012, "CyrFlip"));

            // Neither naming option: item Q's own usage answer, the selection family's sentence.
            AppCommandResponse neither = await Window(host, Out(@"C:\x\w.png"));
            Assert.Equal(10, neither.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, neither.Reason);
            Assert.Equal(0, host.WindowStarts);

            // No window matches the pid.
            AppCommandResponse none = await Window(host, Out(@"C:\x\w.png", "--window-pid", "7777"));
            Assert.Equal("failed", none.Status);
            Assert.Equal(11, none.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, none.Reason);
            Assert.Equal(0, host.WindowStarts);

            // A window that matches but has no on-screen rectangle: target-missing with the restore cure.
            host.WindowStart = CaptureStart.WindowHidden;
            AppCommandResponse hidden = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012"));
            Assert.Equal(11, hidden.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, hidden.Reason);
            Assert.Equal("The named window is minimized or outside the screens, so there is nothing on the screen to capture. Restore it, then run the command again.", hidden.Message);
            AssertNothingWasWritten(host);
        }

        [Fact]
        public async Task WindowAnswersBusyAndAFailedFreeze()
        {
            var host = new FakeExtrasHost();
            host.Windows.Add(Win(11, 4012, "CyrFlip"));

            host.WindowStart = CaptureStart.Busy;
            AppCommandResponse busy = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012"));
            Assert.Equal("busy", busy.Status);
            Assert.Equal(11, busy.ExitCode);
            Assert.Equal(AppCommandReasons.Busy, busy.Reason);
            Assert.Empty(host.Fs.Files);

            host.WindowStart = CaptureStart.GrabFailed;
            AppCommandResponse failed = await Window(host, Out(@"C:\x\w.png", "--window-pid", "4012"));
            Assert.Equal("failed", failed.Status);
            Assert.Equal(1, failed.ExitCode);
            Assert.Equal(AppCommandReasons.CaptureFailed, failed.Reason);
            Assert.Empty(host.Fs.Files);
        }

        [Fact]
        public async Task WindowChecksTheTargetBeforeTheWindowBothersAnything()
        {
            var host = new FakeExtrasHost();
            host.Windows.Add(Win(11, 4012, "CyrFlip"));

            // A name that is not a .png file: exit 10 before a window is frozen.
            AppCommandResponse notPng = await Window(host, Out(@"C:\x\w.jpg", "--window-pid", "4012"));
            Assert.Equal(10, notPng.ExitCode);
            Assert.Equal(0, host.WindowStarts);

            // A folder that does not exist: exit 11, never created, nothing captured.
            AppCommandResponse missing = await Window(host, Out(@"C:\missing\a.png", "--window-pid", "4012"));
            Assert.Equal(11, missing.ExitCode);
            Assert.Equal(0, host.WindowStarts);
            AssertNothingWasWritten(host);
        }

        // ------------------------------------------------------------------ screenshot region --annotate

        [Fact]
        public async Task AnnotateRefusesWhileTheDrawModuleIsOff()
        {
            var host = new FakeExtrasHost();
            host.AnnotateOn = false;

            AppCommandResponse r = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));

            Assert.Equal("refused", r.Status);
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, r.Reason);
            Assert.Equal("The draw-on-capture editor is switched off. Turn it on in the settings of CyrFlip.", r.Message);
            Assert.Equal(0, host.AnnotateStarts);
            AssertNothingWasWritten(host);
        }

        [Fact]
        public async Task AnnotateSavesTheComposedPictureUnderTheOriginalFreezeInstant()
        {
            var host = new FakeExtrasHost();

            AppCommandResponse r = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));

            Assert.Equal("done", r.Status);
            Assert.Equal(0, r.ExitCode);
            Assert.Equal(@"C:\x\a.png", r.Data!["path"]);
            Assert.Equal(40, r.Data["width"]);
            Assert.Equal(30, r.Data["height"]);
            Assert.Equal(Start, ScreenshotSaverCommandTests.TimeChunk(host.Fs.Read(@"C:\x\a.png")!));
            Assert.False(host.Fs.AnyTemp());
            Assert.Equal(1, host.Picture.Disposed);
            // Without --clipboard the composed picture goes nowhere but the file.
            Assert.Empty(host.ClipboardPngs);
            Assert.Equal(0, host.Counted);
        }

        [Fact]
        public async Task AnnotatePutsTheDrawnPictureOnTheClipboardOnlyAfterTheFileExists()
        {
            var host = new FakeExtrasHost();

            AppCommandResponse r = await Annotate(host, Out(@"C:\x\b.png", "--annotate", "--clipboard", "true"));

            Assert.Equal(0, r.ExitCode);
            Assert.Single(host.Events);
            Assert.Equal(1, host.Counted);
            Assert.True(host.Picture.Png.SequenceEqual(host.ClipboardPngs[0]));
        }

        [Fact]
        public async Task AnnotateAnswersCancelledForEscapeAndEditorCancelAndCaptureFailedForAFailedEditor()
        {
            var host = new FakeExtrasHost();
            host.Annotate = AnnotateMode.EscapeAtSelection;
            AppCommandResponse escape = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));
            Assert.Equal("failed", escape.Status);
            Assert.Equal(16, escape.ExitCode);
            Assert.Equal(AppCommandReasons.Cancelled, escape.Reason);
            Assert.Empty(host.Fs.Files);

            host = new FakeExtrasHost();
            host.Annotate = AnnotateMode.EditorCancel;
            AppCommandResponse cancelled = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));
            Assert.Equal(16, cancelled.ExitCode);
            Assert.Equal(AppCommandReasons.Cancelled, cancelled.Reason);
            Assert.Empty(host.Fs.Files);
            Assert.Equal(0, host.AnnotateCancels);

            host = new FakeExtrasHost();
            host.Annotate = AnnotateMode.EditorFails;
            AppCommandResponse editorFailed = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));
            Assert.Equal(1, editorFailed.ExitCode);
            Assert.Equal(AppCommandReasons.CaptureFailed, editorFailed.Reason);
            Assert.Empty(host.Fs.Files);
        }

        [Fact]
        public async Task AnnotateClosesTheOverlayAtTheSelectionBudgetAndAnswersNotProven()
        {
            int bound = AppCommandsScreenshotExtras.SelectionBoundMs;
            int grace = AppCommandsScreenshots.CancelGraceMs;
            AppCommandsScreenshotExtras.SelectionBoundMs = 40;
            AppCommandsScreenshots.CancelGraceMs = 20;
            try
            {
                var host = new FakeExtrasHost();
                host.Annotate = AnnotateMode.SilentSelection;

                AppCommandResponse r = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));

                Assert.Equal("failed", r.Status);
                Assert.Equal(2, r.ExitCode);
                Assert.Equal(AppCommandReasons.Timeout, r.Reason);
                Assert.Equal("The annotated capture was not finished in time. The selection and the editor were closed and nothing was saved.", r.Message);
                // The verb closed what it opened: the overlay, never an editor that is up.
                Assert.True(host.RegionCancels >= 1);
                Assert.Equal(0, host.AnnotateCancels);
                Assert.Empty(host.Fs.Files);
            }
            finally
            {
                AppCommandsScreenshotExtras.SelectionBoundMs = bound;
                AppCommandsScreenshots.CancelGraceMs = grace;
            }
        }

        [Fact]
        public async Task AnnotateClosesTheEditorAtTheRunsBoundAndAnswersNotProven()
        {
            int grace = AppCommandsScreenshots.CancelGraceMs;
            AppCommandsScreenshots.CancelGraceMs = 20;
            try
            {
                var host = new FakeExtrasHost();
                host.Annotate = AnnotateMode.SilentEditor;
                using var cancel = new CancellationTokenSource();
                host.OnAnnotateStarted = () => cancel.Cancel();

                AppCommandResponse r = await Annotate(host, Out(@"C:\x\a.png", "--annotate"), cancel.Token);

                Assert.Equal(2, r.ExitCode);
                Assert.Equal(AppCommandReasons.Timeout, r.Reason);
                Assert.True(host.AnnotateCancels >= 1);
                Assert.Empty(host.Fs.Files);
            }
            finally
            {
                AppCommandsScreenshots.CancelGraceMs = grace;
            }
        }

        [Fact]
        public async Task AnnotateAnswersBusyWhenACaptureIsAlreadyUnderWay()
        {
            var host = new FakeExtrasHost();
            host.AnnotateStart = CaptureStart.Busy;

            AppCommandResponse r = await Annotate(host, Out(@"C:\x\a.png", "--annotate"));

            Assert.Equal("busy", r.Status);
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(AppCommandReasons.Busy, r.Reason);
            Assert.Empty(host.Fs.Files);
        }

        [Fact]
        public async Task AnnotateWithAExistingFileIsRefusedWithoutOverwriteAndReplacedWithIt()
        {
            var host = new FakeExtrasHost();
            host.Fs.Files[@"C:\x\exists.png"] = new byte[] { 7, 7, 7 };

            AppCommandResponse refused = await Annotate(host, Out(@"C:\x\exists.png", "--annotate"));
            Assert.Equal(11, refused.ExitCode);
            Assert.Equal(AppCommandReasons.Exists, refused.Reason);
            Assert.True(host.Fs.Read(@"C:\x\exists.png")!.SequenceEqual(new byte[] { 7, 7, 7 }));

            AppCommandResponse replaced = await Annotate(host, Out(@"C:\x\exists.png", "--annotate", "--overwrite"));
            Assert.Equal(0, replaced.ExitCode);
            Assert.False(host.Fs.Read(@"C:\x\exists.png")!.SequenceEqual(new byte[] { 7, 7, 7 }));
        }

        [Fact]
        public void TheSelectionBudgetStaysTheWaveOneNumber()
        {
            // S0110's own criterion: the plain verb is bounded by its own 120 seconds, and the annotate spelling rations
            // its selection phase with the same number - the dispatch derives the plain spelling's token from it.
            Assert.Equal(120000, AppCommandsScreenshotExtras.SelectionBoundMs);
        }
    }
}
