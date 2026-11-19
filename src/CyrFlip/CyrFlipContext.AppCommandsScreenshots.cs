using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The running copy's side of <c>screenshot region</c>, <c>screenshot full</c> and <c>screenshot window</c> (tickets
    /// S0110 and S0130). The verbs are <see cref="AppCommandsScreenshots"/> and <see cref="AppCommandsScreenshotExtras"/>;
    /// this is the thin host over the capture core the chord uses (<c>BeginRegionCapture</c>, <c>BeginFullCapture</c>,
    /// <c>BeginWindowCapture</c>, the annotate hand-off, <c>TryPutScreenshotOnClipboard</c>) - it keeps no state of its
    /// own. A command and the person's chord never run together: both start on the UI thread, where the busy check and the
    /// overlay's creation are one turn. The token ends the work a second before the listener gives up, so a verb that was
    /// answered "not proven" has already closed its overlay and writes nothing afterwards. The plain <c>screenshot region</c>
    /// keeps its own selection budget (the wave-1 answer moment, byte for byte); the <c>--annotate</c> spelling and the window
    /// verb wait the manifest's completion bound, and the annotate handler rations the selection phase itself.
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private Task<AppCommandResponse> RunScreenshotCommand(AppCommandInvocation invocation, string workingDir)
        {
            string name = invocation.Spec.Name;
            bool annotate = name == "screenshot region" && invocation.Flag("annotate");
            // The plain region: the 120-second selection budget the wave-1 verb shipped (S0110) - the manifest's bound
            // now carries the annotate spelling's 600 seconds, so the plain verb's own moment is kept here.
            int bound = name == "screenshot region" && !annotate
                ? Math.Min(AppCommandsScreenshotExtras.SelectionBoundMs, invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000)
                : (invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000);
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            var host = new ScreenshotCommandHost(this);
            Task<AppCommandResponse> work;
            switch (name)
            {
                case "screenshot window":
                    work = AppCommandsScreenshotExtras.ScreenshotWindow(invocation, workingDir, host, T, cancel.Token);
                    break;
                case "screenshot region":
                    work = annotate
                        ? AppCommandsScreenshotExtras.ScreenshotRegionAnnotate(invocation, workingDir, host, T, cancel.Token)
                        : AppCommandsScreenshots.ScreenshotRegion(invocation, workingDir, host, T, cancel.Token);
                    break;
                default:
                    work = AppCommandsScreenshots.ScreenshotFull(invocation, workingDir, host, T, cancel.Token);
                    break;
            }
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        /// <summary>The finished selection of a command: the crop as a picture (null for a cancel), the frame released, the focus handed back.</summary>
        private CaptureStart StartRegionForCommand(Action<CaptureResult> done)
            => BeginRegionCapture(finish =>
            {
                Bitmap? cropped = CropAndRelease(finish);
                done(new CaptureResult(cropped == null ? null : new BitmapPicture(cropped), finish.Started));
            });

        /// <summary>The whole virtual screen, frozen now; nothing is activated - the person's windows stay as they were.</summary>
        private CaptureStart StartFullForCommand(Action<CaptureResult> done)
        {
            CaptureStart start = BeginFullCapture(out Bitmap? frame, out _, out DateTime started, out _);
            if (start == CaptureStart.Started) done(new CaptureResult(new BitmapPicture(frame!), started));
            return start;
        }

        /// <summary>
        /// The selection and then the draw-on-capture editor, for <c>screenshot region --annotate</c> (S0130): the chord's
        /// annotate hand-off with the command's completion instead of its delivery. A selection cancel is a cancel of the
        /// whole run; the editor takes the frozen frame and the freeze instant exactly as the chord's editor does
        /// (S0071 6.2 - the drawing minutes never move the name), and OK hands the composed picture back under the
        /// ORIGINAL instant. <paramref name="editing"/> says whether the editor came up.
        /// </summary>
        private CaptureStart StartRegionAnnotateForCommand(Action<CaptureResult> done, Action<bool> editing)
            => BeginRegionCapture(finish =>
            {
                if (!finish.Region.HasValue)
                {
                    // Escape or the selection budget's close: a cancel of the whole run, the frame released and the
                    // focus handed back exactly as the plain verb's cancel path does.
                    CropAndRelease(finish);
                    done(new CaptureResult(null, finish.Started));
                    return;
                }
                _regionOverlay = null;
                _ui?.Post(_ => finish.Overlay.Dispose(), null);
                AnnotationEditor? editor = NewAnnotationEditor(finish.Frame, finish.Screen, finish.Region.Value, finish.ReturnTo);
                if (editor == null)
                {
                    editing(false);
                    done(new CaptureResult(null, finish.Started));
                    return;
                }
                editing(true);
                editor.Delivered += composed =>
                {
                    _annotationEditor = null;
                    // Not from inside the editor's own event: its windows are still on the call stack.
                    _ui?.Post(_ => editor.Dispose(), null);
                    if (finish.ReturnTo != IntPtr.Zero) ForegroundActivator.Activate(finish.ReturnTo);
                    // The original freeze instant: the annotation minutes do not move the name.
                    done(new CaptureResult(new BitmapPicture(composed), finish.Started));
                };
                editor.Cancelled += () =>
                {
                    _annotationEditor = null;
                    _ui?.Post(_ => editor.Dispose(), null);
                    if (finish.ReturnTo != IntPtr.Zero) ForegroundActivator.Activate(finish.ReturnTo);
                    done(new CaptureResult(null, finish.Started));
                };
                ShowAnnotationEditor(editor);
            });

        /// <summary>The named window's crop, frozen and cut in one turn of the UI thread; nothing is activated.</summary>
        private CaptureStart StartWindowForCommand(IntPtr window, Action<CaptureResult> done)
        {
            CaptureStart start = BeginWindowCapture(window, out Bitmap? cropped, out DateTime started);
            if (start == CaptureStart.Started) done(new CaptureResult(new BitmapPicture(cropped!), started));
            else cropped?.Dispose();
            return start;
        }

        /// <summary>
        /// The window capture core (S0130): the busy check, the window's on-screen rectangle and the one freeze of the
        /// virtual screen, in one turn of the UI thread. The rectangle out of the frozen frame is the product's own
        /// single-BitBlt discipline - no second capture technology - so a covered window shows what the screen shows,
        /// and a window with no on-screen rectangle (minimized, entirely outside) <see cref="CaptureStart.WindowHidden"/>.
        /// On <see cref="CaptureStart.Started"/> the caller owns the crop.
        /// </summary>
        private CaptureStart BeginWindowCapture(IntPtr window, out Bitmap? cropped, out DateTime started)
        {
            cropped = null;
            started = DateTime.Now;
            if (_disposed || CaptureBusy) return CaptureStart.Busy;

            Rectangle screen = ScreenCapture.VirtualScreen;
            Rectangle bounds = WindowOnScreen(window, screen);
            if (bounds.Width <= 0 || bounds.Height <= 0) return CaptureStart.WindowHidden;

            // CAPTURE-OUTPUT rule 3: the name carries the moment the capture started - the freeze.
            started = DateTime.Now;
            Bitmap? frame = ScreenCapture.Grab(screen);
            if (frame == null) return CaptureStart.GrabFailed;
            try { cropped = ScreenCapture.Crop(frame, screen, bounds); }
            finally { frame.Dispose(); }
            return CaptureStart.Started;
        }

        /// <summary>
        /// The window's visible on-screen rectangle: the DWM extended frame bounds (the <see cref="WindowInterop.GetWindowRect"/>
        /// rectangle without the invisible resize borders, the shadowless bounds a capture wants), <see cref="WindowInterop.GetWindowRect"/>
        /// when the DWM call does not answer, cut to the virtual screen; empty when none of it meets the screens.
        /// </summary>
        private static Rectangle WindowOnScreen(IntPtr window, Rectangle screen)
        {
            WindowInterop.RECT raw;
            if (WindowInterop.DwmGetWindowAttribute(window, WindowInterop.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out raw, Marshal.SizeOf(typeof(WindowInterop.RECT))) != 0
                && !WindowInterop.GetWindowRect(window, out raw))
                return Rectangle.Empty;
            return ScreenCapture.ClampToVirtualScreen(
                new Rectangle(raw.Left, raw.Top, raw.Right - raw.Left, raw.Bottom - raw.Top), screen);
        }

        private sealed class ScreenshotCommandHost : IScreenshotExtrasCommandHost
        {
            private readonly CyrFlipContext _context;

            public ScreenshotCommandHost(CyrFlipContext context)
            {
                _context = context;
            }

            public ScreenshotSaver Saver { get; } = new ScreenshotSaver();

            public CaptureStart StartRegion(Action<CaptureResult> done) => _context.StartRegionForCommand(done);

            public CaptureStart StartFull(Action<CaptureResult> done) => _context.StartFullForCommand(done);

            public void CancelRegion() => _context._regionOverlay?.Cancel();

            public bool PutOnClipboard(byte[] png, byte[] dib) => _context.TryPutScreenshotOnClipboard(png, dib);

            public void CountScreenshot() => _context._config.IncrementScreenshotCount();

            public void Compact() => CompactAfterCapture();

            public Task DelayAsync(int milliseconds, CancellationToken token) => Task.Delay(milliseconds, token);

            public Task<T> RunOnUi<T>(Func<T> work)
            {
                var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                SynchronizationContext? ui = _context._ui;
                if (ui == null || _context._disposed)
                {
                    source.TrySetException(new ObjectDisposedException(nameof(CyrFlipContext)));
                    return source.Task;
                }
                ui.Post(_ =>
                {
                    try { source.TrySetResult(work()); }
                    catch (Exception ex) { source.TrySetException(ex); }
                }, null);
                return source.Task;
            }

            public bool IsRemote(string path) => LaunchTargets.IsRemotePath(path);

            public IReadOnlyList<SelectionWindow> TopLevelWindows() => CyrFlipContext.TopLevelWindows();

            public bool AnnotateEnabled => _context._config.EnableScreenshotAnnotate;

            public CaptureStart StartWindow(IntPtr window, Action<CaptureResult> done)
                => _context.StartWindowForCommand(window, done);

            public CaptureStart StartRegionAnnotate(Action<CaptureResult> done, Action<bool> editing)
                => _context.StartRegionAnnotateForCommand(done, editing);

            public void CancelAnnotateRun()
            {
                if (_context._regionOverlay != null) _context._regionOverlay.Cancel();
                else _context._annotationEditor?.Cancel();
            }
        }
    }
}
