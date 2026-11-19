using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// What the translator verb needs of the running copy (ticket S0128): the instance's own UI language to resolve the
    /// <c>ui</c> token against, the one command translation at a time, and the service the running copy already owns -
    /// the very <see cref="TranslationService"/> the chord uses, called with no sink so nothing paints, nothing synthesizes
    /// and nothing touches the clipboard.
    /// </summary>
    internal interface ITranslateCommandsHost
    {
        /// <summary>The instance's own UI language, which the <c>ui</c> token resolves against.</summary>
        string UiLanguage { get; }

        /// <summary>Claims the one command-translation slot: false when another command translation is in flight.</summary>
        bool BeginCommandTranslation();

        /// <summary>Releases the claim of <see cref="BeginCommandTranslation"/>, whatever the outcome was.</summary>
        void EndCommandTranslation();

        /// <summary>
        /// The service entry (text, resolved target code, named source or null, no sink, the caller's token): everything the
        /// verb has. No popup, no synthesized key, no clipboard call can happen on this path - the host is the only seam.
        /// </summary>
        Task<TranslationResult> TranslateAsync(string text, string targetCode, string? sourceLanguageName, CancellationToken ct);
    }

    /// <summary>
    /// The translator verb (ticket S0128, APP-COMMANDS 0.3, the research row C38): <c>translate text</c> - text in, text out.
    /// A script or an agent hands the text over <c>--text</c> and names the target (<c>--to</c>), optionally the source
    /// (<c>--from</c>), and the running instance's own service answers; no selection is taken, no window is opened, the
    /// clipboard is never touched and no balloon is raised. The verb is a pure function over the parsed invocation and a
    /// host seam, so the tests need no context and no window - a fake service stands in for the model.
    ///
    /// <para>Order of the refusals: the arguments (10) - the strict parser has already refused an absent <c>--text</c> or
    /// <c>--to</c> and an input over the line bounds - then the module (11 <c>module-off</c>, the S0121 order: the base's
    /// rule-13 gate has already run, so the setting is 12 even while the module is off too), then the one command
    /// translation at a time (the base's <c>busy</c> shape, its own sentence: no queue forms behind a seconds-long model
    /// call). The input is cut at the service's own <see cref="TranslationService.MaxChars"/> cap at the entry and the cut is
    /// answered (<c>truncated</c> in the data, the message names it), the service's one retry, its timeout ladder and its
    /// never-start-a-remote-server rule ride unchanged on the service's own path, and the bound's overrun answers
    /// <c>timeout</c> (2) with the call cancelled through the caller's token (APP-COMMANDS rule 12; the four server states
    /// answer <c>network</c> (15) each by name, a model-less server the product's own token <c>no-model</c> (11)).</para>
    ///
    /// <para>The logs keep their disciplines: <c>translate.log</c> gains the command line beside the chord's own - the
    /// target code, the source mode and the outcome, never a character of the text - and the base's
    /// <c>app-commands.log</c> line never changes (the verb word and the outcome only).</para>
    /// </summary>
    internal static class AppCommandsTranslate
    {
        // ------------------------------------------------------------------ translate text

        /// <summary>
        /// <c>translate text --text &lt;text&gt; --to &lt;language or ui&gt; [--from &lt;language or ui&gt;]</c> (row C38):
        /// the arguments first, the module second, the one-at-a-time claim third, then the service - with the input cut at
        /// the entry so the answer can say so, and the target and source resolved against the instance's own languages.
        /// </summary>
        public static async Task<AppCommandResponse> TranslateText(AppCommandInvocation invocation,
            Func<ITranslateCommandsHost?> provider, Func<string, string> T, CancellationToken token)
        {
            try
            {
                string text = invocation.Option("text") ?? "";
                if (text.Trim().Length == 0)
                    return Usage(T("Give the text to translate with --text."));

                string? to = invocation.Option("to");
                if (to == null || to.Trim().Length == 0)
                    return Usage(T("Give the target language with --to: a language code or ui."));

                string? from = invocation.Option("from");
                if (from != null && from.Trim().Length == 0)
                    return Usage(T("Give the source language with --from: a language code or ui."));

                AppCommandResponse? refused = RefuseActive(to, T) ?? RefuseActive(from, T);
                if (refused != null) return refused;

                ITranslateCommandsHost? host = provider();
                if (host == null)
                    return AppCommandVerbs.ModuleOff(T("The translator is switched off. Turn it on in the settings of CyrFlip."));
                if (!host.BeginCommandTranslation())
                    return AppCommandResponse.BusyNow(
                        T("Another command translation is still running. Wait for its answer, then ask again."));

                string targetCode = TranslationLanguages.Resolve(to, host.UiLanguage, null);
                string? sourceName = null;
                if (from != null)
                    sourceName = TranslationLanguages.EnglishName(TranslationLanguages.Resolve(from, host.UiLanguage, null));

                string input = TranslationService.Truncate(text, TranslationService.MaxChars, out bool truncated);
                TranslateLog.Log(CommandLine(targetCode, sourceName, truncated));

                TranslationResult result;
                try
                {
                    result = await host.TranslateAsync(input, targetCode, sourceName, token).ConfigureAwait(false);
                }
                finally
                {
                    host.EndCommandTranslation();
                }
                TranslateLog.Log(ResultLine(result));

                return Answer(result, truncated, T);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Crashed("translate text", ex, T);
            }
        }

        // ------------------------------------------------------------------ the answer

        /// <summary>
        /// The outcome table of section 2: the four server states answer <c>network</c> (15), each by name; a server that
        /// answers with no models installed answers the product's own token <c>no-model</c> (11, rule 12's last sentence);
        /// a server that refused, or a model that answered nothing usable, answers 1; the bound's overrun answers
        /// <c>timeout</c> (2) with the call already cancelled through the caller's token; a translation answers the text,
        /// the resolved model, and whether it is partial or covers a cut input.
        /// </summary>
        private static AppCommandResponse Answer(TranslationResult result, bool truncated, Func<string, string> T)
        {
            switch (result.Status)
            {
                case TranslationStatus.Ok:
                    string message = T("The translation is ready.");
                    if (result.Partial)
                        message += " " + T("The model stopped early, so the translation is partial.");
                    if (truncated)
                        message += " " + Localization.Format(T,
                            "The text was longer than {0} characters, so the translation covers the first {0}.",
                            TranslationService.MaxChars);
                    return Bounded(message, new Dictionary<string, object>
                    {
                        ["text"] = result.Text,
                        ["model"] = result.Model,
                        ["partial"] = result.Partial,
                        ["truncated"] = truncated,
                    }, T);

                case TranslationStatus.NoText:
                    return Usage(T("Give the text to translate with --text."));

                case TranslationStatus.NotInstalled:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server is not installed on this computer."));

                case TranslationStatus.NotRunning:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server is not running, and starting it automatically is switched off in the settings of CyrFlip."));

                case TranslationStatus.StartFailed:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server could not be started."));

                case TranslationStatus.Unreachable:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        Localization.Format(T, "The translation server at {0} did not answer.", result.Server));

                case TranslationStatus.NoModel:
                    return AppCommandResponse.Failed(AppCommandReasons.NoModel, AppCommandExit.NotReady,
                        T("The server answered, but no model is installed on it. The settings of CyrFlip say how to get one."));

                case TranslationStatus.Timeout:
                case TranslationStatus.Cancelled:
                    return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                        T("CyrFlip did not answer in time. It may still be working."));

                case TranslationStatus.ServerError:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The translation server refused the request."));

                default:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The model could not translate this text."));
            }
        }

        /// <summary>
        /// The noun's answer must fit the one response line the caller reads: a translation of a text the service itself caps
        /// always does, and a runaway model is answered rather than cut (<see cref="AppCommandReasons"/> has no
        /// <c>response-too-large</c> of its own - the token of APP-COMMANDS amendment M, S0117's precedent).
        /// </summary>
        private static AppCommandResponse Bounded(string message, Dictionary<string, object> data, Func<string, string> T)
        {
            AppCommandResponse response = AppCommandResponse.Done(message, data);
            response.RequestId = new string('0', 36);
            int bytes = Encoding.UTF8.GetByteCount(AppCommandProtocol.Serialize(response)) + 1;
            response.RequestId = "";
            return bytes <= AppCommandLine.MaxRequestBytes
                ? response
                : AppCommandResponse.Failed("response-too-large", AppCommandExit.Failed,
                    T("The answer is too large. Translate a shorter piece of text."));
        }

        // ------------------------------------------------------------------ the translate.log lines

        /// <summary>
        /// The command's own line of <c>translate.log</c>, beside the chord's: the target code, the source mode and the cut -
        /// never a character of the text (the module's own rule since its first day).
        /// </summary>
        internal static string CommandLine(string targetCode, string? sourceLanguageName, bool truncated)
            => "command: into " + targetCode
               + (string.IsNullOrEmpty(sourceLanguageName) ? " (auto-detect source)" : " (named source)")
               + (truncated ? ", input cut at " + TranslationService.MaxChars : "");

        /// <summary>The outcome line - the service's status, partial, the model and the length; never the text.</summary>
        internal static string ResultLine(TranslationResult result)
            => "command result: " + result.Status
               + (result.Partial ? " (partial - the model stopped answering)" : "")
               + (result.Model.Length > 0 ? ", model " + result.Model : "")
               + (result.Error.Length > 0 ? ", error " + result.Error : "")
               + ", " + result.Text.Length + " chars back";

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// The <c>active</c> token means the keyboard layout that is live in the window a selection is taken from; a command
        /// carries its own text and reads no window, so the token has no honest referent here (the ticket's Assumed entry of
        /// 2026-11-17). Refused wherever it appears.
        /// </summary>
        private static AppCommandResponse? RefuseActive(string? value, Func<string, string> T)
        {
            if (value != null && string.Equals(value.Trim(), TranslationLanguages.ActiveToken, StringComparison.OrdinalIgnoreCase))
                return Usage(T("The token active names the layout of the window a translation is taken from, and a command reads no window. Give a language code or ui."));
            return null;
        }

        private static AppCommandResponse Usage(string message)
            => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, message);

        /// <summary>A fault after the claim would otherwise fault the caller's wait: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }
}
