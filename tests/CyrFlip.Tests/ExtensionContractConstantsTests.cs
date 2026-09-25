using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The VS Code extension has no test runner, so the numbers <c>LAYOUT-SIGNAL</c> fixes on its side
    /// are checked here by <b>reading its source text</b> - the honest name for what this does: it runs
    /// nothing, it only notices when a constant the contract depends on is edited.
    /// </summary>
    public class ExtensionContractConstantsTests
    {
        private static readonly string ExtensionRoot = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "vscode-extension"));

        private static string Read(string relative)
        {
            string path = Path.Combine(ExtensionRoot, relative);
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        [Theory]
        [InlineData("SIGNAL_WRITE_INTERVAL_MS", 500)]  // rule 10: the claim is refreshed at most this often
        [InlineData("EDITOR_ACTIVITY_TTL_MS", 5000)]   // rule 10: and only after editor activity this recent
        public void TheClaimantsTimingIsTheContracts(string name, int expected)
        {
            Match m = Regex.Match(Read(@"src\extension.ts"), @"const\s+" + name + @"\s*=\s*(\d+)\s*;");
            Assert.True(m.Success, name + " not found in extension.ts");
            Assert.Equal(expected, int.Parse(m.Groups[1].Value));
        }

        /// <summary>Rule 3: the consumer keeps four characters of the code and eight of the KLID.</summary>
        [Fact]
        public void TheConsumerTruncatesToTheContractsPrefixes()
        {
            string source = Read(@"src\extension.ts");
            Assert.Contains(".slice(0, 4)", source);
            Assert.Contains(".slice(0, 8)", source);
        }

        /// <summary>Rule 4: the reference poll interval.</summary>
        [Fact]
        public void TheDefaultPollIsTheContracts()
        {
            Match m = Regex.Match(Read("package.json"), "\"cyrflip\\.pollIntervalMs\"\\s*:\\s*\\{[^}]*\"default\"\\s*:\\s*(\\d+)");
            Assert.True(m.Success, "cyrflip.pollIntervalMs default not found in package.json");
            Assert.Equal(200, int.Parse(m.Groups[1].Value));
        }
    }

    /// <summary>
    /// <c>BUILD-EVIDENCE</c> rule 3: a failing test is never retried into a pass. That holds today because
    /// nothing in the suite can retry; this keeps it that way. The one flake family on record was cured by
    /// serialization (<see cref="SharedGdiCollection"/>), not by a retry.
    /// </summary>
    public class TestSuiteEvidenceTests
    {
        [Fact]
        public void NoTestRetryPackageIsReferenced()
        {
            string csproj = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "CyrFlip.Tests.csproj"));
            Assert.True(File.Exists(csproj), "missing " + csproj);

            foreach (Match m in Regex.Matches(File.ReadAllText(csproj), "<PackageReference\\s+Include=\"([^\"]+)\""))
                Assert.DoesNotContain("retry", m.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
