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

        /// <summary>
        /// Every probe seamed: the disk the test names, drives local unless listed, no on-disk
        /// renaming, Windows' own dangerous list empty (so our list is what is under test), and a
        /// local current directory.
        /// </summary>
        private static LaunchProbes Probes(Func<string, bool>? exists = null, string remoteDrives = "")
            => new LaunchProbes
            {
                Exists = exists ?? EmptyDisk,
                IsRemoteDrive = letter => remoteDrives.IndexOf(letter) >= 0,
                RealName = _ => null,
                IsDangerousExtension = _ => false,
                CurrentDirectory = () => @"C:\work",
            };

        /// <summary>A disk probe that fails the test if it is ever asked - for inputs that must never be probed.</summary>
        private static bool MustNotProbe(string path)
        {
            Assert.Fail("the file system was asked about a remote path: " + path);
            return false;
        }

        private static LaunchTarget Parse(string selection, Func<string, bool>? exists = null)
            => Parse(selection, Probes(exists));

        private static LaunchTarget Parse(string selection, LaunchProbes probes)
        {
            Assert.True(LaunchTargets.TryParse(selection, out LaunchTarget? target, probes),
                "expected a launch target for: " + selection);
            return target!;
        }

        private static void Refused(string? selection, Func<string, bool>? exists = null)
            => Assert.False(LaunchTargets.TryParse(selection, out _, Probes(exists)),
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
                Parse(@"\\server\share\report.pdf", MustNotProbe).Kind);

        /// <summary>
        /// S0008 LS-1: asking whether a remote path exists makes Windows contact that host - an SMB
        /// authentication to a server the author of the text chose, and a stall of every input
        /// event on the machine while it does not answer. Every spelling of "remote" is recognised
        /// from the text alone and comes back as a confirmed Program naming the full path.
        /// </summary>
        [Theory]
        [InlineData(@"\\10.255.255.1\x\y.pdf", @"\\10.255.255.1\x\y.pdf")]
        [InlineData(@"//host/share/doc.txt", @"\\host\share\doc.txt")]
        [InlineData("file://host/share/doc.txt", @"\\host\share\doc.txt")]
        [InlineData(@"\\?\UNC\host\share\doc.txt", @"\\host\share\doc.txt")]
        [InlineData(@"Z:\team\plan.docx", @"Z:\team\plan.docx")]
        public void ARemotePathIsNeverProbedAndAlwaysConfirmed(string selection, string expected)
        {
            LaunchTarget target = Parse(selection, Probes(MustNotProbe, remoteDrives: "Z"));
            Assert.Equal(LaunchKind.Program, target.Kind);
            Assert.Equal(expected, target.Target);
        }

        [Fact]
        public void ARemotePathInsideProseIsNotProbedEither()
            => Assert.Equal(LaunchKind.Program,
                Parse(@"see \\host\share\x.txt for details", Probes(MustNotProbe)).Kind);

        [Fact]
        public void ARootRelativePathOnANetworkCurrentDirectoryIsRemote()
        {
            LaunchProbes probes = Probes(MustNotProbe);
            probes.CurrentDirectory = () => @"\\host\share\folder";
            LaunchTarget target = Parse(@"\docs\a.pdf", probes);
            Assert.Equal(LaunchKind.Program, target.Kind);
            Assert.Equal(@"\\host\share\docs\a.pdf", target.Target);
        }

        /// <summary>"\\n" in a code sample is not a share; the device namespace is not a target at all.</summary>
        [Theory]
        [InlineData(@"\\n")]
        [InlineData(@"\\server")]
        [InlineData(@"\\.\PhysicalDrive0")]
        [InlineData(@"\\?\C:\tools\a.exe")]
        public void NeitherAHalfUncNorADevicePathIsATarget(string selection)
            => Assert.False(LaunchTargets.TryParse(selection, out _, Probes(MustNotProbe)));

        /// <summary>
        /// S0008 LS-3: Win32 strips trailing dots and spaces, so "x.exe. ." opens x.exe - and the
        /// extension check has to look at what opens, not at the text.
        /// </summary>
        [Fact]
        public void TrailingDotsAndSpacesDoNotHideAnExecutable()
        {
            LaunchTarget target = Parse(@"C:\t\x.exe. .", Disk(@"C:\t\x.exe"));
            Assert.Equal(LaunchKind.Program, target.Kind);
            Assert.Equal(@"C:\t\x.exe", target.Target);
        }

        [Theory]
        [InlineData(@"C:\t\help.chm")]
        [InlineData(@"C:\t\app.appref-ms")]
        [InlineData(@"C:\t\x.settingcontent-ms")]
        [InlineData(@"C:\t\disk.iso")]
        [InlineData(@"C:\t\pkg.msix")]
        [InlineData(@"C:\t\conn.rdp")]
        public void WhatShellExecuteRunsOrMountsIsAProgram(string path)
            => Assert.Equal(LaunchKind.Program, Parse(path, Disk(path)).Kind);

        [Fact]
        public void WindowsOwnDangerousListIsConsultedToo()
        {
            LaunchProbes probes = Probes(Disk(@"C:\t\a.newrisk"));
            probes.IsDangerousExtension = ext => ext == ".newrisk";
            Assert.Equal(LaunchKind.Program, Parse(@"C:\t\a.newrisk", probes).Kind);
        }

        /// <summary>An 8.3 alias hides the real extension; the real on-disk name decides.</summary>
        [Fact]
        public void TheRealOnDiskNameDecidesTheKind()
        {
            LaunchProbes probes = Probes(Disk(@"C:\t\APP~1.APP"));
            probes.RealName = _ => "app.appref-ms";
            LaunchTarget target = Parse(@"C:\t\APP~1.APP", probes);
            Assert.Equal(LaunchKind.Program, target.Kind);
            Assert.Equal(@"C:\t\app.appref-ms", target.Target);
        }

        /// <summary>S0008 LS-5: an invisible reordering character refuses the candidate outright.</summary>
        [Theory]
        [InlineData("C:\\t\\invoice\u202Efdp.exe")]
        [InlineData("https://example.com/\u200Fx")]
        [InlineData("https://exa\u2066mple.com/")]
        // S0034 LS2-1: percent-encoded, the override only appears once LocalPath unescapes it.
        [InlineData("file://attacker.test/s/invoice%E2%80%AEfdp.exe")]
        [InlineData("file:///C:/t/invoice%E2%80%AEfdp.exe")]
        public void ABidiControlCharacterRefusesTheCandidate(string selection)
            => Refused(selection, Disk("C:\\t\\invoice\u202Efdp.exe"));

        /// <summary>A file whose real on-disk name reads backwards is refused like the text that spells one.</summary>
        [Fact]
        public void ABidiControlInTheRealOnDiskNameRefusesTheCandidate()
        {
            LaunchProbes probes = Probes(Disk(@"C:\t\INVOIC~1.EXE"));
            probes.RealName = _ => "invoice\u202Efdp.exe";
            Assert.False(LaunchTargets.TryParse(@"C:\t\INVOIC~1.EXE", out _, probes));
        }

        /// <summary>
        /// S0034 LS2-4: a remote path is never probed, so the prose after it must not become part of
        /// the target - the word pass finds the path instead.
        /// </summary>
        [Theory]
        [InlineData(@"\\server\share\x.txt for details", @"\\server\share\x.txt")]
        [InlineData(@"Z:\plan.docx is final", @"Z:\plan.docx")]
        public void ProseAfterARemotePathIsNotPartOfTheTarget(string selection, string expected)
            => Assert.Equal(expected, Parse(selection, Probes(MustNotProbe, remoteDrives: "Z")).Target);

        /// <summary>...while spaces inside a share's folder or file names still make one path.</summary>
        [Theory]
        [InlineData(@"\\nas\My Videos\a.mp4")]
        [InlineData(@"\\nas\share\My File.docx")]
        public void SpacesInsideARemotePathKeepItWhole(string selection)
            => Assert.Equal(selection, Parse(selection, Probes(MustNotProbe)).Target);

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

        /// <summary>
        /// S0008 LS-5: the host is the part that says whose link it is, so it is never elided - the
        /// path gives up its middle instead, and a host longer than the caption is shown whole.
        /// </summary>
        [Fact]
        public void TheHostOfALongUrlIsNeverElided()
        {
            string host = "login.bank.example.com.attacker-controlled-domain.test";
            string display = Parse("https://" + host + "/" + new string('a', 80) + "/end").Display;
            Assert.StartsWith("https://" + host, display);

            string shortHost = "https://example.com/" + new string('b', 100);
            Assert.StartsWith("https://example.com..", Parse(shortHost).Display);
        }
    }
}
