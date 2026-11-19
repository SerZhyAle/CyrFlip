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
    /// The three clipboard-history verbs and the two exchange option sets of ticket S0121: the handlers over a real
    /// <see cref="ClipboardHistoryService"/> on a temporary journal - no DPAPI (the fake cipher), no window, no
    /// clipboard (the restore seam is faked) - and the option resolution over faked seams (the file probe, the file
    /// copy, the saver's file-system seam). The cases that need a running tray copy, a real clipboard write or the
    /// network - the strip's own restore through the real clipboard, a real transfer, the module gates against the
    /// live app - are <c>tools/uitest/Test-AppCommands.ps1</c>'s and the release's.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsClipboardHistoryTests : IDisposable
    {
        private const string Marker = "SECRET-MARKER-4711";

        private readonly string _root;

        public AppCommandsClipboardHistoryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-histcmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
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

        private sealed class FakeHistoryHost : IHistoryCommandsHost
        {
            public bool On = true;
            public readonly List<bool> Set = new List<bool>();
            public bool ClipboardHistoryOn => On;
            public void SetPaused(bool paused) => Set.Add(paused);
        }

        private sealed class FakeRestore
        {
            public ClipboardHistoryEntry? Restored;
            public bool Result = true;
            public bool Restore(ClipboardHistoryEntry entry)
            {
                Restored = entry;
                return Result;
            }
        }

        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static Task<AppCommandResponse> List(ClipboardHistoryService? service, params string[] options)
            => AppCommandsClipboardHistory.ListHistory(Inv(new[] { "clipboard-history", "list" }.Concat(options).ToArray()),
                () => service, T, CancellationToken.None);

        private static Task<AppCommandResponse> Put(ClipboardHistoryService? service, FakeRestore restore, params string[] options)
            => AppCommandsClipboardHistory.PutHistory(Inv(new[] { "clipboard-history", "put" }.Concat(options).ToArray()),
                () => service, restore.Restore, T, CancellationToken.None);

        private static Task<AppCommandResponse> Send(params string[] options)
            => SourceOf(options).ContinueWith(t => t.Result.Refusal
                ?? AppCommandResponse.Done(t.Result.Wave1 ? "wave1" : t.Result.Payload!.Kind));

        /// <summary>The send's source resolution over a faked file probe; the tests assign <see cref="_stat"/> per case.</summary>
        private static Task<SendSource> SourceOf(string[] options, TimeSpan? budget = null)
            => AppCommandsClipboardExchange.ResolveSendSource(Inv(new[] { "clipboard", "send" }.Concat(options).ToArray()),
                @"C:\work", budget ?? TimeSpan.FromSeconds(10), stat => _stat,
                path => path.StartsWith(@"\\", StringComparison.Ordinal), T, CancellationToken.None);

        private static ExchangeSourceStat _stat = new ExchangeSourceStat { Exists = false };

        private static DateTime At(int minute) => new DateTime(2026, 11, 17, 10, minute, 30, DateTimeKind.Utc);

        private static ClipboardHistoryEntry Seed(ClipboardHistoryService service, string text, int minute, bool pinned = false)
        {
            service.Import(new[]
            {
                new ExchangeClipboardItem
                {
                    Uuid = ClipboardHistoryService.Hash(text), Text = text, CopiedAtUtc = At(minute), Pinned = pinned, SourceApp = "Code",
                    SourceTitle = "The source window of " + Marker,
                },
            });
            return service.Entries.Single(e => e.Text == text);
        }

        /// <summary>The service half runs on an owner STA thread, the way the strip's own tests build it.</summary>
        private void WithService(Action<ClipboardHistoryService, string> body)
        {
            OnStaThread(() =>
            {
                using var service = new ClipboardHistoryService(enabled: false, paused: false, _root, new FakeCipher());
                body(service, _root);
            });
        }

        private static void OnStaThread(Action body)
            => OnStaThread<object?>(() => { body(); return null; });

        private static T OnStaThread<T>(Func<T> body)
        {
            Exception? failure = null;
            T result = default!;
            var thread = new Thread(() =>
            {
                try { result = body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("owner-thread failure", failure);
            return result;
        }

        private static Dictionary<string, object> Item(object entry) => Assert.IsType<Dictionary<string, object>>(entry);

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheFiveSpellingsAreInTheManifestWithTheRowsFields()
        {
            AppCommandSpec list = AppCommandManifest.Shipped.Find("clipboard-history list")!;
            AppCommandSpec put = AppCommandManifest.Shipped.Find("clipboard-history put")!;
            AppCommandSpec pause = AppCommandManifest.Shipped.Find("clipboard-history pause")!;
            AppCommandSpec send = AppCommandManifest.Shipped.Find("clipboard send")!;
            AppCommandSpec get = AppCommandManifest.Shipped.Find("clipboard get")!;

            Assert.Equal("reads-user-data", list.Risk);
            Assert.Equal("refuse", list.ColdStart);
            Assert.Equal(10000, list.TimeoutMs);
            Assert.True(list.Idempotent);
            Assert.Equal("int", list.Arg("limit")!.Kind);
            Assert.Equal("bool", list.Arg("pinned")!.Kind);

            Assert.Equal("writes-user-data", put.Risk);
            Assert.Equal("refuse", put.ColdStart);
            Assert.Equal(5000, put.TimeoutMs);
            Assert.True(put.Idempotent);
            Assert.True(put.Arg("id")!.Required);

            Assert.Equal("safe", pause.Risk);
            Assert.Equal("refuse", pause.ColdStart);
            Assert.Equal(2000, pause.TimeoutMs);
            Assert.True(pause.Idempotent);
            Assert.True(pause.Arg("paused")!.Required);
            Assert.Equal("bool", pause.Arg("paused")!.Kind);

            Assert.Equal("path", send.Arg("file")!.Kind);
            Assert.False(send.Arg("file")!.Required);
            Assert.Equal("string", send.Arg("text")!.Kind);
            Assert.Equal("leaves-machine", send.Risk);
            Assert.Equal(600000, send.TimeoutMs);
            Assert.False(send.Idempotent);

            Assert.Equal("path", get.Arg("out")!.Kind);
            Assert.Equal("bool", get.Arg("overwrite")!.Kind);
            Assert.Equal("leaves-machine", get.Risk);
            Assert.Equal(300000, get.TimeoutMs);
        }

        [Theory]
        [InlineData("clipboard-history", "list")]
        [InlineData("clipboard-history", "list", "--query", "report")]
        [InlineData("clipboard-history", "list", "--limit", "5", "--pinned")]
        [InlineData("clipboard-history", "put", "--id", "63A0B7C9D4E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2C3D4E5F")]
        [InlineData("clipboard-history", "pause", "--paused", "true")]
        [InlineData("clipboard-history", "pause", "--paused", "false")]
        [InlineData("clipboard", "send", "--file", @"C:\work\a.txt")]
        [InlineData("clipboard", "send", "--text", "hello")]
        [InlineData("clipboard", "get", "--out", @"C:\work\o.txt", "--overwrite")]
        public void TheParserTakesTheOptionLines(params string[] args)
        {
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(args).Kind);
        }

        [Theory]
        [InlineData("unknown option", "clipboard-history", "list", "--zzz")]
        [InlineData("missing --paused", "clipboard-history", "pause")]
        [InlineData("pinned is not true/false", "clipboard-history", "pause", "--paused", "yes")]
        [InlineData("missing --id", "clipboard-history", "put")]
        [InlineData("limit is not a whole number", "clipboard-history", "list", "--limit", "abc")]
        public void TheParserRefusesWhatTheRowsRefuse(string because, params string[] args)
        {
            Assert.Equal(AppCommandLineKind.Refused, AppCommandLine.Parse(args).Kind);
        }

        [Fact]
        public void ATextOverTheArgumentCapIsRefusedByTheParserItself()
        {
            string tooLong = new string('x', 33 * 1024);
            AppCommandLineResult parsed = AppCommandLine.Parse(new[] { "clipboard", "send", "--text", tooLong });
            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.TooLarge, parsed.Reason);
        }

        [Fact]
        public void TheRuleThirteenGateRefusesTheStoreVerbsButNeverTheSafePause()
        {
            AppCommandSpec list = AppCommandManifest.Shipped.Find("clipboard-history list")!;
            AppCommandSpec put = AppCommandManifest.Shipped.Find("clipboard-history put")!;
            AppCommandSpec pause = AppCommandManifest.Shipped.Find("clipboard-history pause")!;

            Assert.NotNull(AppCommandVerbs.RiskGate(list, settingOn: false, legacy: false, T));
            Assert.NotNull(AppCommandVerbs.RiskGate(put, settingOn: false, legacy: false, T));
            Assert.Null(AppCommandVerbs.RiskGate(pause, settingOn: false, legacy: false, T));
        }

        // ------------------------------------------------------------------ the three history verbs

        [Fact]
        public void ModuleOffAnswersBeforeTheJournalIsReadOrCreated()
        {
            Assert.False(File.Exists(Path.Combine(_root, "clipboard-history.log")));
            AppCommandResponse list = OnStaThread(() => List(null).Result);
            AppCommandResponse put = OnStaThread(() => Put(null, new FakeRestore(), "--id", new string('a', 64)).Result);
            AppCommandResponse pause = AppCommandsClipboardHistory.PauseHistory(
                Inv("clipboard-history", "pause", "--paused", "true"), new FakeHistoryHost { On = false }, T);

            foreach (AppCommandResponse answer in new[] { list, put, pause })
            {
                Assert.Equal(AppCommandReasons.ModuleOff, answer.Reason);
                Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
            }
            Assert.False(File.Exists(Path.Combine(_root, "clipboard-history.log")));
        }

        [Fact]
        public void ListAnswersPreviewsInTheStandardOrderWithQueryLimitAndPinned()
        {
            WithService((service, dir) =>
            {
                ClipboardHistoryEntry pinned = Seed(service, "pinned report " + Marker, 5, pinned: true);
                for (int minute = 6; minute <= 9; minute++)
                    Seed(service, "copy at " + At(minute).ToString("mm") + " " + Marker, minute);
                Seed(service, "unrelated older passed deadline " + Marker, 1);

                AppCommandResponse all = List(service).Result;
                Assert.Equal(AppCommandExit.Done, all.ExitCode);
                Assert.Equal(6, (int)all.Data!["total"]);
                object[] items = Assert.IsType<object[]>(all.Data["items"]);
                Assert.Equal(6, items.Length);
                Dictionary<string, object> first = Item(items[0]);
                Assert.Equal(pinned.Uuid, first["id"]);
                Assert.Equal("pinned report " + Marker, first["preview"]);
                Assert.Equal(true, first["pinned"]);
                Assert.Equal("2026-11-17T10:05:30Z", first["copiedUtc"]);
                // previews only: no item carries the whole text, a source app or a source window title.
                foreach (object entry in items)
                {
                    Assert.False(Item(entry).ContainsKey("text"));
                    Assert.False(Item(entry).ContainsKey("sourceApp"));
                    Assert.False(Item(entry).ContainsKey("sourceTitle"));
                }

                Dictionary<string, object> lastByDate = Item(items[1]);
                Assert.Equal("copy at 09 " + Marker, lastByDate["preview"]);

                AppCommandResponse queried = List(service, "--query", "pinned report").Result;
                Assert.Equal(1, (int)queried.Data!["total"]);
                AppCommandResponse pinnedOnly = List(service, "--pinned", "--limit", "4").Result;
                Assert.Single(Assert.IsType<object[]>(pinnedOnly.Data!["items"]));

                Assert.Equal(AppCommandExit.Usage, List(service, "--query", "re").Result.ExitCode);
                Assert.Equal(AppCommandExit.Usage, List(service, "--limit", "51").Result.ExitCode);
                Assert.Equal(AppCommandExit.Usage, List(service, "--limit", "0").Result.ExitCode);
                AppCommandResponse limited = List(service, "--limit", "2").Result;
                Assert.Equal(2, Assert.IsType<object[]>(limited.Data!["items"]).Length);
                Assert.Equal(6, (int)limited.Data["total"]);
                Assert.Equal(false, limited.Data["truncated"]);
            });
        }

        [Fact]
        public void AListCutByTheAnswerBoundSaysTruncated()
        {
            WithService((service, dir) =>
            {
                for (int minute = 1; minute <= 60; minute++)
                    Seed(service, new string('й', 900) + " " + minute, minute % 60);

                AppCommandResponse cut = List(service, "--limit", "50").Result;
                object[] items = Assert.IsType<object[]>(cut.Data!["items"]);
                Assert.True(items.Length < 50, "the bound must have cut the list, got " + items.Length);
                Assert.Equal(60, (int)cut.Data!["total"]);
                Assert.Equal(true, cut.Data["truncated"]);
                Assert.Equal(items.Length, (int)cut.Data["count"]);
            });
        }

        [Fact]
        public void AnUnknownIdIsTargetMissingNamesNothingOfAnyEntry()
        {
            WithService((service, dir) =>
            {
                Seed(service, "the only entry " + Marker, 5);
                string unknown = new string('f', 64);
                AppCommandResponse answer = Put(service, new FakeRestore(), "--id", unknown).Result;

                Assert.Equal(AppCommandReasons.TargetMissing, answer.Reason);
                Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
                Assert.DoesNotContain(Marker, answer.Message);
                Assert.Null(answer.Data);
            });
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("63A0B7C9D4E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2C3D4E5")]
        [InlineData("63A0B7C9D4E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2C3D4E5FF")]
        public void AMalformedIdIsUsage(string id)
        {
            Assert.False(AppCommandsClipboardHistory.IsEntryId(id));
            WithService((service, dir) =>
            {
                Seed(service, "an entry " + Marker, 5);
                AppCommandResponse answer = Put(service, new FakeRestore(), "--id", id).Result;
                Assert.Equal(AppCommandReasons.BadArgument, answer.Reason);
                Assert.Equal(AppCommandExit.Usage, answer.ExitCode);
                Assert.DoesNotContain(Marker, answer.Message);
            });
        }

        [Fact]
        public void PutRestoresTheEntryItFoundAndAnswersTheOutcomeAlone()
        {
            WithService((service, dir) =>
            {
                ClipboardHistoryEntry pinned = Seed(service, "pinned entry " + Marker, 5, pinned: true);
                var restore = new FakeRestore();

                AppCommandResponse answer = Put(service, restore, "--id", pinned.Uuid.ToLowerInvariant()).Result;
                Assert.Equal(AppCommandExit.Done, answer.ExitCode);
                Assert.Same(pinned, restore.Restored);
                Assert.Null(answer.Data);   // the row returns nothing: the caller named it and can list it
                Assert.Equal(1, service.Entries.Count(e => e.IsPinned));   // a pinned entry keeps its pin

                restore.Result = false;
                AppCommandResponse failed = Put(service, restore, "--id", pinned.Uuid).Result;
                Assert.Equal(AppCommandReasons.ClipboardFailed, failed.Reason);
                Assert.Equal(AppCommandExit.Failed, failed.ExitCode);
                Assert.Equal(pinned, service.Entries.Single(e => e.IsPinned));   // the failed restore changed nothing
            });
        }

        [Fact]
        public void PauseSetsTheOneStateAndAnswersIt()
        {
            var host = new FakeHistoryHost();
            AppCommandResponse paused = AppCommandsClipboardHistory.PauseHistory(
                Inv("clipboard-history", "pause", "--paused", "true"), host, T);
            Assert.Equal(AppCommandExit.Done, paused.ExitCode);
            Assert.Equal(new[] { true }, host.Set);
            Assert.Equal(true, paused.Data!["paused"]);

            AppCommandResponse resumed = AppCommandsClipboardHistory.PauseHistory(
                Inv("clipboard-history", "pause", "--paused", "false"), host, T);
            Assert.Equal(new[] { true, false }, host.Set);
            Assert.Equal(false, resumed.Data!["paused"]);
        }

        // ------------------------------------------------------------------ the send options

        [Fact]
        public void SendWithoutTheOptionsIsTheWaveOneVerb()
        {
            SendSource source = SourceOf(Array.Empty<string>()).Result;
            Assert.Null(source.Refusal);
            Assert.Null(source.Payload);
            Assert.True(source.Wave1);
        }

        [Fact]
        public void SendWithBothOptionsIsUsage()
        {
            AppCommandResponse answer = Send("--file", @"C:\work\a.txt", "--text", "hello").Result;
            Assert.Equal(AppCommandReasons.BadArgument, answer.Reason);
            Assert.Equal(AppCommandExit.Usage, answer.ExitCode);
        }

        [Fact]
        public void SendWithAMissingFileIsTargetMissingAndNamesNoPath()
        {
            _stat = new ExchangeSourceStat { Exists = false };
            AppCommandResponse answer = Send("--file", @"C:\work\a.txt").Result;
            Assert.Equal(AppCommandReasons.TargetMissing, answer.Reason);
            Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
            Assert.DoesNotContain(@"a.txt", answer.Message);
        }

        [Fact]
        public void SendWithAnEmptyFileHasNothingToSend()
        {
            _stat = new ExchangeSourceStat { Exists = true, Length = 0 };
            AppCommandResponse answer = Send("--file", @"C:\work\a.txt").Result;
            Assert.Equal(AppCommandReasons.TargetMissing, answer.Reason);
            Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
        }

        [Fact]
        public void SendWithAFileOverTheCapNamesTheCapAndNeverThePath()
        {
            _stat = new ExchangeSourceStat { Exists = true, Length = ExchangeClipboardService.DefaultClipboardBytes + 1 };
            AppCommandResponse answer = Send("--file", @"C:\work\huge.bin").Result;
            Assert.Equal(AppCommandReasons.TooLarge, answer.Reason);
            Assert.Equal(AppCommandExit.Usage, answer.ExitCode);
            Assert.Contains(ExchangeClipboardService.DefaultClipboardBytes.ToString(), answer.Message);
            Assert.DoesNotContain("huge.bin", answer.Message);
        }

        [Fact]
        public void ASilentShareInFileIsNetworkAndASilentLocalPathIsWriteFailed()
        {
            // The probe is held shut, so it never answers within the budget - exactly a share that does not respond.
            // (A budget of zero is not that: a probe that finished before the zero-length wait was looked at won
            // the race, and the answer was "does not exist" - the test failed one run in three.)
            var gate = new ManualResetEvent(false);
            AppCommandResponse share;
            try
            {
                share = AppCommandsClipboardExchange.ResolveSendSource(
                    Inv("clipboard", "send", "--file", @"\\dead\share\a.txt"), @"C:\work", TimeSpan.FromMilliseconds(200),
                    stat => { gate.WaitOne(); return _stat; },
                    path => path.StartsWith(@"\\", StringComparison.Ordinal), T, CancellationToken.None).Result.Refusal!;
            }
            finally { gate.Set(); }
            Assert.Equal(AppCommandReasons.Network, share.Reason);
            Assert.Equal(AppCommandExit.Network, share.ExitCode);
            Assert.DoesNotContain(@"dead", share.Message);

            // A probe that answers nothing at all (null) is the same for a local path whatever the clock does.
            AppCommandResponse local = AppCommandsClipboardExchange.ResolveSendSource(
                Inv("clipboard", "send", "--file", @"C:\frozen\a.txt"), @"C:\work", TimeSpan.Zero,
                stat => null, path => false, T, CancellationToken.None).Result.Refusal!;
            Assert.Equal(AppCommandReasons.WriteFailed, local.Reason);
            Assert.Equal(AppCommandExit.Failed, local.ExitCode);
        }

        [Fact]
        public void SendFileBuildsTheModulesOwnPayload()
        {
            var stamp = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            _stat = new ExchangeSourceStat { Exists = true, Length = 1234, LastWriteUtc = stamp };
            SendSource source = SourceOf(new[] { "--file", "sub\\name.txt" }).Result;
            ExchangeClipboardPayload payload = source.Payload!;
            Assert.Equal(ExchangeClipboardPayload.File, payload.Kind);
            Assert.Equal(1234, payload.ClearSize);
            Assert.Equal(@"C:\work\sub\name.txt", payload.FilePath);
            Assert.Equal("name.txt", payload.Name);
            Assert.Equal("text/plain", payload.MimeType);
            Assert.Equal(stamp.Ticks, payload.FileLastWriteUtcTicks);
        }

        [Fact]
        public void SendTextIsNormalizedUtf8WithASizeAndAnEmptyTextIsUsage()
        {
            SendSource source = SourceOf(new[] { "--text", "line one\r\nline two" }).Result;
            ExchangeClipboardPayload payload = source.Payload!;
            Assert.Equal(ExchangeClipboardPayload.Text, payload.Kind);
            Assert.Equal("line one\nline two", Encoding.UTF8.GetString(payload.Bytes!));
            Assert.Equal(payload.Bytes!.LongLength, payload.ClearSize);

            AppCommandResponse empty = Send("--text", "").Result;
            Assert.Equal(AppCommandExit.Usage, empty.ExitCode);
        }

        [Fact]
        public void AnAcceptedAnswerMayCarryData()
        {
            var data = new Dictionary<string, object> { ["kind"] = "file", ["sizeBytes"] = 1234L };
            AppCommandResponse answer = AppCommandResponse.Accepted("accepted", data);
            Assert.Equal("accepted", answer.Status);
            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Assert.Equal(1234L, answer.Data!["sizeBytes"]);
            AppCommandResponse bare = AppCommandResponse.Accepted("accepted");
            Assert.Null(bare.Data);
        }

        // ------------------------------------------------------------------ the get --out family

        private static ScreenshotSaverCommandTests.CommandFs Fs() => new ScreenshotSaverCommandTests.CommandFs();

        /// <summary>The plan for one target, over the fake file system; refused shapes come back as the refusal itself.</summary>
        private static (OutputPlan? plan, AppCommandResponse? refusal) PlanOf(ScreenshotSaverCommandTests.CommandFs fs, string target,
            bool overwrite = false, string workingDir = @"C:\work", TimeSpan? budget = null, Func<string, bool>? isRemote = null)
            => AppCommandsClipboardExchange.ResolveOut(target, workingDir, overwrite, fs, budget ?? TimeSpan.FromSeconds(10),
                isRemote ?? (path => path.StartsWith(@"\\", StringComparison.Ordinal)), T, CancellationToken.None).Result;

        private static AppCommandResponse ReceiveInto(ScreenshotSaverCommandTests.CommandFs fs, OutputPlan plan,
            ExchangeReceivedValue value, ExchangeFileCopy? copy = null)
        {
            ReceivedWriteOutcome outcome = AppCommandsClipboardExchange.WriteReceivedValue(value, plan, fs,
                copy ?? ((from, to) => { }), CancellationToken.None);
            return AppCommandsClipboardExchange.OutcomeAnswer(outcome, plan, value, T);
        }

        /// <summary>A received text as the service builds it: the exchange client always sets <c>ClearSize</c> beside the bytes.</summary>
        private static ExchangeReceivedValue TextValue(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return new ExchangeReceivedValue { Kind = ExchangeClipboardPayload.Text, Bytes = bytes, ClearSize = bytes.LongLength };
        }

        [Fact]
        public void OutIsResolvedExactlyAsGivenAndTheAnswerNamesWhereItLanded()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            (OutputPlan? plan, AppCommandResponse? refusal) = PlanOf(fs, @"out.txt");
            Assert.Null(refusal);
            Assert.Equal(@"C:\work\out.txt", plan!.Path);

            AppCommandResponse answer = ReceiveInto(fs, plan, TextValue("received " + Marker));
            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Assert.Equal(@"C:\work\out.txt", answer.Data!["savedTo"]);
            Assert.Equal(ExchangeClipboardPayload.Text, answer.Data["kind"]);
            Assert.Equal(Encoding.UTF8.GetBytes("received " + Marker).LongLength, answer.Data["sizeBytes"]);
            Assert.Equal("received " + Marker, Encoding.UTF8.GetString(fs.Read(@"C:\work\out.txt")!));
        }

        [Fact]
        public void OutAFolderThatDoesNotExistIsTargetMissingAndNoFolderIsCreated()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            (OutputPlan? plan, AppCommandResponse? refusal) = PlanOf(fs, @"C:\nope\out.txt");
            Assert.Null(plan);
            Assert.Equal(AppCommandReasons.TargetMissing, refusal!.Reason);
            Assert.Equal(AppCommandExit.NotReady, refusal.ExitCode);
            Assert.Empty(fs.Created);
            Assert.Empty(fs.Files);

            Assert.Null(PlanOf(fs, @"C:\work\out.txt", overwrite: true).refusal);   // and a name Windows accepts still plans
        }

        [Fact]
        public void OutAFolderShapeIsUsageAndADirectoryAtTheTargetIsExists()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            AppCommandResponse shaped = PlanOf(fs, @"C:\work\").refusal!;
            Assert.Equal(AppCommandReasons.BadArgument, shaped.Reason);
            Assert.Equal(AppCommandExit.Usage, shaped.ExitCode);

            AppCommandResponse badName = PlanOf(fs, @"C:\work\out .txt").refusal!;
            Assert.Equal(AppCommandReasons.BadArgument, badName.Reason);
            Assert.Equal(AppCommandExit.Usage, badName.ExitCode);
            Assert.DoesNotContain("out .txt", badName.Message);

            fs.Folders.Add(@"C:\work\occupied.txt");
            AppCommandResponse occupied = PlanOf(fs, @"C:\work\occupied.txt").refusal!;
            Assert.Equal(AppCommandReasons.Exists, occupied.Reason);
            Assert.Equal(AppCommandExit.NotReady, occupied.ExitCode);
        }

        [Fact]
        public void OutAnExistingFileIsRefusedWithoutOverwriteAndReplacedWithIt()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            (OutputPlan? plan, AppCommandResponse? refusal) = PlanOf(fs, @"C:\work\out.txt");
            fs.Files[@"C:\work\out.txt"] = new byte[] { 9 };

            // a file in the way without --overwrite: the target survives untouched, the temporary is removed
            ReceivedWriteOutcome outcome = AppCommandsClipboardExchange.WriteReceivedValue(TextValue("received"), plan!,
                fs, (from, to) => { }, CancellationToken.None);
            Assert.Equal(ReceivedWriteOutcome.Exists, outcome);
            Assert.Equal(new byte[] { 9 }, fs.Files[@"C:\work\out.txt"]);
            Assert.Equal(1, fs.Log.Count(l => l.StartsWith("write", StringComparison.Ordinal)));   // the temporary was written ..
            // .. and taken away again: nothing but the pre-existing file is left in the folder

            (OutputPlan? overwritePlan, AppCommandResponse? none) = PlanOf(fs, @"C:\work\out.txt", overwrite: true);
            Assert.Null(none);
            Assert.True(overwritePlan!.Overwrite);
            AppCommandResponse replaced = ReceiveInto(fs, overwritePlan!, TextValue("received"));
            Assert.Equal(AppCommandExit.Done, replaced.ExitCode);
            Assert.Equal("received", Encoding.UTF8.GetString(fs.Read(@"C:\work\out.txt")!));
        }

        [Fact]
        public void OutTheWriteIsATemporaryInTheTargetsFolderThenARename()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            (OutputPlan? plan, _) = PlanOf(fs, @"C:\work\out.txt");
            AppCommandResponse answer = ReceiveInto(fs, plan!, TextValue("received " + Marker));

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Assert.Equal(1, fs.Log.Count(l => l.StartsWith("write", StringComparison.Ordinal)));
            Assert.Equal(1, fs.Log.Count(l => l.StartsWith("move", StringComparison.Ordinal)));
            Assert.Contains(".tmp", fs.Log[0], StringComparison.Ordinal);   // the temporary sits in the target's folder
            Assert.Equal(@"move C:\work\out.txt", fs.Log[1]);   // the rename is the only step that touches the final name
            Assert.Equal(0, fs.Files.Count(p => Path.GetFileName(p.Key).EndsWith(".tmp", StringComparison.Ordinal)));
        }

        [Fact]
        public void OutADiskFullIsExitThirteen()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            (OutputPlan? plan, _) = PlanOf(fs, @"C:\work\out.txt");
            fs.WriteFailure = new IOException("full", 112);
            AppCommandResponse answer = ReceiveInto(fs, plan!, TextValue("received"));
            Assert.Equal(AppCommandReasons.DiskFull, answer.Reason);
            Assert.Equal(AppCommandExit.NoSpace, answer.ExitCode);
            Assert.False(fs.Files.ContainsKey(@"C:\work\out.txt"));
        }

        [Fact]
        public void ASilentShareInOutIsNetworkAndASilentLocalFolderIsWriteFailed()
        {
            // The folder probe is held shut, so it never answers within the budget - exactly a share that does not
            // respond. (A budget of zero is not that: a probe that finished before the zero-length wait was looked at
            // won the race and the plan came back instead of the refusal - the test failed now and then.)
            var gate = new ManualResetEvent(false);
            AppCommandResponse share, local;
            try
            {
                ScreenshotSaverCommandTests.CommandFs fs = Fs();
                fs.ProbeGate = gate;
                TimeSpan budget = TimeSpan.FromMilliseconds(200);
                share = PlanOf(fs, @"\\dead\share\out.txt", budget: budget).refusal!;
                local = PlanOf(fs, @"C:\work\out.txt", budget: budget, isRemote: path => false).refusal!;
            }
            finally { gate.Set(); }
            Assert.Equal(AppCommandReasons.Network, share.Reason);
            Assert.Equal(AppCommandExit.Network, share.ExitCode);
            Assert.DoesNotContain(@"dead", share.Message);

            Assert.Equal(AppCommandReasons.WriteFailed, local.Reason);
            Assert.Equal(AppCommandExit.Failed, local.ExitCode);
        }

        [Theory]
        [InlineData(ExchangeCodes.ClipboardEmpty, AppCommandReasons.TargetMissing, AppCommandExit.NotReady)]
        [InlineData(ExchangeCodes.KeyLost, AppCommandReasons.WriteFailed, AppCommandExit.Failed)]
        [InlineData(ExchangeCodes.Unreachable, AppCommandReasons.WriteFailed, AppCommandExit.Failed)]
        public void AFetchFailureAnswersByItsKind(string code, string reason, int exit)
        {
            AppCommandResponse answer = AppCommandsClipboardExchange.FetchFailureAnswer(code, T);
            Assert.Equal(reason, answer.Reason);
            Assert.Equal(exit, answer.ExitCode);
        }

        [Fact]
        public void AReceivedFileKindIsCopiedThroughTheCopySeamAndLandsWhole()
        {
            ScreenshotSaverCommandTests.CommandFs fs = Fs();
            string received = Path.Combine(_root, "received", "a.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(received)!);
            File.WriteAllBytes(received, new byte[] { 1, 2, 3, 4 });
            (OutputPlan? plan, _) = PlanOf(fs, @"C:\work\out.bin");

            string? copiedFrom = null;
            ExchangeReceivedValue value = new ExchangeReceivedValue
            {
                Kind = ExchangeClipboardPayload.File, FilePath = received, ClearSize = 4, Bytes = null,
            };
            AppCommandResponse answer = ReceiveInto(fs, plan!, value,
                (from, to) => { copiedFrom = from; fs.WriteAllBytes(to, File.ReadAllBytes(from)); });

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Assert.Equal(received, copiedFrom);
            Assert.Equal(ExchangeClipboardPayload.File, answer.Data!["kind"]);
            Assert.Equal(4L, answer.Data["sizeBytes"]);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, fs.Read(@"C:\work\out.bin"));
        }

        // ------------------------------------------------------------------ the log discipline

        [Fact]
        public void TheLogLineCarriesTheCountAndNoCharacterOfAnEntry()
        {
            var data = new Dictionary<string, object>
            {
                ["count"] = 2,
                ["total"] = 7,
                ["truncated"] = false,
                ["items"] = Array.Empty<object>(),
            };
            AppCommandResponse outcome = AppCommandResponse.Done("History entries listed: 2 of 7.", data);
            string line = AppCommandsLog.Line(new DateTime(2026, 11, 17, 12, 0, 0), "clipboard-history list", "agent", 4321, outcome);

            Assert.Contains("clipboard-history list", line);
            Assert.Contains("count=2", line);
            Assert.DoesNotContain(Marker, line);
            Assert.DoesNotContain("sourceTitle", line);
        }
    }
}
