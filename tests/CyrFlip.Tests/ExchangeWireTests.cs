using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The framing and the envelopes of the exchange wire (<c>DEVICE-EXCHANGE</c> 7.1, 7.2, items BW, BY, DN), replayed
    /// against the transcripts of the real server corpus (FMS_Win ticket 234, four of its transcripts under
    /// <c>Fixtures/DeviceExchange/fmsw-234-clipboard.json</c>). A frame the server wrote must decode; a frame this
    /// program writes must be what the server's transcript shows for the same message.
    /// </summary>
    public class ExchangeWireTests
    {
        private static readonly string Fixtures = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures", "DeviceExchange"));

        // The extracted transcripts are held by the hash of their bytes: a re-issue of the corpus is a deliberate edit.
        private const string TranscriptsSha256 = "449c4fc07e0d48ecab0c708f89d6e3230ed7a8d2f33f0c0eee74aa3c372617d0";

        private sealed class Step
        {
            public string Transcript = "";
            public string Stream = "";
            public bool ClientToServer;
            public string Json = "";
            public byte[] Hex = Array.Empty<byte>();
        }

        private static List<Step> Steps()
        {
            string path = Path.Combine(Fixtures, "fmsw-234-clipboard.json");
            string text = File.ReadAllText(path, Encoding.UTF8);
            var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);
            var steps = new List<Step>();
            foreach (Dictionary<string, object> t in ((object[])root["transcripts"]).Cast<Dictionary<string, object>>())
            {
                foreach (Dictionary<string, object> s in ((object[])t["steps"]).Cast<Dictionary<string, object>>())
                {
                    if (!s.TryGetValue("json", out object? json) || !(json is string js) || js.Length == 0) continue; // raw byte runs are not frames
                    steps.Add(new Step
                    {
                        Transcript = (string)t["name"],
                        Stream = (string)s["stream"],
                        ClientToServer = (string)s["direction"] == "client->server",
                        Json = js,
                        Hex = FromHex((string)s["hex"]),
                    });
                }
            }
            return steps;
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        [Fact]
        public void TheTranscriptsAreTheBytesTheCorpusHeld()
        {
            using (SHA256 sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Fixtures, "fmsw-234-clipboard.json")))).Replace("-", "").ToLowerInvariant();
                Assert.Equal(TranscriptsSha256, hash);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task EveryFrameTheServerWroteDecodesAndHasItsLengthRight()
        {
            List<Step> steps = Steps();
            Assert.True(steps.Count > 60, "the transcripts hold well over sixty frames");
            foreach (Step step in steps)
            {
                // the whole frame as it crossed the stream: 4-byte big-endian length, then the body
                ExchangeFrame? read = await ExchangeWire.ReadFrameAsync(new MemoryStream(step.Hex), authenticated: true, CancellationToken.None);
                Assert.NotNull(read);
                ExchangeFrame body = ExchangeWire.Decode(Encoding.UTF8.GetBytes(step.Json));
                Assert.Equal(body.Type, read!.Type);
                Assert.Equal(2, body.Long("schemaVersion"));
            }
        }

        [Fact]
        public void TheFramesWithNoMembersBeyondTheTypeAreByteForByteTheServersOwn()
        {
            // list, clipboard-info and clipboard-get without a clipId carry nothing but schemaVersion and type.
            var mine = new Dictionary<string, byte[]>
            {
                ["list"] = ExchangeWire.Encode(ExchangeMembers.List()),
                ["clipboard-info"] = ExchangeWire.Encode(ExchangeMembers.ClipboardInfo()),
                ["clipboard-get"] = ExchangeWire.Encode(ExchangeMembers.ClipboardGet(null)),
            };
            int matched = 0;
            foreach (Step step in Steps().Where(s => s.ClientToServer && mine.ContainsKey(ExchangeWire.Decode(Encoding.UTF8.GetBytes(s.Json)).Type)))
            {
                ExchangeFrame f = ExchangeWire.Decode(Encoding.UTF8.GetBytes(step.Json));
                if (f.Has("clipId")) continue;
                Assert.Equal(step.Hex, mine[f.Type]);
                matched++;
            }
            Assert.True(matched >= 3, "each of the three appears in the transcripts");
        }

        [Fact]
        public void AClipIdGetCarriesTheIdAndNothingElse()
        {
            Step stale = Steps().First(s => s.ClientToServer && s.Json.Contains("\"clipboard-get\"") && s.Json.Contains("clipId"));
            ExchangeFrame vector = ExchangeWire.Decode(Encoding.UTF8.GetBytes(stale.Json));
            byte[] mine = ExchangeWire.Encode(ExchangeMembers.ClipboardGet(vector.Str("clipId")));
            Assert.Equal(stale.Hex, mine);
        }

        [Fact]
        public void HelloCarriesTheMembersOfTheServersTranscriptAndNoMore()
        {
            Step hello = Steps().First(s => s.ClientToServer && s.Json.Contains("\"hello\""));
            ExchangeFrame vector = ExchangeWire.Decode(Encoding.UTF8.GetBytes(hello.Json));
            ExchangeFrame mine = ExchangeWire.Decode(ExchangeWire.Encode(
                ExchangeMembers.Hello(vector.Str("deviceId"), vector.Str("deviceToken"), vector.Str("productVersion"))).Skip(4).ToArray());
            Assert.Equal(vector.Str("deviceId"), mine.Str("deviceId"));
            Assert.Equal(vector.Str("deviceToken"), mine.Str("deviceToken"));
            Assert.Equal(vector.Str("productVersion"), mine.Str("productVersion"));
            Assert.Equal(2, mine.Long("schemaVersion"));
            Assert.False(mine.Has("keepaliveSeconds")); // optional (item F); this program proposes none
        }

        [Fact]
        public void ASealedPutIsTheKindSealedTheSealedSizeAndTheBoxAndNothingInClear()
        {
            Step put = Steps().First(s => s.Transcript == "sealed-clipboard" && s.ClientToServer && s.Json.Contains("\"clipboard-put\""));
            var vector = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(put.Json);
            string boxJson = new JavaScriptSerializer().Serialize(vector["sealed"]);
            long size = Convert.ToInt64(vector["size"]);

            byte[] frame = ExchangeWire.Encode(ExchangeMembers.ClipboardPutSealed(size, boxJson));
            var mine = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(frame, 4, frame.Length - 4));

            Assert.Equal("clipboard-put", mine["type"]);
            Assert.Equal("sealed", mine["kind"]);
            Assert.Equal(size, Convert.ToInt64(mine["size"]));
            Assert.False(mine.ContainsKey("mimeType"));
            Assert.False(mine.ContainsKey("name"));
            // the box is passed through untouched: the same members, the same values
            Assert.True(SameValue(vector["sealed"], mine["sealed"]));
            // and the vector holds no member that this frame lacks
            Assert.Empty(vector.Keys.Except(mine.Keys));
        }

        [Fact]
        public void EnrollNamesTheMembersTheContractAsksOfThisProduct()
        {
            byte[] frame = ExchangeWire.Encode(ExchangeMembers.Enroll("alice", "pw", "L866lMziLsSZZSADAoyL9A", "PC", "26.10.16.1", "BAAA"));
            ExchangeFrame f = ExchangeWire.Decode(frame.Skip(4).ToArray());
            Assert.Equal("enroll", f.Type);
            Assert.Equal("cyrflip-windows", f.Str("product"));
            Assert.Equal(new[] { "clipboard" }, f.Strings("roles"));
            Assert.Equal(new[] { "e2e-v1" }, f.Strings("capabilities"));
            Assert.Equal(1, f.Obj("e2eKey")!.Long("v"));
            Assert.Equal("BAAA", f.Obj("e2eKey")!.Str("kem"));
            Assert.Equal("windows", f.Str("platform"));
            Assert.True(frame.Length - 4 <= ExchangeWire.MaxFramePreAuth);
        }

        [Fact]
        public void AFrameOfZeroLengthOrOverTheLimitInForceClosesTheStream()
        {
            Assert.Throws<ExchangeProtocolException>(() => Read(new byte[] { 0, 0, 0, 0 }, authenticated: true));
            Assert.Throws<ExchangeProtocolException>(() => Read(Header(ExchangeWire.MaxFramePreAuth + 1), authenticated: false));
            Assert.Throws<ExchangeProtocolException>(() => Read(Header(ExchangeWire.MaxFramePostAuth + 1), authenticated: true));
        }

        [Fact]
        public void TheLimitFollowsTheStreamsState()
        {
            // 20000 bytes of JSON: refused before authentication, accepted after (item T)
            string pad = new string('x', 20000);
            byte[] body = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"type\":\"x\",\"pad\":\"" + pad + "\"}");
            byte[] framed = Header(body.Length).Concat(body).ToArray();
            Assert.Throws<ExchangeProtocolException>(() => Read(framed, authenticated: false));
            ExchangeFrame? ok = Read(framed, authenticated: true);
            Assert.Equal("x", ok!.Type);
        }

        [Fact]
        public void ACleanEndBetweenFramesIsNullAndAnEndInsideOneIsAnError()
        {
            Assert.Null(Read(new byte[0], authenticated: true));
            Assert.Throws<ExchangeProtocolException>(() => Read(new byte[] { 0, 0 }, authenticated: true));
            Assert.Throws<ExchangeProtocolException>(() => Read(Header(10).Concat(new byte[] { 1, 2, 3 }).ToArray(), authenticated: true));
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        [InlineData("not json")]
        public void ABodyThatIsNotOneObjectIsRefused(string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            Assert.Throws<ExchangeProtocolException>(() => Read(Header(bytes.Length).Concat(bytes).ToArray(), authenticated: true));
        }

        [Fact]
        public void AnUnknownMemberIsIgnoredNotAnError()
        {
            ExchangeFrame f = ExchangeWire.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"type\":\"welcome\",\"account\":\"a\",\"future\":{\"x\":1}}"));
            Assert.Equal("a", f.Str("account"));
            Assert.Equal("", f.Str("missing"));
            Assert.Equal(-1, f.Long("missing"));
        }

        [Fact]
        public void TheWriterEscapesWhatJsonDemandsAndLeavesTextAlone()
        {
            byte[] frame = ExchangeWire.Encode(new ExchangeMembers("x").Add("s", "a\"b\\c\nd<é>'"));
            string body = Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
            Assert.Contains("\\\"", body);
            Assert.Contains("\\\\", body);
            Assert.Contains("\\n", body);
            Assert.Contains("é", body);          // UTF-8 left as it is
            Assert.Contains("<", body);          // no HTML escaping
            ExchangeFrame back = ExchangeWire.Decode(frame.Skip(4).ToArray());
            Assert.Equal("a\"b\\c\nd<é>'", back.Str("s"));
        }

        [Fact]
        public void EveryWelcomeAndEnrolledOfTheTranscriptsParses()
        {
            int parsed = 0;
            foreach (Step step in Steps().Where(s => !s.ClientToServer))
            {
                ExchangeFrame f = ExchangeWire.Decode(Encoding.UTF8.GetBytes(step.Json));
                if (f.Type != "welcome" && f.Type != "enrolled") continue;
                ExchangeWelcome w = ExchangeWelcome.From(f);
                Assert.Contains("clipboard", w.Features);
                Assert.True(w.AccountKind == "member" || w.AccountKind == "guest");
                Assert.Equal(f.Type == "enrolled", w.DeviceToken.Length > 0);
                parsed++;
            }
            Assert.True(parsed > 10);
        }

        [Fact]
        public void TheStateAndValueHeadersOfTheTranscriptsParse()
        {
            ExchangeFrame state = Steps().Where(s => !s.ClientToServer).Select(s => ExchangeWire.Decode(Encoding.UTF8.GetBytes(s.Json)))
                .First(f => f.Type == "clipboard-state" && f.Obj("value") != null && f.Obj("value")!.Str("kind") == "sealed");
            ExchangeValueHeader h = ExchangeValueHeader.From(state.Obj("value")!);
            Assert.Equal("sealed", h.Kind);
            Assert.True(h.Size > 0);
            Assert.Equal(64, h.Sha256.Length);
            Assert.True(h.ClipId.Length >= 20);
            Assert.NotEqual("", h.StoredAt);
            Assert.StartsWith("{", h.BoxJson);
            Assert.Contains("\"fromKey\"", h.BoxJson);
        }

        private static byte[] Header(int length) => new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };

        private static ExchangeFrame? Read(byte[] bytes, bool authenticated)
            => ExchangeWire.ReadFrameAsync(new MemoryStream(bytes), authenticated, CancellationToken.None).GetAwaiter().GetResult();

        internal static bool SameValue(object? a, object? b)
        {
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db)
                return da.Count == db.Count && da.All(kv => db.TryGetValue(kv.Key, out object? other) && SameValue(kv.Value, other));
            if (a is object[] aa && b is object[] ab)
                return aa.Length == ab.Length && aa.Zip(ab, SameValue).All(x => x);
            if (a is string sa && b is string sb) return sa == sb;
            if (a == null || b == null) return a == b;
            return Convert.ToDecimal(a) == Convert.ToDecimal(b);
        }
    }
}
