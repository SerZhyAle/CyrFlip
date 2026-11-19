using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four notes verbs (tickets S0108 and S0120): the handlers over a real <see cref="QuickNotesService"/> with the
    /// fake cipher and a temporary journal - no DPAPI, no window, no context - and the export write over the fake file
    /// system the screenshot saver's seam takes. The cases that need a running tray copy (a cold `note add`, the window
    /// open on an unsaved edit, the module switched off live) are `tools/uitest/Test-QuickNotes.ps1`'s and the release's.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsNotesTests : IDisposable
    {
        private const string Marker = "SECRET-MARKER-4711";

        private readonly string _root;
        private readonly string _path;

        public AppCommandsNotesTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-notecmd-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>Built with no synchronization context, so the service's owner-thread posts run inline.</summary>
        private QuickNotesService Service(bool loadNow = true)
        {
            var store = new QuickNotesStore(_path, new FakeCipher());
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new QuickNotesService(store, loadNow); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static Task<AppCommandResponse> Add(QuickNotesService? service, params string[] options)
            => AppCommandsNotes.NoteAdd(Inv(new[] { "note", "add" }.Concat(options).ToArray()), () => service, T, CancellationToken.None);

        private static Task<AppCommandResponse> List(QuickNotesService? service, params string[] options)
            => AppCommandsNotes.NoteList(Inv(new[] { "note", "list" }.Concat(options).ToArray()), () => service, T, CancellationToken.None);

        private static Task<AppCommandResponse> Get(QuickNotesService? service, params string[] options)
            => AppCommandsNotes.NoteGet(Inv(new[] { "note", "get" }.Concat(options).ToArray()), () => service, T, CancellationToken.None);

        /// <summary>The export over the fake file system of the saver's seam: no disk, a ten-second budget, no share in the way.</summary>
        private static Task<AppCommandResponse> Export(QuickNotesService? service, ScreenshotSaverCommandTests.CommandFs fs,
            params string[] options)
            => AppCommandsNotes.NoteExport(Inv(new[] { "note", "export" }.Concat(options).ToArray()), @"C:\work",
                () => service, fs, TimeSpan.FromSeconds(10),
                path => path.StartsWith(@"\\", StringComparison.Ordinal), T, CancellationToken.None);

        private static DateTime At(int minute) => new DateTime(2026, 11, 14, 10, minute, 30, DateTimeKind.Utc);

        private static QuickNote Text(string text, int minute, string? title = null, Guid? id = null)
            => new QuickNote
            {
                Id = id ?? Guid.NewGuid(), Title = title, RawText = text, CreatedAtUtc = At(minute), UpdatedAtUtc = At(minute),
            };

        private static QuickNote Checklist(int minute, params string[] items)
        {
            var note = new QuickNote { Kind = QuickNoteKind.Checklist, CreatedAtUtc = At(minute), UpdatedAtUtc = At(minute) };
            for (int i = 0; i < items.Length; i++) note.Items.Add(new QuickNoteItem { Text = items[i], Position = i });
            return note;
        }

        private static Dictionary<string, object> Entry(object entry) => Assert.IsType<Dictionary<string, object>>(entry);

        private static object[] Notes(AppCommandResponse response) => Assert.IsType<object[]>(response.Data!["notes"]);

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void BothVerbsAreInTheManifestWithTheirRiskAndColdStart()
        {
            AppCommandSpec add = AppCommandManifest.Shipped.Find("note add")!;
            AppCommandSpec list = AppCommandManifest.Shipped.Find("note list")!;

            Assert.Equal("safe", add.Risk);
            Assert.Equal("start", add.ColdStart);
            Assert.True(add.Arg("text")!.Required);
            Assert.False(add.Arg("title")!.Required);
            Assert.Equal("reads-user-data", list.Risk);
            Assert.Equal("refuse", list.ColdStart);
            Assert.Equal("int", list.Arg("limit")!.Kind);
            foreach (AppCommandSpec spec in new[] { add, list })
            {
                Assert.NotNull(spec.Arg("json"));
                Assert.NotNull(spec.Arg("result"));
                Assert.False(spec.HasLegacyAlias);
            }
        }

        [Theory]
        [InlineData("note|add|--text|buy milk")]
        [InlineData("note|add|--text|x|--title|a name|--result|out.json")]
        [InlineData("note|add|--text=x|--title=y|--json")]
        [InlineData("note|list")]
        [InlineData("note|list|--query|milk|--limit|20|--result|out.json")]
        [InlineData("NOTE|LIST|--limit=500")]
        public void GoodOptionsAreACommand(string line)
            => Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(line.Split('|')).Kind);

        [Theory]
        [InlineData("note|add")]
        [InlineData("note|add|--title|only a name")]
        [InlineData("note|add|--text")]
        [InlineData("note|add|--text|x|--text|y")]
        [InlineData("note|add|--text|x|--query|y")]
        [InlineData("note|list|--limit")]
        [InlineData("note|list|--limit|abc")]
        [InlineData("note|list|--limit|2147483648")]
        [InlineData("note|list|extra")]
        [InlineData("note|list|--text|x")]
        public void AnythingElseIsRefusedAsABadArgument(string line)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(line.Split('|'));
            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, parsed.Reason);
        }

        [Fact]
        public void TheParsersBoundIsInUtf8SoACyrillicTextStopsNearSixteenThousandCharacters()
        {
            // 16384 two-byte letters are exactly 32 KiB: the parser takes them, and so does the verb.
            Assert.Equal(AppCommandLineKind.Command,
                AppCommandLine.Parse(new[] { "note", "add", "--text", new string('я', 16384) }).Kind);
            AppCommandLineResult over = AppCommandLine.Parse(new[] { "note", "add", "--text", new string('я', 16385) });
            Assert.Equal(AppCommandLineKind.Refused, over.Kind);
            Assert.Equal(AppCommandReasons.TooLarge, over.Reason);
        }

        // ------------------------------------------------------------------ note add

        [Fact]
        public async Task TheTextIsStoredByteForByteAndTheAnswerNamesTheNote()
        {
            const string text = " line one\r\n\tindented\nlone\rcr \U0001F600 end  ";
            using QuickNotesService service = Service();

            AppCommandResponse response = await Add(service, "--text", text);

            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal("done", response.Status);
            QuickNote note = Assert.Single(service.Notes);
            Assert.Equal(text, note.RawText);
            Assert.Null(note.Title);
            Assert.Equal(QuickNoteKind.Text, note.Kind);
            Assert.Equal(note.Id.ToString("D"), response.Data!["noteId"]);
            Assert.Equal(note.CreatedAtUtc, note.UpdatedAtUtc);

            service.Dispose();
            using QuickNotesService reopened = Service();
            Assert.Equal(text, Assert.Single(reopened.Notes).RawText);
        }

        [Fact]
        public async Task ABlankTitleIsNoNameAndAGivenOneIsTheName()
        {
            using QuickNotesService service = Service();

            await Add(service, "--text", "a", "--title", "   ");
            await Add(service, "--text", "b", "--title", " Shopping ");
            await Add(service, "--text", "c");

            Assert.Null(service.Notes.Single(n => n.RawText == "a").Title);
            Assert.Equal("Shopping", service.Notes.Single(n => n.RawText == "b").Title);
            Assert.Null(service.Notes.Single(n => n.RawText == "c").Title);
        }

        [Fact]
        public async Task AnEmptyTextIsUsageAndWritesNothingButWhitespaceIsANote()
        {
            using QuickNotesService service = Service();

            AppCommandResponse empty = await Add(service, "--text", "");
            Assert.Equal(AppCommandExit.Usage, empty.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, empty.Reason);
            Assert.Empty(service.Notes);
            Assert.False(File.Exists(_path));

            AppCommandResponse blank = await Add(service, "--text", "   ");
            Assert.Equal(AppCommandExit.Done, blank.ExitCode);
            Assert.Equal("   ", Assert.Single(service.Notes).RawText);
        }

        [Fact]
        public async Task TheVerbsOwnBoundIsThirtyTwoThousandSevenHundredSixtyEightCharacters()
        {
            using QuickNotesService service = Service();

            AppCommandResponse fits = await Add(service, "--text", new string('a', AppCommandsNotes.MaxTextChars));
            Assert.Equal(AppCommandExit.Done, fits.ExitCode);
            Assert.Equal(AppCommandsNotes.MaxTextChars, Assert.Single(service.Notes).RawText.Length);

            AppCommandResponse over = await Add(service, "--text", new string('a', AppCommandsNotes.MaxTextChars + 1));
            Assert.Equal(AppCommandExit.Usage, over.ExitCode);
            Assert.Equal(AppCommandReasons.TooLarge, over.Reason);
            Assert.Single(service.Notes);
        }

        [Fact]
        public async Task ASwitchedOffModuleIsNotReadyAndTheJournalIsNeitherReadNorCreated()
        {
            int asked = 0;
            AppCommandResponse add = await AppCommandsNotes.NoteAdd(Inv("note", "add", "--text", "x"), () => { asked++; return null; }, T, CancellationToken.None);
            AppCommandResponse list = await AppCommandsNotes.NoteList(Inv("note", "list"), () => { asked++; return null; }, T, CancellationToken.None);

            foreach (AppCommandResponse response in new[] { add, list })
            {
                Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
                Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
                Assert.Equal("refused", response.Status);
            }
            Assert.Equal(2, asked);
            Assert.False(File.Exists(_path));
            Assert.False(Directory.Exists(_path));
        }

        [Fact]
        public async Task AFailedWriteIsExitOneAndTheNoteDoesNotExistNowOrLater()
        {
            using QuickNotesService service = Service();
            Assert.Equal(QuickNoteAddResult.Added, service.TryAdd(null, "seed", out _));
            int created = 0, changed = 0, local = 0, failed = 0;
            service.Created += (_, _) => created++;
            service.Changed += (_, _) => changed++;
            service.LocalChanged += _ => local++;
            service.SaveFailed += (_, _) => failed++;

            AppCommandResponse response;
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                response = await Add(service, "--text", "must not appear later");
                Assert.Equal(AppCommandExit.Failed, response.ExitCode);
                Assert.Equal("failed", response.Status);
                Assert.Equal(AppCommandReasons.StoreFailed, response.Reason);
                Assert.Null(response.Data);
            }

            Assert.Equal("seed", Assert.Single(service.Notes).RawText);
            Assert.False(service.SaveFailing, "a failed add is not queued for a retry");
            Assert.Equal(0, created + changed + local + failed);
            service.Flush();                        // what the debounce would do five seconds later
            Assert.Equal("seed", Assert.Single(service.Notes).RawText);
            Assert.Equal(1, File.ReadAllLines(_path).Count(l => l.Length > 0));
            service.Dispose();
            using QuickNotesService reopened = Service();
            Assert.Equal("seed", Assert.Single(reopened.Notes).RawText);
        }

        [Fact]
        public async Task ANotYetLoadedServiceIsAwaitedBeforeTheNoteIsAdded()
        {
            using (QuickNotesService seed = Service())
                Assert.Equal(QuickNoteAddResult.Added, seed.TryAdd("old", "already there", out _));
            using QuickNotesService service = Service(loadNow: false);
            Assert.False(service.IsLoaded);

            AppCommandResponse response = await Add(service, "--text", "new");

            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.True(service.IsLoaded);
            Assert.Equal(2, service.Notes.Count);
        }

        [Fact]
        public async Task ACancelledTokenBeforeTheWriteAddsNothing()
        {
            using QuickNotesService service = Service();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            AppCommandResponse response = await AppCommandsNotes.NoteAdd(Inv("note", "add", "--text", "late"), () => service, T, cancel.Token);

            Assert.Equal(AppCommandExit.NotProven, response.ExitCode);
            Assert.Equal(AppCommandReasons.Timeout, response.Reason);
            Assert.Empty(service.Notes);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public async Task AnUnreadableJournalIsStoreFailedForBothVerbsNeverAnEmptyList()
        {
            File.WriteAllText(_path, "data");
            using var held = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using QuickNotesService service = Service();
            Assert.NotNull(service.ReadFailure);

            AppCommandResponse list = await List(service);
            AppCommandResponse add = await Add(service, "--text", "x");

            foreach (AppCommandResponse response in new[] { list, add })
            {
                Assert.Equal(AppCommandExit.Failed, response.ExitCode);
                Assert.Equal(AppCommandReasons.StoreFailed, response.Reason);
                Assert.Null(response.Data);
            }
            Assert.Empty(service.Notes);
        }

        [Fact]
        public async Task TheResultFileHoldsExactlyTheNoteId()
        {
            using QuickNotesService service = Service();
            AppCommandResponse response = await Add(service, "--text", "buy milk");
            string target = Path.Combine(_root, "note.json");

            Assert.True(AppCommandRunner.WriteResult(target, response));

            Assert.Equal("{\"noteId\":\"" + service.Notes[0].Id.ToString("D") + "\"}", File.ReadAllText(target));
        }

        [Fact]
        public void TryAddRaisesTheUsersEventsAndRefusesBeforeTheReplayAndAfterTheRetirement()
        {
            using QuickNotesService service = Service();
            int created = 0, changed = 0, saved = 0;
            Guid localId = Guid.Empty;
            service.Created += (_, _) => created++;
            service.Changed += (_, _) => changed++;
            service.Saved += (_, _) => saved++;
            service.LocalChanged += added => localId = added;

            Assert.Equal(QuickNoteAddResult.Added, service.TryAdd("t", "body", out Guid id));

            Assert.Equal(1, created);
            Assert.Equal(1, changed);
            Assert.Equal(1, saved);
            Assert.Equal(id, localId);
            Assert.False(service.IsDraft(service.Notes[0]));

            using QuickNotesService notLoaded = Service(loadNow: false);
            Assert.Equal(QuickNoteAddResult.Unavailable, notLoaded.TryAdd(null, "x", out Guid none));
            Assert.Equal(Guid.Empty, none);
            Assert.Empty(notLoaded.Notes);

            service.Dispose();
            Assert.Equal(QuickNoteAddResult.Unavailable, service.TryAdd(null, "after", out _));
        }

        // ------------------------------------------------------------------ note list

        [Fact]
        public async Task TheListIsNewestFirstByCreationWithTheStandardTieBreak()
        {
            using QuickNotesService service = Service();
            var tieA = Text("tie a", 5, id: new Guid("00000000-0000-0000-0000-00000000000a"));
            var tieB = Text("tie b", 5, id: new Guid("00000000-0000-0000-0000-00000000000b"));
            service.AddLocal(Text("oldest", 1));
            service.AddLocal(tieA);
            service.AddLocal(Text("newest", 9));
            service.AddLocal(tieB);

            AppCommandResponse response = await List(service);

            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            object[] notes = Notes(response);
            Assert.Equal(service.Notes.Select(n => n.Id.ToString("D")), notes.Select(n => (string)Entry(n)["id"]));
            Assert.Equal(new[] { "newest", "tie", "tie", "oldest" },
                notes.Select(n => ((string)Entry(n)["preview"]).Split(' ')[0]));
            string[] created = notes.Select(n => (string)Entry(n)["createdUtc"]).ToArray();
            Assert.Equal(created.OrderByDescending(c => c, StringComparer.Ordinal), created);
            Assert.Equal("2026-11-14T10:09:30Z", created[0]);
        }

        [Fact]
        public async Task TheQuerySearchesTheNameTheTextAndEveryChecklistItemLikeTheWindow()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("buy milk and bread", 1));
            service.AddLocal(Text("nothing here", 2, title: "Milk run"));
            service.AddLocal(Checklist(3, "eggs", "buy MILK"));
            service.AddLocal(Text("unrelated", 4));

            AppCommandResponse response = await List(service, "--query", "milk");

            Assert.Equal(3, response.Data!["count"]);
            Assert.Equal(3, response.Data["total"]);
            Assert.Equal(service.Search("milk").Select(n => n.Id.ToString("D")), Notes(response).Select(n => (string)Entry(n)["id"]));

            AppCommandResponse all = await List(service);
            Assert.Equal(4, all.Data!["count"]);
            AppCommandResponse alsoAll = await List(service, "--query", "");
            Assert.Equal(4, alsoAll.Data!["total"]);
            AppCommandResponse none = await List(service, "--query", "zzz-no-such-text");
            Assert.Equal(AppCommandExit.Done, none.ExitCode);
            Assert.Equal(0, none.Data!["count"]);
            Assert.Equal(0, none.Data["total"]);
            Assert.Empty(Notes(none));
        }

        [Fact]
        public async Task TheLimitCutsTheListAndTotalStillCountsEveryMatch()
        {
            using QuickNotesService service = Service();
            for (int i = 1; i <= 3; i++) service.AddLocal(Text("note " + i, i));

            AppCommandResponse cut = await List(service, "--limit", "2");
            Assert.Equal(2, cut.Data!["count"]);
            Assert.Equal(3, cut.Data["total"]);
            Assert.Equal(2, Notes(cut).Length);

            Assert.Equal(1, (await List(service, "--limit", "1")).Data!["count"]);
            Assert.Equal(3, (await List(service, "--limit", "500")).Data!["count"]);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("501")]
        [InlineData("-5")]
        public async Task ALimitOutsideOneToFiveHundredIsUsage(string limit)
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("x", 1));

            AppCommandResponse response = await List(service, "--limit", limit);

            Assert.Equal(AppCommandExit.Usage, response.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, response.Reason);
            Assert.Null(response.Data);
        }

        [Fact]
        public async Task TheTitleIsAbsentForAnUnnamedNoteAndThePreviewIsTheBodyNeverTheName()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("body of the named one\nsecond line", 2, title: "The Name"));
            service.AddLocal(Text("body of the unnamed one", 1));

            object[] notes = Notes(await List(service));

            Dictionary<string, object> named = Entry(notes[0]);
            Assert.Equal("The Name", named["title"]);
            Assert.Equal("body of the named one", named["preview"]);
            Dictionary<string, object> unnamed = Entry(notes[1]);
            Assert.False(unnamed.ContainsKey("title"));
            Assert.Equal("body of the unnamed one", unnamed["preview"]);
            Assert.Equal(new[] { "createdUtc", "id", "preview", "title", "updatedUtc" }, named.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void ThePreviewRules()
        {
            Assert.Equal("a b", Text("a\tb", 1).BodyPreview(120));
            Assert.Equal("first", Text("\r\n\n  \n  first  \nsecond", 1).BodyPreview(120));
            Assert.Equal("second", Checklist(1, "", "   ", "second", "third").BodyPreview(120));
            Assert.Equal("", Text("", 1, title: "only a name").BodyPreview(120));
            Assert.Equal("", Checklist(1).BodyPreview(120));

            string exactly = new string('x', 120);
            Assert.Equal(exactly, Text(exactly, 1).BodyPreview(120));
            string cut = Text(new string('x', 121), 1).BodyPreview(120);
            Assert.Equal(120, cut.Length);
            Assert.EndsWith("…", cut);
            Assert.Equal(new string('x', 119), cut.Substring(0, 119));
        }

        [Fact]
        public void ThePreviewNeverCutsInsideASurrogatePair()
        {
            // The emoji's two halves sit at indexes 118 and 119: a cut at 119 would keep only the high half.
            string body = new string('a', 118) + "\U0001F600" + "bbbb";
            string preview = Text(body, 1).BodyPreview(120);

            Assert.True(preview.Length <= 120);
            Assert.EndsWith("…", preview);
            Assert.False(preview.Any(char.IsSurrogate), "no lone half of a pair in the answer");
            Assert.Equal(new string('a', 118) + "…", preview);

            string fitsWhole = new string('a', 117) + "\U0001F600" + "b";
            Assert.Equal(fitsWhole, Text(fitsWhole, 1).BodyPreview(120));
        }

        [Fact]
        public void TheAnswerStaysUnderTheCallersReadBoundWhateverTheNotesAre()
        {
            var notes = new List<QuickNote>();
            for (int i = 0; i < 500; i++)
                notes.Add(Text(new string('я', 300), i % 59, title: new string('Ж', 400)));

            object[] entries = AppCommandsNotes.Entries(notes, 500);

            Assert.InRange(entries.Length, 1, 499);
            Assert.Equal(notes[0].Id.ToString("D"), Entry(entries[0])["id"]);
            Assert.Equal(AppCommandsNotes.TitleChars, ((string)Entry(entries[0])["title"]).Length);
            var data = new Dictionary<string, object> { ["count"] = entries.Length, ["total"] = 500, ["notes"] = entries };
            string line = AppCommandProtocol.Serialize(new AppCommandResponse { RequestId = Guid.NewGuid().ToString("D"), Data = data, Message = "Notes listed: 0 of 0." });
            Assert.True(Encoding.UTF8.GetByteCount(line) < AppCommandLine.MaxRequestBytes);

            var small = new List<QuickNote> { Text("one", 1), Text("two", 2), Text("three", 3) };
            Assert.Equal(3, AppCommandsNotes.Entries(small, 500).Length);
        }

        [Fact]
        public async Task TheAnswerIsOneJsonObjectAToolCanRead()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("buy milk", 1, title: "Shopping"));

            AppCommandResponse response = await List(service, "--result", "unused");
            AppCommandResponse? parsed = AppCommandProtocol.TryParseResponse(
                AppCommandProtocol.Serialize(new AppCommandResponse { RequestId = "r", Data = response.Data, Message = response.Message }));

            Assert.NotNull(parsed);
            Assert.Equal(1, parsed!.Data!["count"]);
            Dictionary<string, object> entry = Entry(Assert.IsType<object[]>(parsed.Data["notes"])[0]);
            Assert.Equal("Shopping", entry["title"]);
            Assert.Equal("buy milk", entry["preview"]);
        }

        // ------------------------------------------------------------------ note get and export (S0120)

        [Fact]
        public void TheReadVerbsAreInTheManifestWithTheirRows()
        {
            AppCommandSpec get = AppCommandManifest.Shipped.Find("note get")!;
            AppCommandSpec export = AppCommandManifest.Shipped.Find("note export")!;

            Assert.Equal("reads-user-data", get.Risk);
            Assert.Equal("refuse", get.ColdStart);
            Assert.Equal(10000, get.TimeoutMs);
            Assert.Equal("guid", get.Arg("id")!.Kind);
            Assert.True(get.Arg("id")!.Required);
            Assert.Equal("reads-user-data", export.Risk);
            Assert.Equal("refuse", export.ColdStart);
            Assert.Equal(60000, export.TimeoutMs);
            Assert.Equal("path", export.Arg("out")!.Kind);
            Assert.True(export.Arg("out")!.Required);
            Assert.NotNull(export.Arg("overwrite"));
            foreach (AppCommandSpec spec in new[] { get, export })
            {
                Assert.Equal("done", spec.Wait);
                Assert.NotNull(spec.Arg("json"));
                Assert.NotNull(spec.Arg("result"));
                Assert.False(spec.HasLegacyAlias);
            }
        }

        [Theory]
        [InlineData("note|get|--id|12345678-90ab-cdef-1234-567890abcdef")]
        [InlineData("note|get|--id|12345678-90ab-cdef-1234-567890abcdef|--json|false|--result|out.json")]
        [InlineData("NOTE|GET|--id=12345678-90ab-cdef-1234-567890abcdef")]
        [InlineData("note|export|--out|C:\\work\\backup.txt")]
        [InlineData("note|export|--out|b.txt|--overwrite|--json|--result|out.json")]
        [InlineData("NOTE|EXPORT|--out=b.txt|--overwrite=true")]
        public void GoodGetAndExportLinesAreACommand(string line)
            => Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(line.Split('|')).Kind);

        [Theory]
        [InlineData("note|get")]
        [InlineData("note|get|--id")]
        [InlineData("note|get|--id|not-a-guid")]
        [InlineData("note|get|--id|12345678-90ab-cdef-1234-567890abcdef|--limit|5")]
        [InlineData("note|export")]
        [InlineData("note|export|--out")]
        [InlineData("note|export|--out|C:\\work\\a.txt|--overwrite|maybe")]
        [InlineData("note|export|--out|a.txt|extra")]
        [InlineData("note|export|--id|12345678-90ab-cdef-1234-567890abcdef")]
        public void AnythingElseIsRefusedByTheParserBeforeAnyHandler(string line)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(line.Split('|'));
            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, parsed.Reason);
        }

        [Fact]
        public async Task NoteGetAnswersTheWholeNoteAsItIsStored()
        {
            const string body = " line one\r\n\tindented \U0001F600\r\nlone\rcr  ";
            using QuickNotesService service = Service();
            service.AddLocal(Text(body, 5, title: "The Name"));
            service.AddLocal(Text(body, 3));

            AppCommandResponse named = await Get(service, "--id", service.Notes[0].Id.ToString("D"));
            AppCommandResponse unnamed = await Get(service, "--id", service.Notes[1].Id.ToString("D"));

            foreach (AppCommandResponse response in new[] { named, unnamed })
            {
                Assert.Equal(AppCommandExit.Done, response.ExitCode);
                Assert.Equal("done", response.Status);
                var data = response.Data!;
                Assert.Equal("text", data["kind"]);
                Assert.Equal(body, data["text"]);
                Assert.Equal(body.Length, data["length"]);
                Assert.Equal(false, data["truncated"]);
                Assert.Equal("2026-11-14T10:05:30Z", data["createdUtc"]);
                Assert.Equal("2026-11-14T10:05:30Z", data["updatedUtc"]);
            }
            Assert.Equal(service.Notes[0].Id.ToString("D"), named.Data!["id"]);
            Assert.Equal("The Name", named.Data!["title"]);
            Assert.False(unnamed.Data!.ContainsKey("title"));
            Assert.Equal(new[] { "createdUtc", "id", "kind", "length", "text", "title", "truncated", "updatedUtc" },
                named.Data!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public async Task NoteGetAnswersAChecklistAsKindAndItemsWithTheirDoneStates()
        {
            using QuickNotesService service = Service();
            var checklist = Checklist(2, "first", "buy MILK", "third");
            checklist.Items[1].IsChecked = true;
            service.AddLocal(checklist);

            AppCommandResponse response = await Get(service, "--id", checklist.Id.ToString("D"));

            var data = response.Data!;
            Assert.Equal("checklist", data["kind"]);
            Assert.False(data.ContainsKey("text"));
            object[] items = Assert.IsType<object[]>(data["items"]);
            Assert.Equal(3, items.Length);
            Assert.Equal("first", Entry(items[0])["text"]);
            Assert.Equal(false, Entry(items[0])["done"]);
            Assert.Equal("buy MILK", Entry(items[1])["text"]);
            Assert.Equal(true, Entry(items[1])["done"]);
            Assert.Equal("third", Entry(items[2])["text"]);
            Assert.Equal(false, Entry(items[2])["done"]);
            Assert.Equal(checklist.CharacterCount, data["length"]);
            Assert.Equal(false, data["truncated"]);
        }

        [Fact]
        public async Task AnIdNoNoteCarriesIsTargetMissingAndNamesNoNote()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text(Marker + " body", 1, title: Marker));

            AppCommandResponse response = await Get(service, "--id", Guid.NewGuid().ToString("D"));

            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, response.Reason);
            Assert.Null(response.Data);
            Assert.DoesNotContain(Marker, response.Message);
        }

        [Fact]
        public async Task ATextOverTheAnswerBoundIsCutAndTheCutIsTold()
        {
            // 30 000 two-byte letters are already past the 48 KiB answer bound; the emoji sits after them.
            string body = new string('я', 30000) + "\U0001F600" + new string('b', 5000);
            using QuickNotesService service = Service();
            service.AddLocal(Text(body, 1, title: "big"));

            AppCommandResponse response = await Get(service, "--id", service.Notes[0].Id.ToString("D"));

            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            var data = response.Data!;
            Assert.Equal(true, data["truncated"]);
            Assert.Equal(body.Length, data["length"]);
            string cut = Assert.IsType<string>(data["text"]);
            Assert.NotEqual(body, cut);
            Assert.True(cut.Length < body.Length);
            Assert.False(char.IsSurrogate(cut[cut.Length - 1]), "a lone half of a pair never ends the cut");
            Assert.True(Encoding.UTF8.GetByteCount(new JavaScriptSerializer().Serialize(data)) <= AppCommandsNotes.MaxAnswerDataBytes);
        }

        [Fact]
        public async Task GetAndExportAnswerModuleOffBeforeAnythingElseRuns()
        {
            var fs = new ScreenshotSaverCommandTests.CommandFs();
            int asked = 0;
            Func<QuickNotesService?> off = () => { asked++; return null; };

            AppCommandResponse get = await AppCommandsNotes.NoteGet(Inv("note", "get", "--id", Guid.NewGuid().ToString("D")),
                off, T, CancellationToken.None);
            AppCommandResponse export = await AppCommandsNotes.NoteExport(Inv("note", "export", "--out", @"C:\work\backup.txt"),
                @"C:\work", off, fs, TimeSpan.FromSeconds(10), path => false, T, CancellationToken.None);

            foreach (AppCommandResponse response in new[] { get, export })
            {
                Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
                Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            }
            Assert.Equal(2, asked);
            Assert.Empty(fs.Files);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public async Task AnUnreadableJournalIsStoreFailedForGetAndExportNeverAnEmptyAnswer()
        {
            File.WriteAllText(_path, "data");
            using var held = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using QuickNotesService service = Service();
            Assert.NotNull(service.ReadFailure);
            var fs = new ScreenshotSaverCommandTests.CommandFs();

            AppCommandResponse get = await Get(service, "--id", Guid.NewGuid().ToString("D"));
            AppCommandResponse export = await Export(service, fs, "--out", @"C:\work\backup.txt");

            foreach (AppCommandResponse response in new[] { get, export })
            {
                Assert.Equal(AppCommandExit.Failed, response.ExitCode);
                Assert.Equal(AppCommandReasons.StoreFailed, response.Reason);
                Assert.Null(response.Data);
            }
            Assert.Empty(fs.Files);
        }

        // ------------------------------------------------------------------ the export write

        [Fact]
        public async Task TheExportWritesEveryNoteNewestFirstExactlyAsGiven()
        {
            using QuickNotesService service = Service();
            var text = Text(" line one\r\n\tindented \U0001F600", 9, title: "Named");
            var unnamed = Text("second\n", 5);
            var checklist = Checklist(3, "eggs", "buy MILK");
            checklist.Title = "List";
            checklist.Items[0].IsChecked = true;
            service.AddLocal(text);
            service.AddLocal(unnamed);
            service.AddLocal(checklist);

            var fs = new ScreenshotSaverCommandTests.CommandFs();
            AppCommandResponse response = await Export(service, fs, "--out", @"C:\work\backup.txt");

            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal(@"C:\work\backup.txt", response.Data!["path"]);
            Assert.Equal(3, response.Data["notes"]);
            Assert.Contains("3", response.Message, StringComparison.Ordinal);

            string file = Encoding.UTF8.GetString(fs.Files[@"C:\work\backup.txt"]);
            Assert.Equal(CyrFlipExchange.Marker, file.Split('\n')[0].TrimEnd('\r'));
            Assert.Contains("Includes: quick-notes", file);
            Assert.DoesNotContain("# Clipboard history", file);
            Assert.Equal(3, file.Split('\n').Count(line => line.TrimEnd('\r') == "## Note"));
            Assert.Contains("\r\n\tindented", file);
            Assert.Contains("Kind: checklist", file);
            Assert.Contains("- [x] eggs", file);
            int newestAt = file.IndexOf("Id: " + text.Id.ToString("D"), StringComparison.Ordinal);
            int middleAt = file.IndexOf("Id: " + unnamed.Id.ToString("D"), StringComparison.Ordinal);
            int oldestAt = file.IndexOf("Id: " + checklist.Id.ToString("D"), StringComparison.Ordinal);
            Assert.True(newestAt >= 0 && middleAt > newestAt && oldestAt > middleAt, "newest first by creation");
            Assert.All(fs.Files.Keys, key => Assert.False(key.EndsWith(".tmp", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task AnExistingFileIsRefusedWithoutOverwriteAndReplacedWithIt()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));
            var fs = new ScreenshotSaverCommandTests.CommandFs();
            fs.Files[@"C:\work\backup.txt"] = new byte[] { 1, 2, 3 };

            AppCommandResponse refused = await Export(service, fs, "--out", @"C:\work\backup.txt");
            Assert.Equal(AppCommandExit.NotReady, refused.ExitCode);
            Assert.Equal(AppCommandReasons.Exists, refused.Reason);
            Assert.Equal(new byte[] { 1, 2, 3 }, fs.Files[@"C:\work\backup.txt"]);
            Assert.All(fs.Files.Keys, key => Assert.False(key.EndsWith(".tmp", StringComparison.Ordinal)));

            AppCommandResponse replaced = await Export(service, fs, "--out", @"C:\work\backup.txt", "--overwrite");
            Assert.Equal(AppCommandExit.Done, replaced.ExitCode);
            Assert.Contains("## Note", Encoding.UTF8.GetString(fs.Files[@"C:\work\backup.txt"]));
        }

        [Fact]
        public async Task AMissingFolderIsTargetMissingAndIsNeverCreated()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));
            var fs = new ScreenshotSaverCommandTests.CommandFs();

            AppCommandResponse response = await Export(service, fs, "--out", @"C:\no-such-folder\backup.txt");

            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, response.Reason);
            Assert.Null(response.Data);
            Assert.Empty(fs.Created);
            Assert.Empty(fs.Files);
        }

        [Fact]
        public async Task AFolderShapedTargetIsUsageAndADirectoryAtTheTargetIsExists()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));
            var fs = new ScreenshotSaverCommandTests.CommandFs();

            AppCommandResponse trailing = await Export(service, fs, "--out", @"C:\work\");
            Assert.Equal(AppCommandExit.Usage, trailing.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, trailing.Reason);

            AppCommandResponse directory = await Export(service, fs, "--out", @"C:\work");
            Assert.Equal(AppCommandExit.NotReady, directory.ExitCode);
            Assert.Equal(AppCommandReasons.Exists, directory.Reason);

            AppCommandResponse root = await Export(service, fs, "--out", @"C:\");
            Assert.Equal(AppCommandExit.Usage, root.ExitCode);

            AppCommandResponse refused = await Export(service, fs, "--out", @"C:\work\bad|name.txt");
            Assert.Equal(AppCommandExit.Usage, refused.ExitCode);

            Assert.Empty(fs.Files);
            Assert.Empty(fs.Created);
        }

        [Fact]
        public async Task ADiskFullIsExitThirteenAndLeavesNothing()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));
            var fs = new ScreenshotSaverCommandTests.CommandFs { WriteFailure = new IOException("full", 112) };

            AppCommandResponse response = await Export(service, fs, "--out", @"C:\work\backup.txt");

            Assert.Equal(AppCommandExit.NoSpace, response.ExitCode);
            Assert.Equal(AppCommandReasons.DiskFull, response.Reason);
            Assert.Null(response.Data);
            Assert.False(fs.Files.ContainsKey(@"C:\work\backup.txt"));
            Assert.All(fs.Files.Keys, key => Assert.False(key.EndsWith(".tmp", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task TheWriteIsATemporaryFileInTheTargetsFolderThenARename()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));
            var fs = new ScreenshotSaverCommandTests.CommandFs();

            await Export(service, fs, "--out", @"C:\work\backup.txt");

            Assert.Equal(2, fs.Log.Count);
            Assert.StartsWith("write C:\\work\\.backup.txt.", fs.Log[0]);
            Assert.EndsWith(".tmp", fs.Log[0]);
            Assert.Equal("move C:\\work\\backup.txt", fs.Log[1]);
            Assert.Single(fs.Files.Keys, @"C:\work\backup.txt");
        }

        [Fact]
        public async Task AShareThatDoesNotAnswerIsFifteenAndALocalPathThatDoesNotIsWriteFailed()
        {
            using QuickNotesService service = Service();
            service.AddLocal(Text("one", 1));

            var shareFs = new ScreenshotSaverCommandTests.CommandFs();
            using var shareGate = new ManualResetEvent(false);
            shareFs.ProbeGate = shareGate;
            try
            {
                AppCommandResponse share = await AppCommandsNotes.NoteExport(
                    Inv("note", "export", "--out", @"\\nas\share\notes.txt"), @"C:\work", () => service,
                    shareFs, TimeSpan.FromMilliseconds(200), path => path.StartsWith(@"\\", StringComparison.Ordinal), T, CancellationToken.None);
                Assert.Equal(AppCommandExit.Network, share.ExitCode);
                Assert.Equal(AppCommandReasons.Network, share.Reason);
                Assert.Null(share.Data);
            }
            finally { shareGate.Set(); }

            var localFs = new ScreenshotSaverCommandTests.CommandFs();
            using var localGate = new ManualResetEvent(false);
            localFs.ProbeGate = localGate;
            try
            {
                AppCommandResponse local = await AppCommandsNotes.NoteExport(
                    Inv("note", "export", "--out", @"C:\work\stuck.txt"), @"C:\work", () => service,
                    localFs, TimeSpan.FromMilliseconds(200), path => false, T, CancellationToken.None);
                Assert.Equal(AppCommandExit.Failed, local.ExitCode);
                Assert.Equal(AppCommandReasons.WriteFailed, local.Reason);
                Assert.Equal("failed", local.Status);
            }
            finally { localGate.Set(); }
        }

        [Fact]
        public async Task ThePathIsCheckedBeforeTheJournalIsRead()
        {
            File.WriteAllText(_path, "data");
            using var held = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using QuickNotesService service = Service();
            var fs = new ScreenshotSaverCommandTests.CommandFs();

            AppCommandResponse response = await Export(service, fs, "--out", @"C:\no-such\backup.txt");

            Assert.Equal(AppCommandReasons.TargetMissing, response.Reason);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
        }

        [Fact]
        public void AnExportThenImportRoundTripLosesNothingAndASecondImportChangesNothing()
        {
            const string body = " line one\r\n\tindented \U0001F600\rlone cr";
            using var seeded = Service();
            seeded.AddLocal(Text(body, 9, title: "Named"));
            var list = Checklist(5, "eggs", "buy MILK", "third");
            list.Title = "List";
            list.Items[1].IsChecked = true;
            seeded.AddLocal(list);
            seeded.AddLocal(Text("unnamed body", 3));

            string file = Path.Combine(_root, "roundtrip.txt");
            CyrFlipExchangeWriter.WriteFile(file, CyrFlipExchangeWriter.ToText(DateTime.UtcNow, new List<QuickNote>(seeded.Notes), null));

            ExchangeReadResult read = CyrFlipExchangeReader.ReadFile(file);
            Assert.Null(read.Fatal);
            Assert.Empty(read.Problems);
            Assert.Equal(3, read.Notes.Count);

            using var restored = Service();
            Assert.Equal(3, restored.PlanImport(read.Notes, includeNew: true).NotesAdded);
            Assert.Equal(3, restored.Import(read.Notes, includeNew: true).NotesAdded);
            Assert.Equal(3, restored.Notes.Count);

            QuickNote named = restored.Notes.Single(n => n.Title == "Named");
            Assert.Equal(QuickNoteKind.Text, named.Kind);
            Assert.Equal(body, named.RawText);
            Assert.Equal("2026-11-14T10:09:30Z", CyrFlipExchangeWriter.Date(named.CreatedAtUtc));
            Assert.Equal("2026-11-14T10:09:30Z", CyrFlipExchangeWriter.Date(named.UpdatedAtUtc));
            QuickNote checklist = restored.Notes.Single(n => n.Title == "List");
            Assert.Equal(QuickNoteKind.Checklist, checklist.Kind);
            Assert.Equal(new[] { "eggs", "buy MILK", "third" }, checklist.Items.Select(i => i.Text));
            Assert.True(checklist.Items[1].IsChecked);
            QuickNote unnamed = restored.Notes.Single(n => n.RawText == "unnamed body");
            Assert.Null(unnamed.Title);

            // An unchanged export, imported a second time: a tie keeps the local note, the dates are already to the second.
            ExchangeMergeReport again = restored.Import(read.Notes, includeNew: true);
            Assert.Equal(0, again.NotesAdded);
            Assert.Equal(0, again.NotesUpdated);
            Assert.Equal(3, restored.Notes.Count);
            Assert.Equal(body, restored.Notes.Single(n => n.Title == "Named").RawText);
            Assert.True(restored.Notes.Single(n => n.Title == "List").Items[1].IsChecked);
        }

        // ------------------------------------------------------------------ the gate and the log

        [Fact]
        public void TheSettingGatesTheDataVerbsNotTheAddAndRunsBeforeAnyHandler()
        {
            AppCommandSpec add = AppCommandManifest.Shipped.Find("note add")!;

            Assert.Null(AppCommandVerbs.RiskGate(add, settingOn: false, legacy: false, T));
            foreach (string name in new[] { "note list", "note get", "note export" })
            {
                AppCommandSpec spec = AppCommandManifest.Shipped.Find(name)!;
                AppCommandResponse? refused = AppCommandVerbs.RiskGate(spec, settingOn: false, legacy: false, T);
                Assert.NotNull(refused);
                Assert.Equal(AppCommandExit.Permission, refused!.ExitCode);
                Assert.Equal(AppCommandReasons.SwitchOff, refused.Reason);
                Assert.Null(AppCommandVerbs.RiskGate(spec, settingOn: true, legacy: false, T));
            }
        }

        [Fact]
        public async Task NoLogLineAndNoMessageHoldsACharacterOfANote()
        {
            using QuickNotesService service = Service();

            AppCommandResponse add = await Add(service, "--text", Marker + " body", "--title", Marker + " title");
            AppCommandResponse list = await List(service, "--query", Marker);
            AppCommandResponse refused = await Add(service, "--text", "");
            AppCommandResponse exported = await Export(service, new ScreenshotSaverCommandTests.CommandFs(),
                "--out", @"C:\work\" + Marker + ".txt");

            string addLine = AppCommandsLog.Line(DateTime.Now, "note add", "cli", 42, add);
            string listLine = AppCommandsLog.Line(DateTime.Now, "note list", "cli", 42, list);
            string exportLine = AppCommandsLog.Line(DateTime.Now, "note export", "cli", 42, exported);
            foreach (string text in new[] { addLine, listLine, exportLine, add.Message, list.Message, refused.Message, exported.Message })
                Assert.DoesNotContain(Marker, text);
            Assert.EndsWith(" count=1", listLine);
            Assert.EndsWith(" count=1", exportLine);
            Assert.DoesNotContain("count=", addLine);
            Assert.DoesNotContain("count=", AppCommandsLog.Line(DateTime.Now, "status", "cli", 42, AppCommandResponse.Done("ok")));
        }
    }
}
