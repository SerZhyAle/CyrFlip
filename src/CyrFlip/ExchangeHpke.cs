using System;
using System.Security.Cryptography;
using System.Text;

namespace CyrFlip
{
    /// <summary>HKDF-SHA256 (RFC 5869) on <see cref="HMACSHA256"/>.</summary>
    internal static class ExchangeHkdf
    {
        internal const int HashSize = 32;

        /// <summary><c>HKDF-Extract</c>: <c>HMAC(salt, ikm)</c>. An empty salt is a key of zero bytes, which HMAC pads with zeros - the RFC's "HashLen zeros".</summary>
        internal static byte[] Extract(byte[] salt, byte[] ikm)
        {
            using (var hmac = new HMACSHA256(salt))
                return hmac.ComputeHash(ikm);
        }

        /// <summary><c>HKDF-Expand</c> of <paramref name="length"/> bytes (at most 255 blocks).</summary>
        internal static byte[] Expand(byte[] prk, byte[] info, int length)
        {
            if (length < 0 || length > 255 * HashSize) throw new ArgumentOutOfRangeException(nameof(length));
            byte[] output = new byte[length];
            byte[] previous = new byte[0];
            using (var hmac = new HMACSHA256(prk))
            {
                int produced = 0;
                for (int counter = 1; produced < length; counter++)
                {
                    byte[] input = new byte[previous.Length + info.Length + 1];
                    Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                    Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
                    input[input.Length - 1] = (byte)counter;
                    byte[] block = hmac.ComputeHash(input);
                    Array.Clear(input, 0, input.Length);
                    Array.Clear(previous, 0, previous.Length);
                    previous = block;
                    int take = Math.Min(block.Length, length - produced);
                    Buffer.BlockCopy(block, 0, output, produced, take);
                    produced += take;
                }
            }
            Array.Clear(previous, 0, previous.Length);
            return output;
        }

        /// <summary>Extract then expand - the one-call form.</summary>
        internal static byte[] Derive(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            byte[] prk = Extract(salt, ikm);
            try { return Expand(prk, info, length); }
            finally { Array.Clear(prk, 0, prk.Length); }
        }
    }

    /// <summary>
    /// HPKE (RFC 9180) in the <b>Auth</b> mode with the one suite DEVICE-EXCHANGE item DH names:
    /// DHKEM(P-256, HKDF-SHA256) <c>0x0010</c>, HKDF-SHA256 <c>0x0001</c>, AES-128-GCM <c>0x0001</c>. Written from the
    /// RFC, line by line (LabeledExtract, LabeledExpand, ExtractAndExpand, AuthEncap, AuthDecap, KeySchedule, one
    /// Seal / Open at sequence number 0); the contract requires that an implementation passes the Auth-mode
    /// vectors of RFC 9180 Appendix A.3 before it seals anything, and <c>ExchangeE2eTests</c> holds this class to
    /// them byte for byte - <c>enc</c>, <c>shared_secret</c>, <c>key</c>, <c>base_nonce</c>, the ciphertexts.
    /// <para>
    /// The ephemeral key of an encapsulation is a parameter: production passes a fresh random one
    /// (<see cref="ExchangeDeviceKey.Generate()"/>), a test passes the vector's <c>skEm</c>.
    /// </para>
    /// </summary>
    internal static class ExchangeHpke
    {
        internal const ushort KemId = 0x0010;
        internal const ushort KdfId = 0x0001;
        internal const ushort AeadId = 0x0001;
        internal const byte ModeAuth = 0x02;

        internal const int Nsecret = 32;   // DHKEM(P-256, HKDF-SHA256) shared secret
        internal const int Nenc = 65;      // an encapsulated key is a serialized P-256 public key
        internal const int Nk = 16;        // AES-128-GCM key
        internal const int Nn = 12;        // AES-128-GCM nonce
        internal const int Nt = 16;        // AES-128-GCM tag

        /// <summary>The <c>info</c> of the contract: the ASCII bytes <c>fmsx-e2e-v1</c>.</summary>
        internal static readonly byte[] ContractInfo = Encoding.ASCII.GetBytes("fmsx-e2e-v1");

        private static readonly byte[] LabelVersion = Encoding.ASCII.GetBytes("HPKE-v1");
        private static readonly byte[] KemSuiteId = Concat(Encoding.ASCII.GetBytes("KEM"), I2Osp2(KemId));
        private static readonly byte[] HpkeSuiteId = Concat(Encoding.ASCII.GetBytes("HPKE"), I2Osp2(KemId), I2Osp2(KdfId), I2Osp2(AeadId));

