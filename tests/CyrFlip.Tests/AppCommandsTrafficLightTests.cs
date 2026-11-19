using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// <c>trafficlight status</c> and <c>trafficlight check</c> (ticket S0117): the two handlers over a fake host - a fake
    /// engine and a fake scheduler, so no probe runs and no clock matters - and the label rule that keeps an address out of
    /// every answer. The engine's and the scheduler's own correlation is <see cref="TrafficLightCommandCheckTests"/>; the cases
    /// that need a running tray copy (both verbs against a real board, the module switched off live, no instance) are the
    /// release's and <c>tools/uitest/Test-AppCommands.ps1</c>'s.
    /// </summary>
    public class AppCommandsTrafficLightTests
    {
        private const string Address = "192.168.77.5";
        private const string SecretHost = "secret-host.example.org";

        private sealed class FakeHost : ITrafficLightCommandHost
        {
            public List<TrafficLightTile> Tiles = new List<TrafficLightTile>();
            public readonly List<string> Requested = new List<string>();
            public readonly List<string> Cancelled = new List<string>();
            public int Snapshots;
            /// <summary>What the fake scheduler answers a request with; by default a check that has already finished.</summary>
            public Func<string, TrafficCheckTicket>? Ticketer;

            public List<TrafficLightTile> Snapshot()
            {
                Snapshots++;
                return new List<TrafficLightTile>(Tiles);
            }

            public TrafficCheckTicket RequestCheck(TrafficLightTarget target)
            {
                Requested.Add(target.Id);
                if (Ticketer != null) return Ticketer(target.Id);
                return TrafficCheckTicket.Taken(Task.FromResult(TrafficCheckOutcome.Completed), () => Cancelled.Add(target.Id));
            }
        }

        private static string T(string en) => en;

        private static DateTime At(int minute) => new DateTime(2026, 11, 17, 10, minute, 30, DateTimeKind.Utc);

        private static TrafficLightTile Tile(string id, string label, string kind = "ping", string address = Address,
            TrafficState state = TrafficState.Ok, bool stale = false, DateTime? checkedAt = null, bool enabled = true,
            bool invalid = false, string fingerprint = "")
            => new TrafficLightTile
            {
                Target = new TrafficLightTarget { Id = id, Label = label, Kind = kind, Address = address, Enabled = enabled, Fingerprint = fingerprint },
                State = state, Stale = stale, CheckedAtUtc = checkedAt, InvalidAddress = invalid,
            };

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static AppCommandResponse Status(FakeHost? host, params string[] options)
            => AppCommandsTrafficLight.Status(Inv(new[] { "trafficlight", "status" }.Concat(options).ToArray()), () => host, T);

        private static Task<AppCommandResponse> Check(FakeHost? host, CancellationToken token, params string[] options)
            => AppCommandsTrafficLight.Check(Inv(new[] { "trafficlight", "check" }.Concat(options).ToArray()), () => host, T, token);

        private static Task<AppCommandResponse> Check(FakeHost? host, params string[] options) => Check(host, CancellationToken.None, options);

        private static string Everything(AppCommandResponse response)
            => response.Message + " " + new JavaScriptSerializer().Serialize(response.Data ?? new Dictionary<string, object>());

        private static Dictionary<string, object> TileAt(AppCommandResponse response, int index)
            => (Dictionary<string, object>)((object[])response.Data!["tiles"])[index];

        // ------------------------------------------------------------------ the manifest

        [Fact]
        public void TheManifestDeclaresBothVerbsWithTheFieldsOfRowsC22AndC23()
        {
            AppCommandSpec status = AppCommandManifest.Shipped.Find("trafficlight status")!;
            AppCommandSpec check = AppCommandManifest.Shipped.Find("trafficlight check")!;

            Assert.Equal("reads-user-data", status.Risk);
            Assert.Equal("refuse", status.ColdStart);
            Assert.Equal(5000, status.TimeoutMs);
            Assert.Equal("done", status.Wait);
            Assert.NotNull(status.Arg("target"));
            Assert.False(status.Arg("target")!.Required);

            Assert.Equal("leaves-machine", check.Risk);
            Assert.Equal("refuse", check.ColdStart);
            Assert.Equal(60000, check.TimeoutMs);
            Assert.Equal("done", check.Wait);
            Assert.Equal("bool", check.Arg("all")!.Kind);
            Assert.Single(check.OneOf);
            Assert.Equal(new[] { "target", "all" }, check.OneOf[0].ToArray());
            Assert.False(status.HasLegacyAlias);
            Assert.False(check.HasLegacyAlias);
        }

        [Fact]
        public void TheParserTakesExactlyOneOfTargetAndAllForACheck()
        {
            Assert.Equal(AppCommandLineKind.Refused, AppCommandLine.Parse(new[] { "trafficlight", "check" }).Kind);
            Assert.Equal(AppCommandLineKind.Refused, AppCommandLine.Parse(new[] { "trafficlight", "check", "--target", "X", "--all" }).Kind);
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(new[] { "trafficlight", "check", "--all" }).Kind);
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(new[] { "trafficlight", "check", "--target", "X" }).Kind);
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(new[] { "trafficlight", "status" }).Kind);
        }

        [Theory]
        [InlineData("trafficlight status")]
        [InlineData("trafficlight check")]
        public void WithTheSettingOffBothVerbsAreRefusedAtTheClassGateBeforeAnyHandler(string verb)
        {
            AppCommandSpec spec = AppCommandManifest.Shipped.Find(verb)!;

            AppCommandResponse? off = AppCommandVerbs.RiskGate(spec, settingOn: false, legacy: false, T);
            AppCommandResponse? on = AppCommandVerbs.RiskGate(spec, settingOn: true, legacy: false, T);

            Assert.NotNull(off);
            Assert.Equal(AppCommandReasons.SwitchOff, off!.Reason);
            Assert.Equal(12, off.ExitCode);
            Assert.Null(on);
        }

        // ------------------------------------------------------------------ status

        [Fact]
        public void StatusAnswersTheFiveFieldsOfEveryTileAndNothingOfTheAddress()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server", "ping", Address, TrafficState.Down, stale: true, checkedAt: At(5)));
            host.Tiles.Add(Tile("b", "Website", "http", "https://" + SecretHost + "/status", TrafficState.Ok, checkedAt: At(6)));

            AppCommandResponse response = Status(host);

            Assert.Equal("done", response.Status);
            Assert.Equal(0, response.ExitCode);
            Assert.Equal(2, (int)response.Data!["count"]);
            Assert.False(response.Data.ContainsKey("total"));
            Dictionary<string, object> first = TileAt(response, 0);
            Assert.Equal("Backup server", first["label"]);
            Assert.Equal("ping", first["kind"]);
            Assert.Equal("down", first["state"]);
            Assert.Equal(true, first["stale"]);
            Assert.Equal("2026-11-17T10:05:30Z", first["lastCheckedUtc"]);
            Assert.Equal(5, first.Count);
            Dictionary<string, object> second = TileAt(response, 1);
            Assert.Equal("http", second["kind"]);
            Assert.Equal("ok", second["state"]);
            Assert.Equal(false, second["stale"]);

            string all = Everything(response);
            Assert.DoesNotContain("192.168", all);
            Assert.DoesNotContain(SecretHost, all);
            Assert.False(response.Data.ContainsKey("withheld"));
        }

        [Fact]
        public void StatusStartsNoCheckAndReadsTheSameModelTheBoardDraws()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "One", checkedAt: At(1)));
            host.Tiles.Add(Tile("b", "Two", checkedAt: At(2), stale: true));

            Status(host);
            Status(host, "--target", "One");

            Assert.Empty(host.Requested);
            Assert.Equal(2, host.Snapshots);
        }

        [Fact]
        public void StatusWordsTheLightsTheBoardDrawsApart()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("u", "Never checked", state: TrafficState.Unknown));
            host.Tiles.Add(Tile("o", "Fine", state: TrafficState.Ok, checkedAt: At(1)));
            host.Tiles.Add(Tile("d", "Slow", state: TrafficState.Degraded, checkedAt: At(1)));
            host.Tiles.Add(Tile("x", "Red", state: TrafficState.Down, checkedAt: At(1)));
            host.Tiles.Add(Tile("f", "Parked", state: TrafficState.Down, enabled: false, checkedAt: At(1)));
            host.Tiles.Add(Tile("i", "Typo", state: TrafficState.Unknown, invalid: true));

            AppCommandResponse response = Status(host);

            var words = ((object[])response.Data!["tiles"]).Cast<Dictionary<string, object>>().Select(t => (string)t["state"]).ToArray();
            Assert.Equal(new[] { "unknown", "ok", "degraded", "down", "off", "invalid" }, words);
            Assert.Null(TileAt(response, 0)["lastCheckedUtc"]);
            Assert.True(TileAt(response, 1).ContainsKey("lastCheckedUtc"));
        }

        [Fact]
        public void StatusNarrowsToTheWholeLabelWithoutRegardToCaseAndAMissOrNothingIsStillZero()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", "Backup"));
            host.Tiles.Add(Tile("c", "Backup server"));

            AppCommandResponse exact = Status(host, "--target", "  backup SERVER ");
            AppCommandResponse partial = Status(host, "--target", "Back");
            AppCommandResponse nothing = Status(host, "--target", "No such label");

            Assert.Equal(2, (int)exact.Data!["count"]);
            Assert.Equal("Backup server", TileAt(exact, 0)["label"]);
            Assert.Equal(0, partial.ExitCode);
            Assert.Equal(0, (int)partial.Data!["count"]);
            Assert.Empty((object[])partial.Data["tiles"]);
            Assert.Equal(0, nothing.ExitCode);
            Assert.Equal(0, (int)nothing.Data!["count"]);
            Assert.Equal("No target has this label.", nothing.Message);
        }

        [Fact]
        public void StatusSelectsByLabelAndNeverByIdOrAddress()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("0123456789abcdef", "Backup server", address: Address));

            Assert.Equal(0, (int)Status(host, "--target", "0123456789abcdef").Data!["count"]);
            Assert.Equal(0, (int)Status(host, "--target", Address).Data!["count"]);
            Assert.Equal(1, (int)Status(host, "--target", "Backup server").Data!["count"]);
        }

        [Fact]
        public void StatusWithTheModuleOffIs11ModuleOffAndReadsNothing()
        {
            AppCommandResponse response = Status(null);

            Assert.Equal("module-off", response.Reason);
            Assert.Equal(11, response.ExitCode);
            Assert.Null(response.Data);
        }

        [Fact]
        public void ABlankTargetIsAnUsageErrorBeforeTheModuleIsLookedAt()
        {
            AppCommandResponse response = Status(null, "--target", "  ");

            Assert.Equal(AppCommandReasons.BadArgument, response.Reason);
            Assert.Equal(10, response.ExitCode);
        }

        // ------------------------------------------------------------------ labels that are addresses

        [Fact]
        public void StatusRefusesARequestContainingAnUnsafeLabelWithoutNamingIt()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", Address, address: Address));
            host.Tiles.Add(Tile("c", SecretHost, "ping", SecretHost));

            AppCommandResponse response = Status(host);

            Assert.Equal(11, response.ExitCode);
            Assert.Equal(AppCommandReasons.LabelUnsafe, response.Reason);
            Assert.Null(response.Data);
            string all = Everything(response);
            Assert.DoesNotContain(Address, all);
            Assert.DoesNotContain(SecretHost, all);
        }

        [Fact]
        public void NamingATargetWhoseLabelIsItsAddressIsRefusedWithoutAWordOfTheLabel()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("b", SecretHost, "ping", SecretHost));

            AppCommandResponse status = Status(host, "--target", SecretHost);
            AppCommandResponse check = Check(host, "--target", SecretHost).GetAwaiter().GetResult();

            foreach (AppCommandResponse refused in new[] { status, check })
            {
                Assert.Equal("refused", refused.Status);
                Assert.Equal(AppCommandReasons.LabelUnsafe, refused.Reason);
                Assert.Equal(11, refused.ExitCode);
                Assert.Null(refused.Data);
                Assert.DoesNotContain(SecretHost, Everything(refused));
            }
            Assert.Empty(host.Requested);
        }

        [Fact]
        public void StatusRefusesALabelThatNamesAnotherTargetsHost()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "office-server", address: Address));
            host.Tiles.Add(Tile("b", "Website", address: "office-server"));

            AppCommandResponse response = Status(host, "--target", "office-server");

            Assert.Equal(11, response.ExitCode);
            Assert.Equal(AppCommandReasons.LabelUnsafe, response.Reason);
            Assert.Null(response.Data);
            Assert.DoesNotContain("office-server", Everything(response));
        }

        [Fact]
        public void UnknownKindDoesNotEchoAnImportedValue()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server", kind: SecretHost));

            AppCommandResponse response = Status(host);

            Assert.Equal("unknown", TileAt(response, 0)["kind"]);
            Assert.DoesNotContain(SecretHost, Everything(response));
        }

        [Fact]
        public void AnOversizedStatusFailsInsteadOfReturningAnIncompleteBoard()
        {
            var host = new FakeHost();
            for (int i = 0; i < 200; i++)
                host.Tiles.Add(Tile(i.ToString(), new string('\u0001', 64)));

            AppCommandResponse response = Status(host);

            Assert.Equal(1, response.ExitCode);
            Assert.Equal("response-too-large", response.Reason);
            Assert.Null(response.Data);
        }

        [Theory]
        [InlineData("ping", "nas", "NAS", false)]
        [InlineData("ping", "nas", "Home NAS", false)]
        [InlineData("ping", "nas", "Plan A: backup", true)]
        [InlineData("ping", "192.168.1.100", "192.168.1.100", false)]
        [InlineData("ping", "192.168.1.100", "Router 192.168.1.1", false)]
        [InlineData("ping", "10.0.0.1", "Router", true)]
        [InlineData("http", "https://google.com", "google.com", false)]
        [InlineData("http", "https://example.com/status", "Example site", true)]
        [InlineData("http", "https://example.com/status", "status", false)]
        [InlineData("http", "https://example.com", "see https://example.com now", false)]
        [InlineData("folder", @"D:\Backups", "Backups", false)]
        [InlineData("folder", @"D:\Backups", "Nightly copies", true)]
        [InlineData("folder", @"\\nas\share", "NAS backups", false)]
        [InlineData("folder", @"\\nas\share", "Home backups", true)]
        [InlineData("drive", "D:", "D:", false)]
        [InlineData("drive", "D:", "Disk D", false)]
        [InlineData("drive", "D:", "Data disk", true)]
        [InlineData("tcp", "nas:445", "nas:445", false)]
        [InlineData("tcp", "nas:445", "SMB 445", false)]
        [InlineData("tcp", "nas:445", "File server", true)]
        [InlineData("tcp", "10.1.1.1:22", "Jump host 10.1.1.1:22", false)]
        [InlineData("ping", "nas", @"\\nas\share", false)]
        [InlineData("ping", "nas", "ops@example.com", false)]
        [InlineData("ping", "nas", "Backup/restore", false)]
        [InlineData("ping", "nas", "readme.md notes", true)]
        [InlineData("ping", "nas", "Lunch 12:30", false)]
        [InlineData("ping", "nas", "", true)]
        [InlineData("ping", "nas", "Сервер резервных копий", true)]
        public void ALabelIsWithheldWhenItCarriesWhatTheAddressSays(string kind, string address, string label, bool safe)
        {
            var target = new TrafficLightTarget { Id = "t", Label = label, Kind = kind, Address = address };

            Assert.Equal(safe, TrafficLightLabels.IsSafe(target));
        }

        [Fact]
        public void AFingerprintInALabelIsWithheld()
        {
            var target = new TrafficLightTarget
            {
                Id = "t", Label = "Office SHA256:abc", Kind = "exchange", Address = "host.example.org:4443",
            };

            Assert.False(TrafficLightLabels.IsSafe(target));
        }

        // ------------------------------------------------------------------ check

        [Fact]
        public async Task CheckAsksForExactlyTheTargetsOfTheLabelAndAnswersTheirFreshTiles()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server", state: TrafficState.Ok, checkedAt: At(1)));
            host.Tiles.Add(Tile("b", "Website", state: TrafficState.Ok, checkedAt: At(1)));
            var finish = new TaskCompletionSource<TrafficCheckOutcome>();
            host.Ticketer = id => TrafficCheckTicket.Taken(finish.Task, () => { });

            Task<AppCommandResponse> pending = Check(host, "--target", "Backup server");
            Assert.False(pending.IsCompleted);
            // The check ends: the engine now holds a newer look, and only then does the ticket complete.
            host.Tiles[0] = Tile("a", "Backup server", state: TrafficState.Down, checkedAt: At(9));
            finish.SetResult(TrafficCheckOutcome.Completed);
            AppCommandResponse response = await pending;

            Assert.Equal(new[] { "a" }, host.Requested.ToArray());
            Assert.Equal(0, response.ExitCode);
            Assert.Equal(1, (int)response.Data!["count"]);
            Dictionary<string, object> tile = TileAt(response, 0);
            Assert.Equal("down", tile["state"]);
            Assert.Equal("2026-11-17T10:09:30Z", tile["lastCheckedUtc"]);
            Assert.Equal("Checked 1 of 1.", response.Message);
            Assert.False(response.Data.ContainsKey("notChecked"));
        }

        [Fact]
        public async Task CheckAllRefusesUnsafeLabelsBeforeStartingAnyCheck()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", "Website"));
            host.Tiles.Add(Tile("c", SecretHost, "ping", SecretHost));

            AppCommandResponse response = await Check(host, "--all");

            Assert.Empty(host.Requested);
            Assert.Equal(11, response.ExitCode);
            Assert.Equal(AppCommandReasons.LabelUnsafe, response.Reason);
            Assert.Null(response.Data);
            Assert.DoesNotContain(SecretHost, Everything(response));
        }

        [Fact]
        public async Task CheckRefusesALabelMadeUnsafeWhileTheCheckWasRunning()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            var finish = new TaskCompletionSource<TrafficCheckOutcome>();
            host.Ticketer = _ => TrafficCheckTicket.Taken(finish.Task, () => { });
            Task<AppCommandResponse> pending = Check(host, "--all");

            host.Tiles[0] = Tile("a", SecretHost, "ping", SecretHost);
            finish.SetResult(TrafficCheckOutcome.Completed);
            AppCommandResponse response = await pending;

            Assert.Equal(11, response.ExitCode);
            Assert.Equal(AppCommandReasons.LabelUnsafe, response.Reason);
            Assert.Null(response.Data);
            Assert.DoesNotContain(SecretHost, Everything(response));
        }

        [Fact]
        public async Task ATargetThatCannotBeCheckedIsNamedInTheMessageAndLeftOutOfTheTiles()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", "Parked", enabled: false));
            host.Tiles.Add(Tile("c", "Typo", invalid: true));
            host.Tiles.Add(Tile("d", "Far away"));
            host.Ticketer = id => id == "a"
                ? TrafficCheckTicket.Taken(Task.FromResult(TrafficCheckOutcome.Completed), () => { })
                : TrafficCheckTicket.NotTaken(id == "b" ? TrafficCheckStart.Disabled : id == "c" ? TrafficCheckStart.InvalidAddress : TrafficCheckStart.Offline);

            AppCommandResponse response = await Check(host, "--all");

            Assert.Equal(0, response.ExitCode);
            Assert.Equal(1, (int)response.Data!["count"]);
            Assert.Equal(3, (int)response.Data["notChecked"]);
            Assert.Equal("Backup server", TileAt(response, 0)["label"]);
            Assert.Contains("Checked 1 of 4.", response.Message);
            Assert.Contains("Parked, Typo, Far away", response.Message);
        }

        [Fact]
        public async Task ATargetWhoseEarlierCallIsStillOutIsNamedAsNotCheckedAndDoesNotHoldTheAnswer()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", "Held"));
            host.Ticketer = id => id == "a"
                ? TrafficCheckTicket.Taken(Task.FromResult(TrafficCheckOutcome.Completed), () => { })
                : TrafficCheckTicket.NotTaken(TrafficCheckStart.Busy);

            AppCommandResponse response = await Check(host, "--all");

            Assert.Equal(0, response.ExitCode);
            Assert.Equal(1, (int)response.Data!["count"]);
            Assert.Equal(1, (int)response.Data["notChecked"]);
            Assert.Contains("Checked 1 of 2.", response.Message);
            Assert.Contains("busy): Held.", response.Message);
        }

        [Fact]
        public async Task ACheckThatWasLetGoIsNotAFreshTile()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server", checkedAt: At(1)));
            host.Ticketer = id => TrafficCheckTicket.Taken(Task.FromResult(TrafficCheckOutcome.Abandoned), () => { });

            AppCommandResponse response = await Check(host, "--target", "Backup server");

            Assert.Equal(0, response.ExitCode);
            Assert.Equal(0, (int)response.Data!["count"]);
            Assert.Equal(1, (int)response.Data["notChecked"]);
            Assert.Contains("Backup server", response.Message);
        }

        [Fact]
        public async Task ACheckPastItsBoundIs2TimeoutNeverDoneAndLetsGoOfTheWaits()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            host.Tiles.Add(Tile("b", "Website"));
            host.Ticketer = id => TrafficCheckTicket.Taken(new TaskCompletionSource<TrafficCheckOutcome>().Task, () => host.Cancelled.Add(id));
            using var bound = new CancellationTokenSource(80);

            AppCommandResponse response = await Check(host, bound.Token, "--all");

            Assert.Equal("failed", response.Status);
            Assert.Equal(AppCommandReasons.Timeout, response.Reason);
            Assert.Equal(2, response.ExitCode);
            Assert.Equal(new[] { "a", "b" }, host.Cancelled.OrderBy(x => x).ToArray());
        }

        [Fact]
        public async Task CheckWithTheModuleOffIs11ModuleOffAndAsksForNothing()
        {
            AppCommandResponse response = await Check(null, "--all");

            Assert.Equal("module-off", response.Reason);
            Assert.Equal(11, response.ExitCode);
            Assert.Null(response.Data);
        }

        [Fact]
        public async Task CheckAllFalseAndABlankTargetAreUsageErrors()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));

            AppCommandResponse notAll = await Check(host, "--all=false");
            AppCommandResponse blank = await Check(host, "--target", " ");

            Assert.Equal(10, notAll.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, notAll.Reason);
            Assert.Equal(10, blank.ExitCode);
            Assert.Empty(host.Requested);
        }

        [Fact]
        public async Task ALabelThatMatchesNothingIsZeroWithCountZeroAndNothingIsAsked()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));

            AppCommandResponse response = await Check(host, "--target", "Nothing");

            Assert.Equal(0, response.ExitCode);
            Assert.Equal(0, (int)response.Data!["count"]);
            Assert.Empty(host.Requested);
        }

        // ------------------------------------------------------------------ the log

        [Fact]
        public async Task TheLogLineHoldsTheVerbTheOutcomeAndACountButNeverALabel()
        {
            var host = new FakeHost();
            host.Tiles.Add(Tile("a", "Backup server"));
            AppCommandResponse response = await Check(host, "--all");

            string line = AppCommandsLog.Line(new DateTime(2026, 11, 17, 10, 0, 0), "trafficlight check", "cli", 4242, response);

            Assert.Contains("trafficlight check", line);
            Assert.Contains("status=done", line);
            Assert.Contains("exit=0", line);
            Assert.Contains("count=1", line);
            Assert.DoesNotContain("Backup server", line);
            Assert.DoesNotContain(Address, line);
        }
    }
}
