using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The translator verb of ticket S0128: the manifest row C38, the parser lines, the rule-13 gate before the module check,
    /// and the handler over a fake service - no context, no window, no config file, no server and no socket. The cases that
    /// need a running copy, a live Ollama, a real cut against the live service and the 130-second bound itself are the
    /// release's and the owner's desk runs (<c>tools/uitest/Test-AppCommands.ps1</c>, the ticket's Manual section).
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsTranslateTests
    {
        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static AppCommandLineResult Parse(params string[] args) => AppCommandLine.Parse(args);

        /// <summary>
        /// The fake service: records what the entry hands it (the cut text, the resolved codes, the named source, the token's
        /// state) and answers the result the case wants. The host is the verb's only seam, so whatever is recorded here is
        /// everything the verb did - no window, no key and no clipboard call can hide behind anything.
        /// </summary>
        private sealed class FakeTranslateHost : ITranslateCommandsHost
        {
            public string UiLanguage { get; set; } = "ru";
            public bool RejectClaim;
            public bool Claimed;
            public bool Released;
            public TranslationResult? Result;
            public readonly List<(string text, string target, string? source, bool tokenCancelled)> Calls =
                new List<(string, string, string?, bool)>();

            public bool BeginCommandTranslation()
            {
                if (RejectClaim) return false;
                Assert.False(Claimed, "a second translation was started on a claimed host");
                Claimed = true;
                return true;
            }

            public void EndCommandTranslation()
            {
                Assert.True(Claimed, "the claim was released without being taken");
                Released = true;
            }

            public Task<TranslationResult> TranslateAsync(string text, string targetCode, string? sourceLanguageName,
                CancellationToken ct)
            {
                Calls.Add((text, targetCode, sourceLanguageName, ct.IsCancellationRequested));
                return Task.FromResult(Result ?? new TranslationResult
                {
                    Status = TranslationStatus.Ok,
                    Text = "перевод",
                    Model = "aya-expanse:8b",
                });
            }
        }

        private static Task<AppCommandResponse> Run(AppCommandInvocation invocation, FakeTranslateHost host,
            Func<ITranslateCommandsHost?>? provider = null, CancellationToken token = default)
            => AppCommandsTranslate.TranslateText(invocation, provider ?? (() => host), T, token);

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheTranslateTextRowIsInTheManifestWithTheRowsFields()
        {
            AppCommandSpec spec = AppCommandManifest.Shipped.Find("translate text")!;

            Assert.NotNull(spec);
            Assert.Equal("leaves-machine", spec.Risk);
            Assert.Equal("refuse", spec.ColdStart);
            Assert.Equal("done", spec.Wait);
            Assert.Equal(130000, spec.TimeoutMs);
            Assert.True(spec.Idempotent);
            Assert.Equal("string", spec.Arg("text")!.Kind);
            Assert.True(spec.Arg("text")!.Required);
            Assert.Equal("string", spec.Arg("to")!.Kind);
            Assert.True(spec.Arg("to")!.Required);
            Assert.Equal("string", spec.Arg("from")!.Kind);
            Assert.False(spec.Arg("from")!.Required);
            Assert.Contains("text", spec.Summary, StringComparison.Ordinal);
        }

        [Fact]
        public void TheParserTakesTheOptionsAndRefusesTheRequiredOnesWhenAbsent()
        {
            AppCommandLineResult good = Parse("translate", "text", "--text", "hello", "--to", "ru", "--from", "en");
            Assert.Equal(AppCommandLineKind.Command, good.Kind);
            Assert.Equal("ru", good.Invocation!.Option("to"));
            Assert.Equal("en", good.Invocation!.Option("from"));

            AppCommandLineResult noText = Parse("translate", "text", "--to", "ru");
            Assert.Equal(AppCommandLineKind.Refused, noText.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, noText.Reason);

            AppCommandLineResult noTo = Parse("translate", "text", "--text", "hello");
            Assert.Equal(AppCommandLineKind.Refused, noTo.Kind);

            // The empty values reach the handler, which answers them by name.
            Assert.Equal(AppCommandLineKind.Command, Parse("translate", "text", "--text", "", "--to", "ru").Kind);
        }

        [Fact]
        public async Task TheHandlerRefusesTheEmptyValuesAndTheActiveToken()
        {
            FakeTranslateHost host = new FakeTranslateHost();

            AppCommandResponse emptyText = await Run(Inv("translate", "text", "--text", "", "--to", "ru"), host);
            Assert.Equal("refused", emptyText.Status);
            Assert.Equal(AppCommandExit.Usage, emptyText.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, emptyText.Reason);

            AppCommandResponse emptyTo = await Run(Inv("translate", "text", "--text", "hello", "--to", ""), host);
            Assert.Equal(AppCommandExit.Usage, emptyTo.ExitCode);

            AppCommandResponse emptyFrom = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru", "--from", ""), host);
            Assert.Equal(AppCommandExit.Usage, emptyFrom.ExitCode);

            AppCommandResponse activeTo = await Run(Inv("translate", "text", "--text", "hello", "--to", "active"), host);
            Assert.Equal(AppCommandExit.Usage, activeTo.ExitCode);
            Assert.Contains("active", activeTo.Message, StringComparison.Ordinal);

            AppCommandResponse activeFrom = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru", "--from", "ACTIVE"), host);
            Assert.Equal(AppCommandExit.Usage, activeFrom.ExitCode);

            Assert.Empty(host.Calls);
        }

        // ------------------------------------------------------------------ the gate and the module

        [Fact]
        public void TheRuleThirteenGateRefusesTheLeavesMachineVerbWithTheSettingOff()
        {
            AppCommandSpec spec = AppCommandManifest.Shipped.Find("translate text")!;

            // The gate of the base answers 12 before this verb's dispatcher case runs, so it answers 12 while the
            // translator module is off too: the class comes first, the module second (the S0121 order).
            AppCommandResponse? refused = AppCommandVerbs.RiskGate(spec, settingOn: false, legacy: false, T);
            Assert.NotNull(refused);
            Assert.Equal(AppCommandExit.Permission, refused!.ExitCode);
            Assert.Equal(AppCommandReasons.SwitchOff, refused.Reason);

            Assert.Null(AppCommandVerbs.RiskGate(spec, settingOn: true, legacy: false, T));
        }

        [Fact]
        public async Task AnOffTranslatorModuleAnswersModuleOffBeforeAnythingIsAsked()
        {
            FakeTranslateHost host = new FakeTranslateHost();
            bool askedForAHost = false;
            Func<ITranslateCommandsHost?> provider = () =>
            {
                askedForAHost = true;
                return null; // the module is off: no host exists, nothing is created, read or started
            };

            AppCommandResponse response = await AppCommandsTranslate.TranslateText(
                Inv("translate", "text", "--text", "hello", "--to", "ru"), provider, T, CancellationToken.None);

            Assert.True(askedForAHost);
            Assert.Equal("refused", response.Status);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            Assert.Empty(host.Calls);
        }

        // ------------------------------------------------------------------ the entry over a fake service

        [Fact]
        public async Task TheEntryCallsTheServiceWithTheResolvedCodesAndAnswersTheResult()
        {
            FakeTranslateHost host = new FakeTranslateHost();

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal("done", response.Status);
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal("перевод", response.Data!["text"]);
            Assert.Equal("aya-expanse:8b", response.Data["model"]);
            Assert.Equal(false, response.Data["partial"]);
            Assert.Equal(false, response.Data["truncated"]);
            (string text, string target, string? source, _) = Assert.Single(host.Calls);
            Assert.Equal("hello", text);
            Assert.Equal("ru", target);
            Assert.Null(source);
            Assert.True(host.Released);
        }

        [Fact]
        public async Task TheNamedSourceAndTheUiTokenResolveBeforeTheCall()
        {
            FakeTranslateHost host = new FakeTranslateHost { UiLanguage = "de" };

            await Run(Inv("translate", "text", "--text", "hello", "--to", "ui", "--from", "ui"), host);
            (string text, string target, string? source, _) = Assert.Single(host.Calls);
            Assert.Equal("de", target);
            Assert.Equal("German", source);

            FakeTranslateHost named = new FakeTranslateHost { UiLanguage = "ru" };
            await Run(Inv("translate", "text", "--text", "hello", "--to", "FR", "--from", "en"), named);
            (string _, string target2, string? source2, _) = Assert.Single(named.Calls);
            Assert.Equal("fr", target2);
            Assert.Equal("English", source2);
        }

        [Fact]
        public async Task AnInputOverTheServicesCapIsCutAtTheEntryAndTheCutIsAnswered()
        {
            FakeTranslateHost host = new FakeTranslateHost();
            string longText = new string('a', TranslationService.MaxChars + 500);

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", longText, "--to", "ru"), host);

            (string text, string _, string? _, bool _) = Assert.Single(host.Calls);
            Assert.Equal(TranslationService.MaxChars, text.Length);
            Assert.Equal(true, response.Data!["truncated"]);
            Assert.Contains(TranslationService.MaxChars.ToString(), response.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ASecondConcurrentVerbAnswersBusyAndNoQueueForms()
        {
            FakeTranslateHost host = new FakeTranslateHost { RejectClaim = true };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal("busy", response.Status);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.Busy, response.Reason);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public async Task ABoundOverrunAnswersTimeoutAndTheCallWasCancelledThroughTheToken()
        {
            var cancel = new CancellationTokenSource();
            cancel.Cancel();
            FakeTranslateHost host = new FakeTranslateHost
            {
                Result = new TranslationResult { Status = TranslationStatus.Cancelled, Text = "" },
            };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host, token: cancel.Token);

            (string _, string _, string? _, bool tokenCancelled) = Assert.Single(host.Calls);
            Assert.True(tokenCancelled);
            Assert.Equal("failed", response.Status);
            Assert.Equal(AppCommandExit.NotProven, response.ExitCode);
            Assert.Equal(AppCommandReasons.Timeout, response.Reason);
            Assert.True(host.Released);
        }

        [Theory]
        [InlineData("NotInstalled", "not installed")]
        [InlineData("NotRunning", "not running")]
        [InlineData("StartFailed", "not be started")]
        public async Task TheThreeLocalServerStatesAnswerNetworkEachByName(string statusName, string words)
        {
            // The enum is internal, so the theory carries its name.
            var status = (TranslationStatus)Enum.Parse(typeof(TranslationStatus), statusName);
            FakeTranslateHost host = new FakeTranslateHost { Result = new TranslationResult { Status = status } };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal(AppCommandExit.Network, response.ExitCode);
            Assert.Equal(AppCommandReasons.Network, response.Reason);
            Assert.Contains(words, response.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnUnreachableRemoteServerAnswersNetworkAndNamesTheServer()
        {
            FakeTranslateHost host = new FakeTranslateHost
            {
                Result = new TranslationResult { Status = TranslationStatus.Unreachable, Server = "nas.local" },
            };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal(AppCommandExit.Network, response.ExitCode);
            Assert.Equal(AppCommandReasons.Network, response.Reason);
            Assert.Contains("nas.local", response.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AModellessServerAnswersTheProductsOwnNoModelToken()
        {
            FakeTranslateHost host = new FakeTranslateHost { Result = new TranslationResult { Status = TranslationStatus.NoModel } };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal("no-model", response.Reason);
        }

        [Fact]
        public async Task AServerRefusalAndAUselessModelAnswerBothAnswerFailed()
        {
            FakeTranslateHost host = new FakeTranslateHost
            {
                Result = new TranslationResult { Status = TranslationStatus.ServerError, Error = "boom" },
            };
            AppCommandResponse refused = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);
            Assert.Equal(AppCommandExit.Failed, refused.ExitCode);

            FakeTranslateHost empty = new FakeTranslateHost { Result = new TranslationResult { Status = TranslationStatus.Failed } };
            AppCommandResponse failed = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), empty);
            Assert.Equal(AppCommandExit.Failed, failed.ExitCode);
        }

        [Fact]
        public async Task APartialAnswerSaysSo()
        {
            FakeTranslateHost host = new FakeTranslateHost
            {
                Result = new TranslationResult { Status = TranslationStatus.Ok, Text = "част", Model = "m", Partial = true },
            };

            AppCommandResponse response = await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal(true, response.Data!["partial"]);
            Assert.Contains("partial", response.Message, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ the service seam (P1)

        [Fact]
        public void ANullSourceKeepsThePromptsByteIdenticalAndANamePrependsOneSentence()
        {
            const string plain = "Translate the FULL following text into German." + "\n" +
                "Translate every sentence and detail faithfully; do not summarize, shorten, or omit anything." + "\n" +
                "Write the translation ENTIRELY in German using its native alphabet; do NOT use Chinese characters or any other language. Output only the translation, no notes and no quotes." + "\n" +
                "Text: some source";

            // The chord's prompt, unchanged by this ticket (a null is the auto-detect default).
            Assert.Equal(plain, TranslationService.BuildPrompt("German", "some source", null));
            Assert.Equal("The source text is written in Russian." + "\n" + plain,
                TranslationService.BuildPrompt("German", "some source", "Russian"));

            const string retry = "Translate this ENTIRE text into German ONLY. Translate every sentence and detail; " +
                "do not summarize, shorten, or omit anything. The output must be written entirely in German's native alphabet. " +
                "Produce a genuine translation, not a copy. Output only the translation." + "\n" +
                "Text: some source";
            Assert.Equal(retry, TranslationService.BuildRetryPrompt("German", "some source", null));
            Assert.Equal("The source text is written in Russian." + "\n" + retry,
                TranslationService.BuildRetryPrompt("German", "some source", "Russian"));
        }

        // ------------------------------------------------------------------ the two log disciplines

        [Fact]
        public async Task TheTranslateLogLinesCarryCodesModesAndOutcomesAndNeverTheText()
        {
            FakeTranslateHost host = new FakeTranslateHost { UiLanguage = "ru" };
            await Run(Inv("translate", "text", "--text", "hello", "--to", "ru"), host);

            Assert.Equal("command: into ru (auto-detect source)", AppCommandsTranslate.CommandLine("ru", null, truncated: false));
            Assert.Equal("command: into fr (named source), input cut at " + TranslationService.MaxChars,
                AppCommandsTranslate.CommandLine("fr", "French", truncated: true));
            Assert.DoesNotContain("hello", AppCommandsTranslate.CommandLine("ru", null, false), StringComparison.Ordinal);

            var result = new TranslationResult
            {
                Status = TranslationStatus.Ok,
                Text = "перевод",
                Model = "aya-expanse:8b",
                Error = "",
            };
            string line = AppCommandsTranslate.ResultLine(result);
            Assert.Equal("command result: Ok, model aya-expanse:8b, 7 chars back", line);
            Assert.DoesNotContain("перевод", line, StringComparison.Ordinal);
        }

        [Fact]
        public void TheAppCommandsLogLineHoldsTheVerbAndTheOutcomeAndNeverTheText()
        {
            var ok = AppCommandResponse.Done("The translation is ready.", new Dictionary<string, object>
            {
                ["text"] = "перевод",
                ["model"] = "aya-expanse:8b",
                ["partial"] = false,
                ["truncated"] = true,
            });
            string line = AppCommandsLog.Line(new DateTime(2026, 11, 17, 23, 0, 0), "translate text", "cli", 4242, ok);
            Assert.Contains("translate text", line, StringComparison.Ordinal);
            Assert.Contains("status=done", line, StringComparison.Ordinal);
            Assert.DoesNotContain("перевод", line, StringComparison.Ordinal);
            Assert.DoesNotContain("aya", line, StringComparison.Ordinal);
            Assert.DoesNotContain("truncated", line, StringComparison.Ordinal);

            AppCommandResponse noText = AppCommandResponse.Failed(AppCommandReasons.NoModel, AppCommandExit.NotReady, "The server answered, but no model is installed on it.");
            string refused = AppCommandsLog.Line(DateTime.Now, "translate text", "cli", 99, noText);
            Assert.Contains("reason=no-model", refused, StringComparison.Ordinal);
        }
    }
}
