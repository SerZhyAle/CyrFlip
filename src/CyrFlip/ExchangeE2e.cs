using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>One device a box is sealed to: its <c>deviceId</c> and its <c>e2eKey.kem</c> (the 65-byte uncompressed P-256 point).</summary>
    internal sealed class ExchangeE2eRecipient
    {
        internal string DeviceId { get; }
        internal byte[] Kem { get; }

        /// <summary>Throws <see cref="ArgumentException"/> for an empty id or a key that is not a P-256 point.</summary>
        internal ExchangeE2eRecipient(string deviceId, byte[] kem)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("A recipient has a deviceId.", nameof(deviceId));
            if (!ExchangeDeviceKey.IsValidPublicPoint(kem)) throw new ArgumentException("The key of a recipient is a 65-byte uncompressed P-256 point.", nameof(kem));
            DeviceId = deviceId;
            Kem = (byte[])kem.Clone();
        }
    }

    /// <summary>
    /// Where a seal gets its randomness and its ephemeral keys. Production is <see cref="ExchangeE2eSystemSource.Instance"/>
    /// (the CSPRNG); a test passes a scripted one to make a box reproducible. The order in which
    /// <see cref="ExchangeE2e.SealBox(string, byte[], string, ExchangeDeviceKey, IReadOnlyList{ExchangeE2eRecipient}, IExchangeE2eSource, out byte[])"/>
    /// asks is part of that seam: the content key (32 bytes), the nonce (12 bytes), then one ephemeral key per recipient in list order.
    /// </summary>
    internal interface IExchangeE2eSource
    {
        void Fill(byte[] buffer);
        ExchangeDeviceKey NewEphemeralKey();
    }

    internal sealed class ExchangeE2eSystemSource : IExchangeE2eSource
    {
        internal static readonly ExchangeE2eSystemSource Instance = new ExchangeE2eSystemSource();

        private ExchangeE2eSystemSource() { }

        public void Fill(byte[] buffer) => ExchangeE2e.FillRandom(buffer);

        public ExchangeDeviceKey NewEphemeralKey() => ExchangeDeviceKey.Generate();
    }

    /// <summary>
    /// Sealed content - end-to-end encryption (DEVICE-EXCHANGE section 27, items DF, DG, DH and DN), as pure library
    /// code with no I/O and no state: the check code of a device key (DG), the sealed box that carries a content
    /// key to the devices of an account entry (DH), and the sealed bytes of a clipboard value (DN). The callers - the
    /// exchange client - decide which devices are recipients and which keys are trusted (item DG) and place the box
    /// in the frames; none of that is here. The server never opens, checks or changes a box.
    /// <para>
    /// <b>Failures are return codes, not exceptions</b> for everything a hostile or damaged input can cause: the
    /// <c>TryOpen*</c> methods return false with a short stable <c>failure</c> code and no partial output. Exceptions
    /// are for the caller's own bugs (a null, a recipient list of 41, an empty value) and for the machine (a CNG
    /// failure, an I/O error on a stream the caller handed in).
    /// </para>
    /// Failure codes: <c>no-entry</c>, <c>unknown-version</c>, <c>untrusted-key</c>, <c>bad-tag</c>, <c>malformed</c>
    /// (a box or a sealed value that is not the shape the contract describes), <c>truncated</c> (a sealed value that
    /// ends before its length says it should) and <c>crypto-failed</c> (Windows' CNG refused a call on input that was
    /// well formed - the machine, not the data). Every one of them is the contract's <c>sealed-failed</c> to the person.
    /// <para>
    /// A <b>content key</b> that comes out of a seal or an open belongs to the caller, who zeroes it with
    /// <see cref="Wipe"/> when the value is done.
    /// </para>
    /// </summary>
    internal static class ExchangeE2e
    {
        /// <summary>The segment of a sealed clipboard value: 65536 clear bytes, the last segment 1 to 65536.</summary>
        internal const int SegmentSize = 65536;
        internal const int SegmentTagSize = 16;
        internal const int MaxRecipients = 40;
        internal const int ContentKeySize = 32;
        internal const int BoxNonceSize = 12;
        internal const int WrappedKeySize = ContentKeySize + SegmentTagSize;
        internal const string BoxContextClipboard = "clipboard";

        /// <summary>A box longer than this is not read (a box of 40 recipients is about 10 KB).</summary>
        internal const int MaxBoxChars = 1 << 20;

        private static readonly byte[] CheckLabel = Encoding.ASCII.GetBytes("fmsx-e2e-v1 check");
        private static readonly byte[] ValueKeyInfo = Encoding.ASCII.GetBytes("fmsx-e2e-v1 clipboard");

        private static readonly string[] BoxMembers = { "v", "from", "fromKey", "n", "ct", "keys" };
        private static readonly string[] EntryMembers = { "to", "enc", "wk" };

        // ---- helpers ----

        internal static void FillRandom(byte[] buffer)
        {
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(buffer);
        }

        /// <summary>Zeroes a key or a secret. A null is fine.</summary>
        internal static void Wipe(byte[]? secret)
        {
            if (secret != null) Array.Clear(secret, 0, secret.Length);
        }

        /// <summary>base64url, no padding (RFC 4648 section 5) - every binary member of the contract.</summary>
        internal static string Base64UrlEncode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// The strict reading: only the base64url alphabet, no padding, no whitespace, a length that can be a base64
        /// text and the canonical spelling (the unused bits of the last character are zero). Null for anything else.
        /// </summary>
        internal static byte[]? Base64UrlDecode(string? text)
        {
            if (text == null || text.Length % 4 == 1) return null;
            foreach (char c in text)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return null;
            }
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - text.Length % 4) % 4);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(padded); }
            catch (FormatException) { return null; }
            return string.Equals(Base64UrlEncode(bytes), text, StringComparison.Ordinal) ? bytes : null;
        }

        /// <summary>Equal-length compare whose time does not depend on where the bytes differ.</summary>
        internal static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }

        // ---- item DG: the check code ----

        /// <summary>
        /// The check code of a device key (item DG): the first 8 bytes of SHA-256 over the ASCII bytes
        /// <c>fmsx-e2e-v1 check</c> followed by the 65 bytes of <c>kem</c>, read as an unsigned big-endian integer,
        /// modulo 10^12, as twelve decimal digits in three groups of four separated by a space
        /// (<c>9850 2090 7896</c>, the form of <c>vectors/e2e-v1.json</c>).
        /// </summary>
        internal static string CheckCode(byte[] kem65)
        {
            if (kem65 == null) throw new ArgumentNullException(nameof(kem65));
            if (kem65.Length != ExchangeDeviceKey.PublicSize) throw new ArgumentException("A device key is 65 bytes.", nameof(kem65));

            byte[] input = new byte[CheckLabel.Length + kem65.Length];
            Buffer.BlockCopy(CheckLabel, 0, input, 0, CheckLabel.Length);
            Buffer.BlockCopy(kem65, 0, input, CheckLabel.Length, kem65.Length);
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(input);

            ulong value = 0;
            for (int i = 0; i < 8; i++) value = (value << 8) | hash[i];
            string digits = (value % 1000000000000UL).ToString("D12", CultureInfo.InvariantCulture);
            return digits.Substring(0, 4) + " " + digits.Substring(4, 4) + " " + digits.Substring(8, 4);
        }

        // ---- item DH: sealing a box ----

        /// <summary>
        /// Seals <paramref name="payloadUtf8"/> (the UTF-8 bytes of one JSON object) under a fresh content key and
        /// carries that key to every recipient by HPKE Auth mode, as item DH describes. Returns the box as one
        /// compact JSON object text with the members <c>v</c>, <c>from</c>, <c>fromKey</c>, <c>n</c>, <c>ct</c>, <c>keys</c>.
        /// <paramref name="contentKey"/> is the 32-byte <c>CK</c> - the clipboard value key (item DN) derives from it;
        /// the caller zeroes it. At most <see cref="MaxRecipients"/> recipients, each device once;
        /// <see cref="ArgumentException"/> otherwise.
        /// </summary>
        internal static string SealBox(string context, byte[] payloadUtf8, string senderDeviceId, ExchangeDeviceKey sender,
            IReadOnlyList<ExchangeE2eRecipient> recipients, out byte[] contentKey)
            => SealBox(context, payloadUtf8, senderDeviceId, sender, recipients, ExchangeE2eSystemSource.Instance, out contentKey);

        /// <summary><see cref="SealBox(string, byte[], string, ExchangeDeviceKey, IReadOnlyList{ExchangeE2eRecipient}, out byte[])"/> with its randomness supplied (the test seam).</summary>
        internal static string SealBox(string context, byte[] payloadUtf8, string senderDeviceId, ExchangeDeviceKey sender,
            IReadOnlyList<ExchangeE2eRecipient> recipients, IExchangeE2eSource source, out byte[] contentKey)
        {
            if (string.IsNullOrEmpty(context)) throw new ArgumentException("A box has a context.", nameof(context));
            if (payloadUtf8 == null) throw new ArgumentNullException(nameof(payloadUtf8));
            if (string.IsNullOrEmpty(senderDeviceId)) throw new ArgumentException("A box has a sender.", nameof(senderDeviceId));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (recipients == null) throw new ArgumentNullException(nameof(recipients));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (recipients.Count == 0) throw new ArgumentException("A box is sealed to at least one device.", nameof(recipients));
            if (recipients.Count > MaxRecipients) throw new ArgumentException("A box addresses at most " + MaxRecipients + " recipients.", nameof(recipients));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExchangeE2eRecipient recipient in recipients)
            {
                if (recipient == null) throw new ArgumentException("A recipient is null.", nameof(recipients));
                if (!seen.Add(recipient.DeviceId)) throw new ArgumentException("A device is a recipient once.", nameof(recipients));
            }

            byte[] contextBytes = Encoding.UTF8.GetBytes(context);
            byte[] key = new byte[ContentKeySize];
            byte[] nonce = new byte[BoxNonceSize];
            bool done = false;
            try
            {
                source.Fill(key);
                source.Fill(nonce);

                byte[] ct = new byte[payloadUtf8.Length + SegmentTagSize];
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(key))
                    aead.Seal(nonce, contextBytes, payloadUtf8, 0, payloadUtf8.Length, ct, 0);

                var json = new StringBuilder(320 + ct.Length * 4 / 3 + recipients.Count * 240);
                json.Append("{\"v\":1,\"from\":");
                AppendJsonString(json, senderDeviceId);
                json.Append(",\"fromKey\":\"").Append(sender.PublicKemBase64Url).Append('"');
                json.Append(",\"n\":\"").Append(Base64UrlEncode(nonce)).Append('"');
                json.Append(",\"ct\":\"").Append(Base64UrlEncode(ct)).Append('"');
                json.Append(",\"keys\":[");
                for (int i = 0; i < recipients.Count; i++)
                {
                    ExchangeE2eRecipient recipient = recipients[i];
                    byte[] wrapped;
                    byte[] enc;
                    using (ExchangeDeviceKey ephemeral = source.NewEphemeralKey())
                        wrapped = ExchangeHpke.SealAuth(recipient.Kem, sender, ExchangeHpke.ContractInfo, contextBytes, key, ephemeral, out enc);
                    if (i > 0) json.Append(',');
                    json.Append("{\"to\":");
                    AppendJsonString(json, recipient.DeviceId);
                    json.Append(",\"enc\":\"").Append(Base64UrlEncode(enc)).Append('"');
                    json.Append(",\"wk\":\"").Append(Base64UrlEncode(wrapped)).Append("\"}");
                }
                json.Append("]}");

                done = true;
                contentKey = key;
                return json.ToString();
            }
            finally
            {
                if (!done) Wipe(key);
                Wipe(nonce);
            }
        }

        // ---- item DH: opening a box ----

        /// <summary>
        /// Opens a box as the reader <paramref name="ownDeviceId"/> (item DH, "Opening"): takes the entry whose
        /// <c>to</c> is the reader, requires <c>v</c> to be 1, checks <c>fromKey</c> against the key
        /// <paramref name="trustedKemOf"/> returns for <c>from</c> (null means the sender is not trusted), runs HPKE
        /// <c>OpenAuth</c> with <c>fromKey</c> as the sender's key, then opens <c>ct</c>. The
        /// <paramref name="context"/> is the one the reader expects for the place it found the box in - it is never
        /// read from the box, so a box moved to another record fails with <c>bad-tag</c>. Unknown members are ignored.
        /// <para>
        /// On success <paramref name="payload"/> is the clear JSON bytes and <paramref name="contentKey"/> the 32-byte
        /// CK (zeroed by the caller); on any failure both are null, <paramref name="failure"/> says which of
        /// <c>no-entry</c>, <c>unknown-version</c>, <c>untrusted-key</c>, <c>bad-tag</c> or <c>malformed</c> - or
        /// <c>crypto-failed</c> when Windows' CNG itself refused a call on well-formed input (the machine, not the box) -
        /// and nothing partial is left.
        /// </para>
        /// </summary>
        internal static bool TryOpenBox(string context, string boxJson, string ownDeviceId, ExchangeDeviceKey own,
            Func<string, byte[]?> trustedKemOf, out byte[]? payload, out byte[]? contentKey, out string failure)
        {
            payload = null;
            contentKey = null;
            failure = "malformed";
            if (string.IsNullOrEmpty(context)) throw new ArgumentException("A box is read under a context.", nameof(context));
            if (ownDeviceId == null) throw new ArgumentNullException(nameof(ownDeviceId));
            if (own == null) throw new ArgumentNullException(nameof(own));
            if (trustedKemOf == null) throw new ArgumentNullException(nameof(trustedKemOf));

            try
            {
                return OpenBox(context, boxJson, ownDeviceId, own, trustedKemOf, out payload, out contentKey, out failure);
            }
            catch (CryptographicException)
            {
                payload = null;
                contentKey = null;
                failure = "crypto-failed";
                return false;
            }
        }

        private static bool OpenBox(string context, string boxJson, string ownDeviceId, ExchangeDeviceKey own,
            Func<string, byte[]?> trustedKemOf, out byte[]? payload, out byte[]? contentKey, out string failure)
        {
            payload = null;
            contentKey = null;
            failure = "malformed";
            if (!TryParseRoot(boxJson, out Dictionary<string, object>? root) || root == null) return false;
            if (HasLookalikeName(root, BoxMembers)) return false;
            if (!root.TryGetValue("v", out object? version)) return false;
            if (!IsInteger(version, 1))
            {
                failure = "unknown-version";   // a future version may be shaped differently: nothing else is read
                return false;
            }

            string? from = AsString(root, "from");
            byte[]? fromKey = Base64UrlDecode(AsString(root, "fromKey"));
            byte[]? nonce = Base64UrlDecode(AsString(root, "n"));
            byte[]? ct = Base64UrlDecode(AsString(root, "ct"));
            if (from == null || fromKey == null || nonce == null || ct == null) return false;
            if (!ExchangeDeviceKey.IsValidPublicPoint(fromKey) || nonce.Length != BoxNonceSize || ct.Length < SegmentTagSize) return false;
            if (!(root.TryGetValue("keys", out object? keys) && keys is IList list)) return false;

            Dictionary<string, object>? entry = null;
            foreach (object item in list)
            {
                if (item is Dictionary<string, object> candidate && !HasLookalikeName(candidate, EntryMembers)
                    && candidate.TryGetValue("to", out object? to) && to is string toText
                    && string.Equals(toText, ownDeviceId, StringComparison.Ordinal))
                {
                    entry = candidate;
                    break;
                }
            }
            if (entry == null)
            {
                failure = "no-entry";
                return false;
            }
            byte[]? enc = Base64UrlDecode(AsString(entry, "enc"));
            byte[]? wrapped = Base64UrlDecode(AsString(entry, "wk"));
            if (enc == null || wrapped == null || enc.Length != ExchangeHpke.Nenc || wrapped.Length != WrappedKeySize) return false;

            byte[]? trusted;
            try { trusted = trustedKemOf(from); }
            catch (Exception) { trusted = null; }   // a lookup that fails trusts nobody
            if (trusted == null || !FixedTimeEquals(trusted, fromKey))
            {
                failure = "untrusted-key";
                return false;
            }

            byte[] contextBytes = Encoding.UTF8.GetBytes(context);
            if (!ExchangeHpke.TryOpenAuth(enc, own, fromKey, ExchangeHpke.ContractInfo, contextBytes, wrapped, out byte[]? key, out failure) || key == null)
                return false;
            try
            {
                byte[] opened = new byte[ct.Length - SegmentTagSize];
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(key))
                {
                    if (!aead.TryOpen(nonce, contextBytes, ct, 0, ct.Length, opened, 0))
                    {
                        failure = "bad-tag";
                        return false;
                    }
                }
                payload = opened;
                contentKey = key;
                key = null;
                failure = string.Empty;
                return true;
            }
            finally
            {
                Wipe(key);
            }
        }

        /// <summary>
        /// Reads the sender a box names - <c>from</c> and <c>fromKey</c> - without opening it, for the callers that
        /// keep a first-seen key (item DJ, a guest keeps <c>fromKey</c> per <c>grantId</c>). The values are claims
        /// until <see cref="TryOpenBox"/> has checked them. False for anything that is not a box of version 1.
        /// </summary>
        internal static bool TryReadSender(string boxJson, out string? from, out byte[]? fromKey)
        {
            from = null;
            fromKey = null;
            if (!TryParseRoot(boxJson, out Dictionary<string, object>? root) || root == null) return false;
            if (HasLookalikeName(root, BoxMembers)) return false;
            if (!root.TryGetValue("v", out object? version) || !IsInteger(version, 1)) return false;
            string? id = AsString(root, "from");
            byte[]? key = Base64UrlDecode(AsString(root, "fromKey"));
            if (id == null || key == null || !ExchangeDeviceKey.IsValidPublicPoint(key)) return false;
            from = id;
            fromKey = key;
            return true;
        }

        // ---- item DN: the sealed bytes of a clipboard value ----

        /// <summary>Segments of a clear value of <paramref name="clearLength"/> bytes (at least 1).</summary>
        internal static long SegmentCount(long clearLength)
        {
            if (clearLength < 1) throw new ArgumentOutOfRangeException(nameof(clearLength), "A value has at least one byte.");
            return clearLength / SegmentSize + (clearLength % SegmentSize == 0 ? 0 : 1);
        }

        /// <summary>The sealed size of a clear value: the clear size plus 16 bytes per segment. An empty value is not allowed (the contract's <c>size</c> is at least 1).</summary>
        internal static long SealedLength(long clearLength)
        {
            long segments = SegmentCount(clearLength);
            return checked(clearLength + SegmentTagSize * segments);
        }

        /// <summary>The clear size a sealed size stands for, or false when no clear value seals to that many bytes.</summary>
        internal static bool TryClearLength(long sealedLength, out long clearLength)
        {
            clearLength = 0;
            const int full = SegmentSize + SegmentTagSize;
            if (sealedLength < SegmentTagSize + 1) return false;
            long segments = sealedLength / full + (sealedLength % full == 0 ? 0 : 1);
            long last = sealedLength - (segments - 1) * full;
            if (last < SegmentTagSize + 1) return false;
            clearLength = sealedLength - SegmentTagSize * segments;
            return true;
        }

        /// <summary>
        /// The value key of item DN: HKDF-SHA256 with the box's content key as the input key, an empty salt, the
        /// info <c>fmsx-e2e-v1 clipboard</c> and 32 output bytes. The caller zeroes it.
        /// </summary>
        internal static byte[] DeriveValueKey(byte[] contentKey)
        {
            CheckContentKey(contentKey);
            return ExchangeHkdf.Derive(contentKey, new byte[0], ValueKeyInfo, 32);
        }

        private static void CheckContentKey(byte[] contentKey)
        {
            if (contentKey == null) throw new ArgumentNullException(nameof(contentKey));
            if (contentKey.Length != ContentKeySize) throw new ArgumentException("A content key is 32 bytes.", nameof(contentKey));
        }

        /// <summary>The nonce of segment <paramref name="index"/>: 11 bytes of the index, unsigned big-endian, then 1 for the last segment and 0 otherwise.</summary>
        internal static void FillSegmentNonce(byte[] nonce, long index, bool last)
        {
            Array.Clear(nonce, 0, nonce.Length);
            for (int i = 0; i < 8; i++) nonce[10 - i] = (byte)(index >> (8 * i));
            nonce[11] = last ? (byte)1 : (byte)0;
        }

        /// <summary>
        /// Seals <paramref name="clearLength"/> bytes read from <paramref name="clear"/> into <paramref name="sealedOut"/>
        /// (item DN) holding one input and one output segment in memory, whatever the size. Throws
        /// <see cref="EndOfStreamException"/> when the source ends early and <see cref="InvalidDataException"/> when it
        /// holds more (so the caller's <c>size</c> and the bytes agree before anything is sent); the caller discards the
        /// output then. <paramref name="clearLength"/> is at least 1.
        /// </summary>
        internal static void SealStream(Stream clear, long clearLength, byte[] contentKey, Stream sealedOut)
            => SealStream(clear, clearLength, contentKey, sealedOut, out _, out _);

        /// <summary>
        /// <see cref="SealStream(Stream, long, byte[], Stream)"/> that also reports the SHA-256 of the clear bytes and
        /// of the sealed bytes it wrote - the two hashes the sender needs (the box's <c>sha256</c>, and the server's
        /// <c>clipboard-stored</c> answer to check).
        /// </summary>
        internal static void SealStream(Stream clear, long clearLength, byte[] contentKey, Stream sealedOut,
            out byte[] clearSha256, out byte[] sealedSha256)
        {
            if (clear == null) throw new ArgumentNullException(nameof(clear));
            if (sealedOut == null) throw new ArgumentNullException(nameof(sealedOut));
            long segments = SegmentCount(clearLength);

            byte[] valueKey = DeriveValueKey(contentKey);
            byte[] input = new byte[SegmentSize];
            byte[] output = new byte[SegmentSize + SegmentTagSize];
            byte[] nonce = new byte[ExchangeAesGcmKey.NonceSize];
            try
            {
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(valueKey))
                using (SHA256 clearHash = SHA256.Create())
                using (SHA256 sealedHash = SHA256.Create())
                {
                    long remaining = clearLength;
                    for (long index = 0; index < segments; index++)
                    {
                        int count = (int)Math.Min(SegmentSize, remaining);
                        if (ReadFully(clear, input, count) != count)
                            throw new EndOfStreamException("The source holds fewer bytes than its declared length.");
                        FillSegmentNonce(nonce, index, index == segments - 1);
                        aead.Seal(nonce, null, input, 0, count, output, 0);
                        clearHash.TransformBlock(input, 0, count, null, 0);
                        sealedHash.TransformBlock(output, 0, count + SegmentTagSize, null, 0);
                        sealedOut.Write(output, 0, count + SegmentTagSize);
                        remaining -= count;
                    }
                    if (clear.Read(input, 0, 1) > 0)
                        throw new InvalidDataException("The source holds more bytes than its declared length.");
                    clearHash.TransformFinalBlock(input, 0, 0);
                    sealedHash.TransformFinalBlock(output, 0, 0);
                    clearSha256 = clearHash.Hash;
                    sealedSha256 = sealedHash.Hash;
                }
            }
            finally
            {
                Wipe(valueKey);
                Wipe(input);
                Wipe(output);
            }
        }

        /// <summary>
        /// Opens a sealed value (item DN) segment by segment, writing each opened segment to
        /// <paramref name="clearOut"/> as it goes and hashing the clear bytes incrementally; at most one sealed and one
        /// clear segment are in memory. It reads exactly <paramref name="sealedLength"/> bytes from
        /// <paramref name="sealedIn"/> and never past them. Every segment is authenticated before a byte of it is
        /// written, under its own index, and the last one only under the last-segment nonce - so a missing last
        /// segment, a truncation, a reorder and an appended byte all fail (<c>bad-tag</c>, or <c>truncated</c> when the
        /// stream ends before <paramref name="sealedLength"/>; <c>malformed</c> for a length no value seals to).
        /// On failure the caller discards what <paramref name="clearOut"/> received - a value is placed only after
        /// this returns true and the box's size and SHA-256 have been checked against <paramref name="clearLength"/>
        /// and <paramref name="clearSha256"/>. I/O errors of the streams are exceptions; a CNG refusal of well-formed
        /// input is <c>crypto-failed</c>.
        /// </summary>
        internal static bool TryOpenStream(Stream sealedIn, long sealedLength, byte[] contentKey, Stream clearOut,
            out long clearLength, out byte[]? clearSha256, out string failure)
        {
            clearLength = 0;
            clearSha256 = null;
            failure = string.Empty;
            if (sealedIn == null) throw new ArgumentNullException(nameof(sealedIn));
            if (clearOut == null) throw new ArgumentNullException(nameof(clearOut));
            CheckContentKey(contentKey);

            try
            {
                return OpenStream(sealedIn, sealedLength, contentKey, clearOut, out clearLength, out clearSha256, out failure);
            }
            catch (CryptographicException)
            {
                clearLength = 0;
                clearSha256 = null;
                failure = "crypto-failed";
                return false;
            }
        }

        private static bool OpenStream(Stream sealedIn, long sealedLength, byte[] contentKey, Stream clearOut,
            out long clearLength, out byte[]? clearSha256, out string failure)
        {
            clearLength = 0;
            clearSha256 = null;
            failure = string.Empty;
            byte[] valueKey = DeriveValueKey(contentKey);

            byte[] input = new byte[SegmentSize + SegmentTagSize];
            byte[] output = new byte[SegmentSize];
            byte[] nonce = new byte[ExchangeAesGcmKey.NonceSize];
            try
            {
                if (sealedLength < SegmentTagSize + 1)
                {
                    failure = "truncated";
                    return false;
                }
                if (!TryClearLength(sealedLength, out long expectedClear))
                {
                    failure = "malformed";
                    return false;
                }
                long segments = SegmentCount(expectedClear);

                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(valueKey))
                using (SHA256 hash = SHA256.Create())
                {
                    long remaining = sealedLength;
                    long written = 0;
                    for (long index = 0; index < segments; index++)
                    {
                        int count = (int)Math.Min(SegmentSize + SegmentTagSize, remaining);
                        if (ReadFully(sealedIn, input, count) != count)
                        {
                            failure = "truncated";
                            return false;
                        }
                        FillSegmentNonce(nonce, index, index == segments - 1);
                        if (!aead.TryOpen(nonce, null, input, 0, count, output, 0))
                        {
                            failure = "bad-tag";
                            return false;
                        }
                        int clearCount = count - SegmentTagSize;
                        clearOut.Write(output, 0, clearCount);
                        hash.TransformBlock(output, 0, clearCount, null, 0);
                        written += clearCount;
                        remaining -= count;
                    }
                    hash.TransformFinalBlock(output, 0, 0);
                    clearSha256 = hash.Hash;
                    clearLength = written;
                    return true;
                }
            }
            finally
            {
                Wipe(valueKey);
                Wipe(input);
                Wipe(output);
            }
        }

        /// <summary>The sealed bytes of <paramref name="clear"/> (at least one byte) in memory - the convenience form of <see cref="SealStream(Stream, long, byte[], Stream)"/>.</summary>
        internal static byte[] SealBytes(byte[] clear, byte[] contentKey)
        {
            if (clear == null) throw new ArgumentNullException(nameof(clear));
            byte[] sealedBytes = new byte[SealedLength(clear.Length)];
            using (var source = new MemoryStream(clear, false))
            using (var target = new MemoryStream(sealedBytes, true))
                SealStream(source, clear.Length, contentKey, target);
            return sealedBytes;
        }

        /// <summary>The in-memory form of <see cref="TryOpenStream"/>. <paramref name="clear"/> is null on failure.</summary>
        internal static bool TryOpenBytes(byte[] sealedBytes, byte[] contentKey, out byte[]? clear, out string failure)
        {
            clear = null;
            if (sealedBytes == null) throw new ArgumentNullException(nameof(sealedBytes));
            CheckContentKey(contentKey);
            if (!TryClearLength(sealedBytes.Length, out long expected))
            {
                failure = sealedBytes.Length < SegmentTagSize + 1 ? "truncated" : "malformed";
                return false;
            }

            var target = new MemoryStream(checked((int)expected));
            bool opened;
            using (var source = new MemoryStream(sealedBytes, false))
                opened = TryOpenStream(source, sealedBytes.Length, contentKey, target, out _, out _, out failure);
            if (opened) clear = target.GetBuffer();
            else Wipe(target.GetBuffer());
            return opened;
        }

        // ---- private ----

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static void AppendJsonString(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    case '\b': json.Append("\\b"); break;
                    case '\f': json.Append("\\f"); break;
                    default:
                        if (c < 0x20) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }

        private static bool TryParseRoot(string? json, out Dictionary<string, object>? root)
        {
            root = null;
            if (json == null || json.Length == 0 || json.Length > MaxBoxChars) return false;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaxBoxChars, RecursionLimit = 8 };
                root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                return root != null;
            }
            catch (Exception)
            {
                // The text came off the wire. JavaScriptSerializer reports bad JSON as ArgumentException, a depth
                // overrun as InvalidOperationException and a number out of range as OverflowException or
                // FormatException; whichever it is, the box is not a box and nothing of it is read.
                return false;
            }
        }

        /// <summary>
        /// JavaScriptSerializer reads object members case-insensitively; JSON does not. A member that spells one of
        /// the contract's names in another case ("V", "Keys") is not that member - the box is refused rather than read.
        /// </summary>
        private static bool HasLookalikeName(Dictionary<string, object> values, string[] names)
        {
            foreach (string key in values.Keys)
                foreach (string name in names)
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(key, name, StringComparison.Ordinal))
                        return true;
            return false;
        }

        private static string? AsString(Dictionary<string, object> values, string name)
            => values.TryGetValue(name, out object? value) ? value as string : null;

        private static bool IsInteger(object? value, long expected)
            => (value is int i && i == expected) || (value is long l && l == expected);
    }
}
