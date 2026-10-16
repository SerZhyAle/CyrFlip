using System;
using System.Numerics;
using System.Security.Cryptography;

namespace CyrFlip
{
    /// <summary>
    /// The P-256 key pair of one account entry's device (DEVICE-EXCHANGE item DF): the private scalar stays on this
    /// device and the public point travels as <c>e2eKey.kem</c> - the 65-byte uncompressed SEC 1 point
    /// <c>0x04 || X || Y</c>, base64url without padding.
    /// <para>
    /// The curve work is Windows CNG (<see cref="CngKey"/>); nothing is hand-written except the on-curve test of a
    /// public point, which is public data and does not need to be constant-time. A key is created ephemeral
    /// (<c>CngKey.Import</c> / <c>Create</c> with no name), so nothing is ever persisted by CNG: the caller stores
    /// <see cref="PrivateScalar"/> in DPAPI beside the device token (item DF) and rebuilds the key with
    /// <see cref="FromPrivateScalar"/> on the next start. The scalar is also kept in this object, zeroed by
    /// <see cref="Dispose"/>, because an imported CNG key cannot be exported again.
    /// </para>
    /// Methods may be called from several threads at once; <see cref="Dispose"/> may not race with them.
    /// </summary>
    internal sealed class ExchangeDeviceKey : IDisposable
    {
        internal const int ScalarSize = 32;
        internal const int PublicSize = 65;

        // Header of a BCRYPT_ECCKEY_BLOB: the magic, then the length of one coordinate.
        private const uint PublicMagic = 0x314B4345;    // 'ECK1' - BCRYPT_ECDH_PUBLIC_P256_MAGIC
        private const uint PrivateMagic = 0x324B4345;   // 'ECK2' - BCRYPT_ECDH_PRIVATE_P256_MAGIC

        // The order n of the group of P-256 (FIPS 186-4, D.1.2.3): a private scalar is 1 .. n-1.
        private static readonly byte[] CurveOrder =
        {
            0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xBC, 0xE6, 0xFA, 0xAD, 0xA7, 0x17, 0x9E, 0x84, 0xF3, 0xB9, 0xCA, 0xC2, 0xFC, 0x63, 0x25, 0x51,
        };

        private static readonly BigInteger FieldPrime = Parse(
            "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
        private static readonly BigInteger CurveB = Parse(
            "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");

        private readonly CngKey _key;
        private readonly byte[] _scalar;
        private readonly byte[] _public;
        private bool _disposed;

        private ExchangeDeviceKey(CngKey key, byte[] scalar, byte[] publicPoint)
        {
            _key = key;
            _scalar = scalar;
            _public = publicPoint;
        }

        /// <summary>A new key pair from the system's cryptographic random source.</summary>
        internal static ExchangeDeviceKey Generate() => Generate(ExchangeE2e.FillRandom);

        /// <summary>A new key pair whose scalar is drawn with <paramref name="fill"/> (the test seam; production passes the CSPRNG).</summary>
        internal static ExchangeDeviceKey Generate(Action<byte[]> fill)
        {
            if (fill == null) throw new ArgumentNullException(nameof(fill));
            byte[] scalar = new byte[ScalarSize];
            try
            {
                // Rejection sampling: a draw outside 1 .. n-1 happens about once in 2^32 and is simply drawn again.
                do { fill(scalar); } while (!IsValidScalar(scalar));
                return FromPrivateScalar(scalar);
            }
            finally
            {
                Array.Clear(scalar, 0, scalar.Length);
            }
        }

        /// <summary>
        /// The key pair of a private scalar (32 bytes, big-endian, 1 .. n-1) - the form <see cref="PrivateScalar"/>
        /// hands out. The public point is derived by CNG. Throws <see cref="ArgumentException"/> for a scalar of
        /// another length or outside the group order; the caller's array is not kept.
        /// </summary>
        internal static ExchangeDeviceKey FromPrivateScalar(byte[] scalar32)
        {
            if (scalar32 == null) throw new ArgumentNullException(nameof(scalar32));
            if (scalar32.Length != ScalarSize) throw new ArgumentException("A P-256 private scalar is 32 bytes.", nameof(scalar32));
            if (!IsValidScalar(scalar32)) throw new ArgumentException("The private scalar is not in 1 .. n-1.", nameof(scalar32));

            // BCRYPT_ECCKEY_BLOB of a private key: header, X, Y, d. CNG computes the public point from d when
            // X and Y are left zero (checked against the RFC 9180 A.3 key pairs).
            byte[] blob = new byte[8 + 3 * ScalarSize];
            try
            {
                WriteHeader(blob, PrivateMagic);
                Buffer.BlockCopy(scalar32, 0, blob, 8 + 2 * ScalarSize, ScalarSize);
                CngKey key = CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob);
                try
                {
                    byte[] publicBlob = key.Export(CngKeyBlobFormat.EccPublicBlob);
                    if (publicBlob.Length != 8 + 2 * ScalarSize) throw new CryptographicException("CNG exported an unexpected P-256 public key.");
                    byte[] point = new byte[PublicSize];
                    point[0] = 0x04;
                    Buffer.BlockCopy(publicBlob, 8, point, 1, 2 * ScalarSize);
                    return new ExchangeDeviceKey(key, (byte[])scalar32.Clone(), point);
                }
                catch
                {
                    key.Dispose();
                    throw;
                }
            }
            finally
            {
                Array.Clear(blob, 0, blob.Length);
            }
        }

