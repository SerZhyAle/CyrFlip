using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure half of the REMOTE-CONTROL target (ticket CYRFLP-0140): the payload of a request read strictly, the order
    /// a request is decided in, the time window, the ring, the answer's bound, the grants, and the shipped manifest's
    /// <c>remote</c> members. No stream, no sealing, no window and no DPAPI: the cases that need two live devices (R1 to
    /// R10 of the contract) are the owner's device test.
    /// </summary>
    public class RemoteControlTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static byte[] Payload(object payload) => Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));

        private static Dictionary<string, object> Request(string verb, Dictionary<string, object>? args = null, DateTime? issued = null, int? ttl = null)
        {
            var map = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["verb"] = verb,
                ["issuedAt"] = (issued ?? Now).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            };
            if (args != null) map["args"] = args;
            if (ttl != null) map["ttlSeconds"] = ttl.Value;
            return map;
        }

        private static RemoteControlRequest Parsed(Dictionary<string, object> request)
        {
            RemoteControlRequest? r = RemoteControlRequest.TryParse(Payload(request), out string reason);
            Assert.True(r != null, "the request did not read: " + reason);
            return r!;
        }

        /// <summary>A manifest of four verbs: a safe remote one, a device-confirmed one, a non-remote one and a reads-user-data remote one.</summary>
        private static AppCommandManifest TestManifest()
        {
            const string json = @"{ ""schemaVersion"": 1, ""product"": ""CyrFlip"", ""exe"": ""CyrFlip.exe"", ""cliVersion"": ""1.1"", ""commands"": [
  { ""name"": ""layout current"", ""coldStart"": ""refuse"", ""risk"": ""safe"", ""remote"": { ""confirm"": ""grant"" },
    ""args"": [ { ""name"": ""json"", ""kind"": ""bool"" }, { ""name"": ""result"", ""kind"": ""path"" } ] },
  { ""name"": ""hotkeys pause"", ""coldStart"": ""refuse"", ""risk"": ""safe"", ""remote"": { ""confirm"": ""device"" },
    ""args"": [ { ""name"": ""paused"", ""kind"": ""bool"", ""required"": true }, { ""name"": ""json"", ""kind"": ""bool"" } ] },
  { ""name"": ""keepawake set"", ""coldStart"": ""refuse"", ""risk"": ""safe"", ""remote"": { ""confirm"": ""grant"" },
    ""args"": [ { ""name"": ""system"", ""kind"": ""bool"" }, { ""name"": ""minutes"", ""kind"": ""int"" }, { ""name"": ""out"", ""kind"": ""path"" } ] },
  { ""name"": ""scenario run"", ""coldStart"": ""oneshot"", ""risk"": ""writes-user-data"",
    ""args"": [ { ""name"": ""name"", ""kind"": ""string"" } ] },
  { ""name"": ""note list"", ""coldStart"": ""refuse"", ""risk"": ""reads-user-data"", ""remote"": { ""confirm"": ""grant"" },
    ""args"": [ { ""name"": ""limit"", ""kind"": ""int"" } ] },
  { ""name"": ""odd confirm"", ""coldStart"": ""refuse"", ""risk"": ""safe"", ""remote"": { ""confirm"": ""whenever"" }, ""args"": [] }
] }";
            return AppCommandManifest.Parse(Encoding.UTF8.GetBytes(json));
        }

        private static RemoteControlGrant SafeAlways() => RemoteControlGrant.Ticked("entry", "ctrl", "Phone");

        private static RemoteControlVerdict Decide(Dictionary<string, object> request, bool listening = true, RemoteControlGrant? grant = null, bool noGrant = false)
        {
            RemoteControlRequest? r = RemoteControlRequest.TryParse(Payload(request), out string reason);
            return RemoteControlPolicy.Decide(r, reason, listening, noGrant ? null : grant ?? SafeAlways(), TestManifest(), Now);
        }

        private static void AssertRefused(RemoteControlVerdict verdict, string reason, int exit)
        {
            Assert.Equal(RemoteControlVerdictKind.Refuse, verdict.Kind);
            Assert.NotNull(verdict.Refusal);
            Assert.Equal("refused", verdict.Refusal!.Status);
            Assert.Equal(reason, verdict.Refusal.Reason);
            Assert.Equal(exit, verdict.Refusal.Exit);
        }

        // ------------------------------------------------------------------ the payload (rule 5)

        [Fact]
        public void AWellFormedRequestReadsWithTheDefaultTtl()
        {
            RemoteControlRequest r = Parsed(Request("layout current"));
            Assert.Equal("layout current", r.Verb);
            Assert.Empty(r.Args);
            Assert.Equal(RemoteControl.DefaultTtlSeconds, r.TtlSeconds);
            Assert.Equal(Now, r.IssuedAtUtc);
            Assert.Equal(Now.AddSeconds(120 + 300), r.WindowEndUtc);
        }

        [Fact]
        public void AnUnknownMemberOfTheRequestIsNeverIgnored()
        {
            Dictionary<string, object> request = Request("layout current");
            request["force"] = true;
            Assert.Null(RemoteControlRequest.TryParse(Payload(request), out string reason));
            Assert.Equal(AppCommandReasons.BadArgument, reason);
        }

        [Fact]
        public void ANewerPayloadVersionIsSchemaAndAMissingOneIsBadArgument()
        {
            Dictionary<string, object> newer = Request("status");
            newer["v"] = 2;
            Assert.Null(RemoteControlRequest.TryParse(Payload(newer), out string reason));
            Assert.Equal(AppCommandReasons.Schema, reason);

            Dictionary<string, object> none = Request("status");
            none.Remove("v");
            Assert.Null(RemoteControlRequest.TryParse(Payload(none), out reason));
            Assert.Equal(AppCommandReasons.BadArgument, reason);
        }

        [Theory]
        [InlineData(9)]
        [InlineData(601)]
        public void ATtlOutsideTenToSixHundredDoesNotRead(int ttl)
            => Assert.Null(RemoteControlRequest.TryParse(Payload(Request("status", ttl: ttl)), out _));

        [Fact]
        public void AnIssuedAtWithoutAZoneDoesNotRead()
        {
            Dictionary<string, object> request = Request("status");
            request["issuedAt"] = "2026-11-20T12:00:00";
            Assert.Null(RemoteControlRequest.TryParse(Payload(request), out _));
            request["issuedAt"] = "2026-11-20T14:00:00+02:00";
            Assert.Equal(Now, Parsed(request).IssuedAtUtc);
        }

        [Fact]
        public void APayloadAboveTheBoundIsTooLarge()
        {
            Assert.Null(RemoteControlRequest.TryParse(new byte[RemoteControl.MaxPayloadBytes + 1], out string reason));
            Assert.Equal(AppCommandReasons.TooLarge, reason);
        }

        // ------------------------------------------------------------------ the time window (rule 12)

        [Fact]
        public void TheWindowEndsAtIssuedAtPlusTtlPlusTheClockDifference()
        {
            RemoteControlRequest r = Parsed(Request("status", issued: Now.AddSeconds(-(60 + 300)), ttl: 60));
            Assert.False(r.IsExpired(Now));
            Assert.True(r.IsExpired(Now.AddSeconds(1)));
        }

        [Fact]
        public void ARequestFromMoreThanTheAllowedDifferenceAheadIsExpired()
        {
            Assert.False(Parsed(Request("status", issued: Now.AddSeconds(300))).IsExpired(Now));
            Assert.True(Parsed(Request("status", issued: Now.AddSeconds(301))).IsExpired(Now));
        }

        // ------------------------------------------------------------------ the order of the decision (rules 6 to 13)

        [Fact]
        public void WithTheSwitchOffEveryRequestIsNotPermitted()
        {
            AssertRefused(Decide(Request("layout current"), listening: false), RemoteControl.NotPermitted, AppCommandExit.Permission);
            // nothing else is looked at: even a verb that does not exist answers the same
            AssertRefused(Decide(Request("format disk"), listening: false), RemoteControl.NotPermitted, AppCommandExit.Permission);
        }

        [Fact]
        public void AControllerThatIsNotTickedIsNotPermitted()
            => AssertRefused(Decide(Request("layout current"), noGrant: true), RemoteControl.NotPermitted, AppCommandExit.Permission);

        [Fact]
        public void AnExpiredRequestIsRefusedWithExitTwo()
            => AssertRefused(Decide(Request("layout current", issued: Now.AddHours(-1))), RemoteControl.Expired, AppCommandExit.NotProven);

        [Theory]
        [InlineData("scenario run")]   // in the manifest, not remote
        [InlineData("format disk")]    // in no manifest
        [InlineData("odd confirm")]    // a confirm this reader does not know makes the verb not remote
        [InlineData("Layout Current")] // the verb is matched exactly, so a spelling cannot reach a hidden one
        public void AVerbThatIsNotRemoteIsUnknownVerb(string verb)
            => AssertRefused(Decide(Request(verb)), AppCommandReasons.UnknownVerb, AppCommandExit.Usage);

        [Fact]
        public void AnUnknownArgsMemberIsBadArgumentAndNothingRuns()
            => AssertRefused(Decide(Request("layout current", new Dictionary<string, object> { ["fast"] = true })),
                AppCommandReasons.BadArgument, AppCommandExit.Usage);

        [Theory]
        [InlineData("json")]
        [InlineData("result")]
        [InlineData("out")]
        public void JsonResultAndPathOptionsAreNeverRemoteArguments(string option)
        {
            string verb = option == "out" ? "keepawake set" : "layout current";
            object value = option == "json" ? (object)true : "C:\\temp\\x.png";
            AssertRefused(Decide(Request(verb, new Dictionary<string, object> { [option] = value })), AppCommandReasons.BadArgument, AppCommandExit.Usage);
        }

        [Fact]
        public void AValueOfTheWrongJsonTypeIsBadArgument()
        {
            AssertRefused(Decide(Request("keepawake set", new Dictionary<string, object> { ["system"] = "yes" })), AppCommandReasons.BadArgument, AppCommandExit.Usage);
            AssertRefused(Decide(Request("keepawake set", new Dictionary<string, object> { ["minutes"] = "10" })), AppCommandReasons.BadArgument, AppCommandExit.Usage);
        }

        [Fact]
        public void ArgumentsBecomeTokensTheLocalParserTakes()
        {
            RemoteControlVerdict v = Decide(Request("keepawake set", new Dictionary<string, object> { ["system"] = true, ["minutes"] = 30 }));
            Assert.Equal(RemoteControlVerdictKind.Run, v.Kind);
            Assert.Equal(new[] { "--system=true", "--minutes=30" }, v.Tokens);
            AppCommandLineResult parsed = AppCommandLine.ParseOptions(v.Spec!, v.Tokens);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            Assert.Equal("true", parsed.Invocation!.Option("system"));
        }

        [Fact]
        public void AMissingRequiredOptionIsBadArgument()
            => AssertRefused(Decide(Request("hotkeys pause")), AppCommandReasons.BadArgument, AppCommandExit.Usage);

        [Fact]
        public void AClassThatIsNotGrantedIsNotPermitted()
            => AssertRefused(Decide(Request("note list")), RemoteControl.NotPermitted, AppCommandExit.Permission);

        [Fact]
        public void AGrantedClassUnderAskIsAskedAndUnderAlwaysIsRun()
        {
            Assert.Equal(RemoteControlVerdictKind.Run, Decide(Request("layout current")).Kind);
            RemoteControlGrant ask = SafeAlways();
            ask.Classes["safe"] = RemoteControl.ConsentAsk;
            Assert.Equal(RemoteControlVerdictKind.Ask, Decide(Request("layout current"), grant: ask).Kind);
            RemoteControlGrant reads = SafeAlways();
            reads.Classes["reads-user-data"] = RemoteControl.ConsentAsk;
            Assert.Equal(RemoteControlVerdictKind.Ask, Decide(Request("note list"), grant: reads).Kind);
        }

        [Fact]
        public void AVerbConfirmedOnTheDeviceAsksWhateverTheGrantSays()
            => Assert.Equal(RemoteControlVerdictKind.Ask,
                Decide(Request("hotkeys pause", new Dictionary<string, object> { ["paused"] = true })).Kind);

        [Fact]
        public void TheTargetsOwnRefusalsCarryTheContractsStatusAndExit()
        {
            AssertRefused(Decide(Request("status"), listening: false), "not-permitted", 12);
            AssertRefused(Decide(Request("status", issued: Now.AddDays(-1))), "expired", 2);
        }

        // ------------------------------------------------------------------ the ring (rule 12)

        [Fact]
        public void TheRingAnswersARepeatUntilTheRequestsOwnWindowEnds()
        {
            var ring = new RemoteControlRing();
            DateTime end = Now.AddMinutes(10);
            ring.Put("entry", "ctrl", "r1", end, "{\"v\":1}");
            Assert.Equal("{\"v\":1}", ring.Find("entry", "ctrl", "r1", Now));
            Assert.Equal("{\"v\":1}", ring.Find("entry", "ctrl", "r1", end));
            Assert.Null(ring.Find("entry", "ctrl", "r1", end.AddTicks(1)));
            // per controller: another device's request of the same id is another request
            Assert.Null(ring.Find("entry", "other", "r1", Now));
        }

        [Fact]
        public void TheRingSurvivesItsOwnTextAndDropsOnlyEndedWindows()
        {
            var ring = new RemoteControlRing();
            ring.Put("entry", "ctrl", "old", Now.AddSeconds(-1), "a");
            ring.Put("entry", "ctrl", "live", Now.AddSeconds(500), RemoteControlRing.InterruptedAnswer);
            RemoteControlRing back = RemoteControlRing.Parse(ring.Serialize());
            Assert.Equal(2, back.Count);
            Assert.True(back.Prune(Now));
            Assert.Equal(1, back.Count);
            Assert.Equal(RemoteControlRing.InterruptedAnswer, back.Find("entry", "ctrl", "live", Now));
            Assert.Equal(0, RemoteControlRing.Parse("not json").Count);
        }

        [Fact]
        public void AFullRingRefusesNewRequestsInsteadOfDroppingOne()
        {
            var ring = new RemoteControlRing();
            for (int i = 0; i < RemoteControlRing.MaxLiveEntries; i++) ring.Put("e", "c", "r" + i, Now.AddMinutes(5), "a");
            Assert.False(ring.HasRoom(Now));
            Assert.True(ring.HasRoom(Now.AddMinutes(6)));
        }

        [Fact]
        public void TheInterruptedAnswerIsAFailedAnswerThatProvesNothing()
        {
            Assert.True(RemoteControlAnswer.TryReadOutcome(RemoteControlRing.InterruptedAnswer, out string status, out int exit));
            Assert.Equal("failed", status);
            Assert.Equal(AppCommandExit.NotProven, exit);
        }

        // ------------------------------------------------------------------ the answer (rule 5)

        [Fact]
        public void AnAnswerCarriesTheMembersOfRuleFive()
        {
            var answer = RemoteControlAnswer.From(AppCommandResponse.Done("Fertig.", new Dictionary<string, object> { ["paused"] = true }));
            var map = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(answer.ToJson());
            Assert.Equal(1, map["v"]);
            Assert.Equal("done", map["status"]);
            Assert.Equal(0, map["exit"]);
            Assert.False((bool)map["truncated"]);
            Assert.True((bool)((Dictionary<string, object>)map["data"])["paused"]);
            // the message is English for the log, never the interface language of the target
            Assert.Equal("Done.", map["message"]);
            Assert.False(map.ContainsKey("reason"));
        }

        [Fact]
        public void AnAnswerAboveTheBoundDropsItsDataAndSaysSo()
        {
            var big = new Dictionary<string, object> { ["text"] = new string('x', RemoteControl.MaxPayloadBytes) };
            string json = new RemoteControlAnswer { Status = "done", Data = big }.ToJson();
            Assert.True(Encoding.UTF8.GetByteCount(json) <= RemoteControl.MaxPayloadBytes);
            var map = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            Assert.True((bool)map["truncated"]);
            Assert.Empty((Dictionary<string, object>)map["data"]);
        }

        // ------------------------------------------------------------------ the grants (rules 9 and 10)

        [Fact]
        public void ANewlyTickedControllerGetsSafeAlwaysAndNothingElse()
        {
            RemoteControlGrant g = RemoteControlGrant.Ticked("entry", "ctrl", "Phone");
            Assert.Equal(new[] { "safe" }, g.Classes.Keys.ToArray());
            Assert.Equal(RemoteControl.ConsentAlways, g.ConsentFor("safe"));
            Assert.Null(g.ConsentFor("reads-user-data"));
        }

        [Fact]
        public void GrantsRoundTripAndAnUnknownClassOrConsentIsDropped()
        {
            var list = new List<RemoteControlGrant> { RemoteControlGrant.Ticked("e", "a", "A") };
            list[0].Classes["writes-user-data"] = RemoteControl.ConsentAsk;
            List<RemoteControlGrant> back = RemoteControlGrants.Parse(RemoteControlGrants.Serialize(list));
            Assert.Single(back);
            Assert.Equal(RemoteControl.ConsentAsk, back[0].ConsentFor("writes-user-data"));

            List<RemoteControlGrant> odd = RemoteControlGrants.Parse(
                "[{\"entry\":\"e\",\"deviceId\":\"a\",\"classes\":{\"safe\":\"sometimes\",\"admin\":\"always\"}},{\"entry\":\"e\"}]");
            Assert.Empty(odd);
            Assert.Empty(RemoteControlGrants.Parse("{"));
        }

        [Fact]
        public void AControllerThatLeftTheDirectoryOrChangedKeyLosesItsGrant()
        {
            var grants = new List<RemoteControlGrant>
            {
                RemoteControlGrant.Ticked("e", "kept", "K"),
                RemoteControlGrant.Ticked("e", "gone", "G"),
                RemoteControlGrant.Ticked("other", "gone", "G"),
            };
            Assert.True(RemoteControlGrants.DropGone(grants, "e", new List<string> { "kept" }));
            Assert.Equal(new[] { "e:kept", "other:gone" }, grants.Select(g => g.Entry + ":" + g.DeviceId).ToArray());
            Assert.False(RemoteControlGrants.DropGone(grants, "e", new List<string> { "kept" }));
        }

        // ------------------------------------------------------------------ the shipped manifest (rule 7)

        private static readonly string[] FirstSlice =
            { "commands", "version", "status", "show", "quit", "layout current", "layout list", "keepawake set", "hotkeys pause" };

        [Fact]
        public void TheShippedFirstSliceIsSafeAndConfirmedByTheGrant()
        {
            AppCommandManifest m = AppCommandManifest.Shipped;
            Assert.Equal("1.1", m.CliVersion);
            string[] remote = m.Commands.Where(c => c.RemoteConfirm != null).Select(c => c.Name).OrderBy(n => n).ToArray();
            Assert.Equal(FirstSlice.OrderBy(n => n).ToArray(), remote);
            foreach (AppCommandSpec spec in m.Commands.Where(c => c.RemoteConfirm != null))
            {
                Assert.Equal("safe", spec.Risk);
                Assert.Equal(RemoteControl.ConfirmGrant, spec.RemoteConfirm);
                Assert.False(spec.Interactive);
            }
        }

        [Fact]
        public void CommandsAskedRemotelyListOnlyTheRemoteEntriesAndTheirRemoteOptions()
        {
            Dictionary<string, object> view = AppCommandManifest.Shipped.RemoteView();
            var commands = ((object[])view["commands"]).Cast<Dictionary<string, object>>().ToList();
            Assert.Equal(FirstSlice.OrderBy(n => n).ToArray(), commands.Select(c => (string)c["name"]).OrderBy(n => n).ToArray());
            foreach (Dictionary<string, object> c in commands)
                foreach (Dictionary<string, object> a in ((object[])c["args"]).Cast<Dictionary<string, object>>())
                {
                    Assert.NotEqual("json", a["name"]);
                    Assert.NotEqual("result", a["name"]);
                    Assert.NotEqual("path", a["kind"]);
                }
            Assert.Equal("1.1", view["cliVersion"]);
            Assert.True(Encoding.UTF8.GetByteCount(new JavaScriptSerializer().Serialize(view)) < RemoteControl.MaxPayloadBytes);
        }

        [Fact]
        public void StatusNamesTheSwitchOfRuleEight()
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                Encoding.UTF8.GetString(AppCommandManifest.Shipped.Raw).TrimStart('﻿'));
            Dictionary<string, object> status = ((object[])root["commands"]).Cast<Dictionary<string, object>>().Single(c => (string)c["name"] == "status");
            Assert.Contains(((object[])status["returns"]).Cast<Dictionary<string, object>>(), r => (string)r["name"] == "remoteCommands");
        }

        // ------------------------------------------------------------------ ids and the log line

        [Theory]
        [InlineData("a", true)]
        [InlineData("req_01-AB", true)]
        [InlineData("", false)]
        [InlineData("has space", false)]
        [InlineData("zh中文", false)]
        public void ARequestIdIsOneToSixtyFourOfTheGivenCharacters(string id, bool ok)
            => Assert.Equal(ok, RemoteControl.IsRequestId(id));

        [Fact]
        public void ARequestIdOfSixtyFiveCharactersIsRefused()
        {
            Assert.True(RemoteControl.IsRequestId(new string('a', 64)));
            Assert.False(RemoteControl.IsRequestId(new string('a', 65)));
        }

        [Fact]
        public void TheContextsAreTheCarriersOwn()
        {
            Assert.Equal("command:r1", RemoteControl.RequestContext("r1"));
            Assert.Equal("command-result:r1", RemoteControl.ResultContext("r1"));
        }

        [Fact]
        public void ALogFieldHoldsNoSeparatorAndNoControlCharacter()
        {
            Assert.Equal("Phone A", RemoteCommandsLog.Clean("Pho|ne\r\n A", 40));
            Assert.Equal("?", RemoteCommandsLog.Clean("\u0007", 40));
            Assert.Equal("abc", RemoteCommandsLog.Clean("abcdef", 3));
        }
    }
}
