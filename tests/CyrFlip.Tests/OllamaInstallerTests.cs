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
    }
}
