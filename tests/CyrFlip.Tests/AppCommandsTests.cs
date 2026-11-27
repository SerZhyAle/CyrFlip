using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The command line of APP-COMMANDS 0.2 (ticket S0107): the manifest, the parser, the wire, the gate of rule 13 and
    /// the request-response profile on a private pipe. Nothing here starts CyrFlip or touches its real pipe; the cases
    /// that need a running tray copy, a hung one or an elevated one (C3, C5, C6, C9, C10 start) are
    /// <c>tools/uitest/Test-AppCommands.ps1</c>'s and the release's.
    /// </summary>
    public class AppCommandsTests
    {
        private static readonly string Repo = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

        private static AppCommandManifest Manifest => AppCommandManifest.Shipped;

        /// <summary>A command line written as its arguments joined by "|" (an empty string is no arguments).</summary>
        private static string[] Args(string line) => line.Length == 0 ? new string[0] : line.Split('|');

        // ------------------------------------------------------------------ the manifest (C1, C13)

        [Fact]
        public void TheEmbeddedManifestIsTheFileByteForByte()
        {
            byte[] file = File.ReadAllBytes(Path.Combine(Repo, "docs", "cli-commands.json"));
            Assert.Equal(file, Manifest.Raw);
        }

        [Fact]
        public void EveryCommandDeclaresWhatTheContractAsks()
        {
            var risks = new[] { "safe", "reads-user-data", "writes-user-data", "leaves-machine" };
            var cold = new[] { "start", "oneshot", "refuse" };
            Assert.Equal("1.1", Manifest.CliVersion); // 1.1: the remote member of REMOTE-CONTROL rule 7 (CYRFLP-0140), a MINOR
            foreach (AppCommandSpec c in Manifest.Commands)
            {
                Assert.Contains(c.Risk, risks);
                Assert.Contains(c.ColdStart, cold);
                Assert.InRange(c.TimeoutMs, 1, 600000);
                Assert.Contains(c.Wait, new[] { "done", "accepted" });
            }
            foreach (string baseVerb in new[] { "commands", "version", "status", "show", "quit" })
                Assert.True(Manifest.Find(baseVerb)!.IsSafe, baseVerb + " is a safe base verb");
            Assert.Equal("never", Manifest.Find("commands")!.Forward);
            Assert.Equal("never", Manifest.Find("version")!.Forward);
            Assert.Equal("refuse", Manifest.Find("status")!.ColdStart);
        }

        [Fact]
        public void EverySpellingTheProductShipsIsAnAliasInTheManifest()
        {
            string text = File.ReadAllText(Path.Combine(Repo, "docs", "cli-commands.json"));
            foreach (string spelling in new[] { "/launcher-settings", "/exit", "/clipboard-send", "/clipboard-get", "/launcher-run:{guid}" })
                Assert.Contains("\"spelling\": \"" + spelling + "\"", text);
            foreach (string verb in new[] { "show", "quit", "clipboard send", "clipboard get", "scenario run" })
                Assert.True(Manifest.Find(verb)!.HasLegacyAlias, verb + " carries its legacy alias");
        }

        [Fact]
        public void NoVerbOfThisCommandSetTakesASecret()
        {
            // C12 holds vacuously: no option is a secret, a password or a secret file.
            foreach (AppCommandSpec c in Manifest.Commands)
                foreach (AppCommandArg a in c.Args)
                    Assert.DoesNotMatch("(?i)secret|password|token", a.Name);
        }

        [Fact]
        public void TheReadableListNamesEveryVerb()
        {
            string list = Manifest.ReadableList();
            foreach (AppCommandSpec c in Manifest.Commands) Assert.Contains(c.Name, list);
        }

        // ------------------------------------------------------------------ the parser (C4, C13)

        [Fact]
        public void APlainStartAndTheShippedSpellingsKeepTheirOldReading()
        {
            Assert.Equal(AppCommandLineKind.Plain, AppCommandLine.Parse(new string[0]).Kind);
            foreach (string token in new[] { "/exit", "/launcher-settings", "/clipboard-send", "/clipboard-get" })
            {
                AppCommandLineResult r = AppCommandLine.Parse(new[] { token.ToUpperInvariant() });
                Assert.Equal(AppCommandLineKind.Legacy, r.Kind);
                Assert.Equal(token, r.LegacyToken);
            }
            Guid id = Guid.NewGuid();
            Assert.Equal(LauncherIpc.RunPrefix + id.ToString("D"), AppCommandLine.Parse(new[] { "/launcher-run:" + id.ToString("N") }).LegacyToken);
        }

        [Theory]
        [InlineData("status")]
        [InlineData("STATUS")]
        [InlineData("Clipboard|SEND")]
        [InlineData("show|--page|quick-notes")]
        [InlineData("show|--page=Quick-Notes")]
        [InlineData("status|--json|--result|out.json")]
        [InlineData("status|--json=false")]
        // A bool's value may be the next argument, as docs/CLI.md spells it ("--system <true|false>"); a bare bool is true.
        [InlineData("status|--json|true")]
        [InlineData("status|--json|FALSE|--result|out.json")]
        [InlineData("keepawake|set|--system|true|--screen|false")]
        public void AKnownVerbWithGoodOptionsIsACommand(string line)
        {
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(Args(line)).Kind);
        }

        [Theory]
        [InlineData(AppCommandReasons.UnknownVerb, "launch-rockets")]
        [InlineData(AppCommandReasons.UnknownVerb, "/unknown")]
        [InlineData(AppCommandReasons.BadArgument, "status|--verbose")]
        [InlineData(AppCommandReasons.BadArgument, "status|--result")]
        [InlineData(AppCommandReasons.BadArgument, "status|extra")]
        [InlineData(AppCommandReasons.BadArgument, "status|--json=maybe")]
        [InlineData(AppCommandReasons.BadArgument, "status|--json|yes")]          // the word after a bool is its value, and it is neither true nor false
        [InlineData(AppCommandReasons.BadArgument, "status|--json|true|extra")]   // one value only; the next word is a stray argument
        [InlineData(AppCommandReasons.BadArgument, "status|--json|--json")]
        [InlineData(AppCommandReasons.BadArgument, "show|--page|nowhere")]
        [InlineData(AppCommandReasons.BadArgument, "scenario|run")]
        [InlineData(AppCommandReasons.BadArgument, "scenario|run|--id|not-a-guid")]
        [InlineData(AppCommandReasons.BadArgument, "scenario|run|--id|3f2504e0-4f89-11d3-9a0c-0305e82c3301|--name|x")]
        [InlineData(AppCommandReasons.BadArgument, "status|--|--json")]
        public void AnythingElseIsRefusedWithItsReason(string reason, string line)
        {
            AppCommandLineResult r = AppCommandLine.Parse(Args(line));
            Assert.Equal(AppCommandLineKind.Refused, r.Kind);
            Assert.Equal(reason, r.Reason);
            Assert.False(string.IsNullOrEmpty(r.Message));
        }

        [Fact]
        public void AnOversizedArgumentOrRequestIsRefusedNotTruncated()
        {
            string big = new string('a', AppCommandLine.MaxArgumentBytes + 1);
            Assert.Equal(AppCommandReasons.TooLarge, AppCommandLine.Parse(new[] { "scenario", "run", "--name", big }).Reason);
            string half = new string('a', AppCommandLine.MaxArgumentBytes - 10);
            Assert.Equal(AppCommandReasons.TooLarge, AppCommandLine.Parse(new[] { "scenario", "run", "--name", half, "--link", half, "--result", half }).Reason);
        }

        [Fact]
        public void ValuesKeepTheirCaseAndAGuidIsNormalized()
        {
            Guid id = Guid.NewGuid();
            AppCommandInvocation inv = AppCommandLine.Parse(new[] { "scenario", "run", "--ID=" + id.ToString("N").ToUpperInvariant() }).Invocation!;
            Assert.Equal(id.ToString("D"), inv.Option("id"));
            inv = AppCommandLine.Parse(new[] { "scenario", "run", "--name", "My Backup" }).Invocation!;
            Assert.Equal("My Backup", inv.Option("name"));
            Assert.Equal(new[] { "--name", "My Backup" }, inv.Args);
        }

        // ------------------------------------------------------------------ the wire (rule 7, item I)

        [Fact]
        public void ARequestAndAResponseRoundTrip()
        {
            var request = new AppCommandRequest
            {
                Command = "scenario run", Args = new[] { "--name", "Schl\u00fcssel \"x\"" }, WorkingDir = @"C:\work",
                RequestId = Guid.NewGuid().ToString("D"),
            };
            string line = AppCommandProtocol.Serialize(request);
            Assert.DoesNotContain("\n", line);
            AppCommandRequest back = AppCommandProtocol.TryParseRequest(line, out _, out _)!;
            Assert.Equal(request.Command, back.Command);
            Assert.Equal(request.Args, back.Args);
            Assert.Equal(request.RequestId, back.RequestId);
            Assert.False(back.Legacy);

            var response = AppCommandResponse.Refused(AppCommandReasons.SwitchOff, AppCommandExit.Permission, "no");
            response.RequestId = request.RequestId;
            AppCommandResponse parsed = AppCommandProtocol.TryParseResponse(AppCommandProtocol.Serialize(response))!;
            Assert.Equal("refused", parsed.Status);
            Assert.Equal(12, parsed.ExitCode);
            Assert.Equal(AppCommandReasons.SwitchOff, parsed.Reason);
            Assert.Equal(request.RequestId, parsed.RequestId);
        }

        [Theory]
        [InlineData("not json", AppCommandReasons.BadArgument)]
        [InlineData("{\"schemaVersion\":1,\"command\":\"status\"}", AppCommandReasons.BadArgument)]
        [InlineData("{\"schemaVersion\":2,\"command\":\"status\",\"requestId\":\"r\"}", AppCommandReasons.Schema)]
        [InlineData("{\"schemaVersion\":1,\"command\":\"status\",\"requestId\":\"r\",\"args\":[1]}", AppCommandReasons.BadArgument)]
        public void AnUnreadableRequestIsNamedNotThrown(string line, string reason)
        {
            Assert.Null(AppCommandProtocol.TryParseRequest(line, out string why, out _));
            Assert.Equal(reason, why);
        }

        [Fact]
        public void TheResultFileHoldsTheDataWholeAndAFailedWriteSaysSo()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CyrFlipCmd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string target = Path.Combine(dir, "r.json");
                File.WriteAllText(target, "old");
                var response = AppCommandResponse.Done("ok", new Dictionary<string, object> { ["name"] = "Backup" });
                Assert.True(AppCommandRunner.WriteResult(target, response));
                Assert.Equal("{\"name\":\"Backup\"}", File.ReadAllText(target));
                Assert.Single(Directory.GetFiles(dir)); // no temporary file left beside it
                Assert.False(AppCommandRunner.WriteResult(Path.Combine(dir, "missing", "r.json"), response));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TheLogLineCarriesNoArgument()
        {
            var outcome = AppCommandResponse.Done("Started \"Secret backup\".", new Dictionary<string, object> { ["name"] = "Secret backup" });
            string line = AppCommandsLog.Line(DateTime.Now, "scenario run", "cli\nforged", 42, outcome);
            Assert.DoesNotContain("Secret", line);
            Assert.Contains("scenario run origin=other pid=42 status=done exit=0", line);
            Assert.Contains(AppCommandsLog.FileName, SupportBundle.LogFiles);
        }

        // ------------------------------------------------------------------ the gate (rule 13, item F, C8, C14)

        private static string T(string en) => en;

        [Fact]
        public void TheSettingGatesEveryClassButSafeAndALegacySpellingIsGatedByItsModuleOnly()
        {
            AppCommandSpec send = Manifest.Find("clipboard send")!;
            AppCommandResponse refusal = AppCommandVerbs.RiskGate(send, settingOn: false, legacy: false, T)!;
            Assert.Equal(AppCommandReasons.SwitchOff, refusal.Reason);
            Assert.Equal(12, refusal.ExitCode);
            Assert.Contains(AppCommandVerbs.SettingCaption, refusal.Message);
            Assert.Null(AppCommandVerbs.RiskGate(send, settingOn: true, legacy: false, T));
            Assert.Null(AppCommandVerbs.RiskGate(send, settingOn: false, legacy: true, T));
            Assert.Null(AppCommandVerbs.RiskGate(Manifest.Find("status")!, settingOn: false, legacy: false, T));
        }

        [Fact]
        public void TheSettingIsOffAndNeverTravelsInASettingsFile()
        {
            Assert.False(new AppConfig().EnableExternalCommands);
            Assert.True(SettingsFileInventory.Excluded.ContainsKey("EnableExternalCommands"));
            Assert.DoesNotContain(SettingsFileInventory.Exported, e => e.Key == "EnableExternalCommands");
        }

        [Fact]
        public void AScenarioIsPickedOnlyWhenACommandCanRunItAlone()
        {
            var plain = new LauncherScenario { Name = "Backup", Id = Guid.NewGuid() };
            var twin1 = new LauncherScenario { Name = "Twin", Id = Guid.NewGuid() };
            var twin2 = new LauncherScenario { Name = "twin", Id = Guid.NewGuid() };
            var admin = new LauncherScenario { Name = "Admin", Id = Guid.NewGuid(), RunAsAdmin = true };
            var video = new LauncherScenario { Name = "Video", Id = Guid.NewGuid(), Type = LauncherScenarioType.YtDlp };
            var all = new List<LauncherScenario> { plain, twin1, twin2, admin, video };
            AppCommandResponse? Pick(params string[] options)
                => AppCommandVerbs.PickScenario(all, AppCommandLine.Parse(new[] { "scenario", "run" }.Concat(options).ToArray()).Invocation!, T, out _);

            Assert.Null(Pick("--name", "BACKUP"));
            Assert.Null(Pick("--id", plain.Id.ToString("N")));
            Assert.Equal(AppCommandReasons.TargetMissing, Pick("--name", "Nope")!.Reason);
            Assert.Equal(AppCommandReasons.TargetMissing, Pick("--name", "Twin")!.Reason);
            Assert.Equal(AppCommandReasons.NeedsPerson, Pick("--name", "Admin")!.Reason);
            Assert.Equal(AppCommandReasons.BadArgument, Pick("--name", "Video")!.Reason);
            Assert.Null(Pick("--name", "Video", "--link", "https://example.com/v"));
        }

        [Fact]
        public void EveryShowPageNamesASettingsPage()
        {
            foreach (string page in Manifest.Find("show")!.Arg("page")!.Values)
                Assert.NotNull(AppCommandVerbs.PageOf(page));
            Assert.Null(AppCommandVerbs.PageOf(null));
        }

        // ------------------------------------------------------------------ the profile on a private pipe

        [Fact]
        public void ARequestIsAnsweredOnTheSameConnectionAndATokenStillArrives()
        {
            string pipe = "CyrFlipTests_" + Guid.NewGuid().ToString("N");
            string? token = null;
            using var gotToken = new ManualResetEventSlim();
            using var ipc = new LauncherIpc(cmd => { token = cmd; gotToken.Set(); }, pipe,
                onRequest: (request, pid) => AppCommandResponse.Done("ok " + request.Command));
            ipc.Start();

            AppCommandRequest req = AppCommandRunner.NewRequest("status", new string[0]);
            AppCommandDelivery delivery = AppCommandDelivery.NoListener;
            AppCommandResponse? response = null;
            for (int attempt = 0; attempt < 20 && delivery == AppCommandDelivery.NoListener; attempt++)
                delivery = LauncherIpc.SendRequest(req, 3000, out response, deliveryMs: 500, pipeName: pipe);
            Assert.Equal(AppCommandDelivery.Answered, delivery);
            Assert.Equal(req.RequestId, response!.RequestId);
            Assert.Equal("ok status", response.Message);

            // An older second process: the one-line token, write-only, no answer (item K paragraph 4).
            Assert.True(LauncherIpc.TrySend(LauncherIpc.SettingsCommand, 2000, pipe));
            Assert.True(gotToken.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(LauncherIpc.SettingsCommand, token);
        }

        [Fact]
        public void AnUnreadableLineIsAnsweredWithAnEmptyIdAndTheListenerGoesOn()
        {
            string pipe = "CyrFlipTests_" + Guid.NewGuid().ToString("N");
            using var ipc = new LauncherIpc(_ => { }, pipe, onRequest: (r, pid) => AppCommandResponse.Done("ok"));
            ipc.Start();

            string? line = null;
            for (int attempt = 0; attempt < 20 && line == null; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
                    client.Connect(500);
                    byte[] bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"command\":\"status\"\n");
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                    using var reader = new StreamReader(client, Encoding.UTF8);
                    line = reader.ReadLine();
                }
                catch (TimeoutException) { Thread.Sleep(100); }
            }
            AppCommandResponse answer = AppCommandProtocol.TryParseResponse(line!)!;
            Assert.Equal("", answer.RequestId);
            Assert.Equal(10, answer.ExitCode);

            Assert.Equal(AppCommandDelivery.Answered,
                LauncherIpc.SendRequest(AppCommandRunner.NewRequest("status", new string[0]), 3000, out _, deliveryMs: 2000, pipeName: pipe));
        }

        [Fact]
        public void AOneWayPipeOfAnOlderCopyIsRecognizedAndHasALegacyForm()
        {
            string pipe = "CyrFlipTests_" + Guid.NewGuid().ToString("N");
            using var older = new NamedPipeServerStream(pipe, PipeDirection.In, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            AppCommandDelivery delivery = LauncherIpc.SendRequest(AppCommandRunner.NewRequest("show", new string[0]), 2000, out _,
                deliveryMs: 1000, pipeName: pipe);
            Assert.Equal(AppCommandDelivery.OlderCopy, delivery);

            AppCommandInvocation show = AppCommandLine.Parse(new[] { "show" }).Invocation!;
            Assert.Equal(LauncherIpc.SettingsCommand, AppCommandVerbs.LegacyFormOf(show));
            Assert.Null(AppCommandVerbs.LegacyFormOf(AppCommandLine.Parse(new[] { "status" }).Invocation!));
        }

        [Fact]
        public void NobodyListeningIsReportedInsideTheDeliveryBound()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            AppCommandDelivery delivery = LauncherIpc.SendRequest(AppCommandRunner.NewRequest("status", new string[0]), 5000, out _,
                deliveryMs: 300, pipeName: "CyrFlipTests_" + Guid.NewGuid().ToString("N"));
            Assert.Equal(AppCommandDelivery.NoListener, delivery);
            Assert.True(clock.ElapsedMilliseconds < 3000, "the connect bound was not honoured: " + clock.ElapsedMilliseconds + " ms");
        }
    }
}
