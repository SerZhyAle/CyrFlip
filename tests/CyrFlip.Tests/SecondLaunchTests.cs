using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// APP-ACTIVATION rule 7C ("a second launch must not vanish"), ticket S0060 P7-n: a plain second
    /// launch of the exe reaches the running instance and opens its settings; the settings command is
    /// the application's, not the launcher module's, so it passes while the launcher is off; every
    /// other launcher command stays gated; the strict 256-character parser is unchanged.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class SecondLaunchTests
    {
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        // ---- what a second launch forwards ----

        [Fact]
        public void APlainLaunchAsksTheLiveInstanceForItsSettings()
        {
            // No arguments, and an argument that is not a command, are both a plain launch.
            Assert.Null(LauncherIpc.ParseCommand(new string[0]));
            Assert.Null(LauncherIpc.ParseCommand(new[] { @"C:\Users\x\notes.txt" }));
            Assert.Equal(LauncherIpc.SettingsCommand, Program.SecondLaunchCommand(null));
        }

        [Theory]
        [InlineData("/launcher-settings")]
        [InlineData("/exit")]
        [InlineData("/launcher-run:5f0c2a64-4f4c-4a0e-8d52-0d2b3e7c9a11")]
        public void AJumpListTaskForwardsItsOwnCommandUnchanged(string command)
            => Assert.Equal(command, Program.SecondLaunchCommand(command));

        [Fact]
        public void ARequestNobodyTookIsToldToTheUserExceptExit()
        {
            Assert.True(Program.SecondLaunchOwesAnAnswer(LauncherIpc.SettingsCommand, delivered: false));
            Assert.True(Program.SecondLaunchOwesAnAnswer(LauncherIpc.RunPrefix + Guid.NewGuid().ToString("D"), delivered: false));
            // Delivered: the live instance answers, the second process says nothing.
            Assert.False(Program.SecondLaunchOwesAnAnswer(LauncherIpc.SettingsCommand, delivered: true));
            // The build scripts send /exit unattended; a dialog there would hold a pipeline for a click.
            Assert.False(Program.SecondLaunchOwesAnAnswer(LauncherIpc.ExitCommand, delivered: false));
        }

        [Fact]
        public void TheSecondLaunchBranchStartsNoHookNoTrayAndNoSecondInstance()
        {
            string program = File.ReadAllText(Path.Combine(Source, "Program.cs"));
            int start = program.IndexOf("if (!isFirst)", StringComparison.Ordinal);
            Assert.True(start > 0, "the single-instance branch is not where the gate looks for it");
            int end = program.IndexOf("return;", start, StringComparison.Ordinal);
            string branch = program.Substring(start, end - start);

            Assert.Contains("LauncherIpc.TrySend(", branch);
            Assert.DoesNotContain("new CyrFlipContext", branch);
            Assert.DoesNotContain("KeyboardHook", branch);
            Assert.DoesNotContain("NotifyIcon", branch);
            Assert.DoesNotContain("Application.Run", branch);
        }

        // ---- what the live instance passes on ----

        [Theory]
        [InlineData("/exit", false, true)]
        [InlineData("/launcher-settings", false, true)]
        [InlineData("/launcher-run:5f0c2a64-4f4c-4a0e-8d52-0d2b3e7c9a11", false, false)]
        [InlineData("/exit", true, true)]
        [InlineData("/launcher-settings", true, true)]
        [InlineData("/launcher-run:5f0c2a64-4f4c-4a0e-8d52-0d2b3e7c9a11", true, true)]
        public void TheLauncherSwitchGatesOnlyTheLauncherCommands(string command, bool launcherOn, bool accepted)
            => Assert.Equal(accepted, LauncherIpc.IsAccepted(command, launcherOn));

        [Fact]
        public void TheSettingsCommandReachesAListenerWhoseLauncherIsOff()
        {
            string pipe = "CyrFlipTests_" + Guid.NewGuid().ToString("N");
            var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var settingsSeen = new ManualResetEventSlim();
            using var ipc = new LauncherIpc(cmd =>
            {
                received.Enqueue(cmd);
                if (cmd == LauncherIpc.SettingsCommand) settingsSeen.Set();
            }, pipe);
            ipc.SetLauncherCommandsEnabled(false);
            ipc.Start();

            // The same call Program makes for a plain second launch.
            bool sent = false;
            for (int attempt = 0; attempt < 20 && !sent; attempt++)
                sent = LauncherIpc.TrySend(Program.SecondLaunchCommand(null), 500, pipe);

            Assert.True(sent, "TrySend never connected");
            Assert.True(settingsSeen.Wait(TimeSpan.FromSeconds(5)), "the settings command was dropped while the launcher is off");
            Assert.Equal(new[] { LauncherIpc.SettingsCommand }, received.ToArray());
        }

        // ---- the parser is still strict ----

        [Theory]
        [InlineData("/launcher-settings now")]
        [InlineData("/launcher-settings:1")]
        [InlineData("launcher-settings")]
        [InlineData("/settings")]
        [InlineData("/open")]
        public void NothingLooseningTheSettingsCommandIsACommand(string line)
            => Assert.Null(LauncherIpc.ParseCommand(line));

        [Fact]
        public void TheOverlongLimitStillAppliesToTheSettingsCommand()
        {
            Assert.Equal(LauncherIpc.SettingsCommand, LauncherIpc.ParseCommand("  /LAUNCHER-SETTINGS  "));
            Assert.Null(LauncherIpc.ParseCommand(LauncherIpc.SettingsCommand + new string(' ', LauncherIpc.MaxCommandChars)));
        }

        [Fact]
        public void TheCurrentUserOnlyAclOfThePipeIsUntouched()
        {
            // The DACL is built in one place; this fails if the second-launch work widened it.
            string ipc = File.ReadAllText(Path.Combine(Source, "LauncherIpc.cs"));
            Assert.Contains("new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite, AccessControlType.Allow)", ipc);
            Assert.Contains("WellKnownSidType.NetworkSid", ipc);
            // Exactly two rules: the current user allowed, the network denied. No third one.
            Assert.Equal(2, Regex.Matches(ipc, @"new PipeAccessRule\(").Count);
        }
    }
}
