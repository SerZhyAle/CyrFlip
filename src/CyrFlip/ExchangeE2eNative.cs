using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CyrFlip
{
    /// <summary>
    /// The Windows CNG calls behind the sealed-content layer of DEVICE-EXCHANGE section 27 (items DF-DH, DN):
    /// AES-GCM through <c>bcrypt.dll</c> and the raw ECDH secret of P-256 through <c>ncrypt.dll</c>. net48 has
    /// neither <c>AesGcm</c> nor <c>DeriveRawSecretAgreement</c>, and the product ships with no NuGet package, so
    /// this is the whole of the dependency: two system DLLs that every supported Windows already has.
    /// <para>
    /// Nothing here knows a protocol. <see cref="ExchangeHpke"/> and <see cref="ExchangeE2e"/> build on it.
    /// Every handle is a <see cref="SafeHandle"/> and every pinned buffer is released in a <c>finally</c>.
    /// </para>
    /// </summary>
    internal static class ExchangeE2eNative
    {
        // NTSTATUS of a failed GCM tag: BCryptDecrypt answers it when the ciphertext, the nonce, the
        // associated data or the tag differ from what was sealed.
        private const int StatusAuthTagMismatch = unchecked((int)0xC000A002);

        private const string AesAlgorithm = "AES";
        private const string ChainingModeProperty = "ChainingMode";   // BCRYPT_CHAINING_MODE
        private const string ChainingModeGcm = "ChainingModeGCM";     // BCRYPT_CHAIN_MODE_GCM
        private const string KdfRawSecret = "TRUNCATE";               // BCRYPT_KDF_RAW_SECRET

        /// <summary><c>BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO</c> (bcrypt.h); 88 bytes on x64.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct AuthenticatedCipherModeInfo
        {
            public int cbSize;
            public int dwInfoVersion;
            public IntPtr pbNonce;
            public int cbNonce;
            public IntPtr pbAuthData;
            public int cbAuthData;
            public IntPtr pbTag;
            public int cbTag;
            public IntPtr pbMacContext;
            public int cbMacContext;
            public int cbAAD;
            public long cbData;
            public int dwFlags;
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int BCryptOpenAlgorithmProvider(out BCryptAlgorithmHandle phAlgorithm, string pszAlgId, string? pszImplementation, uint dwFlags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        internal static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, uint dwFlags);

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int BCryptSetProperty(BCryptAlgorithmHandle hObject, string pszProperty, byte[] pbInput, int cbInput, uint dwFlags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptGenerateSymmetricKey(BCryptAlgorithmHandle hAlgorithm, out BCryptKeyHandle phKey, IntPtr pbKeyObject, int cbKeyObject, byte[] pbSecret, int cbSecret, uint dwFlags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        internal static extern int BCryptDestroyKey(IntPtr hKey);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptEncrypt(BCryptKeyHandle hKey, IntPtr pbInput, int cbInput, ref AuthenticatedCipherModeInfo pPaddingInfo, IntPtr pbIV, int cbIV, IntPtr pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptDecrypt(BCryptKeyHandle hKey, IntPtr pbInput, int cbInput, ref AuthenticatedCipherModeInfo pPaddingInfo, IntPtr pbIV, int cbIV, IntPtr pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int NCryptDeriveKey(SafeNCryptSecretHandle hSharedSecret, string pwszKDF, IntPtr pParameterList, byte[]? pbDerivedKey, int cbDerivedKey, out int pcbResult, uint dwFlags);

        internal static ExchangeAesGcmKey NewAesGcmKey(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("An AES key is 16, 24 or 32 bytes.", nameof(key));

            BCryptAlgorithmHandle? algorithm = null;
            try
            {
                Check(BCryptOpenAlgorithmProvider(out algorithm, AesAlgorithm, null, 0));
                byte[] mode = Encoding.Unicode.GetBytes(ChainingModeGcm + "\0");
                Check(BCryptSetProperty(algorithm, ChainingModeProperty, mode, mode.Length, 0));
                Check(BCryptGenerateSymmetricKey(algorithm, out BCryptKeyHandle handle, IntPtr.Zero, 0, key, key.Length, 0));
                var result = new ExchangeAesGcmKey(algorithm, handle);
                algorithm = null;   // owned by the key from here on
                return result;
            }
            finally
            {
                algorithm?.Dispose();
            }
        }

        internal static void Check(int status)
        {
            if (status < 0) throw new CryptographicException(status);
        }

        /// <summary>
        /// The raw ECDH secret of P-256 as RFC 9180 and SEC 1 write it: the 32-byte big-endian x coordinate of
        /// the shared point. CNG hands it over through the <c>TRUNCATE</c> KDF (<c>BCRYPT_KDF_RAW_SECRET</c>)
        /// in little-endian order, so it is reversed here - the RFC 9180 Appendix A.3 vector holds that.
        /// </summary>
        internal static byte[] EcdhRawSecret(CngKey own, CngKey peerPublic)
        {
            using (var ecdh = new ECDiffieHellmanCng(own))
            using (SafeNCryptSecretHandle secret = ecdh.DeriveSecretAgreementHandle(peerPublic))
            {
                Check(NCryptDeriveKey(secret, KdfRawSecret, IntPtr.Zero, null, 0, out int size, 0));
                if (size != 32) throw new CryptographicException("The ECDH secret of P-256 is 32 bytes.");

                byte[] raw = new byte[size];
                try
                {
                    Check(NCryptDeriveKey(secret, KdfRawSecret, IntPtr.Zero, raw, raw.Length, out int written, 0));
                    if (written != size) throw new CryptographicException("The ECDH secret came back short.");
                    byte[] bigEndian = new byte[size];
                    for (int i = 0; i < size; i++) bigEndian[i] = raw[size - 1 - i];
                    return bigEndian;
                }
                finally
                {
                    Array.Clear(raw, 0, raw.Length);
                }
            }
        }

        /// <summary>
        /// One <c>BCryptEncrypt</c> / <c>BCryptDecrypt</c> call in GCM mode: pins the buffers, fills the
        /// cipher-mode info (nonce, associated data, the tag's place), calls, and unpins in a <c>finally</c>.
        /// The tag lives in <paramref name="tagBuffer"/> at <paramref name="tagOffset"/> - right behind the
        /// ciphertext, so no copy is made. <paramref name="status"/> is the raw NTSTATUS.
        /// </summary>
        internal static void AeadCall(BCryptKeyHandle key, bool encrypt, byte[] nonce, byte[]? aad,
            byte[] input, int inputOffset, int length, byte[] output, int outputOffset, byte[] tagBuffer, int tagOffset,
            out int status, out int written)
        {
            GCHandle pinNonce = default, pinAad = default, pinInput = default, pinOutput = default, pinTag = default;
            try
            {
                pinNonce = GCHandle.Alloc(nonce, GCHandleType.Pinned);
                pinInput = GCHandle.Alloc(input, GCHandleType.Pinned);
                pinOutput = GCHandle.Alloc(output, GCHandleType.Pinned);
                pinTag = GCHandle.Alloc(tagBuffer, GCHandleType.Pinned);

                var info = new AuthenticatedCipherModeInfo
                {
                    cbSize = Marshal.SizeOf(typeof(AuthenticatedCipherModeInfo)),
                    dwInfoVersion = 1,   // BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO_VERSION
                    pbNonce = pinNonce.AddrOfPinnedObject(),
                    cbNonce = nonce.Length,
                    pbTag = Offset(pinTag.AddrOfPinnedObject(), tagOffset),
                    cbTag = ExchangeAesGcmKey.TagSize,
                };
                if (aad != null && aad.Length > 0)
                {
                    pinAad = GCHandle.Alloc(aad, GCHandleType.Pinned);
                    info.pbAuthData = pinAad.AddrOfPinnedObject();
                    info.cbAuthData = aad.Length;
                }

                IntPtr source = Offset(pinInput.AddrOfPinnedObject(), inputOffset);
                IntPtr target = Offset(pinOutput.AddrOfPinnedObject(), outputOffset);
                status = encrypt
                    ? BCryptEncrypt(key, source, length, ref info, IntPtr.Zero, 0, target, length, out written, 0)
                    : BCryptDecrypt(key, source, length, ref info, IntPtr.Zero, 0, target, length, out written, 0);
            }
            finally
            {
                if (pinNonce.IsAllocated) pinNonce.Free();
                if (pinAad.IsAllocated) pinAad.Free();
                if (pinInput.IsAllocated) pinInput.Free();
                if (pinOutput.IsAllocated) pinOutput.Free();
                if (pinTag.IsAllocated) pinTag.Free();
            }
        }

        internal static bool IsTagMismatch(int status) => status == StatusAuthTagMismatch;

        private static IntPtr Offset(IntPtr pointer, int offset) => new IntPtr(pointer.ToInt64() + offset);
    }

    /// <summary>A <c>BCRYPT_ALG_HANDLE</c> of the AES provider.</summary>
    internal sealed class BCryptAlgorithmHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public BCryptAlgorithmHandle() : base(true) { }

        protected override bool ReleaseHandle() => ExchangeE2eNative.BCryptCloseAlgorithmProvider(handle, 0) == 0;
    }

    /// <summary>A <c>BCRYPT_KEY_HANDLE</c> of one symmetric key.</summary>
    internal sealed class BCryptKeyHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public BCryptKeyHandle() : base(true) { }

        protected override bool ReleaseHandle() => ExchangeE2eNative.BCryptDestroyKey(handle) == 0;
    }

    /// <summary>
    /// One AES key bound to the GCM mode, reusable for any number of messages: the key handle is made once, so a
    /// value of several gigabytes sealed in 64 KiB segments costs one key and not one per segment. 12-byte nonce,
    /// 16-byte tag, the tag stored right behind the ciphertext (<c>ciphertext || tag</c>).
    /// <para>
    /// A key is for one thread at a time (CNG makes no promise about concurrent use of a key handle); the callers
    /// here make one per message or per value, which is cheap beside the work the key then does.
    /// </para>
    /// </summary>
    internal sealed class ExchangeAesGcmKey : IDisposable
    {
        internal const int NonceSize = 12;
        internal const int TagSize = 16;

        private readonly BCryptAlgorithmHandle _algorithm;   // must outlive the key handle
        private readonly BCryptKeyHandle _key;

        internal ExchangeAesGcmKey(BCryptAlgorithmHandle algorithm, BCryptKeyHandle key)
        {
            _algorithm = algorithm;
            _key = key;
        }

        /// <summary>Seals <paramref name="length"/> bytes of <paramref name="source"/>; writes <c>length + 16</c> bytes at <paramref name="target"/>.</summary>
        internal void Seal(byte[] nonce, byte[]? aad, byte[] source, int sourceOffset, int length, byte[] target, int targetOffset)
        {
            CheckRange(source, sourceOffset, length, "source");
            CheckRange(target, targetOffset, length + TagSize, "target");
            CheckNonce(nonce);

            ExchangeE2eNative.AeadCall(_key, true, nonce, aad, source, sourceOffset, length, target, targetOffset,
                target, targetOffset + length, out int status, out int written);
            ExchangeE2eNative.Check(status);
            if (written != length) throw new CryptographicException("AES-GCM sealed an unexpected number of bytes.");
        }

        /// <summary>
        /// Opens <paramref name="sealedLength"/> bytes (<c>ciphertext || tag</c>) of <paramref name="source"/> into
        /// <paramref name="target"/> (<c>sealedLength - 16</c> bytes). False on a failed tag, with the target range
        /// cleared so no unauthenticated plaintext is left behind; a native failure of another kind throws.
        /// </summary>
        internal bool TryOpen(byte[] nonce, byte[]? aad, byte[] source, int sourceOffset, int sealedLength, byte[] target, int targetOffset)
        {
            if (sealedLength < TagSize) throw new ArgumentOutOfRangeException(nameof(sealedLength));
            int length = sealedLength - TagSize;
            CheckRange(source, sourceOffset, sealedLength, "source");
            CheckRange(target, targetOffset, length, "target");
            CheckNonce(nonce);

            ExchangeE2eNative.AeadCall(_key, false, nonce, aad, source, sourceOffset, length, target, targetOffset,
                source, sourceOffset + length, out int status, out int written);
            if (status >= 0 && written == length) return true;

            Array.Clear(target, targetOffset, length);
            if (ExchangeE2eNative.IsTagMismatch(status)) return false;
            ExchangeE2eNative.Check(status);
            return false;
        }

        public void Dispose()
        {
            _key.Dispose();
            _algorithm.Dispose();
        }

        private static void CheckNonce(byte[] nonce)
        {
            if (nonce == null) throw new ArgumentNullException(nameof(nonce));
            if (nonce.Length != NonceSize) throw new ArgumentException("A GCM nonce is 12 bytes.", nameof(nonce));
        }

        private static void CheckRange(byte[] buffer, int offset, int count, string name)
        {
            if (buffer == null) throw new ArgumentNullException(name);
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
