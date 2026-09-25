using System.Net;

namespace CyrFlip
{
    /// <summary>
    /// The one place CyrFlip touches <see cref="ServicePointManager.SecurityProtocol"/> (ticket S0010
    /// TD-4). A 4.7+ target - net48 is one - starts at <see cref="SecurityProtocolType.SystemDefault"/>
    /// (0), which lets Windows negotiate TLS 1.2 <b>and</b> 1.3; OR-ing <c>Tls12</c> into that value
    /// turns it into exactly <c>Tls12</c> and switches TLS 1.3 off for the whole process. The old
    /// premise - "net48 defaults to SSL3/TLS 1.0" - holds only for pre-4.7 targets, or for a machine
    /// whose registry forces the legacy default, which is the one case where adding TLS 1.2 helps.
    /// </summary>
    internal static class TlsPolicy
    {
        /// <summary><paramref name="current"/>, with TLS 1.2 added unless the OS already decides.</summary>
        internal static SecurityProtocolType WithTls12(SecurityProtocolType current)
            => current == SecurityProtocolType.SystemDefault ? current : current | SecurityProtocolType.Tls12;

        public static void EnsureTls12()
        {
            try { ServicePointManager.SecurityProtocol = WithTls12(ServicePointManager.SecurityProtocol); }
            catch { /* an older machine without TLS 1.2 support - let the request try anyway */ }
        }
    }
}
