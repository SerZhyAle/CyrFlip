using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The sync engine of the quick notes on the exchange server (DEVICE-EXCHANGE 1.13, items FG to FM, ticket S0105,
    /// phase 2) between two real devices of one account over <see cref="FakeExchangeServer"/>: each device has its own
    /// entry, device key, notes and sync state, and every box is really sealed and opened - the server sees none of it.
    /// </summary>
    [Collection("ExchangeSecrets")]
    public class ExchangeNotesSyncTests : IDisposable
    {
        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly FakeExchangeServer _server = new FakeExchangeServer();
        private readonly FakeAccount _alice;

        public ExchangeNotesSyncTests()
        {
            ExchangeSecrets.Use(_secrets);
            _alice = _server.AddAccount("alice");
            _server.Features.Add(ExchangeWire.FeatureNotes);
        }

        public void Dispose() => ExchangeSecrets.Use(null);

        // ---------------------------------------------------------------- create, edit, delete

        [Fact]
        public async Task ANoteCreatedOnThePcAppearsOnTheOtherDeviceAndAnEditThereComesBack()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("shopping\r\nmilk");

            Assert.True((await pc.Sync()).Ok);
            ExchangeNotesSyncReport got = await phone.Sync();
            Assert.True(got.Ok, got.Code);
            Assert.Equal(1, got.Received);
            QuickNote there = phone.Notes.Single();
            Assert.Equal(note.Id, there.Id);
            Assert.Equal("shopping\nmilk", there.RawText); // LF on the wire (item FH)
            Assert.Equal(note.CreatedAtUtc, there.CreatedAtUtc);

            phone.Edit(note.Id, "shopping\nmilk\nbread");
            Assert.Equal(1, (await phone.Sync()).Sent);
            ExchangeNotesSyncReport back = await pc.Sync();
            Assert.Equal(1, back.Received);
            Assert.Equal("shopping\nmilk\nbread", pc.Notes.Single().RawText);
            Assert.Equal(0, back.Conflicts);
        }

        [Fact]
        public async Task ABoxIsSealedForTheNotesDevicesAndTheServerHoldsNoClearText()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            FakeDevice clipboardOnly = _server.AddDevice(_alice, "watchDeviceId0000000A", "Watch");
            pc.Create("secret token 42");
            await pc.Sync();

            ExchangeNoteRecord stored = _alice.Notes.Single();
            Assert.DoesNotContain("secret", stored.BoxJson);
            List<string> to = ExchangeNotesSync.RecipientsOf(stored.BoxJson);
            Assert.Contains(pc.Entry.DeviceId, to);
            Assert.Contains(phone.Entry.DeviceId, to);
            Assert.DoesNotContain(clipboardOnly.DeviceId, to); // no role notes, never a recipient (item FI)
        }

        [Fact]
        public async Task ADeleteStaysDeletedAndAnOlderCopyNeverBringsItBack()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("to delete");
            await pc.Sync();
            await phone.Sync();

            phone.Delete(note.Id);
            Assert.Equal(1, (await phone.Sync()).Sent);
            ExchangeNotesSyncReport pcPass = await pc.Sync();
            Assert.Equal(1, pcPass.Deleted);
            Assert.Empty(pc.Notes);

            // an older copy (a state that still believes revision 1) is refused: the tombstone holds
            var stale = new ExchangeNotesSyncState { EntryId = pc.Entry.Id, NotesRevision = 1 };
            stale.Of(note.Id).Revision = 1;
            stale.Of(note.Id).Changed = true;
            var old = new ListNotes();
            old.AddLocal(note);
            ExchangeNotesSyncReport replay = await new ExchangeNotesSync(Client(), () => { }).SyncAsync(pc.Entry, old, stale, CancellationToken.None);
            Assert.True(replay.Ok, replay.Code);
            Assert.True(_alice.Notes.Single(r => r.NoteId == ExchangeNotes.NoteIdOf(note.Id)).Deleted);
            Assert.DoesNotContain(old.Snapshot(), n => n.Id == note.Id);
        }

        // ---------------------------------------------------------------- item FJ, each branch

        [Fact]
        public async Task AnEditAgainstATombstoneSurvivesAsANewNote()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("draft");
            await pc.Sync();
            await phone.Sync();
            phone.Delete(note.Id);
            await phone.Sync();

            pc.Edit(note.Id, "draft, edited offline");
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.Equal(1, pass.Conflicts);
            QuickNote survivor = pc.Notes.Single();
            Assert.NotEqual(note.Id, survivor.Id);
            Assert.Equal("draft, edited offline", survivor.RawText);
            Assert.True(_alice.Notes.Single(r => r.NoteId == ExchangeNotes.NoteIdOf(note.Id)).Deleted);
            Assert.Contains(_alice.Notes, r => r.NoteId == ExchangeNotes.NoteIdOf(survivor.Id) && !r.Deleted);

            await phone.Sync();
            Assert.Equal("draft, edited offline", phone.Notes.Single().RawText);
        }

        [Fact]
        public async Task TwoEditsKeepBothTheStoredVersionUnderItsIdAndTheLocalOneUnderTheDeviceName()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("v1", "Plan");
            await pc.Sync();
            await phone.Sync();

            phone.Edit(note.Id, "v2 from the phone");
            await phone.Sync();
            pc.Edit(note.Id, "v2 from the pc");
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.Equal(1, pass.Conflicts);

            Assert.Equal("v2 from the phone", pc.Notes.Single(n => n.Id == note.Id).RawText);
            QuickNote copy = pc.Notes.Single(n => n.Id != note.Id);
            Assert.Equal("v2 from the pc", copy.RawText);
            Assert.Equal("Plan (PC)", copy.Title);

            await phone.Sync();
            Assert.Equal(2, phone.Notes.Count);
            Assert.Contains(phone.Notes, n => n.RawText == "v2 from the pc");
        }

        [Fact]
        public async Task ALocalDeleteLosesToALiveEditAndTheNoteStays()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("keep me");
            await pc.Sync();
            await phone.Sync();

            phone.Edit(note.Id, "keep me, edited");
            await phone.Sync();
            pc.Delete(note.Id);
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.Equal(1, pass.Conflicts);
            Assert.Equal("keep me, edited", pc.Notes.Single().RawText);
            Assert.False(_alice.Notes.Single().Deleted);
        }

        [Fact]
        public async Task AnUnchangedLocalNoteTakesTheStoredRecordAndSendsNothing()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote note = pc.Create("a");
            await pc.Sync();
            await phone.Sync();
            phone.Edit(note.Id, "b");
            await phone.Sync();

            int puts = _server.Seen.Count(t => t == "notes_put");
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.Equal(1, pass.Received);
            Assert.Equal(0, pass.Sent);
            Assert.Equal(puts, _server.Seen.Count(t => t == "notes_put"));
        }

        [Fact]
        public async Task ANoteCreatedBeforeTheSecondDeviceJoinedIsResealedForIt()
        {
            Device pc = await Join("PC");
            QuickNote note = pc.Create("early");
            await pc.Sync();
            Device phone = await Join("Phone");

            ExchangeNotesSyncReport reseal = await pc.Sync();
            Assert.True(reseal.Ok, reseal.Code);
            Assert.Equal(0, reseal.Sent); // the same content, a new box (item FJ, resealing)
            Assert.Contains(phone.Entry.DeviceId, ExchangeNotesSync.RecipientsOf(_alice.Notes.Single().BoxJson));

            await phone.Sync();
            Assert.Equal("early", phone.Notes.Single(n => n.Id == note.Id).RawText);
        }

        // ---------------------------------------------------------------- listing: more and full

        [Fact]
        public async Task PagingWithMoreReadsEveryRecord()
        {
            _alice.NotesPageSize = 2;
            Device pc = await Join("PC"), phone = await Join("Phone");
            for (int i = 0; i < 5; i++) pc.Create("note " + i);
            await pc.Sync();

            ExchangeNotesSyncReport got = await phone.Sync();
            Assert.Equal(5, got.Received);
            Assert.Equal(5, phone.Notes.Count);
            Assert.Equal(5, phone.State.NotesRevision);
        }

        [Fact]
        public async Task AFullAnswerAfterAPurgeDeletesWhatTheServerNoLongerHolds()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote gone = pc.Create("gone");
            QuickNote stays = pc.Create("stays");
            await pc.Sync();
            await phone.Sync();
            phone.Delete(gone.Id);
            await phone.Sync();
            phone.Create("third"); // raises the revision above the purged tombstone
            await phone.Sync();
            _alice.PurgeTombstones();
            Assert.True(_alice.NotesHorizon > pc.State.NotesRevision);

            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.DoesNotContain(pc.Notes, n => n.Id == gone.Id);
            Assert.Contains(pc.Notes, n => n.Id == stays.Id);
            Assert.Equal(2, pc.Notes.Count);
        }

        // ---------------------------------------------------------------- limits and refusals

        [Fact]
        public async Task ANoteAboveTheLimitStaysOnThisDeviceAndOneAtTheLimitTravels()
        {
            Device pc = await Join("PC"), phone = await Join("Phone");
            QuickNote probe = pc.Create("");
            int overhead = ExchangeNotePayload.Encode(probe).Length;
            pc.Notes.Clear();
            pc.Create(new string('x', ExchangeNotes.MaxPayloadBytes - overhead));
            pc.Create(new string('y', ExchangeNotes.MaxPayloadBytes - overhead + 1));

            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.Equal(1, pass.Sent);
            Assert.Equal(1, pass.TooLarge);
            await phone.Sync();
            Assert.Single(phone.Notes);
        }

        [Fact]
        public async Task ANewNoteAboveTheAccountCountStaysLocalAndSaysSo()
        {
            _alice.NotesCount = 1;
            Device pc = await Join("PC");
            pc.Create("one");
            pc.Create("two");
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.True(pass.LimitReached);
            Assert.Equal(1, pass.Sent);
        }

        [Fact]
        public async Task AServerWithoutSealedGetsNothingNoClearFallback()
        {
            Device pc = await Join("PC");
            pc.Create("x");
            _server.Features.Remove(ExchangeWire.FeatureSealed);
            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.Equal(ExchangeCodes.ServerNoE2e, pass.Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("notes_", StringComparison.Ordinal));
            Assert.Empty(_alice.Notes);
        }

        [Fact]
        public async Task AServerWithoutTheNotesFeatureIsNeverSentANotesFrame()
        {
            _server.Features.Remove(ExchangeWire.FeatureNotes);
            Device pc = await Join("PC");
            pc.Create("x");
            Assert.Equal(ExchangeNotesSync.ReasonServerNoNotes, (await pc.Sync()).Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("notes_", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AGuestAccountIsRefusedNotPermitted()
        {
            Device pc = await Join("PC");
            _alice.Kind = "guest";
            Assert.Equal(ExchangeCodes.NotPermitted, (await pc.Sync()).Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("notes_", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AnEntryEnrolledWithoutTheNotesRoleAsksToSignInAgain()
        {
            Device pc = await Join("PC");
            _alice.Devices.Single(d => d.DeviceId == pc.Entry.DeviceId).Roles = new List<string> { "clipboard" };
            pc.Create("x");
            Assert.Equal(ExchangeNotesSync.ReasonRoleMissing, (await pc.Sync()).Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("notes_", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ANoteWhoseFirstAnswerWasLostIsNotDuplicated()
        {
            Device pc = await Join("PC");
            QuickNote note = pc.Create("once");
            await pc.Sync();
            // the state forgets the stored revision, as after a put whose answer never arrived
            pc.State.Notes.Clear();
            pc.State.NotesRevision = 0;

            ExchangeNotesSyncReport pass = await pc.Sync();
            Assert.True(pass.Ok, pass.Code);
            Assert.Single(pc.Notes);
            Assert.Single(_alice.Notes);
            Assert.Equal(1, pc.State.Notes[note.Id].Revision);
        }

        [Fact]
        public void ARecipientListOfABoxThatDoesNotReadIsEmpty()
        {
            Assert.Empty(ExchangeNotesSync.RecipientsOf(""));
            Assert.Empty(ExchangeNotesSync.RecipientsOf("{not json"));
            Assert.Equal(new[] { "a", "b" }, ExchangeNotesSync.RecipientsOf("{\"from\":\"a\",\"keys\":[{\"to\":\"b\"},{\"to\":\"a\"}]}"));
        }

        // ---------------------------------------------------------------- the state file (phase 3, assumption A2)

        [Fact]
        public void TheStateFileRoundTripsAndHoldsNoNoteText()
        {
            var state = new ExchangeNotesSyncState { EntryId = "entry1", NotesRevision = 42 };
            Guid a = Guid.NewGuid(), b = Guid.NewGuid();
            state.Of(a).Revision = 7;
            state.Of(a).Changed = true;
            state.Of(a).Recipients.AddRange(new[] { "pc", "phone" });
            state.Of(b).Revision = 9;
            state.Of(b).DeletedLocally = true;
            state.Of(Guid.NewGuid()).TooLarge = true;

            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cyrflip-s0105-" + Guid.NewGuid().ToString("N"));
            string path = System.IO.Path.Combine(folder, ExchangeNotesSyncStore.FileName);
            try
            {
                Assert.True(ExchangeNotesSyncStore.Save(path, state));
                Assert.True(ExchangeNotesSyncStore.Save(path, state)); // over an existing file
                ExchangeNotesSyncState back = ExchangeNotesSyncStore.Load(path);
                Assert.Equal("entry1", back.EntryId);
                Assert.Equal(42, back.NotesRevision);
                Assert.Equal(3, back.Notes.Count);
                Assert.True(back.Notes[a].Changed);
                Assert.Equal(new[] { "pc", "phone" }, back.Notes[a].Recipients);
                Assert.True(back.Notes[b].DeletedLocally);
                Assert.False(System.IO.File.Exists(path + ".tmp"));
            }
            finally { System.IO.Directory.Delete(folder, true); }
        }

        [Fact]
        public void AStateFileThatDoesNotReadIsAnEmptyStateBoundToNoEntry()
        {
            Assert.Null(ExchangeNotesSyncStore.Parse("{not json"));
            Assert.Null(ExchangeNotesSyncStore.Parse("{\"v\":2,\"notes\":[]}"));
            ExchangeNotesSyncState partial = ExchangeNotesSyncStore.Parse(
                "{\"v\":1,\"entryId\":\"e\",\"notesRevision\":-5,\"notes\":[{\"id\":\"NOT-A-UUID\",\"rev\":1},{\"id\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"rev\":3}]}")!;
            Assert.Equal(0, partial.NotesRevision);
            Assert.Single(partial.Notes);
            Assert.Equal("", ExchangeNotesSyncStore.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).EntryId);
        }

        [Fact]
        public void TheStateFileNeverEntersASupportBundle()
            => Assert.Contains(ExchangeNotesSyncStore.FileName, SupportBundle.ExcludedFiles);

        // ---------------------------------------------------------------- helpers

        private ExchangeClient Client() => new ExchangeClient(_server, "26.11.15.1200");

        private async Task<Device> Join(string name)
        {
            ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(Client(), new ExchangeJoinRequest
            {
                Host = "nas.example.org", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key),
                Login = "alice", Password = "Passw0rdTest", DeviceName = name,
            }, null, CancellationToken.None);
            Assert.True(joined.Ok, joined.Code);
            return new Device(joined.Value!, new ExchangeNotesSync(Client(), () => { },
                () => new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc)));
        }

        /// <summary>One device of the account: its entry, its notes, its sync state - and the hooks a user's edit fires (phase 3).</summary>
        private sealed class Device
        {
            public readonly ExchangeAccountEntry Entry;
            public readonly ExchangeNotesSyncState State = new ExchangeNotesSyncState();
            private readonly ListNotes _local = new ListNotes();
            private readonly ExchangeNotesSync _sync;
            private int _clock;

            public Device(ExchangeAccountEntry entry, ExchangeNotesSync sync) { Entry = entry; _sync = sync; }

            public List<QuickNote> Notes => _local.Items;

            public Task<ExchangeNotesSyncReport> Sync() => _sync.SyncAsync(Entry, _local, State, CancellationToken.None);

            public QuickNote Create(string text, string? title = null)
            {
                DateTime at = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc).AddMinutes(++_clock);
                var note = new QuickNote { Title = title, Kind = QuickNoteKind.Text, RawText = text, CreatedAtUtc = at, UpdatedAtUtc = at };
                _local.Items.Add(note);
                return note.Clone();
            }

            public void Edit(Guid id, string text)
            {
                QuickNote note = _local.Items.Single(n => n.Id == id);
                note.RawText = text;
                note.UpdatedAtUtc = note.UpdatedAtUtc.AddSeconds(++_clock);
                State.MarkChanged(id);
            }

            public void Delete(Guid id)
            {
                _local.Items.RemoveAll(n => n.Id == id);
                State.MarkDeleted(id);
            }
        }

        private sealed class ListNotes : IExchangeNotesLocal
        {
            public readonly List<QuickNote> Items = new List<QuickNote>();

            public IReadOnlyList<QuickNote> Snapshot() => Items.Select(n => n.Clone()).ToList();
            public QuickNote? Find(Guid id) => Items.FirstOrDefault(n => n.Id == id)?.Clone();

            /// <summary>Ids the test declares "being edited right now": the sync must leave them alone.</summary>
            public readonly HashSet<Guid> Editing = new HashSet<Guid>();

            public bool ApplyRemote(QuickNote note)
            {
                if (Editing.Contains(note.Id)) return false;
                int at = Items.FindIndex(n => n.Id == note.Id);
                QuickNote copy = note.Clone();
                if (at < 0) { Items.Add(copy); return true; }
                copy.CreatedAtUtc = Items[at].CreatedAtUtc;
                Items[at] = copy;
                return true;
            }

            public bool DeleteRemote(Guid id, bool evenIfEdited)
            {
                if (!evenIfEdited && Editing.Contains(id)) return false;
                Items.RemoveAll(n => n.Id == id);
                return true;
            }
            public void AddLocal(QuickNote note) => Items.Add(note.Clone());
        }
    }
}
