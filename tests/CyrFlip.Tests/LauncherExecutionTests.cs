using System;
using System.Diagnostics;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure halves of the launch pipeline: target validation, PATH resolution, interpreter
    /// choice with its load-bearing quoting, yt-dlp link validation and the yt-dlp command shape
    /// (the untrusted link must reach cmd only through the environment). Nothing here starts a
    /// process.
    /// </summary>
    public class LauncherExecutionTests
    {
        private static string Identity(string ru) => ru; // the translator seam, untranslated for tests

        // ---- Target validation ----

        [Fact]
        public void EmptyPathIsRejected()
        {
            Assert.False(LauncherExecution.TryResolveTarget("", null, Identity, out string? error));
            Assert.Equal("У сценария не задан путь.", error);
        }

        [Fact]
        public void ExistingFilePasses()
        {
            string self = typeof(LauncherExecutionTests).Assembly.Location;
            Assert.True(LauncherExecution.TryResolveTarget(self, null, Identity, out _));
        }

        [Theory]
        [InlineData("https://example.com/watch?v=1")]
        [InlineData("http://example.com")]
        public void HttpUrlsPass(string url)
            => Assert.True(LauncherExecution.TryResolveTarget(url, null, Identity, out _));

        [Fact]
        public void PathRelativeToTheWorkingDirectoryPasses()
        {
            string self = typeof(LauncherExecutionTests).Assembly.Location;
            Assert.True(LauncherExecution.TryResolveTarget(
                Path.GetFileName(self), Path.GetDirectoryName(self), Identity, out _));
        }

        [Fact]
        public void RootedMissingPathFailsWithTheFileMessage()
        {
            Assert.False(LauncherExecution.TryResolveTarget(
                @"C:\definitely\not\here.exe", null, Identity, out string? error));
            Assert.Contains(@"C:\definitely\not\here.exe", error);
        }

        [Fact]
        public void BareCommandResolvableOnPathPasses()
            => Assert.True(LauncherExecution.TryResolveTarget("cmd.exe", null, Identity, out _));

        [Fact]
        public void BareCommandWithoutExtensionResolvesThroughPathext()
            => Assert.NotNull(LauncherExecution.ResolveOnPath("cmd"));

        [Fact]
        public void UnresolvableCommandFails()
        {
            Assert.False(LauncherExecution.TryResolveTarget(
                "cyrflip-no-such-command-xyz", null, Identity, out string? error));
            Assert.Contains("cyrflip-no-such-command-xyz", error);
        }

        // ---- yt-dlp link validation and command shape ----

        [Theory]
        [InlineData("https://a/b\"c")]
        [InlineData("https://a/b\r\nwhoami")]
        [InlineData("https://a/\tb")]
        public void LinksWithQuotesOrControlCharactersAreRejected(string link)
            => Assert.NotNull(LauncherExecution.ValidateYtDlpLink(link, Identity));

        [Fact]
        public void OrdinaryLinkWithShellMetacharactersIsAccepted()
            => Assert.Null(LauncherExecution.ValidateYtDlpLink("https://x.test/watch?v=1&list=2|3<4>5", Identity));

        [Fact]
        public void YtDlpCommandCarriesTheLinkOnlyInTheEnvironment()
        {
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp };
            string hostileLink = "https://x.test/a?b=1&c=2|whoami<x>y";
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, hostileLink, @"C:\Downloads");

            Assert.Equal("cmd.exe", info.FileName);
            // The command line references the variable, never the raw link - so &, |, <, > can
            // never become shell operators.
            Assert.DoesNotContain(hostileLink, info.Arguments);
            Assert.Contains("\"%" + LauncherExecution.YtDlpLinkVariable + "%\"", info.Arguments);
            Assert.StartsWith("/k yt-dlp ", info.Arguments);
            Assert.Equal(hostileLink, info.EnvironmentVariables[LauncherExecution.YtDlpLinkVariable]);
            Assert.False(info.UseShellExecute); // required for the environment to transfer
            Assert.Equal(@"C:\Downloads", info.WorkingDirectory);
        }

        [Fact]
        public void YtDlpFormatIsInsertedBeforeTheLink()
        {
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp, YtDlpFormat = "-f bestvideo+bestaudio" };
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, "https://x.test/v", @"C:\D");
            Assert.Equal("/k yt-dlp -f bestvideo+bestaudio \"%" + LauncherExecution.YtDlpLinkVariable + "%\"", info.Arguments);
        }

        [Fact]
        public void EmptyOutputFolderDefaultsToDownloads()
        {
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp };
            string folder = LauncherExecution.ResolveYtDlpOutputFolder(scenario);
            Assert.EndsWith("Downloads", folder);
            scenario.YtDlpOutputFolder = @"D:\Media";
            Assert.Equal(@"D:\Media", LauncherExecution.ResolveYtDlpOutputFolder(scenario));
        }

        // ---- Script interpreter (the quoting is the OneClickRunner-proven shape) ----

        [Fact]
        public void Ps1GoesToPowerShellWithBypassAndTrailingFile()
        {
            Assert.True(LauncherScriptInterpreter.TryResolve(
                @"C:\my scripts\do it.ps1", "-x \"1 2\"", out string interpreter, out string args));
            Assert.Contains("powershell", interpreter, StringComparison.OrdinalIgnoreCase);
            // -File must stay last so every following token belongs to the script.
            Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\my scripts\\do it.ps1\" -x \"1 2\"", args);
        }

        [Theory]
        [InlineData(@"C:\tools\build all.bat")]
        [InlineData(@"C:\tools\build all.cmd")]
        public void BatAndCmdUseTheDoubleQuotedSlashSForm(string script)
        {
            Assert.True(LauncherScriptInterpreter.TryResolve(script, "-y \"a b\"", out string interpreter, out string args));
            Assert.Equal("cmd.exe", interpreter);
            // /s /c "" script "..." - the one form that survives a spaced path AND quoted arguments.
            Assert.Equal("/s /c \"\"" + script + "\" -y \"a b\"\"", args);
        }

        [Theory]
        [InlineData(@"C:\Windows\System32\calc.exe")]
        [InlineData("https://example.com/run.ps1")] // an http .ps1 belongs to the browser
        [InlineData(@"C:\docs\readme.txt")]
        public void NonScriptsAreLeftToTheShell(string path)
            => Assert.False(LauncherScriptInterpreter.TryResolve(path, null, out _, out _));

        [Fact]
        public void PowerShellHostAlwaysResolvesToSomething()
            => Assert.False(string.IsNullOrEmpty(LauncherScriptInterpreter.PowerShellHost()));
    }
}