        // ---- the labeled KDF calls of RFC 9180 section 4 ----

        private static byte[] LabeledExtract(byte[] suiteId, byte[] salt, string label, byte[] ikm)
        {
            byte[] labeledIkm = Concat(LabelVersion, suiteId, Encoding.ASCII.GetBytes(label), ikm);
            try { return ExchangeHkdf.Extract(salt, labeledIkm); }
            finally { Array.Clear(labeledIkm, 0, labeledIkm.Length); }
        }

        private static byte[] LabeledExpand(byte[] suiteId, byte[] prk, string label, byte[] info, int length)
        {
            byte[] labeledInfo = Concat(I2Osp2((ushort)length), LabelVersion, suiteId, Encoding.ASCII.GetBytes(label), info);
            try { return ExchangeHkdf.Expand(prk, labeledInfo, length); }
            finally { Array.Clear(labeledInfo, 0, labeledInfo.Length); }
        }

        private static byte[] ExtractAndExpand(byte[] dh, byte[] kemContext)
        {
            byte[] eaePrk = LabeledExtract(KemSuiteId, new byte[0], "eae_prk", dh);
            try { return LabeledExpand(KemSuiteId, eaePrk, "shared_secret", kemContext, Nsecret); }
            finally { Array.Clear(eaePrk, 0, eaePrk.Length); }
        }

        // ---- the KEM ----

        /// <summary>
        /// <c>AuthEncap(pkR, skS)</c> with the ephemeral key pair <paramref name="ephemeral"/> in place of a
        /// <c>GenerateKeyPair()</c>. Returns the KEM shared secret; <paramref name="enc"/> is the serialized
        /// ephemeral public key. Throws on a recipient key that is no P-256 point.
        /// </summary>
        internal static byte[] AuthEncap(byte[] recipientPublic, ExchangeDeviceKey senderPrivate, ExchangeDeviceKey ephemeral, out byte[] enc)
        {
            enc = ephemeral.PublicKem;
            byte[] dh1 = new byte[0], dh2 = new byte[0], dh = new byte[0], kemContext = new byte[0];
            try
            {
                using (var pkR = ExchangeDeviceKey.ImportPublic(recipientPublic))
                {
                    dh1 = ephemeral.DeriveRawSecret(pkR);
                    dh2 = senderPrivate.DeriveRawSecret(pkR);
                }
                dh = Concat(dh1, dh2);
                kemContext = Concat(enc, recipientPublic, senderPrivate.PublicKem);
                return ExtractAndExpand(dh, kemContext);
            }
            finally
            {
                Array.Clear(dh1, 0, dh1.Length);
                Array.Clear(dh2, 0, dh2.Length);
                Array.Clear(dh, 0, dh.Length);
            }
        }

        /// <summary>
        /// <c>AuthDecap(enc, skR, pkS)</c>. Returns the KEM shared secret, or null when <paramref name="enc"/> or
        /// <paramref name="senderPublic"/> is no P-256 point (a malformed box, not a crypto verdict).
        /// </summary>
        internal static byte[]? AuthDecap(byte[] enc, ExchangeDeviceKey recipientPrivate, byte[] senderPublic)
        {
            if (!ExchangeDeviceKey.IsValidPublicPoint(enc) || !ExchangeDeviceKey.IsValidPublicPoint(senderPublic)) return null;
            byte[] dh1 = new byte[0], dh2 = new byte[0], dh = new byte[0];
            try
            {
                using (var pkE = ExchangeDeviceKey.ImportPublic(enc))
                using (var pkS = ExchangeDeviceKey.ImportPublic(senderPublic))
                {
                    dh1 = recipientPrivate.DeriveRawSecret(pkE);
                    dh2 = recipientPrivate.DeriveRawSecret(pkS);
                }
                dh = Concat(dh1, dh2);
                byte[] kemContext = Concat(enc, recipientPrivate.PublicKem, senderPublic);
                return ExtractAndExpand(dh, kemContext);
            }
            finally
            {
                Array.Clear(dh1, 0, dh1.Length);
                Array.Clear(dh2, 0, dh2.Length);
                Array.Clear(dh, 0, dh.Length);
            }
        }

        // ---- the key schedule and the AEAD ----

