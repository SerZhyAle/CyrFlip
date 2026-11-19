using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The journal binding of the notes sync (ticket S0105, phase 3): the service tells the user's changes from the
    /// sync's, the sync never writes over a note being edited, and the marks that arrive during a pass wait for its end.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class QuickNotesSyncLocalTests : IDisposable
    {
        private readonly string _root;
        private readonly string _journal;
        private readonly string _statePath;

        public QuickNotesSyncLocalTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-notesync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _journal = Path.Combine(_root, QuickNotesStore.FileName);
            _statePath = Path.Combine(_root, ExchangeNotesSyncStore.FileName);
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

        private QuickNotesService Service()
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new QuickNotesService(new QuickNotesStore(_journal, new FakeCipher()), loadNow: true); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private ExchangeNotesSyncLedger Ledger() => ExchangeNotesSyncLedger.Load(_statePath, work => work());

        private static QuickNote Remote(Guid id, string text, DateTime created) => new QuickNote
        {
            Id = id,
            RawText = text,
            CreatedAtUtc = created,
            UpdatedAtUtc = created.AddMinutes(1),
        };

        [Fact]
        public void TheUsersChangesAreReportedAndTheSyncsAreNot()
        {
            var changed = new List<Guid>();
            var deleted = new List<Guid>();
            Guid mineId;
            using (QuickNotesService service = Service())
            {
                service.LocalChanged += changed.Add;
                service.LocalDeleted += deleted.Add;

                QuickNote mine = service.CreateDraft(QuickNoteKind.Text);
                mine.RawText = "typed here";
                Assert.True(service.Commit(mine));
                mineId = mine.Id;

                Guid remoteId = Guid.NewGuid();
                Assert.True(service.ApplyRemote(Remote(remoteId, "from the phone", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))));
                Assert.True(service.DeleteRemote(remoteId, evenIfEdited: false));
                service.AddLocal(Remote(Guid.NewGuid(), "a conflict copy", DateTime.UtcNow));

                service.Delete(service.Find(mine.Id)!);
            }
            Assert.Equal(new[] { mineId }, changed);
            Assert.Equal(new[] { mineId }, deleted);
        }

        [Fact]
        public void ARemoteVersionKeepsTheLocalCreationDateAndSurvivesARestart()
        {
            Guid id;
            DateTime created;
            using (QuickNotesService service = Service())
            {
                QuickNote mine = service.CreateDraft(QuickNoteKind.Text);
                mine.RawText = "old";
                service.Commit(mine);
                id = mine.Id;
                created = mine.CreatedAtUtc;
                Assert.True(service.ApplyRemote(Remote(id, "new", created.AddDays(-30))));
                Assert.Equal("new", service.Find(id)!.RawText);
                Assert.Equal(created, service.Find(id)!.CreatedAtUtc);
            }
            using (QuickNotesService again = Service())
            {
                Assert.Equal("new", again.Find(id)!.RawText);
                Assert.Equal(created, again.Find(id)!.CreatedAtUtc);
            }
        }

        [Fact]
        public void ANoteBeingEditedIsNeitherOverwrittenNorDeletedUnlessReKeyed()
        {
            using (QuickNotesService service = Service())
            {
                QuickNote mine = service.CreateDraft(QuickNoteKind.Text);
                mine.RawText = "v1";
                service.Commit(mine);
                mine.RawText = "v2 typing";
                service.Touch(mine);

                Assert.False(service.ApplyRemote(Remote(mine.Id, "server", mine.CreatedAtUtc)));
                Assert.Equal("v2 typing", service.Find(mine.Id)!.RawText);
                Assert.False(service.DeleteRemote(mine.Id, evenIfEdited: false));
                Assert.NotNull(service.Find(mine.Id));

                Assert.True(service.DeleteRemote(mine.Id, evenIfEdited: true));
                Assert.Null(service.Find(mine.Id));
                Assert.True(service.IsDeleted(mine.Id));
                Assert.False(service.Commit(mine)); // the window's stale object is never written back
            }
        }

        [Fact]
        public void DeleteEverythingIsADeleteOfEachNote()
        {
            var deleted = new List<Guid>();
            using (QuickNotesService service = Service())
            {
                for (int i = 0; i < 3; i++)
                {
                    QuickNote n = service.CreateDraft(QuickNoteKind.Text);
                    n.RawText = "n" + i;
                    service.Commit(n);
                }
                service.LocalDeleted += deleted.Add;
                service.DeleteAll();
            }
            Assert.Equal(3, deleted.Distinct().Count());
        }

        [Fact]
        public void AMarkDuringAPassWaitsForItsEndAndThenReachesTheFile()
        {
            Guid id = Guid.NewGuid();
            ExchangeNotesSyncLedger ledger = Ledger();
            ExchangeNotesSyncState state = ledger.BeginPass()!;
            Assert.Null(ledger.BeginPass()); // passes never overlap
            state.Of(id).Revision = 7;

            ledger.MarkChanged(id);
            Assert.True(ledger.IsMarkPending(id));
            Assert.False(state.Notes[id].Changed);

            ledger.EndPass();
            Assert.False(ledger.IsMarkPending(id));
            Assert.True(state.Notes[id].Changed);

            ExchangeNotesSyncState read = ExchangeNotesSyncStore.Load(_statePath);
            Assert.Equal(7, read.Notes[id].Revision);
            Assert.True(read.Notes[id].Changed);
        }

        [Fact]
        public void AMarkOutsideAPassIsSavedAtOnce()
        {
            Guid id = Guid.NewGuid();
            ExchangeNotesSyncLedger ledger = Ledger();
            ledger.BeginPass()!.Of(id).Revision = 3;
            ledger.EndPass();

            ledger.MarkDeleted(id);
            ExchangeNotesSyncState read = ExchangeNotesSyncStore.Load(_statePath);
            Assert.True(read.Notes[id].DeletedLocally);

            ledger.MarkChanged(id); // brought back under its id: an edit again
            read = ExchangeNotesSyncStore.Load(_statePath);
            Assert.False(read.Notes[id].DeletedLocally);
            Assert.True(read.Notes[id].Changed);
        }

        [Fact]
        public void TheAdapterRefusesANoteWhoseMarkIsStillPending()
        {
            using (QuickNotesService service = Service())
            {
                ExchangeNotesSyncLedger ledger = Ledger();
                var local = new QuickNotesSyncLocal(service, ledger, owner: null);
                using (QuickNotesSyncLocal.Bind(service, ledger))
                {
                    QuickNote mine = service.CreateDraft(QuickNoteKind.Text);
                    mine.RawText = "local";
                    service.Commit(mine);

                    ledger.BeginPass();
                    mine.RawText = "local, edited during the pass";
                    service.Touch(mine);
                    service.Flush(); // written: the mark now waits in the ledger, the debounce holds nothing

                    Assert.True(ledger.IsMarkPending(mine.Id));
                    Assert.False(local.ApplyRemote(Remote(mine.Id, "server", mine.CreatedAtUtc)));
                    Assert.False(local.DeleteRemote(mine.Id, evenIfEdited: false));
                    Assert.Equal("local, edited during the pass", local.Find(mine.Id)!.RawText);
                    ledger.EndPass();

                    QuickNote copy = local.Snapshot().Single();
                    copy.RawText = "changed copy";
                    Assert.Equal("local, edited during the pass", service.Find(mine.Id)!.RawText); // copies, never the live object
                }
            }
        }
    }
}
