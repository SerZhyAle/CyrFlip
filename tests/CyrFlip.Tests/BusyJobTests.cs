using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The conversation between a long job and its modal window (APP-BEHAVIOUR rule 3, ticket S0060
    /// P7-h): real progress, a cooperative cancel that is an outcome and not a failure, the wait cursor
    /// only where nothing can be cancelled - and the old contract (no job: a caption, no button, the
    /// 40 ms no-flash rule, the wait handle and never <c>Task.Wait</c>) unchanged.
    /// </summary>
    public class BusyJobTests
    {
        // ---- the pure state ----

        [Fact]
        public void AJobStartsIndeterminateWithNoWordsOfItsOwn()
        {
            using var job = new BusyJob();
            BusyView view = job.Snapshot();

            Assert.False(view.Determinate);
            Assert.Equal(0d, view.Fraction);
            Assert.Equal("", view.Count);
            Assert.Null(view.Message);
            Assert.Equal("Please wait", view.Caption("Please wait", "Cancelling.."));
        }

        [Fact]
        public void RealProgressGivesACountABarAndAnHonestFraction()
        {
            using var job = new BusyJob();
            job.Report(5, 20, "Writing the notes..");
            BusyView view = job.Snapshot();

            Assert.True(view.Determinate);
            Assert.Equal("5 / 20", view.Count);
            Assert.Equal(0.25, view.Fraction, 6);
            Assert.Equal("Writing the notes..  5 / 20", view.Caption("fallback", "Cancelling.."));
        }

        [Fact]
        public void ADoneBeyondTheTotalNeverDrawsMoreThanAFullBar()
        {
            using var job = new BusyJob();
            job.Report(99, 10);
            Assert.Equal(1d, job.Snapshot().Fraction);
            Assert.Equal("10 / 10", job.Snapshot().Count);

            job.Report(-4, 10);
            Assert.Equal(0d, job.Snapshot().Fraction);
        }

        [Fact]
        public void ANegativeOrZeroTotalIsIndeterminateNotADivisionByZero()
        {
            using var job = new BusyJob();
            job.Report(3, 0, "Reading..");
            Assert.False(job.Snapshot().Determinate);
            job.Report(3, -7);
            Assert.False(job.Snapshot().Determinate);
            Assert.Equal(0d, job.Snapshot().Fraction);
        }

        [Fact]
        public void ASentenceAloneReturnsToIndeterminateAndKeepsTheWords()
        {
            using var job = new BusyJob();
            job.Report(2, 8, "Importing..");
            job.Report("Saving the journal..");
            BusyView view = job.Snapshot();

            Assert.False(view.Determinate);
            Assert.Equal("Saving the journal..", view.Caption("fallback", "Cancelling.."));
        }

        [Fact]
        public void ABlankSentenceFallsBackToTheDialogsOwnText()
        {
            using var job = new BusyJob();
            job.Report(1, 4, "   ");
            Assert.Equal("Please wait  1 / 4", job.Snapshot().Caption("Please wait", "Cancelling.."));
        }

        // ---- cancel ----

        [Fact]
        public void AJobThatCannotBeCancelledRefusesTheRequest()
        {
            using var job = new BusyJob(cancellable: false);
            Assert.False(job.RequestCancel());
            Assert.False(job.CancelRequested);
            Assert.False(job.Token.IsCancellationRequested);
            Assert.False(job.Snapshot().CancelEnabled);
        }

        [Fact]
        public void TheFirstRequestTakesEffectAndEveryRoadIsTheSameOne()
        {
            using var job = new BusyJob(cancellable: true);
            Assert.True(job.Snapshot().CancelEnabled);

            Assert.True(job.RequestCancel());   // the button
            Assert.False(job.RequestCancel());  // Escape, a moment later: already asked
            Assert.False(job.RequestCancel());  // the close box

            Assert.True(job.CancelRequested);
            Assert.True(job.Token.IsCancellationRequested);
            BusyView view = job.Snapshot();
            Assert.False(view.CancelEnabled);
            Assert.Equal("Cancelling..", view.Caption("fallback", "Cancelling.."));
        }

        [Fact]
        public void ACancelAfterTheJobEndedIsRefusedAndNotReportedAsAnOutcome()
        {
            using var job = new BusyJob(cancellable: true);
            job.Finish();
            Assert.False(job.RequestCancel());
            Assert.False(job.CancelRequested);
            Assert.False(job.Token.IsCancellationRequested);
        }

        [Fact]
        public void TheTokenNeverThrowsAfterDisposal()
        {
            var job = new BusyJob(cancellable: true);
            job.Dispose();
            Assert.True(job.Token.IsCancellationRequested);
            Assert.Null(Record.Exception(() => job.RequestCancel()));
            Assert.Null(Record.Exception(() => job.Snapshot()));
        }

        [Fact]
        public void ConcurrentReportsAreAlwaysReadAsOneCoherentSnapshot()
        {
            using var job = new BusyJob(cancellable: true);
            var stop = new ManualResetEventSlim();
            var writers = Enumerable.Range(0, 3).Select(w => new Thread(() =>
            {
                for (int i = 1; i <= 5000 && !stop.IsSet; i++) job.Report(i % 11, 10, "w" + w);
            })).ToArray();
            foreach (Thread writer in writers) writer.Start();

            for (int i = 0; i < 2000; i++)
            {
                BusyView view = job.Snapshot();
                Assert.InRange(view.Done, 0, Math.Max(view.Total, 0));
                Assert.InRange(view.Fraction, 0d, 1d);
            }
            stop.Set();
            foreach (Thread writer in writers) writer.Join();
        }
    }

    /// <summary>The window, built and driven without waiting for a timer.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class BusyDialogTests
    {
        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the UI thread of the test hung");
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in All(child)) yield return deeper;
            }
        }

        [Fact]
        public void WithNoJobTheWindowIsWhatItAlwaysWas()
        {
            OnSta(() =>
            {
                using var dialog = new BusyDialog("English", Task.CompletedTask);
                Assert.Null(dialog.CancelControl);
                Assert.Null(dialog.Bar);
                Assert.True(dialog.UseWaitCursor);
                Assert.Null(dialog.CancelButton);
                Assert.Equal("Please wait, CyrFlip is processing the data..", dialog.MessageText);
            });
        }

        [Fact]
        public void TheWaitCursorIsShownOnlyWhereTheWorkCannotBeCancelled()
        {
            OnSta(() =>
            {
                using var fixedJob = new BusyJob(cancellable: false);
                using (var dialog = new BusyDialog("English", Task.CompletedTask, fixedJob))
                {
                    Assert.True(dialog.UseWaitCursor);
                    Assert.Null(dialog.CancelControl);
                }
                using var cancellable = new BusyJob(cancellable: true);
                using (var dialog = new BusyDialog("English", Task.CompletedTask, cancellable))
                {
                    Assert.False(dialog.UseWaitCursor, "a wait cursor beside a live Cancel says the app is not listening");
                    Assert.NotNull(dialog.CancelControl);
                    Assert.Same(dialog.CancelControl, dialog.CancelButton); // Escape is that button
                }
            });
        }

        [Fact]
        public void TheReportIsMappedToTheLabelTheBarAndTheButton()
        {
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                using var dialog = new BusyDialog("English", new TaskCompletionSource<int>().Task, job);
                dialog.Show(); // a control reports itself invisible while its window is not on screen

                job.Report(5, 20, "Writing the notes..");
                dialog.RefreshFromJob();
                Assert.Equal("Writing the notes..  5 / 20", dialog.MessageText);
                Assert.True(dialog.Bar!.Visible);
                Assert.Equal(0.25, dialog.Bar.Fraction, 6);
                Assert.True(dialog.CancelControl!.Enabled);

                job.Report("Saving..");
                dialog.RefreshFromJob();
                Assert.Equal("Saving..", dialog.MessageText);
                Assert.False(dialog.Bar.Visible, "an indeterminate job draws no bar - it would be a guess");
            });
        }

        [Fact]
        public void TheButtonTheEscapeKeyAndTheCloseBoxAreOneRequest()
        {
            OnSta(() =>
            {
                var work = new TaskCompletionSource<int>();

                // The button (a button that is not on a shown window cannot be clicked).
                using (var job = new BusyJob(cancellable: true))
                using (var dialog = new BusyDialog("English", work.Task, job))
                {
                    dialog.Show();
                    dialog.CancelControl!.PerformClick();
                    Assert.True(job.CancelRequested);
                    Assert.Equal("Cancelling..", dialog.MessageText);
                    Assert.False(dialog.CancelControl.Enabled);
                    Assert.False(dialog.IsDisposed, "the window stays until the work has really stopped");
                }

                // The close box / Alt+F4: the form's own closing path asks, and the close is refused.
                using (var job = new BusyJob(cancellable: true))
                using (var dialog = new BusyDialog("English", work.Task, job))
                {
                    dialog.Show();
                    dialog.Close();
                    Assert.True(job.CancelRequested);
                    Assert.False(dialog.IsDisposed);
                }

                // Escape is the CancelButton: its click is the same call.
                using (var job = new BusyJob(cancellable: true))
                using (var dialog = new BusyDialog("English", work.Task, job))
                {
                    dialog.Show();
                    dialog.CancelButton!.PerformClick();
                    Assert.True(job.CancelRequested);
                }
            });
        }

        [Fact]
        public void AWindowWithNoCancellableJobStillCannotBeClosedByTheUser()
        {
            OnSta(() =>
            {
                var work = new TaskCompletionSource<int>();
                using var dialog = new BusyDialog("English", work.Task);
                dialog.Show();
                dialog.Close();
                Assert.False(dialog.IsDisposed);
                Assert.True(dialog.Visible);
            });
        }

        [Fact]
        public void EveryLanguageFitsTheMessageTheBarAndTheButton()
        {
            var problems = new List<string>();
            OnSta(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    string code = Localization.Codes[i];
                    using var job = new BusyJob(cancellable: true);
                    job.Report(7, 20, Localization.Translate(language, "Please wait, CyrFlip is processing the data.."));
                    using var dialog = new BusyDialog(language, new TaskCompletionSource<int>().Task, job);
                    dialog.RefreshFromJob();
                    dialog.CreateControl();
                    dialog.PerformLayout();

                    foreach (Control control in All(dialog).Where(c => c is Label || c is Button))
                    {
                        Size wanted = control.GetPreferredSize(Size.Empty);
                        if (wanted.Width > control.Width || wanted.Height > control.Height)
                            problems.Add($"[{language}] {control.GetType().Name} \"{control.Text}\": needs {wanted}, got {control.Size}");
                        if (code != "ru" && code != "uk" && control.Text.Any(c => c >= 'Ѐ' && c <= 'ӿ'))
                            problems.Add($"[{language}] {control.GetType().Name} shows Cyrillic: \"{control.Text}\"");
                    }
                    Assert.Equal(Localization.IsRightToLeft(language) ? RightToLeft.Yes : RightToLeft.No, dialog.RightToLeft);

                    // After a cancel request the same window, in the same language, still fits.
                    dialog.AskToCancel();
                    dialog.PerformLayout();
                    Label text = All(dialog).OfType<Label>().First();
                    Size cancelling = text.GetPreferredSize(Size.Empty);
                    if (cancelling.Width > text.Width || cancelling.Height > text.Height)
                        problems.Add($"[{language}] the \"Cancelling..\" line is clipped: needs {cancelling}, got {text.Size}");
                }
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        // ---- the static entry points, with the real modal loop ----

        [Fact]
        public void RunReturnsQuietlyWhenTheWorkIsCancelled()
        {
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                // The request comes from another thread, as a click would - after the 40 ms grace, so
                // the window really is up and has to close itself when the work ends.
                Task.Run(() => { Thread.Sleep(250); job.RequestCancel(); });

                int result = BusyDialog.Run<int>(null, "English", () =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        job.Token.ThrowIfCancellationRequested();
                        job.Report(i, 1000);
                        Thread.Sleep(10);
                    }
                    return 7;
                }, job);

                Assert.Equal(0, result);            // cancelled: no value, and no exception
                Assert.True(job.CancelRequested);   // the caller reads the outcome here
            });
        }

        [Fact]
        public void WorkThatKeepsWhatItDidIsReturnedAndStillReportedAsCancelled()
        {
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                Task.Run(() => { Thread.Sleep(250); job.RequestCancel(); });

                int done = BusyDialog.Run<int>(null, "English", () =>
                {
                    int kept = 0;
                    while (!job.Token.IsCancellationRequested) { kept++; job.Report(kept, 100000); Thread.Sleep(10); }
                    return kept; // "work already completed is kept rather than discarded"
                }, job);

                Assert.True(done > 0);
                Assert.True(job.CancelRequested);
            });
        }

        [Fact]
        public void AFailureIsStillAFailureWithAJob()
        {
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                Assert.Throws<InvalidOperationException>(() => BusyDialog.Run(null, "English", () =>
                {
                    Thread.Sleep(150);
                    throw new InvalidOperationException("disk");
                }, job));
                Assert.False(job.CancelRequested);
            });
        }

        [Fact]
        public void AnOperationCancelledForAnotherReasonIsStillAnException()
        {
            // Cancelled is an outcome only when the USER asked for it; a token cancelled elsewhere is not.
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                Assert.Throws<OperationCanceledException>(() => BusyDialog.Run(null, "English", () =>
                {
                    Thread.Sleep(100);
                    throw new OperationCanceledException();
                }, job));
            });
        }

        [Fact]
        public void FastWorkNeverShowsTheWindowAndLeavesNothingToCancel()
        {
            OnSta(() =>
            {
                using var job = new BusyJob(cancellable: true);
                int result = BusyDialog.Run(null, "English", () => 5, job);
                Assert.Equal(5, result);
                Assert.False(job.RequestCancel(), "the job ended - there is nothing left to cancel");
            });
        }

        // ---- the rules the old file held, still held ----

        [Fact]
        public void TheWaitIsAHandleAndNeverTaskWait()
        {
            string source = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "BusyDialog.cs")));
            Assert.Contains("AsyncWaitHandle.WaitOne(GraceMs)", source);
            Assert.DoesNotContain("task.Wait(", source);
            Assert.DoesNotContain(".Wait();", source);
        }

        [Fact]
        public void TheGraceStaysFarUnderTheHooksTimeout()
            => Assert.InRange(BusyDialog.GraceMs, 1, 100);
    }
}
