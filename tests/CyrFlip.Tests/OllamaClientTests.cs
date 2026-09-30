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
        // S0037 TR-2: Ollama's own OLLAMA_HOST shape has no scheme.
        [InlineData("127.0.0.1:11434", "http://127.0.0.1:11434")]
        [InlineData("localhost:11434/", "http://localhost:11434")]
        [InlineData("ai.lan:11434/api", "http://ai.lan:11434")]
        [InlineData("https://ai.example:443", "https://ai.example:443")]
        public void TheEndpointIsNormalizedIntoWhatTheUserProbablyMeant(string? entered, string expected)
            => Assert.Equal(expected, OllamaClient.NormalizeBase(entered));

        /// <summary>...and the scheme-less local server is local: it is the one CyrFlip may start.</summary>
        [Theory]
        [InlineData("127.0.0.1:11434")]
        [InlineData("localhost:11434")]
        public void ASchemeLessLocalEndpointIsLoopback(string entered)
            => Assert.True(OllamaClient.IsLoopback(OllamaClient.NormalizeBase(entered)));

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
                chunk => chunks.Add(chunk), 1000, 5000, 0, CancellationToken.None);

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
                null, 1000, 5000, 0, CancellationToken.None);

            string body = Assert.Single(transport.Posts).Body;
            Assert.Contains("\"model\":\"qwen2.5:3b\"", body);
            Assert.Contains("\"system\":\"you are an engine\"", body);
            Assert.Contains("\"prompt\":\"translate this\"", body);
            Assert.Contains("\"stream\":true", body);
            Assert.Contains("\"keep_alive\":\"7m\"", body);
            Assert.Contains("/api/generate", transport.Posts[0].Url);
        }

        // ---- S0010 TD-2: a deadline mid-stream keeps what was written ----

        [Fact]
        public async Task ADeadlineAfterTheFirstChunkReturnsTheTextSoFarMarkedIncomplete()
        {
            var transport = new FakeTransport { StopAfterLines = true };
            transport.Lines.Enqueue(new[]
            {
                "{\"response\":\"one \",\"done\":false}", "{\"response\":\"two \",\"done\":false}",
                "{\"response\":\"three \",\"done\":false}", "{\"response\":\"four \",\"done\":false}",
                "{\"response\":\"five\",\"done\":false}",
            });
            var client = new OllamaClient("", transport);

            string? answer = await client.GenerateAsync("m", "s", "p", 5, null, 1000, 30000, 0, CancellationToken.None);

            Assert.Equal("one two three four five", answer);
            Assert.True(client.LastStoppedEarly);
        }

        [Fact]
        public async Task ADeadlineBeforeTheFirstChunkIsStillATimeout()
        {
            var client = new OllamaClient("", new FakeTransport { StopAfterLines = true });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GenerateAsync("m", "s", "p", 5, null, 1000, 30000, 0, CancellationToken.None));
            Assert.False(client.LastStoppedEarly);
        }

        [Fact]
        public async Task TheUsersOwnCancelThrowsEvenAfterChunksArrived()
        {
            var transport = new FakeTransport { StopAfterLines = true };
            transport.Lines.Enqueue(new[] { "{\"response\":\"half\",\"done\":false}" });
            var client = new OllamaClient("", transport);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Esc means "never mind": nothing may be delivered, however much was written.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GenerateAsync("m", "s", "p", 5, null, 1000, 30000, 0, cts.Token));
        }

        /// <summary>
        /// The read loop's clock (TD-2), scaled down: a line every 20 ms for 1.6 s against a 500 ms idle
        /// limit completes - the old total cap (the 500 ms "load" budget) would have cut it - and a
        /// stream that falls silent after five lines is cut, with those five delivered. The idle limit
        /// is 25 gaps wide on purpose: the full suite runs 40 s of parallel GUI tests, and a 20 ms
        /// delay against a 100 ms limit was cut by thread-pool starvation alone (S0029).
        /// </summary>
        [Fact]
        public async Task ASlowStreamThatKeepsWritingIsNeverCutOff()
        {
            int sent = 0;
            var lines = new List<string>();
            using var linked = new CancellationTokenSource(500); // the "load" budget: first line only

            bool ok = await HttpOllamaTransport.PumpAsync(async token =>
            {
                if (sent == 80) return null;
                await Task.Delay(20, token);
                return "line " + sent++;
            }, lines.Add, linked, idleTimeoutMs: 500, maxAnswerMs: 0);

            Assert.True(ok);
            Assert.Equal(80, lines.Count);
        }

        [Fact]
        public async Task AStreamThatFallsSilentIsCutAfterWhatItWrote()
        {
            int sent = 0;
            var lines = new List<string>();
            using var linked = new CancellationTokenSource(5000);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpOllamaTransport.PumpAsync(async token =>
            {
                if (sent == 5) await Task.Delay(Timeout.Infinite, token);
                await Task.Delay(10, token);
                return "line " + sent++;
            }, lines.Add, linked, idleTimeoutMs: 500, maxAnswerMs: 0));

            Assert.Equal(5, lines.Count);
        }

        [Fact]
        public async Task ARunawayStreamStopsAtTheCeiling()
        {
            var lines = new List<string>();
            using var linked = new CancellationTokenSource(2000);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpOllamaTransport.PumpAsync(async token =>
            {
                await Task.Delay(10, token);
                return "again";
            }, lines.Add, linked, idleTimeoutMs: 1000, maxAnswerMs: 150));

            Assert.InRange(lines.Count, 1, 60);
        }

        // ---- S0010 TD-6 / TD-4 ----

        [Theory]
        [InlineData("http://localhost:11434", true)]
        [InlineData("http://LOCALHOST:11434", true)]
        [InlineData("http://127.0.0.1:11434", true)]
        [InlineData("http://127.0.0.5:11434", true)]
        [InlineData("http://[::1]:11434", true)]
        [InlineData("http://192.168.1.20:11434", false)]
        [InlineData("http://gpu-box.lan:11434", false)]
        [InlineData("https://ollama.example.com", false)]
        [InlineData("not a url", false)]
        public void OnlyThisMachineCountsAsLoopback(string url, bool loopback)
            => Assert.Equal(loopback, OllamaClient.IsLoopback(url));

        [Fact]
        public void TheHostIsWhatTheMessageNames()
        {
            Assert.Equal("192.168.1.20:11434", OllamaClient.HostOf("http://192.168.1.20:11434"));
            Assert.Equal("ollama.example.com", OllamaClient.HostOf("https://ollama.example.com"));
        }

        [Fact]
        public void TheSystemDefaultIsLeftToTheSystem()
        {
            // SystemDefault lets Windows pick TLS 1.3 too; OR-ing Tls12 into it would pin 1.2 alone.
            Assert.Equal(System.Net.SecurityProtocolType.SystemDefault,
                TlsPolicy.WithTls12(System.Net.SecurityProtocolType.SystemDefault));
#pragma warning disable CS0618 // Ssl3 is obsolete - which is exactly the legacy default being tested
            var legacy = System.Net.SecurityProtocolType.Ssl3 | System.Net.SecurityProtocolType.Tls;
            Assert.Equal(legacy | System.Net.SecurityProtocolType.Tls12, TlsPolicy.WithTls12(legacy));
#pragma warning restore CS0618
        }

        [Fact]
        public async Task AFailedRequestIsNullRatherThanAnEmptyTranslation()
        {
            var transport = new FakeTransport { PostSucceeds = false };
            var client = new OllamaClient("", transport);

            Assert.Null(await client.GenerateAsync("m", "s", "p", 5, null, 1000, 5000, 0, CancellationToken.None));
        }

        [Fact]
        public async Task AnErrorInTheStreamIsAFailureEvenThoughTheRequestItselfSucceeded()
        {
            var transport = new FakeTransport();
            transport.Lines.Enqueue(new[] { "{\"error\":\"model 'nope' not found\"}" });
            var client = new OllamaClient("", transport);

            Assert.Null(await client.GenerateAsync("nope", "s", "p", 5, null, 1000, 5000, 0, CancellationToken.None));
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

            public readonly List<string> Gets = new List<string>();

            public Task<string?> GetStringAsync(string url, int timeoutMs, CancellationToken ct)
            {
                Gets.Add(url);
                // The real transport rethrows only the caller's own cancellation.
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Body);
            }

            /// <summary>The budgets the real transport applies, recorded so tests can assert them.</summary>
            public int LastTimeoutMs;
            public int LastIdleTimeoutMs;
            public int LastMaxAnswerMs;

            /// <summary>
            /// A deadline that fires mid-stream: the queued lines of the next post are delivered,
            /// then the post throws as the real transport does when its timer cancels it.
            /// </summary>
            public bool StopAfterLines;

            /// <summary>Throw as a deadline does from this post on (1 = the first), leaving earlier ones alone.</summary>
            public int ThrowFromPost;

            public Task<bool> PostLinesAsync(string url, string json, Action<string> onLine,
                int timeoutMs, int idleTimeoutMs, int maxAnswerMs, CancellationToken ct)
            {
                if (ThrowOnPost) throw new OperationCanceledException(ct);
                if (ThrowFromPost > 0 && Posts.Count + 1 >= ThrowFromPost) throw new OperationCanceledException();
                LastTimeoutMs = timeoutMs;
                LastIdleTimeoutMs = idleTimeoutMs;
                LastMaxAnswerMs = maxAnswerMs;
                Posts.Add(new Posted(url, json));
                if (Lines.Count > 0)
                    foreach (string line in Lines.Dequeue())
                        onLine(line);
                if (StopAfterLines) throw new OperationCanceledException();
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
