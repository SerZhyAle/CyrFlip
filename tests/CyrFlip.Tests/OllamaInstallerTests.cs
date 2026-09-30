using System;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What stands between a downloaded OllamaSetup.exe and ShellExecute (ticket S0010 TD-5): the
    /// length the server announced, a valid Authenticode signature, and the signer being Ollama.
    /// Nothing here downloads anything - the checks are exercised on their own.
    /// </summary>
    public class OllamaInstallerTests
    {
        [Theory]
        [InlineData(700, 700, true)]
        [InlineData(700, -1, true)]   // no Content-Length: the signature check is what is left
        [InlineData(699, 700, false)] // a cleanly closed short stream is a truncated exe
        [InlineData(0, -1, false)]
        public void ADownloadIsCompleteOnlyAtTheAnnouncedLength(long read, long announced, bool complete)
            => Assert.Equal(complete, OllamaManager.IsComplete(read, announced));

        [Theory]
        [InlineData("CN=Ollama Inc., O=Ollama Inc., L=Toronto, S=Ontario, C=CA, SERIALNUMBER=2713355", true)]
        [InlineData("CN=Ollama Inc., O=Ollama Inc., L=Somewhere Else, C=US", true)] // a renewed certificate
        [InlineData("CN=Ollama Inc., O=Someone Else", false)]
        [InlineData("CN=Ollama Inc. Ltd, O=Ollama Inc. Ltd", false)]
        [InlineData("CN=Evil, O=Ollama Inc.", false)]
        [InlineData("", false)]
        public void OnlyOllamaAsBothNameAndOrganisationIsTheExpectedSigner(string subject, bool matches)
            => Assert.Equal(matches, AuthenticodeCheck.SubjectNames(subject, AuthenticodeCheck.OllamaPublisher));

        [Fact]
        public void AnUnsignedFileIsNeverTrusted()
        {
            string file = Path.Combine(Path.GetTempPath(), "cyrflip-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllBytes(file, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
            try
            {
                Assert.False(AuthenticodeCheck.IsSignedBy(file, AuthenticodeCheck.OllamaPublisher));
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void AMissingFileIsNeverTrusted()
            => Assert.False(AuthenticodeCheck.IsSignedBy(@"C:\no\such\OllamaSetup.exe", AuthenticodeCheck.OllamaPublisher));

        // ---- S0037 TR-1: a download that goes silent ----

        /// <summary>
        /// A response stream as net48's is: a few chunks, then a read that never completes and never
        /// looks at its token - only disposing the stream ends it.
        /// </summary>
        private sealed class StallingStream : Stream
        {
            private int _chunksLeft;
            private readonly System.Threading.Tasks.TaskCompletionSource<int> _stall = new System.Threading.Tasks.TaskCompletionSource<int>();

            public StallingStream(int chunks) => _chunksLeft = chunks;

            public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken ct)
            {
                if (_chunksLeft-- > 0) return System.Threading.Tasks.Task.FromResult(Math.Min(count, 10));
                return _stall.Task;   // the token is ignored, as the real one does once a read is pending
            }

            protected override void Dispose(bool disposing)
            {
                _stall.TrySetException(new ObjectDisposedException("response stream"));
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Fact]
        public async System.Threading.Tasks.Task ADownloadThatGoesSilentEndsAsATimeoutWithWhatItHad()
        {
            var target = new MemoryStream();
            long reported = 0;
            var copy = OllamaManager.CopyAsync(new StallingStream(chunks: 3), target, idleTimeoutMs: 200,
                done => reported = done, System.Threading.CancellationToken.None);

            Assert.Same(copy, await System.Threading.Tasks.Task.WhenAny(copy, System.Threading.Tasks.Task.Delay(10_000)));
            await Assert.ThrowsAsync<TimeoutException>(() => copy);
            Assert.Equal(30, target.Length);
            Assert.Equal(30, reported);
        }

        [Fact]
        public async System.Threading.Tasks.Task ACancelEndsAStalledDownloadAsACancellation()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            var copy = OllamaManager.CopyAsync(new StallingStream(chunks: 1), new MemoryStream(), idleTimeoutMs: 60_000, null, cts.Token);

            cts.CancelAfter(100);

            Assert.Same(copy, await System.Threading.Tasks.Task.WhenAny(copy, System.Threading.Tasks.Task.Delay(10_000)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
        }

        [Fact]
        public async System.Threading.Tasks.Task AStreamThatEndsIsCopiedWhole()
        {
            var source = new MemoryStream(new byte[250_000]);
            var target = new MemoryStream();
            long read = await OllamaManager.CopyAsync(source, target, 1000, null, System.Threading.CancellationToken.None);
            Assert.Equal(250_000, read);
            Assert.Equal(250_000, target.Length);
        }

        // ---- S0037 TR-5: old installer folders ----

        [Fact]
        public void OldInstallerFoldersArePrunedAndRecentOnesOnlyWhenOllamaIsInstalled()
        {
            string temp = Path.Combine(Path.GetTempPath(), "cyrflip-prune-" + Guid.NewGuid().ToString("N"));
            string old = Path.Combine(temp, "CyrFlip-ollama-old");
            string recent = Path.Combine(temp, "CyrFlip-ollama-recent");
            string other = Path.Combine(temp, "someone-else");
            try
            {
                foreach (string dir in new[] { old, recent, other })
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "OllamaSetup.exe"), "MZ");
                }
                Directory.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-2));

                OllamaManager.PruneStaleDownloads(evenRecent: false, tempFolder: temp);
                Assert.False(Directory.Exists(old));
                Assert.True(Directory.Exists(recent));   // may belong to an installer running now

                OllamaManager.PruneStaleDownloads(evenRecent: true, tempFolder: temp);
                Assert.False(Directory.Exists(recent));
                Assert.True(Directory.Exists(other));    // never anything but our own prefix
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }
    }
}
