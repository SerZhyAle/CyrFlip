using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>What starting a capture came to. Everything but <see cref="Started"/> means no overlay and no picture exist.</summary>
    internal enum CaptureStart
    {
        Started,
        /// <summary>Another capture, the annotation editor, or the program's own end is under way (<c>CaptureBusy</c>).</summary>
        Busy,
        GrabFailed,
        OverlayFailed,
        /// <summary>The named window has no on-screen rectangle - minimized or entirely outside the screens (ticket S0130).</summary>
        WindowHidden,
    }

    /// <summary>A picture a capture hands over: its size and its two encodings, owned by the receiver, which disposes it.</summary>
    internal interface ICapturedPicture : IDisposable
    {
        int Width { get; }
        int Height { get; }
        /// <summary>The PNG the clipboard gets (no <c>tIME</c> chunk - the saver adds it for a file).</summary>
        byte[] EncodePng();
        /// <summary>The bottom-up 32-bit DIB the clipboard gets beside the PNG.</summary>
        byte[] EncodeDib();
    }

    /// <summary>The finished capture: a picture, or null when the person cancelled; and the freeze instant either way.</summary>
    internal sealed class CaptureResult
    {
        public CaptureResult(ICapturedPicture? picture, DateTime started)
        {
            Picture = picture; Started = started;
        }

        public ICapturedPicture? Picture { get; }
        /// <summary>The local wall-clock moment the screens were frozen - what the name and the PNG <c>tIME</c> chunk carry (CAPTURE-OUTPUT rules 3 and 16).</summary>
        public DateTime Started { get; }
    }

    /// <summary>A <see cref="Bitmap"/> as a <see cref="ICapturedPicture"/>: owns it, disposes it once.</summary>
    internal sealed class BitmapPicture : ICapturedPicture
    {
        private Bitmap? _bitmap;

        public BitmapPicture(Bitmap bitmap)
        {
            _bitmap = bitmap;
            Width = bitmap.Width;
            Height = bitmap.Height;
        }

        public int Width { get; }
        public int Height { get; }

        public byte[] EncodePng() => ClipboardImage.EncodePng(Held());
        public byte[] EncodeDib() => ClipboardImage.EncodeDib(Held());

        private Bitmap Held() => _bitmap ?? throw new ObjectDisposedException(nameof(BitmapPicture));

        public void Dispose()
        {
            Bitmap? bitmap = Interlocked.Exchange<Bitmap?>(ref _bitmap, null);
            bitmap?.Dispose();
        }
    }

    /// <summary>
    /// What the two screenshot verbs need from the running program (ticket S0110). The production host is a thin object
    /// over the chord's own capture core and clipboard write; the tests supply a scripted one. Members marked
    /// "UI thread" are called through <see cref="RunOnUi{T}"/>: the capture state (<c>CaptureBusy</c>, the overlay) has one
    /// owner, and the check and the overlay's creation are one turn of it.
    /// </summary>
    internal interface IScreenshotCommandHost
    {
        /// <summary>The saver (and so the file system) a command writes through.</summary>
        ScreenshotSaver Saver { get; }

        /// <summary>UI thread: freeze the screens and show the selection; <paramref name="done"/> runs once, on the UI thread, with the crop or a cancel.</summary>
        CaptureStart StartRegion(Action<CaptureResult> done);

        /// <summary>UI thread: freeze the screens now; <paramref name="done"/> runs before this returns with the whole virtual screen.</summary>
        CaptureStart StartFull(Action<CaptureResult> done);

        /// <summary>UI thread: close the selection overlay if it is up (<c>StartRegion</c>'s callback then gets a cancel).</summary>
        void CancelRegion();

        /// <summary>Worker thread: PNG + DIB onto the clipboard, behind the clipboard guard; false when another program holds it.</summary>
        bool PutOnClipboard(byte[] png, byte[] dib);

        /// <summary>The picture reached the clipboard: <c>ScreenshotCount</c> rises (its documented meaning).</summary>
        void CountScreenshot();

        /// <summary>The frame, the crop and the encodings are large-object garbage the moment a command ends (the 50 MB idle budget).</summary>
        void Compact();

        /// <summary>A delay that holds no thread; cancelled by the verb's bound.</summary>
        Task DelayAsync(int milliseconds, CancellationToken token);

        Task<T> RunOnUi<T>(Func<T> work);

        /// <summary>A network path (UNC, a mapped drive) - never probed on the UI thread, told apart in the answer of a timeout.</summary>
        bool IsRemote(string path);
    }

    /// <summary>
    /// <c>screenshot region</c> and <c>screenshot full</c> (ticket S0110, APP-COMMANDS 0.2): a chosen part of the screen, or
    /// all of it after an optional delay, straight into a file the caller names. Each verb is a pure function over the
    /// parsed invocation, the caller's working directory and a host, so the tests need no window and no screen.
    ///
    /// <para>Order: the arguments (10) -> the target on the pool (10 / 11 / 14 / 15) -> for <c>full</c> the delay, off the
    /// thread the keyboard hook shares -> the freeze and, for <c>region</c>, the person's selection (16 on a cancel)
    /// -> PNG on the pool -> the file (11 / 13 / 14 / 15) -> the clipboard only when asked, and only after the file
    /// is on disk (1 <c>clipboard-failed</c>, the file stays). The target is checked before a person is bothered and again
    /// when the file is written. The verbs write only the named target: no capture folder, no fallback, no balloon, and
    /// neither <c>ScreenshotSaveEnabled</c> nor a chord switch is read. Nothing of the caller's path goes into a message,
    /// an exception name or a log line.</para>
    /// </summary>
    internal static class AppCommandsScreenshots
    {
        /// <summary>The longest <c>--delay-ms</c> (APP-COMMANDS draft manifest of the screenshot group).</summary>
        public const int MaxDelayMs = 30000;

        /// <summary>
        /// How long a closed overlay gets to report its cancel before the verb stops waiting for it. A field, not a
        /// constant, only so a test can shorten it.
        /// </summary>
        internal static int CancelGraceMs = 2000;

        public static Task<AppCommandResponse> ScreenshotRegion(AppCommandInvocation invocation, string workingDir,
            IScreenshotCommandHost host, Func<string, string> T, CancellationToken token)
            => Run(invocation, workingDir, host, T, token, region: true);

        public static Task<AppCommandResponse> ScreenshotFull(AppCommandInvocation invocation, string workingDir,
            IScreenshotCommandHost host, Func<string, string> T, CancellationToken token)
            => Run(invocation, workingDir, host, T, token, region: false);

        private static async Task<AppCommandResponse> Run(AppCommandInvocation invocation, string workingDir,
            IScreenshotCommandHost host, Func<string, string> T, CancellationToken token, bool region)
        {
            CaptureResult? captured = null;
            try
            {
                int delay = 0;
                string? rawDelay = region ? null : invocation.Option("delay-ms");
                if (rawDelay != null
                    && !(int.TryParse(rawDelay, NumberStyles.Integer, CultureInfo.InvariantCulture, out delay)
                         && delay >= 0 && delay <= MaxDelayMs))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        Localization.Format(T, "The delay must be a whole number of milliseconds from {0} to {1}.", 0, MaxDelayMs));
                string target = invocation.Option("out") ?? "";
                bool overwrite = invocation.Flag("overwrite");
                bool clipboard = invocation.Flag("clipboard");

                // The first look at the target - before anybody is asked to select anything.
                TargetCheck check = await CheckTarget(target, workingDir, overwrite, host, T, token);
                if (check.Plan == null) return check.Refusal!;
                OutputPlan plan = check.Plan;

                if (!region && delay > 0)
                {
                    try { await host.DelayAsync(delay, token); }
                    catch (OperationCanceledException) { return TimedOut(T); }
                }

                var finished = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                CaptureStart start = await host.RunOnUi(() => region
                    ? host.StartRegion(result => finished.TrySetResult(result))
                    : host.StartFull(result => finished.TrySetResult(result)));
                if (start == CaptureStart.Busy)
                    return AppCommandResponse.BusyNow(T("A screen capture is already under way. Finish or cancel it, then try again."));
                if (start != CaptureStart.Started)
                    return AppCommandResponse.Failed(AppCommandReasons.CaptureFailed, AppCommandExit.Failed,
                        T("The screen could not be captured. Nothing was saved."));

                captured = region ? await AwaitSelection(finished.Task, host, token) : await finished.Task;
                if (captured == null) return TimedOut(T);

                ICapturedPicture? picture = captured.Picture;
                if (picture == null)
                    return token.IsCancellationRequested
                        ? TimedOut(T)
                        : AppCommandResponse.Failed(AppCommandReasons.Cancelled, AppCommandExit.Cancelled,
                            T("The selection was cancelled. Nothing was saved."));
                // Nothing is written after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested) return TimedOut(T);

                return await Deliver(picture, captured.Started, plan, clipboard, target, workingDir, host, T);
            }
            catch (Exception ex)
            {
                ScreenshotLog.Log("command failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                    T("The command failed. The log bundle on the About page has the details."));
            }
            finally
            {
                // A picture that never reached Deliver (a fault between the capture and it) is still ours to release.
                captured?.Picture?.Dispose();
            }
        }

        /// <summary>The plan for <c>--out</c>, or the answer that says why there is none.</summary>
        internal sealed class TargetCheck
        {
            public OutputPlan? Plan { get; set; }
            public AppCommandResponse? Refusal { get; set; }
        }

        /// <summary>
        /// The plan for <c>--out</c>, worked out on the pool under the saver's budget: a path may meet a share that does
        /// not answer, and the thread that asks must never be the one the keyboard hook shares.
        /// </summary>
        internal static async Task<TargetCheck> CheckTarget(string target, string workingDir, bool overwrite,
            IScreenshotCommandHost host, Func<string, string> T, CancellationToken token)
        {
            IScreenshotFileSystem fs = host.Saver.FileSystem;
            Task<OutputResolution> work = Task.Run(() => OutputPlan.Resolve(target, workingDir, overwrite, fs));
            Task winner;
            using (CancellationTokenSource stopTimer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                winner = await Task.WhenAny(work, Task.Delay(host.Saver.Budget, stopTimer.Token));
                stopTimer.Cancel();
            }
            if (winner != work)
            {
                // Nobody will read what the abandoned probe says; its fault must not surface later.
                _ = work.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return new TargetCheck
                {
                    Refusal = token.IsCancellationRequested ? TimedOut(T) : Unreachable(target, workingDir, host, T),
                };
            }
            if (work.Status != TaskStatus.RanToCompletion)
            {
                _ = work.Exception;
                return new TargetCheck { Refusal = Unreachable(target, workingDir, host, T) };
            }

            OutputResolution resolution = work.Result;
            if (resolution.Plan != null) return new TargetCheck { Plan = resolution.Plan };
            return new TargetCheck
            {
                Refusal = new AppCommandResponse
                {
                    Status = resolution.Status,
                    ExitCode = resolution.ExitCode,
                    Reason = resolution.Reason,
                    Message = MessageOf(resolution.Problem, T),
                },
            };
        }

        internal static string MessageOf(OutputProblem problem, Func<string, string> T)
        {
            switch (problem)
            {
                case OutputProblem.NotPng:
                    return T("--out must be a file name ending in .png, an existing folder, or a path ending in a backslash.");
                case OutputProblem.MissingFolder:
                    return T("The folder in --out does not exist. CyrFlip does not create it.");
                case OutputProblem.Exists:
                    return T("A file with this name already exists. Choose another name, or add --overwrite to replace it.");
                default:
                    return T("--out is not a path Windows accepts here: no device names, no control characters, none of < > \" | * ?, no name ending in a space or a dot (or with a space before its extension), a colon only after the drive letter.");
            }
        }

        /// <summary>
        /// The person's selection, under the verb's bound: at the bound the instance closes the overlay itself - on the UI
        /// thread's turn - so a caller that gave up never leaves a stuck overlay behind and <c>CaptureBusy</c> clears. Null
        /// only when the overlay did not report even then.
        /// </summary>
        internal static async Task<CaptureResult?> AwaitSelection(Task<CaptureResult> finished, IScreenshotCommandHost host, CancellationToken token)
        {
            var bound = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() =>
            {
                bound.TrySetResult(true);
                _ = host.RunOnUi(() => { host.CancelRegion(); return true; });
            }))
            {
                if (await Task.WhenAny(finished, bound.Task) == finished) return finished.Result;
                await Task.WhenAny(finished, Task.Delay(CancelGraceMs));
                return finished.IsCompleted ? finished.Result : null;
            }
        }

        internal static async Task<AppCommandResponse> Deliver(ICapturedPicture picture, DateTime started, OutputPlan plan,
            bool clipboard, string target, string workingDir, IScreenshotCommandHost host, Func<string, string> T)
        {
            int width = picture.Width, height = picture.Height;
            byte[] png = new byte[0];
            byte[] dib = new byte[0];
            try
            {
                try
                {
                    // CPU work on a large bitmap: the pool, never the thread the keyboard hook shares.
                    await Task.Run(() =>
                    {
                        png = picture.EncodePng();
                        if (clipboard) dib = picture.EncodeDib();
                    });
                }
                finally
                {
                    // The frame is the one large allocation; it goes before any file or clipboard wait.
                    picture.Dispose();
                }

                ScreenshotSaver.SaveToResult saved = await Task.Run(() => host.Saver.SaveTo(png, started, plan));
                switch (saved.Outcome)
                {
                    case ScreenshotSaver.SaveToOutcome.Saved:
                        var data = new Dictionary<string, object>
                        {
                            ["path"] = saved.FilePath ?? "",
                            ["width"] = width,
                            ["height"] = height,
                        };
                        if (!clipboard) return AppCommandResponse.Done(T("Saved the capture."), data);
                        // Clipboard last: a file that failed leaves the clipboard exactly as the person had it.
                        bool onClipboard = await Task.Run(() => host.PutOnClipboard(png, dib));
                        if (onClipboard)
                        {
                            host.CountScreenshot();
                            return AppCommandResponse.Done(T("Saved the capture and put it on the clipboard."), data);
                        }
                        AppCommandResponse held = AppCommandResponse.Failed(AppCommandReasons.ClipboardFailed, AppCommandExit.Failed,
                            T("The capture is saved, but another program holds the clipboard, so it is not on it."));
                        held.Data = data;
                        return held;
                    case ScreenshotSaver.SaveToOutcome.Exists:
                        return AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady, MessageOf(OutputProblem.Exists, T));
                    case ScreenshotSaver.SaveToOutcome.DiskFull:
                        return AppCommandResponse.Failed(AppCommandReasons.NoSpace, AppCommandExit.NoSpace,
                            T("The disk is full. The capture was not saved."));
                    case ScreenshotSaver.SaveToOutcome.FolderMissing:
                        return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                            MessageOf(OutputProblem.MissingFolder, T));
                    case ScreenshotSaver.SaveToOutcome.Timeout:
                        return IsRemote(target, workingDir, host)
                            ? AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                                T("The share did not answer in time. The capture was not saved."))
                            : AppCommandResponse.Failed(AppCommandReasons.IoError, AppCommandExit.IoError,
                                T("The file could not be written. The capture was not saved."));
                    default:
                        ScreenshotLog.Log("command save failed: " + saved.Reason);
                        return AppCommandResponse.Failed(AppCommandReasons.IoError, AppCommandExit.IoError,
                            T("The file could not be written. The capture was not saved."));
                }
            }
            finally
            {
                host.Compact();
            }
        }

        internal static AppCommandResponse Unreachable(string target, string workingDir, IScreenshotCommandHost host, Func<string, string> T)
            => IsRemote(target, workingDir, host)
                ? AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                    T("The share in --out did not answer in time. Nothing was captured."))
                : AppCommandResponse.Failed(AppCommandReasons.IoError, AppCommandExit.IoError,
                    T("The folder in --out could not be checked. Nothing was captured."));

        /// <summary>A path the caller named is remote by its own text, or - for a relative one - by the working directory it is taken from.</summary>
        internal static bool IsRemote(string target, string workingDir, IScreenshotCommandHost host)
        {
            if (host.IsRemote(target)) return true;
            bool rooted;
            try { rooted = Path.IsPathRooted(target); }
            catch (ArgumentException) { rooted = true; } // an illegal character: the path is the caller's own text, not the directory's
            return !rooted && host.IsRemote(workingDir);
        }

        internal static AppCommandResponse TimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("No area was chosen in time. The capture was closed and nothing was saved."));
    }
}
