using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The HTTP boundary of the translator, kept behind an interface so every parser above it is
    /// unit-testable without a server: the tests hand <see cref="OllamaClient"/> a transport that
    /// replays canned NDJSON instead of talking to localhost.
    /// </summary>
    internal interface IOllamaTransport
    {
        /// <summary>GET a body, or null on any failure (unreachable, non-2xx, malformed).</summary>
        Task<string?> GetStringAsync(string url, int timeoutMs, CancellationToken ct);

        /// <summary>
        /// POST <paramref name="json"/> and feed every non-empty response line to
        /// <paramref name="onLine"/> as it arrives (Ollama streams NDJSON). False when the request
        /// itself failed; a cancelled request throws <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <param name="timeoutMs">
        /// Budget for the <b>first</b> line. On a cold model this is almost entirely loading time -
        /// minutes on a CPU-only machine - and has nothing to do with how hard the text is.
        /// </param>
        /// <param name="idleTimeoutMs">
        /// Once the first line proves the model is loaded and writing: how long it may go quiet
        /// before the stream is given up, re-armed on <b>every</b> line (ticket S0010 TD-2). A slow
        /// machine that keeps writing is never cut off. 0 keeps the single first-line budget.
        /// </param>
        /// <param name="maxAnswerMs">
        /// The ceiling for the whole answer, counted from the first line - only a runaway model that
        /// never stops writing reaches it. 0 = none.
        /// </param>
        Task<bool> PostLinesAsync(string url, string json, Action<string> onLine,
            int timeoutMs, int idleTimeoutMs, int maxAnswerMs, CancellationToken ct);
    }

    /// <summary>The real transport: one shared <see cref="HttpClient"/>, per-request timeouts.</summary>
    internal sealed class HttpOllamaTransport : IOllamaTransport
    {
        /// <summary>
        /// One client for the whole app: a new HttpClient per request leaks sockets in TIME_WAIT,
        /// which on a chatty feature shows up as "the translator stopped working after a while".
        /// The timeout lives on the per-request token instead, so a long stream is not cut short.
        /// </summary>
        private static readonly HttpClient Client = CreateClient();

        public static readonly HttpOllamaTransport Instance = new HttpOllamaTransport();

        private static HttpClient CreateClient()
        {
            TlsPolicy.EnsureTls12();
            return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<string?> GetStringAsync(string url, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var linked = Linked(ct, timeoutMs);
                using HttpResponseMessage response = await Client.GetAsync(url, linked.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return null; }
        }

        public async Task<bool> PostLinesAsync(string url, string json, Action<string> onLine,
            int timeoutMs, int idleTimeoutMs, int maxAnswerMs, CancellationToken ct)
        {
            using var linked = Linked(ct, timeoutMs);
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                using HttpResponseMessage response = await Client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // Ollama puts its most useful failures in the body of a 4xx/5xx - "model requires
                    // more system memory (5.5 GiB) than is available" is a 500 - so hand that line to
                    // the caller before giving up, or the user is told to check their endpoint.
                    try
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (body.Length > 0) onLine(body);
                    }
                    catch { /* no body, or unreadable - the failure itself is still reported */ }
                    return false;
                }

                using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                // ReadLineAsync takes no token on net48, so cancellation has to reach it by closing
                // the stream underneath - otherwise Esc would only be honoured after the model
                // finished the sentence it was writing. Registered after the stream so it is
                // disposed first (using-declarations unwind in reverse).
                //
                // The dispose is queued, never run inline (ticket S0010 TD-3): Cancel() runs this
                // callback on the thread that cancelled - the UI thread, for Esc, closing the popup
                // or a newer chord - and closing a half-read chunked response on .NET Framework can
                // drain the rest of it, i.e. wait for the model to finish, while the keyboard hook
                // that shares the UI thread waits too.
                using CancellationTokenRegistration abort = linked.Token.Register(() =>
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { stream.Dispose(); } catch { }
                    }));

                return await PumpAsync(_ => reader.ReadLineAsync(), onLine, linked, idleTimeoutMs, maxAnswerMs)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }

        /// <summary>
        /// The read loop, apart from HTTP so its clock is testable (S0010 TD-2). Before the first
        /// line the budget is whatever <paramref name="linked"/> was armed with (the model load);
        /// from the first line on, every line re-arms it to <paramref name="idleTimeoutMs"/>, so only
        /// a model that goes <b>quiet</b> times out - a slow one that keeps writing does not. A
        /// timeout surfaces as <see cref="OperationCanceledException"/> after the lines already
        /// handed to <paramref name="onLine"/>, which is what lets the caller keep them.
        /// </summary>
        /// <param name="readLine">
        /// Reads one line, null at the end. It is handed the linked token; the real reader cannot
        /// take one on net48 and is cancelled by closing its stream instead.
        /// </param>
        internal static async Task<bool> PumpAsync(Func<CancellationToken, Task<string?>> readLine,
            Action<string> onLine, CancellationTokenSource linked, int idleTimeoutMs, int maxAnswerMs)
        {
            System.Diagnostics.Stopwatch? answering = null;
            while (true)
            {
                string? line;
                try { line = await readLine(linked.Token).ConfigureAwait(false); }
                catch when (linked.IsCancellationRequested) { break; }
                if (line == null) break;
                linked.Token.ThrowIfCancellationRequested();
                if (line.Length == 0) continue;
                if (answering == null)
                {
                    // The model has answered, so it is loaded: from here the clock is about whether
                    // it is still writing, not about how long a cold model takes to come up.
                    answering = System.Diagnostics.Stopwatch.StartNew();
                }
                else if (maxAnswerMs > 0 && answering.ElapsedMilliseconds > maxAnswerMs)
                {
                    // Only a model stuck repeating itself gets here; a quiet one is the idle timer's.
                    linked.Cancel();
                    linked.Token.ThrowIfCancellationRequested();
                }
                // CancelAfter re-arms the existing timer.
                if (idleTimeoutMs > 0) linked.CancelAfter(idleTimeoutMs);
                onLine(line);
            }
            linked.Token.ThrowIfCancellationRequested();
            return true;
        }

        private static CancellationTokenSource Linked(CancellationToken ct, int timeoutMs)
        {
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutMs > 0) linked.CancelAfter(timeoutMs);
            return linked;
        }
    }

    /// <summary>
    /// Talks to a local Ollama server (http://localhost:11434 by default): availability, installed
    /// models, model download, and one streaming completion. It knows the protocol, not the feature -
    /// prompts, retries and language choice live in <see cref="TranslationService"/>.
    /// </summary>
    internal sealed class OllamaClient
    {
        public const string DefaultEndpoint = "http://localhost:11434";
        private const int ProbeTimeoutMs = 2500;
        private const int TagsTimeoutMs = 4000;

        private readonly IOllamaTransport _transport;

        public OllamaClient(string? endpoint)
            : this(endpoint, HttpOllamaTransport.Instance)
        {
        }

        internal OllamaClient(string? endpoint, IOllamaTransport transport)
        {
            BaseUrl = NormalizeBase(endpoint);
            _transport = transport;
            RaiseConnectionLimit(BaseUrl);
        }

        /// <summary>Connections .NET Framework allows to the Ollama endpoint at once.</summary>
        internal const int ConnectionLimit = 4;

        /// <summary>
        /// .NET Framework allows two connections per host by default (ticket S0010 TD-3). A cancelled
        /// stream keeps its connection busy while Ollama goes on generating, so the next probe could
        /// wait behind it, time out, and be reported as "Ollama is not running".
        /// </summary>
        private static void RaiseConnectionLimit(string baseUrl)
        {
            try
            {
                ServicePoint point = ServicePointManager.FindServicePoint(new Uri(baseUrl));
                if (point.ConnectionLimit < ConnectionLimit) point.ConnectionLimit = ConnectionLimit;
            }
            catch { /* an address we cannot parse fails with its own message on the first request */ }
        }

        /// <summary>
        /// True when <paramref name="baseUrl"/> names this machine - the only server CyrFlip may
        /// start by itself (ticket S0010 TD-6). A remote endpoint that does not answer is reported
        /// as such; starting the <i>local</i> Ollama for it only cost eight seconds and a misleading
        /// "not installed".
        /// </summary>
        internal static bool IsLoopback(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) || uri == null) return false;
            if (uri.IsLoopback) return true;
            return IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        /// <summary>The host (and a non-default port) of <paramref name="baseUrl"/>, for a message.</summary>
        internal static string HostOf(string baseUrl)
            => Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) && uri != null ? uri.Authority : baseUrl;

        public string BaseUrl { get; }

        /// <summary>
        /// What the server said when the last request failed (the NDJSON <c>error</c> line), so the
        /// popup can show the real reason instead of blaming the model.
        /// </summary>
        public string LastError { get; private set; } = "";

        /// <summary>
        /// Accepts what a user actually types: an empty box, a trailing slash, or the /api root.
        /// </summary>
        internal static string NormalizeBase(string? endpoint)
        {
            string value = (endpoint ?? "").Trim();
            if (value.Length == 0) return DefaultEndpoint;
            while (value.EndsWith("/", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 1);
            if (value.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
            if (value.Length == 0) return DefaultEndpoint;
            // "127.0.0.1:11434" and "localhost:11434" are Ollama's own OLLAMA_HOST shape. Without a
            // scheme, Uri reads the host as one, every request failed, and the local default server
            // was reported as an unreachable remote one and never auto-started (S0037 TR-2).
            if (value.IndexOf("://", StringComparison.Ordinal) < 0) value = "http://" + value;
            return value;
        }

        public async Task<bool> ProbeAsync(CancellationToken ct)
            => await _transport.GetStringAsync(BaseUrl + "/api/tags", ProbeTimeoutMs, ct).ConfigureAwait(false) != null;

        /// <summary>
        /// The installed models, or <b>null</b> when the list could not be read at all. The difference
        /// matters: an empty list is "this server has no models" (the user must download one), while
        /// null is "the server didn't answer" - and answering those two the same way told a user with a
        /// model-less server that their configured model was fine.
        /// </summary>
        public async Task<List<string>?> ListModelsAsync(CancellationToken ct)
        {
            string? body = await _transport.GetStringAsync(BaseUrl + "/api/tags", TagsTimeoutMs, ct).ConfigureAwait(false);
            return body == null ? null : ParseModelNames(body);
        }

        /// <summary>
        /// Pulls a model, reporting the server's own status line plus a percentage. No timeout: a
        /// multi-gigabyte pull legitimately takes minutes, and the caller can cancel.
        /// </summary>
        public async Task<bool> PullModelAsync(string model, IProgress<string>? progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            string body = Json(new Dictionary<string, object>
            {
                { "name", model.Trim() },
                { "stream", true },
            });

            bool success = false;
            bool failed = false;
            bool sent = await _transport.PostLinesAsync(BaseUrl + "/api/pull", body, line =>
            {
                string text = ParsePullProgress(line, out bool done, out string? error);
                if (error != null) { failed = true; progress?.Report(error); return; }
                if (done) success = true;
                if (text.Length > 0) progress?.Report(text);
            }, 0, 0, 0, ct).ConfigureAwait(false);

            return sent && success && !failed;
        }

        /// <summary>
        /// One streaming completion. Every chunk is handed to <paramref name="partial"/> as it
        /// arrives (so the window fills in live) and the whole text is returned at the end;
        /// null means the request itself failed (server down, model missing, HTTP error).
        ///
        /// <paramref name="partial"/> is a plain callback, not an <see cref="IProgress{T}"/>, on
        /// purpose: it is raised on whatever thread the HTTP read completed on, and marshalling is
        /// the caller's business - an IProgress created down here would capture the wrong context.
        /// </summary>
        /// <param name="keepAliveMinutes">
        /// How long Ollama keeps the model in RAM afterwards. <b>Negative means forever</b>, which is
        /// Ollama's own <c>-1</c> and the default here: the first call after a cold start costs
        /// minutes on a CPU-only machine, and paying that again every few minutes is the single
        /// biggest thing that makes the feature feel broken.
        /// </param>
        /// <remarks>
        /// A deadline that fires <b>after</b> the model has written something does not throw: the
        /// text so far is returned and <see cref="LastStoppedEarly"/> says it is incomplete (ticket
        /// S0010 TD-2) - on a slow machine that text is minutes of work the user has already watched
        /// appear. A deadline before the first chunk, and every cancellation by
        /// <paramref name="ct"/>, still throws <see cref="OperationCanceledException"/>.
        /// </remarks>
        public async Task<string?> GenerateAsync(string model, string system, string prompt,
            int keepAliveMinutes, Action<string>? partial, int timeoutMs, int idleTimeoutMs,
            int maxAnswerMs, CancellationToken ct)
        {
            string body = Json(new Dictionary<string, object>
            {
                { "model", model },
                { "system", system },
                { "prompt", prompt },
                { "stream", true },
                { "keep_alive", KeepAliveValue(keepAliveMinutes) },
                { "options", new Dictionary<string, object> { { "temperature", 0 } } },
            });

            LastError = "";
            LastStoppedEarly = false;
            var text = new StringBuilder();
            bool serverError = false;
            bool ok;
            try
            {
                ok = await _transport.PostLinesAsync(BaseUrl + "/api/generate", body, line =>
                {
                    string chunk = ParseGenerateChunk(line, out bool _, out string? error);
                    if (error != null) { serverError = true; LastError = error; return; }
                    if (chunk.Length == 0) return;
                    text.Append(chunk);
                    partial?.Invoke(chunk);
                }, timeoutMs, idleTimeoutMs, maxAnswerMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && text.Length > 0 && !serverError)
            {
                LastStoppedEarly = true;
                return text.ToString();
            }

            if (!ok || serverError) return null;
            return text.ToString();
        }

        /// <summary>
        /// True when the last <see cref="GenerateAsync"/> returned text that a deadline cut short -
        /// the model stopped answering, or wrote past the ceiling.
        /// </summary>
        public bool LastStoppedEarly { get; private set; }

        /// <summary>
        /// What goes into <c>keep_alive</c>: the number -1 for "keep it loaded until Ollama exits",
        /// otherwise a duration string. A <b>number</b> and not "-1m" - Ollama reads the negative
        /// number as forever, and a negative duration string as an error.
        /// </summary>
        internal static object KeepAliveValue(int keepAliveMinutes)
            => keepAliveMinutes < 0 ? (object)(-1) : keepAliveMinutes + "m";

        // ---- parsers (pure, unit-tested) ------------------------------------------------

        /// <summary>Names of the models in a /api/tags body; anything unexpected yields an empty list.</summary>
        internal static List<string> ParseModelNames(string? json)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(json)) return names;
            try
            {
                if (!(Serializer().DeserializeObject(json) is Dictionary<string, object> root)) return names;
                if (!root.TryGetValue("models", out object? models) || !(models is object[] items)) return names;
                foreach (object item in items)
                {
                    if (!(item is Dictionary<string, object> map)) continue;
                    if (!map.TryGetValue("name", out object? name)) continue;
                    string text = Convert.ToString(name) ?? "";
                    if (text.Length > 0) names.Add(text);
                }
            }
            catch { /* a body we don't understand is the same as no models */ }
            return names;
        }

        /// <summary>One NDJSON line of /api/generate: the text chunk, plus the done/error flags.</summary>
        internal static string ParseGenerateChunk(string line, out bool done, out string? error)
        {
            done = false;
            error = null;
            try
            {
                if (!(Serializer().DeserializeObject(line) is Dictionary<string, object> root)) return "";
                if (root.TryGetValue("error", out object? err))
                {
                    error = Convert.ToString(err) ?? "error";
                    return "";
                }
                if (root.TryGetValue("done", out object? isDone)) done = ToBool(isDone);
                if (root.TryGetValue("response", out object? response)) return Convert.ToString(response) ?? "";
                return "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// One NDJSON line of /api/pull as a display string ("downloading 42%"), plus the
        /// success/error flags. The status text is the server's own English wording.
        /// </summary>
        internal static string ParsePullProgress(string line, out bool success, out string? error)
        {
            success = false;
            error = null;
            try
            {
                if (!(Serializer().DeserializeObject(line) is Dictionary<string, object> root)) return "";
                if (root.TryGetValue("error", out object? err))
                {
                    error = Convert.ToString(err) ?? "error";
                    return "";
                }

                string status = root.TryGetValue("status", out object? state) ? Convert.ToString(state) ?? "" : "";
                if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) success = true;

                if (root.TryGetValue("completed", out object? completed) && root.TryGetValue("total", out object? total))
                {
                    double totalBytes = ToDouble(total);
                    if (totalBytes > 0)
                        return (status + " " + (int)(ToDouble(completed) / totalBytes * 100) + "%").Trim();
                }
                return status;
            }
            catch { return ""; }
        }

        private static bool ToBool(object? value)
        {
            try { return value != null && Convert.ToBoolean(value); }
            catch { return false; }
        }

        private static double ToDouble(object? value)
        {
            try { return value == null ? 0 : Convert.ToDouble(value); }
            catch { return 0; }
        }

        private static string Json(object value) => Serializer().Serialize(value);

        private static JavaScriptSerializer Serializer()
            => new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    }
}
