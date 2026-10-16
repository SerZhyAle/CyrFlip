using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>What happened with one account entry during an operation.</summary>
    internal sealed class ExchangeEntryResult
    {
        public string Label = "";
        /// <summary>Empty when the entry took part and succeeded; else a code of <see cref="ExchangeCodes"/>.</summary>
        public string Code = "";
        /// <summary>Other devices of the account that cannot be sealed to (items DG and DR), for a send.</summary>
        /// <summary>Other devices of the account a value was not sealed to: no key (<c>e2e-missing</c>), and a changed key (<c>key-changed</c>).</summary>
        public readonly List<string> DevicesWithoutKeyNames = new List<string>();
        public readonly List<string> DevicesKeyChangedNames = new List<string>();
        public int Recipients;
        public bool Ok => Code.Length == 0;
    }

    internal sealed class ExchangeSendReport
    {
        public readonly List<ExchangeEntryResult> Entries = new List<ExchangeEntryResult>();
        public string Kind = "";
        public long Size;
        /// <summary>Set when the operation never reached an entry (no account that can take part).</summary>
        public string Code = "";
        public int Stored => Entries.Count(e => e.Ok);
        public bool Ok => Code.Length == 0 && Stored > 0;
    }

    /// <summary>A value fetched, verified and opened. A text and a picture are in <see cref="Bytes"/>; a file is already in the received-files folder.</summary>
    internal sealed class ExchangeReceivedValue
    {
        public string Kind = "";
        public byte[]? Bytes;
        public string FilePath = "";
        public string Name = "";
        public long ClearSize;
        public string SourceDeviceName = "";
        public string StoredAt = "";
        public string EntryLabel = "";
    }

    internal sealed class ExchangeFetchReport
    {
        public readonly List<ExchangeEntryResult> Entries = new List<ExchangeEntryResult>();
        /// <summary>Set when nothing was received: a code, or <see cref="ExchangeCodes.ClipboardEmpty"/> for an empty slot everywhere.</summary>
        public string Code = "";
        public ExchangeReceivedValue? Value;
        public bool Ok => Code.Length == 0 && Value != null;
    }

    /// <summary>
    /// "Send clipboard to server" and "Get clipboard from server" for every account entry of this device
    /// (<c>DEVICE-EXCHANGE</c> section 22, items CA and DN; <c>CLIPBOARD-GUARD</c> items L to R; ticket S0074).
    /// Every value is sealed on the way out and opened on the way in - there is no clear fallback through the
    /// server (item DP). The clipboard itself is not touched here: the caller reads it first
    /// (<see cref="ExchangeClipboardSource"/>) and places the result after
    /// (<see cref="ExchangeClipboardSink"/>), each under its own guard, so the seconds of network time hold nothing.
    /// </summary>
    internal sealed class ExchangeClipboardService
    {
        /// <summary>The default account limit (item BZ) - used to refuse the impossible locally; the server's own answer is final.</summary>
        public const long DefaultClipboardBytes = 5368709120L;

        /// <summary>A text or a picture is held in memory while it is received; a file goes to disk.</summary>
        public const long MaxInMemoryBytes = 512L * 1024 * 1024;

        private readonly AppConfig _config;
        private readonly ExchangeClient _client;
        private readonly Action _saveAccounts;

        public ExchangeClipboardService(AppConfig config, ExchangeClient client, Action saveAccounts)
        {
            _config = config;
            _client = client;
            _saveAccounts = saveAccounts;
        }

        public static string LabelOf(ExchangeAccountEntry e) => e.Login + " @ " + e.Address;

        // ================= send =================

        public async Task<ExchangeSendReport> SendAsync(ExchangeClipboardPayload payload, CancellationToken ct)
        {
            var report = new ExchangeSendReport { Kind = payload.Kind, Size = payload.ClearSize };
            List<ExchangeAccountEntry> entries = _config.ExchangeClipboardAccounts.ToList();
            if (entries.Count == 0)
            {
                report.Code = ExchangeCodes.NoAccounts;
                return report;
            }

            string? sha = null; // hashed once, lazily: the first entry that gets as far as a box needs it
            for (int ordinal = 0; ordinal < entries.Count; ordinal++)
            {
                ExchangeAccountEntry entry = entries[ordinal];
                var result = new ExchangeEntryResult { Label = LabelOf(entry) };
                report.Entries.Add(result);

                if (entry.NeedsSignIn) { result.Code = ExchangeAccountEntry.SignInToken(entry.SignIn); continue; }
                if (entry.IsGuest) { result.Code = ExchangeCodes.NotPermitted; continue; }
                if (ct.IsCancellationRequested) { result.Code = ExchangeCodes.Cancelled; continue; }

                try
                {
                    result.Code = await SendToEntryAsync(entry, payload, result, () => sha ??= HashClear(payload), ct).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    result.Code = ExchangeCodes.SourceChanged;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Code = ExchangeCodes.NothingToSend; // the file could no longer be read
                }
                ExchangeClipboardLog.Log("send", payload.Kind, payload.ClearSize, ordinal, result.Code.Length == 0 ? "ok" : result.Code);
            }
            return report;
        }

        private async Task<string> SendToEntryAsync(ExchangeAccountEntry entry, ExchangeClipboardPayload payload,
            ExchangeEntryResult result, Func<string> clearSha256, CancellationToken ct)
        {
            using (ExchangeDeviceKey? key = LoadDeviceKey(entry))
            {
                if (key == null) return ExchangeCodes.KeyLost;

                // The control stream: who is on the account, with which keys (the list), checked first.
                ExchangeOutcome<List<ExchangePeer>> listed = await ListPeersAsync(entry, ct).ConfigureAwait(false);
                if (!listed.Ok) return listed.Code;
                List<ExchangePeer> peers = listed.Value!;
                if (ExchangeRecipients.Learn(entry.DeviceId, peers, entry.TrustedKeys)) _saveAccounts();
                ExchangeRecipients.Classify(entry.DeviceId, peers, entry.TrustedKeys, result.DevicesWithoutKeyNames, result.DevicesKeyChangedNames);

                List<ExchangePeer> chosen = ExchangeRecipients.Select(entry.DeviceId, peers, entry.TrustedKeys, ExchangeE2e.MaxRecipients);
                var recipients = new List<ExchangeE2eRecipient>();
                foreach (ExchangePeer p in chosen)
                {
                    byte[]? kem = DecodeKem(p.Kem);
                    if (kem != null) recipients.Add(new ExchangeE2eRecipient(p.DeviceId, kem));
                }
                result.Recipients = recipients.Count;
                if (recipients.Count == 0) return ExchangeCodes.NoRecipient;

                // The box: the members of item BW for the clear value, with the clear size and SHA-256 (item DN).
                if (payload.FilePath.Length > 0) EnsureUnchanged(payload); // before the hash, and again before the bytes go out
                string sha = clearSha256();
                byte[] boxPayload = Encoding.UTF8.GetBytes(BoxPayloadJson(payload, sha));
                string boxJson = ExchangeE2e.SealBox(ExchangeE2e.BoxContextClipboard, boxPayload, entry.DeviceId, key, recipients, out byte[] contentKey);
                try
                {
                    long sealedSize = ExchangeE2e.SealedLength(payload.ClearSize);
                    ExchangeOutcome<ExchangeValueHeader> put = await _client.PutSealedAsync(entry, boxJson, sealedSize,
                        output =>
                        {
                            using (Stream clear = payload.OpenClear())
                            {
                                if (payload.FilePath.Length > 0) EnsureUnchanged(payload);
                                ExchangeE2e.SealStream(clear, payload.ClearSize, contentKey, output);
                            }
                        },
                        welcome => PrecheckWelcome(entry, welcome), ct).ConfigureAwait(false);
                    ApplyAndSave(entry, put.Ok ? null : put.Code);
                    return put.Ok ? "" : put.Code;
                }
                finally { Array.Clear(contentKey, 0, contentKey.Length); }
            }
        }

        // ================= get =================

        /// <summary>
        /// Fetches the value to take: each eligible entry is asked for its clipboard's header and the newest by the
        /// server's own time wins (item CA; a tie goes to the entry listed first), then that one value is downloaded,
        /// checked against the server's size and SHA-256, opened, and checked against the box's. Nothing is placed
        /// unless every check held (item BY).
        /// </summary>
        public async Task<ExchangeFetchReport> FetchAsync(CancellationToken ct)
        {
            var report = new ExchangeFetchReport();
            List<ExchangeAccountEntry> entries = _config.ExchangeClipboardAccounts.ToList();
            if (entries.Count == 0)
            {
                report.Code = ExchangeCodes.NoAccounts;
                return report;
            }

            ExchangeAccountEntry? best = null;
            ExchangeValueHeader? bestHeader = null;
            DateTimeOffset bestAt = DateTimeOffset.MinValue;
            int empty = 0;

            for (int ordinal = 0; ordinal < entries.Count; ordinal++)
            {
                ExchangeAccountEntry entry = entries[ordinal];
                var result = new ExchangeEntryResult { Label = LabelOf(entry) };
                report.Entries.Add(result);

                if (entry.NeedsSignIn) { result.Code = ExchangeAccountEntry.SignInToken(entry.SignIn); continue; }
                if (entry.IsGuest) { result.Code = ExchangeCodes.NotPermitted; continue; }
                if (ct.IsCancellationRequested) { result.Code = ExchangeCodes.Cancelled; continue; }

                ExchangeOutcome<ExchangeValueHeader?> info = await InfoOfEntryAsync(entry, ct).ConfigureAwait(false);
                if (!info.Ok) { result.Code = info.Code; ExchangeClipboardLog.Log("get-info", "none", 0, ordinal, info.Code); continue; }
                ExchangeValueHeader? header = info.Value;
                if (header == null) { empty++; continue; }

                DateTimeOffset at = ParseTime(header.StoredAt);
                if (best == null || at > bestAt) { best = entry; bestHeader = header; bestAt = at; }
            }

            if (best == null || bestHeader == null)
            {
                // an entry that answered "empty" makes the whole answer "empty"; the ones that failed stay named
                report.Code = empty > 0 ? ExchangeCodes.ClipboardEmpty : FirstCode(report.Entries);
                if (report.Code.Length == 0) report.Code = ExchangeCodes.ClipboardEmpty;
                return report;
            }

            int bestOrdinal = entries.IndexOf(best);
            ExchangeEntryResult chosen = report.Entries[bestOrdinal];
            ExchangeOutcome<ExchangeReceivedValue> value = await DownloadAsync(best, bestHeader.ClipId, ct).ConfigureAwait(false);
            if (!value.Ok && value.Code == ExchangeCodes.Unavailable)
                value = await DownloadAsync(best, null, ct).ConfigureAwait(false); // replaced since the header: take the current one
            if (!value.Ok)
            {
                chosen.Code = value.Code;
                report.Code = value.Code;
                ExchangeClipboardLog.Log("get", "sealed", bestHeader.Size, bestOrdinal, value.Code);
                return report;
            }

            ExchangeReceivedValue received = value.Value!;
            received.EntryLabel = chosen.Label;
            report.Value = received;
            ExchangeClipboardLog.Log("get", received.Kind, received.ClearSize, bestOrdinal, "ok");
            return report;
        }

        private async Task<ExchangeOutcome<ExchangeValueHeader?>> InfoOfEntryAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await _client.OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok)
            {
                ApplyAndSave(entry, opened.Code);
                return ExchangeOutcome.Fail<ExchangeValueHeader?>(opened.Code);
            }
            using (ExchangeSession session = opened.Value!)
            {
                ApplyAndSave(entry, null, session);
                string? blocked = PrecheckWelcome(entry, session.Welcome);
                if (blocked != null) return ExchangeOutcome.Fail<ExchangeValueHeader?>(blocked);

                // the same stream's directory teaches the keys of the account's devices before a box is opened
                ExchangeOutcome<List<ExchangePeer>> listed = await _client.ListAsync(session, ct).ConfigureAwait(false);
                if (!listed.Ok) return ExchangeOutcome.Fail<ExchangeValueHeader?>(listed.Code);
                if (ExchangeRecipients.Learn(entry.DeviceId, listed.Value!, entry.TrustedKeys)) _saveAccounts();

                return await _client.InfoAsync(session, ct).ConfigureAwait(false);
            }
        }

        private async Task<ExchangeOutcome<ExchangeReceivedValue>> DownloadAsync(ExchangeAccountEntry entry, string? clipId, CancellationToken ct)
        {
            using (ExchangeDeviceKey? key = LoadDeviceKey(entry))
            {
                if (key == null) return ExchangeOutcome.Fail<ExchangeReceivedValue>(ExchangeCodes.KeyLost);

                string folder = ExchangeClipboardSink.FolderOf(_config);
                ExchangeReceivedValue? received = null;
                string? partPath = null;
                try
                {
                    ExchangeOutcome<ExchangeValueHeader?> got = await _client.GetAsync(entry, clipId,
                        welcome => PrecheckWelcome(entry, welcome),
                        (header, stream) => Consume(entry, key, header, stream, folder, out received, out partPath), ct).ConfigureAwait(false);
                    if (!got.Ok) return ExchangeOutcome.Fail<ExchangeReceivedValue>(got.Code);
                    if (got.Value == null || received == null) return ExchangeOutcome.Fail<ExchangeReceivedValue>(ExchangeCodes.ClipboardEmpty);
                    received.SourceDeviceName = got.Value.SourceDeviceName;
                    received.StoredAt = got.Value.StoredAt;

                    if (received.Kind == ExchangeClipboardPayload.File && partPath != null)
                    {
                        string final = ExchangeClipboardSink.UniquePath(folder, received.Name);
                        File.Move(partPath, final);
                        partPath = null;
                        received.FilePath = final;
                    }
                    return ExchangeOutcome.Success(received);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return ExchangeOutcome.Fail<ExchangeReceivedValue>(ExchangeCodes.PlaceFailed);
                }
                finally
                {
                    if (partPath != null) TryDelete(partPath);
                }
            }
        }

        /// <summary>
        /// Reads the sealed bytes that follow a <c>clipboard-value</c> and opens them. Returns null on success,
        /// else the code. Runs on a pool thread inside the stream; it reads exactly <c>header.Size</c> bytes.
        /// </summary>
        private string? Consume(ExchangeAccountEntry entry, ExchangeDeviceKey key, ExchangeValueHeader header, Stream stream,
            string folder, out ExchangeReceivedValue? received, out string? partPath)
        {
            received = null;
            partPath = null;

            // a clear kind is a sender without the capability: never placed (item DN)
            if (header.Kind != ExchangeWire.KindSealed || header.BoxJson.Length == 0) return ExchangeCodes.SealedFailed;

            bool opened = ExchangeE2e.TryOpenBox(ExchangeE2e.BoxContextClipboard, header.BoxJson, entry.DeviceId, key,
                from => ExchangeRecipients.TrustedKemOf(entry.TrustedKeys, from, DecodeKem),
                out byte[]? boxPayload, out byte[]? contentKey, out _);
            if (!opened || boxPayload == null || contentKey == null) return ExchangeCodes.SealedFailed;

            try
            {
                if (!TryReadBoxPayload(boxPayload, out string kind, out string name, out long clearSize, out string clearSha))
                    return ExchangeCodes.SealedFailed;
                if (header.Size != ExchangeE2e.SealedLength(clearSize)) return ExchangeCodes.SealedFailed;

                bool toDisk = kind == ExchangeClipboardPayload.File;
                if (!toDisk && clearSize > MaxInMemoryBytes) return ExchangeCodes.TooLarge;

                string? part = null;
                Stream clearOut;
                MemoryStream? memory = null;
                if (toDisk)
                {
                    Directory.CreateDirectory(folder);
                    ExchangeClipboardSink.SweepStaleParts(folder);
                    part = Path.Combine(folder, Guid.NewGuid().ToString("N") + ExchangeClipboardSink.PartSuffix);
                    clearOut = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan);
                }
                else
                {
                    memory = new MemoryStream((int)clearSize);
                    clearOut = memory;
                }

                bool kept = false;
                try
                {
                    using (var counting = new HashingReadStream(stream, header.Size))
                    using (clearOut)
                    {
                        bool ok = ExchangeE2e.TryOpenStream(counting, header.Size, contentKey, clearOut, out long clearLength, out byte[]? gotSha, out string failure);
                        // A stream that ended early broke in transit; a segment that does not open is a value this device cannot
                        // read (item DN: sealed-failed). Only bytes that all opened can be compared with the server's members.
                        if (!ok) return failure == "truncated" ? ExchangeCodes.Damaged : ExchangeCodes.SealedFailed;
                        // the server's members over the sealed bytes (item BY), then the box's over the clear bytes (item DN)
                        if (!counting.Complete || !string.Equals(counting.HexSha256(), header.Sha256, StringComparison.OrdinalIgnoreCase))
                            return ExchangeCodes.Damaged;
                        if (clearLength != clearSize || gotSha == null || !string.Equals(Hex(gotSha), clearSha, StringComparison.OrdinalIgnoreCase))
                            return ExchangeCodes.SealedFailed;
                    }
                    kept = true;
                }
                finally
                {
                    if (!kept && part != null) TryDelete(part);
                }

                received = new ExchangeReceivedValue
                {
                    Kind = kind,
                    Name = kind == ExchangeClipboardPayload.File ? name : "",
                    ClearSize = clearSize,
                    Bytes = memory?.ToArray(),
                };
                partPath = part;
                return null;
            }
            finally
            {
                Array.Clear(boxPayload, 0, boxPayload.Length);
                Array.Clear(contentKey, 0, contentKey.Length);
            }
        }

        // ================= shared =================

        /// <summary>
        /// The checks that need the <c>welcome</c> alone (items BV, AO and DP): no clipboard feature, a guest, a
        /// server that cannot carry sealed content - each its own message, none a connection failure. Also keeps
        /// what the server said about itself.
        /// </summary>
        private string? PrecheckWelcome(ExchangeAccountEntry entry, ExchangeWelcome welcome)
        {
            if (!welcome.Has(ExchangeWire.FeatureClipboard)) return ExchangeCodes.ServerNoClipboard;
            if (welcome.IsGuest) return ExchangeCodes.NotPermitted;
            if (!welcome.Has(ExchangeWire.FeatureSealed)) return ExchangeCodes.ServerNoE2e;
            return null;
        }

        private async Task<ExchangeOutcome<List<ExchangePeer>>> ListPeersAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await _client.OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok)
            {
                ApplyAndSave(entry, opened.Code);
                return ExchangeOutcome.Fail<List<ExchangePeer>>(opened.Code);
            }
            using (ExchangeSession session = opened.Value!)
            {
                ApplyAndSave(entry, null, session);
                string? blocked = PrecheckWelcome(entry, session.Welcome);
                if (blocked != null) return ExchangeOutcome.Fail<List<ExchangePeer>>(blocked);
                return await _client.ListAsync(session, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Records what a stream taught about the entry: the server's features, version and account kind, a
        /// renewed pin, and - for a refusal that asks for the password - that the entry must be signed in again.
        /// </summary>
        private void ApplyAndSave(ExchangeAccountEntry entry, string? refusal, ExchangeSession? session = null)
        {
            bool changed = false;
            if (refusal != null && ExchangeCodes.AsksForPassword(refusal))
            {
                ExchangeSignInState state = ExchangeCodes.SignInStateOf(refusal);
                if (entry.SignIn != state) { entry.SignIn = state; changed = true; }
            }
            if (session != null)
            {
                ExchangeWelcome w = session.Welcome;
                if (!entry.Features.SequenceEqual(w.Features)) { entry.Features = new List<string>(w.Features); changed = true; }
                if (entry.AccountKind != w.AccountKind) { entry.AccountKind = w.AccountKind; changed = true; }
                if (w.ServerVersion.Length > 0 && entry.ServerVersion != w.ServerVersion) { entry.ServerVersion = w.ServerVersion; changed = true; }
                ExchangePin after = session.Connection.Trust.PinAfter(entry.Pin);
                if (after.Leaf != entry.PinLeaf) { entry.SetPin(after); changed = true; }
            }
            if (changed) _saveAccounts();
        }

        private static ExchangeDeviceKey? LoadDeviceKey(ExchangeAccountEntry entry)
        {
            ExchangeSecret? secret = entry.LoadSecret();
            if (secret == null || secret.DevicePrivate.Length == 0) return null;
            byte[]? scalar = ExchangeE2e.Base64UrlDecode(secret.DevicePrivate);
            if (scalar == null || scalar.Length != 32) return null;
            try
            {
                ExchangeDeviceKey key = ExchangeDeviceKey.FromPrivateScalar(scalar);
                // the stored public half must be this very key, or the box would name a key nobody holds
                return string.Equals(key.PublicKemBase64Url, entry.DeviceKem, StringComparison.Ordinal) ? key : null;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException)
            {
                return null;
            }
            finally { Array.Clear(scalar, 0, scalar.Length); }
        }

        /// <summary>A device key as the directory writes it: base64url of a 65-byte uncompressed SEC1 point, else null.</summary>
        internal static byte[]? DecodeKem(string kem)
        {
            byte[]? bytes = ExchangeE2e.Base64UrlDecode(kem);
            return bytes != null && bytes.Length == 65 && bytes[0] == 0x04 ? bytes : null;
        }

        /// <summary>The box's payload (item DN): the members of item BW for the clear value, with its size and SHA-256.</summary>
        internal static string BoxPayloadJson(ExchangeClipboardPayload payload, string clearSha256)
        {
            var members = new Dictionary<string, object> { ["kind"] = payload.Kind };
            if (payload.MimeType.Length > 0) members["mimeType"] = payload.MimeType;
            if (payload.Name.Length > 0) members["name"] = payload.Name;
            members["size"] = payload.ClearSize;
            members["sha256"] = clearSha256;
            return new JavaScriptSerializer().Serialize(members);
        }

        private static bool TryReadBoxPayload(byte[] json, out string kind, out string name, out long size, out string sha)
        {
            kind = ""; name = ""; size = -1; sha = "";
            try
            {
                var map = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(json)) as Dictionary<string, object>;
                if (map == null) return false;
                kind = map.TryGetValue("kind", out object? k) && k is string ks ? ks : "";
                name = map.TryGetValue("name", out object? n) && n is string ns ? ns : "";
                sha = map.TryGetValue("sha256", out object? s) && s is string ss ? ss : "";
                if (map.TryGetValue("size", out object? z))
                {
                    if (z is int zi) size = zi;
                    else if (z is long zl) size = zl;
                    else if (z is decimal zd && zd == decimal.Truncate(zd)) size = (long)zd;
                }
                bool kindOk = kind == ExchangeClipboardPayload.Text || kind == ExchangeClipboardPayload.Image || kind == ExchangeClipboardPayload.File;
                return kindOk && size >= 1 && sha.Length == 64 && (kind != ExchangeClipboardPayload.File || name.Length > 0);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                return false;
            }
        }

        /// <summary>SHA-256 of the clear value, lower-case hex: the memory it is in, or the file streamed once.</summary>
        private static string HashClear(ExchangeClipboardPayload payload)
        {
            using (SHA256 sha = SHA256.Create())
            using (Stream clear = payload.OpenClear())
                return Hex(sha.ComputeHash(clear));
        }

        private static void EnsureUnchanged(ExchangeClipboardPayload payload)
        {
            var info = new FileInfo(payload.FilePath);
            if (!info.Exists || info.Length != payload.ClearSize || info.LastWriteTimeUtc.Ticks != payload.FileLastWriteUtcTicks)
                throw new InvalidDataException("source-changed");
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static DateTimeOffset ParseTime(string rfc3339)
            => DateTimeOffset.TryParse(rfc3339, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset t) ? t : DateTimeOffset.MinValue;

        private static string FirstCode(IEnumerable<ExchangeEntryResult> entries)
        {
            foreach (ExchangeEntryResult e in entries)
                if (!e.Ok) return e.Code;
            return "";
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        /// <summary>Reads at most a declared number of bytes of a stream and hashes what it read (the server's SHA-256 is over these).</summary>
        private sealed class HashingReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _limit;
            private readonly SHA256 _sha = SHA256.Create();
            private long _count;
            private byte[]? _digest;

            public HashingReadStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

            /// <summary>Exactly the declared number of bytes came through.</summary>
            public bool Complete => _count == _limit;

            public string HexSha256()
            {
                if (_digest == null)
                {
                    _sha.TransformFinalBlock(new byte[0], 0, 0);
                    _digest = _sha.Hash ?? new byte[32];
                }
                return Hex(_digest);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = _limit - _count;
                if (left <= 0) return 0;
                int n = _inner.Read(buffer, offset, (int)Math.Min(count, left));
                if (n > 0)
                {
                    _sha.TransformBlock(buffer, offset, n, null, 0);
                    _count += n;
                }
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _limit;
            public override long Position { get => _count; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _sha.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
