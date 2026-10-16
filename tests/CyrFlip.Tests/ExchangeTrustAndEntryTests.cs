using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>The trust rule of <c>DEVICE-EXCHANGE</c> items D and AY, and what an account entry keeps (items AA, AD, 5.2).</summary>
    public class ExchangeTrustTests
    {
        private static readonly ExchangePin Pinned = new ExchangePin("SHA256:leaf-A", "SHA256:key-A");

        [Fact]
        public void NoPinAndAChainWindowsTrustsPinsNothingAndAccepts()
        {
            ExchangeTrustResult r = ExchangeTrust.Evaluate(ExchangePin.None, "SHA256:leaf-A", "SHA256:key-A", SslPolicyErrors.None);
            Assert.Equal(ExchangeTrustKind.Platform, r.Kind);
            Assert.True(r.Accepts);
            Assert.True(r.PinAfter(ExchangePin.None).IsEmpty);
        }

        [Fact]
        public void NoPinAndAChainWindowsDoesNotTrustIsNotAcceptedUntilThePersonSaysSo()
        {
            ExchangeTrustResult r = ExchangeTrust.Evaluate(ExchangePin.None, "SHA256:leaf-A", "SHA256:key-A", SslPolicyErrors.RemoteCertificateChainErrors);
            Assert.Equal(ExchangeTrustKind.NeedsAcceptance, r.Kind);
            Assert.False(r.Accepts);
        }

        [Fact]
        public void ThePinnedLeafIsTrustedWhateverWindowsThinksOfItsNameOrDates()
        {
            ExchangeTrustResult r = ExchangeTrust.Evaluate(Pinned, "SHA256:leaf-A", "SHA256:key-A",
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch);
            Assert.Equal(ExchangeTrustKind.PinMatched, r.Kind);
            Assert.True(r.Accepts);
        }

        [Fact]
        public void ALeafWithTheSameKeyIsARenewalAndTheNewLeafReplacesTheKeptOne()
        {
            ExchangeTrustResult r = ExchangeTrust.Evaluate(Pinned, "SHA256:leaf-B", "SHA256:key-A", SslPolicyErrors.RemoteCertificateChainErrors);
            Assert.Equal(ExchangeTrustKind.PinRenewed, r.Kind);
            Assert.True(r.Accepts);
            ExchangePin after = r.PinAfter(Pinned);
            Assert.Equal("SHA256:leaf-B", after.Leaf);
            Assert.Equal("SHA256:key-A", after.Key);
        }

        [Fact]
        public void ALeafWithAnotherKeyIsRefusedWhateverTheChainSays()
        {
            // even a leaf Windows trusts: a pin is the entry's own decision (item D)
            ExchangeTrustResult r = ExchangeTrust.Evaluate(Pinned, "SHA256:leaf-C", "SHA256:key-C", SslPolicyErrors.None);
            Assert.Equal(ExchangeTrustKind.Changed, r.Kind);
            Assert.False(r.Accepts);
            Assert.Equal("SHA256:leaf-C", r.ObservedLeaf);
        }

        [Fact]
        public void APinFromAConnectionStringHasNoKeyYetAndADifferentLeafIsRefused()
        {
            var fromString = new ExchangePin("SHA256:leaf-A", "");
            Assert.Equal(ExchangeTrustKind.PinMatched, ExchangeTrust.Evaluate(fromString, "SHA256:leaf-A", "SHA256:key-A", SslPolicyErrors.RemoteCertificateChainErrors).Kind);
            Assert.Equal(ExchangeTrustKind.Changed, ExchangeTrust.Evaluate(fromString, "SHA256:leaf-B", "SHA256:key-A", SslPolicyErrors.RemoteCertificateChainErrors).Kind);
        }

        [Fact]
        public void AMissingCertificateIsNeverAccepted()
        {
            Assert.False(ExchangeTrust.Evaluate(Pinned, (X509Certificate?)null, SslPolicyErrors.RemoteCertificateNotAvailable).Accepts);
            Assert.False(ExchangeTrust.Evaluate(ExchangePin.None, (X509Certificate?)null, SslPolicyErrors.RemoteCertificateNotAvailable).Accepts);
        }

        [Fact]
        public void TheKeyPinIsTheSha256OfTheSubjectPublicKeyInfoOfTheLeaf()
        {
            using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256)!)
            {
                var request = new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256);
                using (X509Certificate2 cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)))
                {
                    byte[] der = cert.RawData;
                    byte[]? spki = ExchangeTrust.SubjectPublicKeyInfo(der);
                    Assert.NotNull(spki);
                    // a SubjectPublicKeyInfo is a SEQUENCE whose last element is a BIT STRING holding the key bits
                    Assert.Equal(0x30, spki![0]);
                    byte[] publicKey = cert.GetPublicKey(); // the uncompressed point: 04 || X || Y
                    Assert.True(IndexOf(spki, publicKey) > 0, "the key bits sit inside the SubjectPublicKeyInfo");
                    Assert.True(spki.Length < der.Length);
                    Assert.StartsWith("SHA256:", ExchangeTrust.KeyPin(der));
                    Assert.NotEqual(ExchangeTrust.LeafPin(der), ExchangeTrust.KeyPin(der));
                    // a renewal with the same key keeps the key pin and changes the leaf pin
                    using (X509Certificate2 renewed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(60)))
                    {
                        Assert.Equal(ExchangeTrust.KeyPin(der), ExchangeTrust.KeyPin(renewed.RawData));
                        Assert.NotEqual(ExchangeTrust.LeafPin(der), ExchangeTrust.LeafPin(renewed.RawData));
                    }
                }
            }
        }

        [Fact]
        public void TheFingerprintFormIsShaPrefixAndFortyThreeBase64Characters()
        {
            string pin = ExchangeTrust.LeafPin(new byte[] { 1, 2, 3 });
            Assert.StartsWith("SHA256:", pin);
            Assert.Equal(43, pin.Length - "SHA256:".Length);
            Assert.DoesNotContain("=", pin);
            Assert.True(TrafficLightAddress.IsValidFingerprint(pin));
        }

        [Fact]
        public void ABrokenCertificateYieldsNoKeyPinRatherThanAGuess()
        {
            Assert.Null(ExchangeTrust.SubjectPublicKeyInfo(new byte[] { 0x30, 0x03, 1, 2, 3 }));
            Assert.Null(ExchangeTrust.SubjectPublicKeyInfo(new byte[0]));
            Assert.Equal("", ExchangeTrust.KeyPin(new byte[] { 0x30, 0xFF }));
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }

    [Collection("ExchangeSecrets")]
    public class ExchangeAccountEntryTests : IDisposable
    {
        private readonly InMemorySecretStore _store = new InMemorySecretStore();

        public ExchangeAccountEntryTests() => ExchangeSecrets.Use(_store);

        public void Dispose() => ExchangeSecrets.Use(null);

        private static ExchangeAccountEntry Sample() => new ExchangeAccountEntry
        {
            Id = "entry-1",
            Host = "nas.example.org",
            Port = 22022,
            PinLeaf = "SHA256:leaf",
            PinKey = "SHA256:key",
            Login = "alice",
            DeviceId = "L866lMziLsSZZSADAoyL9A",
            DisplayName = "Home NAS",
            DeviceKem = "BAAA",
            ServerVersion = "1.0",
            Features = { "clipboard", "sealed" },
            TrustedKeys = { ["dev-phone"] = "BMHB" },
        };

        [Fact]
        public void TheJsonInTheRegistryCarriesNoTokenNoPrivateKeyAndNoPassword()
        {
            ExchangeAccountEntry e = Sample();
            e.SaveSecret(new ExchangeSecret { Token = "tok_SECRET_123", DevicePrivate = "PRIVATE_SCALAR_456" });

            string json = ExchangeAccountEntry.SerializeList(new[] { e });

            Assert.DoesNotContain("tok_SECRET_123", json);
            Assert.DoesNotContain("PRIVATE_SCALAR_456", json);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("alice", json);
            Assert.Contains("L866lMziLsSZZSADAoyL9A", json);
        }

        [Fact]
        public void AnEntryRoundTripsWithItsPinItsKeysAndItsState()
        {
            ExchangeAccountEntry e = Sample();
            e.SignIn = ExchangeSignInState.SignedOut;
            e.AccountKind = ExchangeAccountEntry.KindGuest;

            ExchangeAccountEntry back = ExchangeAccountEntry.DeserializeList(ExchangeAccountEntry.SerializeList(new[] { e })).Single();

            Assert.Equal("entry-1", back.Id);
            Assert.Equal("nas.example.org", back.Host);
            Assert.Equal(22022, back.Port);
            Assert.Equal("SHA256:leaf", back.PinLeaf);
            Assert.Equal("SHA256:key", back.PinKey);
            Assert.Equal("alice", back.Login);
            Assert.Equal("Home NAS", back.DisplayName);
            Assert.Equal(ExchangeSignInState.SignedOut, back.SignIn);
            Assert.True(back.IsGuest);
            Assert.True(back.NeedsSignIn);
            Assert.Equal(new[] { "clipboard", "sealed" }, back.Features);
            Assert.Equal("BMHB", back.TrustedKeys["dev-phone"]);
            Assert.Equal("BAAA", back.DeviceKem);
        }

        [Fact]
        public void AnUnreadableOrEmptyValueYieldsNoEntriesAndARowWithoutAHostIsDropped()
        {
            Assert.Empty(ExchangeAccountEntry.DeserializeList(null));
            Assert.Empty(ExchangeAccountEntry.DeserializeList(""));
            Assert.Empty(ExchangeAccountEntry.DeserializeList("{not json"));
            Assert.Empty(ExchangeAccountEntry.DeserializeList("[{\"login\":\"x\"}]"));
        }

        [Fact]
        public void TheOlderNeedsSignInFlagReadsAsAPlainSignInAgain()
        {
            ExchangeAccountEntry e = ExchangeAccountEntry.DeserializeList("[{\"host\":\"h\",\"port\":22022,\"needsSignIn\":true}]").Single();
            Assert.True(e.NeedsSignIn);
        }

        [Fact]
        public void APinHasNoKeyWithoutALeaf()
        {
            ExchangeAccountEntry e = ExchangeAccountEntry.DeserializeList("[{\"host\":\"h\",\"fk\":\"SHA256:orphan\"}]").Single();
            Assert.True(e.Pin.IsEmpty);
            Assert.Equal("", e.PinKey);
        }

        [Fact]
        public void TheIdentityIsTheHostLowerCasedThePortAndTheLoginCaseInsensitive()
        {
            var a = new ExchangeAccountEntry { Host = "NAS.Example.org", Port = 22022, Login = "Alice" };
            var b = new ExchangeAccountEntry { Host = "nas.example.org", Port = 22022, Login = "alice" };
            var c = new ExchangeAccountEntry { Host = "nas.example.org", Port = 4444, Login = "alice" };
            Assert.Equal(a.Identity, b.Identity);
            Assert.NotEqual(a.Identity, c.Identity);
            Assert.Same(a, ExchangeEnrollment.FindSameIdentity(new[] { a, c }, "nas.example.ORG", 22022, "ALICE"));
            Assert.Null(ExchangeEnrollment.FindSameIdentity(new[] { a, c }, "other", 22022, "alice"));
        }

        [Fact]
        public void TheSecretsLiveInTheStoreAndAreForgottenWithTheEntry()
        {
            ExchangeAccountEntry e = Sample();
            e.SaveSecret(new ExchangeSecret { Token = "t", DevicePrivate = "k" });
            Assert.Equal("t", e.LoadSecret()!.Token);
            Assert.Equal("k", e.LoadSecret()!.DevicePrivate);
            e.DeleteSecret();
            Assert.Null(e.LoadSecret());
            Assert.Equal(0, _store.Count);
        }

        [Fact]
        public void TheTitleIsTheLabelThenTheLoginThenTheAddress()
        {
            var e = new ExchangeAccountEntry { Host = "h.example", Port = 22022, Login = "bob", DisplayName = "Office" };
            Assert.Equal("Office", e.Title);
            e.DisplayName = "";
            Assert.Equal("bob", e.Title);
            e.Login = "";
            Assert.Equal("h.example:22022", e.Title);
        }

        [Fact]
        public void TheSignInTokensRoundTrip()
        {
            foreach (ExchangeSignInState s in Enum.GetValues(typeof(ExchangeSignInState)))
                Assert.Equal(s, ExchangeAccountEntry.SignInFromToken(ExchangeAccountEntry.SignInToken(s)));
            Assert.Equal(ExchangeSignInState.Ok, ExchangeAccountEntry.SignInFromToken("whatever"));
        }
    }

    [CollectionDefinition("ExchangeSecrets", DisableParallelization = true)]
    public class ExchangeSecretsCollection { }
}
