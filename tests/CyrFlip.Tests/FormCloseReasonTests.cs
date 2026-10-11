using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The three "close means hide" windows turn a close that means "this window gone for now" into
    /// a hide: the user's own close, and - since S0065 E-13 - a <b>bare program-sent
    /// <c>WM_CLOSE</c></b> (a window manager, <c>taskkill</c> without <c>/F</c>, the Restart Manager),
    /// which Windows reports as <see cref="CloseReason.TaskManagerClosing"/> and which used to
    /// dispose the window, ending the app on its next open. A session end
    /// (<c>WM_QUERYENDSESSION</c>) and the fatal path's <c>Application.Exit()</c> are still let
    /// through: opening Settings once must never make CyrFlip veto a Windows sign-out
    /// (spec S0003 LC-1), and a cancelled exit left the app running after telling the user it
    /// would close.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds the real settings window - see SharedGdiCollection
    public class FormCloseReasonTests
    {
        private const int WM_QUERYENDSESSION = 0x0011;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [Fact]
        public void Intercept_hides_on_a_user_close_and_a_bare_program_close()
        {
            foreach (CloseReason reason in Enum.GetValues(typeof(CloseReason)))
            {
                var e = new FormClosingEventArgs(reason, false);
                bool hide = CloseToHide.Intercept(e);
                // S0065 E-13: TaskManagerClosing is what a bare WM_CLOSE from another program
                // arrives as; on these tray windows it is a hide like the user's own close.
                bool gone = reason == CloseReason.UserClosing || reason == CloseReason.TaskManagerClosing;
                Assert.Equal(gone, hide);
                Assert.Equal(gone, e.Cancel);
            }
        }

        /// <summary>
        /// The reproduced E-13 fault: a bare WM_CLOSE used to dispose the Settings window, and the
        /// next open from the tray or the pipe threw ObjectDisposedException into the fatal path.
        /// The window must be hidden, alive, and openable again.
        /// </summary>
        [Fact]
        public void A_bare_WmClose_hides_the_settings_window_instead_of_disposing_it() => OnUiThread(() =>
        {
            Form form = BuildSettings();
            try
            {
                form.Show();
                SendMessage(form.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                Assert.False(form.Visible, "the bare close left the settings window visible");
                Assert.False(form.IsDisposed, "the bare close disposed the settings window");

                form.Show(); // what ShowSettings does next - the window must still open
                Assert.True(form.Visible, "the window could not be shown again after a bare close");
            }
            finally { form.Dispose(); }
        });

        [Fact]
        public void Settings_window_lets_windows_end_the_session() => OnUiThread(() =>
        {
            Form form = BuildSettings();
            try { AssertHidesOnUserCloseAndLetsTheSessionEnd(form); } finally { form.Dispose(); }
        });

        [Fact]
        public void Translation_window_lets_windows_end_the_session() => OnUiThread(() =>
        {
            using var form = new TranslationResultWindow(new AppConfig(), "English");
            AssertHidesOnUserCloseAndLetsTheSessionEnd(form);
        });

        [Fact]
        public void History_strip_lets_windows_end_the_session() => OnUiThread(() =>
        {
            // A private folder: the real clipboard-history.log is never read by a test.
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            var service = new ClipboardHistoryService(enabled: false, paused: false, dir);
            try
            {
                using var form = new ClipboardHistoryWindow(service, new AppConfig(), () => { });
                AssertHidesOnUserCloseAndLetsTheSessionEnd(form);
            }
            finally
            {
                service.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        });

        private static void AssertHidesOnUserCloseAndLetsTheSessionEnd(Form form)
        {
            // The shape of a sign-out: the window has a handle, is hidden, and Windows asks it.
            IntPtr handle = form.Handle;
            Assert.NotEqual(IntPtr.Zero, SendMessage(handle, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero));

            // Application.Exit() - the fatal-error path - must not be vetoed either.
            Assert.False(RaiseClosing(form, CloseReason.ApplicationExitCall).Cancel, "ApplicationExitCall was cancelled");

            // The one close that is still turned into a hide: the user's.
            form.Show();
            Assert.True(RaiseClosing(form, CloseReason.UserClosing).Cancel, "UserClosing was not cancelled");
            Assert.False(form.Visible, "a user close did not hide the window");
        }

        private static FormClosingEventArgs RaiseClosing(Form form, CloseReason reason)
        {
            var e = new FormClosingEventArgs(reason, false);
            typeof(Form).GetMethod("OnFormClosing", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(form, new object[] { e });
            return e;
        }

        private static Form BuildSettings() => TestForms.NewSettings(new AppConfig { UiLanguage = "English" });

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }
    }
}
