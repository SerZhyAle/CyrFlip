using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// How CyrFlip ends (spec S0003): the sign-out watcher, the second pass through Dispose, the
    /// clipboard guard when a worker cannot start, the cursor reset that must not happen when the
    /// cursor was never replaced, and the autostart sync that must not crash a start.
    /// </summary>
    public class LifecycleTests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ---- LC-2: SessionEndWatcher ----

        [Fact]
        public void Session_watcher_never_vetoes_and_ends_once_only_when_the_session_really_ends()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var watcher = new SessionEndWatcher();
                    int queried = 0, ended = 0;
                    watcher.QueryEnding += (_, _) => queried++;
                    watcher.Ending += (_, _) => ended++;

                    IntPtr answer = SendMessage(watcher.Handle, SessionEndWatcher.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero);
                    Assert.Equal((IntPtr)1, answer);
                    Assert.Equal(1, queried);

                    // Another app vetoed: WM_ENDSESSION(FALSE) - the session goes on, nothing is torn down.
                    SendMessage(watcher.Handle, SessionEndWatcher.WM_ENDSESSION, IntPtr.Zero, IntPtr.Zero);
                    Assert.Equal(0, ended);

                    SendMessage(watcher.Handle, SessionEndWatcher.WM_ENDSESSION, (IntPtr)1, IntPtr.Zero);
                    SendMessage(watcher.Handle, SessionEndWatcher.WM_ENDSESSION, (IntPtr)1, IntPtr.Zero);
                    Assert.Equal(1, ended);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }

        [Fact]
        public void A_throwing_query_handler_still_answers_yes()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var watcher = new SessionEndWatcher();
                    watcher.QueryEnding += (_, _) => throw new InvalidOperationException("boom");
                    Assert.Equal((IntPtr)1, SendMessage(watcher.Handle, SessionEndWatcher.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }

        // ---- LC-3: Dispose runs twice ----

        [Fact]
        public void A_second_dispose_is_a_no_op()
        {
            // The real constructor installs hooks and a tray icon, so the context is built without it.
            // Its fields are then all null and the first pass fails part-way - which is the point: the
            // guard is set before any teardown, so the second pass (WinForms disposing the context
            // after Program's `using` did) must return without touching anything.
            var context = (CyrFlipContext)FormatterServices.GetUninitializedObject(typeof(CyrFlipContext));
            try { context.Dispose(); } catch (NullReferenceException) { }
            context.Dispose();
            Assert.True((bool)typeof(CyrFlipContext).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!);
            GC.SuppressFinalize(context);
        }

        // ---- LC-6: the clipboard guard when a worker cannot start ----

        [Fact]
        public void Busy_is_released_when_the_worker_thread_cannot_start()
        {
            int busy = 1; // the caller already took the guard
            Assert.Throws<OutOfMemoryException>(() =>
                CyrFlipContext.StartClipboardWorker(ref busy, () => { }, _ => throw new OutOfMemoryException()));
            Assert.Equal(0, busy);
        }

        [Fact]
        public void Busy_is_left_to_the_worker_when_the_thread_starts()
        {
            int busy = 1;
            Thread? started = null;
            CyrFlipContext.StartClipboardWorker(ref busy, () => { }, t => started = t);
            Assert.Equal(1, busy); // released by the worker's own finally, not here
            Assert.NotNull(started);
            Assert.True(started!.IsBackground);
        }

        // ---- LC-7: no cursor reset when the cursor was never replaced ----

        [Fact]
        public void Force_restore_reloads_cursors_only_after_a_replacement()
        {
            Action original = LayoutCursor.ReloadCursors;
            int reloads = 0;
            try
            {
                LayoutCursor.ReloadCursors = () => reloads++;
                LayoutCursor.ForceRestore(); // clear whatever state an earlier test left
                reloads = 0;

                LayoutCursor.ForceRestore();
                Assert.Equal(0, reloads);

                LayoutCursor.MarkReplacedForTest();
                LayoutCursor.ForceRestore();
                LayoutCursor.ForceRestore(); // exit + ProcessExit both call it: one reload is enough
                Assert.Equal(1, reloads);
            }
            finally
            {
                LayoutCursor.ReloadCursors = original;
            }
        }

        // ---- LC-5: autostart sync on every start ----

        [Fact]
        public void Autostart_sync_writes_nothing_when_the_entry_already_matches()
        {
            int writes = 0;
            bool wrote = Autostart.SyncRunValue(() => "\"C:\\A\\CyrFlip.exe\"", _ => writes++, "\"C:\\A\\CyrFlip.exe\"");
            Assert.False(wrote);
            Assert.Equal(0, writes);
        }

        [Fact]
        public void Autostart_sync_leaves_autostart_off_alone()
        {
            int writes = 0;
            Assert.False(Autostart.SyncRunValue(() => null, _ => writes++, "\"C:\\A\\CyrFlip.exe\""));
            Assert.Equal(0, writes);
        }

        [Fact]
        public void Autostart_sync_repoints_an_entry_from_another_location()
        {
            string? written = null;
            Assert.True(Autostart.SyncRunValue(() => "\"C:\\Old\\CyrFlip.exe\"", c => written = c, "\"C:\\New\\CyrFlip.exe\""));
            Assert.Equal("\"C:\\New\\CyrFlip.exe\"", written);
        }

        [Fact]
        public void Autostart_sync_never_throws_when_the_registry_is_denied()
        {
            Assert.False(Autostart.SyncRunValue(() => throw new UnauthorizedAccessException(), _ => { }, "x"));
            Assert.False(Autostart.SyncRunValue(() => "old", _ => throw new UnauthorizedAccessException(), "x"));
        }
    }
}
