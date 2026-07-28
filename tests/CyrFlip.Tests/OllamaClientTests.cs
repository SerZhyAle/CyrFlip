using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The translator's protocol layer. Everything here is parsing and request shaping, so it is
    /// exercised through a fake transport that replays canned NDJSON - a test must never reach a real
    /// Ollama server (CI has none, and a developer machine that does would make the suite lie).
    /// </summary>
    public class OllamaClientTests
    {
        [Theory]
        [InlineData(null, "http://localhost:11434")]
        [InlineData("", "http://localhost:11434")]
        [InlineData("   ", "http://localhost:11434")]
        [InlineData("http://localhost:11434", "http://localhost:11434")]
        [InlineData("http://localhost:11434/", "http://localhost:11434")]
        [InlineData("http://localhost:11434/api", "http://localhost:11434")]
        [InlineData("http://localhost:11434/api/", "http://localhost:11434")]
        [InlineData("http://ai.lan:11434", "http://ai.lan:11434")]
        public void TheEndpointIsNormalizedIntoWhatTheUserProbablyMeant(string? entered, string expected)
            => Assert.Equal(expected, OllamaClient.NormalizeBase(entered));

        [Fact]
        public void ModelNamesAreReadFromATagsBody()
        {
            List<string> names = OllamaClient.ParseModelNames(
                "{\"models\":[{\"name\":\"qwen2.5:3b\",\"size\":12},{\"name\":\"gemma2:latest\"}]}");

            Assert.Equal(new[] { "qwen2.5:3b", "gemma2:latest" }, names);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("{}")]
        [InlineData("{\"models\":null}")]
        [InlineData("{\"models\":[{\"noname\":1}]}")]
        public void AnUnreadableTagsBodyMeansNoModelsRatherThanAnException(string? body)
            => Assert.Empty(OllamaClient.ParseModelNames(body));

        [Fact]
        public void AGenerateLineYieldsItsTextChunk()
        {
            string chunk = OllamaClient.ParseGenerateChunk(
                "{\"model\":\"qwen2.5:3b\",\"response\":\"Привет\",\"done\":false}", out bool done, out string? error);

            Assert.Equal("Привет", chunk);
            Assert.False(done);
            Assert.Null(error);
        }

        [Fact]
        public void TheFinalGenerateLineSaysDoneAndCarriesNoText()
        {
            string chunk = OllamaClient.ParseGenerateChunk(
                "{\"model\":\"qwen2.5:3b\",\"response\":\"\",\"done\":true}", out bool done, out string? error);

            Assert.Equal("", chunk);
            Assert.True(done);
            Assert.Null(error);
        }

        [Fact]
        public void AServerErrorLineIsReportedAsAnError()
        {
            OllamaClient.ParseGenerateChunk("{\"error\":\"model 'nope' not found\"}", out bool _, out string? error);
            Assert.Equal("model 'nope' not found", error);
        }

        [Fact]
        public void AGarbageLineIsSilentlyEmptyBecauseAStreamMustNotDieOnOneBadLine()
        {
            string chunk = OllamaClient.ParseGenerateChunk("<html>proxy error</html>", out bool done, out string? error);

            Assert.Equal("", chunk);
            Assert.False(done);
            Assert.Null(error);
        }

        [Fact]
        public void PullProgressCombinesTheServerStatusWithAPercentage()
        {
            string text = OllamaClient.ParsePullProgress(
                "{\"status\":\"downloading\",\"completed\":450,\"total\":900}", out bool success, out string? error);

            Assert.Equal("downloading 50%", text);
            Assert.False(success);
            Assert.Null(error);
        }

        [Fact]
        public void PullProgressWithoutSizesIsJustTheStatus()
        {
            Assert.Equal("pulling manifest",
                OllamaClient.ParsePullProgress("{\"status\":\"pulling manifest\"}", out bool _, out string? _));
        }

        [Fact]
        public void PullReportsSuccessAndErrorsDistinctly()
        {
            OllamaClient.ParsePullProgress("{\"status\":\"success\"}", out bool success, out string? _);
            Assert.True(success);

            OllamaClient.ParsePullProgress("{\"error\":\"no space left\"}", out bool _, out string? error);
            Assert.Equal("no space left", error);
        }

        [Fact]
        public async Task ProbeIsTrueOnlyWhenTheServerAnswers()
        {
            var transport = new FakeTransport();
            var client = new OllamaClient("", transport);

            transport.Body = null;
            Assert.False(await client.ProbeAsync(CancellationToken.None));

            transport.Body = "{\"models\":[]}";
            Assert.True(await client.ProbeAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AStreamedCompletionIsStitchedBackTogetherAndReportedPieceByPiece()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[]
            {
                "{\"response\":\"Hello\",\"done\":false}",
                "{\"response\":\", \",\"done\":false}",
                "{\"response\":\"world\",\"done\":false}",
                "{\"response\":\"\",\"done\":true}",
            });
            var client = new OllamaClient("", transport);
            var chunks = new List<string>();

            string? answer = await client.GenerateAsync("qwen2.5:3b", "system", "prompt", 5,
                chunk => chunks.Add(chunk), 1000, 5000, CancellationToken.None);

            Assert.Equal("Hello, world", answer);
            Assert.Equal(new[] { "Hello", ", ", "world" }, chunks);
        }

        [Fact]
        public async Task TheGenerateRequestCarriesTheModelPromptAndKeepAlive()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[] { "{\"response\":\"ok\",\"done\":true}" });
            var client = new OllamaClient("", transport);

            await client.GenerateAsync("qwen2.5:3b", "you are an engine", "translate this", 7,
                null, 1000, 5000, CancellationToken.None);

            string body = Assert.Single(transport.Posts).Body;
            Assert.Contains("\"model\":\"qwen2.5:3b\"", body);
            Assert.Contains("\"system\":\"you are an engine\"", body);
            Assert.Contains("\"prompt\":\"translate this\"", body);
            Assert.Contains("\"stream\":true", body);
            Assert.Contains("\"keep_alive\":\"7m\"", body);
            Assert.Contains("/api/generate", transport.Posts[0].Url);
        }

        [Fact]
        public async Task AFailedRequestIsNullRatherThanAnEmptyTranslation()
        {
            var transport = new FakeTransport { PostSucceeds = false };
            var client = new OllamaClient("", transport);

            Assert.Null(await client.GenerateAsync("m", "s", "p", 5, null, 1000, 5000, CancellationToken.None));
        }

        [Fact]
        public async Task AnErrorInTheStreamIsAFailureEvenThoughTheRequestItselfSucceeded()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[] { "{\"error\":\"model 'nope' not found\"}" });
            var client = new OllamaClient("", transport);

            Assert.Null(await client.GenerateAsync("nope", "s", "p", 5, null, 1000, 5000, CancellationToken.None));
        }

        [Fact]
        public async Task APullOnlyCountsAsDoneWhenTheServerSaysSuccess()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[] { "{\"status\":\"pulling manifest\"}", "{\"status\":\"success\"}" });
            var client = new OllamaClient("", transport);
            var reported = new List<string>();

            Assert.True(await client.PullModelAsync("qwen2.5:3b",
                new Progress<string>(text => reported.Add(text)), CancellationToken.None));

            // An interrupted pull ends without the success line.
            transport.Lines.Enqueue(new[] { "{\"status\":\"downloading\",\"completed\":1,\"total\":10}" });
            Assert.False(await client.PullModelAsync("qwen2.5:3b", null, CancellationToken.None));
        }

        [Fact]
        public async Task APullErrorIsAFailureEvenWhenASuccessLineFollows()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[] { "{\"error\":\"no space left\"}", "{\"status\":\"success\"}" });
            var client = new OllamaClient("", transport);

            Assert.False(await client.PullModelAsync("qwen2.5:3b", null, CancellationToken.None));
        }

        [Fact]
        public async Task AnEmptyModelNameIsNeverSentToTheServer()
        {
            var transport = new FakeTransport();
            var client = new OllamaClient("", transport);

            Assert.False(await client.PullModelAsync("   ", null, CancellationToken.None));
            Assert.Empty(transport.Posts);
        }

        /// <summary>Replays canned NDJSON and records what was posted; never touches the network.</summary>
        internal sealed class FakeTransport : IOllamaTransport
        {
            public string? Body;
            public bool PostSucceeds = true;
            /// <summary>Stand in for a cancelled token or an expired deadline - both surface this way.</summary>
            public bool ThrowOnPost;
            public readonly Queue<string[]> Lines = new Queue<string[]>();
            public readonly List<Posted> Posts = new List<Posted>();

            public Task<string?> GetStringAsync(string url, int timeoutMs, CancellationToken ct)
                => Task.FromResult(Body);

            /// <summary>The two budgets the real transport applies, recorded so tests can assert them.</summary>
            public int LastTimeoutMs;
            public int LastAnswerTimeoutMs;

            public Task<bool> PostLinesAsync(string url, string json, Action<string> onLine,
                int timeoutMs, int answerTimeoutMs, CancellationToken ct)
            {
                if (ThrowOnPost) throw new OperationCanceledException(ct);
                LastTimeoutMs = timeoutMs;
                LastAnswerTimeoutMs = answerTimeoutMs;
                Posts.Add(new Posted(url, json));
                if (Lines.Count > 0)
                    foreach (string line in Lines.Dequeue())
                        onLine(line);
                return Task.FromResult(PostSucceeds);
            }

            internal sealed class Posted
            {
                public readonly string Url;
                public readonly string Body;
                public Posted(string url, string body) { Url = url; Body = body; }
            }
        }
    }
}
