using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0031 CH2-2: a capture read on the pool is applied on the thread that owns the list,
    /// whatever <c>SynchronizationContext.Current</c> was when the service was built. The service is
    /// created here on a thread that has <b>no</b> context at all - the case the old constructor
    /// silently turned into "apply on the pool".
    /// </summary>
    public sealed class ClipboardHistoryThreadTests
    {
        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

            public string? Unprotect(string cipher) =>
                cipher.StartsWith("B64:", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(4)))
                    : null;
        }

        [Fact]
        public void A_capture_from_the_pool_is_applied_on_the_owning_thread_without_a_sync_context()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            int ownerThread = 0, changedOn = 0, entries = 0;
            bool hadContext = true;
            Exception? failure = null;

            var owner = new Thread(() =>
            {
                try
                {
                    hadContext = SynchronizationContext.Current != null;
                    var service = new ClipboardHistoryService(enabled: true, paused: false, dir, new FakeCipher());
                    try
                    {
                        ownerThread = Environment.CurrentManagedThreadId;
                        service.Changed += (_, _) => changedOn = Environment.CurrentManagedThreadId;
                        Task.Run(() => service.DeliverCapture(12345, "copied on the pool", "app", "title")).Wait();
                        var clock = Stopwatch.StartNew();
                        while (changedOn == 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
                        {
                            Application.DoEvents();
                            Thread.Sleep(5);
                        }
                        entries = service.Entries.Count;
                    }
                    finally { service.Dispose(); }
                }
                catch (Exception e) { failure = e; }
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            owner.Join();
            try { Directory.Delete(dir, true); } catch { }

            Assert.Null(failure);
            Assert.False(hadContext);
            Assert.NotEqual(0, changedOn);
            Assert.Equal(ownerThread, changedOn);
            Assert.Equal(1, entries);
        }

        [Fact]
        public void Nothing_is_applied_before_the_owner_pumps_its_messages()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            int before = -1, after = -1;
            Exception? failure = null;

            var owner = new Thread(() =>
            {
                try
                {
                    var service = new ClipboardHistoryService(enabled: true, paused: false, dir, new FakeCipher());
                    try
                    {
                        Task.Run(() => service.DeliverCapture(777, "text", "", "")).Wait();
                        before = service.Entries.Count;   // the pool did not touch the list
                        var clock = Stopwatch.StartNew();
                        while (service.Entries.Count == 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
                        {
                            Application.DoEvents();
                            Thread.Sleep(5);
                        }
                        after = service.Entries.Count;
                    }
                    finally { service.Dispose(); }
                }
                catch (Exception e) { failure = e; }
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            owner.Join();
            try { Directory.Delete(dir, true); } catch { }

            Assert.Null(failure);
            Assert.Equal(0, before);
            Assert.Equal(1, after);
        }

        /// <summary>A cipher that remembers which threads decrypted - every journal line is one call.</summary>
        private sealed class ThreadRecordingCipher : IQuickNotesCipher
        {
            private readonly FakeCipher _inner = new FakeCipher();
            public readonly ConcurrentBag<int> UnprotectThreads = new ConcurrentBag<int>();
            public string Protect(string plain) => _inner.Protect(plain);

            public string? Unprotect(string cipher)
            {
                UnprotectThreads.Add(Environment.CurrentManagedThreadId);
                return _inner.Unprotect(cipher);
            }
        }

        /// <summary>
        /// Ticket S0082: asking for the file's records with the history off (the manager window, the
        /// exchange export) replays the journal on the pool, never on the thread that asked - the UI
        /// thread is the hooks' thread, and at 131 us a record a large journal stalled both hooks.
        /// The merge is the owner's, exactly once, whether the caller completes it or the posted
        /// message does.
        /// </summary>
        [Fact]
        public void The_replay_asked_for_with_the_history_off_decrypts_on_the_pool_and_merges_once()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
            const int Records = 50;
            var cipher = new ThreadRecordingCipher();
            int ownerThread = 0, rightAfter = -1, beforeComplete = -1, afterComplete = -1, afterPump = -1, finished = 0;
            bool sameTask = false, loaded = false;
            Exception? failure = null;

            Directory.CreateDirectory(dir);
            using (var journal = new ClipboardHistoryJournal(Path.Combine(dir, "clipboard-history.log"), cipher))
            {
                for (int i = 0; i < Records; i++)
                {
                    string text = "entry " + i;
                    journal.Append(ClipboardHistoryJournal.Record.Of("add", new ClipboardHistoryEntry
                    {
                        Uuid = ClipboardHistoryService.Hash(text), Text = text, CreatedAt = new DateTime(2026, 10, 29, 12, 0, i, DateTimeKind.Utc),
                    }));
                }
            }

            var owner = new Thread(() =>
            {
                try
                {
                    var service = new ClipboardHistoryService(enabled: false, paused: false, dir, cipher);
                    try
                    {
                        ownerThread = Environment.CurrentManagedThreadId;
                        service.ReplayFinished += (_, _) => finished++;
                        Task replay = service.ReplayAsync();
                        rightAfter = service.Entries.Count;          // nothing was read on this thread
                        sameTask = ReferenceEquals(replay, service.ReplayAsync());
                        loaded = replay.Wait(TimeSpan.FromSeconds(10));
                        beforeComplete = service.Entries.Count;      // the pool does not touch the list
                        service.CompleteReplay();
                        afterComplete = service.Entries.Count;
                        var clock = Stopwatch.StartNew();
                        while (clock.Elapsed < TimeSpan.FromMilliseconds(200))
                        {
                            Application.DoEvents();                  // the posted merge finds it done
                            Thread.Sleep(5);
                        }
                        afterPump = service.Entries.Count;
                    }
                    finally { service.Dispose(); }
                }
                catch (Exception e) { failure = e; }
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.Start();
            owner.Join();
            try { Directory.Delete(dir, true); } catch { }

            Assert.Null(failure);
            Assert.True(loaded);
            Assert.True(sameTask);
            Assert.Equal(0, rightAfter);
            Assert.Equal(0, beforeComplete);
            Assert.Equal(Records, afterComplete);
            Assert.Equal(Records, afterPump);
            Assert.Equal(1, finished);
            Assert.NotEmpty(cipher.UnprotectThreads);
            Assert.DoesNotContain(ownerThread, cipher.UnprotectThreads);
        }
    }
}