        /// <summary>
        /// The private scalar, 32 bytes big-endian, for the caller to seal into DPAPI. Each call returns a new copy
        /// that the caller owns and should zero (<see cref="ExchangeE2e.Wipe"/>) once stored.
        /// </summary>
        internal byte[] PrivateScalar
        {
            get
            {
                ThrowIfDisposed();
                return (byte[])_scalar.Clone();
            }
        }

        /// <summary>The public key as the 65-byte uncompressed SEC 1 point <c>0x04 || X || Y</c> (a new copy per call).</summary>
        internal byte[] PublicKem
        {
            get
            {
                ThrowIfDisposed();
                return (byte[])_public.Clone();
            }
        }

        /// <summary>The public key as base64url without padding - the <c>kem</c> of <c>e2eKey</c>.</summary>
        internal string PublicKemBase64Url
        {
            get
            {
                ThrowIfDisposed();
                return ExchangeE2e.Base64UrlEncode(_public);
            }
        }

        /// <summary>The raw ECDH secret with a peer's public point (32 bytes, big-endian x). Throws on a point that is no key.</summary>
        internal byte[] DeriveRawSecret(byte[] peerPublic65)
        {
            using (CngKey peer = ImportPublic(peerPublic65))
                return DeriveRawSecret(peer);
        }

        internal byte[] DeriveRawSecret(CngKey peerPublic)
        {
            ThrowIfDisposed();
            return ExchangeE2eNative.EcdhRawSecret(_key, peerPublic);
        }

        /// <summary>
        /// A CNG public key for an uncompressed P-256 point. The shape and the curve equation are checked here, apart
        /// from CNG, so an invalid point is refused the same way on every Windows. Throws <see cref="ArgumentException"/>.
        /// </summary>
        internal static CngKey ImportPublic(byte[] point65)
        {
            if (!IsValidPublicPoint(point65)) throw new ArgumentException("Not a P-256 point in uncompressed form.", nameof(point65));
            byte[] blob = new byte[8 + 2 * ScalarSize];
            WriteHeader(blob, PublicMagic);
            Buffer.BlockCopy(point65, 1, blob, 8, 2 * ScalarSize);
            return CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob);
        }

        /// <summary>True for 65 bytes <c>0x04 || X || Y</c> with X and Y below the field prime and Y^2 = X^3 - 3X + b.</summary>
        internal static bool IsValidPublicPoint(byte[]? point65)
        {
            if (point65 == null || point65.Length != PublicSize || point65[0] != 0x04) return false;
            BigInteger x = FromBigEndian(point65, 1, ScalarSize);
            BigInteger y = FromBigEndian(point65, 1 + ScalarSize, ScalarSize);
            if (x >= FieldPrime || y >= FieldPrime) return false;
            BigInteger left = BigInteger.Remainder(y * y, FieldPrime);
            BigInteger right = BigInteger.Remainder(x * x * x - 3 * x + CurveB, FieldPrime);
            if (right.Sign < 0) right += FieldPrime;
            return left == right;
        }

        /// <summary>True for a 32-byte big-endian value in 1 .. n-1.</summary>
        internal static bool IsValidScalar(byte[] scalar32)
        {
            if (scalar32.Length != ScalarSize) return false;
            bool zero = true;
            for (int i = 0; i < ScalarSize; i++) if (scalar32[i] != 0) { zero = false; break; }
            if (zero) return false;
            for (int i = 0; i < ScalarSize; i++)
            {
                if (scalar32[i] < CurveOrder[i]) return true;
                if (scalar32[i] > CurveOrder[i]) return false;
            }
            return false;   // equal to n
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Array.Clear(_scalar, 0, _scalar.Length);
            _key.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExchangeDeviceKey));
        }

        private static void WriteHeader(byte[] blob, uint magic)
        {
            // Little-endian, as the structure is laid out in memory.
            blob[0] = (byte)magic; blob[1] = (byte)(magic >> 8); blob[2] = (byte)(magic >> 16); blob[3] = (byte)(magic >> 24);
            blob[4] = ScalarSize; blob[5] = 0; blob[6] = 0; blob[7] = 0;
        }

        private static BigInteger FromBigEndian(byte[] bytes, int offset, int count)
        {
            byte[] little = new byte[count + 1];   // the extra zero keeps the number positive
            for (int i = 0; i < count; i++) little[i] = bytes[offset + count - 1 - i];
            return new BigInteger(little);
        }

        private static BigInteger Parse(string hex) => BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.AllowHexSpecifier);
    }
}