        /// <summary>
        /// <c>KeySchedule(mode, shared_secret, info, psk = "", psk_id = "")</c>, giving the AES-128-GCM
        /// <paramref name="key"/> (16 bytes) and the <paramref name="baseNonce"/> (12 bytes). The caller zeroes both.
        /// </summary>
        internal static void KeySchedule(byte mode, byte[] sharedSecret, byte[] info, out byte[] key, out byte[] baseNonce)
        {
            byte[] empty = new byte[0];
            byte[] pskIdHash = LabeledExtract(HpkeSuiteId, empty, "psk_id_hash", empty);
            byte[] infoHash = LabeledExtract(HpkeSuiteId, empty, "info_hash", info);
            byte[] context = Concat(new[] { mode }, pskIdHash, infoHash);
            byte[] secret = LabeledExtract(HpkeSuiteId, sharedSecret, "secret", empty);
            try
            {
                key = LabeledExpand(HpkeSuiteId, secret, "key", context, Nk);
                baseNonce = LabeledExpand(HpkeSuiteId, secret, "base_nonce", context, Nn);
            }
            finally
            {
                Array.Clear(secret, 0, secret.Length);
            }
        }

        /// <summary>
        /// <c>SealAuth(pkR, info, aad, pt, skS)</c> at sequence number 0: returns <c>ct || tag</c> and the
        /// encapsulated key. <paramref name="ephemeral"/> is the key pair of this one encapsulation.
        /// </summary>
        internal static byte[] SealAuth(byte[] recipientPublic, ExchangeDeviceKey senderPrivate, byte[] info, byte[] aad,
            byte[] plaintext, ExchangeDeviceKey ephemeral, out byte[] enc)
        {
            byte[] sharedSecret = AuthEncap(recipientPublic, senderPrivate, ephemeral, out enc);
            byte[] key = new byte[0], baseNonce = new byte[0];
            try
            {
                KeySchedule(ModeAuth, sharedSecret, info, out key, out baseNonce);
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(key))
                {
                    byte[] sealedBytes = new byte[plaintext.Length + Nt];
                    aead.Seal(baseNonce, aad, plaintext, 0, plaintext.Length, sealedBytes, 0);   // nonce = base_nonce XOR I2OSP(0, Nn)
                    return sealedBytes;
                }
            }
            finally
            {
                Array.Clear(sharedSecret, 0, sharedSecret.Length);
                Array.Clear(key, 0, key.Length);
                Array.Clear(baseNonce, 0, baseNonce.Length);
            }
        }

        /// <summary>
        /// <c>OpenAuth(enc, skR, info, aad, ct, pkS)</c> at sequence number 0. False with <paramref name="failure"/>
        /// <c>malformed</c> when <paramref name="enc"/> or <paramref name="senderPublic"/> is no point or the
        /// ciphertext is too short, <c>bad-tag</c> when the AEAD refuses it; <paramref name="plaintext"/> is null then.
        /// </summary>
        internal static bool TryOpenAuth(byte[] enc, ExchangeDeviceKey recipientPrivate, byte[] senderPublic, byte[] info, byte[] aad,
            byte[] sealedBytes, out byte[]? plaintext, out string failure)
        {
            plaintext = null;
            failure = "malformed";
            if (sealedBytes.Length < Nt) return false;

            byte[]? sharedSecret = AuthDecap(enc, recipientPrivate, senderPublic);
            if (sharedSecret == null) return false;

            byte[] key = new byte[0], baseNonce = new byte[0];
            try
            {
                KeySchedule(ModeAuth, sharedSecret, info, out key, out baseNonce);
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(key))
                {
                    byte[] opened = new byte[sealedBytes.Length - Nt];
                    if (!aead.TryOpen(baseNonce, aad, sealedBytes, 0, sealedBytes.Length, opened, 0))
                    {
                        failure = "bad-tag";
                        return false;
                    }
                    plaintext = opened;
                    failure = string.Empty;
                    return true;
                }
            }
            finally
            {
                Array.Clear(sharedSecret, 0, sharedSecret.Length);
                Array.Clear(key, 0, key.Length);
                Array.Clear(baseNonce, 0, baseNonce.Length);
            }
        }

        // ---- byte helpers ----

        private static byte[] I2Osp2(ushort value) => new[] { (byte)(value >> 8), (byte)value };

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] part in parts) total += part.Length;
            byte[] result = new byte[total];
            int offset = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }
    }
}
