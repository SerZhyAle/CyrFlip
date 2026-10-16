using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;

namespace CyrFlip.Tests
{
    /// <summary>One direction of a byte pipe: a blocking read until bytes arrive or the writer closes.</summary>
    internal sealed class PipeHalf : Stream
    {
        private readonly object _gate = new object();
        private readonly Queue<byte> _bytes = new Queue<byte>();
        private bool _closed;

        public void CloseWriting() { lock (_gate) { _closed = true; Monitor.PulseAll(_gate); } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                while (_bytes.Count == 0)
                {
                    if (_closed) return 0;
                    Monitor.Wait(_gate);
                }
                int n = Math.Min(count, _bytes.Count);
                for (int i = 0; i < n; i++) buffer[offset + i] = _bytes.Dequeue();
                return n;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                if (_closed) throw new IOException("the pipe is closed");
                for (int i = 0; i < count; i++) _bytes.Enqueue(buffer[offset + i]);
                Monitor.PulseAll(_gate);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>A stream whose reads come from one pipe half and whose writes go to the other: one end of a connection.</summary>
    internal sealed class DuplexEnd : Stream
    {
        private readonly PipeHalf _in;
        private readonly PipeHalf _out;

        public DuplexEnd(PipeHalf input, PipeHalf output) { _in = input; _out = output; }

        public static void Pair(out DuplexEnd client, out DuplexEnd server)
        {
            var a = new PipeHalf();
            var b = new PipeHalf();
            client = new DuplexEnd(a, b); // the client reads a, writes b
            server = new DuplexEnd(b, a);
        }

        public void Close2() { _out.CloseWriting(); _in.CloseWriting(); }
        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class InMemorySecretStore : IExchangeSecretStore
    {
        private readonly ConcurrentDictionary<string, ExchangeSecret> _secrets = new ConcurrentDictionary<string, ExchangeSecret>();
        public ExchangeSecret? Load(string entryId) => _secrets.TryGetValue(entryId, out ExchangeSecret? s) ? new ExchangeSecret { Token = s.Token, DevicePrivate = s.DevicePrivate } : null;
        public void Save(string entryId, ExchangeSecret secret) => _secrets[entryId] = new ExchangeSecret { Token = secret.Token, DevicePrivate = secret.DevicePrivate };
        public void Delete(string entryId) => _secrets.TryRemove(entryId, out _);
        public int Count => _secrets.Count;
    }

    internal sealed class FakeDevice
    {
        public string DeviceId = "";
        public string Name = "";
        public string Token = "";
        public string Kem = "";
        public bool Capable = true;
        public DateTime UpdatedAtUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        public ExchangeDeviceKey? Key; // a test device that can open boxes
    }

    internal sealed class FakeAccount
    {
        public string Login = "";
        public string Password = "";
        public string Kind = "member";
        public long ClipboardBytes = 5368709120L;
        public readonly List<FakeDevice> Devices = new List<FakeDevice>();

        // the one clipboard slot (22 item BU), in memory only
        public byte[]? SlotBytes;
        public string SlotBox = "";
        public string SlotKind = "";
        public string SlotClipId = "";
        public string SlotSourceDeviceId = "";
        public string SlotSourceDeviceName = "";
        public string SlotStoredAt = "";
        /// <summary>When set, the server announces this digest instead of the real one (a damaged transfer).</summary>
        public string? ShaOverride;
    }

    /// <summary>
    /// A scripted exchange server in memory: the framing, enroll and hello, the control stream (list, clipboard-info,
    /// revoke) and the two data streams (clipboard-put, clipboard-get) of <c>DEVICE-EXCHANGE</c> 7.2 and 22, with the
    /// answers the FMS_Win server corpus shows. It does not look inside a box: it stores the sealed bytes and the
    /// box, hashes the bytes and serves them back, exactly like the real server (item DN).
    /// </summary>
    internal sealed class FakeExchangeServer : IExchangeTransport
    {
        public string Leaf = "SHA256:serverLeaf-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        public string Key = "SHA256:serverKey-BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        public SslPolicyErrors Policy = SslPolicyErrors.RemoteCertificateChainErrors; // self-signed
        public List<string> Features = new List<string> { "directory", "tunnel", "pairing", "clipboard", "sealed" };
        public readonly Dictionary<string, FakeAccount> Accounts = new Dictionary<string, FakeAccount>(StringComparer.Ordinal);
        public readonly List<string> Seen = new List<string>();        // "type" of every frame the server read, in order
        public readonly List<string> Connections = new List<string>(); // "first envelope" per connection
        public bool Unreachable;
        /// <summary>Runs once, right after clipboard-info was answered (a value replaced meanwhile).</summary>
        public Action? OnInfoAnswered;
        private int _tokenCounter;
        private int _clipCounter;
        private readonly object _gate = new object();

        public FakeAccount AddAccount(string login, string password = "Passw0rdTest", string kind = "member")
        {
            var a = new FakeAccount { Login = login, Password = password, Kind = kind };
            Accounts[login] = a;
            return a;
        }

        public FakeDevice AddDevice(FakeAccount account, string id, string name, bool capable = true, bool withKey = true)
        {
            var d = new FakeDevice { DeviceId = id, Name = name, Capable = capable };
            if (withKey)
            {
                d.Key = ExchangeDeviceKey.Generate();
                d.Kem = d.Key.PublicKemBase64Url;
            }
            d.Token = "fake-token-" + (++_tokenCounter);
            account.Devices.Add(d);
            return d;
        }

        public Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct)
        {
            if (Unreachable) throw new ExchangeFailure(ExchangeCodes.Unreachable);
            ExchangeTrustResult trust = ExchangeTrust.Evaluate(pin, Leaf, Key, Policy);
            if (!decideTrustOnly && !trust.Accepts)
                throw new ExchangeFailure(trust.Kind == ExchangeTrustKind.Changed ? ExchangeCodes.CertificateChanged : ExchangeCodes.CertificateUntrusted) { Trust = trust };

            DuplexEnd.Pair(out DuplexEnd client, out DuplexEnd server);
            if (!decideTrustOnly) Task.Run(() => Serve(server));
            else server.Close2();
            var conn = new ExchangeConnection(client, () => { client.Close2(); server.Close2(); }, trust, ct, idleMs: 0);
            return Task.FromResult(conn);
        }

        private async Task Serve(DuplexEnd s)
        {
            string first = "";
            try
            {
                ExchangeFrame? hello = await ExchangeWire.ReadFrameAsync(s, false, CancellationToken.None).ConfigureAwait(false);
                if (hello == null) return;
                Note(hello.Type);
                first = hello.Type;
                FakeAccount? account = null;
                FakeDevice? device = null;

                if (hello.Type == "enroll")
                {
                    if (!Accounts.TryGetValue(hello.Str("login"), out account) || account.Password != hello.Str("password"))
                    {
                        await Write(s, new ExchangeMembers("refused").Add("reason", "bad-credentials")); return;
                    }
                    string id = hello.Str("deviceId");
                    device = account.Devices.FirstOrDefault(d => d.DeviceId == id);
                    ExchangeFrame? key = hello.Obj("e2eKey");
                    string kem = key?.Str("kem") ?? "";
                    if (device != null && device.Kem.Length > 0 && kem != device.Kem)
                    {
                        await Write(s, new ExchangeMembers("refused").Add("reason", "id-collision")); return;
                    }
                    if (device == null) { device = new FakeDevice { DeviceId = id }; account.Devices.Add(device); }
                    device.Name = hello.Str("deviceName");
                    device.Kem = kem;
                    device.Capable = hello.Strings("capabilities").Contains("e2e-v1");
                    device.Token = "fake-token-" + (++_tokenCounter);
                    await Write(s, WelcomeFrame("enrolled", account).Add("deviceToken", device.Token)); // order: type first
                }
                else if (hello.Type == "hello")
                {
                    string id = hello.Str("deviceId");
                    account = Accounts.Values.FirstOrDefault(a => a.Devices.Any(d => d.DeviceId == id));
                    device = account?.Devices.First(d => d.DeviceId == id);
                    if (account == null || device == null || device.Token != hello.Str("deviceToken"))
                    {
                        await Write(s, new ExchangeMembers("refused").Add("reason", device != null && device.Token.Length == 0 ? "device-revoked" : "bad-credentials")); return;
                    }
                    await Write(s, WelcomeFrame("welcome", account));
                }
                else return;

                ExchangeFrame? next = await ExchangeWire.ReadFrameAsync(s, true, CancellationToken.None).ConfigureAwait(false);
                if (next == null) return;
                Note(next.Type);
                if (next.Type == "clipboard-put") { await ClipboardPut(s, account!, device!, next); return; }
                if (next.Type == "clipboard-get") { await ClipboardGet(s, account!, next); return; }

                // the control stream
                while (next != null)
                {
                    switch (next.Type)
                    {
                        case "list":
                            await Write(s, Directory(account!)); break;
                        case "clipboard-info":
                            ExchangeMembers info = Info(account!);
                            Interlocked.Exchange(ref OnInfoAnswered, null)?.Invoke(); // the value may change right after the header was made
                            await Write(s, info);
                            break;
                        case "revoke":
                            FakeDevice? gone = account!.Devices.FirstOrDefault(d => d.DeviceId == next.Str("deviceId"));
                            if (gone != null) { gone.Token = ""; account.Devices.Remove(gone); }
                            break;
                        case "keepalive":
                            await Write(s, new ExchangeMembers("keepalive")); break;
                        default:
                            return; // a known envelope that is invalid on a control stream closes it (the real server)
                    }
                    next = await ExchangeWire.ReadFrameAsync(s, true, CancellationToken.None).ConfigureAwait(false);
                    if (next != null) Note(next.Type);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ExchangeProtocolException || ex is ObjectDisposedException) { }
            finally
            {
                lock (_gate) Connections.Add(first);
                s.Close2();
            }
        }

        private void Note(string type) { lock (_gate) Seen.Add(type); }

        private ExchangeMembers WelcomeFrame(string type, FakeAccount account)
            => new ExchangeMembers(type)
                .Add("account", account.Login).Add("accountKind", account.Kind)
                .Add("publicEndpoint", "exchange.example.net:22022").Add("keepaliveSeconds", 30)
                .Add("features", Features.ToArray()).Add("serverVersion", "fake");

        private ExchangeMembers Directory(FakeAccount account)
        {
            var devices = new List<object>();
            foreach (FakeDevice d in account.Devices)
            {
                var rec = new ExchangeMembers.Plain().Add("deviceId", d.DeviceId).Add("deviceName", d.Name);
                if (d.Capable) rec.Add("capabilities", new object[] { "e2e-v1" });
                if (d.Kem.Length > 0) rec.Add("e2eKey", new ExchangeMembers.Plain().Add("v", 1).Add("kem", d.Kem));
                rec.Add("updatedAt", d.UpdatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                devices.Add(rec);
            }
            return new ExchangeMembers("directory").Add("revision", 5).Add("devices", devices);
        }

        private ExchangeMembers Info(FakeAccount account)
        {
            var m = new ExchangeMembers("clipboard-state");
            if (account.Kind == "member" && account.SlotBytes != null) m.Add("value", ValueMembers(account));
            return m;
        }

        private static ExchangeMembers.Plain ValueMembers(FakeAccount a)
        {
            var v = new ExchangeMembers.Plain()
                .Add("clipId", a.SlotClipId).Add("size", (long)a.SlotBytes!.Length).Add("sha256", a.ShaOverride ?? Sha(a.SlotBytes))
                .Add("storedAt", a.SlotStoredAt).Add("sourceDeviceId", a.SlotSourceDeviceId).Add("sourceDeviceName", a.SlotSourceDeviceName)
                .Add("kind", a.SlotKind);
            if (a.SlotBox.Length > 0) v.Add("sealed", new ExchangeRawJson(a.SlotBox));
            return v;
        }

        private async Task ClipboardPut(DuplexEnd s, FakeAccount account, FakeDevice device, ExchangeFrame put)
        {
            if (account.Kind == "guest") { await Write(s, new ExchangeMembers("refused").Add("reason", "not-permitted")); return; }
            long size = put.Long("size");
            if (size < 1 || size > account.ClipboardBytes) { await Write(s, new ExchangeMembers("refused").Add("reason", "limit-reached")); return; }
            // a sealed put carries the kind "sealed" and a box, and nothing in clear (item DN)
            if (put.Str("kind") != "sealed" || put.Has("mimeType") || put.Has("name") || put.Obj("sealed") == null) return;
            await Write(s, new ExchangeMembers("clipboard-ready"));
            var data = new byte[size];
            int have = 0;
            while (have < size)
            {
                int n = await s.ReadAsync(data, have, (int)size - have).ConfigureAwait(false);
                if (n == 0) return; // a short put is dropped, the value stays (item BX)
                have += n;
            }
            account.SlotBytes = data;
            account.SlotBox = put.JsonOf("sealed");
            account.SlotKind = "sealed";
            account.SlotClipId = "clip" + (++_clipCounter);
            account.SlotSourceDeviceId = device.DeviceId;
            account.SlotSourceDeviceName = device.Name;
            account.SlotStoredAt = "2026-10-07T12:00:0" + (_clipCounter % 10) + "Z";
            await Write(s, new ExchangeMembers("clipboard-stored").Add("clipId", account.SlotClipId).Add("sha256", Sha(data)).Add("storedAt", account.SlotStoredAt));
        }

        private async Task ClipboardGet(DuplexEnd s, FakeAccount account, ExchangeFrame get)
        {
            if (account.Kind == "guest") { await Write(s, new ExchangeMembers("refused").Add("reason", "not-permitted")); return; }
            if (account.SlotBytes == null) { await Write(s, new ExchangeMembers("clipboard-empty")); return; }
            string want = get.Str("clipId");
            if (want.Length > 0 && want != account.SlotClipId) { await Write(s, new ExchangeMembers("refused").Add("reason", "unavailable")); return; }
            byte[] bytes = account.SlotBytes;
            var header = new ExchangeMembers("clipboard-value")
                .Add("clipId", account.SlotClipId).Add("size", (long)bytes.Length).Add("sha256", account.ShaOverride ?? Sha(bytes))
                .Add("storedAt", account.SlotStoredAt).Add("sourceDeviceId", account.SlotSourceDeviceId).Add("sourceDeviceName", account.SlotSourceDeviceName)
                .Add("kind", account.SlotKind);
            if (account.SlotBox.Length > 0) header.Add("sealed", new ExchangeRawJson(account.SlotBox));
            await Write(s, header);
            await s.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static Task Write(Stream s, ExchangeMembers m) => ExchangeWire.WriteFrameAsync(s, m, CancellationToken.None);

        internal static string Sha(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
