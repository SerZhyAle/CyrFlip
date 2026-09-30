using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0030: nothing that can wait on a disk, a network share or a background job runs on the
    /// thread both low-level hooks share. What a unit test can prove is the seams - that a remote path
    /// is never handed to a probe, that the icon lookup and the launch leave the calling thread, that
    /// the selection probe answers from its own long-lived worker, and that an import's journal write
    /// goes through the off-thread hook it is given. Whether typing stays fluid is the live check in
    /// <c>tools/uitest/Test-HookThreadBlocking.ps1</c>.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public sealed class HookThreadBlockingTests
    {
        // ---- HT-1: the remote classifier and the launcher's probes ----

        [Theory]
        [InlineData(@"\\nas\tools\app.exe", true)]
        [InlineData("//nas/tools/app.exe", true)]
        [InlineData(@"\\?\UNC\nas\tools\app.exe", true)]
        [InlineData("file://nas/tools/app.exe", true)]
        [InlineData(@"\\10.255.255.1\x\a.exe", true)]
        [InlineData(@"C:\Windows\System32\calc.exe", false)]
        [InlineData("calc.exe", false)]            // relative: resolved later, not remote by itself
        [InlineData("https://example.com/x.exe", false)]
        [InlineData(@"\\?\C:\x.exe", false)]       // the device namespace is not a share
        [InlineData("", false)]
        public void TheRemoteClassifierDecidesFromTheTextAlone(string path, bool remote)
        {
            var probes = new LaunchProbes
            {
                Exists = _ => throw new InvalidOperationException("the classifier must not probe"),
                IsRemoteDrive = _ => false,
                CurrentDirectory = () => @"C:\",
            };
            Assert.Equal(remote, LaunchTargets.IsRemotePath(path, probes));
        }

        [Fact]
        public void AMappedNetworkDriveIsRemote()
        {
            var probes = new LaunchProbes { IsRemoteDrive = letter => letter == 'Z', CurrentDirectory = () => @"C:\" };
            Assert.True(LaunchTargets.IsRemotePath(@"Z:\tools\app.exe", probes));
            Assert.False(LaunchTargets.IsRemotePath(@"C:\tools\app.exe", probes));
        }

        private sealed class CountingProbes
        {
            public int FileExists, ResolveOnPath, PowerShell;
            public readonly LauncherPathProbes Probes;

            public CountingProbes()
            {
                Probes = new LauncherPathProbes
                {
                    FileExists = _ => { Interlocked.Increment(ref FileExists); return false; },
                    ResolveOnPath = _ => { Interlocked.Increment(ref ResolveOnPath); return null; },
                    PowerShellHost = () => { Interlocked.Increment(ref PowerShell); return "powershell.exe"; },
                    VocabularyIcon = id => @"C:\icons\" + id + ".ico",   // never the real icons folder
                    IsRemote = path => LaunchTargets.IsRemotePath(path, new LaunchProbes { IsRemoteDrive = _ => false, CurrentDirectory = () => @"C:\" }),
                };
            }

            public int Total => FileExists + ResolveOnPath + PowerShell;
        }

        [Theory]
        [InlineData(@"\\10.255.255.1\x\a.exe")]
        [InlineData(@"\\nas\scripts\deploy.ps1")]
        [InlineData("file://nas/share/tool.cmd")]
        [InlineData(@"\\nas\docs\readme.txt")]
        public void ARemoteScenarioIsNeverProbedForItsIcon(string path)
        {
            var counting = new CountingProbes();
            LauncherIcon icon = LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = path }, counting.Probes);

            Assert.Equal(0, counting.Total);
            Assert.False(string.IsNullOrEmpty(icon.Path)); // the app's own icon stands in
        }

        [Fact]
        public void TheJumpListBuilderNeverProbesARemoteScenario()
        {
            var counting = new CountingProbes();
            var scenarios = new List<LauncherScenario>
            {
                new LauncherScenario { Name = "share", Path = @"\\10.255.255.1\x\a.exe" },
                new LauncherScenario { Name = "script", Path = @"\\nas\s\run.bat" },
            };

            List<LauncherJumpList.TaskSpec> tasks = LauncherJumpList.BuildTasks(scenarios, @"C:\CyrFlip.exe", s => s, counting.Probes);

            Assert.Equal(0, counting.Total);
            Assert.Equal(4, tasks.Count); // two scenarios, Manage, Exit
        }

        [Fact]
        public void ALocalScenarioStillGetsItsOwnIcon()
        {
            var counting = new CountingProbes();
            counting.Probes.FileExists = p => { Interlocked.Increment(ref counting.FileExists); return p == @"C:\Tools\app.exe"; };

            LauncherIcon icon = LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = @"C:\Tools\app.exe" }, counting.Probes);

            Assert.Equal(@"C:\Tools\app.exe", icon.Path);
            Assert.Equal(1, counting.FileExists);
        }

        [Fact]
        public void APathHitOnANetworkFolderIsNotUsedAsAnIconSource()
        {
            var counting = new CountingProbes();
            counting.Probes.ResolveOnPath = _ => @"\\server\bin\tool.exe";

            LauncherIcon icon = LauncherIconResolver.Resolve(new LauncherScenario { Name = "x", Path = "tool.exe" }, counting.Probes);

            Assert.NotEqual(@"\\server\bin\tool.exe", icon.Path);
        }

        /// <summary>Collects posts instead of running them, so the test decides when "the UI thread" runs.</summary>
        private sealed class QueueContext : SynchronizationContext
        {
            private readonly object _gate = new object();
            private readonly Queue<(SendOrPostCallback, object?)> _posts = new Queue<(SendOrPostCallback, object?)>();

            public override void Post(SendOrPostCallback d, object? state)
            {
                lock (_gate) { _posts.Enqueue((d, state)); Monitor.PulseAll(_gate); }
            }

            public bool RunOne(int timeoutMs)
            {
                (SendOrPostCallback, object?) post;
                lock (_gate)
                {
                    if (_posts.Count == 0 && !Monitor.Wait(_gate, timeoutMs)) return false;
                    post = _posts.Dequeue();
                }
                post.Item1(post.Item2);
                return true;
            }
        }

        [Fact]
        public void TheIconCacheResolvesOffTheCallingThreadAndHandsTheIconBack()
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                int caller = Environment.CurrentManagedThreadId;
                int loader = caller;
                int loads = 0;
                using var cache = new LauncherIconCache(_ =>
                {
                    loader = Environment.CurrentManagedThreadId;
                    Interlocked.Increment(ref loads);
                    return (Icon)SystemIcons.Application.Clone();
                });
                var scenario = new LauncherScenario { Name = "x", Path = @"\\nas\x.exe" };
                Guid? readyFor = null;

                Assert.Null(cache.Get(scenario, (id, _) => readyFor = id));
                Assert.Null(cache.Get(scenario, (id, _) => readyFor = id)); // still pending: not asked twice
                Assert.True(context.RunOne(10_000), "the icon never came back");

                Assert.NotEqual(caller, loader);
                Assert.Equal(scenario.Id, readyFor);
                Assert.NotNull(cache.Get(scenario, (_, _) => { }));
                Assert.Equal(1, loads);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        [Fact]
        public void AnIconArrivingAfterTheListChangedIsDropped()
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var cache = new LauncherIconCache(_ => (Icon)SystemIcons.Application.Clone());
                var scenario = new LauncherScenario { Name = "x", Path = "a.exe" };
                bool ready = false;

                cache.Get(scenario, (_, _) => ready = true);
                cache.Invalidate();
                Assert.True(context.RunOne(10_000));

                Assert.False(ready);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        [Fact]
        public void TheAsyncLaunchValidatesOffTheCallingThread()
        {
            int caller = -1;
            int translatedOn = -1;
            bool success = true;

            var t = new Thread(() =>
            {
                caller = Environment.CurrentManagedThreadId;
                translatedOn = caller;
                var scenario = new LauncherScenario { Name = "missing", Path = @"C:\definitely\not\here\x.exe" };

                LauncherLaunchResult result = LauncherExecution.LaunchAsync(scenario, s =>
                {
                    translatedOn = Environment.CurrentManagedThreadId;
                    return s;
                }).GetAwaiter().GetResult();

                success = result.Success;
            });
            t.Start();
            t.Join();

            Assert.False(success);
            Assert.NotEqual(caller, translatedOn); // the validation's message was built on the worker
        }

        [Fact]
        public void TheYtDlpPromptStaysOnTheCallingThread()
        {
            int caller = -1;
            int promptedOn = -1;
            bool cancelled = false;

            var t = new Thread(() =>
            {
                caller = Environment.CurrentManagedThreadId;
                var scenario = new LauncherScenario { Name = "yt", Type = LauncherScenarioType.YtDlp };

                LauncherLaunchResult result = LauncherExecution.LaunchAsync(scenario, s => s, () =>
                {
                    promptedOn = Environment.CurrentManagedThreadId;
                    return null; // the user cancelled
                }).GetAwaiter().GetResult();

                cancelled = result.Cancelled;
            });
            t.Start();
            t.Join();

            Assert.True(cancelled);
            Assert.Equal(caller, promptedOn);
        }

        // ---- HT-3: the selection probe's own worker ----

        [Fact]
        public void AProbeRequestedFromASignalProducesTheSameSnapshot()
        {
            var expected = new SelectionSnapshot(SelectionState.Present, "hello");
            int caller = Environment.CurrentManagedThreadId;
            int probedOn = caller;
            using var worker = new SelectionProbe.Worker((x, y) =>
            {
                probedOn = Environment.CurrentManagedThreadId;
                Assert.Equal((10, 20), (x, y));
                return expected;
            });

            SelectionProbe.Run run = worker.Request(10, 20);

            Assert.True(run.WaitDone(10_000));
            SelectionSnapshot snapshot = run.Collect();
            Assert.Equal(SelectionState.Present, snapshot.State);
            Assert.Equal("hello", snapshot.Text);
            Assert.NotEqual(caller, probedOn);
        }

        [Fact]
        public void TheWorkerIsOneThreadForEveryRequest()
        {
            var threads = new HashSet<int>();
            using var worker = new SelectionProbe.Worker((_, _) =>
            {
                lock (threads) threads.Add(Environment.CurrentManagedThreadId);
                return SelectionSnapshot.Unknown;
            });

            for (int i = 0; i < 20; i++)
                Assert.True(worker.Request(i, i).WaitDone(10_000));

            Assert.Single(threads);
        }

        [Fact]
        public void AnOvertakenRequestIsAnsweredAtOnceWithoutAProbe()
        {
            var gate = new ManualResetEventSlim(false);
            int probes = 0;
            using var worker = new SelectionProbe.Worker((_, _) =>
            {
                Interlocked.Increment(ref probes);
                gate.Wait(10_000);
                return new SelectionSnapshot(SelectionState.Present, "x");
            });

            SelectionProbe.Run first = worker.Request(1, 1);
            SpinWait.SpinUntil(() => Volatile.Read(ref probes) == 1, 10_000); // the worker is busy with it
            SelectionProbe.Run second = worker.Request(2, 2);
            SelectionProbe.Run third = worker.Request(3, 3);   // overtakes the second before it ran

            Assert.True(second.WaitDone(10_000));
            Assert.Equal(SelectionState.Unknown, second.Collect().State);

            gate.Set();
            Assert.True(first.WaitDone(10_000));
            Assert.True(third.WaitDone(10_000));
            Assert.Equal(2, Volatile.Read(ref probes)); // the first and the third, never the second
        }

        /// <summary>
        /// S0034 LS2-5: a launch parse still asking a slow disk when the menu collects the answer
        /// costs the launch item only - the verdict and the text are already published.
        /// </summary>
        [Fact]
        public void ASlowLaunchParseDoesNotTurnAKnownVerdictIntoUnknown()
        {
            var parsing = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var target = new LaunchTarget(@"C:\docs\a.pdf", @"C:\docs\a.pdf", LaunchKind.Document);
            using var worker = new SelectionProbe.Worker(
                (_, _) => new SelectionSnapshot(SelectionState.Absent, @"C:\docs\a.pdf"),
                _ => { parsing.Set(); release.Wait(10_000); return target; });

            SelectionProbe.Run run = worker.Request(0, 0);
            Assert.True(parsing.Wait(10_000));

            SelectionSnapshot early = run.Collect(); // the menu gives up while the parse still runs
            Assert.Equal(SelectionState.Absent, early.State);
            Assert.Equal(@"C:\docs\a.pdf", early.Text);
            Assert.Null(early.Launch);

            release.Set();
            Assert.True(run.WaitDone(10_000));
            Assert.Same(target, run.Collect().Launch);
        }

        [Fact]
        public void ARequestAfterTheWorkerStoppedIsUnknownAtOnce()
        {
            var worker = new SelectionProbe.Worker((_, _) => new SelectionSnapshot(SelectionState.Present, "x"));
            worker.Dispose();

            SelectionProbe.Run run = worker.Request(0, 0);

            Assert.True(run.WaitDone(1000));
            Assert.Equal(SelectionState.Unknown, run.Collect().State);
        }

        // ---- HT-2 / HT-6: the notes import's write and the retirement ----

        private sealed class PlainCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => plain;
            public string? Unprotect(string stored) => stored;
        }

        private static string TempJournal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipHT-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "notes.log");
        }

        [Fact]
        public void AnImportWritesItsRecordsThroughTheOffThreadHookInOneBatch()
        {
            string path = TempJournal();
            var incoming = new List<ExchangeNote>();
            for (int i = 0; i < 50; i++)
                incoming.Add(new ExchangeNote
                {
                    Id = Guid.NewGuid(), RawText = "note " + i, Kind = QuickNoteKind.Text,
                    CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc),
                });

            int caller = Environment.CurrentManagedThreadId;
            int wroteOn = caller;
            int calls = 0;
            using (var service = new QuickNotesService(new QuickNotesStore(path, new PlainCipher())))
            {
                ExchangeMergeReport report = service.Import(incoming, includeNew: false, write =>
                {
                    calls++;
                    // A thread of its own: Task.Result may run an unstarted task inline, on this thread.
                    bool written = false;
                    var worker = new Thread(() => { wroteOn = Environment.CurrentManagedThreadId; written = write(); });
                    worker.Start();
                    worker.Join();
                    return written;
                });

                Assert.Equal(50, report.NotesAdded);
                Assert.Equal(50, service.Notes.Count);
            }

            Assert.Equal(1, calls);
            Assert.NotEqual(caller, wroteOn);
            using var reread = new QuickNotesService(new QuickNotesStore(path, new PlainCipher()));
            Assert.Equal(50, reread.Notes.Count);
        }

        [Fact]
        public async Task ARetiredServiceFinishesOnThePoolAndANewOneReplaysAfterIt()
        {
            string path = TempJournal();
            Task retired;
            using (var first = new QuickNotesService(new QuickNotesStore(path, new PlainCipher())))
            {
                QuickNote note = first.CreateDraft(QuickNoteKind.Text);
                note.RawText = "kept";
                first.Touch(note); // left to the debounce: the retirement must write it
                retired = first.RetireAsync();
            }

            var second = new QuickNotesService(new QuickNotesStore(path, new PlainCipher()), loadNow: false);
            Task loaded = second.LoadAsync(retired);
            Assert.Same(retired, await Task.WhenAny(retired, Task.Delay(10_000)));
            Assert.Same(loaded, await Task.WhenAny(loaded, Task.Delay(10_000)));

            Assert.True(second.IsLoaded);
            Assert.Single(second.Notes);
            Assert.Equal("kept", second.Notes[0].RawText);
            second.Dispose();
        }
    }
}
