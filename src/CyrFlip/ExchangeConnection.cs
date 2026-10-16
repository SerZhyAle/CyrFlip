using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// One TLS stream to an exchange server, already past the trust rule of items D and AY. Two things the
    /// framework does not give a netfx <c>SslStream</c> are done here once: a read that is in flight cannot be
    /// cancelled by a token (so a cancel, and a stream that goes silent, <b>close the socket</b>, which makes the
    /// blocked call throw), and nothing times a silent peer out (so a watchdog does - the server itself drops a
    /// data stream that delivers no byte for 60 seconds, item BW).
    /// </summary>
    internal sealed class ExchangeConnection : IDisposable
    {
        /// <summary>No byte in either direction for this long ends the stream.</summary>
        public const int DefaultIdleMs = 60000;

        private readonly Action _closeTransport;
        private readonly Stream _inner;
        private readonly int _idleMs;
        private readonly Timer? _watchdog;
        private readonly CancellationTokenRegistration _cancelRegistration;
        private long _lastActivityTicks;
        private int _closed;

        /// <summary>The verdict of the trust rule for the certificate this stream was opened on.</summary>
        public ExchangeTrustResult Trust { get; }

        /// <summary>The stream every read and write goes through; it feeds the watchdog.</summary>
        public Stream Stream { get; }

        /// <summary>True once the watchdog or the caller's token closed the stream.</summary>
        public bool WasCancelled { get; private set; }
        public bool TimedOut { get; private set; }

        public ExchangeConnection(Stream inner, Action closeTransport, ExchangeTrustResult trust, CancellationToken ct, int idleMs = DefaultIdleMs)
        {
            _inner = inner;
            _closeTransport = closeTransport;
            _idleMs = idleMs;
            Trust = trust;
            Stream = new ActivityStream(inner, Touch);
            Touch();
            if (idleMs > 0)
                _watchdog = new Timer(_ => CheckIdle(), null, Math.Max(250, idleMs / 4), Math.Max(250, idleMs / 4));
            _cancelRegistration = ct.Register(() =>
            {
                WasCancelled = true;
                // Closing a socket from the registering thread can block on netfx: hand it to the pool.
                Task.Run(Close);
            });
        }

        private void Touch() => Interlocked.Exchange(ref _lastActivityTicks, Environment.TickCount);

        private void CheckIdle()
        {
            int quiet = unchecked(Environment.TickCount - (int)Interlocked.Read(ref _lastActivityTicks));
            if (quiet > _idleMs)
            {
                TimedOut = true;
                Close();
            }
        }

        public Task SendAsync(ExchangeMembers members, CancellationToken ct) => ExchangeWire.WriteFrameAsync(Stream, members, ct);

        /// <summary>The next envelope; <paramref name="authenticated"/> picks the frame limit of the stream's state.</summary>
        public Task<ExchangeFrame?> ReceiveAsync(bool authenticated, CancellationToken ct) => ExchangeWire.ReadFrameAsync(Stream, authenticated, ct);

        /// <summary>Closes the stream at once; every call blocked on it fails. Safe to call twice, from any thread.</summary>
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _closeTransport(); }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException) { }
        }

        public void Dispose()
        {
            _watchdog?.Dispose();
            _cancelRegistration.Dispose();
            Close();
        }

        /// <summary>Counts every completed read and write as activity.</summary>
        private sealed class ActivityStream : Stream
        {
            private readonly Stream _s;
            private readonly Action _touch;

            public ActivityStream(Stream s, Action touch) { _s = s; _touch = touch; }

            public override bool CanRead => _s.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => _s.CanWrite;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => _s.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _s.FlushAsync(cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _s.Read(buffer, offset, count);
                _touch();
                return n;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                int n = await _s.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                _touch();
                return n;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _s.Write(buffer, offset, count);
                _touch();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _s.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                _touch();
            }
        }
    }

    /// <summary>Opens the TLS streams of the client; the seam the tests replace with a scripted server.</summary>
    internal interface IExchangeTransport
    {
        /// <summary>
        /// Connects, runs the handshake and applies the trust rule. When <paramref name="decideTrustOnly"/> is
        /// set the stream is returned whatever the verdict (the verdict is in <see cref="ExchangeConnection.Trust"/>)
        /// so the caller can show a fingerprint; otherwise a verdict that does not accept ends in an
        /// <see cref="ExchangeFailure"/> before a byte is sent.
        /// </summary>
        Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct);
    }

    /// <summary>The real transport: a <see cref="TcpClient"/> and an <see cref="SslStream"/> of TLS 1.2 or later (item AX).</summary>
    internal sealed class TlsExchangeTransport : IExchangeTransport
    {
        public const int ConnectTimeoutMs = 15000;

        public async Task<ExchangeConnection> ConnectAsync(string host, int port, ExchangePin pin, bool decideTrustOnly, CancellationToken ct)
        {
            var tcp = new TcpClient();
            ExchangeTrustResult? trust = null;
            SslStream? ssl = null;
            var connectTimeout = new CancellationTokenSource(ConnectTimeoutMs);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectTimeout.Token))
            using (linked.Token.Register(() => Task.Run(() => { try { tcp.Close(); } catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { } })))
            {
                try
                {
                    await tcp.ConnectAsync(host, port).ConfigureAwait(false);
                    tcp.NoDelay = true;
                    ssl = new SslStream(tcp.GetStream(), false, (sender, certificate, chain, errors) =>
                    {
                        trust = ExchangeTrust.Evaluate(pin, certificate, errors);
                        return decideTrustOnly || trust.Accepts;
                    });
                    // SslProtocols.None is the operating system's choice (TLS 1.3 where it has it); the floor is
                    // checked below instead of forced, so a client never negotiates less than 1.2.
                    await ssl.AuthenticateAsClientAsync(host, null, SslProtocols.None, false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ssl?.Dispose();
                    tcp.Dispose();
                    connectTimeout.Dispose();
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    if (connectTimeout.IsCancellationRequested) throw new ExchangeFailure(ExchangeCodes.Unreachable);
                    if (trust != null && !trust.Accepts && !decideTrustOnly)
                        throw new ExchangeFailure(trust.Kind == ExchangeTrustKind.Changed ? ExchangeCodes.CertificateChanged : ExchangeCodes.CertificateUntrusted) { Trust = trust };
                    if (ex is AuthenticationException || (ex is IOException && ssl != null))
                        throw new ExchangeFailure(ExchangeCodes.TlsFailed);
                    if (ex is SocketException || ex is IOException || ex is ObjectDisposedException)
                        throw new ExchangeFailure(ExchangeCodes.Unreachable);
                    throw;
                }
            }
            connectTimeout.Dispose();

            if ((int)ssl!.SslProtocol < (int)SslProtocols.Tls12 && ssl.SslProtocol != SslProtocols.None)
            {
                ssl.Dispose();
                tcp.Dispose();
                throw new ExchangeFailure(ExchangeCodes.TlsFailed);
            }
            if (trust == null) trust = ExchangeTrust.Evaluate(pin, ssl.RemoteCertificate, SslPolicyErrors.RemoteCertificateNotAvailable);

            SslStream final = ssl;
            return new ExchangeConnection(final, () =>
            {
                try { final.Dispose(); } finally { tcp.Close(); }
            }, trust, ct);
        }
    }

    /// <summary>A failure with a stable code (see <see cref="ExchangeCodes"/>); the code is for the log and the message table.</summary>
    internal sealed class ExchangeFailure : Exception
    {
        public string Code { get; }

        /// <summary>The trust verdict when the failure is about the certificate: the dialog shows both fingerprints.</summary>
        public ExchangeTrustResult? Trust { get; set; }

        public ExchangeFailure(string code) : base(code) { Code = code; }
    }
}
