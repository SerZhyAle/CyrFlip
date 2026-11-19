using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The notes of an account on the exchange server (DEVICE-EXCHANGE 1.13, ticket S0105), phase 1: the clear payload
    /// of item FH, the frames of item FI and the client against a scripted store that applies items FJ and FL in the
    /// order item FI gives. The box is opaque here - the server never opens it, and neither does this layer.
    /// </summary>
    [Collection("ExchangeSecrets")]
    public class ExchangeNotesTests : IDisposable
    {
        private const string Box = "{\"v\":1,\"fake\":\"box\"}";
        private static readonly Guid NoteA = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
        private static readonly Guid NoteB = new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        private static readonly Guid NoteC = new Guid("16fd2706-8baf-433b-82eb-8c7fada847da");

        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly FakeExchangeServer _server = new FakeExchangeServer();
        private readonly FakeAccount _alice;

        public ExchangeNotesTests()
        {
            ExchangeSecrets.Use(_secrets);
            _alice = _server.AddAccount("alice");
            _server.Features.Add(ExchangeWire.FeatureNotes);
        }

        public void Dispose() => ExchangeSecrets.Use(null);

        // ---------------------------------------------------------------- item FH, the clear payload

        private static QuickNote TextNote(string text, string? title = null) => new QuickNote
        {
            Id = NoteA,
            Title = title,
            Kind = QuickNoteKind.Text,
            RawText = text,
            CreatedAtUtc = new DateTime(2026, 11, 15, 9, 30, 0, 125, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 11, 15, 9, 31, 2, 7, DateTimeKind.Utc),
        };

        [Fact]
        public void ATextNoteIsEncodedInTheFixedMemberOrderWithLfAndMilliseconds()
        {
            byte[] payload = ExchangeNotePayload.Encode(TextNote("line 1\r\nline\t2\rend", "  Plan  "));
            Assert.Equal("{\"formatVersion\":1,\"title\":\"Plan\",\"kind\":\"text\",\"text\":\"line 1\\nline\\t2\\nend\","
                + "\"createdAt\":\"2026-11-15T09:30:00.125Z\",\"updatedAt\":\"2026-11-15T09:31:02.007Z\"}",
                Encoding.UTF8.GetString(payload));
        }

        [Fact]
        public void ANoteWithNoNameCarriesNullNeverAPlaceholderWord()
        {
            string json = Encoding.UTF8.GetString(ExchangeNotePayload.Encode(TextNote("x", "   ")));
            Assert.Contains("\"title\":null", json);
        }

        [Fact]
        public void AChecklistRoundTripsItsItemIdsPositionsAndTicks()
        {
            var note = new QuickNote
            {
                Id = NoteB, Kind = QuickNoteKind.Checklist, Title = null,
                CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            };
            note.Items.Add(new QuickNoteItem { Id = NoteC, Text = "second", IsChecked = true, Position = 7 });
            note.Items.Add(new QuickNoteItem { Id = NoteA, Text = "первый 🙂", IsChecked = false, Position = 3 });

            byte[] payload = ExchangeNotePayload.Encode(note);
            string json = Encoding.UTF8.GetString(payload);
            Assert.Contains("\"items\":[{\"itemId\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"text\":\"первый 🙂\",\"checked\":false,\"position\":3},", json);

            Assert.True(ExchangeNotePayload.TryDecode(payload, NoteB, out QuickNote? back, out string failure), failure);
            Assert.Equal(QuickNoteKind.Checklist, back!.Kind);
            Assert.Null(back.Title);
            Assert.Equal(new[] { NoteA, NoteC }, back.Items.Select(i => i.Id));
            Assert.Equal(new[] { 3, 7 }, back.Items.Select(i => i.Position));
            Assert.Equal(new[] { false, true }, back.Items.Select(i => i.IsChecked));
            Assert.Equal(note.CreatedAtUtc, back.CreatedAtUtc);
            Assert.True(ExchangeNotePayload.SameContent(note, back));
        }

        [Fact]
        public void ATextComesBackAsItTravelledAndCompareAsTheSameContentAsTheCrlfOriginal()
        {
            QuickNote original = TextNote("a\r\nb");
            Assert.True(ExchangeNotePayload.TryDecode(ExchangeNotePayload.Encode(original), NoteA, out QuickNote? back, out _));
            Assert.Equal("a\nb", back!.RawText);
            Assert.True(ExchangeNotePayload.SameContent(original, back));
            back.RawText = "a\nc";
            Assert.False(ExchangeNotePayload.SameContent(original, back));
        }

        [Fact]
        public void AnUnknownMemberIsIgnored()
        {
            string json = "{\"formatVersion\":1,\"title\":null,\"kind\":\"text\",\"text\":\"hi\",\"colour\":\"red\","
                + "\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}";
            Assert.True(ExchangeNotePayload.TryDecode(Encoding.UTF8.GetBytes(json), NoteA, out QuickNote? note, out _));
            Assert.Equal("hi", note!.RawText);
        }

        [Theory]
        [InlineData("{\"formatVersion\":2,\"kind\":\"text\",\"text\":\"\",\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.FormatUnsupported)]
        [InlineData("{\"formatVersion\":1,\"kind\":\"drawing\",\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.FormatUnsupported)]
        [InlineData("{\"formatVersion\":1,\"kind\":\"text\",\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.Damaged)]
        [InlineData("{\"formatVersion\":1,\"kind\":\"text\",\"text\":\"x\",\"createdAt\":\"yesterday\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.Damaged)]
        [InlineData("{\"kind\":\"text\",\"text\":\"x\",\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.Damaged)]
        [InlineData("{\"formatVersion\":1,\"kind\":\"checklist\",\"items\":[{\"itemId\":\"0F8FAD5B-D9CB-469F-A165-70867728950E\",\"text\":\"x\",\"checked\":false,\"position\":0}],\"createdAt\":\"2026-11-15T09:30:00.000Z\",\"updatedAt\":\"2026-11-15T09:30:00.000Z\"}", ExchangeCodes.Damaged)]
        [InlineData("not json", ExchangeCodes.Damaged)]
        [InlineData("[1,2]", ExchangeCodes.Damaged)]
        public void APayloadThisBuildCannotReadIsRefusedWithItsCode(string json, string code)
        {
            Assert.False(ExchangeNotePayload.TryDecode(Encoding.UTF8.GetBytes(json), NoteA, out QuickNote? note, out string failure));
            Assert.Null(note);
            Assert.Equal(code, failure);
        }

        [Fact]
        public void APayloadAtTheLimitIsReadAndOneByteAboveIsNot()
        {
            int overhead = ExchangeNotePayload.Encode(TextNote("")).Length;
            byte[] atLimit = ExchangeNotePayload.Encode(TextNote(new string('a', ExchangeNotes.MaxPayloadBytes - overhead)));
            byte[] above = ExchangeNotePayload.Encode(TextNote(new string('a', ExchangeNotes.MaxPayloadBytes - overhead + 1)));
            Assert.Equal(131072, atLimit.Length);
            Assert.False(ExchangeNotePayload.ExceedsLimit(atLimit));
            Assert.True(ExchangeNotePayload.ExceedsLimit(above));
            Assert.True(ExchangeNotePayload.TryDecode(atLimit, NoteA, out _, out _));
            Assert.False(ExchangeNotePayload.TryDecode(above, NoteA, out _, out _));
        }

        [Fact]
        public void ANoteIdTravelsLowerCaseAndTheBoxContextNamesIt()
        {
            Assert.Equal("note:0f8fad5b-d9cb-469f-a165-70867728950e", ExchangeNotes.BoxContext(NoteA));
            Assert.True(ExchangeNotes.TryParseNoteId("0f8fad5b-d9cb-469f-a165-70867728950e", out Guid id));
            Assert.Equal(NoteA, id);
            Assert.False(ExchangeNotes.TryParseNoteId("{0f8fad5b-d9cb-469f-a165-70867728950e}", out _));
            Assert.False(ExchangeNotes.TryParseNoteId("0F8FAD5B-D9CB-469F-A165-70867728950E", out _));
        }

        // ---------------------------------------------------------------- item FI, the frames

        [Fact]
        public void TheNotesFramesCarryTheirMembersInOrderAndADeleteCarriesNoBox()
        {
            Assert.Equal("{\"schemaVersion\":2,\"type\":\"notes_list\",\"since\":12}", Body(ExchangeMembers.NotesList(12)));
            Assert.Equal("{\"schemaVersion\":2,\"type\":\"notes_put\",\"noteId\":\"n\",\"baseRevision\":3,\"deleted\":false,\"sealed\":" + Box + "}",
                Body(ExchangeMembers.NotesPut("n", 3, false, Box)));
            Assert.Equal("{\"schemaVersion\":2,\"type\":\"notes_put\",\"noteId\":\"n\",\"baseRevision\":4,\"deleted\":true}",
                Body(ExchangeMembers.NotesPut("n", 4, true, Box)));
            Assert.Throws<ArgumentException>(() => ExchangeMembers.NotesPut("n", 0, false, null));
        }

        private static string Body(ExchangeMembers m)
        {
            byte[] framed = ExchangeWire.Encode(m);
            return Encoding.UTF8.GetString(framed, 4, framed.Length - 4);
        }

        // ---------------------------------------------------------------- the client against the scripted store

        [Fact]
        public async Task ACreateAnEditAndADeleteAreStoredAndListedInRevisionOrder()
        {
            using (ExchangeSession s = await Open())
            {
                string a = ExchangeNotes.NoteIdOf(NoteA);
                ExchangeOutcome<ExchangeNotePutResult> created = await Client().NotesPutAsync(s, a, 0, false, Box, CancellationToken.None);
                Assert.True(created.Ok && created.Value!.Stored, created.Code);
                Assert.Equal(1, created.Value.Revision);

                ExchangeOutcome<ExchangeNotePutResult> edited = await Client().NotesPutAsync(s, a, 1, false, Box, CancellationToken.None);
                Assert.Equal(2, edited.Value!.Revision);

                ExchangeOutcome<ExchangeNotePutResult> deleted = await Client().NotesPutAsync(s, a, 2, true, null, CancellationToken.None);
                Assert.Equal(3, deleted.Value!.Revision);

                ExchangeOutcome<ExchangeNotesChanges> list = await Client().NotesListAsync(s, 0, CancellationToken.None);
                Assert.True(list.Ok, list.Code);
                Assert.Equal(3, list.Value!.NotesRevision);
                ExchangeNoteRecord only = Assert.Single(list.Value.Records);
                Assert.True(only.Deleted);
                Assert.Equal("", only.BoxJson);
                Assert.Equal(a, only.NoteId);
                Assert.False(list.Value.More);
                Assert.False(list.Value.Full);
            }
        }

        [Fact]
        public async Task AnOlderCopyIsRefusedAsAConflictCarryingTheStoredRecordAndNothingIsStored()
        {
            using (ExchangeSession s = await Open())
            {
                string a = ExchangeNotes.NoteIdOf(NoteA);
                await Client().NotesPutAsync(s, a, 0, false, Box, CancellationToken.None);
                await Client().NotesPutAsync(s, a, 1, true, null, CancellationToken.None); // the tombstone, revision 2

                ExchangeOutcome<ExchangeNotePutResult> stale = await Client().NotesPutAsync(s, a, 1, false, Box, CancellationToken.None);
                Assert.True(stale.Ok, stale.Code);
                Assert.False(stale.Value!.Stored);
                Assert.True(stale.Value.Conflict!.Deleted);
                Assert.Equal(2, stale.Value.Conflict.Revision);
                Assert.Equal(2, _alice.NotesRevision);

                ExchangeOutcome<ExchangeNotePutResult> again = await Client().NotesPutAsync(s, a, 0, false, Box, CancellationToken.None);
                Assert.False(again.Value!.Stored); // a second create of a known id is a conflict, never a resurrection
            }
        }

        [Fact]
        public async Task PagingAsksAgainFromTheLastRevisionReceived()
        {
            _alice.NotesPageSize = 2;
            using (ExchangeSession s = await Open())
            {
                foreach (Guid id in new[] { NoteA, NoteB, NoteC })
                    await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(id), 0, false, Box, CancellationToken.None);

                ExchangeNotesChanges first = (await Client().NotesListAsync(s, 0, CancellationToken.None)).Value!;
                Assert.True(first.More);
                Assert.Equal(new long[] { 1, 2 }, first.Records.Select(r => r.Revision));
                ExchangeNotesChanges second = (await Client().NotesListAsync(s, first.Records.Last().Revision, CancellationToken.None)).Value!;
                Assert.False(second.More);
                Assert.Equal(new long[] { 3 }, second.Records.Select(r => r.Revision));
            }
        }

        [Fact]
        public async Task ASinceBelowTheHorizonAfterAPurgeIsAFullAnswer()
        {
            using (ExchangeSession s = await Open())
            {
                await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 0, false, Box, CancellationToken.None);
                await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 1, true, null, CancellationToken.None);
                await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteB), 0, false, Box, CancellationToken.None);
                _alice.PurgeTombstones();

                ExchangeNotesChanges full = (await Client().NotesListAsync(s, 1, CancellationToken.None)).Value!;
                Assert.True(full.Full);
                Assert.Equal(2, full.NotesHorizon);
                Assert.Equal(ExchangeNotes.NoteIdOf(NoteB), Assert.Single(full.Records).NoteId);
            }
        }

        [Fact]
        public async Task ANewNoteAboveTheCountIsRefusedButAnEditAndADeleteAreNot()
        {
            _alice.NotesCount = 1;
            using (ExchangeSession s = await Open())
            {
                await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 0, false, Box, CancellationToken.None);
                ExchangeOutcome<ExchangeNotePutResult> second = await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteB), 0, false, Box, CancellationToken.None);
                Assert.Equal(ExchangeCodes.LimitReached, second.Code);
                Assert.True((await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 1, false, Box, CancellationToken.None)).Value!.Stored);
                Assert.True((await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 2, true, null, CancellationToken.None)).Value!.Stored);
            }
        }

        [Fact]
        public async Task AGuestAccountHasNoNotes()
        {
            using (ExchangeSession s = await Open())
            {
                _alice.Kind = "guest";
                Assert.Equal(ExchangeCodes.NotPermitted, (await Client().NotesListAsync(s, 0, CancellationToken.None)).Code);
                Assert.Equal(ExchangeCodes.NotPermitted,
                    (await Client().NotesPutAsync(s, ExchangeNotes.NoteIdOf(NoteA), 0, false, Box, CancellationToken.None)).Code);
            }
        }

        [Fact]
        public async Task AnUnsolicitedNotesChangedIsRememberedOnTheSessionAndTheAnswerStillArrives()
        {
            using (ExchangeSession s = await Open())
            {
                Assert.Equal(-1, s.NotesChangedRevision);
                _server.PushNotesChangedBeforeAnswer = 9;
                ExchangeOutcome<ExchangeNotesChanges> list = await Client().NotesListAsync(s, 0, CancellationToken.None);
                Assert.True(list.Ok, list.Code);
                Assert.Equal(9, s.NotesChangedRevision);
            }
        }

        // ---------------------------------------------------------------- helpers

        private ExchangeClient Client() => new ExchangeClient(_server, "26.11.15.1200");

        private async Task<ExchangeSession> Open()
        {
            ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(Client(), new ExchangeJoinRequest
            {
                Host = "nas.example.org", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key),
                Login = "alice", Password = "Passw0rdTest", DeviceName = "PC",
            }, null, CancellationToken.None);
            Assert.True(joined.Ok, joined.Code);
            ExchangeOutcome<ExchangeSession> opened = await Client().OpenAsync(joined.Value!, CancellationToken.None);
            Assert.True(opened.Ok, opened.Code);
            Assert.True(opened.Value!.Welcome.Has(ExchangeWire.FeatureNotes));
            return opened.Value;
        }
    }
}
