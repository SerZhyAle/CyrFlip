using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// A small modal "please wait" window shown while a job runs on the pool (ticket S0030 HT-2).
    ///
    /// <para>It exists for one reason: the UI thread is also the thread both low-level hooks run on,
    /// and Windows silently drops a hook whose callback waits past <c>LowLevelHooksTimeout</c>
    /// (~300 ms). Reading a 20 MB exchange file, replaying the notes journal or writing a few thousand
    /// encrypted records is seconds of work - done on this thread, it froze the machine's keyboard
    /// with it. A modal window runs its own message loop, so the hooks keep being answered while the
    /// job runs elsewhere; and because <c>ShowDialog</c> disables every other window of the thread, the
    /// user cannot edit what the job is working on in the meantime.</para>
    ///
    /// <para>A job that finishes within <see cref="GraceMs"/> never shows the window at all - most
    /// imports of a handful of notes should not flash a dialog. The window cannot be closed by the
    /// user while the job runs unless the job can be stopped: with no <see cref="BusyJob"/> there is
    /// nothing to cancel it with, and closing it would only hand the thread back to a caller that is
    /// not finished.</para>
    ///
    /// <para><b>Progress and cancel (APP-BEHAVIOUR rule 3, ticket S0060 P7-h)</b> are opt-in and
    /// backward compatible: a caller that passes a <see cref="BusyJob"/> gets the job's own sentence, a
    /// real count and bar when the work reports one, and a Cancel button when the job allows it - the
    /// button, Escape and the close box are one path (<see cref="OnFormClosing"/>). Cancelling is
    /// cooperative and is an outcome, not a failure: the work stops at its next check, what it did is
    /// kept, and <c>Run</c> returns quietly (the caller reads <see cref="BusyJob.CancelRequested"/>).
    /// The wait cursor is shown only when the work cannot be cancelled. The window is still modal and
    /// pumps messages, so both hooks keep being answered.</para>
    /// </summary>
    internal sealed class BusyDialog : ThemedForm
    {
        /// <summary>How long the caller may wait before the window appears. Well under the hook timeout.</summary>
        public const int GraceMs = 40;

        /// <summary>How often the window re-reads the job's report - far below what the eye follows.</summary>
        internal const int PollMs = 100;

        private readonly Task _task;
        private readonly BusyJob? _job;
        private readonly Font? _ownFont;
        private readonly Label _message;
        private readonly BusyBar? _bar;
        private readonly Button? _cancel;
        private readonly System.Windows.Forms.Timer? _poll;
        private readonly string _defaultText;
        private readonly string _cancellingText;

        internal BusyDialog(string language, Task task, BusyJob? job = null)
        {
            HelpLanguage = language;
            _task = task;
            _job = job;
            string T(string en) => Localization.Translate(language, en);
            _defaultText = T("Please wait, CyrFlip is processing the data..");
            _cancellingText = T("Cancelling..");
            Text = "CyrFlip";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ControlBox = false; ShowIcon = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }
            int wrap = TextRenderer.MeasureText(new string('x', 48), Font).Width;

            var layout = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
                Padding = new Padding(12), Dock = DockStyle.Fill,
            };
            // The bar is drawn here, from the palette's roles: a stock ProgressBar is drawn by Windows in
            // its own colours and would be one more declared exception to the theme (S0020).
            _message = new Label
            {
                Text = _defaultText,
                AutoSize = true, MaximumSize = new Size(wrap, 0), Margin = new Padding(3, 3, 3, 3), UseMnemonic = false,
            };
            layout.Controls.Add(_message);

            if (job != null)
            {
                // As wide as the bar will be, so the window does not jump when the bar appears.
                _message.MinimumSize = new Size(wrap, 0);
                _bar = new BusyBar
                {
                    Visible = false, Margin = new Padding(3, 4, 3, 4),
                    MinimumSize = new Size(wrap, Math.Max(6, Font.Height / 2)),
                    AccessibleName = _defaultText,
                };
                layout.Controls.Add(_bar);

                if (job.Cancellable)
                {
                    // Escape (CancelButton -> a click), the button and the close box are one path,
                    // AskToCancel - not three implementations that drift (APP-BEHAVIOUR rule 1). The
                    // button carries no DialogResult on purpose: that would close the window at once,
                    // and it must stay up until the work has really stopped.
                    _cancel = new Button
                    {
                        Text = T("Cancel"),
                        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        Anchor = AnchorStyles.None, Margin = new Padding(3, 8, 3, 3),
                    };
                    ButtonGlyphs.Attach(_cancel, AppGlyphs.Cancel, ButtonLook.Dismissing);
                    _cancel.Click += (_, _) => AskToCancel();
                    layout.Controls.Add(_cancel);
                    CancelButton = _cancel;
                }
                _poll = new System.Windows.Forms.Timer { Interval = PollMs };
                _poll.Tick += (_, _) => RefreshFromJob();
            }
            Controls.Add(layout);
            // Rule 3: the wait cursor only where the work cannot be cancelled.
            UseWaitCursor = job == null || !job.Cancellable;
        }

        /// <summary>The Cancel button, or null when the work cannot be cancelled (for the layout and key tests).</summary>
        internal Button? CancelControl => _cancel;

        /// <summary>The text line, for the tests.</summary>
        internal string MessageText => _message.Text;

        /// <summary>The bar, or null when the dialog has no job; for the tests.</summary>
        internal BusyBar? Bar => _bar;

        /// <summary>
        /// Re-read the job and redraw: the sentence, the count, the bar and the Cancel button. Driven by
        /// the timer on the UI thread; public to the tests so the mapping is checked without waiting.
        /// </summary>
        internal void RefreshFromJob()
        {
            if (_job == null) return;
            BusyView view = _job.Snapshot();
            string caption = view.Caption(_defaultText, _cancellingText);
            if (_message.Text != caption) _message.Text = caption;
            if (_bar != null)
            {
                bool show = view.Determinate && !view.CancelRequested;
                if (_bar.Visible != show) _bar.Visible = show;
                _bar.Fraction = view.Fraction;
                _bar.AccessibleName = caption;
            }
            if (_cancel != null && _cancel.Enabled != view.CancelEnabled) _cancel.Enabled = view.CancelEnabled;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshFromJob();
            _poll?.Start();
            // Registered only now: a continuation that ran before the handle existed would have
            // nothing to close, and the dialog would then wait forever.
            _task.ContinueWith(_ =>
            {
                // The work ended: a click that lands before the window has closed is too late to be
                // an outcome, and the caller must not read "cancelled" off a job that ran to the end.
                _job?.Finish();
                try { BeginInvoke(new Action(Close)); }
                catch { /* already closed */ }
            }, TaskScheduler.Default);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Only the user's own close is refused (CloseToHide's rule): a sign-out must not be vetoed.
            // Where the work can be cancelled the close is the cancel request - the same one the
            // button and Escape make - and the window stays until the work has really stopped.
            if (e.CloseReason == CloseReason.UserClosing && !_task.IsCompleted)
            {
                e.Cancel = true;
                AskToCancel();
            }
            base.OnFormClosing(e);
        }

        /// <summary>
        /// The one way the user asks the work to stop: it is told, the window shows that it was asked
        /// ("Cancelling..") and stays until the work has really ended. True when the request took effect.
        /// </summary>
        internal bool AskToCancel()
        {
            if (_job == null || _task.IsCompleted || !_job.RequestCancel()) return false;
            RefreshFromJob();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poll?.Dispose();
                _ownFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Run <paramref name="work"/> on the pool, pumping messages here until it ends; its exception is
        /// rethrown. With a <paramref name="job"/> the window reports its progress and may cancel it: a
        /// <see cref="OperationCanceledException"/> after a cancel request returns <c>default</c> quietly.
        /// </summary>
        public static T Run<T>(IWin32Window? owner, string language, Func<T> work, BusyJob? job = null)
            => Wait(owner, language, Task.Run(work), job);

        /// <summary>The same for work that returns nothing.</summary>
        public static void Run(IWin32Window? owner, string language, Action work, BusyJob? job = null)
            => Wait(owner, language, Task.Run(work), job);

        /// <summary>Wait for <paramref name="task"/> with the message loop running; its exception is rethrown.</summary>
        public static T Wait<T>(IWin32Window? owner, string language, Task<T> task, BusyJob? job = null)
        {
            Show(owner, language, task, job);
            try { return task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (job != null && job.CancelRequested) { return default!; }
        }

        /// <summary>Wait for <paramref name="task"/> with the message loop running; its exception is rethrown.</summary>
        public static void Wait(IWin32Window? owner, string language, Task task, BusyJob? job = null)
        {
            Show(owner, language, task, job);
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (job != null && job.CancelRequested) { return; } // an outcome, not a failure
        }

        private static void Show(IWin32Window? owner, string language, Task task, BusyJob? job)
        {
            try
            {
                // The wait handle, not Task.Wait: Wait may run a task the pool has not picked up yet
                // inline, i.e. right here on the thread this class exists to keep free.
                try { ((IAsyncResult)task).AsyncWaitHandle.WaitOne(GraceMs); }
                catch { /* the fault is rethrown by the caller */ }
                if (task.IsCompleted) return;

                using var dialog = new BusyDialog(language, task, job);
                if (owner != null)
                {
                    dialog.ShowDialog(owner);
                    return;
                }
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.Shown += (_, _) => ForegroundActivator.Activate(dialog);
                dialog.ShowDialog();
            }
            finally
            {
                // Nothing is left to cancel: a late request is refused, not reported as an outcome.
                job?.Finish();
            }
        }
    }

    /// <summary>
    /// The determinate bar of <see cref="BusyDialog"/>: a track and a fill, both in the palette's roles
    /// (the window is themed like every other, and a stock ProgressBar is not). It runs along the
    /// reading direction, so a right-to-left window fills from the right.
    /// </summary>
    internal sealed class BusyBar : Control, IThemeAware
    {
        private ThemePalette _palette = ThemePalette.Light;
        private double _fraction;

        public BusyBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        /// <summary>How much of the track is filled, 0..1.</summary>
        public double Fraction
        {
            get => _fraction;
            set
            {
                double clamped = Math.Max(0d, Math.Min(1d, value));
                if (Math.Abs(clamped - _fraction) < 0.0001) return;
                _fraction = clamped;
                Invalidate();
            }
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var track = new SolidBrush(_palette.SurfaceSunken)) g.FillRectangle(track, box);
            int fill = (int)Math.Round((box.Width - 1) * _fraction);
            if (fill > 0)
            {
                int x = RightToLeft == RightToLeft.Yes ? box.Right - fill : box.Left + 1;
                using (var accent = new SolidBrush(_palette.Accent))
                    g.FillRectangle(accent, x, box.Top + 1, fill, Math.Max(1, box.Height - 1));
            }
            using (var frame = new Pen(_palette.Border)) g.DrawRectangle(frame, box);
        }
    }
}
