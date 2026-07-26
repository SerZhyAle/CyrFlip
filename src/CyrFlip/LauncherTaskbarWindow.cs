using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The launcher's presence on the taskbar: a 1x1, fully transparent, permanently minimized
    /// window whose only job is to give CyrFlip a taskbar button while the scenario launcher is on.
    /// It exists only while the feature is enabled (spec §4 - switched off, CyrFlip looks exactly as
    /// it did before the launcher: tray only, no button).
    ///
    /// <b>Why a window at all.</b> A Jump List hangs off a taskbar button, and a tray-only app has
    /// none - the tasks were reachable only from a pinned shortcut or while some other CyrFlip window
    /// happened to be open. With this anchor the launcher behaves the way a launcher is expected to:
    /// <b>right-click</b> the button opens the Jump List (Windows draws it, from
    /// <see cref="LauncherJumpList"/>) and <b>left-click</b> opens the same scenario list as an
    /// ordinary menu.
    ///
    /// <b>Why it rests minimized.</b> A taskbar click on the window that is already active minimizes
    /// it instead of activating it, so a window that stayed restored would answer only every second
    /// click. Restoring raises <c>WM_SIZE</c>/<c>SIZE_RESTORED</c> (the one notification every path
    /// the shell uses ends in - <c>SC_RESTORE</c> is not always sent); the menu is shown there and
    /// the window minimizes again as soon as it closes, which also hands the keyboard focus straight
    /// back to whatever the user was typing in. The <c>SIZE_RESTORED</c> that arrives while the
    /// window is being created is ignored: only a restore out of the minimized resting state counts.
    /// </summary>
    internal sealed class LauncherTaskbarWindow : Form
    {
        private const int WM_SIZE = 0x0005;
        private const int SIZE_RESTORED = 0;
        private const int SIZE_MINIMIZED = 1;

        private readonly Action<ContextMenuStrip> _fillMenu;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private bool _resting;   // minimized and waiting for a taskbar click
        private bool _menuOpen;

        public LauncherTaskbarWindow(Action<ContextMenuStrip> fillMenu)
        {
            _fillMenu = fillMenu;

            Text = "CyrFlip";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000); // invisible even during the moment it is restored
            Size = new Size(1, 1);
            Opacity = 0;
            WindowState = FormWindowState.Minimized;

            System.Drawing.Icon? mark = LauncherBrand.GetIcon(32);
            if (mark != null) Icon = mark;

            _menu.Closed += (_, _) => { _menuOpen = false; BeginInvoke((MethodInvoker)Dismiss); };
        }

        /// <summary>The taskbar tooltip, refreshed whenever the UI language changes.</summary>
        public void ApplyLanguage(Func<string, string> translate)
            => Text = "CyrFlip — " + translate("Быстрый запуск");

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_SIZE) return;

            long state = m.WParam.ToInt64();
            if (state == SIZE_MINIMIZED)
            {
                _resting = true;
            }
            else if (state == SIZE_RESTORED && _resting && !_menuOpen)
            {
                _resting = false;
                // Off the message that is restoring the window - showing a modal-ish menu from
                // inside WM_SIZE would run the menu's loop before the restore finished.
                BeginInvoke((MethodInvoker)ShowMenu);
            }
        }

        private void ShowMenu()
        {
            if (_menuOpen || IsDisposed) return;
            _fillMenu(_menu);
            if (_menu.Items.Count == 0) { Dismiss(); return; }

            _menuOpen = true;
            // The classic tray-menu trick: without a foreground owner the menu would not close on
            // the first click outside it.
            WindowInterop.SetForegroundWindow(Handle);
            _menu.Show(Control.MousePosition);
        }

        /// <summary>Back to the resting state: no focus held, and the next click opens the menu again.</summary>
        private void Dismiss()
        {
            if (IsDisposed) return;
            if (WindowState != FormWindowState.Minimized) WindowState = FormWindowState.Minimized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // "Close window" on the taskbar button must not quietly remove a surface the user turned
            // on in the settings - the launcher switch owns this window's lifetime, and the tray icon
            // it belongs to cannot be closed that way either. Windows shutting down still closes it.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Dismiss();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _menu.Dispose();
            base.Dispose(disposing);
        }
    }
}
