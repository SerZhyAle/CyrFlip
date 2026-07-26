using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The feature logic of the translator: what goes into the prompt, when an answer is retried, and
    /// which model is used. The HTTP layer is the fake transport from <see cref="OllamaClientTests"/>,
    /// and the "is Ollama installed / start it" P/Invokes are constructor seams, so none of this
    /// touches the machine.
    /// </summary>
    public class TranslationServiceTests
    {
        [Fact]
        public void ThePromptNamesTheTargetLanguageAndCarriesTheWholeText()
        {
            string prompt = TranslationService.BuildPrompt("German", "Привет, мир");

            Assert.Contains("German", prompt);
            Assert.Contains("Привет, мир", prompt);
            // The instructions that keep small models from summarizing or drifting to another script.
            Assert.Contains("do not summarize", prompt);
            Assert.Contains("native alphabet", prompt);
        }

        [Fact]
        public void TheRetryPromptIsFirmerButStillCarriesTheSameText()
        {
            string retry = TranslationService.BuildRetryPrompt("German", "Привет, мир");

            Assert.Contains("Привет, мир", retry);
            Assert.Contains("not a copy", retry);
            Assert.NotEqual(TranslationService.BuildPrompt("German", "Привет, мир"), retry);
        }

        [Fact]
        public void LongSelectionsAreCutAtTheLimitAndSayThatTheyWere()
        {
            string source = new string('a', TranslationService.MaxChars + 500);

            string cut = TranslationService.Truncate(source, TranslationService.MaxChars, out bool truncated);

            Assert.True(truncated);
            Assert.Equal(TranslationService.MaxChars, cut.Length);

            string short_ = TranslationService.Truncate("hello", TranslationService.MaxChars, out bool untouched);
            Assert.False(untouched);
            Assert.Equal("hello", short_);
        }

        [Fact]
        public void AnEchoedOrEmptyAnswerIsWorthOneMoreTry()
        {
            Assert.True(TranslationService.NeedsRetry("Привет, мир", ""));
            Assert.True(TranslationService.NeedsRetry("Привет, мир", "   "));
            Assert.True(TranslationService.NeedsRetry("Привет, мир", "Привет, мир"));
            Assert.True(TranslationService.NeedsRetry("Привет, мир", "привет, МИР")); // case is not a translation

            Assert.False(TranslationService.NeedsRetry("Привет, мир", "Hello, world"));
        }

        [Fact]
        public void AShortStringThatSurvivesTranslationUnchangedIsNotAnEcho()
        {
            // "OK" translates to "OK" in most languages; rejecting it would loop forever.
            Assert.False(TranslationService.LooksLikeEcho("OK", "OK"));
            Assert.True(TranslationService.LooksLikeEcho("Привет", "Привет"));
        }

        [Fact]
        public void ThePreferredModelIsTheBestTranslatorInstalledNotJustTheFirst()
        {
            Assert.Equal("aya-expanse:8b",
                TranslationService.PickPreferredModel(new[] { "codellama:7b", "aya-expanse:8b", "qwen2.5:3b" }));
            Assert.Equal("qwen2.5:3b",
                TranslationService.PickPreferredModel(new[] { "codellama:7b", "qwen2.5:3b" }));
            // Nothing recognised: take what there is rather than refuse to translate.
            Assert.Equal("codellama:7b", TranslationService.PickPreferredModel(new[] { "codellama:7b" }));
            Assert.Equal("", TranslationService.PickPreferredModel(new string[0]));
        }

        [Fact]
        public void AModelNameMatchesItsTaggedFormAndBack()
        {
            Assert.True(TranslationService.ModelMatches("qwen2.5:3b", "qwen2.5:3b"));
            Assert.True(TranslationService.ModelMatches("qwen2.5:latest", "qwen2.5"));
            Assert.True(TranslationService.ModelMatches("qwen2.5", "qwen2.5:latest"));
            Assert.False(TranslationService.ModelMatches("qwen2.5:3b", "gemma2"));
            // "qwen2.5" must not match "qwen2.5-coder:7b" - a different model, not a tag.
            Assert.False(TranslationService.ModelMatches("qwen2.5-coder:7b", "qwen2.5"));
        }

        [Fact]
        public async Task AHealthyServerTranslatesAndReportsTheModelItUsed()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            transport.Lines.Enqueue(Answer("Hello, world"));
            TranslationService service = Service(transport, new AppConfig { TranslateModel = "qwen2.5:3b" });
            var chunks = new List<string>();

            TranslationResult result = await service.TranslateAsync("Привет, мир", "en",
                new TranslationSink(chunks.Add, () => { }), CancellationToken.None);

            Assert.Equal(TranslationStatus.Ok, result.Status);
            Assert.Equal("Hello, world", result.Text);
            Assert.Equal("qwen2.5:3b", result.Model);
            Assert.False(result.Truncated);
            Assert.Equal(new[] { "Hello, world" }, chunks);
            Assert.Contains("English", transport.Posts[0].Body); // the target language reached the prompt
        }

        [Fact]
        public async Task AnEchoIsRetriedOnceAndTheStreamedTextIsResetFirst()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            transport.Lines.Enqueue(Answer("Привет, мир"));   // the model handed the source back
            transport.Lines.Enqueue(Answer("Hello, world"));  // the firmer prompt worked
            TranslationService service = Service(transport, new AppConfig());
            int resets = 0;

            TranslationResult result = await service.TranslateAsync("Привет, мир", "en",
                new TranslationSink(_ => { }, () => resets++), CancellationToken.None);

            Assert.Equal(TranslationStatus.Ok, result.Status);
            Assert.Equal("Hello, world", result.Text);
            Assert.Equal(1, resets); // the window was cleared before the second attempt painted into it
            Assert.Equal(2, transport.Posts.Count);
        }

        [Fact]
        public async Task ARetryThatIsAlsoBadKeepsTheFirstAnswerRatherThanShowingNothing()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            transport.Lines.Enqueue(Answer("Привет, мир"));
            transport.Lines.Enqueue(Answer(""));
            TranslationService service = Service(transport, new AppConfig());

            TranslationResult result = await service.TranslateAsync("Привет, мир", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.Ok, result.Status);
            Assert.Equal("Привет, мир", result.Text);
        }

        [Fact]
        public async Task AnInstalledModelIsUsedWhenTheConfiguredOneIsMissing()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("gemma2:2b", "aya-expanse:8b") };
            transport.Lines.Enqueue(Answer("Hallo"));
            TranslationService service = Service(transport, new AppConfig { TranslateModel = "qwen2.5:3b" });

            TranslationResult result = await service.TranslateAsync("Привет", "de", null, CancellationToken.None);

            Assert.Equal("aya-expanse:8b", result.Model); // the best installed translator, not the missing one
        }

        [Fact]
        public async Task TheConfiguredModelWinsEvenWhenAPreferredOneIsAlsoInstalled()
        {
            // aya sorts first in the preference list, so this only passes if the configured name is
            // actually honoured rather than the list being re-ranked every time.
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("aya-expanse:8b", "gemma2:2b") };
            transport.Lines.Enqueue(Answer("Hallo"));
            TranslationService service = Service(transport, new AppConfig { TranslateModel = "gemma2:2b" });

            TranslationResult result = await service.TranslateAsync("Привет", "de", null, CancellationToken.None);

            Assert.Equal("gemma2:2b", result.Model);
        }

        [Fact]
        public async Task TheConfiguredNameMatchesTheInstalledTag()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("gemma2:latest") };
            transport.Lines.Enqueue(Answer("Hallo"));
            TranslationService service = Service(transport, new AppConfig { TranslateModel = "gemma2" });

            TranslationResult result = await service.TranslateAsync("Привет", "de", null, CancellationToken.None);

            Assert.Equal("gemma2:latest", result.Model);
        }

        [Fact]
        public async Task EscDuringGenerationIsReportedAsCancelledSoNothingIsDelivered()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b"), ThrowOnPost = true };
            TranslationService service = Service(transport, new AppConfig());
            var cts = new CancellationTokenSource();
            cts.Cancel();

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, cts.Token);

            // Cancelled, not Timeout: the caller shows nothing at all for this one.
            Assert.Equal(TranslationStatus.Cancelled, result.Status);
        }

        [Fact]
        public async Task AModelThatNeverAnswersIsATimeoutRatherThanACancellation()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b"), ThrowOnPost = true };
            TranslationService service = Service(transport, new AppConfig());

            // The user cancelled nothing; the per-request deadline fired inside the transport.
            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.Timeout, result.Status);
        }

        [Fact]
        public async Task AnEmptySelectionNeverReachesTheServer()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            TranslationService service = Service(transport, new AppConfig());

            TranslationResult result = await service.TranslateAsync("   ", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.NoText, result.Status);
            Assert.Empty(transport.Posts);
        }

        [Fact]
        public async Task ASelectionLongerThanTheLimitIsTranslatedUpToItAndFlagged()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            transport.Lines.Enqueue(Answer("..."));
            TranslationService service = Service(transport, new AppConfig());
            string source = new string('я', TranslationService.MaxChars + 10);

            TranslationResult result = await service.TranslateAsync(source, "en", null, CancellationToken.None);

            Assert.True(result.Truncated);
            Assert.Equal(TranslationService.MaxChars + 10, result.SourceLength);
        }

        [Fact]
        public async Task WithoutOllamaInstalledTheAnswerIsSayingSoNotAFailure()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = null }; // server unreachable
            TranslationService service = Service(transport, new AppConfig(), installed: false);

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.NotInstalled, result.Status);
        }

        [Fact]
        public async Task WithAutoStartOffAnUnreachableServerIsReportedAsNotRunning()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = null };
            TranslationService service = Service(transport,
                new AppConfig { TranslateAutoStartServer = false }, installed: true);

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.NotRunning, result.Status);
        }

        [Fact]
        public async Task AStartThatFailsOutrightIsReportedWithoutWaitingForTheServer()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = null };
            TranslationService service = Service(transport,
                new AppConfig { TranslateAutoStartServer = true }, installed: true, started: false);

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.StartFailed, result.Status);
        }

        [Theory]
        [InlineData("")]
        [InlineData("qwen2.5:3b")]
        public async Task AServerWithNoModelsCannotTranslateEvenWhenTheSettingsNameOne(string configured)
        {
            // The settings naming a model proves nothing: this server does not have it, and sending
            // the request anyway could only come back as an opaque failure.
            var transport = new OllamaClientTests.FakeTransport { Body = "{\"models\":[]}" };
            TranslationService service = Service(transport, new AppConfig { TranslateModel = configured });

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.NoModel, result.Status);
            Assert.Empty(transport.Posts);
        }

        [Fact]
        public async Task AnHttpFailureIsAServerErrorNotTheModelFailing()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b"), PostSucceeds = false };
            TranslationService service = Service(transport, new AppConfig());

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            // "the model could not handle this text" would be a lie - the request never got that far.
            Assert.Equal(TranslationStatus.ServerError, result.Status);
        }

        [Fact]
        public async Task TheServersOwnErrorLineIsCarriedOutSoTheUserCanReadIt()
        {
            var transport = new OllamaClientTests.FakeTransport { Body = Tags("qwen2.5:3b") };
            transport.Lines.Enqueue(new[] { "{\"error\":\"model 'qwen2.5:3b' not found\"}" });
            TranslationService service = Service(transport, new AppConfig());

            TranslationResult result = await service.TranslateAsync("Привет", "en", null, CancellationToken.None);

            Assert.Equal(TranslationStatus.ServerError, result.Status);
            Assert.Equal("model 'qwen2.5:3b' not found", result.Error);
        }

        private static TranslationService Service(IOllamaTransport transport, AppConfig config,
            bool installed = true, bool started = true)
            => new TranslationService(config, endpoint => new OllamaClient(endpoint, transport),
                () => installed, () => started);

        private static string Tags(params string[] models)
        {
            var parts = new List<string>();
            foreach (string model in models) parts.Add("{\"name\":\"" + model + "\"}");
            return "{\"models\":[" + string.Join(",", parts.ToArray()) + "]}";
        }

        private static string[] Answer(string text)
            => new[] { "{\"response\":" + Quote(text) + ",\"done\":false}", "{\"response\":\"\",\"done\":true}" };

        private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
