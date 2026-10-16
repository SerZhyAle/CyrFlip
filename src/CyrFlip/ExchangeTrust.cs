using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CyrFlip
{
    /// <summary>
    /// The pin an account entry keeps for its server's leaf certificate (<c>DEVICE-EXCHANGE</c> 7.1, items D and
    /// AY): two values of the one leaf that was accepted - its own fingerprint and the fingerprint of its public
    /// key. Both are written <c>SHA256:</c> plus base64 without padding. An empty pin means the server's
    /// certificate is validated by Windows, which pins nothing.
    /// </summary>
    internal sealed class ExchangePin
    {
        public static readonly ExchangePin None = new ExchangePin("", "");

        public readonly string Leaf;
        public readonly string Key;

        public ExchangePin(string leaf, string key)
        {
            Leaf = leaf ?? "";
            Key = key ?? "";
        }

        public bool IsEmpty => Leaf.Length == 0;
    }

    internal enum ExchangeTrustKind
    {
        /// <summary>No pin, and Windows trusts the chain for the host: nothing is recorded.</summary>
        Platform,
        /// <summary>The leaf is the pinned one.</summary>
        PinMatched,
        /// <summary>Another leaf with the pinned key: a renewal, accepted without a prompt (item AY).</summary>
        PinRenewed,
        /// <summary>No pin and Windows does not trust the chain: the person must be shown the fingerprint (item D).</summary>
        NeedsAcceptance,
        /// <summary>A pin exists and the leaf has another key: refused, both fingerprints shown (item D).</summary>
        Changed,
    }

    internal sealed class ExchangeTrustResult
    {
        public ExchangeTrustKind Kind;
        public string ObservedLeaf = "";
        public string ObservedKey = "";
        public SslPolicyErrors PolicyErrors;

        /// <summary>Whether a connection may go on to send a byte.</summary>
        public bool Accepts => Kind == ExchangeTrustKind.Platform
                               || Kind == ExchangeTrustKind.PinMatched
                               || Kind == ExchangeTrustKind.PinRenewed;

        /// <summary>The pin an accepted connection leaves behind: the old one, or the renewal's.</summary>
        public ExchangePin PinAfter(ExchangePin before)
        {
            if (Kind == ExchangeTrustKind.PinRenewed) return new ExchangePin(ObservedLeaf, ObservedKey);
            return before;
        }
    }

    /// <summary>The trust rule of items D and AY, pure, and the two fingerprints it reads off a certificate.</summary>
    internal static class ExchangeTrust
    {
        public static ExchangeTrustResult Evaluate(ExchangePin pin, string observedLeaf, string observedKey, SslPolicyErrors errors)
        {
            var result = new ExchangeTrustResult { ObservedLeaf = observedLeaf, ObservedKey = observedKey, PolicyErrors = errors };

            if (!pin.IsEmpty)
            {
                // For a pinned self-signed leaf the pin is the trust: a name or a date that Windows
                // dislikes is not a reason to refuse it (item AY).
                if (string.Equals(pin.Leaf, observedLeaf, StringComparison.Ordinal))
                    result.Kind = ExchangeTrustKind.PinMatched;
                else if (pin.Key.Length > 0 && string.Equals(pin.Key, observedKey, StringComparison.Ordinal))
                    result.Kind = ExchangeTrustKind.PinRenewed;
                else
                    result.Kind = ExchangeTrustKind.Changed;
                return result;
            }

            result.Kind = errors == SslPolicyErrors.None ? ExchangeTrustKind.Platform : ExchangeTrustKind.NeedsAcceptance;
            return result;
        }

        public static ExchangeTrustResult Evaluate(ExchangePin pin, X509Certificate? certificate, SslPolicyErrors errors)
        {
            if (certificate == null)
                return new ExchangeTrustResult { Kind = ExchangeTrustKind.Changed, PolicyErrors = errors };
            byte[] der = certificate.GetRawCertData();
            return Evaluate(pin, LeafPin(der), KeyPin(der), errors);
        }

        /// <summary><c>SHA256:</c> and the base64 of the SHA-256 of the DER leaf, without padding.</summary>
        public static string LeafPin(byte[] certificateDer) => Pin(certificateDer);

        /// <summary>The same over the leaf's SubjectPublicKeyInfo; empty when the certificate cannot be walked.</summary>
        public static string KeyPin(byte[] certificateDer)
        {
            byte[]? spki = SubjectPublicKeyInfo(certificateDer);
            return spki == null ? "" : Pin(spki);
        }

        private static string Pin(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return "SHA256:" + Convert.ToBase64String(sha.ComputeHash(data)).TrimEnd('=');
        }

        /// <summary>
        /// The DER SubjectPublicKeyInfo of an X.509 certificate: Certificate, tbsCertificate, then past the
        /// optional version, the serial, the signature algorithm, the issuer, the validity and the subject.
        /// A certificate that does not walk that way yields null rather than a guess.
        /// </summary>
        public static byte[]? SubjectPublicKeyInfo(byte[] der)
        {
            try
            {
                int pos = 0;
                if (!Enter(der, ref pos, 0x30, out int certEnd)) return null;
                if (!Enter(der, ref pos, 0x30, out int tbsEnd)) return null;
                if (pos < tbsEnd && der[pos] == 0xA0 && !Skip(der, ref pos)) return null; // [0] version
                for (int i = 0; i < 5; i++) // serial, signature, issuer, validity, subject
                    if (pos >= tbsEnd || !Skip(der, ref pos)) return null;
                int start = pos;
                if (pos >= tbsEnd || der[pos] != 0x30 || !Skip(der, ref pos)) return null;
                if (pos > tbsEnd || certEnd > der.Length) return null;
                byte[] spki = new byte[pos - start];
                Buffer.BlockCopy(der, start, spki, 0, spki.Length);
                return spki;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static bool Enter(byte[] d, ref int pos, byte tag, out int end)
        {
            end = 0;
            if (pos >= d.Length || d[pos] != tag) return false;
            if (!ReadLength(d, pos + 1, out int len, out int lenBytes)) return false;
            pos += 1 + lenBytes;
            end = pos + len;
            return end <= d.Length;
        }

        private static bool Skip(byte[] d, ref int pos)
        {
            if (pos >= d.Length) return false;
            if (!ReadLength(d, pos + 1, out int len, out int lenBytes)) return false;
            long next = (long)pos + 1 + lenBytes + len;
            if (next > d.Length) return false;
            pos = (int)next;
            return true;
        }

        private static bool ReadLength(byte[] d, int at, out int length, out int lenBytes)
        {
            length = 0;
            lenBytes = 1;
            if (at >= d.Length) return false;
            int first = d[at];
            if (first < 0x80) { length = first; return true; }
            int count = first & 0x7F;
            if (count == 0 || count > 3 || at + count >= d.Length) return false;
            for (int i = 1; i <= count; i++) length = (length << 8) | d[at + i];
            lenBytes = 1 + count;
            return length >= 0;
        }
    }
}
