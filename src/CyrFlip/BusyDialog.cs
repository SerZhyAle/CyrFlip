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
    /// user while the job runs: there is nothing to cancel it with, and closing it would only hand
    /// the thread back to a caller that is not finished.</para>
    /// </summary>
    internal sealed class BusyDialog : ThemedForm
    {
        /// <summary>How long the caller may wait before the window appears. Well under the hook timeout.</summary>
        public const int GraceMs = 40;

        private readonly Task _task;
        private readonly Font? _ownFont;

        internal BusyDialog(string language, Task task)
        {
            _task = task;
            string T(string ru) => Localization.Translate(language, ru);
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
            // A caption only: a marquee ProgressBar is drawn by Windows in its own colours and would
            // be one more declared exception to the theme (S0020) for a window seen for a second.
            layout.Controls.Add(new Label
            {
                Text = T("Подождите, CyrFlip обрабатывает данные.."),
                AutoSize = true, MaximumSize = new Size(wrap, 0), Margin = new Padding(3, 3, 3, 3), UseMnemonic = false,
            });
            Controls.Add(layout);
            UseWaitCursor = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Registered only now: a continuation that ran before the handle existed would have
            // nothing to close, and the dialog would then wait forever.
            _task.ContinueWith(_ =>
            {
                try { BeginInvoke(new Action(Close)); }
                catch { /* already closed */ }
            }, TaskScheduler.Default);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Only the user's own close is refused (CloseToHide's rule): a sign-out must not be vetoed.
            if (e.CloseReason == CloseReason.UserClosing && !_task.IsCompleted) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ownFont?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Run <paramref name="work"/> on the pool, pumping messages here until it ends; its exception is rethrown.</summary>
        public static T Run<T>(IWin32Window? owner, string language, Func<T> work)
            => Wait(owner, language, Task.Run(work));

        /// <summary>Run <paramref name="work"/> on the pool, pumping messages here until it ends; its exception is rethrown.</summary>
        public static void Run(IWin32Window? owner, string language, Action work)
            => Wait(owner, language, Task.Run(work));

        /// <summary>Wait for <paramref name="task"/> with the message loop running; its exception is rethrown.</summary>
        public static T Wait<T>(IWin32Window? owner, string language, Task<T> task)
        {
            Show(owner, language, task);
            return task.GetAwaiter().GetResult();
        }

        /// <summary>Wait for <paramref name="task"/> with the message loop running; its exception is rethrown.</summary>
        public static void Wait(IWin32Window? owner, string language, Task task)
        {
            Show(owner, language, task);
            task.GetAwaiter().GetResult();
        }

        private static void Show(IWin32Window? owner, string language, Task task)
        {
            // The wait handle, not Task.Wait: Wait may run a task the pool has not picked up yet
            // inline, i.e. right here on the thread this class exists to keep free.
            try { ((IAsyncResult)task).AsyncWaitHandle.WaitOne(GraceMs); }
            catch { /* the fault is rethrown by the caller */ }
            if (task.IsCompleted) return;

            using var dialog = new BusyDialog(language, task);
            if (owner != null)
            {
                dialog.ShowDialog(owner);
                return;
            }
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Shown += (_, _) => ForegroundActivator.Activate(dialog);
            dialog.ShowDialog();
        }
    }
}
