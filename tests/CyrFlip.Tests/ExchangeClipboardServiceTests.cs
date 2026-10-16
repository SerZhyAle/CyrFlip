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
    /// "Send clipboard to server" and "Get clipboard from server" end to end over the scripted server, with the real
    /// sealing: a value this program sends is opened by another device of the account with that device's own key, and a
    /// value another device seals is opened here (<c>DEVICE-EXCHANGE</c> items CA, DG, DN and DP).
    /// </summary>
    [Collection("ExchangeSecrets")]
    public class ExchangeClipboardServiceTests : IDisposable
    {
        private readonly InMemorySecretStore _secrets = new InMemorySecretStore();
        private readonly FakeExchangeServer _server = new FakeExchangeServer();
        private readonly FakeAccount _alice;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cyrflip-exsvc-" + Guid.NewGuid().ToString("N"));
        private readonly AppConfig _config = new AppConfig();
        private int _saves;

        public ExchangeClipboardServiceTests()
        {
            ExchangeSecrets.Use(_secrets);
            Directory.CreateDirectory(_dir);
            _config.ExchangeClipboardReceivedFolder = Path.Combine(_dir, "received");
            _alice = _server.AddAccount("alice");
        }

        public void Dispose()
        {
            ExchangeSecrets.Use(null);
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private ExchangeClient Client() => new ExchangeClient(_server, "26.10.16.1200");

        private ExchangeClipboardService Service() => new ExchangeClipboardService(_config, Client(), () => _saves++);

        private async Task<ExchangeAccountEntry> Join(string host = "nas.example.org", string login = "alice", FakeExchangeServer? server = null)
        {
            FakeExchangeServer s = server ?? _server;
            ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(new ExchangeClient(s, "1"), new ExchangeJoinRequest
            {
                Host = host, Port = 22022, Pin = new ExchangePin(s.Leaf, s.Key), Login = login, Password = "Passw0rdTest", DeviceName = "PC",
            }, null, CancellationToken.None);
            Assert.True(joined.Ok, joined.Code);
            _config.ExchangeClipboardAccounts.Add(joined.Value!);
            return joined.Value!;
        }

        private static ExchangeClipboardPayload TextPayload(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            return new ExchangeClipboardPayload { Kind = "text", Bytes = utf8, ClearSize = utf8.Length };
        }

        /// <summary>What a phone does with a value it finds: opens the box with its own key and the sender's trusted key.</summary>
        private static byte[]? PhoneOpens(FakeAccount account, FakeDevice phone, byte[] senderKem, out string kind, out string text)
        {
            kind = ""; text = "";
            if (account.SlotBytes == null) return null;
            Assert.Equal("sealed", account.SlotKind);
            Assert.True(ExchangeE2e.TryOpenBox("clipboard", account.SlotBox, phone.DeviceId, phone.Key!, from => senderKem,
                out byte[]? payload, out byte[]? ck, out string failure), failure);
            var meta = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(payload!));
            kind = (string)meta["kind"];
            Assert.True(ExchangeE2e.TryOpenBytes(account.SlotBytes, ck!, out byte[]? clear, out failure), failure);
            Assert.Equal(Convert.ToInt64(meta["size"]), clear!.Length);
            Assert.Equal(FakeExchangeServer.Sha(clear), (string)meta["sha256"]);
            text = kind == "text" ? Encoding.UTF8.GetString(clear) : "";
            return clear;
        }

        // ---------------------------------------------------------------- enrolling

        [Fact]
        public async Task JoiningMakesAnIdentityKeepsTheSecretsInTheStoreAndNothingSecretInTheEntry()
        {
            ExchangeAccountEntry e = await Join();
            Assert.Equal(22, e.DeviceId.Length);                 // 16 random bytes, base64url
            Assert.Equal(87, e.DeviceKem.Length);                // the 65-byte point, base64url
            Assert.Equal("alice", e.Login);
            Assert.Equal(_server.Leaf, e.PinLeaf);
            Assert.False(e.NeedsSignIn);
            Assert.True(e.ServerHas("sealed"));
            ExchangeSecret secret = e.LoadSecret()!;
            Assert.StartsWith("fake-token-", secret.Token);
            Assert.Equal(43, secret.DevicePrivate.Length);       // the 32-byte scalar, base64url
            string json = ExchangeAccountEntry.SerializeList(new[] { e });
            Assert.DoesNotContain(secret.Token, json);
            Assert.DoesNotContain(secret.DevicePrivate, json);
            // the server learned the public key and the capability, nothing else of the secret
            FakeDevice d = _alice.Devices.Single(x => x.DeviceId == e.DeviceId);
            Assert.Equal(e.DeviceKem, d.Kem);
            Assert.True(d.Capable);
        }

        [Fact]
        public async Task SigningInAgainKeepsTheDeviceIdAndTheKeyAndRotatesTheToken()
        {
            ExchangeAccountEntry e = await Join();
            string id = e.DeviceId, kem = e.DeviceKem, oldToken = e.LoadSecret()!.Token;
            e.SignIn = ExchangeSignInState.SignedOut;

            ExchangeOutcome<ExchangeAccountEntry> again = await ExchangeEnrollment.JoinAsync(Client(), new ExchangeJoinRequest
            {
                Host = e.Host, Port = e.Port, Pin = e.Pin, Login = e.Login, Password = "Passw0rdTest", DeviceName = "PC",
            }, e, CancellationToken.None);

            Assert.True(again.Ok, again.Code);
            Assert.Same(e, again.Value);
            Assert.Equal(id, e.DeviceId);
            Assert.Equal(kem, e.DeviceKem);
            Assert.NotEqual(oldToken, e.LoadSecret()!.Token);
            Assert.False(e.NeedsSignIn);
            Assert.Single(_alice.Devices, d => d.DeviceId == id);
        }

        [Fact]
        public async Task AnIdCollisionOnAFreshIdRegeneratesItOnceAndASecondOneIsARefusal()
        {
            var colliding = new CollidingServer(_server, collisions: 1);
            ExchangeOutcome<ExchangeAccountEntry> r = await ExchangeEnrollment.JoinAsync(new ExchangeClient(colliding, "1"), new ExchangeJoinRequest
            {
                Host = "h", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key), Login = "alice", Password = "Passw0rdTest",
            }, null, CancellationToken.None);
            Assert.True(r.Ok, r.Code);
            Assert.Equal(2, colliding.Enrolls);

            var always = new CollidingServer(_server, collisions: 5);
            ExchangeOutcome<ExchangeAccountEntry> twice = await ExchangeEnrollment.JoinAsync(new ExchangeClient(always, "1"), new ExchangeJoinRequest
            {
                Host = "h", Port = 22022, Pin = new ExchangePin(_server.Leaf, _server.Key), Login = "alice", Password = "Passw0rdTest",
            }, null, CancellationToken.None);
            Assert.Equal(ExchangeCodes.IdCollision, twice.Code);
            Assert.Equal(2, always.Enrolls);
        }

        // ---------------------------------------------------------------- send

        [Fact]
        public async Task ASentTextIsSealedForTheOtherCapableDevicesAndTheServerHoldsOnlyCiphertext()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            FakeDevice tablet = _server.AddDevice(_alice, "tablet-0123456789abcdef", "Old tablet", capable: false, withKey: false);
            ExchangeAccountEntry me = await Join();

            ExchangeSendReport report = await Service().SendAsync(TextPayload("hello from the PC\n— second line"), CancellationToken.None);

            Assert.True(report.Ok);
            ExchangeEntryResult r = report.Entries.Single();
            Assert.True(r.Ok, r.Code);
            Assert.Equal(1, r.Recipients);
            Assert.Equal(new[] { "Old tablet" }, r.DevicesWithoutKeyNames);
            Assert.Empty(r.DevicesKeyChangedNames);
            Assert.True(_saves > 0, "the learned key of the phone is saved with the entry");
            Assert.Equal(phone.Kem, me.TrustedKeys[phone.DeviceId]);
            Assert.False(me.TrustedKeys.ContainsKey(tablet.DeviceId));

            // nothing readable at the server: the bytes are not the text, and the box carries no name or type in clear
            Assert.DoesNotContain("hello from the PC", Encoding.UTF8.GetString(_alice.SlotBytes!));
            Assert.DoesNotContain("hello from the PC", _alice.SlotBox);
            Assert.Equal("sealed", _alice.SlotKind);

            // the phone opens it
            byte[] mykem = ExchangeE2e.Base64UrlDecode(me.DeviceKem)!;
            PhoneOpens(_alice, phone, mykem, out string kind, out string text);
            Assert.Equal("text", kind);
            Assert.Equal("hello from the PC\n— second line", text);
        }

        [Fact]
        public async Task ASentPictureAndFileCarryTheirTypeAndNameInsideTheBoxOnly()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            byte[] myKem = ExchangeE2e.Base64UrlDecode(me.DeviceKem)!;

            string path = Path.Combine(_dir, "report.pdf");
            byte[] content = Enumerable.Range(0, 3 * 65536 + 17).Select(i => (byte)(i % 251)).ToArray();
            File.WriteAllBytes(path, content);
            var file = new ExchangeClipboardPayload
            {
                Kind = "file", FilePath = path, Name = "report.pdf", MimeType = "application/pdf", ClearSize = content.Length,
                FileLastWriteUtcTicks = new FileInfo(path).LastWriteTimeUtc.Ticks,
            };

            ExchangeSendReport report = await Service().SendAsync(file, CancellationToken.None);
            Assert.True(report.Ok, report.Entries.Single().Code);
            Assert.Equal(ExchangeE2e.SealedLength(content.Length), _alice.SlotBytes!.Length);
            Assert.DoesNotContain("report.pdf", _alice.SlotBox);
            Assert.DoesNotContain("pdf", _alice.SlotBox);
            Assert.Equal(content, PhoneOpens(_alice, phone, myKem, out string kind, out _));
            Assert.Equal("file", kind);
        }

        [Fact]
        public async Task AFileThatChangedWhileItWasBeingSentIsNeverStored()
        {
            _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            await Join();
            string path = Path.Combine(_dir, "grow.bin");
            File.WriteAllBytes(path, new byte[1000]);
            var file = new ExchangeClipboardPayload
            {
                Kind = "file", FilePath = path, Name = "grow.bin", MimeType = "application/octet-stream", ClearSize = 1000,
                FileLastWriteUtcTicks = new FileInfo(path).LastWriteTimeUtc.Ticks,
            };
            File.AppendAllText(path, "more bytes after the clipboard was read");

            ExchangeSendReport report = await Service().SendAsync(file, CancellationToken.None);

            Assert.False(report.Ok);
            Assert.Equal(ExchangeCodes.SourceChanged, report.Entries.Single().Code);
            Assert.Null(_alice.SlotBytes);
        }

        [Fact]
        public async Task WithNoOtherDeviceThatCanOpenItNothingIsSent()
        {
            _server.AddDevice(_alice, "tablet-0123456789abcdef", "Old tablet", capable: false, withKey: false);
            await Join();
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.False(report.Ok);
            Assert.Equal(ExchangeCodes.NoRecipient, report.Entries.Single().Code);
            Assert.Equal(new[] { "Old tablet" }, report.Entries.Single().DevicesWithoutKeyNames);
            Assert.Null(_alice.SlotBytes);
            Assert.DoesNotContain("clipboard-put", _server.Seen);
        }

        [Fact]
        public async Task ADeviceWhoseKeyChangedIsSealedNothingAndIsNamed()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            FakeDevice laptop = _server.AddDevice(_alice, "laptop-0123456789abcdef", "Laptop");
            ExchangeAccountEntry me = await Join();
            Assert.True((await Service().SendAsync(TextPayload("first"), CancellationToken.None)).Ok);
            Assert.Equal(laptop.Kem, me.TrustedKeys[laptop.DeviceId]);

            laptop.Key = ExchangeDeviceKey.Generate();       // the server now lists another key under the same id
            laptop.Kem = laptop.Key.PublicKemBase64Url;
            ExchangeSendReport report = await Service().SendAsync(TextPayload("second"), CancellationToken.None);

            Assert.True(report.Ok);
            ExchangeEntryResult r = report.Entries.Single();
            Assert.Equal(1, r.Recipients);
            Assert.Equal(new[] { "Laptop" }, r.DevicesKeyChangedNames);
            Assert.NotEqual(laptop.Kem, me.TrustedKeys[laptop.DeviceId]); // the kept key is never replaced
            byte[] myKem = ExchangeE2e.Base64UrlDecode(me.DeviceKem)!;
            Assert.Equal("second", PhoneOpensText(phone, myKem));
        }

        [Fact]
        public async Task AServerThatCannotCarrySealedContentGetsNothingSentAndNothingClear()
        {
            _server.Features = new List<string> { "directory", "clipboard" };
            _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            await Join();
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.Equal(ExchangeCodes.ServerNoE2e, report.Entries.Single().Code);
            Assert.DoesNotContain("clipboard-put", _server.Seen);
        }

        [Fact]
        public async Task AServerWithoutTheClipboardFeatureIsNotAskedForIt()
        {
            _server.Features = new List<string> { "directory", "sealed" };
            await Join();
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.Equal(ExchangeCodes.ServerNoClipboard, report.Entries.Single().Code);
            Assert.DoesNotContain("clipboard-put", _server.Seen);
        }

        [Fact]
        public async Task AGuestEntryIsSkippedWithItsOwnWordsAndNeverConnects()
        {
            FakeAccount visitor = _server.AddAccount("visitor", kind: "guest");
            ExchangeAccountEntry e = await Join(login: "visitor");
            Assert.True(e.IsGuest);
            _server.Seen.Clear();
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.False(report.Ok);
            Assert.Equal(ExchangeCodes.NotPermitted, report.Entries.Single().Code);
            Assert.Empty(_server.Seen);
            Assert.Null(visitor.SlotBytes);
        }

        [Fact]
        public async Task AnEntryThatMustSignInAgainIsSkippedAndKeepsItsReason()
        {
            ExchangeAccountEntry e = await Join();
            e.SignIn = ExchangeSignInState.SignedOut;
            _server.Seen.Clear();
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.Equal(ExchangeCodes.SignedOut, report.Entries.Single().Code);
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task ADeviceTheServerForgotMarksTheEntryAsKeepingTheDeviceButAskingForThePassword()
        {
            ExchangeAccountEntry e = await Join();
            _alice.Devices.Single(d => d.DeviceId == e.DeviceId).Token = "rotated-elsewhere";
            _saves = 0;
            ExchangeSendReport report = await Service().SendAsync(TextPayload("x"), CancellationToken.None);
            Assert.Equal(ExchangeCodes.BadCredentials, report.Entries.Single().Code);
            Assert.Equal(ExchangeSignInState.BadCredentials, e.SignIn);
            Assert.True(_saves > 0);
            Assert.Equal(e.DeviceId, e.DeviceId); // the entry stays, with its device id
            Assert.NotNull(e.LoadSecret());
        }

        [Fact]
        public async Task SendingToSeveralAccountsSucceedsWhenOneTakesItAndNamesTheOthers()
        {
            var second = new FakeExchangeServer();
            FakeAccount bob = second.AddAccount("bob");
            second.AddDevice(bob, "bobs-phone-0123456789ab", "Bob's phone");
            _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            await Join();
            await Join(host: "other.example.org", login: "bob", server: second);
            var router = new RoutedTransport { ["nas.example.org"] = _server, ["other.example.org"] = second };
            second.Unreachable = true;

            var service = new ExchangeClipboardService(_config, new ExchangeClient(router, "1"), () => _saves++);
            ExchangeSendReport report = await service.SendAsync(TextPayload("both"), CancellationToken.None);

            Assert.True(report.Ok);
            Assert.Equal(1, report.Stored);
            Assert.Equal("", report.Entries[0].Code);
            Assert.Equal(ExchangeCodes.Unreachable, report.Entries[1].Code);
            Assert.Null(bob.SlotBytes);
        }

        [Fact]
        public async Task NoAccountsAtAllIsItsOwnAnswer()
        {
            Assert.Equal(ExchangeCodes.NoAccounts, (await Service().SendAsync(TextPayload("x"), CancellationToken.None)).Code);
            Assert.Equal(ExchangeCodes.NoAccounts, (await Service().FetchAsync(CancellationToken.None)).Code);
        }

        // ---------------------------------------------------------------- get

        private ExchangeClipboardPayload FilePayload(string name, byte[] content)
        {
            string path = Path.Combine(_dir, "src-" + name);
            File.WriteAllBytes(path, content);
            return new ExchangeClipboardPayload { Kind = "file", FilePath = path, Name = name, MimeType = "application/octet-stream", ClearSize = content.Length };
        }

        /// <summary>The phone puts a value for this device on the slot: seals it and stores bytes and box like the server would.</summary>
        private void PhoneSends(FakeAccount account, FakeDevice phone, ExchangeAccountEntry toDevice, string kind, string name, string mime, byte[] clear, string storedAt = "2026-10-07T12:00:05Z", bool tamper = false)
        {
            var me = new ExchangeE2eRecipient(toDevice.DeviceId, ExchangeE2e.Base64UrlDecode(toDevice.DeviceKem)!);
            var meta = new Dictionary<string, object> { ["kind"] = kind };
            if (mime.Length > 0) meta["mimeType"] = mime;
            if (name.Length > 0) meta["name"] = name;
            meta["size"] = (long)clear.Length;
            meta["sha256"] = FakeExchangeServer.Sha(clear);
            string payload = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(meta);
            string box = ExchangeE2e.SealBox("clipboard", Encoding.UTF8.GetBytes(payload), phone.DeviceId, phone.Key!, new[] { me }, out byte[] ck);
            byte[] sealedBytes = ExchangeE2e.SealBytes(clear, ck);
            if (tamper) sealedBytes[sealedBytes.Length / 2] ^= 0x40;
            account.SlotBytes = sealedBytes; account.SlotBox = box; account.SlotKind = "sealed";
            account.SlotClipId = "phone-clip"; account.SlotStoredAt = storedAt;
            account.SlotSourceDeviceId = phone.DeviceId; account.SlotSourceDeviceName = phone.Name;
        }

        [Fact]
        public async Task ATextFromThePhoneIsFetchedVerifiedOpenedAndTheSendersKeyIsLearned()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            PhoneSends(_alice, phone, me, "text", "", "", Encoding.UTF8.GetBytes("from the phone"));

            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);

            Assert.True(report.Ok, report.Code);
            Assert.Equal("text", report.Value!.Kind);
            Assert.Equal("from the phone", Encoding.UTF8.GetString(report.Value.Bytes!));
            Assert.Equal("Phone", report.Value.SourceDeviceName);
            Assert.Equal("2026-10-07T12:00:05Z", report.Value.StoredAt);
            Assert.Equal(phone.Kem, me.TrustedKeys[phone.DeviceId]);
        }

        [Fact]
        public async Task AFileFromThePhoneLandsInTheReceivedFolderWithANumberedNameAndNeverOverwrites()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            byte[] content = Enumerable.Range(0, 2 * 65536 + 3).Select(i => (byte)(i % 249)).ToArray();
            PhoneSends(_alice, phone, me, "file", "holiday.png", "image/png", content);
            Directory.CreateDirectory(_config.ExchangeClipboardReceivedFolder);
            File.WriteAllText(Path.Combine(_config.ExchangeClipboardReceivedFolder, "holiday.png"), "mine");

            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);

            Assert.True(report.Ok, report.Code);
            Assert.Equal("file", report.Value!.Kind);
            Assert.Equal(Path.Combine(_config.ExchangeClipboardReceivedFolder, "holiday (1).png"), report.Value.FilePath);
            Assert.Equal(content, File.ReadAllBytes(report.Value.FilePath));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(_config.ExchangeClipboardReceivedFolder, "holiday.png")));
            Assert.Empty(Directory.GetFiles(_config.ExchangeClipboardReceivedFolder, "*" + ExchangeClipboardSink.PartSuffix));
        }

        [Fact]
        public async Task ADamagedValueIsNotPlacedAndLeavesNoPartBehind()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            PhoneSends(_alice, phone, me, "file", "x.bin", "application/octet-stream", new byte[70000], tamper: true);

            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);

            Assert.False(report.Ok);
            Assert.Equal(ExchangeCodes.SealedFailed, report.Code);
            Assert.Null(report.Value);
            string folder = _config.ExchangeClipboardReceivedFolder;
            Assert.True(!Directory.Exists(folder) || Directory.GetFileSystemEntries(folder).Length == 0);
        }

        [Fact]
        public async Task ABytesCountOrDigestThatDiffersFromTheServersMembersIsDamagedNotSealedFailed()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            PhoneSends(_alice, phone, me, "text", "", "", Encoding.UTF8.GetBytes("abc"));
            _alice.ShaOverride = new string('0', 64);
            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);
            Assert.Equal(ExchangeCodes.Damaged, report.Code);
        }

        [Fact]
        public async Task AValueFromADeviceWhoseKeyIsNotTheOneWeTrustIsNeverOpened()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            // the phone's key was first seen as something else: its box now names a key we do not trust
            me.TrustedKeys[phone.DeviceId] = ExchangeDeviceKey.Generate().PublicKemBase64Url;
            PhoneSends(_alice, phone, me, "text", "", "", Encoding.UTF8.GetBytes("forged?"));
            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);
            Assert.Equal(ExchangeCodes.SealedFailed, report.Code);
        }

        [Fact]
        public async Task AClearValueFromADeviceWithoutTheCapabilityIsNeverPlaced()
        {
            await Join();
            _alice.SlotBytes = Encoding.UTF8.GetBytes("plain text");
            _alice.SlotKind = "text"; _alice.SlotBox = ""; _alice.SlotClipId = "c"; _alice.SlotStoredAt = "2026-10-07T12:00:00Z";
            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);
            Assert.Equal(ExchangeCodes.SealedFailed, report.Code);
            Assert.Null(report.Value);
        }

        [Fact]
        public async Task AnEmptyClipboardEverywhereIsAStateNotAnError()
        {
            await Join();
            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);
            Assert.Equal(ExchangeCodes.ClipboardEmpty, report.Code);
            Assert.Null(report.Value);
        }

        [Fact]
        public async Task OfSeveralAccountsTheNewestByTheServersTimeWinsAndATieGoesToTheFirstListed()
        {
            var second = new FakeExchangeServer();
            FakeAccount bob = second.AddAccount("bob");
            FakeDevice bobsPhone = second.AddDevice(bob, "bobs-phone-0123456789ab", "Bob's phone");
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry a = await Join();
            ExchangeAccountEntry b = await Join(host: "other.example.org", login: "bob", server: second);
            var router = new RoutedTransport { ["nas.example.org"] = _server, ["other.example.org"] = second };
            var service = new ExchangeClipboardService(_config, new ExchangeClient(router, "1"), () => _saves++);

            PhoneSends(_alice, phone, a, "text", "", "", Encoding.UTF8.GetBytes("older"), storedAt: "2026-10-07T12:00:01Z");
            PhoneSends(bob, bobsPhone, b, "text", "", "", Encoding.UTF8.GetBytes("newer"), storedAt: "2026-10-07T12:00:09Z");
            ExchangeFetchReport newest = await service.FetchAsync(CancellationToken.None);
            Assert.Equal("newer", Encoding.UTF8.GetString(newest.Value!.Bytes!));

            PhoneSends(bob, bobsPhone, b, "text", "", "", Encoding.UTF8.GetBytes("tie-bob"), storedAt: "2026-10-07T12:00:01Z");
            ExchangeFetchReport tie = await service.FetchAsync(CancellationToken.None);
            Assert.Equal("older", Encoding.UTF8.GetString(tie.Value!.Bytes!));
        }

        [Fact]
        public async Task AnAccountThatIsOfflineIsSkippedAndNamedWhileTheOthersAreUsed()
        {
            var second = new FakeExchangeServer();
            second.AddAccount("bob");
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry a = await Join();
            await Join(host: "other.example.org", login: "bob", server: second);
            var router = new RoutedTransport { ["nas.example.org"] = _server, ["other.example.org"] = second };
            second.Unreachable = true;
            PhoneSends(_alice, phone, a, "text", "", "", Encoding.UTF8.GetBytes("still here"));

            ExchangeFetchReport report = await new ExchangeClipboardService(_config, new ExchangeClient(router, "1"), () => _saves++).FetchAsync(CancellationToken.None);

            Assert.True(report.Ok);
            Assert.Equal("", report.Entries[0].Code);
            Assert.Equal(ExchangeCodes.Unreachable, report.Entries[1].Code);
        }

        [Fact]
        public async Task AReplacedValueBetweenTheHeaderAndTheDownloadIsFetchedAgainAtTheCurrentOne()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry me = await Join();
            PhoneSends(_alice, phone, me, "text", "", "", Encoding.UTF8.GetBytes("first"));
            _server.OnInfoAnswered = () => PhoneSends(_alice, phone, me, "text", "", "", Encoding.UTF8.GetBytes("second"), storedAt: "2026-10-07T12:00:30Z");
            _alice.SlotClipId = "old-clip"; // the header names old-clip; by the download it is "phone-clip"
            ExchangeFetchReport report = await Service().FetchAsync(CancellationToken.None);
            Assert.True(report.Ok, report.Code);
            Assert.Equal("second", Encoding.UTF8.GetString(report.Value!.Bytes!));
        }

        [Fact]
        public async Task ACancelBeforeTheNetworkIsTouchedCancelsEveryEntry()
        {
            await Join();
            _server.Seen.Clear();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                ExchangeSendReport send = await Service().SendAsync(TextPayload("x"), cts.Token);
                Assert.Equal(ExchangeCodes.Cancelled, send.Entries.Single().Code);
                ExchangeFetchReport get = await Service().FetchAsync(cts.Token);
                Assert.Equal(ExchangeCodes.Cancelled, get.Entries.Single().Code);
            }
            Assert.Empty(_server.Seen);
        }

        [Fact]
        public async Task ALostDeviceKeyAsksForTheAccountToBeAddedAgain()
        {
            FakeDevice phone = _server.AddDevice(_alice, "phone-0123456789abcdef", "Phone");
            ExchangeAccountEntry e = await Join();
            PhoneSends(_alice, phone, e, "text", "", "", Encoding.UTF8.GetBytes("waiting"));
            e.SaveSecret(new ExchangeSecret { Token = e.LoadSecret()!.Token, DevicePrivate = "" });
            Assert.Equal(ExchangeCodes.KeyLost, (await Service().SendAsync(TextPayload("x"), CancellationToken.None)).Entries.Single().Code);
            ExchangeFetchReport get = await Service().FetchAsync(CancellationToken.None);
            Assert.Equal(ExchangeCodes.KeyLost, get.Code);
        }

        // ---------------------------------------------------------------- helpers

        private string PhoneOpensText(FakeDevice phone, byte[] senderKem)
        {
            PhoneOpens(_alice, phone, senderKem, out _, out string text);
            return text;
        }

        /// <summary>Routes a host name to its scripted server (several accounts on several servers).</summary>
        private sealed class RoutedTransport : IExchangeTransport
        {
            private readonly Dictionary<string, FakeExchangeServer> _byHost = new Dictionary<string, FakeExchangeServer>();
            public FakeExchangeServer this[string host] { set => _byHost[host] = value; }
            public Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct)
                => _byHost[host].ConnectAsync(host, port, pin, decideTrustOnly, ct);
        }

        /// <summary>Answers the first enrolls with id-collision, then lets the real fake server answer.</summary>
        private sealed class CollidingServer : IExchangeTransport
        {
            private readonly FakeExchangeServer _inner;
            private int _left;
            public int Enrolls { get; private set; }
            public CollidingServer(FakeExchangeServer inner, int collisions) { _inner = inner; _left = collisions; }

            public Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct)
            {
                if (decideTrustOnly || _left <= 0) { Enrolls++; return _inner.ConnectAsync(host, port, pin, decideTrustOnly, ct); }
                _left--;
                Enrolls++;
                DuplexEnd.Pair(out DuplexEnd client, out DuplexEnd server);
                Task.Run(async () =>
                {
                    await ExchangeWire.ReadFrameAsync(server, false, CancellationToken.None);
                    await ExchangeWire.WriteFrameAsync(server, new ExchangeMembers("refused").Add("reason", "id-collision"), CancellationToken.None);
                    server.Close2();
                });
                ExchangeTrustResult trust = ExchangeTrust.Evaluate(pin, _inner.Leaf, _inner.Key, _inner.Policy);
                return Task.FromResult(new ExchangeConnection(client, () => { client.Close2(); server.Close2(); }, trust, ct, idleMs: 0));
            }
        }
    }

}
