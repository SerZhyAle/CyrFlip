using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>Why an entry asks for the password again (<c>DEVICE-EXCHANGE</c> items I and AT).</summary>
    internal enum ExchangeSignInState
    {
        Ok,
        /// <summary><c>signed-out</c>: the account's password was changed; every device signs in again.</summary>
        SignedOut,
        /// <summary><c>device-revoked</c>: this device was removed from the account.</summary>
        DeviceRemoved,
        /// <summary><c>bad-credentials</c> on a <c>hello</c>: the server no longer knows this token.</summary>
        BadCredentials,
    }

    /// <summary>
    /// An account entry of this device (<c>DEVICE-EXCHANGE</c> items AA and AD, ticket S0074): the address, the
    /// login, the pin, this entry's own <c>deviceId</c> and the public half of its device key live in
    /// <c>HKCU</c> as JSON; the <b>device token and the private key never do</b> - they live in
    /// <see cref="ExchangeSecrets"/>, DPAPI-protected for the current user. The password is not stored at all.
    /// </summary>
    internal sealed class ExchangeAccountEntry
    {
        /// <summary>This device's local name for the entry; the key of its secrets. Never on the wire.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Host { get; set; } = "";
        public int Port { get; set; } = ExchangeConnectionString.DefaultPort;

        /// <summary>The pinned leaf, <c>SHA256:</c> plus base64; empty when Windows validates the chain.</summary>
        public string PinLeaf { get; set; } = "";
        /// <summary>The fingerprint of the pinned leaf's public key (item AY); empty exactly when <see cref="PinLeaf"/> is.</summary>
        public string PinKey { get; set; } = "";

        public string Login { get; set; } = "";
        /// <summary>16 random bytes, base64url, 22 characters; generated before <c>enroll</c> and kept for the entry's life.</summary>
        public string DeviceId { get; set; } = "";
        /// <summary>The server's display name <c>n</c> from the connection string - an unverified label.</summary>
        public string DisplayName { get; set; } = "";
        /// <summary><c>member</c> or <c>guest</c>, as the server answered.</summary>
        public string AccountKind { get; set; } = ExchangeAccountEntry.KindMember;
        public ExchangeSignInState SignIn { get; set; } = ExchangeSignInState.Ok;
        public string ServerVersion { get; set; } = "";
        /// <summary>The <c>features</c> the server listed at the last <c>welcome</c>.</summary>
        public List<string> Features { get; set; } = new List<string>();
        /// <summary>This entry's public device key, base64url of the 65-byte SEC1 point (<c>e2eKey.kem</c>).</summary>
        public string DeviceKem { get; set; } = "";
        /// <summary>
        /// The key first seen for each other <c>deviceId</c> of the account (item DG, trust on first use):
        /// <c>deviceId</c> to base64url <c>kem</c>. Public values; never replaced, only forgotten with the device.
        /// </summary>
        public Dictionary<string, string> TrustedKeys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public const string KindMember = "member";
        public const string KindGuest = "guest";
        public const int MaxTrustedKeys = 256;

        public ExchangePin Pin => new ExchangePin(PinLeaf, PinKey);

        public bool IsGuest => string.Equals(AccountKind, KindGuest, StringComparison.Ordinal);

        public bool NeedsSignIn => SignIn != ExchangeSignInState.Ok;

        /// <summary>Whether the last <c>welcome</c> listed <paramref name="feature"/>; false before any connection.</summary>
        public bool ServerHas(string feature) => Features.Contains(feature);

        /// <summary>The host and port the person typed or the string named - the one address ever shown (item DA).</summary>
        public string Address => TrafficLightAddress.JoinHostPort(Host, Port);

        /// <summary>What a list shows for the entry: the label if there is one, else the login, else the address.</summary>
        public string Title => DisplayName.Length > 0 ? DisplayName : (Login.Length > 0 ? Login : Address);

        /// <summary>
        /// An entry's identity on this device (item AA): the host lower-cased, the port and the login compared
        /// case-insensitively. A second entry of one identity is the same entry signing in again.
        /// </summary>
        public string Identity => (Host.ToLowerInvariant() + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                   + "/" + Login.ToLowerInvariant());

        public void SetPin(ExchangePin pin)
        {
            PinLeaf = pin.Leaf;
            PinKey = pin.Key;
        }

        public ExchangeSecret? LoadSecret() => ExchangeSecrets.Current.Load(Id);

        public void SaveSecret(ExchangeSecret secret) => ExchangeSecrets.Current.Save(Id, secret);

        public void DeleteSecret() => ExchangeSecrets.Current.Delete(Id);

        // ---- the JSON in HKCU: no token, no private key, no password ----

        public static string SerializeList(IEnumerable<ExchangeAccountEntry> entries)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (ExchangeAccountEntry e in entries)
            {
                var trusted = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in e.TrustedKeys) trusted[pair.Key] = pair.Value;
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = e.Id,
                    ["host"] = e.Host,
                    ["port"] = e.Port,
                    ["fp"] = e.PinLeaf,
                    ["fk"] = e.PinKey,
                    ["login"] = e.Login,
                    ["deviceId"] = e.DeviceId,
                    ["name"] = e.DisplayName,
                    ["kind"] = e.AccountKind,
                    ["signIn"] = SignInToken(e.SignIn),
                    ["serverVersion"] = e.ServerVersion,
                    ["features"] = e.Features.ToArray(),
                    ["kem"] = e.DeviceKem,
                    ["trusted"] = trusted,
                });
            }
            return new JavaScriptSerializer().Serialize(list);
        }

        /// <summary>
        /// The entries a stored value holds. Tolerant by design: a row without a host is dropped, an unknown
        /// member is ignored, and the older <c>needsSignIn</c> flag reads as a plain "sign in again".
        /// </summary>
        public static List<ExchangeAccountEntry> DeserializeList(string? json)
        {
            TryDeserializeList(json, out List<ExchangeAccountEntry> list);
            return list;
        }

        /// <summary>False when the value is not a JSON array at all (an unreadable value, never to be saved over).</summary>
        public static bool TryDeserializeList(string? json, out List<ExchangeAccountEntry> result)
        {
            result = new List<ExchangeAccountEntry>();
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                object parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
                if (!(parsed is object[] rows)) return false;
                foreach (object row in rows)
                {
                    if (!(row is Dictionary<string, object> map)) continue;
                    var entry = new ExchangeAccountEntry();
                    string id = Text(map, "id");
                    if (id.Length > 0) entry.Id = id;
                    entry.Host = Text(map, "host");
                    if (map.TryGetValue("port", out object? port) && int.TryParse(Convert.ToString(port, System.Globalization.CultureInfo.InvariantCulture), out int p) && p > 0 && p < 65536)
                        entry.Port = p;
                    entry.PinLeaf = Text(map, "fp");
                    entry.PinKey = Text(map, "fk");
                    if (entry.PinLeaf.Length == 0) entry.PinKey = "";
                    entry.Login = Text(map, "login");
                    entry.DeviceId = Text(map, "deviceId");
                    entry.DisplayName = Text(map, "name");
                    string kind = Text(map, "kind");
                    entry.AccountKind = kind == KindGuest ? KindGuest : KindMember;
                    entry.SignIn = SignInFromToken(Text(map, "signIn"));
                    if (entry.SignIn == ExchangeSignInState.Ok && map.TryGetValue("needsSignIn", out object? nsi)
                        && bool.TryParse(Convert.ToString(nsi, System.Globalization.CultureInfo.InvariantCulture), out bool needs) && needs)
                        entry.SignIn = ExchangeSignInState.BadCredentials;
                    entry.ServerVersion = Text(map, "serverVersion");
                    if (map.TryGetValue("features", out object? features) && features is object[] fa)
                        foreach (object f in fa) if (f is string fs && fs.Length > 0) entry.Features.Add(fs);
                    entry.DeviceKem = Text(map, "kem");
                    if (map.TryGetValue("trusted", out object? trusted) && trusted is Dictionary<string, object> tmap)
                        foreach (KeyValuePair<string, object> t in tmap)
                            if (t.Value is string kem && kem.Length > 0 && entry.TrustedKeys.Count < MaxTrustedKeys)
                                entry.TrustedKeys[t.Key] = kem;
                    if (entry.Host.Length > 0) result.Add(entry);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                result = new List<ExchangeAccountEntry>();
                return false; // an unreadable value yields no entries; the caller never saves over it until the person edits
            }
            return true;
        }

        private static string Text(Dictionary<string, object> map, string name)
            => map.TryGetValue(name, out object? v) && v is string s ? s : "";

        internal static string SignInToken(ExchangeSignInState state)
        {
            switch (state)
            {
                case ExchangeSignInState.SignedOut: return "signed-out";
                case ExchangeSignInState.DeviceRemoved: return "device-revoked";
                case ExchangeSignInState.BadCredentials: return "bad-credentials";
                default: return "ok";
            }
        }

        internal static ExchangeSignInState SignInFromToken(string token)
        {
            switch (token)
            {
                case "signed-out": return ExchangeSignInState.SignedOut;
                case "device-revoked": return ExchangeSignInState.DeviceRemoved;
                case "bad-credentials": return ExchangeSignInState.BadCredentials;
                default: return ExchangeSignInState.Ok;
            }
        }
    }

    /// <summary>The two secrets of an entry: the device token and the private half of its device key.</summary>
    internal sealed class ExchangeSecret
    {
        public string Token = "";
        /// <summary>The 32-byte P-256 private scalar, base64url without padding.</summary>
        public string DevicePrivate = "";
    }

    internal interface IExchangeSecretStore
    {
        ExchangeSecret? Load(string entryId);
        void Save(string entryId, ExchangeSecret secret);
        void Delete(string entryId);
    }

    /// <summary>Where the secrets of the entries are looked up; swapped for an in-memory store by the tests.</summary>
    internal static class ExchangeSecrets
    {
        private static IExchangeSecretStore? _override;

        public static IExchangeSecretStore Current => _override ?? DpapiRegistrySecretStore.Shared;

        internal static void Use(IExchangeSecretStore? store) => _override = store;
    }

    /// <summary>
    /// One registry value per entry under <c>HKCU\Software\CyrFlip\ExchangeSecrets</c>: base64 of the DPAPI
    /// (<c>CurrentUser</c>) blob of a small JSON document. Never exported, never in a support bundle, never logged.
    /// </summary>
    internal sealed class DpapiRegistrySecretStore : IExchangeSecretStore
    {
        public static readonly DpapiRegistrySecretStore Shared = new DpapiRegistrySecretStore();

        private const string SubKey = @"Software\CyrFlip\ExchangeSecrets";

        public ExchangeSecret? Load(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return null;
            try
            {
                using (IRuntimeRegistryKey? key = RuntimeRegistry.User.OpenSubKey(SubKey))
                {
                    if (!(key?.GetValue(entryId) is string cipher) || cipher.Length == 0) return null;
                    byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
                    try
                    {
                        var map = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(plain)) as Dictionary<string, object>;
                        if (map == null) return null;
                        return new ExchangeSecret
                        {
                            Token = map.TryGetValue("t", out object? t) && t is string ts ? ts : "",
                            DevicePrivate = map.TryGetValue("k", out object? k) && k is string ks ? ks : "",
                        };
                    }
                    finally { Array.Clear(plain, 0, plain.Length); }
                }
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException
                                       || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                return null; // a blob another user or another machine wrote: as good as absent
            }
        }

        public void Save(string entryId, ExchangeSecret secret)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(
                new Dictionary<string, object> { ["t"] = secret.Token, ["k"] = secret.DevicePrivate }));
            try
            {
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                using (IRuntimeRegistryKey key = RuntimeRegistry.User.CreateSubKey(SubKey))
                    key.SetValue(entryId, Convert.ToBase64String(cipher), RegistryValueKind.String);
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public void Delete(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            try
            {
                using (IRuntimeRegistryKey? key = RuntimeRegistry.User.OpenSubKey(SubKey, true))
                    key?.DeleteValue(entryId, false);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
