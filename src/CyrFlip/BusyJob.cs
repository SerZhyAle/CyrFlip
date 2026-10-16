using System;
using System.Globalization;
using System.Threading;

namespace CyrFlip
{
    /// <summary>
    /// What <see cref="BusyDialog"/> shows right now: an immutable snapshot of a <see cref="BusyJob"/>,
    /// so the window never reads a half-written report. Pure - no WinForms - so the whole "message,
    /// count, bar, cancel" matrix is unit-tested.
    /// </summary>
    internal sealed class BusyView
    {
        internal BusyView(string? message, int done, int total, bool cancellable, bool cancelRequested)
        {
            Message = string.IsNullOrWhiteSpace(message) ? null : message;
            Total = Math.Max(0, total);
            Done = Total == 0 ? 0 : Math.Max(0, Math.Min(Total, done));
            Cancellable = cancellable;
            CancelRequested = cancelRequested;
        }

        /// <summary>The job's own sentence ("Writing the notes.."), already localized; null = the dialog's default.</summary>
        public string? Message { get; }

        public int Done { get; }

        /// <summary>The amount of work, or 0 while it is not known (the indeterminate state).</summary>
        public int Total { get; }

        public bool Cancellable { get; }

        public bool CancelRequested { get; }

        /// <summary>The job reports real progress: a bar and a count are drawn, never a guess.</summary>
        public bool Determinate => Total > 0;

        /// <summary>0..1 of the bar; 0 while indeterminate.</summary>
        public double Fraction => Total > 0 ? (double)Done / Total : 0d;

        /// <summary>"12 / 40" - digits and a slash only, so it needs no translation; empty while indeterminate.</summary>
        public string Count => Determinate
            ? Done.ToString(CultureInfo.InvariantCulture) + " / " + Total.ToString(CultureInfo.InvariantCulture)
            : "";

        /// <summary>The Cancel button can be pressed: the job allows it and it has not been asked for already.</summary>
        public bool CancelEnabled => Cancellable && !CancelRequested;

        /// <summary>
        /// The line of text: the job's message (or <paramref name="fallback"/>) and, while determinate,
        /// the count after it. Once a cancel was asked for the line says so instead
        /// (<paramref name="cancelling"/>) - the work may take a moment to notice, and a window that
        /// looks unchanged after a click reads as one that did not hear it.
        /// </summary>
        public string Caption(string fallback, string cancelling)
        {
            if (CancelRequested) return cancelling;
            string text = Message ?? fallback;
            return Determinate ? text + "  " + Count : text;
        }
    }

    /// <summary>
    /// The conversation between a long job on the pool and the modal window that waits for it
    /// (APP-BEHAVIOUR rule 3, ticket S0060 P7-h): the job reports <b>real</b> progress, the user may ask it
    /// to stop, and stopping is an <b>outcome</b>, not a failure.
    ///
    /// <para>Cancellation is <b>cooperative</b>: <see cref="Token"/> is handed to the work, which checks
    /// it between units and either returns what it has (that work is kept) or throws
    /// <see cref="OperationCanceledException"/> - <see cref="BusyDialog"/> turns the latter into a quiet
    /// return, never into an error dialog. A job that does not look at the token simply finishes, and the
    /// caller reads <see cref="CancelRequested"/> to know the user had asked.</para>
    ///
    /// <para>Thread-safe: <see cref="Report(int, int, string)"/> is called from the worker, the snapshot
    /// is read by the window's timer on the UI thread. After <see cref="Finish"/> a cancel is refused -
    /// there is nothing left to cancel, and a late click must not be reported as an outcome.</para>
    /// </summary>
    internal sealed class BusyJob : IDisposable
    {
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private string? _message;
        private int _done;
        private int _total;
        private bool _cancelRequested;
        private bool _finished;
        private bool _disposed;

        /// <param name="cancellable">The window offers Cancel (and shows no wait cursor - rule 3: a wait
        /// cursor beside a live Cancel says the application is not listening).</param>
        public BusyJob(bool cancellable = false)
        {
            Cancellable = cancellable;
        }

        public bool Cancellable { get; }

        /// <summary>Handed to the work; cancelled by <see cref="RequestCancel"/>. Never throws, even after disposal.</summary>
        public CancellationToken Token => _disposed ? new CancellationToken(true) : _cancel.Token;

        public bool CancelRequested
        {
            get { lock (_gate) return _cancelRequested; }
        }

        /// <summary>Determinate progress: <paramref name="done"/> of <paramref name="total"/> units, with an optional sentence.</summary>
        public void Report(int done, int total, string? message = null)
        {
            lock (_gate)
            {
                _done = done;
                _total = total;
                if (message != null) _message = message;
            }
        }

        /// <summary>Indeterminate progress: only a sentence ("Reading the file..").</summary>
        public void Report(string message)
        {
            lock (_gate)
            {
                _message = message;
                _done = 0;
                _total = 0;
            }
        }

        /// <summary>
        /// Ask the job to stop. True only for the request that took effect: the job allows it, it has not
        /// finished and it was not asked already - Escape, the close box and the button all land here.
        /// </summary>
        public bool RequestCancel()
        {
            lock (_gate)
            {
                if (!Cancellable || _finished || _cancelRequested) return false;
                _cancelRequested = true;
            }
            try { _cancel.Cancel(); }
            catch (ObjectDisposedException) { return true; } // released meanwhile: the flag above is the answer
            return true;
        }

        /// <summary>The job's work ended (completed, cancelled or failed): a later cancel is refused.</summary>
        public void Finish()
        {
            lock (_gate) _finished = true;
        }

        public BusyView Snapshot()
        {
            lock (_gate) return new BusyView(_message, _done, _total, Cancellable, _cancelRequested);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _cancel.Dispose();
        }
    }
}
