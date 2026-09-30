using System;
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
    }
}
