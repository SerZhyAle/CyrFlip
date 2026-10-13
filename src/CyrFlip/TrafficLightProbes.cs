using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// A probe that waits on the network. It honours <paramref name="ct"/> by closing its socket - the close is
    /// queued to the pool, never run inline (the <c>OllamaClient</c> TD-3 rule: closing a half-read stream on
    /// netfx can block) - and never writes anything anywhere.
    /// </summary>
    internal interface IAsyncTrafficProbe
    {
        Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct);
    }

    /// <summary>
    /// A probe that makes a synchronous call which cannot be cancelled (<c>Directory.Exists</c>, an enumeration
    /// or <c>DriveInfo</c> on a dead SMB server can block for tens of seconds). The scheduler runs these on its
    /// four dedicated worker threads and abandons the call at the hard timeout (D22).
    /// </summary>
    internal interface IBlockingTrafficProbe
    {
        ProbeOutcome Run(TrafficLightTarget target);
    }

    /// <summary>The probes by kind. The real set is <see cref="CreateDefault"/>; tests build their own.</summary>
    internal sealed class TrafficLightProbeSet
    {
        private readonly Dictionary<string, IAsyncTrafficProbe> _async;
        private readonly Dictionary<string, IBlockingTrafficProbe> _blocking;

        public TrafficLightProbeSet(IDictionary<string, IAsyncTrafficProbe>? asyncProbes,
            IDictionary<string, IBlockingTrafficProbe>? blockingProbes)
        {
            _async = asyncProbes != null ? new Dictionary<string, IAsyncTrafficProbe>(asyncProbes) : new Dictionary<string, IAsyncTrafficProbe>();
            _blocking = blockingProbes != null ? new Dictionary<string, IBlockingTrafficProbe>(blockingProbes) : new Dictionary<string, IBlockingTrafficProbe>();
        }

        public static TrafficLightProbeSet CreateDefault() => new TrafficLightProbeSet(
            new Dictionary<string, IAsyncTrafficProbe>
            {
                [TrafficLightKinds.Ping] = new PingProbe(),
                [TrafficLightKinds.Tcp] = new TcpProbe(),
                [TrafficLightKinds.Http] = new HttpProbe(),
                [TrafficLightKinds.Sftp] = new SftpProbe(),
                [TrafficLightKinds.Exchange] = new ExchangeProbe(),
            },
            new Dictionary<string, IBlockingTrafficProbe>
            {
                [TrafficLightKinds.Folder] = new FolderProbe(),
                [TrafficLightKinds.Drive] = new DriveProbe(),
            });

        public bool IsBlocking(string kind) => _blocking.ContainsKey(kind);
        public IAsyncTrafficProbe? Async(string kind) => _async.TryGetValue(kind, out IAsyncTrafficProbe? probe) ? probe : null;
        public IBlockingTrafficProbe? Blocking(string kind) => _blocking.TryGetValue(kind, out IBlockingTrafficProbe? probe) ? probe : null;
    }

    /// <summary>The shared mapping from the framework's failures to the invariant reason codes.</summary>
    internal static class ProbeNet
    {
        public static string ReasonOf(Exception ex, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return TrafficReasons.Timeout;
            Exception e = ex is AggregateException aggregate && aggregate.InnerException != null ? aggregate.InnerException : ex;
            if (e is OperationCanceledException) return TrafficReasons.Timeout;
            if (e is PingException && e.InnerException != null) e = e.InnerException;
            if (e is IOException && e.InnerException is SocketException ioSocket) e = ioSocket;
            if (e is SocketException socket) return ReasonOf(socket.SocketErrorCode);
            if (e is ObjectDisposedException) return TrafficReasons.Timeout;
            return TrafficReasons.Failed;
        }

        public static string ReasonOf(SocketError error)
        {
            switch (error)
            {
                case SocketError.ConnectionRefused: return TrafficReasons.ConnectionRefused;
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                case SocketError.NoRecovery: return TrafficReasons.NameNotResolved;
                case SocketError.NetworkUnreachable:
                case SocketError.HostUnreachable:
                case SocketError.NetworkDown:
                case SocketError.HostDown: return TrafficReasons.NetworkUnreachable;
                case SocketError.TimedOut: return TrafficReasons.Timeout;
                case SocketError.AccessDenied: return TrafficReasons.AccessDenied;
                default: return TrafficReasons.Failed;
            }
        }

        /// <summary>Runs <paramref name="close"/> on the pool - a close inline can block on a half-read stream.</summary>
        public static void CloseOnPool(Action close)
            => ThreadPool.QueueUserWorkItem(_ => { try { close(); } catch (Exception ex) { TrafficLightLog.Fault("probe-close", ex); } });

        public static int Millis(Stopwatch watch) => (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);

        /// <summary>
        /// Connects to <paramref name="host"/>:<paramref name="port"/>. On success the connected client is
        /// returned; on failure the outcome is.
        /// </summary>
        public static async Task<KeyValuePair<TcpClient?, ProbeOutcome?>> ConnectAsync(string host, int port, Stopwatch watch, CancellationToken ct)
        {
            var client = new TcpClient();
            CancellationTokenRegistration registration = ct.Register(() => CloseOnPool(client.Close));
            try
            {
                await client.ConnectAsync(host, port).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    registration.Dispose();
                    CloseOnPool(client.Close);
                    return new KeyValuePair<TcpClient?, ProbeOutcome?>(null, ProbeOutcome.Fail(TrafficReasons.Timeout));
                }
                registration.Dispose();
                return new KeyValuePair<TcpClient?, ProbeOutcome?>(client, null);
            }
            catch (Exception ex)
            {
                registration.Dispose();
                CloseOnPool(client.Close);
                return new KeyValuePair<TcpClient?, ProbeOutcome?>(null, ProbeOutcome.Fail(ReasonOf(ex, ct), MillisOrZero(watch)));
            }
        }

        private static int MillisOrZero(Stopwatch watch) => watch.IsRunning ? Millis(watch) : 0;
    }

    /// <summary>One ICMP echo (<c>ping</c>).</summary>
    internal sealed class PingProbe : IAsyncTrafficProbe
    {
        public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
        {
            string host = target.Address.Trim();
            var watch = Stopwatch.StartNew();
            Ping ping = new Ping();
            try
            {
                PingReply reply = await ping.SendPingAsync(host, target.EffectiveTimeoutMs).ConfigureAwait(false);
                watch.Stop();
                switch (reply.Status)
                {
                    case IPStatus.Success:
                        return ProbeOutcome.Good((int)Math.Max(0, Math.Min(int.MaxValue, reply.RoundtripTime)));
                    case IPStatus.TimedOut:
                    case IPStatus.TimeExceeded:
                    case IPStatus.TtlExpired:
                        return ProbeOutcome.Fail(TrafficReasons.Timeout);
                    case IPStatus.DestinationHostUnreachable:
                    case IPStatus.DestinationNetworkUnreachable:
                    case IPStatus.DestinationUnreachable:
                    case IPStatus.DestinationProtocolUnreachable:
                    case IPStatus.DestinationPortUnreachable:
                        return ProbeOutcome.Fail(TrafficReasons.NetworkUnreachable);
                    case IPStatus.BadDestination:
                        return ProbeOutcome.Fail(TrafficReasons.NameNotResolved);
                    default:
                        return ProbeOutcome.Fail(TrafficReasons.Failed);
                }
            }
            catch (Exception ex)
            {
                return ProbeOutcome.Fail(ProbeNet.ReasonOf(ex, ct));
            }
            finally
            {
                ProbeNet.CloseOnPool(ping.Dispose);
            }
        }
    }

    /// <summary>One TCP connect, then close (<c>tcp</c>).</summary>
    internal sealed class TcpProbe : IAsyncTrafficProbe
    {
        public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
        {
            if (!TrafficLightAddress.TryParseHostPort(target.Address, out string host, out int port))
                return ProbeOutcome.Fail(TrafficReasons.InvalidAddress);
            var watch = Stopwatch.StartNew();
            KeyValuePair<TcpClient?, ProbeOutcome?> result = await ProbeNet.ConnectAsync(host, port, watch, ct).ConfigureAwait(false);
            watch.Stop();
            if (result.Value != null) return result.Value;
            ProbeNet.CloseOnPool(result.Key!.Close);
            return ProbeOutcome.Good(ProbeNet.Millis(watch));
        }
    }

    /// <summary>
    /// <c>sftp</c>: connect and read the server's identification line - <c>SSH-2.0-...</c> or
    /// <c>SSH-1.99-...</c>, at most 255 bytes. <b>Nothing is sent</b> and no key exchange happens, so this proves
    /// "an SSH server answers", not "this is the share" (section 5).
    /// </summary>
    internal sealed class SftpProbe : IAsyncTrafficProbe
    {
        public const int MaxLine = 255;

        public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
        {
            if (!TrafficLightAddress.TryParseHostPort(target.Address, out string host, out int port))
                return ProbeOutcome.Fail(TrafficReasons.InvalidAddress);
            var watch = Stopwatch.StartNew();
            KeyValuePair<TcpClient?, ProbeOutcome?> connected = await ProbeNet.ConnectAsync(host, port, watch, ct).ConfigureAwait(false);
            if (connected.Value != null) return connected.Value;

            TcpClient client = connected.Key!;
            CancellationTokenRegistration registration = ct.Register(() => ProbeNet.CloseOnPool(client.Close));
            try
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[MaxLine];
                int length = 0;
                while (length < MaxLine)
                {
                    int read = await stream.ReadAsync(buffer, length, MaxLine - length, ct).ConfigureAwait(false);
                    if (read == 0) break; // the server closed without saying anything
                    length += read;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0) break;
                }
                watch.Stop();
                if (length == 0) return ProbeOutcome.Fail(TrafficReasons.NotSshServer);
                string line = System.Text.Encoding.ASCII.GetString(buffer, 0, length);
                return IsIdentification(line)
                    ? ProbeOutcome.Good(ProbeNet.Millis(watch))
                    : ProbeOutcome.Fail(TrafficReasons.NotSshServer);
            }
            catch (Exception ex)
            {
                return ProbeOutcome.Fail(ProbeNet.ReasonOf(ex, ct));
            }
            finally
            {
                registration.Dispose();
                ProbeNet.CloseOnPool(client.Close);
            }
        }

        /// <summary>The first line is an SSH identification string of protocol 2.0 or 1.99 (RFC 4253 section 4.2).</summary>
        internal static bool IsIdentification(string text)
        {
            int end = text.IndexOf('\n');
            string line = (end >= 0 ? text.Substring(0, end) : text).TrimEnd('\r');
            return line.StartsWith("SSH-2.0-", StringComparison.Ordinal) || line.StartsWith("SSH-1.99-", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <c>exchange</c>: connect, run the TLS client handshake with SNI = the host, hash the leaf certificate and
    /// close. <b>No application byte is ever sent</b> (D20). The validation callback only records the chain
    /// verdict and the leaf hash; because no application data follows, no trust decision is made on behalf of a
    /// data transfer, and nothing is pinned, remembered or silently accepted.
    /// </summary>
    internal sealed class ExchangeProbe : IAsyncTrafficProbe
    {
        public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
        {
            if (!TrafficLightAddress.TryParseHostPort(target.Address, out string host, out int port))
                return ProbeOutcome.Fail(TrafficReasons.InvalidAddress);
            var watch = Stopwatch.StartNew();
            KeyValuePair<TcpClient?, ProbeOutcome?> connected = await ProbeNet.ConnectAsync(host, port, watch, ct).ConfigureAwait(false);
            if (connected.Value != null) return connected.Value;

            TcpClient client = connected.Key!;
            CancellationTokenRegistration registration = ct.Register(() => ProbeNet.CloseOnPool(client.Close));
            string observed = "";
            SslPolicyErrors policy = SslPolicyErrors.None;
            try
            {
                using (var ssl = new SslStream(client.GetStream(), false, (sender, certificate, chain, errors) =>
                {
                    policy = errors;
                    if (certificate != null) observed = FingerprintOf(certificate);
                    return true; // recorded, not decided: nothing is sent after the handshake
                }))
                {
                    await ssl.AuthenticateAsClientAsync(host, null, SslProtocols.None, false).ConfigureAwait(false);
                }
                watch.Stop();
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return ProbeOutcome.Fail(TrafficReasons.Timeout);
                // A handshake that fails is "no TLS answer": the port is open and something else is there.
                if (ex is AuthenticationException || ex is IOException || ex is InvalidOperationException)
                    return ProbeOutcome.Fail(TrafficReasons.TlsFailed, ProbeNet.Millis(watch));
                return ProbeOutcome.Fail(ProbeNet.ReasonOf(ex, ct));
            }
            finally
            {
                registration.Dispose();
                ProbeNet.CloseOnPool(client.Close);
            }

            return Verdict(target.Fingerprint, observed, policy, ProbeNet.Millis(watch));
        }

        /// <summary>
        /// The verdict table of section 5, pure: with a pin the leaf must match it; without one the chain must be
        /// trusted by Windows or the endpoint is "alive, not verified" (yellow). A mismatch is red and a security
        /// class - the tooltip shows the pinned and the observed value.
        /// </summary>
        internal static ProbeOutcome Verdict(string pinned, string observed, SslPolicyErrors policy, int latencyMs)
        {
            if (!string.IsNullOrEmpty(pinned))
            {
                if (string.Equals(pinned, observed, StringComparison.Ordinal))
                    return ProbeOutcome.Good(latencyMs);
                return ProbeOutcome.Fail(TrafficReasons.CertificateChanged, latencyMs,
                    "pinned " + pinned + " / observed " + (observed.Length == 0 ? "-" : observed));
            }
            return policy == SslPolicyErrors.None
                ? ProbeOutcome.Good(latencyMs)
                : ProbeOutcome.Alive(latencyMs, TrafficReasons.Unverified);
        }

        /// <summary><c>SHA256:</c> and the base64 of the SHA-256 of the DER leaf, without padding (43 characters).</summary>
        internal static string FingerprintOf(X509Certificate certificate)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(certificate.GetRawCertData());
                return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
            }
        }
    }

    /// <summary>
    /// <c>http</c> (D8): HEAD, then GET - response headers only, the body is never read - on a 4xx or 5xx
    /// answer to HEAD that is not already on the accepted list, sent to the address HEAD's redirects ended at;
    /// redirects followed up to five; the system proxy; a plain CyrFlip User-Agent; no cookies and no
    /// credentials. The status of the <b>final</b> answer is judged against the accepted list.
    /// </summary>
    internal sealed class HttpProbe : IAsyncTrafficProbe
    {
        public const int MaxRedirects = 5;

        public async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
        {
            TlsPolicy.EnsureTls12();
            TrafficLightStatusCodes accepted = TrafficLightStatusCodes.Parse(target.AcceptedStatusCodes);
            var watch = Stopwatch.StartNew();

            Answer answer = await SendAsync(target, "HEAD", target.Address, ct).ConfigureAwait(false);
            // A server that refuses HEAD (405, a 4xx or 5xx of its own) is asked once more with GET - headers
            // only. Not when HEAD's answer is one the person has already accepted: a second request could not
            // improve a green verdict, and it costs the same again. And the second request starts where the
            // first one ended, so a redirect chain is walked once, not twice.
            if (answer.Failure == null && answer.Status >= 400 && !accepted.Contains(answer.Status))
            {
                string from = answer.FinalAddress.Length > 0 ? answer.FinalAddress : target.Address;
                Answer get = await SendAsync(target, "GET", from, ct).ConfigureAwait(false);
                if (get.Failure != null || get.Status > 0) answer = get;
            }
            watch.Stop();
            if (answer.Failure != null) return answer.Failure;
            return Judge(answer.Status, accepted, ProbeNet.Millis(watch), answer.Note);
        }

        /// <summary>A completed answer is reachable; its status decides green or yellow (D7).</summary>
        internal static ProbeOutcome Judge(int status, TrafficLightStatusCodes accepted, int latencyMs, string note)
            => accepted.Contains(status)
                ? ProbeOutcome.Good(latencyMs, note)
                : ProbeOutcome.Alive(latencyMs, TrafficReasons.Http(status), note);

        private struct Answer
        {
            public int Status;
            public ProbeOutcome? Failure;
            public string Note;
            /// <summary>Where the request ended up after its redirects; empty when it could not be told.</summary>
            public string FinalAddress;
        }

        private static string FinalOf(WebResponse response)
        {
            try { return response.ResponseUri?.AbsoluteUri ?? ""; }
            catch (Exception) { return ""; }
        }

        private static async Task<Answer> SendAsync(TrafficLightTarget target, string method, string address, CancellationToken ct)
        {
            string note = "";
            HttpWebRequest request;
            try { request = (HttpWebRequest)WebRequest.Create(address); }
            catch (Exception) { return new Answer { Failure = ProbeOutcome.Fail(TrafficReasons.InvalidAddress), Note = "" }; }

            request.Method = method;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = MaxRedirects;
            request.UserAgent = "CyrFlip/" + SupportBundle.AppVersion() + " (availability check)";
            request.KeepAlive = false;
            request.CookieContainer = null;
            request.Credentials = null;
            request.UseDefaultCredentials = false;
            request.Proxy = WebRequest.DefaultWebProxy;
            request.Timeout = target.EffectiveTimeoutMs;
            request.ReadWriteTimeout = target.EffectiveTimeoutMs;
            request.ServicePoint.ConnectionLimit = 8;
            bool allowUntrusted = target.AllowUntrustedCertificate;
            bool sawUntrusted = false;
            request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) =>
            {
                if (errors != SslPolicyErrors.None) sawUntrusted = true;
                return errors == SslPolicyErrors.None || allowUntrusted;
            };

            CancellationTokenRegistration registration = ct.Register(() => ProbeNet.CloseOnPool(request.Abort));
            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    string final = FinalOf(response);
                    // The headers are in; the body is never read. Aborting - rather than closing - is what
                    // keeps netfx from draining the rest of a large answer before it lets go.
                    if (method == "GET") request.Abort();
                    if (sawUntrusted && allowUntrusted) note = "untrusted certificate accepted";
                    return new Answer { Status = status, Note = note, FinalAddress = final };
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    int status = (int)errorResponse.StatusCode;
                    string final = FinalOf(errorResponse);
                    try { request.Abort(); } catch (Exception abort) { TrafficLightLog.Fault("http-abort", abort); }
                    // HttpWebRequest throws a protocol error for a 3xx only when the redirect limit ran out.
                    if (status >= 300 && status < 400)
                        return new Answer { Failure = ProbeOutcome.Fail(TrafficReasons.TooManyRedirects) };
                    if (sawUntrusted && allowUntrusted) note = "untrusted certificate accepted";
                    return new Answer { Status = status, Note = note, FinalAddress = final };
                }
                return new Answer { Failure = ProbeOutcome.Fail(ReasonOf(ex, ct)) };
            }
            catch (Exception ex)
            {
                return new Answer { Failure = ProbeOutcome.Fail(ProbeNet.ReasonOf(ex, ct)) };
            }
            finally
            {
                registration.Dispose();
            }
        }

        private static string ReasonOf(WebException ex, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return TrafficReasons.Timeout;
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                case WebExceptionStatus.RequestCanceled:
                    return TrafficReasons.Timeout;
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return TrafficReasons.NameNotResolved;
                case WebExceptionStatus.ConnectFailure:
                    return ex.InnerException is SocketException socket ? ProbeNet.ReasonOf(socket.SocketErrorCode) : TrafficReasons.ConnectionRefused;
                case WebExceptionStatus.TrustFailure:
                    return TrafficReasons.CertificateNotTrusted;
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.ReceiveFailure:
                    return TrafficReasons.TlsFailed;
                default:
                    return TrafficReasons.Failed;
            }
        }
    }

    /// <summary>
    /// <c>folder</c> (D1): exists and <b>enumerable</b> - an enumeration is opened and at most one entry read,
    /// never a whole listing. Nothing is written. Blocking: the calls cannot be cancelled.
    /// </summary>
    internal sealed class FolderProbe : IBlockingTrafficProbe
    {
        public ProbeOutcome Run(TrafficLightTarget target)
        {
            var watch = Stopwatch.StartNew();
            string path = target.Address.Trim();
            try
            {
                // One operation, not an existence test followed by an enumeration: on a share that does not
                // answer each of them waits out the same network timeout, and the exception of the single
                // enumeration already tells "denied" from "not there".
                using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
                    entries.MoveNext(); // at most one entry
                return ProbeOutcome.Good(ProbeNet.Millis(watch));
            }
            catch (UnauthorizedAccessException)
            {
                return ProbeOutcome.Fail(TrafficReasons.AccessDenied, ProbeNet.Millis(watch));
            }
            catch (DirectoryNotFoundException)
            {
                return ProbeOutcome.Fail(TrafficReasons.PathNotFound, ProbeNet.Millis(watch));
            }
            catch (IOException)
            {
                return ProbeOutcome.Fail(TrafficReasons.PathNotFound, ProbeNet.Millis(watch));
            }
            catch (Exception)
            {
                return ProbeOutcome.Fail(TrafficReasons.Failed, ProbeNet.Millis(watch));
            }
        }
    }

    /// <summary>
    /// <c>drive</c> (D18): the letter is among the drives Windows lists and <c>DriveInfo.IsReady</c>. Identified
    /// by letter only; a drive that comes back under another letter reads "not present".
    /// </summary>
    internal sealed class DriveProbe : IBlockingTrafficProbe
    {
        public ProbeOutcome Run(TrafficLightTarget target)
        {
            var watch = Stopwatch.StartNew();
            string letter = TrafficLightAddress.NormalizeDrive(target.Address.Trim());
            if (!TrafficLightAddress.IsDriveLetter(letter)) return ProbeOutcome.Fail(TrafficReasons.InvalidAddress);
            try
            {
                DriveInfo? found = null;
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.Name.Length > 0 && char.ToUpperInvariant(drive.Name[0]) == char.ToUpperInvariant(letter[0]))
                    {
                        found = drive;
                        break;
                    }
                }
                if (found == null) return ProbeOutcome.Fail(TrafficReasons.DrivePresentNot, ProbeNet.Millis(watch));
                return found.IsReady
                    ? ProbeOutcome.Good(ProbeNet.Millis(watch))
                    : ProbeOutcome.Fail(TrafficReasons.DrivePresentNot, ProbeNet.Millis(watch));
            }
            catch (Exception)
            {
                return ProbeOutcome.Fail(TrafficReasons.Failed, ProbeNet.Millis(watch));
            }
        }
    }
}
