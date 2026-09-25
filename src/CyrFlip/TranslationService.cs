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
        /// <summary>A server on another machine that does not answer - never "start" it (S0010 TD-6).</summary>
        Unreachable,
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
        /// <summary>
        /// The model stopped answering - or wrote past the ceiling - after writing <see cref="Text"/>:
        /// shown with a note rather than thrown away (S0010 TD-2), and never pasted automatically.
        /// </summary>
        public bool Partial { get; set; }
        /// <summary>The server's host, for the <see cref="TranslationStatus.Unreachable"/> message.</summary>
        public string Server { get; set; } = "";
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
    /// earns exactly one retry with a firmer prompt - unless the echo is right, because the text
    /// already is in the target language (S0010 TD-7, <see cref="IsAcceptedEcho"/>). The wrong-script / noisy / truncated heuristics
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
        /// <summary>
        /// Offered in the model dropdown, best first. Trimmed on 2026-07-28 after measuring every
        /// candidate on English ⇄ Russian ⇄ Ukrainian against a live Ollama: <c>qwen2.5:3b</c> and
        /// <c>llama3.2:3b</c> were dropped outright (the first emitted Chinese characters inside a
        /// Russian translation, the second does not claim Russian or Ukrainian at all), and
        /// <c>gemma2:2b</c> is kept only as the small-machine compromise it is - the UI text says so.
        /// </summary>
        public static readonly string[] RecommendedModels = { "aya-expanse:8b", "gemma2:9b", "gemma2:2b" };

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
        public Task<TranslationResult> TranslateAsync(string text, string targetCode,
            TranslationSink? sink, CancellationToken ct)
            => TranslateAsync(text, targetCode, null, sink, ct);

        /// <param name="sourceCode">
        /// The language the text is expected to be in (the fixed-pair rows), or null to let the model
        /// work it out - which is what every auto-detecting row does.
        /// </param>
        public async Task<TranslationResult> TranslateAsync(string text, string targetCode,
            string? sourceCode, TranslationSink? sink, CancellationToken ct)
        {
            var result = new TranslationResult { SourceLength = (text ?? "").Length };
            try
            {
                return await TranslateCoreAsync(text ?? "", targetCode, sourceCode, sink, result, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A cancel while the server is probed, started or asked for its models (S0010 TD-6)
                // is the user's Esc like any other - not a failure to log and report.
                result.Status = ct.IsCancellationRequested ? TranslationStatus.Cancelled : TranslationStatus.Timeout;
                result.Text = "";
                return result;
            }
        }

        private async Task<TranslationResult> TranslateCoreAsync(string text, string targetCode,
            string? sourceCode, TranslationSink? sink, TranslationResult result, CancellationToken ct)
        {
            string source = Truncate(text, MaxChars, out bool truncated).Trim();
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
                if (ready == TranslationStatus.Unreachable) result.Server = OllamaClient.HostOf(client.BaseUrl);
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
            string? sourceLanguage = string.IsNullOrEmpty(sourceCode) || string.Equals(sourceCode, targetCode, StringComparison.OrdinalIgnoreCase)
                ? null
                : TranslationLanguages.EnglishName(sourceCode);

            string? answer;
            try
            {
                answer = await client.GenerateAsync(model, SystemPrompt, BuildPrompt(language, source, sourceLanguage),
                    _config.TranslateKeepAliveMinutes, chunk => sink?.Chunk(chunk),
                    TimeoutMs, IdleTimeoutMs, MaxAnswerMs, ct).ConfigureAwait(false);
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
            bool partial = client.LastStoppedEarly;
            // A cut-short answer is not retried: the deadline that cut it would only cut the retry too.
            if (!partial && NeedsRetry(source, answer) && !IsAcceptedEcho(source, answer, targetCode))
            {
                sink?.Reset();
                string? second;
                try
                {
                    second = await client.GenerateAsync(model, SystemPrompt, BuildRetryPrompt(language, source, sourceLanguage),
                        _config.TranslateKeepAliveMinutes, chunk => sink?.Chunk(chunk),
                        TimeoutMs, IdleTimeoutMs, MaxAnswerMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Status = ct.IsCancellationRequested ? TranslationStatus.Cancelled : TranslationStatus.Timeout;
                    return result;
                }

                second = (second ?? "").Trim();
                // Prefer a usable retry; otherwise keep whatever the first attempt produced, since a
                // slightly odd translation still beats an empty window.
                if (IsUsableRetry(source, second, targetCode)) { answer = second; partial = client.LastStoppedEarly; }
                else if (answer.Length == 0) { answer = second; partial = client.LastStoppedEarly; }
            }

            if (answer.Length == 0)
            {
                result.Status = TranslationStatus.Failed;
                return result;
            }

            result.Text = answer;
            result.Partial = partial;
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
            // Only this machine's server is ours to start (S0010 TD-6). Starting the local Ollama for
            // a remote address that does not answer cost eight seconds and then blamed a local install.
            if (!OllamaClient.IsLoopback(client.BaseUrl)) return TranslationStatus.Unreachable;
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

        /// <summary>
        /// Budget for the <b>first</b> token, i.e. for loading the model. Cold-loading a 7B model on
        /// a machine with no usable GPU is minutes, and that has nothing to do with the selection.
        /// </summary>
        private int TimeoutMs => Math.Max(5, _config.TranslateTimeoutSeconds) * 1000;

        /// <summary>
        /// Once the model is writing, how long it may go quiet (ticket S0010 TD-2, open decision 2).
        /// This replaced a total "15 s + 1 s per 40 characters" cap that a CPU-only machine - a few
        /// tokens a second - overran on every long selection, mid-stream, throwing the text away.
        /// A slow model that keeps writing is now never cut off.
        /// </summary>
        internal const int IdleTimeoutMs = 30 * 1000;

        /// <summary>
        /// The ceiling for one answer, from its first token: only a model stuck repeating itself
        /// reaches it, and what it wrote is kept as a partial answer like any other deadline.
        /// </summary>
        internal const int MaxAnswerMs = 10 * 60 * 1000;

        // ---- pure helpers (unit-tested) --------------------------------------------------

        internal static string Truncate(string text, int max, out bool truncated)
        {
            truncated = text.Length > max;
            return truncated ? text.Substring(0, max) : text;
        }

        /// <summary>
        /// The source-language line for a fixed-pair row. Phrased as an <b>expectation</b>: pressing
        /// the wrong half of the pair must degrade to a correct translation, not to a model dutifully
        /// translating out of a language the text is not in.
        /// </summary>
        internal static string SourceHintLine(string? sourceLanguageName)
            => string.IsNullOrEmpty(sourceLanguageName) ? "" :
               "The text is expected to be in " + sourceLanguageName +
               "; if it plainly is not, translate from whatever language it is actually in.\n";

        internal static string BuildPrompt(string languageName, string text, string? sourceLanguageName = null)
            => SourceHintLine(sourceLanguageName) +
               "Translate the FULL following text into " + languageName + "." + "\n" +
               "Translate every sentence and detail faithfully; do not summarize, shorten, or omit anything." + "\n" +
               "Write the translation ENTIRELY in " + languageName + " using its native alphabet; do NOT use " +
               "Chinese characters or any other language. Output only the translation, no notes and no quotes." + "\n" +
               "Text: " + text;

        internal static string BuildRetryPrompt(string languageName, string text, string? sourceLanguageName = null)
            => SourceHintLine(sourceLanguageName) +
               "Translate this ENTIRE text into " + languageName + " ONLY. Translate every sentence and detail; " +
               "do not summarize, shorten, or omit anything. The output must be written entirely in " +
               languageName + "'s native alphabet. Produce a genuine translation, not a copy. " +
               "Output only the translation." + "\n" +
               "Text: " + text;

        /// <summary>An empty answer, or the source handed straight back, is worth one more try.</summary>
        internal static bool NeedsRetry(string source, string candidate)
            => candidate.Trim().Length == 0 || LooksLikeEcho(source, candidate);

        // ---- the echo rule (ticket S0010 TD-7) ----
        //
        // An echo is the right answer when the text already is in the target language - the "ui" row
        // on Russian text in a Russian UI. The retry prompt demands "not a copy", so retrying there
        // replaced a correct answer with a paraphrase or another language. Whether source and target
        // are the same language is judged by script, the one thing that can be read off the text
        // without a model: an echo is accepted when the source is written in the target's script and
        // nothing in it contradicts the target language.

        /// <summary>A retry answer at least this similar to the source is an echo too.</summary>
        internal const double NearEchoSimilarity = 0.95;

        /// <summary>The first answer is an echo, and one that is right: no retry.</summary>
        internal static bool IsAcceptedEcho(string source, string answer, string targetCode)
        {
            if (answer.Trim().Length == 0 || !LooksLikeEcho(source, answer)) return false;
            TextScript? target = ScriptOfLanguage(targetCode);
            if (target == null || DominantScript(source) != target) return false;
            // Cyrillic and Latin are shared by many languages: the answer must be the source itself,
            // and no letter in it may belong only to another language of that script.
            if ((target == TextScript.Cyrillic || target == TextScript.Latin)
                && Similarity(source, answer) < NearEchoSimilarity)
                return false;
            return !ContradictsLanguage(source, targetCode);
        }

        /// <summary>
        /// The retry's answer replaces the first only when it is a real translation: not empty, not a
        /// near-echo, and - when the target's script is known - written in that script.
        /// </summary>
        internal static bool IsUsableRetry(string source, string retry, string targetCode)
        {
            if (retry.Trim().Length == 0) return false;
            if (LooksLikeEcho(source, retry) || Similarity(source, retry) >= NearEchoSimilarity) return false;
            TextScript? target = ScriptOfLanguage(targetCode);
            return target == null || DominantScript(retry) == target;
        }

        internal enum TextScript { Latin, Cyrillic, Greek, Arabic, Hebrew, Devanagari, Bengali, Han, Hangul, Thai, Other }

        /// <summary>The script with the most letters in <paramref name="text"/>; null when it has none.</summary>
        internal static TextScript? DominantScript(string text)
        {
            var counts = new int[(int)TextScript.Other + 1];
            bool any = false;
            foreach (char c in text ?? "")
            {
                if (!char.IsLetter(c)) continue;
                counts[(int)ScriptOf(c)]++;
                any = true;
            }
            if (!any) return null;
            int best = 0;
            for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
            return (TextScript)best;
        }

        private static TextScript ScriptOf(char c)
        {
            if (c < 0x0250 || (c >= 0x1E00 && c <= 0x1EFF)) return TextScript.Latin;
            if (c >= 0x0370 && c <= 0x03FF) return TextScript.Greek;
            if (c >= 0x0400 && c <= 0x052F) return TextScript.Cyrillic;
            if (c >= 0x0590 && c <= 0x05FF) return TextScript.Hebrew;
            if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) || (c >= 0xFB50 && c <= 0xFEFF)) return TextScript.Arabic;
            if (c >= 0x0900 && c <= 0x097F) return TextScript.Devanagari;
            if (c >= 0x0980 && c <= 0x09FF) return TextScript.Bengali;
            if (c >= 0x0E00 && c <= 0x0E7F) return TextScript.Thai;
            if ((c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F) || (c >= 0xAC00 && c <= 0xD7AF)) return TextScript.Hangul;
            // CJK ideographs, plus kana - Japanese is filed with Han, see ScriptOfLanguage.
            if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF)) return TextScript.Han;
            return TextScript.Other;
        }

        /// <summary>The script a language is written in; null for a language not listed here.</summary>
        internal static TextScript? ScriptOfLanguage(string code)
        {
            switch ((code ?? "").Split('-')[0].ToLowerInvariant())
            {
                case "ru": case "uk": case "be": case "bg": case "mk": case "kk": case "ky": case "mn": case "tg":
                    return TextScript.Cyrillic;
                case "en": case "de": case "fr": case "es": case "it": case "pt": case "nl": case "pl": case "cs":
                case "sk": case "sl": case "hr": case "ro": case "hu": case "fi": case "sv": case "da": case "nb":
                case "no": case "is": case "et": case "lv": case "lt": case "tr": case "id": case "ms": case "vi":
                case "ca": case "ga": case "sq": case "az": case "uz": case "af": case "sw": case "tl": case "eu":
                    return TextScript.Latin;
                case "el": return TextScript.Greek;
                case "ar": case "ur": case "fa": case "ps": return TextScript.Arabic;
                case "he": case "yi": return TextScript.Hebrew;
                case "hi": case "mr": case "ne": return TextScript.Devanagari;
                case "bn": case "as": return TextScript.Bengali;
                case "zh": case "ja": return TextScript.Han;
                case "ko": return TextScript.Hangul;
                case "th": return TextScript.Thai;
                default: return null;
            }
        }

        /// <summary>
        /// Letters one language of a shared script has and the other lacks - enough to tell that
        /// Russian text is not already Ukrainian, and the other way round.
        /// </summary>
        internal static bool ContradictsLanguage(string source, string targetCode)
        {
            string lower = (source ?? "").ToLowerInvariant();
            switch ((targetCode ?? "").Split('-')[0].ToLowerInvariant())
            {
                case "uk": return lower.IndexOfAny(new[] { 'ы', 'э', 'ъ', 'ё' }) >= 0;
                case "ru": return lower.IndexOfAny(new[] { 'і', 'ї', 'є', 'ґ', 'ў' }) >= 0;
                case "be": return lower.IndexOfAny(new[] { 'и', 'щ', 'ъ', 'ї', 'є', 'ґ' }) >= 0;
                default: return false;
            }
        }

        /// <summary>
        /// 1 - edit distance / the longer length, ignoring case: 1 for the same text, near 0 for
        /// unrelated text. Two rows of ints; a selection is capped at <see cref="MaxChars"/>.
        /// </summary>
        internal static double Similarity(string a, string b)
        {
            string x = (a ?? "").Trim().ToLowerInvariant();
            string y = (b ?? "").Trim().ToLowerInvariant();
            int longer = Math.Max(x.Length, y.Length);
            if (longer == 0) return 1;
            var previous = new int[y.Length + 1];
            var current = new int[y.Length + 1];
            for (int j = 0; j <= y.Length; j++) previous[j] = j;
            for (int i = 1; i <= x.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= y.Length; j++)
                {
                    int cost = x[i - 1] == y[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                int[] swap = previous; previous = current; current = swap;
            }
            return 1.0 - (double)previous[y.Length] / longer;
        }

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
