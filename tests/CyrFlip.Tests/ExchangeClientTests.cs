using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The client of the wire against a scripted in-memory server (<see cref="FakeExchangeServer"/>): the trust
    /// step before a password, the enroll, the hello, the control stream and the two data streams, and the codes
    /// every failure comes back as.
    /// </summary>
    [Collection("ExchangeSecrets")]
    public class ExchangeClientTests : IDisposable
    {
        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly FakeExchangeServer _server = new FakeExchangeServer();
        private readonly FakeAccount _alice;

        public ExchangeClientTests()
        {
            ExchangeSecrets.Use(_secrets);
            _alice = _server.AddAccount("alice");
        }

        public void Dispose() => ExchangeSecrets.Use(null);

        private ExchangeClient Client() => new ExchangeClient(_server, "26.10.16.1200");

        private static ExchangeEnrollRequest Request(ExchangePin pin, string password = "Passw0rdTest") => new ExchangeEnrollRequest
        {
            Host = "nas.example.org", Port = 22022, Pin = pin, Login = "alice", Password = password,
            DeviceId = ExchangeEnrollment.NewDeviceId(), DeviceName = "PC", DeviceKem = "BAAA",
        };

        [Fact]
        public async Task TheProbeSendsNothingAndReportsWhatTheCertificateIs()
        {
            ExchangeOutcome<ExchangeTrustResult> probe = await Client().ProbeAsync("nas.example.org", 22022, ExchangePin.None, CancellationToken.None);
            Assert.True(probe.Ok);
            Assert.Equal(ExchangeTrustKind.NeedsAcceptance, probe.Value!.Kind); // the fake server is self-signed
            Assert.Equal(_server.Leaf, probe.Value.ObservedLeaf);
            Assert.Empty(_server.Seen); // not a byte of an envelope (item BQ)
        }

        [Fact]
        public async Task APasswordNeverReachesAServerWhoseCertificateWasNotAccepted()
        {
            ExchangeOutcome<ExchangeWelcome> r = await Client().EnrollAsync(Request(ExchangePin.None), CancellationToken.None);
            Assert.False(r.Ok);
            Assert.Equal(ExchangeCodes.CertificateUntrusted, r.Code);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task APinForAnotherKeyRefusesBeforeAnythingIsSentAndCarriesBothFingerprints()
        {
            ExchangeOutcome<ExchangeWelcome> r = await Client().EnrollAsync(Request(new ExchangePin("SHA256:other", "SHA256:otherKey")), CancellationToken.None);
            Assert.Equal(ExchangeCodes.CertificateChanged, r.Code);
            Assert.NotNull(r.Trust);
            Assert.Equal(_server.Leaf, r.Trust!.ObservedLeaf);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task EnrollWithTheAcceptedPinReturnsTheTokenAndWhatTheServerSaid()
        {
            ExchangeOutcome<ExchangeWelcome> r = await Client().EnrollAsync(Request(new ExchangePin(_server.Leaf, _server.Key)), CancellationToken.None);
            Assert.True(r.Ok);
            Assert.StartsWith("fake-token-", r.Value!.DeviceToken);
            Assert.Equal("alice", r.Value.Account);
            Assert.True(r.Value.Has("clipboard"));
            Assert.True(r.Value.Has("sealed"));
            Assert.Equal(new[] { "enroll" }, _server.Seen);
        }

        [Fact]
        public async Task AWrongPasswordIsBadCredentialsAndAnEmptyOneNeverLeavesTheProgram()
        {
            var pin = new ExchangePin(_server.Leaf, _server.Key);
            Assert.Equal(ExchangeCodes.BadCredentials, (await Client().EnrollAsync(Request(pin, "nope"), CancellationToken.None)).Code);
            _server.Seen.Clear();

            Assert.Equal(ExchangeCodes.EmptyCredential, (await Client().EnrollAsync(Request(pin, ""), CancellationToken.None)).Code);
            ExchangeEnrollRequest noLogin = Request(pin);
            noLogin.Login = "";
            Assert.Equal(ExchangeCodes.EmptyCredential, (await Client().EnrollAsync(noLogin, CancellationToken.None)).Code);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task APasswordOverTheBoundsOfTheWireIsRefusedLocally()
        {
            var pin = new ExchangePin(_server.Leaf, _server.Key);
            Assert.Equal(ExchangeCodes.BadCredentials, (await Client().EnrollAsync(Request(pin, new string('x', 1025)), CancellationToken.None)).Code);
            Assert.Equal(ExchangeCodes.BadCredentials, (await Client().EnrollAsync(Request(pin, "pass\nword"), CancellationToken.None)).Code);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task AnUnreachableServerIsUnreachableAndASilentOneIsNotAnsweredNeverTheOtherWay()
        {
            _server.Unreachable = true;
            Assert.Equal(ExchangeCodes.Unreachable, (await Client().EnrollAsync(Request(new ExchangePin(_server.Leaf, _server.Key)), CancellationToken.None)).Code);
            _server.Unreachable = false;

            // a stream that closes with no frame after our own envelope: "did not answer" (item AN)
            var silent = new SilentServer();
            ExchangeOutcome<ExchangeWelcome> r = await new ExchangeClient(silent, "1").EnrollAsync(Request(ExchangePin.None), CancellationToken.None);
            Assert.Equal(ExchangeCodes.Unanswered, r.Code);
        }

        [Fact]
        public async Task OpenSaysHelloWithTheStoredTokenAndKeepsWhatWelcomeSaid()
        {
            ExchangeAccountEntry entry = await Enrolled();
            ExchangeOutcome<ExchangeSession> opened = await Client().OpenAsync(entry, CancellationToken.None);
            Assert.True(opened.Ok);
            using (opened.Value)
            {
                Assert.Equal("member", opened.Value!.Welcome.AccountKind);
                Assert.True(opened.Value.Welcome.Has("sealed"));
            }
            Assert.Equal(new[] { "hello" }, _server.Seen.Take(1));
        }

        [Fact]
        public async Task AnEntryWithNoTokenIsBadCredentialsWithoutTouchingTheNetwork()
        {
            ExchangeAccountEntry entry = await Enrolled();
            entry.DeleteSecret();
            _server.Seen.Clear();
            Assert.Equal(ExchangeCodes.BadCredentials, (await Client().OpenAsync(entry, CancellationToken.None)).Code);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task ARevokedDeviceAndAWrongTokenAreToldApart()
        {
            ExchangeAccountEntry entry = await Enrolled();
            entry.SaveSecret(new ExchangeSecret { Token = "forged", DevicePrivate = entry.LoadSecret()!.DevicePrivate });
            Assert.Equal(ExchangeCodes.BadCredentials, (await Client().OpenAsync(entry, CancellationToken.None)).Code);
        }

        [Fact]
        public async Task ListReturnsTheDevicesWithTheirKeysAndCapabilities()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-id-0123456789ab", "Phone");
            _server.AddDevice(_alice, "watch-id-0123456789ab", "Old tablet", capable: false, withKey: false);
            ExchangeAccountEntry entry = await Enrolled();
            using (ExchangeSession s = (await Client().OpenAsync(entry, CancellationToken.None)).Value!)
            {
                ExchangeOutcome<List<ExchangePeer>> list = await Client().ListAsync(s, CancellationToken.None);
                Assert.True(list.Ok);
                ExchangePeer p = list.Value!.Single(x => x.DeviceId == phone.DeviceId);
                Assert.True(p.Capable);
                Assert.Equal(phone.Kem, p.Kem);
                Assert.True(p.UpdatedAtUtcTicks > 0);
                ExchangePeer old = list.Value.Single(x => x.Name == "Old tablet");
                Assert.False(old.Capable);
                Assert.Equal("", old.Kem);
            }
        }

        [Fact]
        public async Task InfoOfAnEmptyClipboardIsNullNotAFailure()
        {
            ExchangeAccountEntry entry = await Enrolled();
            using (ExchangeSession s = (await Client().OpenAsync(entry, CancellationToken.None)).Value!)
            {
                ExchangeOutcome<ExchangeValueHeader?> info = await Client().InfoAsync(s, CancellationToken.None);
                Assert.True(info.Ok);
                Assert.Null(info.Value);
            }
        }

        [Fact]
        public async Task APutThatTheServerRefusesBeforeAByteFlowsReportsTheReasonAndWritesNothing()
        {
            _alice.ClipboardBytes = 10;
            ExchangeAccountEntry entry = await Enrolled();
            bool wrote = false;
            ExchangeOutcome<ExchangeValueHeader> put = await Client().PutSealedAsync(entry, "{\"v\":1}", 11, _ => wrote = true, w => null, CancellationToken.None);
            Assert.False(put.Ok);
            Assert.Equal(ExchangeCodes.LimitReached, put.Code);
            Assert.False(wrote);
        }

        [Fact]
        public async Task APutAsksTheServerAboutItsFeaturesBeforeAnythingIsSent()
        {
            ExchangeAccountEntry entry = await Enrolled();
            bool wrote = false;
            ExchangeOutcome<ExchangeValueHeader> put = await Client().PutSealedAsync(entry, "{\"v\":1}", 5, _ => wrote = true, w => ExchangeCodes.ServerNoE2e, CancellationToken.None);
            Assert.Equal(ExchangeCodes.ServerNoE2e, put.Code);
            Assert.False(wrote);
            Assert.DoesNotContain("clipboard-put", _server.Seen);
        }

        [Fact]
        public async Task APutWritesExactlyTheDeclaredBytesAndIsStoredOnlyOnTheStoredFrame()
        {
            ExchangeAccountEntry entry = await Enrolled();
            byte[] bytes = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
            ExchangeOutcome<ExchangeValueHeader> put = await Client().PutSealedAsync(entry, "{\"v\":1,\"from\":\"x\"}", bytes.Length, s => s.Write(bytes, 0, bytes.Length), w => null, CancellationToken.None);
            Assert.True(put.Ok);
            Assert.Equal(FakeExchangeServer.Sha(bytes), put.Value!.Sha256);
            Assert.NotEqual("", put.Value.ClipId);
            Assert.Equal(bytes, _alice.SlotBytes);
            Assert.Equal("sealed", _alice.SlotKind);
        }

        [Fact]
        public async Task AShortPutIsNeverStoredAndTheOldValueStays()
        {
            _alice.SlotBytes = new byte[] { 9, 9, 9 };
            _alice.SlotClipId = "old";
            ExchangeAccountEntry entry = await Enrolled();
            byte[] bytes = new byte[10];
            ExchangeOutcome<ExchangeValueHeader> put = await Client().PutSealedAsync(entry, "{\"v\":1}", 100, s =>
            {
                s.Write(bytes, 0, 10);
                throw new EndOfStreamException("the source ended before its declared size"); // what SealStream does
            }, w => null, CancellationToken.None);
            // the stream is closed with 90 bytes owed: the server drops the part and never sends clipboard-stored
            Assert.False(put.Ok);
            await Task.Delay(100);
            Assert.Equal(new byte[] { 9, 9, 9 }, _alice.SlotBytes);
            Assert.Equal("old", _alice.SlotClipId);
        }

        [Fact]
        public async Task AGetReadsExactlyTheDeclaredBytesAndAnEmptyClipboardIsNotAFailure()
        {
            ExchangeAccountEntry entry = await Enrolled();
            ExchangeOutcome<ExchangeValueHeader?> empty = await Client().GetAsync(entry, null, w => null, (h, s) => "never", CancellationToken.None);
            Assert.True(empty.Ok);
            Assert.Null(empty.Value);

            byte[] bytes = Enumerable.Range(0, 300).Select(i => (byte)(i * 7)).ToArray();
            _alice.SlotBytes = bytes; _alice.SlotBox = ""; _alice.SlotKind = "sealed"; _alice.SlotClipId = "c1"; _alice.SlotStoredAt = "2026-10-07T12:00:00Z";
            byte[]? got = null;
            ExchangeOutcome<ExchangeValueHeader?> value = await Client().GetAsync(entry, null, w => null, (h, s) =>
            {
                got = new byte[h.Size];
                int have = 0;
                while (have < got.Length) { int n = s.Read(got, have, got.Length - have); if (n == 0) return "short"; have += n; }
                return null;
            }, CancellationToken.None);
            Assert.True(value.Ok);
            Assert.Equal(bytes, got);
            Assert.Equal("c1", value.Value!.ClipId);
        }

        [Fact]
        public async Task AGetNamingAReplacedClipIdIsUnavailable()
        {
            ExchangeAccountEntry entry = await Enrolled();
            _alice.SlotBytes = new byte[] { 1 }; _alice.SlotClipId = "now"; _alice.SlotKind = "sealed";
            ExchangeOutcome<ExchangeValueHeader?> r = await Client().GetAsync(entry, "before", w => null, (h, s) => null, CancellationToken.None);
            Assert.Equal(ExchangeCodes.Unavailable, r.Code);
        }

        [Fact]
        public async Task ACancelledOperationIsCancelledNotUnreachable()
        {
            ExchangeAccountEntry entry = await Enrolled();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                ExchangeOutcome<ExchangeSession> r = await Client().OpenAsync(entry, cts.Token);
                Assert.Equal(ExchangeCodes.Cancelled, r.Code);
            }
        }

        [Fact]
        public async Task RevokeRemovesTheDeviceOnTheServerOnABestEffortBasis()
        {
            ExchangeAccountEntry entry = await Enrolled();
            Assert.True(await Client().RevokeAsync(entry, CancellationToken.None));
            await WaitFor(() => _alice.Devices.All(d => d.DeviceId != entry.DeviceId));
            _server.Unreachable = true;
            Assert.False(await Client().RevokeAsync(entry, CancellationToken.None));
        }

        // ---------------------------------------------------------------- helpers

        private async Task<ExchangeAccountEntry> Enrolled()
        {
            ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(Client(), new ExchangeJoinRequest
            {
                Host = "nas.example.org", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key),
                Login = "alice", Password = "Passw0rdTest", DeviceName = "PC",
            }, null, CancellationToken.None);
            Assert.True(joined.Ok, joined.Code);
            _server.Seen.Clear();
            return joined.Value!;
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
            Assert.True(condition());
        }

        /// <summary>A server that completes the handshake and then closes without a word.</summary>
        private sealed class SilentServer : IExchangeTransport
        {
            public Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct)
            {
                DuplexEnd.Pair(out DuplexEnd client, out DuplexEnd server);
                server.Close2();
                ExchangeTrustResult trust = ExchangeTrust.Evaluate(ExchangePin.None, "L", "K", SslPolicyErrors.None);
                return Task.FromResult(new ExchangeConnection(client, () => client.Close2(), trust, ct, idleMs: 0));
            }
        }
    }
}
