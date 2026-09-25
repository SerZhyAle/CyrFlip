using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What the context menu will and will not offer to open. The selection comes from whatever
    /// window the pointer was over, so this is untrusted text that the user is one click away from
    /// handing to the shell: the refusals matter more than the acceptances, and each of them is
    /// pinned here by name.
    /// </summary>
    public class LaunchTargetTests
    {
        /// <summary>A disk that holds exactly what a test says it holds.</summary>
        private static Func<string, bool> Disk(params string[] present)
        {
            var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
            return path => set.Contains(path);
        }

        private static readonly Func<string, bool> EmptyDisk = _ => false;

        private static LaunchTarget Parse(string selection, Func<string, bool>? exists = null)
        {
            Assert.True(LaunchTargets.TryParse(selection, out LaunchTarget? target, exists ?? EmptyDisk),
                "expected a launch target for: " + selection);
            return target!;
        }

        private static void Refused(string? selection, Func<string, bool>? exists = null)
            => Assert.False(LaunchTargets.TryParse(selection, out _, exists ?? EmptyDisk),
                "expected no launch target for: " + selection);

        // ---- URLs -------------------------------------------------------------------------

        [Theory]
        [InlineData("https://example.com/page?q=1")]
        [InlineData("http://example.com")]
        [InlineData("ftp://files.example.com/pub")]
        [InlineData("mailto:sza@ukr.net")]
        public void AnAbsoluteUrlInAnAllowedSchemeIsOpened(string selection)
            => Assert.Equal(LaunchKind.Url, Parse(selection).Kind);

        [Theory]
        [InlineData("www.example.org")]
        [InlineData("example.com")]
        [InlineData("docs.example.co.uk/guide")]
        public void ABareWebAddressGetsHttps(string selection)
        {
            LaunchTarget target = Parse(selection);
            Assert.Equal(LaunchKind.Url, target.Kind);
            Assert.StartsWith("https://", target.Target);
            Assert.Equal(selection, target.Display); // the caption stays what the user selected
        }

        /// <summary>
        /// The reason a bare host needs a known top-level domain rather than "two to twenty-four
        /// letters": every filename in a log or a chat message would otherwise become a web address.
        /// </summary>
        [Theory]
        [InlineData("readme.md")]
        [InlineData("notes.txt")]
        [InlineData("setup.exe")]
        [InlineData("script.ps1")]
        [InlineData("version.1")]
        public void AFilenameIsNotAWebAddress(string selection) => Refused(selection);

        /// <summary>The schemes that make "open the selection" a way to run code.</summary>
        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("shell:startup")]
        [InlineData("ms-settings:keyboard")]
        [InlineData("vbscript:msgbox")]
        [InlineData("data:text/html,<script>x</script>")]
        public void AnUnknownSchemeIsRefusedByName(string selection) => Refused(selection);

        [Theory]
        [InlineData("cmd /c del *.*")]
        [InlineData("rm -rf /")]
        [InlineData("just some selected words")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void OrdinaryTextIsNotALaunchTarget(string? selection) => Refused(selection);

        /// <summary>An e-mail address without mailto: is a name, not an instruction to open a client.</summary>
        [Fact]
        public void ABareEmailAddressIsRefused() => Refused("sza@ukr.net");

        // ---- Paths ------------------------------------------------------------------------

        [Fact]
        public void AnExistingDocumentOpensInItsAssociatedApp()
        {
            LaunchTarget target = Parse(@"C:\docs\report.pdf", Disk(@"C:\docs\report.pdf"));
            Assert.Equal(LaunchKind.Document, target.Kind);
            Assert.Equal(@"C:\docs\report.pdf", target.Target);
        }

        [Fact]
        public void AnExistingFolderOpensToo()
            => Assert.Equal(LaunchKind.Document, Parse(@"C:\tools", Disk(@"C:\tools")).Kind);

        /// <summary>
        /// A path that is not there is not a target. It is what keeps a plausible-looking string out
        /// of <c>ShellExecute</c>, and it is why nothing here ever becomes a command line.
        /// </summary>
        [Fact]
        public void APathThatDoesNotExistIsRefused() => Refused(@"C:\tools\ghost.exe");

        [Fact]
        public void ARelativePathIsRefusedEvenWhenSomethingByThatNameExists()
            => Refused(@"tools\setup.exe", Disk(@"tools\setup.exe"));

        [Theory]
        [InlineData(@"C:\tools\setup.exe")]
        [InlineData(@"C:\tools\install.bat")]
        [InlineData(@"C:\tools\build.ps1")]
        [InlineData(@"C:\tools\patch.reg")]
        [InlineData(@"C:\tools\app.lnk")]
        public void AnythingThatRunsCodeComesBackAsProgram(string path)
            => Assert.Equal(LaunchKind.Program, Parse(path, Disk(path)).Kind);

        /// <summary>
        /// A file on a share is someone else's file whatever its extension says, so it is confirmed
        /// like a program rather than opened on sight.
        /// </summary>
        [Fact]
        public void AFileOnANetworkShareIsAlwaysConfirmed()
            => Assert.Equal(LaunchKind.Program,
                Parse(@"\\server\share\report.pdf", Disk(@"\\server\share\report.pdf")).Kind);

        [Fact]
        public void AFileUrlBecomesThePathItNames()
        {
            LaunchTarget target = Parse("file:///C:/docs/report.pdf", Disk(@"C:\docs\report.pdf"));
            Assert.Equal(@"C:\docs\report.pdf", target.Target);
        }

        // ---- Normalization ----------------------------------------------------------------

        [Theory]
        [InlineData("  https://example.com/  ")]
        [InlineData("<https://example.com/>")]
        [InlineData("\"https://example.com/\"")]
        [InlineData("«https://example.com/»")]
        [InlineData("(https://example.com/)")]
        [InlineData("https://example.com/.")]
        [InlineData("https://example.com/,")]
        public void TheWrappingProseIsTrimmedOff(string selection)
            => Assert.Equal("https://example.com/", Parse(selection).Target);

        // ---- A target inside a wider selection --------------------------------------------

        /// <summary>
        /// A selection drawn with the mouse picks up its neighbours - the word next to the link, the
        /// sentence around it, the rest of the line - and in Chromium the accessibility stack hands
        /// back one node's worth of it. So a target is looked for word by word too, and the caption
        /// names exactly what will open.
        /// </summary>
        [Theory]
        [InlineData("см. https://example.com/ тут")]
        [InlineData("https://example.com/ и ещё текст")]
        [InlineData("первая строка\nhttps://example.com/\nтретья")]
        [InlineData("https://example.com/\tи табуляция")]
        public void ALinkIsFoundInsideAWiderSelection(string selection)
            => Assert.Equal("https://example.com/", Parse(selection).Target);

        /// <summary>Two candidates: the first one wins, and it is the one the caption shows.</summary>
        [Fact]
        public void TheFirstCandidateWinsAndIsWhatTheCaptionNames()
        {
            LaunchTarget target = Parse("https://first.example/ https://second.example/");
            Assert.Equal("https://first.example/", target.Target);
            Assert.Equal("https://first.example/", target.Display);
        }

        /// <summary>
        /// Word-by-word must not turn prose into a launch offer - which is the whole reason the
        /// bare-host rule needs a known TLD.
        /// </summary>
        [Theory]
        [InlineData("Пересобрал и перезапустил - в трее теперь 26.9.18.1916")]
        [InlineData("cmd /c del *.*")]
        [InlineData("см. файл readme.md в корне")]
        public void ProseWithNoTargetInItStaysRefused(string selection) => Refused(selection);

        /// <summary>
        /// A path with a space in it is found only by the whole-text pass: splitting on spaces
        /// cannot put it back together, and half of it must never be opened.
        /// </summary>
        [Fact]
        public void APathWithSpacesSurvivesAsAWholeButIsNeverHalvedByTheWordPass()
        {
            Assert.Equal(@"C:\Program Files\app",
                Parse(@"C:\Program Files\app", Disk(@"C:\Program Files\app")).Target);
            Refused(@"открой C:\Program Files\app потом", Disk(@"C:\Program Files\app"));
        }

        [Fact]
        public void AnOverlongSelectionIsRefused()
            => Refused("https://example.com/" + new string('a', LaunchTargets.MaxLength));

        [Fact]
        public void TheCaptionIsElidedInTheMiddleSoBothEndsSurvive()
        {
            string url = "https://example.com/" + new string('a', 120) + "/end";
            string display = Parse(url).Display;

            Assert.Equal(LaunchTargets.MaxDisplayChars, display.Length);
            Assert.StartsWith("https://", display);
            Assert.EndsWith("/end", display);
            Assert.Contains("..", display);
        }
    }
}
