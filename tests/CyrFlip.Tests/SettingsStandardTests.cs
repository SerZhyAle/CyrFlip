using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The settings standard of CyrFlip on an exchange server (<c>DEVICE-EXCHANGE</c> 1.12, items EN to ER, ticket
    /// S0095): the groups and the local exclusions, the payload a save writes, the reading of one a get returned, the
    /// four frames against the catalog's samples and a scripted server, and the conformance cases the clients owe
    /// (10 to 14 of the amendment's table).
    /// </summary>
    [Collection("ExchangeSecrets")]
    public class SettingsStandardTests : IDisposable
    {
        private static readonly string Vectors = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures", "DeviceExchange", "settings-standard-v1"));
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly FakeExchangeServer _server = new FakeExchangeServer();
        private readonly FakeAccount _alice;
        private int _saves;

        public SettingsStandardTests()
        {
            ExchangeSecrets.Use(_secrets);
            _server.Features.Add("settings");
            _alice = _server.AddAccount("alice");
        }

        public void Dispose() => ExchangeSecrets.Use(null);

        private static string[] AllGroups => SettingsStandard.Groups.Select(g => g.Id).ToArray();

        private static Dictionary<string, object> Json(string text)
            => (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text);

        // ---------------------------------------------------------------- groups and exclusions (item EQ)

        [Fact]
        public void EveryValueOfTheSettingsFileIsEitherInExactlyOneGroupOrLocalOnly()
        {
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
            {
                int groups = SettingsStandard.Groups.Count(g => g.Keys.Contains(entry.Key));
                bool local = SettingsStandard.GroupIdOf(entry) == null;
                Assert.True(local ? groups == 0 : groups == 1, entry.Key + " is in " + groups + " groups");
                if (local)
                    Assert.True(SettingsStandard.LocalOnly.ContainsKey(entry.Key) || entry.Group.StartsWith("navigation/", StringComparison.Ordinal)
                        || entry.Group.StartsWith("launcher/", StringComparison.Ordinal), entry.Key + " is local-only with no declared reason");
            }
            Assert.Equal(10, SettingsStandard.Groups.Count);
            Assert.All(SettingsStandard.Groups, g => Assert.NotEmpty(g.Keys));
        }

        [Fact]
        public void GroupIdsHaveTheShapeOfTheContract()
        {
            Assert.All(SettingsStandard.Groups, g => Assert.True(SettingsStandard.IsValidGroupId(g.Id), g.Id));
            Assert.Equal(AllGroups.Length, AllGroups.Distinct().Count());
            Assert.False(SettingsStandard.IsValidGroupId("Quick-notes"));
            Assert.False(SettingsStandard.IsValidGroupId("1general"));
            Assert.False(SettingsStandard.IsValidGroupId(new string('a', 33)));
        }

        [Theory]
        [InlineData("ScreenshotFolder")]                 // a path
        [InlineData("ExchangeClipboardReceivedFolder")]  // a path
        [InlineData("TranslateEndpoint")]                // a loopback address and a port
        [InlineData("TrafficLightTargets")]              // hosts, ports, folders and drive letters
        [InlineData("EnableScenarioLauncher")]           // scenarios name programs by their paths
        [InlineData("SettingsWindowX")]                  // window bounds
        [InlineData("ClipboardHistoryWidth")]
        [InlineData("TrafficLightVisible")]              // window state
        [InlineData("SettingsViewport")]
        public void ADeclaredExclusionIsInNoGroup(string key)
        {
            Assert.DoesNotContain(SettingsStandard.Groups, g => g.Keys.Contains(key));
        }

        [Fact]
        public void WhatTheSettingsFileAlreadyLeavesOutIsNeverInAGroup()
        {
            foreach (string key in SettingsFileInventory.Excluded.Keys) // credentials, the display profile, counters, history
                Assert.DoesNotContain(SettingsStandard.Groups, g => g.Keys.Contains(key));
        }

        // ---------------------------------------------------------------- the payload (items EO and EQ)

        [Fact]
        public void ASaveWritesTheChosenGroupsAndNoDeclaredExclusion()
        {
            var config = new AppConfig { ScreenshotFolder = @"D:\Shots", SettingsWindowX = 123, TranslateEndpoint = "http://127.0.0.1:11434", ExchangeClipboardReceivedFolder = @"E:\In" };
            string payload = SettingsStandard.Serialize(config, AllGroups)!;
            Assert.True(Encoding.UTF8.GetByteCount(payload) <= SettingsStandard.MaxPayloadBytes);

            Dictionary<string, object> root = Json(payload);
            Assert.Equal(1, root["version"]);
            var groups = (Dictionary<string, object>)root["groups"];
            Assert.Equal(AllGroups, groups.Keys.ToArray());

            var everyKey = new HashSet<string>(groups.Values.Cast<Dictionary<string, object>>().SelectMany(g => g.Keys));
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported.Where(e => SettingsStandard.GroupIdOf(e) == null))
                Assert.DoesNotContain(entry.Key, everyKey);
            foreach (string key in SettingsFileInventory.Excluded.Keys)
                Assert.DoesNotContain(key, everyKey);
            Assert.DoesNotContain(@"D:\\Shots", payload);
            Assert.DoesNotContain("11434", payload);
        }

        [Fact]
        public void NoneOfTheCommonSpellingsOfTheManifestIsEverWritten()
        {
            var manifest = Json(File.ReadAllText(Path.Combine(Vectors, "07-local-exclusion-manifest.json")));
            var spellings = ((object[])manifest["mandatoryLocalExclusions"]).Cast<string>().ToArray();
            Assert.Contains("window_bounds", spellings);
            string payload = SettingsStandard.Serialize(new AppConfig(), AllGroups)!;
            var keys = ((Dictionary<string, object>)Json(payload)["groups"]).Values.Cast<Dictionary<string, object>>().SelectMany(g => g.Keys).ToArray();
            foreach (string spelling in spellings)
                Assert.DoesNotContain(keys, k => string.Equals(k, spelling, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AGroupLeftOutOfASaveIsAbsentFromThePayload()
        {
            string payload = SettingsStandard.Serialize(new AppConfig(), new[] { "general", "symbols" })!;
            var groups = (Dictionary<string, object>)Json(payload)["groups"];
            Assert.Equal(new[] { "general", "symbols" }, groups.Keys.ToArray());
            Assert.Equal("{\"version\":1,\"groups\":{}}", SettingsStandard.Serialize(new AppConfig(), Array.Empty<string>()));
        }

        // ---------------------------------------------------------------- the reading (item ER)

        [Fact]
        public void ARoundTripCarriesTheExchangeableValuesAndLeavesTheLocalOnesAlone()
        {
            var source = new AppConfig { Theme = "dark", CaretDotMode = true, ClipboardHistoryOpacity = 90, QuickNotesWordWrap = true, ScreenshotFolder = @"D:\Shots" };
            string payload = SettingsStandard.Serialize(source, AllGroups)!;

            var target = new AppConfig { ScreenshotFolder = @"C:\Mine", SettingsWindowX = 77 };
            SettingsStandardRead read = SettingsStandard.Parse(payload, 1, AllGroups);
            Assert.True(read.Ok, read.Code);
            SettingsImportPlan plan = SettingsImportPlan.Build(target, read.Document);
            Assert.Equal("dark", plan.Candidate.Theme);
            Assert.True(plan.Candidate.CaretDotMode);
            Assert.Equal(90, plan.Candidate.ClipboardHistoryOpacity);
            Assert.True(plan.Candidate.QuickNotesWordWrap);
            Assert.Equal(@"C:\Mine", plan.Candidate.ScreenshotFolder);
            Assert.Equal(77, plan.Candidate.SettingsWindowX);
        }

        [Fact]
        public void AnInboundPayloadCannotOverwriteWhatStaysOnThisComputer()
        {
            // An old, foreign or hostile payload: local-only keys placed in a group, a group this program does not know.
            string hostile = "{\"version\":1,\"groups\":{\"general\":{\"Theme\":\"dark\",\"ScreenshotFolder\":\"X:\\\\evil\",\"ExchangeClipboardAccounts\":\"[]\",\"SettingsWindowX\":5},"
                + "\"graphics\":{\"ScreenshotFolder\":\"X:\\\\evil\"},\"translate\":{\"TranslateEndpoint\":\"http://evil.example:1\"},\"secrets\":{\"device_key\":\"x\"}}}";
            SettingsStandardRead read = SettingsStandard.Parse(hostile, 1, AllGroups);
            Assert.True(read.Ok, read.Code);
            Assert.Equal(new[] { "Theme" }, read.Document.Values.Keys.ToArray());
            Assert.Equal(5, read.Document.UnknownKeys);
        }

        [Fact]
        public void AGroupLeftOutOfAReadIsNotApplied()
        {
            string payload = SettingsStandard.Serialize(new AppConfig { Theme = "dark", CaretDotMode = true }, AllGroups)!;
            SettingsStandardRead read = SettingsStandard.Parse(payload, 1, new[] { "indicators" });
            Assert.True(read.Ok);
            Assert.True(read.Document.Values.ContainsKey("CaretDotMode"));
            Assert.False(read.Document.Values.ContainsKey("Theme"));
            Assert.All(read.Document.Values.Keys, k => Assert.Contains(k, SettingsStandard.Find("indicators")!.Keys));
        }

        [Theory]
        [InlineData("{\"version\":1,\"groups\":{}}", 2, ExchangeCodes.FormatUnsupported)]   // a newer format applies nothing
        [InlineData("{\"version\":2,\"groups\":{}}", 1, ExchangeCodes.InvalidPayload)]      // version unequal to formatVersion
        [InlineData("[1,2]", 1, ExchangeCodes.InvalidPayload)]                                // not one object
        [InlineData("not json", 1, ExchangeCodes.InvalidPayload)]
        [InlineData("{\"version\":1}", 1, ExchangeCodes.InvalidPayload)]                      // no groups object
        [InlineData("{\"version\":1,\"groups\":{\"general\":3}}", 1, ExchangeCodes.InvalidPayload)] // a group that is not an object
        [InlineData("{\"version\":1,\"groups\":{}}", 0, ExchangeCodes.InvalidPayload)]
        public void AnAnswerThatIsNotUnderstoodWholeAppliesNothing(string payload, long formatVersion, string code)
        {
            SettingsStandardRead read = SettingsStandard.Parse(payload, formatVersion, AllGroups);
            Assert.Equal(code, read.Code);
            Assert.Empty(read.Document.Values);
        }

        [Fact]
        public void APayloadAboveTheBoundAppliesNothing()
        {
            string big = "{\"version\":1,\"groups\":{\"general\":{\"Theme\":\"" + new string('a', SettingsStandard.MaxPayloadBytes) + "\"}}}";
            Assert.Equal(ExchangeCodes.InvalidPayload, SettingsStandard.Parse(big, 1, AllGroups).Code);
        }

        [Fact]
        public void TheSamplePayloadOfAnotherProgramIsUnderstoodAndChangesNothingHere()
        {
            var frame = Json(File.ReadAllText(Path.Combine(Vectors, "04-get-standard-payload-response.json")));
            SettingsStandardRead read = SettingsStandard.Parse((string)frame["settingsPayload"], (int)frame["formatVersion"], AllGroups);
            Assert.True(read.Ok, read.Code);
            Assert.Empty(read.Document.Values); // its "general" holds keys of fms_win, unknown here
        }

        // ---------------------------------------------------------------- the frames against the samples

        [Fact]
        public void TheVectorsAreTheCatalogsBytes()
        {
            var pinned = new Dictionary<string, string>
            {
                ["01-save-standard-request.json"] = "84e4c3a4a3adbee35d8ba4d761e21e64eec32602fe016c20481a86fee49131c6",
                ["03-get-standard-request.json"] = "0144d219c45cc7078448ddcf5cb934a792c2c7695ffe9e8eae6e8c55c2adf9ed",
                ["04-get-standard-payload-response.json"] = "17d21f895b6dacdb296d4ff830de78f195f8cdca3cec060fd8236794e19a882d",
                ["07-local-exclusion-manifest.json"] = "60136bf6f6741aae7826b1c9118e4130613b9b59556c448cb7d18c57c6c37b7d",
            };
            using (SHA256 sha = SHA256.Create())
                foreach (var pair in pinned)
                {
                    string hex = string.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Vectors, pair.Key))).Select(b => b.ToString("x2")));
                    Assert.Equal(pair.Value, hex);
                }
        }

        [Fact]
        public void TheTwoRequestsHaveTheMembersOfTheSamples()
        {
            var sample = Json(File.ReadAllText(Path.Combine(Vectors, "01-save-standard-request.json")));
            byte[] frame = ExchangeWire.Encode(ExchangeMembers.SettingsSaveStandard((string)sample["appId"], (int)sample["formatVersion"],
                (string)sample["settingsPayload"], new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc)));
            var ours = Json(Encoding.UTF8.GetString(frame, 4, frame.Length - 4));
            Assert.Equal(sample.OrderBy(p => p.Key), ours.OrderBy(p => p.Key));

            var getSample = Json(File.ReadAllText(Path.Combine(Vectors, "03-get-standard-request.json")));
            byte[] get = ExchangeWire.Encode(ExchangeMembers.SettingsGetStandard((string)getSample["appId"]));
            Assert.Equal(getSample.OrderBy(p => p.Key), Json(Encoding.UTF8.GetString(get, 4, get.Length - 4)).OrderBy(p => p.Key));
        }

        [Fact]
        public void TheRefusalsOfTheSamplesAreKnownReasonsWithTheContractsWords()
        {
            foreach (string file in new[] { "05-refused-not-found.json", "06-refused-incompatible.json" })
            {
                var refused = Json(File.ReadAllText(Path.Combine(Vectors, file)));
                string reason = (string)refused["reason"];
                Assert.True(ExchangeCodes.IsKnownWireReason(reason), reason);
                ExchangeMessage m = ExchangeMessages.Describe(reason, ExchangeContext.Get, s => s);
                Assert.DoesNotContain((string)refused["message"], m.Text); // the refusal's message is never shown
            }
        }

        // ---------------------------------------------------------------- against a scripted server

        private ExchangeSettingsStandardService Service(FakeExchangeServer? server = null)
            => new ExchangeSettingsStandardService(new ExchangeClient(server ?? _server, "26.11.12.1200"), () => _saves++);

        private async Task<ExchangeAccountEntry> Join(string login = "alice")
        {
            ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(new ExchangeClient(_server, "1"), new ExchangeJoinRequest
            {
                Host = "nas.example.org", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key), Login = login, Password = "Passw0rdTest", DeviceName = "PC",
            }, null, CancellationToken.None);
            Assert.True(joined.Ok, joined.Code);
            return joined.Value!;
        }

        [Fact]
        public async Task ASaveIsVersionOneAndAGetReturnsWhatWasStored()
        {
            ExchangeAccountEntry entry = await Join();
            Assert.True(ExchangeSettingsStandardService.Offered(entry));

            ExchangeOutcome<ExchangeStandardPayload> before = await Service().GetAsync(entry, CancellationToken.None);
            Assert.Equal(ExchangeCodes.NotFound, before.Code);

            string payload = SettingsStandard.Serialize(new AppConfig { Theme = "dark" }, AllGroups)!;
            ExchangeOutcome<ExchangeStandardSaved> saved = await Service().SaveAsync(entry, payload, CancellationToken.None);
            Assert.True(saved.Ok, saved.Code);
            Assert.Equal(1, saved.Value!.Version);
            Assert.Equal(_alice.StandardUpdatedAt, saved.Value.UpdatedAt);

            ExchangeOutcome<ExchangeStandardSaved> again = await Service().SaveAsync(entry, payload, CancellationToken.None);
            Assert.Equal(2, again.Value!.Version); // a repeat is safe: same content, only the version moves

            ExchangeOutcome<ExchangeStandardPayload> got = await Service().GetAsync(entry, CancellationToken.None);
            Assert.True(got.Ok, got.Code);
            Assert.Equal(payload, got.Value!.Payload);
            Assert.Equal(1, got.Value.FormatVersion);
            Assert.True(SettingsStandard.Parse(got.Value.Payload, got.Value.FormatVersion, AllGroups).Document.Values.ContainsKey("Theme"));
        }

        [Fact]
        public async Task AServerWithoutTheFeatureIsNeverSentTheFramesAndTheActionsAreHidden()
        {
            _server.Features.Remove("settings");
            ExchangeAccountEntry entry = await Join();
            Assert.False(ExchangeSettingsStandardService.Offered(entry));

            ExchangeOutcome<ExchangeStandardSaved> saved = await Service().SaveAsync(entry, "{\"version\":1,\"groups\":{}}", CancellationToken.None);
            ExchangeOutcome<ExchangeStandardPayload> got = await Service().GetAsync(entry, CancellationToken.None);
            Assert.Equal(ExchangeCodes.ServerNoSettings, saved.Code);
            Assert.Equal(ExchangeCodes.ServerNoSettings, got.Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("settings_", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AGuestIsRefusedWithoutAFrameAndNothingIsStored()
        {
            _server.AddAccount("guest1", kind: "guest");
            ExchangeAccountEntry entry = await Join("guest1");
            ExchangeOutcome<ExchangeStandardSaved> saved = await Service().SaveAsync(entry, "{\"version\":1,\"groups\":{}}", CancellationToken.None);
            Assert.Equal(ExchangeCodes.NotPermitted, saved.Code);
            Assert.DoesNotContain(_server.Seen, t => t.StartsWith("settings_", StringComparison.Ordinal));
            Assert.Null(_server.Accounts["guest1"].StandardPayload);
        }

        [Fact]
        public async Task AStreamThatDropsLeavesAReadUnappliedAndASaveUnconfirmed()
        {
            ExchangeAccountEntry entry = await Join();
            _server.DropSettingsAnswer = true;
            ExchangeOutcome<ExchangeStandardPayload> got = await Service().GetAsync(entry, CancellationToken.None);
            Assert.Equal(ExchangeCodes.Unanswered, got.Code);
            ExchangeOutcome<ExchangeStandardSaved> saved = await Service().SaveAsync(entry, "{\"version\":1,\"groups\":{}}", CancellationToken.None);
            Assert.Equal(ExchangeCodes.SaveUnconfirmed, saved.Code);
            Assert.False(string.IsNullOrEmpty(ExchangeMessages.Describe(saved.Code, ExchangeContext.Send, s => s).What));
        }

        [Fact]
        public async Task AnInvalidPayloadIsRefusedByTheServerAndNothingChanges()
        {
            ExchangeAccountEntry entry = await Join();
            ExchangeOutcome<ExchangeStandardSaved> saved = await Service().SaveAsync(entry, "[]", CancellationToken.None);
            Assert.Equal(ExchangeCodes.InvalidPayload, saved.Code);
            Assert.Equal(0, _alice.StandardVersion);
        }

        // ---------------------------------------------------------------- independence (item ER, case 12)

        [Fact]
        public void NothingButTheTwoButtonsSendsOrReadsTheStandard()
        {
            string all = string.Concat(Directory.GetFiles(Source, "*.cs").Select(f => "\n//FILE " + Path.GetFileName(f) + "\n" + File.ReadAllText(f)));
            // The frames are built in one place and sent by the client alone.
            Assert.Equal(new[] { "ExchangeClient.cs" }, FilesCalling(all, "ExchangeMembers.SettingsSaveStandard("));
            Assert.Equal(new[] { "ExchangeSettingsStandardService.cs" }, FilesCalling(all, "_client.SaveStandardAsync("));
            Assert.Equal(new[] { "ExchangeSettingsStandardService.cs" }, FilesCalling(all, "_client.GetStandardAsync("));
            // The service is driven only from the context's handler of the page's request, raised only by a button.
            Assert.Equal(new[] { "CyrFlipContext.SettingsStandard.cs" }, FilesCalling(all, "new ExchangeSettingsStandardService("));
            Assert.Equal(new[] { "SettingsForm.ExchangeServer.cs" }, FilesCalling(all, "SettingsStandardRequested?.Invoke("));
        }

        private static string[] FilesCalling(string all, string call)
        {
            var files = new List<string>();
            string current = "";
            foreach (string line in all.Split('\n'))
            {
                if (line.StartsWith("//FILE ", StringComparison.Ordinal)) { current = line.Substring(7).Trim(); continue; }
                if (line.Contains(call) && !files.Contains(current)) files.Add(current);
            }
            return files.ToArray();
        }
    }
}
