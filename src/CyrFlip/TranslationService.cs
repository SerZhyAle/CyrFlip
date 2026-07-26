using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// Where a translation ended up. Everything except <see cref="Ok"/> is shown to the user in the
    /// result window as one honest line plus a button (see CyrFlipContext.TranslationMessage).
    /// </summary>
    internal enum TranslationStatus
    {
        Ok,
        NoText,
        NotInstalled,
        NotRunning,
        StartFailed,
        NoModel,
        Timeout,
        Cancelled,
        /// <summary>The request itself failed - the server said no, not the model.</summary>
        ServerError,
        /// <summary>The model answered, but with nothing usable even after the retry.</summary>
        Failed,
    }

    /// <summary>The outcome of one translation, including what had to be trimmed to get there.</summary>
    internal sealed class TranslationResult
    {
        public TranslationStatus Status { get; set; } = TranslationStatus.Failed;
        public string Text { get; set; } = "";
        public string Model { get; set; } = "";
        /// <summary>What the server said, when it was the server that failed.</summary>
        public string Error { get; set; } = "";
        /// <summary>True when the source was longer than <see cref="TranslationService.MaxChars"/>.</summary>
        public bool Truncated { get; set; }
        public int SourceLength { get; set; }
    }

    /// <summary>
    /// Where the streamed text goes while the model is writing. Two callbacks rather than an
    /// <see cref="IProgress{T}"/> because a retry has to <see cref="Reset"/> what the first attempt
    /// already painted; the caller is responsible for marshalling to the UI thread.
    /// </summary>
    internal sealed class TranslationSink
    {
        private readonly Action<string> _chunk;
        private readonly Action _reset;

        public TranslationSink(Action<string> chunk, Action reset) { _chunk = chunk; _reset = reset; }

        public void Chunk(string text) { try { _chunk(text); } catch { } }
        public void Reset() { try { _reset(); } catch { } }
    }

    /// <summary>
    /// The feature logic of the translator: make sure a server and a model are there, build the
    /// prompt, stream one completion, and judge the answer. It owns no UI and no HTTP - the protocol
    /// is <see cref="OllamaClient"/>, the process is <see cref="OllamaManager"/>.
    ///
    /// Deliberately small judgement (spec §5.3): an empty answer or a verbatim echo of the source
    /// earns exactly one retry with a firmer prompt. The wrong-script / noisy / truncated heuristics
    /// FastMediaSorter needs for OCR batches would here mostly reject good short translations, and
    /// the user is looking straight at the result anyway.
    ///
    /// The two ways of failing are kept apart on purpose: <see cref="TranslationStatus.ServerError"/>
    /// (the request never got an answer - the server's own words are carried out in
    /// <see cref="TranslationResult.Error"/>) against <see cref="TranslationStatus.Failed"/> (the model
    /// answered, with nothing usable). Telling a user with a missing model that "the model could not
    /// handle this text" sends them looking in the wrong place.
    /// </summary>
    internal sealed class TranslationService
    {
        /// <summary>
        /// Longer selections are cut here and the window says so. Chunking a long text into several
        /// requests and stitching it back together is deliberately out of scope for v0.1.
        /// </summary>
        public const int MaxChars = 4000;

        private const int StartServerAttempts = 16;   // × 500 ms ≈ 8 s
        private const int StartServerDelayMs = 500;

        internal const string SystemPrompt =
            "You are a precise translation engine. Output text ONLY in the requested target language, " +
            "using that language's native script. Never switch to another language and never use Chinese " +
            "characters unless Chinese is explicitly the requested target.";

        private static readonly string[] PreferredModels = { "aya", "qwen", "gemma", "mistral", "llama", "phi" };

        /// <summary>
        /// What the model box offers when the server has nothing installed yet - small enough to be a
        /// realistic download, in rising order of appetite. The sizes are spelled out in the settings
        /// text rather than here, so the list stays a list of model names the API accepts verbatim.
        /// </summary>
        public static readonly string[] RecommendedModels = { "qwen2.5:3b", "gemma2:2b", "llama3.2:3b", "aya-expanse:8b" };

        private readonly AppConfig _config;
        private readonly Func<string, OllamaClient> _clientFactory;
        private readonly Func<bool> _isInstalled;
        private readonly Func<bool> _startServer;

        public TranslationService(AppConfig config)
            : this(config, endpoint => new OllamaClient(endpoint), OllamaManager.IsInstalled, OllamaManager.StartServer)
        {
        }

        internal TranslationService(AppConfig config, Func<string, OllamaClient> clientFactory,
            Func<bool> isInstalled, Func<bool> startServer)
        {
            _config = config;
            _clientFactory = clientFactory;
            _isInstalled = isInstalled;
            _startServer = startServer;
        }

        /// <summary>The model actually used last, which may differ from the configured one.</summary>
        public string LastModel { get; private set; } = "";

        public OllamaClient CreateClient() => _clientFactory(_config.TranslateEndpoint);

        /// <summary>
        /// Translate one selection into <paramref name="targetCode"/>, streaming into
        /// <paramref name="sink"/> as the model writes.
        /// </summary>
        public async Task<TranslationResult> TranslateAsync(string text, string targetCode,
            TranslationSink? sink, CancellationToken ct)
        {
            var result = new TranslationResult { SourceLength = (text ?? "").Length };
            string source = Truncate(text ?? "", MaxChars, out bool truncated).Trim();
            result.Truncated = truncated;
            if (source.Length == 0)
            {
                result.Status = TranslationStatus.NoText;
                return result;
            }

            OllamaClient client = CreateClient();
            TranslationStatus ready = await EnsureServerAsync(client, ct).ConfigureAwait(false);
            if (ready != TranslationStatus.Ok)
            {
                result.Status = ready;
                return result;
            }

            string model = await ResolveModelAsync(client, ct).ConfigureAwait(false);
            if (model.Length == 0)
            {
                result.Status = TranslationStatus.NoModel;
                return result;
            }
            LastModel = model;
            result.Model = model;

            string language = TranslationLanguages.EnglishName(targetCode);

            string? answer;
            try
            {
                answer = await client.GenerateAsync(model, SystemPrompt, BuildPrompt(language, source),
                    _config.TranslateKeepAliveMinutes, chunk => sink?.Chunk(chunk), TimeoutMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result.Status = ct.IsCancellationRequested ? TranslationStatus.Cancelled : TranslationStatus.Timeout;
                return result;
            }

            if (answer == null)
            {
                // The request failed, which is not the same as "the model produced nothing".
                result.Status = TranslationStatus.ServerError;
                result.Error = client.LastError;
                return result;
            }

            answer = answer.Trim();
            if (NeedsRetry(source, answer))
            {
                sink?.Reset();
                string? second;
                try
                {
                    second = await client.GenerateAsync(model, SystemPrompt, BuildRetryPrompt(language, source),
                        _config.TranslateKeepAliveMinutes, chunk => sink?.Chunk(chunk), TimeoutMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Status = ct.IsCancellationRequested ? TranslationStatus.Cancelled : TranslationStatus.Timeout;
                    return result;
                }

                second = (second ?? "").Trim();
                // Prefer a usable retry; otherwise keep whatever the first attempt produced, since a
                // slightly odd translation still beats an empty window.
                if (second.Length > 0 && !NeedsRetry(source, second)) answer = second;
                else if (answer.Length == 0) answer = second;
            }

            if (answer.Length == 0)
            {
                result.Status = TranslationStatus.Failed;
                return result;
            }

            result.Text = answer;
            result.Status = TranslationStatus.Ok;
            return result;
        }

        /// <summary>
        /// Probe the server, and start it once if it is installed and the user allowed it. Also the
        /// settings tab's "check the connection" path.
        /// </summary>
        public async Task<TranslationStatus> EnsureServerAsync(OllamaClient client, CancellationToken ct)
        {
            if (await client.ProbeAsync(ct).ConfigureAwait(false)) return TranslationStatus.Ok;
            if (!_isInstalled()) return TranslationStatus.NotInstalled;
            if (!_config.TranslateAutoStartServer) return TranslationStatus.NotRunning;
            if (!_startServer()) return TranslationStatus.StartFailed;

            for (int i = 0; i < StartServerAttempts; i++)
            {
                await Task.Delay(StartServerDelayMs, ct).ConfigureAwait(false);
                if (await client.ProbeAsync(ct).ConfigureAwait(false)) return TranslationStatus.Ok;
            }
            return TranslationStatus.StartFailed;
        }

        /// <summary>
        /// The configured model when it is installed, else the best installed one. An empty answer
        /// means the server has no models at all and the configured name is blank too.
        /// </summary>
        public async Task<string> ResolveModelAsync(OllamaClient client, CancellationToken ct)
        {
            string wanted = (_config.TranslateModel ?? "").Trim();
            List<string>? installed = await client.ListModelsAsync(ct).ConfigureAwait(false);
            // The list could not be read at all: trust an explicit name rather than refuse to try.
            if (installed == null) return wanted;
            // The server answered and has nothing installed - saying "no model" beats sending a
            // request that can only fail, whatever the settings name.
            if (installed.Count == 0) return "";

            if (wanted.Length > 0)
                foreach (string name in installed)
                    if (ModelMatches(name, wanted)) return name;

            return PickPreferredModel(installed);
        }

        private int TimeoutMs => Math.Max(5, _config.TranslateTimeoutSeconds) * 1000;

        // ---- pure helpers (unit-tested) --------------------------------------------------

        internal static string Truncate(string text, int max, out bool truncated)
        {
            truncated = text.Length > max;
            return truncated ? text.Substring(0, max) : text;
        }

        internal static string BuildPrompt(string languageName, string text)
            => "Translate the FULL following text into " + languageName + "." + "\n" +
               "Translate every sentence and detail faithfully; do not summarize, shorten, or omit anything." + "\n" +
               "Write the translation ENTIRELY in " + languageName + " using its native alphabet; do NOT use " +
               "Chinese characters or any other language. Output only the translation, no notes and no quotes." + "\n" +
               "Text: " + text;

        internal static string BuildRetryPrompt(string languageName, string text)
            => "Translate this ENTIRE text into " + languageName + " ONLY. Translate every sentence and detail; " +
               "do not summarize, shorten, or omit anything. The output must be written entirely in " +
               languageName + "'s native alphabet. Produce a genuine translation, not a copy. " +
               "Output only the translation." + "\n" +
               "Text: " + text;

        /// <summary>An empty answer, or the source handed straight back, is worth one more try.</summary>
        internal static bool NeedsRetry(string source, string candidate)
            => candidate.Trim().Length == 0 || LooksLikeEcho(source, candidate);

        internal static bool LooksLikeEcho(string source, string candidate)
        {
            string a = (source ?? "").Trim();
            string b = (candidate ?? "").Trim();
            // Short strings legitimately survive translation unchanged ("OK", "CyrFlip", "2026").
            if (a.Length < 3) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Models that tend to translate well, in order; otherwise the first installed one.</summary>
        internal static string PickPreferredModel(IList<string> installed)
        {
            if (installed == null || installed.Count == 0) return "";
            foreach (string preferred in PreferredModels)
                foreach (string name in installed)
                    if (name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0)
                        return name;
            return installed[0];
        }

        /// <summary>"qwen2.5" matches an installed "qwen2.5:latest", and the other way round.</summary>
        internal static bool ModelMatches(string installedName, string wanted)
        {
            if (string.Equals(installedName, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            if (wanted.IndexOf(':') < 0 && installedName.StartsWith(wanted + ":", StringComparison.OrdinalIgnoreCase)) return true;
            if (wanted.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
                && string.Equals(installedName, wanted.Substring(0, wanted.Length - ":latest".Length), StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }
    }
}
