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
    /// The three "close means hide" windows must turn only a <b>user</b> close into a hide. They used to
    /// cancel every close: a hidden window still answers <c>WM_QUERYENDSESSION</c>, so opening Settings
    /// once was enough to make CyrFlip veto a Windows sign-out, and the fatal-error path's
    /// <c>Application.Exit()</c> was cancelled by the same handler (spec S0003 LC-1).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds the real settings window - see SharedGdiCollection
    public class FormCloseReasonTests
    {
        private const int WM_QUERYENDSESSION = 0x0011;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [Fact]
        public void Intercept_cancels_only_a_user_close()
        {
            foreach (CloseReason reason in Enum.GetValues(typeof(CloseReason)))
            {
                var e = new FormClosingEventArgs(reason, false);
                bool hide = CloseToHide.Intercept(e);
                bool user = reason == CloseReason.UserClosing;
                Assert.Equal(user, hide);
                Assert.Equal(user, e.Cancel);
            }
        }

        [Fact]
        public void Settings_window_lets_windows_end_the_session() => OnUiThread(() =>
        {
            Form form = BuildSettings();
            try { AssertClosesOnlyByUser(form); } finally { form.Dispose(); }
        });

        [Fact]
        public void Translation_window_lets_windows_end_the_session() => OnUiThread(() =>
        {
            using var form = new TranslationResultWindow(new AppConfig(), "English");
            AssertClosesOnlyByUser(form);
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
                AssertClosesOnlyByUser(form);
            }
            finally
            {
                service.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        });

        private static void AssertClosesOnlyByUser(Form form)
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

        private static Form BuildSettings()
        {
            Type type = typeof(AppConfig).Assembly.GetType("CyrFlip.SettingsForm", true)!;
            Action<bool> b = _ => { };
            Action noop = () => { };
            Action<int> i = _ => { };
            Action<string> s = _ => { };
            var launcherStore = new LauncherScenarioStore(Path.Combine(
                Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N")));
            Func<string, bool, string> export = (_, _) => "";
            return (Form)Activator.CreateInstance(type, new object[]
            {
                new AppConfig { UiLanguage = "English" }, b, b, b, b, b, b, b, b, b, i, s, noop, noop, noop, noop, noop, b, b, b, b, b, b,
                launcherStore, b,
                noop, noop, noop, export,
            })!;
        }

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
