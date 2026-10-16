using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>The answer to an operation: ok, or a code of <see cref="ExchangeCodes"/> that names why not.</summary>
    internal class ExchangeOutcome
    {
        public bool Ok => Code.Length == 0;
        public string Code { get; set; } = "";

        /// <summary>The trust verdict when the failure is about the server's certificate.</summary>
        public ExchangeTrustResult? Trust { get; set; }

        public static ExchangeOutcome<T> Success<T>(T value) => new ExchangeOutcome<T> { Value = value };
        public static ExchangeOutcome<T> Fail<T>(string code, ExchangeTrustResult? trust = null)
            => new ExchangeOutcome<T> { Code = code, Trust = trust };
    }

    internal sealed class ExchangeOutcome<T> : ExchangeOutcome
    {
        public T? Value { get; set; }
    }

    /// <summary>What <c>welcome</c> or <c>enrolled</c> says (<c>DEVICE-EXCHANGE</c> 7.2).</summary>
    internal sealed class ExchangeWelcome
    {
        public string Account = "";
        public string AccountKind = ExchangeAccountEntry.KindMember;
        public List<string> Features = new List<string>();
        public string ServerVersion = "";
        /// <summary>Only on <c>enrolled</c>. A secret: it goes to the secret store and nowhere else.</summary>
        public string DeviceToken = "";

        public bool Has(string feature) => Features.Contains(feature);
        public bool IsGuest => AccountKind == ExchangeAccountEntry.KindGuest;

        internal static ExchangeWelcome From(ExchangeFrame f)
        {
            var w = new ExchangeWelcome
            {
                Account = f.Str("account"),
                AccountKind = f.Str("accountKind") == ExchangeAccountEntry.KindGuest ? ExchangeAccountEntry.KindGuest : ExchangeAccountEntry.KindMember,
                ServerVersion = f.Str("serverVersion"),
                DeviceToken = f.Str("deviceToken"),
            };
            w.Features.AddRange(f.Strings("features"));
            return w;
        }
    }

    /// <summary>A device of the account as the directory lists it (6.1), reduced to what sealing needs.</summary>
    internal sealed class ExchangePeer
    {
        public string DeviceId = "";
        public string Name = "";
        /// <summary>The record declares <c>e2e-v1</c>.</summary>
        public bool Capable;
        /// <summary><c>e2eKey.kem</c>, base64url, or empty when the record carries none.</summary>
        public string Kem = "";
        /// <summary><c>updatedAt</c> as UTC ticks; 0 when absent or unreadable.</summary>
        public long UpdatedAtUtcTicks;
    }

    /// <summary>The members of <c>clipboard-state.value</c> and <c>clipboard-value</c> (items BU and DN).</summary>
    internal sealed class ExchangeValueHeader
    {
        public string ClipId = "";
        public string Kind = "";
        public long Size = -1;
        public string Sha256 = "";
        public string StoredAt = "";
        public string SourceDeviceId = "";
        public string SourceDeviceName = "";
        /// <summary>The <c>sealed</c> box as JSON text, or empty when the value carries none.</summary>
        public string BoxJson = "";

        internal static ExchangeValueHeader From(ExchangeFrame f) => new ExchangeValueHeader
        {
            ClipId = f.Str("clipId"),
            Kind = f.Str("kind"),
            Size = f.Long("size"),
            Sha256 = f.Str("sha256"),
            StoredAt = f.Str("storedAt"),
            SourceDeviceId = f.Str("sourceDeviceId"),
            SourceDeviceName = f.Str("sourceDeviceName"),
            BoxJson = f.JsonOf("sealed"),
        };
    }

    internal sealed class ExchangeEnrollRequest
    {
        public string Host = "";
        public int Port = ExchangeConnectionString.DefaultPort;
        public ExchangePin Pin = ExchangePin.None;
        public string Login = "";
        public string Password = "";
        public string DeviceId = "";
        public string DeviceName = "";
        public string DeviceKem = "";
    }

    /// <summary>An authenticated stream and what its <c>welcome</c> said.</summary>
    internal sealed class ExchangeSession : IDisposable
    {
        public ExchangeConnection Connection { get; }
        public ExchangeWelcome Welcome { get; }

        public ExchangeSession(ExchangeConnection connection, ExchangeWelcome welcome)
        {
            Connection = connection;
            Welcome = welcome;
        }

        public void Dispose() => Connection.Dispose();
    }

    /// <summary>
    /// The client of the exchange server wire for the subset a clipboard device needs (ticket S0074):
    /// <c>enroll</c>, <c>hello</c>, <c>list</c>, <c>revoke</c>, <c>clipboard-info</c>, <c>clipboard-put</c> and
    /// <c>clipboard-get</c>. It decides nothing about the content: sealing and opening belong to
    /// <see cref="ExchangeClipboardService"/>.
    ///
    /// <para>Every method returns an <see cref="ExchangeOutcome"/> and never throws for a network or protocol
    /// failure; a failure is a code. A person never reads the code (item DA) - <see cref="ExchangeMessages"/> does.</para>
    /// </summary>
    internal sealed class ExchangeClient
    {
        public const int MaxLoginChars = 256;
        public const int MaxPasswordBytes = 1024;
        public const int MaxDeviceNameChars = 64;

        private readonly IExchangeTransport _transport;
        private readonly string _productVersion;

        public ExchangeClient(IExchangeTransport? transport = null, string? productVersion = null)
        {
            _transport = transport ?? new TlsExchangeTransport();
            _productVersion = Clip(productVersion ?? SupportBundle.AppVersion(), 64);
        }

        // ---- before a password: what does the server's certificate say (BN, AD step 2) ----

        /// <summary>
        /// Opens TLS to the endpoint and sends not a byte (item BQ): the verdict of the trust rule, with both
        /// fingerprints, so the person can be shown what they are about to trust before a password exists.
        /// </summary>
        public async Task<ExchangeOutcome<ExchangeTrustResult>> ProbeAsync(string host, int port, ExchangePin pin, CancellationToken ct)
        {
            ExchangeConnection? conn = null;
            try
            {
                conn = await _transport.ConnectAsync(host, port, pin, true, ct).ConfigureAwait(false);
                return ExchangeOutcome.Success(conn.Trust);
            }
            catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
            {
                return ExchangeOutcome.Fail<ExchangeTrustResult>(CodeOf(ex, conn, ct, afterHandshake: false));
            }
            finally { conn?.Dispose(); }
        }

        // ---- enroll: the one place a password is sent (5.2) ----

        public async Task<ExchangeOutcome<ExchangeWelcome>> EnrollAsync(ExchangeEnrollRequest r, CancellationToken ct)
        {
            // item BR: a credential that is empty, or that the wire would refuse by size, never leaves the program
            if (r.Login.Length == 0 || r.Password.Length == 0 || r.DeviceId.Length == 0)
                return ExchangeOutcome.Fail<ExchangeWelcome>(ExchangeCodes.EmptyCredential);
            if (r.Login.Length > MaxLoginChars || Encoding.UTF8.GetByteCount(r.Password) > MaxPasswordBytes
                || HasControl(r.Login) || HasControl(r.Password))
                return ExchangeOutcome.Fail<ExchangeWelcome>(ExchangeCodes.BadCredentials);

            ExchangeConnection? conn = null;
            bool handshake = false;
            try
            {
                conn = await _transport.ConnectAsync(r.Host, r.Port, r.Pin, false, ct).ConfigureAwait(false);
                handshake = true;
                await conn.SendAsync(ExchangeMembers.Enroll(r.Login, r.Password, r.DeviceId,
                    Clip(r.DeviceName.Length == 0 ? Environment.MachineName : r.DeviceName, MaxDeviceNameChars),
                    _productVersion, r.DeviceKem), ct).ConfigureAwait(false);
                ExchangeFrame? answer = await conn.ReceiveAsync(false, ct).ConfigureAwait(false);
                if (answer == null) return ExchangeOutcome.Fail<ExchangeWelcome>(ExchangeCodes.Unanswered);
                if (answer.Type == "refused") return ExchangeOutcome.Fail<ExchangeWelcome>(ReasonOf(answer));
                if (answer.Type != "enrolled" || answer.Str("deviceToken").Length == 0)
                    return ExchangeOutcome.Fail<ExchangeWelcome>(ExchangeCodes.Unanswered);
                return ExchangeOutcome.Success(ExchangeWelcome.From(answer));
            }
            catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
            {
                return ExchangeOutcome.Fail<ExchangeWelcome>(CodeOf(ex, conn, ct, handshake), (ex as ExchangeFailure)?.Trust);
            }
            finally { conn?.Dispose(); }
        }

        // ---- an authenticated stream for an entry: hello, then welcome (5.2, 7.2) ----

        /// <summary>
        /// Opens a stream for <paramref name="entry"/> and says hello with its token. The entry is not changed:
        /// the caller applies <see cref="ExchangeSession.Welcome"/> and a renewed pin
        /// (<see cref="ExchangeConnection.Trust"/>) when it saves. A refusal that asks for the password
        /// comes back as its own code, never retried silently (item BR).
        /// </summary>
        public async Task<ExchangeOutcome<ExchangeSession>> OpenAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeSecret? secret = entry.LoadSecret();
            if (secret == null || secret.Token.Length == 0 || entry.DeviceId.Length == 0)
                return ExchangeOutcome.Fail<ExchangeSession>(ExchangeCodes.BadCredentials);

            ExchangeConnection? conn = null;
            bool handshake = false;
            try
            {
                conn = await _transport.ConnectAsync(entry.Host, entry.Port, entry.Pin, false, ct).ConfigureAwait(false);
                handshake = true;
                await conn.SendAsync(ExchangeMembers.Hello(entry.DeviceId, secret.Token, _productVersion), ct).ConfigureAwait(false);
                ExchangeFrame? answer = await conn.ReceiveAsync(false, ct).ConfigureAwait(false);
                if (answer == null) return CloseWith(conn, ExchangeCodes.Unanswered);
                if (answer.Type == "refused") return CloseWith(conn, ReasonOf(answer));
                if (answer.Type != "welcome") return CloseWith(conn, ExchangeCodes.Unanswered);

                var session = new ExchangeSession(conn, ExchangeWelcome.From(answer));
                conn = null; // the session owns it now
                return ExchangeOutcome.Success(session);
            }
            catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
            {
                string code = CodeOf(ex, conn, ct, handshake);
                ExchangeTrustResult? trust = (ex as ExchangeFailure)?.Trust;
                conn?.Dispose();
                return ExchangeOutcome.Fail<ExchangeSession>(code, trust);
            }
        }

        private static ExchangeOutcome<ExchangeSession> CloseWith(ExchangeConnection conn, string code)
        {
            conn.Dispose();
            return ExchangeOutcome.Fail<ExchangeSession>(code);
        }

        // ---- the control stream: list and clipboard-info ----

        /// <summary><c>list</c>: the account's devices as the directory lists them right now (7.4).</summary>
        public async Task<ExchangeOutcome<List<ExchangePeer>>> ListAsync(ExchangeSession session, CancellationToken ct)
        {
            try
            {
                await session.Connection.SendAsync(ExchangeMembers.List(), ct).ConfigureAwait(false);
                ExchangeFrame? answer = await ReceiveAnswerAsync(session, ct, "directory").ConfigureAwait(false);
                if (answer == null) return ExchangeOutcome.Fail<List<ExchangePeer>>(ExchangeCodes.Unanswered);
                if (answer.Type == "refused") return ExchangeOutcome.Fail<List<ExchangePeer>>(ReasonOf(answer));

                var peers = new List<ExchangePeer>();
                foreach (ExchangeFrame d in answer.Objects("devices"))
                {
                    ExchangeFrame? key = d.Obj("e2eKey");
                    peers.Add(new ExchangePeer
                    {
                        DeviceId = d.Str("deviceId"),
                        Name = d.Str("deviceName"),
                        Capable = d.Strings("capabilities").Contains(ExchangeWire.CapabilityE2e),
                        Kem = key != null && key.Long("v") == 1 ? key.Str("kem") : "",
                        UpdatedAtUtcTicks = TicksOf(d.Str("updatedAt")),
                    });
                }
                return ExchangeOutcome.Success(peers);
            }
            catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
            {
                return ExchangeOutcome.Fail<List<ExchangePeer>>(CodeOf(ex, session.Connection, ct, true));
            }
        }

        /// <summary><c>clipboard-info</c> (item BY): the value the clipboard holds, or null when it is empty.</summary>
        public async Task<ExchangeOutcome<ExchangeValueHeader?>> InfoAsync(ExchangeSession session, CancellationToken ct)
        {
            try
            {
                await session.Connection.SendAsync(ExchangeMembers.ClipboardInfo(), ct).ConfigureAwait(false);
                ExchangeFrame? answer = await ReceiveAnswerAsync(session, ct, "clipboard-state").ConfigureAwait(false);
                if (answer == null) return ExchangeOutcome.Fail<ExchangeValueHeader?>(ExchangeCodes.Unanswered);
                if (answer.Type == "refused") return ExchangeOutcome.Fail<ExchangeValueHeader?>(ReasonOf(answer));
                ExchangeFrame? value = answer.Obj("value");
                return ExchangeOutcome.Success<ExchangeValueHeader?>(value == null ? null : ExchangeValueHeader.From(value));
            }
            catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
            {
                return ExchangeOutcome.Fail<ExchangeValueHeader?>(CodeOf(ex, session.Connection, ct, true));
            }
        }

        // ---- the data streams (items BW and BY) ----

        /// <summary>
        /// <c>clipboard-put</c> of a sealed value. <paramref name="writeSealedBytes"/> is called once the server
        /// said <c>clipboard-ready</c> and must write <b>exactly</b> <paramref name="sealedSize"/> raw bytes to the
        /// stream it is given - no frames, no chunk headers. The put succeeded only on <c>clipboard-stored</c>.
        /// </summary>
        public async Task<ExchangeOutcome<ExchangeValueHeader>> PutSealedAsync(ExchangeAccountEntry entry,
            string boxJson, long sealedSize, Action<Stream> writeSealedBytes, Func<ExchangeWelcome, string?> precheck, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok) return ExchangeOutcome.Fail<ExchangeValueHeader>(opened.Code, opened.Trust);
            using (ExchangeSession session = opened.Value!)
            {
                string? blocked = precheck(session.Welcome);
                if (blocked != null) return ExchangeOutcome.Fail<ExchangeValueHeader>(blocked);
                try
                {
                    await session.Connection.SendAsync(ExchangeMembers.ClipboardPutSealed(sealedSize, boxJson), ct).ConfigureAwait(false);
                    ExchangeFrame? ready = await session.Connection.ReceiveAsync(true, ct).ConfigureAwait(false);
                    if (ready == null) return ExchangeOutcome.Fail<ExchangeValueHeader>(ExchangeCodes.Unanswered);
                    if (ready.Type == "refused") return ExchangeOutcome.Fail<ExchangeValueHeader>(ReasonOf(ready));
                    if (ready.Type != "clipboard-ready") return ExchangeOutcome.Fail<ExchangeValueHeader>(ExchangeCodes.Unanswered);

                    await Task.Run(() => writeSealedBytes(session.Connection.Stream), ct).ConfigureAwait(false);
                    await session.Connection.Stream.FlushAsync(ct).ConfigureAwait(false);

                    ExchangeFrame? stored = await session.Connection.ReceiveAsync(true, ct).ConfigureAwait(false);
                    if (stored == null) return ExchangeOutcome.Fail<ExchangeValueHeader>(ExchangeCodes.Unanswered);
                    if (stored.Type == "refused") return ExchangeOutcome.Fail<ExchangeValueHeader>(ReasonOf(stored));
                    if (stored.Type != "clipboard-stored") return ExchangeOutcome.Fail<ExchangeValueHeader>(ExchangeCodes.Unanswered);
                    return ExchangeOutcome.Success(ExchangeValueHeader.From(stored));
                }
                catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
                {
                    return ExchangeOutcome.Fail<ExchangeValueHeader>(CodeOf(ex, session.Connection, ct, true));
                }
            }
        }

        /// <summary>
        /// <c>clipboard-get</c>. After the <c>clipboard-value</c> header exactly <c>size</c> raw bytes follow;
        /// <paramref name="consume"/> reads them (and only them) from the stream it is given and returns a code
        /// of failure or null. An empty clipboard is <c>Value == null</c>, not a failure.
        /// </summary>
        public async Task<ExchangeOutcome<ExchangeValueHeader?>> GetAsync(ExchangeAccountEntry entry, string? clipId,
            Func<ExchangeWelcome, string?> precheck, Func<ExchangeValueHeader, Stream, string?> consume, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok) return ExchangeOutcome.Fail<ExchangeValueHeader?>(opened.Code, opened.Trust);
            using (ExchangeSession session = opened.Value!)
            {
                string? blocked = precheck(session.Welcome);
                if (blocked != null) return ExchangeOutcome.Fail<ExchangeValueHeader?>(blocked);
                try
                {
                    await session.Connection.SendAsync(ExchangeMembers.ClipboardGet(clipId), ct).ConfigureAwait(false);
                    ExchangeFrame? answer = await session.Connection.ReceiveAsync(true, ct).ConfigureAwait(false);
                    if (answer == null) return ExchangeOutcome.Fail<ExchangeValueHeader?>(ExchangeCodes.Unanswered);
                    if (answer.Type == "refused") return ExchangeOutcome.Fail<ExchangeValueHeader?>(ReasonOf(answer));
                    if (answer.Type == "clipboard-empty") return ExchangeOutcome.Success<ExchangeValueHeader?>(null);
                    if (answer.Type != "clipboard-value") return ExchangeOutcome.Fail<ExchangeValueHeader?>(ExchangeCodes.Unanswered);

                    ExchangeValueHeader header = ExchangeValueHeader.From(answer);
                    if (header.Size < 1) return ExchangeOutcome.Fail<ExchangeValueHeader?>(ExchangeCodes.Unanswered);
                    string? failure = await Task.Run(() => consume(header, session.Connection.Stream), ct).ConfigureAwait(false);
                    if (failure != null) return ExchangeOutcome.Fail<ExchangeValueHeader?>(failure);
                    return ExchangeOutcome.Success<ExchangeValueHeader?>(header);
                }
                catch (Exception ex) when (IsNetworkFailure(ex) || ex is ExchangeFailure)
                {
                    return ExchangeOutcome.Fail<ExchangeValueHeader?>(CodeOf(ex, session.Connection, ct, true));
                }
            }
        }

        // ---- sign out on this device (items AB and AV) ----

        /// <summary>
        /// <c>revoke</c> with the entry's own <c>deviceId</c>, best effort: opened with <c>hello</c> if no stream is
        /// held, written, flushed, closed without <c>bye</c>. No answer exists (7.4); true means the write left.
        /// </summary>
        public async Task<bool> RevokeAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok) return false;
            using (ExchangeSession session = opened.Value!)
            {
                try
                {
                    await session.Connection.SendAsync(ExchangeMembers.Revoke(entry.DeviceId), ct).ConfigureAwait(false);
                    await session.Connection.Stream.FlushAsync(ct).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex) when (IsNetworkFailure(ex))
                {
                    return false;
                }
            }
        }

        // ---- helpers ----

        /// <summary>
        /// The answer to the envelope just sent, skipping a small number of unrelated frames (a keepalive, a pushed
        /// change) that may arrive first on a control stream; an unrelated frame is never an answer.
        /// </summary>
        private static async Task<ExchangeFrame?> ReceiveAnswerAsync(ExchangeSession session, CancellationToken ct, string expectedType)
        {
            for (int skipped = 0; skipped < 8; skipped++)
            {
                ExchangeFrame? frame = await session.Connection.ReceiveAsync(true, ct).ConfigureAwait(false);
                if (frame == null || frame.Type == expectedType || frame.Type == "refused") return frame;
            }
            return null;
        }

        private static string ReasonOf(ExchangeFrame refused)
        {
            string reason = refused.Str("reason");
            return ExchangeCodes.IsKnownWireReason(reason) ? reason : "refused";
        }

        private static bool IsNetworkFailure(Exception ex)
            => ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is AuthenticationException
               || ex is ExchangeProtocolException || ex is OperationCanceledException || ex is TimeoutException;

        /// <summary>
        /// The code of a failure after item AN: a stream that closed with no frame after our own envelope was
        /// written is "the server did not answer", never an unreachable host; a closed stream before the
        /// handshake finished is unreachable; our own token or the watchdog is cancelled or silent.
        /// </summary>
        internal static string CodeOf(Exception ex, ExchangeConnection? conn, CancellationToken ct, bool afterHandshake)
        {
            if (ct.IsCancellationRequested || (conn != null && conn.WasCancelled) || ex is OperationCanceledException && !(conn?.TimedOut ?? false))
                return ExchangeCodes.Cancelled;
            if (ex is ExchangeFailure f) return f.Code;
            if (conn != null && conn.TimedOut) return ExchangeCodes.Unanswered;
            if (ex is AuthenticationException) return ExchangeCodes.TlsFailed;
            return afterHandshake ? ExchangeCodes.Unanswered : ExchangeCodes.Unreachable;
        }

        private static string Clip(string text, int max)
            => text.Length <= max ? text : text.Substring(0, max);

        private static bool HasControl(string text)
        {
            foreach (char c in text)
                if (c < 0x20 || c == 0x7F) return true;
            return false;
        }

        private static long TicksOf(string rfc3339)
        {
            if (rfc3339.Length == 0) return 0;
            return DateTimeOffset.TryParse(rfc3339, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed.UtcTicks : 0;
        }
    }
}
