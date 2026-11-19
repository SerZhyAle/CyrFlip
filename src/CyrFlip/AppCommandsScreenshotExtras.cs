using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// What the two capture-extra verbs need from the running program beyond the wave-1 screenshot host (ticket
    /// S0130): the window list of the selection family, the draw module's switch, a window capture, the annotate
    /// hand-off and its close. The production host is the same thin object over the chord's own capture core; the
    /// tests supply a scripted one. Members marked "UI thread" are called through <see cref="IScreenshotCommandHost.RunOnUi{T}"/>.
    /// </summary>
    internal interface IScreenshotExtrasCommandHost : IScreenshotCommandHost
    {
        /// <summary>The visible, titled top-level windows with the pid beside each - the selection family's own list, shared so the naming options cannot drift.</summary>
        IReadOnlyList<SelectionWindow> TopLevelWindows();

        /// <summary>The draw-on-capture module's switch: <c>--annotate</c> is a module option and answers <c>module-off</c> while it is off.</summary>
        bool AnnotateEnabled { get; }

        /// <summary>UI thread: freeze the screens and hand over the named window's on-screen rectangle; <paramref name="done"/> runs before this returns, with the crop.</summary>
        CaptureStart StartWindow(IntPtr window, Action<CaptureResult> done);

        /// <summary>
        /// UI thread: the region selection, then the draw-on-capture editor. <paramref name="editing"/> runs once when the selection
        /// ends - true when the editor is up, false when it could not be - and <paramref name="done"/> runs once, on the UI thread,
        /// with the composed picture, or null when the person cancelled.
        /// </summary>
        CaptureStart StartRegionAnnotate(Action<CaptureResult> done, Action<bool> editing);

        /// <summary>UI thread: close whatever an annotate run left open - the selection overlay or the editor - so <c>CaptureBusy</c> clears.</summary>
        void CancelAnnotateRun();
    }

    /// <summary>
    /// <c>screenshot window</c> and the <c>--annotate</c> option of <c>screenshot region</c> (ticket S0130, APP-COMMANDS 0.3, the
    /// research rows C45 and C46): a script captures one named window without a keyboard, and a docs writer asks for the editor
    /// the person draws in. Both are pure functions over the parsed invocation, the caller's working directory and a host, exactly
    /// as the wave-1 screenshot verbs are.
    ///
    /// <para>Order inside <c>screenshot window</c>: the target (<c>CheckTarget</c>, the wave-1 rule set - a bad <c>--out</c> answers
    /// without freezing anything) -> the window over the selection family's own list and matcher (<c>AppCommandsSelection.Named</c>:
    /// neither option 10, no match 11 <c>target-missing</c>, more than one 10 <c>bad-argument</c>) -> the freeze and the crop in one
    /// turn of the UI thread (<c>busy</c> 11, a window with no on-screen rectangle 11 <c>target-missing</c>, a failed freeze 1
    /// <c>capture-failed</c>) -> the delivery of the wave-1 verbs with the clipboard never touched. The verb writes only the named
    /// target and reads no chord switch and no module switch.</para>
    ///
    /// <para>Order inside <c>screenshot region --annotate</c>: the module (<c>module-off</c> 11 while <c>EnableScreenshotAnnotate</c>
    /// is off) -> the target -> the selection and the editor through the same capture core and the same editor the annotate chords
    /// use -> the composed picture delivered with the ORIGINAL freeze instant. The selection sits under the wave-1 verb's own
    /// 120-second budget; the drawing sits under the whole run's bound, and at that bound the instance closes what it opened - the
    /// editor or the overlay - so a caller that gave up never leaves a person drawing into an editor they did not ask for. Escape
    /// during the selection and Cancel in the editor are 16 with nothing written.</para>
    /// </summary>
    internal static class AppCommandsScreenshotExtras
    {
        /// <summary>
        /// The selection budget of the annotate spelling's first phase - the wave-1 verb's own number (S0110): nobody choosing within
        /// it is closed out of the run, and whatever the person then draws for is never disturbed by it.
        /// </summary>
        /// <summary>A field, not a constant, only so a test can shorten the wait (the S0110 precedent of CancelGraceMs).</summary>
        internal static int SelectionBoundMs = 120000;

        public static async Task<AppCommandResponse> ScreenshotWindow(AppCommandInvocation invocation, string workingDir,
            IScreenshotExtrasCommandHost host, Func<string, string> T, CancellationToken token)
        {
            CaptureResult? captured = null;
            try
            {
                string target = invocation.Option("out") ?? "";
                bool overwrite = invocation.Flag("overwrite");

                // The target first - a bad --out answers without freezing anything.
                AppCommandsScreenshots.TargetCheck check =
                    await AppCommandsScreenshots.CheckTarget(target, workingDir, overwrite, host, T, token);
                if (check.Plan == null) return check.Refusal!;
                OutputPlan plan = check.Plan;

                // The window - the selection family's own matcher and sentences, over the same list (APP-COMMANDS item Q).
                IntPtr windowHandle = AppCommandsSelection.Named(host.TopLevelWindows(), invocation, T, out AppCommandResponse? naming);
                if (naming != null) return naming;

                // The busy check and the freeze are one turn of the UI thread: a chord and a command never run together.
                CaptureStart start = await host.RunOnUi(() => host.StartWindow(windowHandle, result => captured = result));
                if (start == CaptureStart.Busy)
                    return AppCommandResponse.BusyNow(T("A screen capture is already under way. Finish or cancel it, then try again."));
                if (start == CaptureStart.WindowHidden)
                    return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        T("The named window is minimized or outside the screens, so there is nothing on the screen to capture. Restore it, then run the command again."));
                if (start != CaptureStart.Started)
                {
                    ScreenshotLog.Log("window command start failed: " + start);
                    return AppCommandResponse.Failed(AppCommandReasons.CaptureFailed, AppCommandExit.Failed,
                        T("The screen could not be captured. Nothing was saved."));
                }
                if (token.IsCancellationRequested)
                    return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                        T("The capture was not finished in time. Nothing was saved."));
                CaptureResult window = captured!;

                // The delivery of the wave-1 verbs, with the clipboard never touched: the row lists no --clipboard.
                return await AppCommandsScreenshots.Deliver(window.Picture!, window.Started, plan,
                    clipboard: false, target, workingDir, host, T);
            }
            catch (Exception ex)
            {
                ScreenshotLog.Log("window command failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                    T("The command failed. The log bundle on the About page has the details."));
            }
            finally
            {
                // A picture that never reached Deliver (a fault between the freeze and it) is still ours to release.
                captured?.Picture?.Dispose();
            }
        }

        public static async Task<AppCommandResponse> ScreenshotRegionAnnotate(AppCommandInvocation invocation, string workingDir,
            IScreenshotExtrasCommandHost host, Func<string, string> T, CancellationToken token)
        {
            CaptureResult? captured = null;
            try
            {
                if (!host.AnnotateEnabled)
                    return AppCommandVerbs.ModuleOff(T("The draw-on-capture editor is switched off. Turn it on in the settings of CyrFlip."));

                string target = invocation.Option("out") ?? "";
                bool overwrite = invocation.Flag("overwrite");
                bool clipboard = invocation.Flag("clipboard");

                // The target first - a bad --out answers before the person is bothered with a selection.
                AppCommandsScreenshots.TargetCheck check =
                    await AppCommandsScreenshots.CheckTarget(target, workingDir, overwrite, host, T, token);
                if (check.Plan == null) return check.Refusal!;
                OutputPlan plan = check.Plan;

                var finished = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var editing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CaptureStart start = await host.RunOnUi(() => host.StartRegionAnnotate(result => finished.TrySetResult(result),
                    editorUp => editing.TrySetResult(editorUp)));
                if (start == CaptureStart.Busy)
                    return AppCommandResponse.BusyNow(T("A screen capture is already under way. Finish or cancel it, then try again."));
                if (start != CaptureStart.Started)
                {
                    ScreenshotLog.Log("annotate command start failed: " + start);
                    return AppCommandResponse.Failed(AppCommandReasons.CaptureFailed, AppCommandExit.Failed,
                        T("The screen could not be captured. Nothing was saved."));
                }

                AnnotateOutcome outcome = await AwaitAnnotateRun(finished.Task, editing.Task, host, token);
                CaptureResult? report = outcome.Result;
                captured = report;
                if (outcome.TimedOut || report == null)
                    return AnnotateTimedOut(T);
                if (outcome.EditorFailed)
                    return AppCommandResponse.Failed(AppCommandReasons.CaptureFailed, AppCommandExit.Failed,
                        T("The screen could not be captured. Nothing was saved."));
                // Nothing is written after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested)
                    return AnnotateTimedOut(T);
                ICapturedPicture? picture = report.Picture;
                if (picture == null)
                    return AppCommandResponse.Failed(AppCommandReasons.Cancelled, AppCommandExit.Cancelled,
                        T("The selection was cancelled. Nothing was saved."));
                return await AppCommandsScreenshots.Deliver(picture, report.Started, plan, clipboard, target, workingDir, host, T);
            }
            catch (Exception ex)
            {
                ScreenshotLog.Log("annotate command failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                    T("The command failed. The log bundle on the About page has the details."));
            }
            finally
            {
                // A picture that never reached Deliver (a fault between the compose and it) is still ours to release.
                captured?.Picture?.Dispose();
            }
        }

        /// <summary>The annotate run's wait: its report, and whether one of the verb's own budgets closed it.</summary>
        private sealed class AnnotateOutcome
        {
            public AnnotateOutcome(CaptureResult? result, bool timedOut, bool editorFailed = false)
            {
                Result = result; TimedOut = timedOut; EditorFailed = editorFailed;
            }

            /// <summary>The run's report - a picture, or a cancel - or null when nothing reported even after the grace.</summary>
            public CaptureResult? Result { get; }
            /// <summary>One of the verb's own budgets closed the run: whatever it reported, the answer is "not proven".</summary>
            public bool TimedOut { get; }
            /// <summary>The editor could not be opened after a chosen selection: the run is a failed capture, not a cancel.</summary>
            public bool EditorFailed { get; }
        }

        private static AppCommandResponse AnnotateTimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("The annotated capture was not finished in time. The selection and the editor were closed and nothing was saved."));

        /// <summary>
        /// The annotate run's wait - two budgets in a row: the selection under the wave-1 verb's own 120-second budget (the overlay
        /// is closed at it, and only the overlay - the run goes on once the editor is up), then the drawing under the whole run's
        /// bound, where the editor is closed. A report that only the verb's own close produced - and a report that never comes even
        /// after the grace - is <see cref="AnnotateOutcome.TimedOut"/>: the verb answers "not proven" and writes nothing after it.
        /// </summary>
        private static async Task<AnnotateOutcome> AwaitAnnotateRun(Task<CaptureResult> finished, Task<bool> editing,
            IScreenshotExtrasCommandHost host, CancellationToken token)
        {
            // Phase one: the selection, closed at its own budget when nobody chooses - and never disturbed once the person has.
            using (CancellationTokenSource selectionBudget = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                selectionBudget.CancelAfter(SelectionBoundMs);
                var selectionOver = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = selectionBudget.Token.Register(() =>
                {
                    selectionOver.TrySetResult(true);
                    // The overlay only: an editor that is already up is never closed by the selection's budget.
                    _ = host.RunOnUi(() => { host.CancelRegion(); return true; });
                });

                while (true)
                {
                    Task winner = await Task.WhenAny(finished, editing, selectionOver.Task);
                    if (winner == finished)
                        // The run reported during the selection phase: a cancel (the person's Escape), or a picture a fast
                        // selection and a fast OK already composed. The verb closed nothing, so the report is the truth.
                        return new AnnotateOutcome(await finished, timedOut: false);
                    if (winner == editing)
                    {
                        if (await editing)
                            break; // the editor is up: the selection's budget is spent and the drawing has the run's own bound
                        // The editor did not come up: a chosen selection the product could not open its editor over is a
                        // failed capture, never a person's cancel.
                        await Task.WhenAny(finished, Task.Delay(AppCommandsScreenshots.CancelGraceMs));
                        return new AnnotateOutcome(finished.IsCompleted ? await finished : null, timedOut: !finished.IsCompleted,
                            editorFailed: true);
                    }
                    // The selection's budget fired. If the editor came up in that same moment the run stands; otherwise the
                    // overlay was closed by the verb, and whatever it reports within the grace - a cancel, even a
                    // last-moment picture - is the wave-1's own answer at its bound: not proven, the picture released.
                    if (editing.IsCompleted && await editing) break;
                    await Task.WhenAny(finished, Task.Delay(AppCommandsScreenshots.CancelGraceMs));
                    return new AnnotateOutcome(finished.IsCompleted ? await finished : null, timedOut: true);
                }
            }

            // Phase two: the drawing, under the whole run's bound - at it the editor is closed by the verb, and a report
            // that only our close produced is "not proven", never a cancelled the caller would retry by asking again.
            var runOver = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration runRegistration = token.Register(() =>
            {
                runOver.TrySetResult(true);
                _ = host.RunOnUi(() => { host.CancelAnnotateRun(); return true; });
            });
            if (await Task.WhenAny(finished, runOver.Task) == finished)
                return new AnnotateOutcome(await finished, timedOut: false);
            await Task.WhenAny(finished, Task.Delay(AppCommandsScreenshots.CancelGraceMs));
            return new AnnotateOutcome(finished.IsCompleted ? await finished : null, timedOut: true);
        }
    }
}
