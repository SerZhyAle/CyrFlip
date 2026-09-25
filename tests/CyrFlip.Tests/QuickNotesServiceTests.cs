using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The live set of notes: what a draft is, when it becomes real, and what a restart brings back.
    /// The rule worth stating out loud is the first one - opening the editor and changing your mind
    /// must leave nothing behind, while emptying a note that already exists is an edit and is kept.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class QuickNotesServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _path;

        public QuickNotesServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-notesvc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, QuickNotesStore.FileName);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        private QuickNotesService Service(bool loadNow = true) => Service(new QuickNotesStore(_path, new FakeCipher()), loadNow);

        /// <summary>
        /// Built with no synchronization context, so the service's owner-thread posts (the debounce
        /// flush, the applied replay) run inline and a test never waits on a context it cannot pump.
        /// </summary>
        private static QuickNotesService Service(QuickNotesStore store, bool loadNow = true)
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new QuickNotesService(store, loadNow); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private int NonEmptyLines() => File.Exists(_path) ? File.ReadAllLines(_path).Count(l => l.Length > 0) : 0;

        [Fact]
        public void AnEmptyDraftIsNeverCreated()
        {
            using QuickNotesService service = Service();
            QuickNote draft = service.CreateDraft(QuickNoteKind.Text);

            Assert.False(service.Commit(draft));
            Assert.Empty(service.Notes);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void ADraftWithATitleAloneIsCreated()
        {
            using QuickNotesService service = Service();
            QuickNote draft = service.CreateDraft(QuickNoteKind.Text);
            draft.Title = "just a name";

            Assert.True(service.Commit(draft));
            Assert.Single(service.Notes);
        }

        [Fact]
        public void AnExistingNoteEmptiedByTheUserIsStillSaved()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            note.RawText = "something";
            service.Commit(note);

            note.RawText = "";
            service.Touch(note);                 // what the window does on every edit
            Assert.True(service.Commit(note));   // an edit, not a draft nobody filled in
            Assert.Single(service.Notes);
        }

        [Fact]
        public void TheCreationDateIsAssignedAtTheFirstSaveAndNeverChangesAgain()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            Assert.Equal(default, note.CreatedAtUtc);

            note.RawText = "x";
            service.Commit(note);
            DateTime created = note.CreatedAtUtc;
            Assert.NotEqual(default, created);

            note.RawText = "x, edited";
            service.Touch(note);
            service.Commit(note);

            Assert.Equal(created, note.CreatedAtUtc);
            Assert.True(note.UpdatedAtUtc >= created);
        }

        [Fact]
        public void ADraftIsADraftUntilItIsCommitted()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            Assert.True(service.IsDraft(note));

            note.RawText = "x";
            service.Commit(note);

            Assert.False(service.IsDraft(note));
        }

        [Fact]
        public void ABlankTitleIsNormalizedAwayOnTheWayToTheJournal()
        {
            using (QuickNotesService service = Service())
            {
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "body";
                note.Title = "   ";
                service.Commit(note);
                Assert.Null(note.Title);
            }

            using QuickNotesService reopened = Service();
            Assert.Null(Assert.Single(reopened.Notes).Title);
        }

        [Fact]
        public void NotesComeBackNewestFirstAfterARestart()
        {
            using (QuickNotesService service = Service())
            {
                foreach (string body in new[] { "first", "second", "third" })
                {
                    QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                    note.RawText = body;
                    service.Commit(note);
                    System.Threading.Thread.Sleep(2);   // distinct creation timestamps
                }
            }

            using QuickNotesService reopened = Service();

            Assert.Equal(new[] { "third", "second", "first" },
                new List<QuickNote>(reopened.Notes).ConvertAll(n => n.RawText).ToArray());
        }

        [Fact]
        public void DeletingADraftTouchesNoFileWhileDeletingARealNoteIsRecorded()
        {
            using QuickNotesService service = Service();
            QuickNote draft = service.CreateDraft(QuickNoteKind.Text);
            service.Delete(draft);
            Assert.False(File.Exists(_path));

            QuickNote real = service.CreateDraft(QuickNoteKind.Text);
            real.RawText = "x";
            service.Commit(real);
            service.Delete(real);

            Assert.Empty(service.Notes);
            using QuickNotesService reopened = Service();
            Assert.Empty(reopened.Notes);
        }

        [Fact]
        public void FlushWritesWhatTheDebounceWasStillHolding()
        {
            using (QuickNotesService service = Service())
            {
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "typed but not committed";
                service.Touch(note);            // no Commit: this is what the timer would have done
                service.Flush();
                Assert.Single(service.Notes);
            }

            using QuickNotesService reopened = Service();
            Assert.Equal("typed but not committed", Assert.Single(reopened.Notes).RawText);
        }

        [Fact]
        public void DisposingFlushesTheOpenEditRatherThanLosingIt()
        {
            using (QuickNotesService service = Service())
            {
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = "never explicitly saved";
                service.Touch(note);
            }   // Dispose stops the timer, flushes, compacts

            using QuickNotesService reopened = Service();
            Assert.Equal("never explicitly saved", Assert.Single(reopened.Notes).RawText);
        }

        [Fact]
        public void DeleteAllLeavesNothingOnDiskAndNothingInMemory()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            note.RawText = "x";
            service.Commit(note);

            service.DeleteAll();

            Assert.Empty(service.Notes);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void SearchGoesThroughTheSameRulesTheOrderClassDefines()
        {
            using QuickNotesService service = Service();
            foreach (string body in new[] { "var c = new HttpClient();", "buy milk" })
            {
                QuickNote note = service.CreateDraft(QuickNoteKind.Text);
                note.RawText = body;
                service.Commit(note);
            }

            Assert.Single(service.Search("httpclient"));
            Assert.Equal(2, service.Search("").Count);
        }

        // ---- Ticket S0006 ----

        /// <summary>
        /// QN-1: "delete all" followed by anything that commits the note the window still held -
        /// closing it, the chord, switching the feature off - wrote that note back as a new one.
        /// </summary>
        [Fact]
        public void ANoteDeletedByDeleteAllIsNeverWrittenBack()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            note.RawText = "private fragment";
            service.Commit(note);
            int cleared = 0;
            service.Cleared += (_, _) => cleared++;

            service.DeleteAll();
            note.RawText = "private fragment, still open in the window";
            service.Touch(note);

            Assert.False(service.Commit(note));
            service.Flush();
            Assert.Equal(1, cleared);
            Assert.Empty(service.Notes);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void ANoteDeletedOnItsOwnIsNeverWrittenBackEither()
        {
            using QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            note.RawText = "x";
            service.Commit(note);
            service.Delete(note);
            int lines = NonEmptyLines();

            service.Touch(note);
            Assert.False(service.Commit(note));

            Assert.Equal(lines, NonEmptyLines());
            using QuickNotesService reopened = Service();
            Assert.Empty(reopened.Notes);
        }

        /// <summary>
        /// QN-3: browsing from note A to B used to stamp A as modified and append a full copy of it.
        /// </summary>
        [Fact]
        public void CommittingAnUntouchedNoteWritesNothingAndKeepsItsModifiedDate()
        {
            using QuickNotesService service = Service();
            QuickNote a = service.CreateDraft(QuickNoteKind.Text);
            a.RawText = "a";
            service.Commit(a);
            QuickNote b = service.CreateDraft(QuickNoteKind.Text);
            b.RawText = "b";
            service.Commit(b);
            DateTime modified = a.UpdatedAtUtc;
            int lines = NonEmptyLines();

            // Open A, open B, open A - the window commits on every switch.
            Assert.False(service.Commit(a));
            Assert.False(service.Commit(b));
            Assert.False(service.Commit(a));

            Assert.Equal(lines, NonEmptyLines());
            Assert.Equal(modified, a.UpdatedAtUtc);
        }

        /// <summary>
        /// QN-8: a creation is counted by the service, once, whichever path created the note - the
        /// debounce usually gets there before the window's own commit does.
        /// </summary>
        [Fact]
        public void ADraftFlushedByTheDebounceRaisesCreatedOnce()
        {
            using QuickNotesService service = Service();
            int created = 0;
            service.Created += (_, _) => Interlocked.Increment(ref created);
            QuickNote draft = service.CreateDraft(QuickNoteKind.Text);
            draft.RawText = "typed";
            service.Touch(draft);

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (service.IsDraft(draft) && DateTime.UtcNow < deadline) Thread.Sleep(20);

            Assert.False(service.IsDraft(draft), "the debounce never flushed the draft");
            Assert.False(service.Commit(draft));   // the window's commit afterwards: nothing to do
            Assert.Equal(1, Volatile.Read(ref created));
        }

        /// <summary>QN-2: the journal is compacted once it is due, not only at a clean exit.</summary>
        [Fact]
        public void SixHundredCommitsCompactTheJournalWithoutADispose()
        {
            QuickNotesService service = Service();
            QuickNote note = service.CreateDraft(QuickNoteKind.Text);
            note.RawText = "v0";
            service.Commit(note);
            for (int i = 1; i < 600; i++)
            {
                note.RawText = "v" + i;
                service.Touch(note);
                service.Commit(note);
            }

            // The compaction runs on the pool; wait for any that was started.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                Task? compaction = service.CompactionTask;
                if (compaction != null && compaction.IsCompleted) break;
                Thread.Sleep(20);
            }

            Assert.NotNull(service.CompactionTask);
            Assert.True(NonEmptyLines() < 600, "journal still holds " + NonEmptyLines() + " records");
            Assert.True(File.Exists(_path + ".bak"), "a compaction leaves the previous journal as .bak");

            // Deliberately not disposed until here: nothing above relied on the exit path.
            service.Dispose();
            using QuickNotesService reopened = Service();
            Assert.Equal("v599", Assert.Single(reopened.Notes).RawText);
        }

        /// <summary>QN-2: the replay does not run on the thread that asked for it.</summary>
        [Fact]
        public void ReplayingALargeJournalRunsOffTheCallingThread()
        {
            var seed = new QuickNotesStore(_path, new FakeCipher());
            for (int i = 0; i < 5000; i++)
                seed.Create(new QuickNote { RawText = "note " + i, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i) });

            var cipher = new ThreadRecordingCipher();
            QuickNotesService service = Service(new QuickNotesStore(_path, cipher), loadNow: false);
            Assert.False(service.IsLoaded);
            Assert.Empty(service.Notes);

            Task loading = service.LoadAsync();
            Assert.True(loading.Wait(TimeSpan.FromSeconds(30)));

            Assert.True(service.IsLoaded);
            Assert.Equal(5000, service.Notes.Count);
            Assert.DoesNotContain(Thread.CurrentThread.ManagedThreadId, cipher.Threads);
            service.Dispose();
        }

        /// <summary>
        /// A service disposed before its replay was applied has an empty list, and a compaction then
        /// would replace every note on disk with nothing. It must leave the journal alone.
        /// </summary>
        [Fact]
        public void AServiceDisposedBeforeItsReplayWasAppliedNeverCompactsTheJournal()
        {
            var seed = new QuickNotesStore(_path, new FakeCipher());
            var note = new QuickNote { RawText = "keep me", CreatedAtUtc = DateTime.UtcNow };
            seed.Create(note);
            for (int i = 0; i < QuickNotesStore.CompactAfterOperations; i++) seed.Update(note);
            string before = File.ReadAllText(_path);

            var store = new QuickNotesStore(_path, new FakeCipher());
            store.Load();   // the store knows the journal is due; the service never applied it
            QuickNotesService service = Service(store, loadNow: false);
            service.Dispose();

            Assert.Equal(before, File.ReadAllText(_path));
        }

        private sealed class ThreadRecordingCipher : IQuickNotesCipher
        {
            private readonly FakeCipher _inner = new FakeCipher();
            public readonly System.Collections.Concurrent.ConcurrentBag<int> Threads = new System.Collections.Concurrent.ConcurrentBag<int>();
            public string Protect(string plain) => _inner.Protect(plain);
            public string? Unprotect(string cipher)
            {
                Threads.Add(Thread.CurrentThread.ManagedThreadId);
                return _inner.Unprotect(cipher);
            }
        }
    }
}
