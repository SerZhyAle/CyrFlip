using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// DEVICE-EXCHANGE section 27, items DF, DG, DH and DN: the sealed-content layer (<see cref="ExchangeE2e"/>,
    /// <see cref="ExchangeHpke"/>, <see cref="ExchangeDeviceKey"/>, <see cref="ExchangeE2eNative"/>). Held to four
    /// outside authorities, none of them written here:
    /// <list type="bullet">
    /// <item>RFC 9180 Appendix A.3, the Auth-mode vector of DHKEM(P-256, HKDF-SHA256) / HKDF-SHA256 / AES-128-GCM
    /// - the contract requires an implementation to pass it before it seals anything;</item>
    /// <item>the AES-GCM known answers of the GCM specification (McGrew and Viega, test cases 1, 2, 4, 13, 14, 16)
    /// and the HKDF cases of RFC 5869;</item>
    /// <item><c>vectors/e2e-v1.json</c> of the catalog (the Android product's file: device keys, boxes, the failing
    /// cases, the clipboard values);</item>
    /// <item>the <c>sealed</c> member of the Windows server corpus of ticket 234 (12 boxes sealed by an independent
    /// sealer over the same device keys).</item>
    /// </list>
    /// The catalog is not on a CI runner, so both files are byte copies under <c>Fixtures/DeviceExchange/</c>, pinned
    /// by SHA-256 below. The <c>*.json</c> rule of <c>.gitattributes</c> may check them out with CRLF, so the pin is
    /// taken over the LF form - the form of the catalog.
    /// </summary>
    public class ExchangeE2eTests
    {
        private static readonly string FixtureFolder = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures", "DeviceExchange"));

        // ================================================================ the vector files

        [Theory]
        // The contract states this hash for vectors/e2e-v1.json (README, items DE to DR, conformance artifacts).
        [InlineData("e2e-v1.json", "5391a9ae33450c5de82b622278a5acb38773bdf2819ebe8ff8604114758fde35")]
        // The `sealed` member of proposed-vectors/fmsw-234-2026-10-16/frames-v2.json (whole file SHA-256
        // 7ddf9fb472b825efcc77d54405681cdbf0f2e2b485e0b58bc7782bddfce5d2fd), cut out verbatim: the text from the `{`
        // after `"sealed": ` to its closing `}`.
        [InlineData("fmsw-234-sealed.json", "ed85b761de097f989ad52434e4af8945d82b939123867836d9a06abde70ffb55")]
        public void TheVectorFilesAreByteIdenticalToTheCatalogsCopies(string name, string sha256)
        {
            byte[] bytes = StripCarriageReturns(File.ReadAllBytes(Path.Combine(FixtureFolder, name)));
            Assert.Equal(sha256, ToHex(Sha256(bytes)));
        }

        // ================================================================ RFC 9180 Appendix A.3, Auth mode

        // DHKEM(P-256, HKDF-SHA256), HKDF-SHA256, AES-128-GCM, mode_auth (0x02); info "Ode on a Grecian Urn".
        private const string RfcInfo = "4f6465206f6e2061204772656369616e2055726e";
        private const string RfcSkRm = "d929ab4be2e59f6954d6bedd93e638f02d4046cef21115b00cdda2acb2a4440e";
        private const string RfcSkSm = "1120ac99fb1fccc1e8230502d245719d1b217fe20505c7648795139d177f0de9";
        private const string RfcSkEm = "6b8de0873aed0c1b2d09b8c7ed54cbf24fdf1dfc7a47fa501f918810642d7b91";
        private const string RfcPkRm = "04423e363e1cd54ce7b7573110ac121399acbc9ed815fae03b72ffbd4c18b01836835c5a09513f28fc971b7266cfde2e96afe84bb0f266920e82c4f53b36e1a78d";
        private const string RfcPkSm = "04a817a0902bf28e036d66add5d544cc3a0457eab150f104285df1e293b5c10eef8651213e43d9cd9086c80b309df22cf37609f58c1127f7607e85f210b2804f73";
        private const string RfcPkEm = "042224f3ea800f7ec55c03f29fc9865f6ee27004f818fcbdc6dc68932c1e52e15b79e264a98f2c535ef06745f3d308624414153b22c7332bc1e691cb4af4d53454";
        private const string RfcSharedSecret = "d4aea336439aadf68f9348880aa358086f1480e7c167b6ef15453ba69b94b44f";
        private const string RfcKey = "19aa8472b3fdc530392b0e54ca17c0f5";
        private const string RfcBaseNonce = "b390052d26b67a5b8a8fcaa4";
        private const string RfcPlaintext = "4265617574792069732074727574682c20747275746820626561757479";
        private const string RfcAad0 = "436f756e742d30";
        private const string RfcCiphertext0 = "82ffc8c44760db691a07c5627e5fc2c08e7a86979ee79b494a17cc3405446ac2bdb8f265db4a099ed3289ffe19";
        private const string RfcAad1 = "436f756e742d31";
        private const string RfcNonce1 = "b390052d26b67a5b8a8fcaa5";
        private const string RfcCiphertext1 = "b0a705a54532c7b4f5907de51c13dffe1e08d55ee9ba59686114b05945494d96725b239468f1229e3966aa1250";

        [Fact]
        public void Rfc9180A3_TheKeyPairsOfTheVectorDeriveTheirPublicPoints()
        {
            foreach (var pair in new[] { (RfcSkRm, RfcPkRm), (RfcSkSm, RfcPkSm), (RfcSkEm, RfcPkEm) })
                using (ExchangeDeviceKey key = ExchangeDeviceKey.FromPrivateScalar(Hex(pair.Item1)))
                    Assert.Equal(pair.Item2, ToHex(key.PublicKem));
        }

        [Fact]
        public void Rfc9180A3_AuthEncapGivesTheVectorsEncAndSharedSecret()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkSm)))
            using (ExchangeDeviceKey ephemeral = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkEm)))
            {
                byte[] shared = ExchangeHpke.AuthEncap(Hex(RfcPkRm), sender, ephemeral, out byte[] enc);
                Assert.Equal(RfcPkEm, ToHex(enc));
                Assert.Equal(RfcSharedSecret, ToHex(shared));
            }
        }

        [Fact]
        public void Rfc9180A3_AuthDecapGivesTheSameSharedSecret()
        {
            using (ExchangeDeviceKey recipient = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkRm)))
            {
                byte[]? shared = ExchangeHpke.AuthDecap(Hex(RfcPkEm), recipient, Hex(RfcPkSm));
                Assert.NotNull(shared);
                Assert.Equal(RfcSharedSecret, ToHex(shared!));
            }
        }

        [Fact]
        public void Rfc9180A3_TheKeyScheduleGivesTheVectorsKeyAndBaseNonce()
        {
            ExchangeHpke.KeySchedule(ExchangeHpke.ModeAuth, Hex(RfcSharedSecret), Hex(RfcInfo), out byte[] key, out byte[] baseNonce);
            Assert.Equal(RfcKey, ToHex(key));
            Assert.Equal(RfcBaseNonce, ToHex(baseNonce));
        }

        [Fact]
        public void Rfc9180A3_SealAuthGivesTheVectorsCiphertextAndEnc()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkSm)))
            using (ExchangeDeviceKey ephemeral = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkEm)))
            {
                byte[] sealedBytes = ExchangeHpke.SealAuth(Hex(RfcPkRm), sender, Hex(RfcInfo), Hex(RfcAad0), Hex(RfcPlaintext), ephemeral, out byte[] enc);
                Assert.Equal(RfcPkEm, ToHex(enc));
                Assert.Equal(RfcCiphertext0, ToHex(sealedBytes));
            }
        }

        [Fact]
        public void Rfc9180A3_OpenAuthOpensTheVectorsCiphertext()
        {
            using (ExchangeDeviceKey recipient = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkRm)))
            {
                Assert.True(ExchangeHpke.TryOpenAuth(Hex(RfcPkEm), recipient, Hex(RfcPkSm), Hex(RfcInfo), Hex(RfcAad0), Hex(RfcCiphertext0), out byte[]? plaintext, out string failure));
                Assert.Equal(RfcPlaintext, ToHex(plaintext!));
                Assert.Equal(string.Empty, failure);
            }
        }

        [Fact]
        public void Rfc9180A3_OpenAuthRefusesAChangedAadAnotherSenderAndATamperedCiphertext()
        {
            using (ExchangeDeviceKey recipient = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkRm)))
            using (ExchangeDeviceKey stranger = ExchangeDeviceKey.Generate())
            {
                // the aad of the second message of the vector is not the aad of the first
                Assert.False(ExchangeHpke.TryOpenAuth(Hex(RfcPkEm), recipient, Hex(RfcPkSm), Hex(RfcInfo), Hex(RfcAad1), Hex(RfcCiphertext0), out byte[]? p1, out string f1));
                Assert.Null(p1);
                Assert.Equal("bad-tag", f1);

                // Auth mode binds the sender: another sender's key derives another secret
                Assert.False(ExchangeHpke.TryOpenAuth(Hex(RfcPkEm), recipient, stranger.PublicKem, Hex(RfcInfo), Hex(RfcAad0), Hex(RfcCiphertext0), out byte[]? p2, out string f2));
                Assert.Null(p2);
                Assert.Equal("bad-tag", f2);

                byte[] flipped = Hex(RfcCiphertext0);
                flipped[3] ^= 0x01;
                Assert.False(ExchangeHpke.TryOpenAuth(Hex(RfcPkEm), recipient, Hex(RfcPkSm), Hex(RfcInfo), Hex(RfcAad0), flipped, out byte[]? p3, out string f3));
                Assert.Null(p3);
                Assert.Equal("bad-tag", f3);

                // an enc that is no point is malformed, not a verdict of the cipher
                byte[] off = Hex(RfcPkEm);
                off[64] ^= 0x01;
                Assert.False(ExchangeHpke.TryOpenAuth(off, recipient, Hex(RfcPkSm), Hex(RfcInfo), Hex(RfcAad0), Hex(RfcCiphertext0), out byte[]? p4, out string f4));
                Assert.Null(p4);
                Assert.Equal("malformed", f4);
            }
        }

        [Fact]
        public void Rfc9180A3_TheSecondMessageOfTheVectorOpensUnderTheSequenceNumberOneNonce()
        {
            // nonce = base_nonce XOR I2OSP(seq, Nn): the vector's second encryption uses ...a5 for ...a4
            byte[] nonce = Hex(RfcBaseNonce);
            nonce[nonce.Length - 1] ^= 0x01;
            Assert.Equal(RfcNonce1, ToHex(nonce));
            using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(Hex(RfcKey)))
            {
                byte[] sealedBytes = new byte[Hex(RfcPlaintext).Length + 16];
                aead.Seal(nonce, Hex(RfcAad1), Hex(RfcPlaintext), 0, Hex(RfcPlaintext).Length, sealedBytes, 0);
                Assert.Equal(RfcCiphertext1, ToHex(sealedBytes));
            }
        }

        // ================================================================ AES-GCM known answers

        [Theory]
        // McGrew and Viega, "The Galois/Counter Mode of Operation", appendix B. Each row checked against .NET's AesGcm.
        [InlineData("00000000000000000000000000000000", "000000000000000000000000", "", "", "", "58e2fccefa7e3061367f1d57a4e7455a")]                                                       // test case 1
        [InlineData("00000000000000000000000000000000", "000000000000000000000000", "", "00000000000000000000000000000000", "0388dace60b6a392f328c2b971b2fe78", "ab6e47d42cec13bdf53a67b21257bddf")] // test case 2
        [InlineData("feffe9928665731c6d6a8f9467308308", "cafebabefacedbaddecaf888", "feedfacedeadbeeffeedfacedeadbeefabaddad2",
            "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39",
            "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091", "5bc94fbc3221a5db94fae95ae7121a47")]               // test case 4
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "000000000000000000000000", "", "", "", "530f8afbc74536b9a963b4f1c4cb738b")]               // test case 13
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "000000000000000000000000", "", "00000000000000000000000000000000", "cea7403d4d606b6e074ec5d3baf39d18", "d0d1c8a799996bf0265b98b5d48ab919")] // test case 14
        [InlineData("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308", "cafebabefacedbaddecaf888", "feedfacedeadbeeffeedfacedeadbeefabaddad2",
            "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39",
            "522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662", "76fc6ece0f4e1768cddf8853bb2d551b")]       // test case 16
        public void AesGcmGivesTheKnownAnswersAndRefusesAnyChange(string key, string nonce, string aad, string plaintext, string ciphertext, string tag)
        {
            byte[] pt = Hex(plaintext);
            using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(Hex(key)))
            {
                byte[] sealedBytes = new byte[pt.Length + 16];
                aead.Seal(Hex(nonce), Hex(aad), pt, 0, pt.Length, sealedBytes, 0);
                Assert.Equal(ciphertext + tag, ToHex(sealedBytes));

                byte[] opened = new byte[pt.Length];
                Assert.True(aead.TryOpen(Hex(nonce), Hex(aad), sealedBytes, 0, sealedBytes.Length, opened, 0));
                Assert.Equal(plaintext, ToHex(opened));

                // any bit of the ciphertext, the tag, the aad or the nonce
                for (int position = 0; position < sealedBytes.Length; position += Math.Max(1, sealedBytes.Length / 7))
                {
                    byte[] bad = (byte[])sealedBytes.Clone();
                    bad[position] ^= 0x80;
                    byte[] target = new byte[pt.Length];
                    Assert.False(aead.TryOpen(Hex(nonce), Hex(aad), bad, 0, bad.Length, target, 0));
                    Assert.True(target.All(b => b == 0), "no unauthenticated plaintext is left behind");
                }
                byte[] otherAad = Hex(aad).Concat(new byte[] { 0x01 }).ToArray();
                Assert.False(aead.TryOpen(Hex(nonce), otherAad, sealedBytes, 0, sealedBytes.Length, new byte[pt.Length], 0));
                byte[] otherNonce = Hex(nonce);
                otherNonce[0] ^= 0x01;
                Assert.False(aead.TryOpen(otherNonce, Hex(aad), sealedBytes, 0, sealedBytes.Length, new byte[pt.Length], 0));
            }
        }

        [Fact]
        public void AesGcmWorksOnASliceOfALargerBufferAndOneKeyServesManyMessages()
        {
            byte[] key = Hex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
            using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(key))
            {
                for (int round = 0; round < 50; round++)
                {
                    byte[] nonce = new byte[12];
                    nonce[11] = (byte)round;
                    byte[] clear = Enumerable.Range(0, 100 + round).Select(i => (byte)(i * 7 + round)).ToArray();
                    byte[] container = new byte[40 + clear.Length + 16 + 9];
                    byte[] source = new byte[13 + clear.Length];
                    Buffer.BlockCopy(clear, 0, source, 13, clear.Length);

                    aead.Seal(nonce, null, source, 13, clear.Length, container, 40);
                    byte[] opened = new byte[5 + clear.Length];
                    Assert.True(aead.TryOpen(nonce, null, container, 40, clear.Length + 16, opened, 5));
                    Assert.Equal(clear, opened.Skip(5).ToArray());
                }
            }
        }

        [Fact]
        public void AesGcmRefusesKeysAndNoncesOfTheWrongSize()
        {
            Assert.Throws<ArgumentException>(() => ExchangeE2eNative.NewAesGcmKey(new byte[15]));
            using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(new byte[16]))
            {
                Assert.Throws<ArgumentException>(() => aead.Seal(new byte[11], null, new byte[1], 0, 1, new byte[17], 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => aead.Seal(new byte[12], null, new byte[1], 0, 1, new byte[16], 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => aead.TryOpen(new byte[12], null, new byte[10], 0, 10, new byte[1], 0));
            }
        }

        [Fact]
        public void TheCipherModeInfoHasTheSizeBcryptExpectsOnX64()
        {
            if (IntPtr.Size != 8) return;   // the product ships x64 only
            Assert.Equal(88, Marshal.SizeOf(typeof(ExchangeE2eNative.AuthenticatedCipherModeInfo)));
        }

        // ================================================================ HKDF (RFC 5869)

        [Fact]
        public void HkdfSha256GivesTheRfc5869Cases()
        {
            // test case 1
            byte[] okm1 = ExchangeHkdf.Derive(Enumerable.Repeat((byte)0x0b, 22).ToArray(), Hex("000102030405060708090a0b0c"), Hex("f0f1f2f3f4f5f6f7f8f9"), 42);
            Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865", ToHex(okm1));

            // test case 3: an empty salt and an empty info - the shapes the contract uses
            byte[] okm3 = ExchangeHkdf.Derive(Enumerable.Repeat((byte)0x0b, 22).ToArray(), new byte[0], new byte[0], 42);
            Assert.Equal("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8", ToHex(okm3));
        }

        // ================================================================ device keys (item DF)

        [Fact]
        public void ADeviceKeyRoundTripsThroughItsPrivateScalar()
        {
            using (ExchangeDeviceKey original = ExchangeDeviceKey.Generate())
            {
                byte[] scalar = original.PrivateScalar;
                Assert.Equal(32, scalar.Length);
                using (ExchangeDeviceKey restored = ExchangeDeviceKey.FromPrivateScalar(scalar))
                {
                    Assert.Equal(original.PublicKem, restored.PublicKem);
                    Assert.Equal(original.PrivateScalar, restored.PrivateScalar);

                    // the same key means the same secret with any peer
                    using (ExchangeDeviceKey peer = ExchangeDeviceKey.Generate())
                    {
                        Assert.Equal(original.DeriveRawSecret(peer.PublicKem), restored.DeriveRawSecret(peer.PublicKem));
                        Assert.Equal(original.DeriveRawSecret(peer.PublicKem), peer.DeriveRawSecret(original.PublicKem));
                    }
                }
            }
        }

        [Fact]
        public void ADeviceKeyPublishesAUncompressedPointInBase64UrlAndNeverTheSameTwice()
        {
            using (ExchangeDeviceKey a = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey b = ExchangeDeviceKey.Generate())
            {
                Assert.Equal(65, a.PublicKem.Length);
                Assert.Equal(0x04, a.PublicKem[0]);
                Assert.True(ExchangeDeviceKey.IsValidPublicPoint(a.PublicKem));
                Assert.Equal(87, a.PublicKemBase64Url.Length);   // 65 bytes, base64url, no padding
                Assert.DoesNotContain("=", a.PublicKemBase64Url);
                Assert.Equal(a.PublicKem, ExchangeE2e.Base64UrlDecode(a.PublicKemBase64Url));
                Assert.NotEqual(a.PublicKemBase64Url, b.PublicKemBase64Url);
                Assert.NotEqual(a.PrivateScalar, b.PrivateScalar);
            }
        }

        [Fact]
        public void APrivateScalarMustBeInTheGroup()
        {
            Assert.Throws<ArgumentException>(() => ExchangeDeviceKey.FromPrivateScalar(new byte[32]));                         // zero
            Assert.Throws<ArgumentException>(() => ExchangeDeviceKey.FromPrivateScalar(new byte[31]));
            Assert.Throws<ArgumentException>(() => ExchangeDeviceKey.FromPrivateScalar(new byte[33]));
            byte[] n = Hex("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
            Assert.Throws<ArgumentException>(() => ExchangeDeviceKey.FromPrivateScalar(n));                                    // n itself
            byte[] nMinusOne = (byte[])n.Clone();
            nMinusOne[31] -= 1;
            using (ExchangeDeviceKey top = ExchangeDeviceKey.FromPrivateScalar(nMinusOne)) Assert.True(ExchangeDeviceKey.IsValidPublicPoint(top.PublicKem));
            byte[] one = new byte[32];
            one[31] = 1;
            using (ExchangeDeviceKey g = ExchangeDeviceKey.FromPrivateScalar(one))                                              // 1 * G is the base point
                Assert.Equal("046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5", ToHex(g.PublicKem));
        }

        [Fact]
        public void GenerateRedrawsAScalarOutsideTheGroup()
        {
            int draws = 0;
            using (ExchangeDeviceKey key = ExchangeDeviceKey.Generate(buffer =>
            {
                draws++;
                if (draws == 1) return;                                                   // all zero: not a scalar
                if (draws == 2) { for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xFF; return; }   // above n
                Array.Clear(buffer, 0, buffer.Length);
                buffer[31] = 5;
            }))
            {
                Assert.Equal(3, draws);
                byte[] expected = new byte[32];
                expected[31] = 5;
                Assert.Equal(expected, key.PrivateScalar);
            }
        }

        [Fact]
        public void ADisposedKeyIsGone()
        {
            ExchangeDeviceKey key = ExchangeDeviceKey.Generate();
            key.Dispose();
            key.Dispose();   // twice is fine
            Assert.Throws<ObjectDisposedException>(() => key.PublicKem);
            Assert.Throws<ObjectDisposedException>(() => key.PrivateScalar);
        }

        [Fact]
        public void OnlyAPointOnTheCurveIsAPublicKey()
        {
            using (ExchangeDeviceKey key = ExchangeDeviceKey.Generate())
            {
                byte[] good = key.PublicKem;
                Assert.True(ExchangeDeviceKey.IsValidPublicPoint(good));

                byte[] offCurve = (byte[])good.Clone();
                offCurve[64] ^= 0x01;
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(offCurve));

                byte[] compressed = (byte[])good.Clone();
                compressed[0] = 0x02;
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(compressed));
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(good.Take(64).ToArray()));
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(null));

                byte[] infinity = new byte[65];
                infinity[0] = 0x04;
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(infinity));

                // a coordinate at or above the field prime is not the same point in two spellings
                byte[] overPrime = (byte[])good.Clone();
                byte[] p = Hex("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
                Buffer.BlockCopy(p, 0, overPrime, 1, 32);
                Assert.False(ExchangeDeviceKey.IsValidPublicPoint(overPrime));

                Assert.Throws<ArgumentException>(() => key.DeriveRawSecret(offCurve));
            }
        }

        // ================================================================ base64url

        [Fact]
        public void Base64UrlRoundTripsEveryLengthWithoutPaddingAndWithTheUrlAlphabet()
        {
            var random = new Random(4242);
            for (int length = 0; length <= 40; length++)
            {
                byte[] data = new byte[length];
                random.NextBytes(data);
                string text = ExchangeE2e.Base64UrlEncode(data);
                Assert.DoesNotContain("=", text);
                Assert.DoesNotContain("+", text);
                Assert.DoesNotContain("/", text);
                Assert.Equal(data, ExchangeE2e.Base64UrlDecode(text));
            }
            Assert.Equal("-_8", ExchangeE2e.Base64UrlEncode(new byte[] { 0xFB, 0xFF }));
        }

        [Theory]
        [InlineData("AA==")]      // padding is not part of the form
        [InlineData("A")]         // a length no base64 text has
        [InlineData("AAAAA")]
        [InlineData("AA+/")]      // the standard alphabet
        [InlineData("AA A")]      // whitespace
        [InlineData("AA\n")]
        [InlineData("AB")]        // non-canonical: the unused bits of the last character are set
        [InlineData("AAB")]
        [InlineData("éééé")]
        public void Base64UrlDecodeIsStrict(string text)
        {
            Assert.Null(ExchangeE2e.Base64UrlDecode(text));
        }

        [Fact]
        public void Base64UrlDecodeOfNullAndEmpty()
        {
            Assert.Null(ExchangeE2e.Base64UrlDecode(null));
            Assert.Empty(ExchangeE2e.Base64UrlDecode("")!);
        }

        // ================================================================ the check code (item DG)

        [Fact]
        public void TheCheckCodeIsTheContractsDefinition()
        {
            // an independent evaluation of item DG for a fixed key: SHA-256("fmsx-e2e-v1 check" || kem), first 8 bytes,
            // big-endian, mod 10^12, twelve digits in groups of four
            byte[] kem = Hex(RfcPkRm);
            byte[] input = Encoding.ASCII.GetBytes("fmsx-e2e-v1 check").Concat(kem).ToArray();
            byte[] hash = Sha256(input);
            BigInteger value = new BigInteger(hash.Take(8).Reverse().Concat(new byte[] { 0 }).ToArray());
            string digits = (value % BigInteger.Pow(10, 12)).ToString("D12", CultureInfo.InvariantCulture);
            Assert.Equal(digits.Substring(0, 4) + " " + digits.Substring(4, 4) + " " + digits.Substring(8, 4), ExchangeE2e.CheckCode(kem));
            Assert.Matches("^[0-9]{4} [0-9]{4} [0-9]{4}$", ExchangeE2e.CheckCode(kem));
            Assert.Throws<ArgumentException>(() => ExchangeE2e.CheckCode(new byte[64]));
        }

        [Fact]
        public void TheCheckCodesOfTheVectorFileAreReproduced()
        {
            foreach (VectorDevice device in Devices.Value.Values)
            {
                Assert.Equal(device.CheckCode, ExchangeE2e.CheckCode(device.Kem));
                using (ExchangeDeviceKey key = ExchangeDeviceKey.FromPrivateScalar(device.PrivateKey))
                    Assert.Equal(device.KemText, key.PublicKemBase64Url);
            }
        }

        // ================================================================ boxes: the vector file (item DH)

        private static readonly Dictionary<string, string> FailureOfVectorCode = new Dictionary<string, string>
        {
            { "NO_ENTRY", "no-entry" },
            { "UNTRUSTED_SENDER", "untrusted-key" },
            { "BAD_TAG", "bad-tag" },
        };

        [Fact]
        public void EveryBoxOfTheVectorFileOpensForEveryRecipientWithItsPayload()
        {
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            int opened = 0;
            foreach (Dictionary<string, object> box in Items(file["boxes"]))
            {
                VectorDevice sender = Devices.Value[(string)box["from"]];
                string boxJson = Json(box["box"]);
                foreach (object recipientName in Items(box["to"]))
                {
                    VectorDevice reader = Devices.Value[(string)recipientName];
                    Assert.True(ExchangeE2e.TryOpenBox((string)box["context"], boxJson, reader.DeviceId, reader.Key,
                        id => id == sender.DeviceId ? sender.Kem : null, out byte[]? payload, out byte[]? contentKey, out string failure),
                        (string)box["name"] + " as " + reader.Name + ": " + failure);
                    Assert.Equal(string.Empty, failure);
                    Assert.Equal(32, contentKey!.Length);
                    Assert.Equal(Canonical(box["payload"]), Canonical(ParseJson(payload!)));
                    opened++;
                }
            }
            Assert.Equal(7, opened);   // access x2, grant x1, broadcast x2, clipboard x2
        }

        [Fact]
        public void EveryFailingCaseOfTheVectorFileFailsWithItsReason()
        {
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var boxes = Items(file["boxes"]).Cast<Dictionary<string, object>>().ToDictionary(b => (string)b["name"]);
            int run = 0;
            foreach (Dictionary<string, object> failing in Items(file["failing"]))
            {
                string name = (string)failing["name"];
                VectorDevice reader = Devices.Value[(string)failing["openAs"]];
                VectorDevice trustedAsPhone = Devices.Value[(string)failing["readerTrustsForPhone"]];
                object boxObject = failing.ContainsKey("replacedBox") ? failing["replacedBox"] : boxes[(string)failing["box"]]["box"];

                bool ok = ExchangeE2e.TryOpenBox((string)failing["context"], Json(boxObject), reader.DeviceId, reader.Key,
                    id => id == "dev-phone" ? trustedAsPhone.Kem : null, out byte[]? payload, out byte[]? contentKey, out string failure);

                Assert.False(ok, name);
                Assert.Null(payload);
                Assert.Null(contentKey);
                Assert.Equal(FailureOfVectorCode[(string)failing["expected"]], failure);
                run++;
            }
            Assert.Equal(4, run);
        }

        [Fact]
        public void TheContextIsTheCallersAndNeverTheBoxs()
        {
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var box = (Dictionary<string, object>)Items(file["boxes"]).Cast<object>().First();
            VectorDevice sender = Devices.Value["phone"];
            VectorDevice reader = Devices.Value["tablet"];
            string context = (string)box["context"];

            // a box that claims a context of its own, and members nobody knows: the claim is not read, the rest is ignored
            var claimed = new Dictionary<string, object>((Dictionary<string, object>)box["box"]);
            claimed["context"] = "access:another-resource";
            claimed["extra"] = new ArrayList { 1, 2, "three" };
            string withExtras = Json(claimed);

            Assert.True(ExchangeE2e.TryOpenBox(context, withExtras, reader.DeviceId, reader.Key, id => sender.Kem, out byte[]? payload, out byte[]? contentKey, out string failure), failure);
            Assert.NotNull(payload);
            Assert.NotNull(contentKey);

            // the box's own claim does not rescue it under the context the reader found it in
            Assert.False(ExchangeE2e.TryOpenBox("access:another-resource", withExtras, reader.DeviceId, reader.Key, id => sender.Kem, out _, out _, out failure));
            Assert.Equal("bad-tag", failure);
        }

        [Fact]
        public void ABoxWithUnknownVersionOrWithoutShapeIsRefusedWithoutBeingRead()
        {
            VectorDevice sender = Devices.Value["phone"];
            VectorDevice reader = Devices.Value["tablet"];
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var good = (Dictionary<string, object>)((Dictionary<string, object>)Items(file["boxes"]).Cast<object>().First())["box"];
            string context = "access:r7c1d2e3-0000-4000-8000-00000000e2e1";

            string Open(string json)
            {
                Assert.False(ExchangeE2e.TryOpenBox(context, json, reader.DeviceId, reader.Key, id => sender.Kem, out byte[]? p, out byte[]? k, out string f));
                Assert.Null(p);
                Assert.Null(k);
                return f;
            }

            var v2 = new Dictionary<string, object>(good) { ["v"] = 2 };
            Assert.Equal("unknown-version", Open(Json(v2)));
            Assert.Equal("unknown-version", Open(Json(new Dictionary<string, object>(good) { ["v"] = "1" })));
            Assert.Equal("unknown-version", Open(Json(new Dictionary<string, object>(good) { ["v"] = 1.5 })));
            Assert.Equal("unknown-version", Open("{\"v\":2}"));   // a future version may be shaped otherwise: nothing else is checked

            var noVersion = new Dictionary<string, object>(good);
            noVersion.Remove("v");
            Assert.Equal("malformed", Open(Json(noVersion)));

            // JSON member names are case-sensitive; the reader (JavaScriptSerializer) is not
            var lookalike = new Dictionary<string, object>(good);
            lookalike.Remove("keys");
            lookalike["Keys"] = good["keys"];
            Assert.Equal("malformed", Open(Json(lookalike)));

            foreach (string member in new[] { "from", "fromKey", "n", "ct", "keys" })
            {
                var without = new Dictionary<string, object>(good);
                without.Remove(member);
                Assert.Equal("malformed", Open(Json(without)));
            }

            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["keys"] = "dev-tablet" })));
            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["n"] = ExchangeE2e.Base64UrlEncode(new byte[11]) })));
            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["ct"] = ExchangeE2e.Base64UrlEncode(new byte[15]) })));
            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["fromKey"] = "AAAA" })));
            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["ct"] = "AA==" })));
            Assert.Equal("malformed", Open(""));
            Assert.Equal("malformed", Open("not json"));
            Assert.Equal("malformed", Open("[1,2,3]"));
            Assert.Equal("malformed", Open("\"v\""));
            Assert.Equal("malformed", Open("{"));
            Assert.Equal("malformed", Open(new string(' ', ExchangeE2e.MaxBoxChars + 1)));

            // an entry whose key material is damaged is malformed for the reader it addresses, and for nobody else
            var entries = new ArrayList(Items(good["keys"]).Cast<object>().ToList());
            var damaged = new Dictionary<string, object>((Dictionary<string, object>)entries[0]) { ["wk"] = ExchangeE2e.Base64UrlEncode(new byte[47]) };
            entries[0] = damaged;
            Assert.Equal("malformed", Open(Json(new Dictionary<string, object>(good) { ["keys"] = entries })));
        }

        [Fact]
        public void HostileJsonIsRefusedAndNeverEscapesAsAnException()
        {
            VectorDevice reader = Devices.Value["tablet"];
            string deep = new string('[', 200) + new string(']', 200);
            string[] hostile =
            {
                "{\"v\":1e999}",
                "{\"v\":99999999999999999999999999999}",
                "{\"v\":1,\"keys\":" + deep + "}",
                "{\"v\":1,\"from\":\"\\ud800\",\"fromKey\":\"x\"}",
                "{\"v\":1,\"v\":1}",
                "{\"v\":null}",
                "{\"v\":1,\"from\":null,\"fromKey\":null,\"n\":null,\"ct\":null,\"keys\":null}",
                "{\"v\":1,\"from\":1,\"fromKey\":2,\"n\":3,\"ct\":4,\"keys\":[5,6,null,\"x\",[],{}]}",
                "\u0000",
                "{\"v\":1,\"keys\":[{\"to\":\"dev-tablet\",\"enc\":1,\"wk\":[]}]}",
                new string('{', 5000),
            };
            foreach (string text in hostile)
            {
                bool ok = ExchangeE2e.TryOpenBox("clipboard", text, reader.DeviceId, reader.Key, id => reader.Kem, out byte[]? payload, out byte[]? key, out string failure);
                Assert.False(ok, text.Length > 60 ? text.Substring(0, 60) : text);
                Assert.Null(payload);
                Assert.Null(key);
                Assert.Contains(failure, new[] { "malformed", "unknown-version", "no-entry" });
            }
        }

        [Fact]
        public void TheTrustCallbackDecidesAndFailsClosed()
        {
            VectorDevice phone = Devices.Value["phone"];
            VectorDevice tablet = Devices.Value["tablet"];
            VectorDevice stranger = Devices.Value["stranger"];
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var box = (Dictionary<string, object>)Items(file["boxes"]).Cast<object>().First();
            string context = (string)box["context"];
            string boxJson = Json(box["box"]);

            string? asked = null;
            Assert.True(ExchangeE2e.TryOpenBox(context, boxJson, tablet.DeviceId, tablet.Key, id => { asked = id; return phone.Kem; }, out _, out _, out string failure), failure);
            Assert.Equal("dev-phone", asked);   // the callback is asked about the sender the box names

            Assert.False(ExchangeE2e.TryOpenBox(context, boxJson, tablet.DeviceId, tablet.Key, id => null, out _, out _, out failure));
            Assert.Equal("untrusted-key", failure);
            Assert.False(ExchangeE2e.TryOpenBox(context, boxJson, tablet.DeviceId, tablet.Key, id => stranger.Kem, out _, out _, out failure));
            Assert.Equal("untrusted-key", failure);
            Assert.False(ExchangeE2e.TryOpenBox(context, boxJson, tablet.DeviceId, tablet.Key, id => new byte[64], out _, out _, out failure));
            Assert.Equal("untrusted-key", failure);
            Assert.False(ExchangeE2e.TryOpenBox(context, boxJson, tablet.DeviceId, tablet.Key, id => throw new InvalidOperationException("lookup failed"), out _, out _, out failure));
            Assert.Equal("untrusted-key", failure);
        }

        [Fact]
        public void ADeviceThatHoldsAnotherKeyUnderTheAddressedIdFailsTheTag()
        {
            // the entry is found by `to`; the key behind that id decides whether HPKE opens it
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var box = (Dictionary<string, object>)Items(file["boxes"]).Cast<object>().First();
            VectorDevice phone = Devices.Value["phone"];
            using (ExchangeDeviceKey impostor = ExchangeDeviceKey.Generate())
            {
                Assert.False(ExchangeE2e.TryOpenBox((string)box["context"], Json(box["box"]), "dev-tablet", impostor, id => phone.Kem, out byte[]? payload, out byte[]? contentKey, out string failure));
                Assert.Equal("bad-tag", failure);
                Assert.Null(payload);
                Assert.Null(contentKey);
            }
        }

        [Fact]
        public void ABoxNamesItsSenderForTheCallersWhoKeepAFirstSeenKey()
        {
            VectorDevice phone = Devices.Value["phone"];
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var box = (Dictionary<string, object>)Items(file["boxes"]).Cast<object>().First();
            Assert.True(ExchangeE2e.TryReadSender(Json(box["box"]), out string? from, out byte[]? fromKey));
            Assert.Equal("dev-phone", from);
            Assert.Equal(phone.Kem, fromKey);

            Assert.False(ExchangeE2e.TryReadSender("{\"v\":2,\"from\":\"x\",\"fromKey\":\"AAAA\"}", out from, out fromKey));
            Assert.Null(from);
            Assert.Null(fromKey);
            Assert.False(ExchangeE2e.TryReadSender("garbage", out _, out _));
        }

        // ================================================================ sealing a box

        [Fact]
        public void ASealedBoxHasTheShapeOfItemDh()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                byte[] payload = Encoding.UTF8.GetBytes("{\"kind\":\"text\"}");
                string json = ExchangeE2e.SealBox("clipboard", payload, "dev-a", sender, new[] { new ExchangeE2eRecipient("dev-b", reader.PublicKem) }, out byte[] contentKey);
                try
                {
                    // one compact object, members in the contract's order, no whitespace
                    Assert.StartsWith("{\"v\":1,\"from\":\"dev-a\",\"fromKey\":\"" + sender.PublicKemBase64Url + "\",\"n\":\"", json);
                    Assert.True(json.IndexOf("\"n\":", StringComparison.Ordinal) < json.IndexOf("\"ct\":", StringComparison.Ordinal));
                    Assert.True(json.IndexOf("\"ct\":", StringComparison.Ordinal) < json.IndexOf("\"keys\":", StringComparison.Ordinal));
                    Assert.DoesNotContain(" ", json);
                    Assert.DoesNotContain("\n", json);

                    var parsed = (Dictionary<string, object>)ParseJson(Encoding.UTF8.GetBytes(json));
                    Assert.Equal(new[] { "v", "from", "fromKey", "n", "ct", "keys" }, parsed.Keys.ToArray());
                    Assert.Equal(12, ExchangeE2e.Base64UrlDecode((string)parsed["n"])!.Length);
                    Assert.Equal(payload.Length + 16, ExchangeE2e.Base64UrlDecode((string)parsed["ct"])!.Length);
                    var entry = (Dictionary<string, object>)Items(parsed["keys"]).Cast<object>().Single();
                    Assert.Equal(new[] { "to", "enc", "wk" }, entry.Keys.ToArray());
                    Assert.Equal("dev-b", entry["to"]);
                    Assert.Equal(65, ExchangeE2e.Base64UrlDecode((string)entry["enc"])!.Length);
                    Assert.Equal(48, ExchangeE2e.Base64UrlDecode((string)entry["wk"])!.Length);
                    Assert.Equal(32, contentKey.Length);
                }
                finally
                {
                    ExchangeE2e.Wipe(contentKey);
                }
            }
        }

        [Fact]
        public void ASealedBoxOpensOnEveryRecipientWithTheSameContentKeyAndOnlyThere()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey one = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey two = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey outsider = ExchangeDeviceKey.Generate())
            {
                // not only ASCII: the payload is UTF-8 whatever the language of the file name or the text
                byte[] payload = Encoding.UTF8.GetBytes("{\"name\":\"Привет 世界 😀\"}");
                string json = ExchangeE2e.SealBox("access:r1", payload, "sender", sender,
                    new[] { new ExchangeE2eRecipient("one", one.PublicKem), new ExchangeE2eRecipient("two", two.PublicKem) }, out byte[] sealedKey);

                Func<string, byte[]?> trust = id => id == "sender" ? sender.PublicKem : null;
                Assert.True(ExchangeE2e.TryOpenBox("access:r1", json, "one", one, trust, out byte[]? p1, out byte[]? k1, out string f1), f1);
                Assert.True(ExchangeE2e.TryOpenBox("access:r1", json, "two", two, trust, out byte[]? p2, out byte[]? k2, out string f2), f2);
                Assert.Equal(payload, p1);
                Assert.Equal(payload, p2);
                Assert.Equal(sealedKey, k1);
                Assert.Equal(sealedKey, k2);

                Assert.False(ExchangeE2e.TryOpenBox("access:r1", json, "outsider", outsider, trust, out byte[]? p3, out byte[]? k3, out string f3));
                Assert.Equal("no-entry", f3);
                Assert.Null(p3);
                Assert.Null(k3);

                // one box is not another's: a context moved, and a sender nobody trusts
                Assert.False(ExchangeE2e.TryOpenBox("access:r2", json, "one", one, trust, out _, out _, out string f4));
                Assert.Equal("bad-tag", f4);
                Assert.False(ExchangeE2e.TryOpenBox("access:r1", json, "one", one, id => null, out _, out _, out string f5));
                Assert.Equal("untrusted-key", f5);
            }
        }

        [Fact]
        public void TwoSealsOfTheSameThingShareNothing()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                var recipients = new[] { new ExchangeE2eRecipient("r", reader.PublicKem) };
                byte[] payload = Encoding.UTF8.GetBytes("{}");
                string a = ExchangeE2e.SealBox("clipboard", payload, "s", sender, recipients, out byte[] keyA);
                string b = ExchangeE2e.SealBox("clipboard", payload, "s", sender, recipients, out byte[] keyB);
                Assert.NotEqual(a, b);
                Assert.NotEqual(keyA, keyB);
            }
        }

        [Fact]
        public void AScriptedSourceMakesASealReproducible()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkSm)))
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.FromPrivateScalar(Hex(RfcSkRm)))
            {
                var recipients = new[] { new ExchangeE2eRecipient("r", reader.PublicKem) };
                byte[] payload = Encoding.UTF8.GetBytes("{\"a\":1}");
                string Seal(out byte[] key) => ExchangeE2e.SealBox("clipboard", payload, "s", sender, recipients,
                    new ScriptedSource(Enumerable.Repeat((byte)0x11, 32).ToArray(), Enumerable.Repeat((byte)0x22, 12).ToArray(), Hex(RfcSkEm)), out key);

                string first = Seal(out byte[] k1);
                string second = Seal(out byte[] k2);
                Assert.Equal(first, second);
                Assert.Equal(Enumerable.Repeat((byte)0x11, 32).ToArray(), k1);
                Assert.Equal(k1, k2);

                // the box carries the nonce the source gave and the RFC's ephemeral point as enc
                var parsed = (Dictionary<string, object>)ParseJson(Encoding.UTF8.GetBytes(first));
                Assert.Equal(Enumerable.Repeat((byte)0x22, 12).ToArray(), ExchangeE2e.Base64UrlDecode((string)parsed["n"]));
                var entry = (Dictionary<string, object>)Items(parsed["keys"]).Cast<object>().Single();
                Assert.Equal(RfcPkEm, ToHex(ExchangeE2e.Base64UrlDecode((string)entry["enc"])!));

                Assert.True(ExchangeE2e.TryOpenBox("clipboard", first, "r", reader, id => sender.PublicKem, out byte[]? opened, out _, out string failure), failure);
                Assert.Equal(payload, opened);
            }
        }

        [Fact]
        public void ABoxAddressesAtMostFortyRecipients()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            {
                var keys = new List<ExchangeDeviceKey>();
                try
                {
                    for (int i = 0; i < 41; i++) keys.Add(ExchangeDeviceKey.Generate());
                    List<ExchangeE2eRecipient> forty = Enumerable.Range(0, 40).Select(i => new ExchangeE2eRecipient("d" + i, keys[i].PublicKem)).ToList();
                    byte[] payload = Encoding.UTF8.GetBytes("{\"x\":true}");

                    string json = ExchangeE2e.SealBox("clipboard", payload, "sender", sender, forty, out byte[] contentKey);
                    var parsed = (Dictionary<string, object>)ParseJson(Encoding.UTF8.GetBytes(json));
                    Assert.Equal(40, Items(parsed["keys"]).Cast<object>().Count());
                    foreach (int index in new[] { 0, 17, 39 })
                    {
                        Assert.True(ExchangeE2e.TryOpenBox("clipboard", json, "d" + index, keys[index], id => sender.PublicKem, out byte[]? opened, out byte[]? key, out string failure), failure);
                        Assert.Equal(payload, opened);
                        Assert.Equal(contentKey, key);
                    }

                    List<ExchangeE2eRecipient> fortyOne = forty.Concat(new[] { new ExchangeE2eRecipient("d40", keys[40].PublicKem) }).ToList();
                    Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBox("clipboard", payload, "sender", sender, fortyOne, out _));
                }
                finally
                {
                    foreach (ExchangeDeviceKey key in keys) key.Dispose();
                }
            }
        }

        [Fact]
        public void SealingRefusesWhatCannotBeABox()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                var one = new[] { new ExchangeE2eRecipient("r", reader.PublicKem) };
                byte[] payload = Encoding.UTF8.GetBytes("{}");
                Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBox("clipboard", payload, "s", sender, new ExchangeE2eRecipient[0], out _));
                Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBox("clipboard", payload, "s", sender, new[] { one[0], new ExchangeE2eRecipient("r", reader.PublicKem) }, out _));
                Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBox("", payload, "s", sender, one, out _));
                Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBox("clipboard", payload, "", sender, one, out _));
                Assert.Throws<ArgumentNullException>(() => ExchangeE2e.SealBox("clipboard", null!, "s", sender, one, out _));

                Assert.Throws<ArgumentException>(() => new ExchangeE2eRecipient("", reader.PublicKem));
                Assert.Throws<ArgumentException>(() => new ExchangeE2eRecipient("r", new byte[65]));
                Assert.Throws<ArgumentException>(() => new ExchangeE2eRecipient("r", reader.PublicKem.Take(64).ToArray()));
                byte[] offCurve = reader.PublicKem;
                offCurve[40] ^= 0x01;
                Assert.Throws<ArgumentException>(() => new ExchangeE2eRecipient("r", offCurve));
            }
        }

        [Fact]
        public void ADeviceIdIsEscapedInTheBox()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                string senderId = "s\"\\\n\t\u0001xé";
                string readerId = "r\"quote";
                string json = ExchangeE2e.SealBox("clipboard", Encoding.UTF8.GetBytes("{}"), senderId, sender, new[] { new ExchangeE2eRecipient(readerId, reader.PublicKem) }, out _);
                Assert.True(ExchangeE2e.TryOpenBox("clipboard", json, readerId, reader, id => id == senderId ? sender.PublicKem : null, out byte[]? payload, out _, out string failure), failure);
                Assert.Equal("{}", Encoding.UTF8.GetString(payload!));
            }
        }

        // ================================================================ tampering with a box

        [Fact]
        public void AnyChangedBitOfABoxFailsToOpen()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                string good = ExchangeE2e.SealBox("clipboard", Encoding.UTF8.GetBytes("{\"kind\":\"text\",\"size\":5}"), "s", sender,
                    new[] { new ExchangeE2eRecipient("r", reader.PublicKem) }, out _);
                Func<string, byte[]?> trust = id => sender.PublicKem;
                Assert.True(ExchangeE2e.TryOpenBox("clipboard", good, "r", reader, trust, out _, out _, out string failure0), failure0);

                var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(good);
                var entry = (Dictionary<string, object>)Items(root["keys"]).Cast<object>().Single();

                string Tampered(Action<Dictionary<string, object>, Dictionary<string, object>> change)
                {
                    var copyRoot = new Dictionary<string, object>(root);
                    var copyEntry = new Dictionary<string, object>(entry);
                    copyRoot["keys"] = new ArrayList { copyEntry };
                    change(copyRoot, copyEntry);
                    return Json(copyRoot);
                }

                string FlipBit(string base64Url, int bytePosition)
                {
                    byte[] bytes = ExchangeE2e.Base64UrlDecode(base64Url)!;
                    bytes[bytePosition] ^= 0x01;
                    return ExchangeE2e.Base64UrlEncode(bytes);
                }

                var cases = new Dictionary<string, string[]>   // which member, and the failures a flip there may report
                {
                    ["ct-first"] = new[] { "bad-tag" },
                    ["ct-last"] = new[] { "bad-tag" },
                    ["n"] = new[] { "bad-tag" },
                    ["wk-first"] = new[] { "bad-tag" },
                    ["wk-last"] = new[] { "bad-tag" },
                    ["enc-x"] = new[] { "bad-tag", "malformed" },
                    ["enc-y"] = new[] { "bad-tag", "malformed" },
                    ["fromKey"] = new[] { "untrusted-key", "malformed" },
                };
                var tampered = new Dictionary<string, string>
                {
                    ["ct-first"] = Tampered((r, e) => r["ct"] = FlipBit((string)r["ct"], 0)),
                    ["ct-last"] = Tampered((r, e) => r["ct"] = FlipBit((string)r["ct"], ExchangeE2e.Base64UrlDecode((string)r["ct"])!.Length - 1)),
                    ["n"] = Tampered((r, e) => r["n"] = FlipBit((string)r["n"], 5)),
                    ["wk-first"] = Tampered((r, e) => e["wk"] = FlipBit((string)e["wk"], 0)),
                    ["wk-last"] = Tampered((r, e) => e["wk"] = FlipBit((string)e["wk"], 47)),
                    ["enc-x"] = Tampered((r, e) => e["enc"] = FlipBit((string)e["enc"], 10)),
                    ["enc-y"] = Tampered((r, e) => e["enc"] = FlipBit((string)e["enc"], 60)),
                    ["fromKey"] = Tampered((r, e) => r["fromKey"] = FlipBit((string)r["fromKey"], 30)),
                };
                foreach (KeyValuePair<string, string> pair in tampered)
                {
                    Assert.False(ExchangeE2e.TryOpenBox("clipboard", pair.Value, "r", reader, trust, out byte[]? payload, out byte[]? key, out string failure), pair.Key);
                    Assert.Contains(failure, cases[pair.Key]);
                    Assert.Null(payload);
                    Assert.Null(key);
                }

                // another sender's box with this sender's id and key swapped for ours does not open either
                using (ExchangeDeviceKey other = ExchangeDeviceKey.Generate())
                {
                    string swapped = Tampered((r, e) => r["fromKey"] = other.PublicKemBase64Url);
                    Assert.False(ExchangeE2e.TryOpenBox("clipboard", swapped, "r", reader, trust, out _, out _, out string failure));
                    Assert.Equal("untrusted-key", failure);
                    Assert.False(ExchangeE2e.TryOpenBox("clipboard", swapped, "r", reader, id => other.PublicKem, out _, out _, out failure));
                    Assert.Equal("bad-tag", failure);   // trusted, but the sender's static key took part in the secret
                }
            }
        }

        // ================================================================ the sealed bytes (item DN)

        [Theory]
        [InlineData(1L, 17L)]
        [InlineData(1000L, 1016L)]
        [InlineData(65535L, 65551L)]
        [InlineData(65536L, 65552L)]
        [InlineData(65537L, 65569L)]
        [InlineData(131072L, 131104L)]
        [InlineData(196613L, 196677L)]   // 3 x 65536 + 5: four segments, 64 bytes of tags
        public void TheSealedSizeIsTheClearSizePlusSixteenPerSegment(long clear, long sealedSize)
        {
            Assert.Equal(sealedSize, ExchangeE2e.SealedLength(clear));
            Assert.True(ExchangeE2e.TryClearLength(sealedSize, out long back));
            Assert.Equal(clear, back);
        }

        [Fact]
        public void SizesNoValueSealsToAreRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeE2e.SealedLength(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeE2e.SealedLength(-1));
            Assert.Equal(1L << 40, ExchangeE2e.SegmentCount(65536L * (1L << 40)));
            foreach (long impossible in new long[] { 0, 1, 16, 65553, 65560, 65568, 131105, 131119 })   // a last segment of 1 to 16 bytes cannot be
                Assert.False(ExchangeE2e.TryClearLength(impossible, out _), impossible.ToString(CultureInfo.InvariantCulture));
            Assert.True(ExchangeE2e.TryClearLength(17, out long one));
            Assert.Equal(1, one);

            // the open side says so rather than guessing: a length no value seals to is `malformed`, one too short `truncated`
            byte[] key = NewContentKey();
            Assert.False(ExchangeE2e.TryOpenBytes(new byte[65553], key, out byte[]? none, out string failure));
            Assert.Null(none);
            Assert.Equal("malformed", failure);
            Assert.False(ExchangeE2e.TryOpenBytes(new byte[16], key, out none, out failure));
            Assert.Equal("truncated", failure);
        }

        [Fact]
        public void DeviceKeysServeSeveralThreadsAtOnce()
        {
            using (ExchangeDeviceKey sender = ExchangeDeviceKey.Generate())
            using (ExchangeDeviceKey reader = ExchangeDeviceKey.Generate())
            {
                var recipients = new[] { new ExchangeE2eRecipient("r", reader.PublicKem) };
                var problems = new System.Collections.Concurrent.ConcurrentBag<string>();
                System.Threading.Tasks.Parallel.For(0, 64, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
                {
                    byte[] payload = Encoding.UTF8.GetBytes("{\"i\":" + i.ToString(CultureInfo.InvariantCulture) + "}");
                    string json = ExchangeE2e.SealBox("clipboard", payload, "s", sender, recipients, out byte[] key);
                    if (!ExchangeE2e.TryOpenBox("clipboard", json, "r", reader, id => sender.PublicKem, out byte[]? opened, out byte[]? openedKey, out string failure))
                        problems.Add(i + ": " + failure);
                    else if (!payload.SequenceEqual(opened!) || !key.SequenceEqual(openedKey!))
                        problems.Add(i + ": different bytes");
                });
                Assert.True(problems.IsEmpty, string.Join("; ", problems));
            }
        }

        [Fact]
        public void TheSegmentNonceIsElevenBytesOfTheIndexAndTheLastFlag()
        {
            byte[] nonce = new byte[12];
            ExchangeE2e.FillSegmentNonce(nonce, 0, false);
            Assert.Equal("000000000000000000000000", ToHex(nonce));
            ExchangeE2e.FillSegmentNonce(nonce, 0, true);
            Assert.Equal("000000000000000000000001", ToHex(nonce));
            ExchangeE2e.FillSegmentNonce(nonce, 1, false);
            Assert.Equal("000000000000000000000100", ToHex(nonce));   // the index 1 is the last of the eleven bytes; the flag is the twelfth
            ExchangeE2e.FillSegmentNonce(nonce, 0x0102030405060708L, true);
            Assert.Equal("000000010203040506070801", ToHex(nonce));
        }

        [Fact]
        public void TheValueKeyIsHkdfOfTheContentKeyWithTheClipboardInfo()
        {
            byte[] contentKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            byte[] expected = ExchangeHkdf.Derive(contentKey, new byte[0], Encoding.ASCII.GetBytes("fmsx-e2e-v1 clipboard"), 32);
            Assert.Equal(expected, ExchangeE2e.DeriveValueKey(contentKey));
            Assert.Throws<ArgumentException>(() => ExchangeE2e.DeriveValueKey(new byte[31]));
        }

        [Fact]
        public void EveryClipboardValueOfTheVectorFileReproducesItsKeysAndSealedBytes()
        {
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            int run = 0;
            foreach (Dictionary<string, object> value in Items(file["clipboard"]))
            {
                string name = (string)value["name"];
                VectorDevice sender = Devices.Value["phone"];
                VectorDevice reader = Devices.Value["tablet"];
                string boxJson = Json(value["box"]);

                // the box: opens for the recipient and yields the content key and the description of the clear value
                Assert.True(ExchangeE2e.TryOpenBox(ExchangeE2e.BoxContextClipboard, boxJson, reader.DeviceId, reader.Key,
                    id => sender.Kem, out byte[]? payload, out byte[]? contentKey, out string failure), name + ": " + failure);
                Assert.Equal((string)value["contentKey"], ToHex(contentKey!));
                var description = (Dictionary<string, object>)ParseJson(payload!);
                long clearSize = Convert.ToInt64(value["clearSize"], CultureInfo.InvariantCulture);
                Assert.Contains((string)description["kind"], new[] { "text", "image", "file" });   // the kinds of item BW
                Assert.Equal(clearSize, Convert.ToInt64(description["size"], CultureInfo.InvariantCulture));
                Assert.Equal((string)value["clearSha256"], (string)description["sha256"]);

                // the box's ciphertext is AES-256-GCM(CK, n, payload, context): sealing the same payload under the file's CK and n gives it back
                var boxObject = (Dictionary<string, object>)value["box"];
                using (ExchangeAesGcmKey aead = ExchangeE2eNative.NewAesGcmKey(contentKey!))
                {
                    byte[] again = new byte[payload!.Length + 16];
                    aead.Seal(ExchangeE2e.Base64UrlDecode((string)boxObject["n"])!, Encoding.UTF8.GetBytes("clipboard"), payload, 0, payload.Length, again, 0);
                    Assert.Equal((string)boxObject["ct"], ExchangeE2e.Base64UrlEncode(again));
                }

                // the value key and the sealed bytes
                Assert.Equal((string)value["valueKey"], ToHex(ExchangeE2e.DeriveValueKey(contentKey!)));
                byte[] clear = PatternBytes(clearSize);
                Assert.Equal((string)value["clearSha256"], ToHex(Sha256(clear)));
                long segments = Convert.ToInt64(value["segments"], CultureInfo.InvariantCulture);
                Assert.Equal(segments, ExchangeE2e.SegmentCount(clearSize));
                Assert.Equal(Convert.ToInt64(value["sealedSize"], CultureInfo.InvariantCulture), ExchangeE2e.SealedLength(clearSize));

                byte[] sealedBytes = ExchangeE2e.SealBytes(clear, contentKey!);
                Assert.Equal(Convert.ToInt64(value["sealedSize"], CultureInfo.InvariantCulture), sealedBytes.LongLength);
                Assert.Equal((string)value["sealedSha256"], ToHex(Sha256(sealedBytes)));
                int firstClear = (int)Math.Min(clearSize, ExchangeE2e.SegmentSize);
                Assert.Equal((string)value["firstSegmentTag"], ToHex(sealedBytes.Skip(firstClear).Take(16).ToArray()));

                // and it opens
                Assert.True(ExchangeE2e.TryOpenBytes(sealedBytes, contentKey!, out byte[]? opened, out failure), name + ": " + failure);
                Assert.Equal(clear, opened);

                // the cases the file lists as failing
                int fullSegment = ExchangeE2e.SegmentSize + ExchangeE2e.SegmentTagSize;
                foreach (object failingCase in Items(value["failing"]))
                {
                    string text = (string)failingCase;
                    byte[] broken;
                    if (text.StartsWith("truncated", StringComparison.Ordinal))
                        broken = sealedBytes.Take((int)(segments - 1) * fullSegment).ToArray();          // without the last segment
                    else if (text.StartsWith("reordered", StringComparison.Ordinal))
                        broken = sealedBytes.Skip(fullSegment).Take(fullSegment).Concat(sealedBytes.Take(fullSegment)).Concat(sealedBytes.Skip(2 * fullSegment)).ToArray();   // the first two swapped
                    else throw new InvalidOperationException("unknown failing case: " + text);

                    Assert.False(ExchangeE2e.TryOpenBytes(broken, contentKey!, out byte[]? none, out string brokenFailure), name + ": " + text);
                    Assert.Null(none);
                    Assert.Contains(brokenFailure, new[] { "bad-tag", "truncated" });
                }
                run++;
            }
            Assert.Equal(3, run);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(65535)]
        [InlineData(65536)]
        [InlineData(65537)]
        [InlineData(131072)]
        [InlineData(196613)]   // 3 x 65536 + 5
        public void AValueOfAnyShapeRoundTripsThroughTheStreams(int size)
        {
            byte[] contentKey = NewContentKey();
            byte[] clear = NoiseBytes(size, size);

            using (var source = new MemoryStream(clear))
            using (var sealedStream = new MemoryStream())
            {
                ExchangeE2e.SealStream(source, size, contentKey, sealedStream, out byte[] clearHash, out byte[] sealedHash);
                byte[] sealedBytes = sealedStream.ToArray();
                Assert.Equal(ExchangeE2e.SealedLength(size), sealedBytes.LongLength);
                Assert.Equal(ToHex(Sha256(clear)), ToHex(clearHash));
                Assert.Equal(ToHex(Sha256(sealedBytes)), ToHex(sealedHash));
                Assert.Equal(sealedBytes, ExchangeE2e.SealBytes(clear, contentKey));

                using (var sealedIn = new MemoryStream(sealedBytes))
                using (var clearOut = new MemoryStream())
                {
                    Assert.True(ExchangeE2e.TryOpenStream(sealedIn, sealedBytes.Length, contentKey, clearOut, out long clearLength, out byte[]? sha, out string failure), failure);
                    Assert.Equal(size, clearLength);
                    Assert.Equal(clear, clearOut.ToArray());
                    Assert.Equal(ToHex(clearHash), ToHex(sha!));
                    Assert.Equal(sealedBytes.Length, sealedIn.Position);   // read exactly the sealed length
                }
            }
            Assert.True(ExchangeE2e.TryOpenBytes(ExchangeE2e.SealBytes(clear, contentKey), contentKey, out byte[]? viaBytes, out _));
            Assert.Equal(clear, viaBytes);
        }

        [Fact]
        public void AValueOfTwentyMegabytesRoundTripsInBoundedMemory()
        {
            const long size = 20L * 1024 * 1024 + 12345;
            byte[] contentKey = NewContentKey();
            var source = new PatternStream(size);
            var sealedStream = new MemoryStream((int)ExchangeE2e.SealedLength(size));

            ExchangeE2e.SealStream(source, size, contentKey, sealedStream, out byte[] clearHash, out byte[] sealedHash);

            Assert.Equal(ExchangeE2e.SealedLength(size), sealedStream.Length);
            Assert.True(source.LargestRequest <= ExchangeE2e.SegmentSize, "never more than one segment asked of the source at a time");
            Assert.Equal(ToHex(Sha256(sealedStream.ToArray())), ToHex(sealedHash));

            sealedStream.Position = 0;
            var sink = new HashingSink();
            Assert.True(ExchangeE2e.TryOpenStream(sealedStream, sealedStream.Length, contentKey, sink, out long clearLength, out byte[]? sha, out string failure), failure);
            Assert.Equal(size, clearLength);
            Assert.Equal(size, sink.Written);
            Assert.True(sink.LargestWrite <= ExchangeE2e.SegmentSize, "never more than one segment handed to the sink at a time");
            Assert.Equal(ToHex(clearHash), ToHex(sha!));
            Assert.Equal(ToHex(clearHash), sink.HashHex());
        }

        [Fact]
        public void TheStreamsHoldTheDeclaredLengthExactly()
        {
            byte[] contentKey = NewContentKey();
            byte[] clear = NoiseBytes(1000, 1);

            Assert.Throws<EndOfStreamException>(() => ExchangeE2e.SealStream(new MemoryStream(clear), 1001, contentKey, new MemoryStream()));
            Assert.Throws<InvalidDataException>(() => ExchangeE2e.SealStream(new MemoryStream(clear), 999, contentKey, new MemoryStream()));
            Assert.Throws<EndOfStreamException>(() => ExchangeE2e.SealStream(new MemoryStream(NoiseBytes(70000, 2)), 70001, contentKey, new MemoryStream()));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeE2e.SealStream(new MemoryStream(new byte[0]), 0, contentKey, new MemoryStream()));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeE2e.SealBytes(new byte[0], contentKey));
            Assert.Throws<ArgumentException>(() => ExchangeE2e.SealBytes(clear, new byte[31]));
        }

        [Fact]
        public void ASealedValueNeverOpensUnderAnotherKeyOrIsReadPastItsLength()
        {
            byte[] contentKey = NewContentKey();
            byte[] other = NewContentKey();
            byte[] clear = NoiseBytes(70000, 3);
            byte[] sealedBytes = ExchangeE2e.SealBytes(clear, contentKey);

            Assert.False(ExchangeE2e.TryOpenBytes(sealedBytes, other, out byte[]? none, out string failure));
            Assert.Null(none);
            Assert.Equal("bad-tag", failure);

            // bytes beyond the declared length are not read - nothing waits for them on a socket
            byte[] withTail = sealedBytes.Concat(new byte[] { 1, 2, 3 }).ToArray();
            using (var sealedIn = new MemoryStream(withTail))
            using (var clearOut = new MemoryStream())
            {
                Assert.True(ExchangeE2e.TryOpenStream(sealedIn, sealedBytes.Length, contentKey, clearOut, out long length, out _, out failure), failure);
                Assert.Equal(70000, length);
                Assert.Equal(sealedBytes.Length, sealedIn.Position);
            }
        }

        // ================================================================ tampering with sealed bytes

        [Fact]
        public void AnyChangeToTheSealedBytesFailsAndLeavesNothingUsable()
        {
            byte[] contentKey = NewContentKey();
            byte[] clear = NoiseBytes(2 * 65536 + 300, 4);            // three segments, the last short
            byte[] good = ExchangeE2e.SealBytes(clear, contentKey);
            int full = ExchangeE2e.SegmentSize + ExchangeE2e.SegmentTagSize;
            Assert.Equal(2 * full + 300 + 16, good.Length);

            void AssertFails(byte[] bytes, string what, params string[] allowed)
            {
                using (var clearOut = new MemoryStream())
                {
                    bool ok = ExchangeE2e.TryOpenStream(new MemoryStream(bytes), bytes.Length, contentKey, clearOut, out long length, out byte[]? hash, out string failure);
                    Assert.False(ok, what);
                    Assert.Equal(0, length);
                    Assert.Null(hash);
                    Assert.Contains(failure, allowed);
                }
                Assert.False(ExchangeE2e.TryOpenBytes(bytes, contentKey, out byte[]? none, out _), what);
                Assert.Null(none);
            }

            // one bit in each segment: the ciphertext, the tag, the first and the last byte
            foreach (int position in new[] { 0, 1000, full - 17, full - 1, full, full + 5, 2 * full - 1, 2 * full, 2 * full + 100, good.Length - 1 })
            {
                byte[] bad = (byte[])good.Clone();
                bad[position] ^= 0x01;
                AssertFails(bad, "bit " + position, "bad-tag");
            }

            // two segments swapped (each is valid for its own index only)
            byte[] swapped = good.Skip(full).Take(full).Concat(good.Take(full)).Concat(good.Skip(2 * full)).ToArray();
            AssertFails(swapped, "swapped 0/1", "bad-tag");
            byte[] swappedFirstAndLast = good.Skip(2 * full).Concat(good.Skip(full).Take(full)).Concat(good.Take(full)).ToArray();
            AssertFails(swappedFirstAndLast, "swapped 0/2", "bad-tag", "malformed");

            // the last segment dropped: the one before it is now last and was not sealed as last
            AssertFails(good.Take(2 * full).ToArray(), "dropped last", "bad-tag");
            AssertFails(good.Take(full).ToArray(), "only the first", "bad-tag");
            AssertFails(new byte[0], "empty", "truncated");
            AssertFails(new byte[16], "tag only", "truncated");

            // a byte appended, and a whole segment appended
            AssertFails(good.Concat(new byte[] { 0 }).ToArray(), "one byte appended", "bad-tag");
            AssertFails(good.Concat(new byte[20]).ToArray(), "twenty bytes appended", "bad-tag", "malformed");
            AssertFails(good.Concat(good.Take(full)).ToArray(), "a segment appended", "bad-tag");

            // a length that says more than the stream holds
            using (var shortStream = new MemoryStream(good.Take(full + 100).ToArray()))
            {
                Assert.False(ExchangeE2e.TryOpenStream(shortStream, good.Length, contentKey, new MemoryStream(), out long l, out byte[]? h, out string failure));
                Assert.Equal("truncated", failure);
                Assert.Equal(0, l);
                Assert.Null(h);
            }
        }

        [Fact]
        public void ASegmentIsBoundToItsPositionEvenWhenTheValueIsRepeated()
        {
            // the same 65536 bytes twice: both segments seal differently, so a segment cannot stand in for another
            byte[] contentKey = NewContentKey();
            byte[] block = NoiseBytes(65536, 5);
            byte[] sealedBytes = ExchangeE2e.SealBytes(block.Concat(block).ToArray(), contentKey);
            int full = ExchangeE2e.SegmentSize + ExchangeE2e.SegmentTagSize;
            Assert.NotEqual(sealedBytes.Take(full).ToArray(), sealedBytes.Skip(full).ToArray());
        }

        // ================================================================ the Windows server corpus (ticket 234)

        [Fact]
        public void EveryBoxOfTheServerCorpusOpensForEveryRecipientAndNoOneElse()
        {
            Dictionary<string, object> corpus = LoadFixture("fmsw-234-sealed.json");

            // the corpus names the same device keys as the shared vector file
            foreach (Dictionary<string, object> device in Items(corpus["devices"]))
                Assert.Equal(Devices.Value[(string)device["name"]].KemText, (string)((Dictionary<string, object>)device["e2eKey"])["kem"]);

            int boxes = 0, opens = 0;
            foreach (Dictionary<string, object> testCase in Items(corpus["cases"]))
            {
                string name = (string)testCase["name"];
                string context = (string)testCase["context"];
                string boxJson = Json(testCase["box"]);
                var from = (Dictionary<string, object>)testCase["from"];
                VectorDevice sender = Devices.Value[(string)from["device"]];
                string senderId = (string)from["deviceId"];
                Func<string, byte[]?> trust = id => id == senderId ? sender.Kem : null;

                foreach (Dictionary<string, object> to in Items(testCase["to"]))
                {
                    VectorDevice reader = Devices.Value[(string)to["device"]];
                    Assert.True(ExchangeE2e.TryOpenBox(context, boxJson, (string)to["deviceId"], reader.Key, trust,
                        out byte[]? payload, out byte[]? contentKey, out string failure), name + " for " + reader.Name + ": " + failure);
                    Assert.Equal(Canonical(testCase["payload"]), Canonical(ParseJson(payload!)));
                    opens++;

                    // the same box under a context it was not sealed for
                    Assert.False(ExchangeE2e.TryOpenBox(context + "-moved", boxJson, (string)to["deviceId"], reader.Key, trust, out _, out _, out failure), name);
                    Assert.Equal("bad-tag", failure);

                    if (testCase.ContainsKey("clipboard"))
                    {
                        // the sealed bytes the server counted: this clear text under the box's content key
                        var clipboard = (Dictionary<string, object>)testCase["clipboard"];
                        byte[] clear = Encoding.UTF8.GetBytes((string)clipboard["clear"]);
                        Assert.Equal((string)clipboard["clearSha256"], ToHex(Sha256(clear)));
                        byte[] sealedBytes = ExchangeE2e.SealBytes(clear, contentKey!);
                        Assert.Equal(Convert.ToInt64(clipboard["sealedSize"], CultureInfo.InvariantCulture), sealedBytes.LongLength);
                        Assert.Equal((string)clipboard["sealedSha256"], ToHex(Sha256(sealedBytes)));
                        Assert.True(ExchangeE2e.TryOpenBytes(sealedBytes, contentKey!, out byte[]? opened, out failure), failure);
                        Assert.Equal(clear, opened);
                    }
                }

                // a recipient of another box of the corpus has no entry here unless it was addressed
                var addressed = new HashSet<string>(Items(testCase["to"]).Cast<Dictionary<string, object>>().Select(t => (string)t["deviceId"]));
                Assert.False(ExchangeE2e.TryOpenBox(context, boxJson, "unaddressed-device", Devices.Value["stranger"].Key, trust, out _, out _, out string noEntry));
                Assert.Equal("no-entry", noEntry);
                Assert.DoesNotContain("unaddressed-device", addressed);
                boxes++;
            }
            Assert.Equal(12, boxes);
            Assert.True(opens >= 12, opens.ToString(CultureInfo.InvariantCulture));
        }

        // ================================================================ helpers

        private sealed class VectorDevice
        {
            public string Name = "";
            public string DeviceId = "";
            public string KemText = "";
            public byte[] Kem = new byte[0];
            public byte[] PrivateKey = new byte[0];
            public string CheckCode = "";
            public ExchangeDeviceKey Key = null!;
        }

        // The five devices of the shared vector file, by name. Public test data: the keys are the file's.
        private static readonly Lazy<Dictionary<string, VectorDevice>> Devices = new Lazy<Dictionary<string, VectorDevice>>(() =>
        {
            Dictionary<string, object> file = LoadFixture("e2e-v1.json");
            var result = new Dictionary<string, VectorDevice>();
            foreach (Dictionary<string, object> entry in Items(file["devices"]))
            {
                var key = (Dictionary<string, object>)entry["e2eKey"];
                var device = new VectorDevice
                {
                    Name = (string)entry["name"],
                    DeviceId = (string)entry["deviceId"],
                    KemText = (string)key["kem"],
                    Kem = ExchangeE2e.Base64UrlDecode((string)key["kem"])!,
                    PrivateKey = Hex((string)entry["privateKey"]),
                    CheckCode = (string)entry["checkCode"],
                };
                device.Key = ExchangeDeviceKey.FromPrivateScalar(device.PrivateKey);
                result[device.Name] = device;
            }
            return result;
        });

        /// <summary>Plays back fixed draws: the content key, the nonce, then one ephemeral scalar per recipient (the same one each time).</summary>
        private sealed class ScriptedSource : IExchangeE2eSource
        {
            private readonly Queue<byte[]> _fills;
            private readonly byte[] _ephemeralScalar;

            public ScriptedSource(byte[] contentKey, byte[] nonce, byte[] ephemeralScalar)
            {
                _fills = new Queue<byte[]>(new[] { contentKey, nonce });
                _ephemeralScalar = ephemeralScalar;
            }

            public void Fill(byte[] buffer)
            {
                byte[] next = _fills.Dequeue();
                Assert.Equal(next.Length, buffer.Length);
                Buffer.BlockCopy(next, 0, buffer, 0, buffer.Length);
            }

            public ExchangeDeviceKey NewEphemeralKey() => ExchangeDeviceKey.FromPrivateScalar(_ephemeralScalar);
        }

        /// <summary>A read-only stream of <c>i mod 251</c>, the value the vector file's clipboard cases use - without holding it.</summary>
        private sealed class PatternStream : Stream
        {
            private readonly long _length;
            private long _position;

            public PatternStream(long length) { _length = length; }

            public int LargestRequest { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                LargestRequest = Math.Max(LargestRequest, count);
                int n = (int)Math.Min(count, _length - _position);
                for (int i = 0; i < n; i++) buffer[offset + i] = (byte)((_position + i) % 251);
                _position += n;
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>A write-only stream that hashes and counts what it is given.</summary>
        private sealed class HashingSink : Stream
        {
            private readonly SHA256 _hash = SHA256.Create();
            private byte[]? _result;

            public long Written { get; private set; }
            public int LargestWrite { get; private set; }

            public string HashHex()
            {
                if (_result == null)
                {
                    _hash.TransformFinalBlock(new byte[0], 0, 0);
                    _result = _hash.Hash;
                }
                return ToHex(_result);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                LargestWrite = Math.Max(LargestWrite, count);
                Written += count;
                _hash.TransformBlock(buffer, offset, count, null, 0);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Written;
            public override long Position { get => Written; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        private static byte[] NewContentKey()
        {
            byte[] key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            return key;
        }

        /// <summary>Repeatable pseudo-random bytes (not secret).</summary>
        private static byte[] NoiseBytes(int length, int seed)
        {
            byte[] bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        /// <summary>The value of the vector file's clipboard cases: byte <c>i</c> is <c>i mod 251</c>.</summary>
        private static byte[] PatternBytes(long length)
        {
            byte[] bytes = new byte[length];
            for (long i = 0; i < length; i++) bytes[i] = (byte)(i % 251);
            return bytes;
        }

        private static Dictionary<string, object> LoadFixture(string name)
            => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(FixtureFolder, name), new UTF8Encoding(false)));

        private static IEnumerable<object> Items(object list) => ((IList)list).Cast<object>();

        private static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value);

        private static object ParseJson(byte[] utf8) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Encoding.UTF8.GetString(utf8));

        /// <summary>A comparison form of a parsed JSON value: members sorted, no whitespace, numbers in invariant form.</summary>
        private static string Canonical(object? value)
        {
            switch (value)
            {
                case null: return "null";
                case string text: return "\"" + text + "\"";
                case bool flag: return flag ? "true" : "false";
                case IDictionary dictionary:
                    return "{" + string.Join(",", dictionary.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal)
                        .Select(k => "\"" + k + "\":" + Canonical(dictionary[k]))) + "}";
                case IList list:
                    return "[" + string.Join(",", list.Cast<object>().Select(Canonical)) + "]";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        private static byte[] StripCarriageReturns(byte[] bytes)
        {
            var result = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                if (!(bytes[i] == 0x0D && i + 1 < bytes.Length && bytes[i + 1] == 0x0A))
                    result.Add(bytes[i]);
            return result.ToArray();
        }

        private static byte[] Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(bytes);
        }

        private static byte[] Hex(string text)
        {
            byte[] bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(text.Substring(2 * i, 2), 16);
            return bytes;
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
