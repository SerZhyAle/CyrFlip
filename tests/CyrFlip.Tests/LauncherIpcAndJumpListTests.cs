using System;
using System.Collections.Generic;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The launcher command protocol (strict parsing - nothing unexpected ever becomes a command),
    /// a live named-pipe round trip on a test-private pipe name, and the pure Jump List task
    /// mapping (order, arguments, the trailing Manage/Exit pair).
    /// </summary>
    public class LauncherIpcAndJumpListTests
    {
        // ---- Command parsing ----

        [Fact]
        public void RunCommandParsesAndNormalizesTheGuid()
        {
            var id = Guid.NewGuid();
            string? command = LauncherIpc.ParseCommand(new[] { "/LAUNCHER-RUN:" + id.ToString("B").ToUpperInvariant() });
            Assert.Equal(LauncherIpc.RunPrefix + id.ToString("D"), command);
            Assert.Equal(id, LauncherIpc.RunId(command!));
        }

        [Theory]
        [InlineData("/launcher-settings")]
        [InlineData("/Launcher-Settings")]
        public void SettingsCommandParses(string arg)
            => Assert.Equal(LauncherIpc.SettingsCommand, LauncherIpc.ParseCommand(new[] { arg }));

        [Fact]
        public void ExitCommandParses()
            => Assert.Equal(LauncherIpc.ExitCommand, LauncherIpc.ParseCommand(new[] { "/exit" }));

        [Theory]
        [InlineData("/launcher-run:not-a-guid")]
        [InlineData("/launcher-run:")]
        [InlineData("/run:12345678-1234-1234-1234-123456789abc")] // OneClickRunner's own prefix is NOT ours
        [InlineData("--help")]
        [InlineData("C:\\some\\file.txt")]
        public void AnythingElseIsNotACommand(string arg)
            => Assert.Null(LauncherIpc.ParseCommand(new[] { arg }));

        [Fact]
        public void NoArgumentsIsNoCommand()
            => Assert.Null(LauncherIpc.ParseCommand(new string[0]));

        [Fact]
        public void RunIdOfANonRunCommandIsNull()
            => Assert.Null(LauncherIpc.RunId(LauncherIpc.SettingsCommand));

        // ---- Live pipe round trip (private pipe name so a running CyrFlip is never touched) ----

        [Fact]
        public void SendReachesTheListenerAndSurvivesAnEarlierBadConnection()
        {
            string pipe = "CyrFlipTests_" + Guid.NewGuid().ToString("N");
            string? received = null;
            using var done = new ManualResetEventSlim();
            using var ipc = new LauncherIpc(cmd => { received = cmd; done.Set(); }, pipe);
            ipc.Start();

            // A connection that sends nothing must not stop the listener (T0002).
            using (var empty = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.Out))
            {
                try { empty.Connect(2000); } catch { }
            }

            string command = LauncherIpc.RunPrefix + Guid.NewGuid().ToString("D");
            bool sent = false;
            for (int attempt = 0; attempt < 20 && !sent; attempt++)
                sent = LauncherIpc.TrySend(command, 500, pipe);

            Assert.True(sent, "TrySend never connected");
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "the listener never delivered the command");
            Assert.Equal(command, received);
        }

        [Fact]
        public void SendToNobodyFailsFast()
            => Assert.False(LauncherIpc.TrySend("/exit", 200, "CyrFlipTests_nobody_" + Guid.NewGuid().ToString("N")));

        // ---- Jump List task mapping (pure - no shell involved) ----

        [Fact]
        public void TasksMirrorTheScenarioOrderAndEndWithManageAndExit()
        {
            var first = new LauncherScenario { Name = "First", Path = "calc.exe" };
            var second = new LauncherScenario { Name = "Second", Path = "notepad.exe" };
            List<LauncherJumpList.TaskSpec> tasks = LauncherJumpList.BuildTasks(
                new[] { first, second }, @"C:\apps\CyrFlip.exe", ru => "T:" + ru);

            Assert.Equal(4, tasks.Count);
            Assert.Equal("First", tasks[0].Title);
            Assert.Equal(LauncherIpc.RunPrefix + first.Id.ToString("D"), tasks[0].Arguments);
            Assert.Equal("Second", tasks[1].Title);
            Assert.Equal(LauncherIpc.RunPrefix + second.Id.ToString("D"), tasks[1].Arguments);
            // The two fixed tasks are localized through the supplied translator and carry the
            // launcher protocol's exact commands.
            Assert.Equal("T:Управление сценариями...", tasks[2].Title);
            Assert.Equal(LauncherIpc.SettingsCommand, tasks[2].Arguments);
            Assert.Equal("T:Выход из CyrFlip", tasks[3].Title);
            Assert.Equal(LauncherIpc.ExitCommand, tasks[3].Arguments);
        }

        [Fact]
        public void NoScenariosStillYieldsManageAndExit()
        {
            List<LauncherJumpList.TaskSpec> tasks = LauncherJumpList.BuildTasks(
                new LauncherScenario[0], @"C:\apps\CyrFlip.exe", ru => ru);
            Assert.Equal(2, tasks.Count);
            Assert.Equal(LauncherIpc.SettingsCommand, tasks[0].Arguments);
            Assert.Equal(LauncherIpc.ExitCommand, tasks[1].Arguments);
        }

        [Fact]
        public void EveryTaskCarriesANonEmptyIconSource()
        {
            // T0021: no blank icons - even an unresolvable target falls back to the app icon.
            var strange = new LauncherScenario { Name = "odd", Path = "no-such-thing-anywhere.xyz" };
            List<LauncherJumpList.TaskSpec> tasks = LauncherJumpList.BuildTasks(
                new[] { strange }, @"C:\apps\CyrFlip.exe", ru => ru);
            foreach (LauncherJumpList.TaskSpec task in tasks)
                Assert.False(string.IsNullOrEmpty(task.IconPath), "blank icon for " + task.Title);
        }

        // ---- Hotkey binding snapshot ----

        [Fact]
        public void HookAcceptsBindingsAndIgnoresCorruptChords()
        {
            using var hook = new KeyboardHook(); // never installed - Update* only touches fields
            hook.UpdateLauncherHotkeys(new[]
            {
                (Guid.NewGuid(), "Ctrl+Alt+F9"),
                (Guid.NewGuid(), "not a chord"), // must stay inert, not become Ctrl+Shift+F12
                (Guid.NewGuid(), ""),
            });
            hook.UpdateLauncherHotkeys(new (Guid, string)[0]); // launcher off - empty snapshot
        }
    }
}
