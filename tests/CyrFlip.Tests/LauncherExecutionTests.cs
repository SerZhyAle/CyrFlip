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
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, hostileLink, @"C:\Downloads", YtDlpPath);

            Assert.Equal("cmd.exe", info.FileName);
            // The command line references the variable, never the raw link - so &, |, <, > can
            // never become shell operators.
            Assert.DoesNotContain(hostileLink, info.Arguments);
            Assert.Contains("\"%" + LauncherExecution.YtDlpLinkVariable + "%\"", info.Arguments);
            Assert.Equal(hostileLink, info.EnvironmentVariables[LauncherExecution.YtDlpLinkVariable]);
            Assert.False(info.UseShellExecute); // required for the environment to transfer
            Assert.Equal(@"C:\Downloads", info.WorkingDirectory);
        }

        private const string YtDlpPath = @"C:\Tools\yt-dlp.exe";

        /// <summary>
        /// S0008 LS-4: the full path that was resolved on PATH (cmd would search the download folder
        /// first), "--" before the link so it can never be an option, and no current-directory search
        /// for anything cmd starts itself.
        /// </summary>
        [Fact]
        public void YtDlpIsStartedByItsFullPathWithTheLinkAfterDoubleDash()
        {
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp };
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, "https://x.test/v", @"C:\D", YtDlpPath);
            Assert.Equal("/s /k \"\"" + YtDlpPath + "\" -- \"%" + LauncherExecution.YtDlpLinkVariable + "%\"\"", info.Arguments);
            Assert.Equal("1", info.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"]);
        }

        [Fact]
        public void YtDlpFormatTokensAreQuotedBeforeTheDoubleDash()
        {
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp, YtDlpFormat = "-f bestvideo[height<=1080]+bestaudio" };
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, "https://x.test/v", @"C:\D", YtDlpPath);
            Assert.Equal("/s /k \"\"" + YtDlpPath + "\" \"-f\" \"bestvideo[height<=1080]+bestaudio\" -- \"%"
                + LauncherExecution.YtDlpLinkVariable + "%\"\"", info.Arguments);
        }

        /// <summary>A stored format outside the allowed set is kept in the scenario but never reaches cmd.</summary>
        [Theory]
        [InlineData("-f best & calc")]
        [InlineData("-o %USERPROFILE%/x")]
        [InlineData("-f \"best\"")]
        [InlineData("-f best|more")]
        [InlineData("-f best^x")]
        public void AFormatWithShellMetacharactersIsNotPassed(string format)
        {
            Assert.False(LauncherExecution.IsValidYtDlpFormat(format));
            var scenario = new LauncherScenario { Type = LauncherScenarioType.YtDlp, YtDlpFormat = format };
            ProcessStartInfo info = LauncherExecution.BuildYtDlpStartInfo(scenario, "https://x.test/v", @"C:\D", YtDlpPath);
            Assert.DoesNotContain("best", info.Arguments);
            Assert.DoesNotContain("USERPROFILE", info.Arguments);
        }

        [Fact]
        public void AnOverlongFormatIsNotPassed()
            => Assert.False(LauncherExecution.IsValidYtDlpFormat("-f " + new string('a', LauncherExecution.MaxYtDlpFormatLength)));

        [Theory]
        [InlineData("")]
        [InlineData("-f bestvideo+bestaudio")]
        [InlineData("-x --audio-format mp3")]
        // The set is the one open decision 2 of S0008 fixed: no quote, %, ^, &, | - nothing cmd reads.
        public void AnOrdinaryFormatIsValid(string format)
            => Assert.True(LauncherExecution.IsValidYtDlpFormat(format));

        [Theory]
        [InlineData("-x")]
        [InlineData("--exec calc")]
        [InlineData("ftp://x.test/v")]
        [InlineData("x.test/v")]
        [InlineData("file:///C:/x")]
        public void ALinkMustBeAnAbsoluteHttpAddress(string link)
            => Assert.NotNull(LauncherExecution.ValidateYtDlpLink(link, Identity));

        // ---- What is validated is what is started (S0008 LS-6) ----

        [Fact]
        public void AScriptFoundOnlyOnPathIsStartedByItsAbsolutePath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cyrflip-path-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string script = Path.Combine(dir, "tool.ps1");
                File.WriteAllText(script, "");
                Assert.True(LauncherExecution.TryResolveTarget("tool.ps1", null, Identity,
                    out string? resolved, out _, pathVariable: dir));
                Assert.Equal(script, resolved);

                var scenario = new LauncherScenario { Path = "tool.ps1", RunAsAdmin = true };
                ProcessStartInfo info = LauncherExecution.BuildStartInfo(scenario, resolved!);
                Assert.Contains("-File \"" + script + "\"", info.Arguments);
                Assert.Equal("runas", info.Verb);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ABareExecutableOnPathIsElevatedByItsAbsolutePath()
        {
            Assert.True(LauncherExecution.TryResolveTarget("cmd.exe", null, Identity, out string? resolved, out _));
            Assert.True(Path.IsPathRooted(resolved));
            ProcessStartInfo info = LauncherExecution.BuildStartInfo(new LauncherScenario { Path = "cmd.exe", RunAsAdmin = true }, resolved!);
            Assert.Equal(resolved, info.FileName);
        }

        /// <summary>A relative PATH entry is the current directory - exactly the search being avoided.</summary>
        [Fact]
        public void ARelativePathEntryIsNotSearched()
            => Assert.Null(LauncherExecution.ResolveOnPath("cmd.exe", "." + Path.PathSeparator + @"relative\dir"));

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

        // ---- What launcher.log may say (S0010 TD-1) ----

        /// <summary>
        /// launcher.log goes into the log bundle, and a scenario's arguments are where tokens and
        /// connection strings live: no line the launch writes may carry the path or the arguments.
        /// </summary>
        [Fact]
        public void NoLaunchLineCarriesThePathOrTheArguments()
        {
            var scenario = new LauncherScenario
            {
                Id = new Guid("0b6c3f0e-1111-2222-3333-444455556666"),
                Name = "Deploy",
                Path = @"C:\secret-dir\deploy.exe",
                Arguments = "--token=SECRET-TOKEN",
            };
            string[] lines =
            {
                LauncherExecution.LaunchedLine(scenario, LauncherExecution.KindOf(scenario.Path), 42),
                LauncherExecution.BlockedLine(scenario),
                LauncherExecution.FailedLine(scenario, "exe",
                    new System.ComponentModel.Win32Exception(2, @"C:\secret-dir\deploy.exe --token=SECRET-TOKEN")),
                LauncherExecution.FailedLine(scenario, "exe", new InvalidOperationException("SECRET-TOKEN")),
            };

            foreach (string line in lines)
            {
                Assert.DoesNotContain("secret-dir", line);
                Assert.DoesNotContain("SECRET", line);
                Assert.Contains("'Deploy' [0b6c3f0e-1111-2222-3333-444455556666]", line);
            }
            Assert.Equal("Launched 'Deploy' [0b6c3f0e-1111-2222-3333-444455556666] (exe, pid=42, admin=False)", lines[0]);
            Assert.EndsWith("Win32Exception (Win32 error 2)", lines[2]);
        }

        [Theory]
        [InlineData(@"C:\x\tool.exe", "exe")]
        [InlineData(@"C:\x\tool.ps1", "script")]
        [InlineData(@"C:\x\tool.CMD", "script")]
        [InlineData("https://example.com/x.ps1", "url")]
        public void TheKindNamesTheTargetWithoutNamingIt(string resolved, string kind)
            => Assert.Equal(kind, LauncherExecution.KindOf(resolved));
    }
}
