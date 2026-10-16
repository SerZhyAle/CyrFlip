using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>What the person has settled by the time a password is typed (items AD and BN).</summary>
    internal sealed class ExchangeJoinRequest
    {
        public string Host = "";
        public int Port = ExchangeConnectionString.DefaultPort;
        /// <summary>The pin the person accepted, or the string's <c>fp</c>; <see cref="ExchangePin.None"/> when Windows validates the chain.</summary>
        public ExchangePin Pin = ExchangePin.None;
        /// <summary>The label <c>n</c> of the connection string - unverified, shown beside the host and never instead of it.</summary>
        public string DisplayName = "";
        public string Login = "";
        public string Password = "";
        public string DeviceName = "";
    }

    /// <summary>
    /// The quick join of <c>DEVICE-EXCHANGE</c> item AD, after the trust step: identity (a fresh <c>deviceId</c> and
    /// device key, or the existing entry's), <c>enroll</c>, and the secrets into DPAPI. The password lives in the
    /// request and the one <c>enroll</c> exchange and is never stored (5.2).
    /// </summary>
    internal static class ExchangeEnrollment
    {
        /// <summary>16 random bytes, base64url without padding: 22 characters (6.1, item AM).</summary>
        public static string NewDeviceId()
        {
            byte[] bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return ExchangeE2e.Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Enrolls this device in the account. With <paramref name="existing"/> - the same identity signing in
        /// again (item AA) or an entry asking for the password (item AT) - the <c>deviceId</c> and the device key
        /// stay, and the server rotates the token. Without it a fresh identity is made, and an <c>id-collision</c>
        /// answer regenerates it once (item AD step 3); a second collision is shown as a refusal.
        /// </summary>
        public static async Task<ExchangeOutcome<ExchangeAccountEntry>> JoinAsync(ExchangeClient client, ExchangeJoinRequest r,
            ExchangeAccountEntry? existing, CancellationToken ct)
        {
            ExchangeDeviceKey? key = existing != null ? KeyOf(existing) : null;
            string deviceId = existing != null && key != null ? existing.DeviceId : NewDeviceId();
            if (key == null) key = ExchangeDeviceKey.Generate();

            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    ExchangeOutcome<ExchangeWelcome> enrolled = await client.EnrollAsync(new ExchangeEnrollRequest
                    {
                        Host = r.Host,
                        Port = r.Port,
                        Pin = r.Pin,
                        Login = r.Login,
                        Password = r.Password,
                        DeviceId = deviceId,
                        DeviceName = r.DeviceName,
                        DeviceKem = key.PublicKemBase64Url,
                    }, ct).ConfigureAwait(false);

                    if (!enrolled.Ok)
                    {
                        if (enrolled.Code == ExchangeCodes.IdCollision && attempt == 0 && (existing == null || KeyOf(existing) == null))
                        {
                            deviceId = NewDeviceId();
                            key.Dispose();
                            key = ExchangeDeviceKey.Generate(); // the key lives as long as its id: a new id gets a new key
                            continue;
                        }
                        return ExchangeOutcome.Fail<ExchangeAccountEntry>(enrolled.Code, enrolled.Trust);
                    }

                    ExchangeWelcome w = enrolled.Value!;
                    ExchangeAccountEntry entry = existing ?? new ExchangeAccountEntry();
                    entry.Host = r.Host;
                    entry.Port = r.Port;
                    entry.SetPin(r.Pin);
                    entry.Login = r.Login;
                    entry.DeviceId = deviceId;
                    if (r.DisplayName.Length > 0) entry.DisplayName = r.DisplayName;
                    entry.AccountKind = w.AccountKind;
                    entry.SignIn = ExchangeSignInState.Ok;
                    entry.ServerVersion = w.ServerVersion;
                    entry.Features = w.Features.ToList();
                    entry.DeviceKem = key.PublicKemBase64Url;

                    byte[] scalar = key.PrivateScalar;
                    try
                    {
                        entry.SaveSecret(new ExchangeSecret { Token = w.DeviceToken, DevicePrivate = ExchangeE2e.Base64UrlEncode(scalar) });
                    }
                    finally { Array.Clear(scalar, 0, scalar.Length); }
                    return ExchangeOutcome.Success(entry);
                }
            }
            finally { key.Dispose(); }
        }

        /// <summary>The entry's device key from its secret, or null when it is gone or is not the key its public half names.</summary>
        private static ExchangeDeviceKey? KeyOf(ExchangeAccountEntry entry)
        {
            ExchangeSecret? secret = entry.LoadSecret();
            if (secret == null || secret.DevicePrivate.Length == 0) return null;
            byte[]? scalar = ExchangeE2e.Base64UrlDecode(secret.DevicePrivate);
            if (scalar == null || scalar.Length != 32) return null;
            try
            {
                ExchangeDeviceKey key = ExchangeDeviceKey.FromPrivateScalar(scalar);
                if (string.Equals(key.PublicKemBase64Url, entry.DeviceKem, StringComparison.Ordinal)) return key;
                key.Dispose();
                return null;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException)
            {
                return null;
            }
            finally { Array.Clear(scalar, 0, scalar.Length); }
        }

        /// <summary>
        /// The entry of the same identity already on this device (item AA), or null: adding it again re-enrolls
        /// that entry instead of making a second one.
        /// </summary>
        public static ExchangeAccountEntry? FindSameIdentity(System.Collections.Generic.IEnumerable<ExchangeAccountEntry> entries, string host, int port, string login)
        {
            string identity = host.ToLowerInvariant() + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + login.ToLowerInvariant();
            return entries.FirstOrDefault(e => string.Equals(e.Identity, identity, StringComparison.Ordinal));
        }
    }
}
