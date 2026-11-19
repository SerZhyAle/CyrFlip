using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four state-switch verbs of ticket S0125: the manifest rows C41 to C44, the parser lines, the rule-13 gate,
    /// and the handlers over fake hosts and <see cref="KeepAwake"/>'s recorder seam - no context, no window, no config
    /// file and no real execution-state call. The cases that need a running tray copy, a real registry write, a real
    /// SPI_SETCURSORS reload or the live second-process paths - the tray items' own CheckedChanged wiring, the session
    /// timer's fire, a packaged build, the elevation answers - are <c>tools/uitest/Test-AppCommands.ps1</c>'s and the
    /// release's.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsStateSwitchesTests : IDisposable
    {
        private readonly List<string> _reloadSchemeLog = new List<string>();

        public AppCommandsStateSwitchesTests()
        {
            // The P/Invoke seam the keep-awake tests swap in: the machine's idle policy is never touched here.
            KeepAwake.SetExecutionState = _ => WindowInterop.EXECUTION_STATE.ES_CONTINUOUS;
            KeepAwake.Reset();
        }

        public void Dispose()
        {
            KeepAwake.Reset();
            KeepAwake.SetExecutionState = WindowInterop.SetThreadExecutionState;
        }

        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private sealed class FakeKeepAwakeHost : IKeepAwakeCommandsHost
        {
            public readonly List<(bool? system, bool? screen)> Persist = new List<(bool?, bool?)>();
            public readonly List<(bool? system, bool? screen, int minutes)> Sessions =
                new List<(bool?, bool?, int)>();
            public DateTime SessionEndsUtc = new DateTime(2026, 11, 17, 23, 0, 0, DateTimeKind.Utc);

            public void SetKeepAwakeSwitches(bool? system, bool? screen)
            {
                Persist.Add((system, screen));
                // The tray item's own path, applied: the live call (here KeepAwake itself, recorder-seamed).
                if (system.HasValue) KeepAwake.SetSystemAwake(system.Value);
                if (screen.HasValue) KeepAwake.SetScreenOn(screen.Value);
            }

            public DateTime BeginKeepAwakeSession(bool? system, bool? screen, int minutes)
            {
                Sessions.Add((system, screen, minutes));
                if (system.HasValue) KeepAwake.SetSystemAwake(system.Value);
                if (screen.HasValue) KeepAwake.SetScreenOn(screen.Value);
                return SessionEndsUtc;
            }
        }

        private sealed class FakeThemeHost : IThemeCommandsHost
        {
            public (ThemeMode mode, string token)? Set;
            public void SetTheme(ThemeMode mode, string token) => Set = (mode, token);
        }

        private sealed class FakeAutostartHost : IAutostartCommandsHost
        {
            public bool Managed;
            public Exception? ToThrow;
            public readonly List<bool> Set = new List<bool>();
            public bool AutostartManagedByWindows => Managed;
            public void SetAutostart(bool enabled)
            {
                if (ToThrow != null) throw ToThrow;
                Set.Add(enabled);
            }
        }

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheFourSpellingsAreInTheManifestWithTheRowsFields()
        {
            AppCommandSpec keepawake = AppCommandManifest.Shipped.Find("keepawake set")!;
            AppCommandSpec theme = AppCommandManifest.Shipped.Find("theme set")!;
            AppCommandSpec autostart = AppCommandManifest.Shipped.Find("autostart set")!;
            AppCommandSpec cursor = AppCommandManifest.Shipped.Find("cursor restore")!;

            Assert.Equal("safe", keepawake.Risk);
            Assert.Equal("refuse", keepawake.ColdStart);
            Assert.Equal(2000, keepawake.TimeoutMs);
            Assert.True(keepawake.Idempotent);
            Assert.Equal("bool", keepawake.Arg("system")!.Kind);
            Assert.False(keepawake.Arg("system")!.Required);
            Assert.Equal("bool", keepawake.Arg("screen")!.Kind);
            Assert.Equal("int", keepawake.Arg("minutes")!.Kind);

            Assert.Equal("safe", theme.Risk);
            Assert.Equal("refuse", theme.ColdStart);
            Assert.Equal(3000, theme.TimeoutMs);
            Assert.True(theme.Idempotent);
            Assert.True(theme.Arg("theme")!.Required);
            Assert.Equal("enum", theme.Arg("theme")!.Kind);
            Assert.Contains("dark", theme.Arg("theme")!.Values);

            Assert.Equal("writes-user-data", autostart.Risk);
            Assert.Equal("oneshot", autostart.ColdStart);
            Assert.Equal(5000, autostart.TimeoutMs);
            Assert.True(autostart.Idempotent);
            Assert.Single(autostart.OneOf);
            Assert.Equal(new[] { "on", "off" }, autostart.OneOf[0]);
            Assert.Equal("bool", autostart.Arg("on")!.Kind);

            Assert.Equal("writes-user-data", cursor.Risk);
            Assert.Equal("oneshot", cursor.ColdStart);
            Assert.Equal(5000, cursor.TimeoutMs);
            Assert.True(cursor.Idempotent);
            Assert.Null(cursor.Arg("system"));
        }

        [Theory]
        [InlineData("keepawake", "set", "--system", "true")]
        [InlineData("keepawake", "set", "--system")]
        [InlineData("keepawake", "set", "--screen", "false", "--system", "true")]
        [InlineData("keepawake", "set", "--screen", "--minutes", "480")]
        [InlineData("theme", "set", "--theme", "dark")]
        [InlineData("theme", "set", "--theme", "LIGHT")]
        [InlineData("autostart", "set", "--on")]
        [InlineData("autostart", "set", "--off")]
        [InlineData("cursor", "restore")]
        [InlineData("cursor", "restore", "--json")]
        public void TheParserTakesTheOptionLines(params string[] args)
        {
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(args).Kind);
        }

        [Theory]
        [InlineData("minutes not a number", "keepawake", "set", "--minutes", "abc")]
        [InlineData("switch not true/false", "keepawake", "set", "--system", "yes")]
        [InlineData("missing --theme", "theme", "set")]
        [InlineData("theme outside the three", "theme", "set", "--theme", "blue")]
        [InlineData("neither on nor off", "autostart", "set")]
        [InlineData("both on and off", "autostart", "set", "--on", "--off")]
        [InlineData("an unknown option", "cursor", "restore", "--zzz")]
        public void TheParserRefusesWhatTheRowsRefuse(string because, params string[] args)
        {
            Assert.Equal(AppCommandLineKind.Refused, AppCommandLine.Parse(args).Kind);
        }

        [Fact]
        public void TheRuleThirteenGateRefusesTheTwoWritesAndNeverTheTwoSafeVerbs()
        {
            AppCommandSpec keepawake = AppCommandManifest.Shipped.Find("keepawake set")!;
            AppCommandSpec theme = AppCommandManifest.Shipped.Find("theme set")!;
            AppCommandSpec autostart = AppCommandManifest.Shipped.Find("autostart set")!;
            AppCommandSpec cursor = AppCommandManifest.Shipped.Find("cursor restore")!;

            Assert.Null(AppCommandVerbs.RiskGate(keepawake, settingOn: false, legacy: false, T));
            Assert.Null(AppCommandVerbs.RiskGate(theme, settingOn: false, legacy: false, T));
            Assert.NotNull(AppCommandVerbs.RiskGate(autostart, settingOn: false, legacy: false, T));
            Assert.NotNull(AppCommandVerbs.RiskGate(cursor, settingOn: false, legacy: false, T));
        }

        // ------------------------------------------------------------------ keepawake set

        [Fact]
        public void ARequestThatNamesNoSwitchIsUsage()
        {
            var host = new FakeKeepAwakeHost();
            AppCommandResponse bare = AppCommandsStateSwitches.KeepAwakeSet(Inv("keepawake", "set"), host, T);
            Assert.Equal(AppCommandReasons.BadArgument, bare.Reason);
            Assert.Equal(AppCommandExit.Usage, bare.ExitCode);

            // A duration alone is not a switch: --minutes without --system or --screen is refused the same way.
            AppCommandResponse durationOnly =
                AppCommandsStateSwitches.KeepAwakeSet(Inv("keepawake", "set", "--minutes", "30"), host, T);
            Assert.Equal(AppCommandExit.Usage, durationOnly.ExitCode);

            Assert.Empty(host.Persist);
            Assert.Empty(host.Sessions);
        }

        [Fact]
        public void ThePersistedFormGoesThroughTheOneSetterAndAnswersTheCombinedState()
        {
            var host = new FakeKeepAwakeHost();
            AppCommandResponse system = AppCommandsStateSwitches.KeepAwakeSet(
                Inv("keepawake", "set", "--system", "true"), host, T);
            Assert.Equal(AppCommandExit.Done, system.ExitCode);
            Assert.Equal((true, null), host.Persist[0]);
            Assert.Equal(true, system.Data!["system"]);
            Assert.Equal(false, system.Data["screen"]);
            Assert.Equal("The computer will not sleep.", system.Message);
            Assert.False(system.Data.ContainsKey("expiresUtc"));

            AppCommandResponse both = AppCommandsStateSwitches.KeepAwakeSet(
                Inv("keepawake", "set", "--screen", "true"), host, T);
            Assert.Equal(true, both.Data!["system"]);
            Assert.Equal(true, both.Data["screen"]);
            Assert.Equal("The computer will not sleep and the screen will not turn off.", both.Message);

            AppCommandResponse off = AppCommandsStateSwitches.KeepAwakeSet(
                Inv("keepawake", "set", "--system", "false", "--screen", "false"), host, T);
            Assert.Equal("Both keep-awake switches are off.", off.Message);
            Assert.Equal(3, host.Persist.Count);
            Assert.Empty(host.Sessions);
        }

        [Fact]
        public void TheSessionFormAppliesLiveWritesNothingAndNamesTheEnd()
        {
            var host = new FakeKeepAwakeHost();
            AppCommandResponse session = AppCommandsStateSwitches.KeepAwakeSet(
                Inv("keepawake", "set", "--screen", "true", "--minutes", "90"), host, T);
            Assert.Equal(AppCommandExit.Done, session.ExitCode);
            Assert.Single(host.Sessions);
            Assert.Equal((null, true, 90), host.Sessions[0]);
            // The persisted path never ran: the config is untouched by a --minutes request.
            Assert.Empty(host.Persist);
            Assert.Equal(true, session.Data!["screen"]);
            Assert.Equal("2026-11-17T23:00:00Z", session.Data["expiresUtc"]);
            Assert.Contains("The switches return to their saved state after 90 minutes.", session.Message);
            // Idempotence is the answer's shape too: the same request again is the same answer.
            AppCommandResponse again = AppCommandsStateSwitches.KeepAwakeSet(
                Inv("keepawake", "set", "--screen", "true", "--minutes", "90"), host, T);
            Assert.Equal(AppCommandExit.Done, again.ExitCode);
            Assert.Equal(2, host.Sessions.Count);
        }

        [Theory]
        [InlineData("keepawake", "set", "--system", "true", "--minutes", "0")]
        [InlineData("keepawake", "set", "--system", "true", "--minutes", "481")]
        [InlineData("keepawake", "set", "--system", "true", "--minutes", "-5")]
        public void ABoundOutsideOneToFourHundredEightyIsUsage(params string[] args)
        {
            var host = new FakeKeepAwakeHost();
            AppCommandResponse answer = AppCommandsStateSwitches.KeepAwakeSet(Inv(args), host, T);
            Assert.Equal(AppCommandReasons.BadArgument, answer.Reason);
            Assert.Equal(AppCommandExit.Usage, answer.ExitCode);
            Assert.Empty(host.Sessions);
        }

        [Fact]
        public void EndSessionReturnsTheSavedStateWithoutANewRequest()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.SetScreenOn(true);
            AppCommandsStateSwitches.EndSession(persistedSystem: false, persistedScreen: true);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.True(KeepAwake.KeepScreenOn);

            // Both saved off: the return is the bare ES_CONTINUOUS of the startup's own restore.
            KeepAwake.SetSystemAwake(true);
            AppCommandsStateSwitches.EndSession(persistedSystem: false, persistedScreen: false);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
            Assert.Equal(WindowInterop.EXECUTION_STATE.ES_CONTINUOUS, KeepAwake.BuildMask());
        }

        // ------------------------------------------------------------------ theme set

        [Fact]
        public void ThemeSetWritesTheStoredTokenThroughTheChoosePath()
        {
            var host = new FakeThemeHost();
            AppCommandResponse dark = AppCommandsStateSwitches.ThemeSet(
                Inv("theme", "set", "--theme", "Dark"), host, T);
            Assert.Equal(AppCommandExit.Done, dark.ExitCode);
            Assert.Equal((ThemeMode.Dark, "dark"), host.Set);
            Assert.Equal("dark", dark.Data!["theme"]);

            AppCommandResponse system = AppCommandsStateSwitches.ThemeSet(
                Inv("theme", "set", "--theme", "system"), host, T);
            Assert.Equal((ThemeMode.System, "system"), host.Set);
            Assert.Equal("system", system.Data!["theme"]);
            Assert.True(system.Data.ContainsKey("theme"));
        }

        // ------------------------------------------------------------------ autostart set

        [Fact]
        public void AutostartSetWritesThePairAndAnswersTheNewState()
        {
            var host = new FakeAutostartHost();
            AppCommandResponse on = AppCommandsStateSwitches.AutostartSet(
                Inv("autostart", "set", "--on"), host, T);
            Assert.Equal(AppCommandExit.Done, on.ExitCode);
            Assert.Equal(new[] { true }, host.Set);
            Assert.Equal(true, on.Data!["enabled"]);
            Assert.Equal("CyrFlip will start with Windows.", on.Message);

            AppCommandResponse off = AppCommandsStateSwitches.AutostartSet(
                Inv("autostart", "set", "--off"), host, T);
            Assert.Equal(new[] { true, false }, host.Set);
            Assert.Equal(false, off.Data!["enabled"]);
            Assert.Equal("CyrFlip will not start with Windows.", off.Message);
        }

        [Fact]
        public void APackagedBuildIsManagedByWindowsAndWritesNothing()
        {
            var host = new FakeAutostartHost { Managed = true };
            AppCommandResponse answer = AppCommandsStateSwitches.AutostartSet(
                Inv("autostart", "set", "--on"), host, T);
            Assert.Equal(AppCommandReasons.ManagedByWindows, answer.Reason);
            Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
            Assert.Empty(host.Set);
        }

        [Fact]
        public void ARegistryFailureAnswersAndOpensNoDialog()
        {
            var host = new FakeAutostartHost { ToThrow = new UnauthorizedAccessException() };
            AppCommandResponse answer = AppCommandsStateSwitches.AutostartSet(
                Inv("autostart", "set", "--on"), host, T);
            Assert.Equal(AppCommandReasons.Error, answer.Reason);
            Assert.Equal(AppCommandExit.Failed, answer.ExitCode);
            Assert.Equal("The command failed. The log bundle on the About page has the details.", answer.Message);
        }

        // ------------------------------------------------------------------ cursor restore

        [Fact]
        public void CursorRestoreReloadsTheSchemeUnconditionallyAndIsIdempotent()
        {
            AppCommandResponse first = AppCommandsStateSwitches.CursorRestore(
                () => _reloadSchemeLog.Add("reload"), T);
            Assert.Equal(AppCommandExit.Done, first.ExitCode);
            Assert.Equal(true, first.Data!["restored"]);
            Assert.Equal("The default system cursors are reloaded.", first.Message);

            AppCommandResponse second = AppCommandsStateSwitches.CursorRestore(
                () => _reloadSchemeLog.Add("reload"), T);
            Assert.Equal(AppCommandExit.Done, second.ExitCode);
            Assert.Equal(true, second.Data!["restored"]);
            Assert.Equal(new[] { "reload", "reload" }, _reloadSchemeLog);
        }

        // ------------------------------------------------------------------ the log discipline

        [Fact]
        public void TheLogLineCarriesTheStateTokensAndNeitherDatesNorArguments()
        {
            AppCommandResponse session = AppCommandResponse.Done("The screen will not turn off.",
                new Dictionary<string, object>
                {
                    ["screen"] = true,
                    ["system"] = false,
                    ["expiresUtc"] = "2026-11-17T23:00:00Z",
                });
            string line = AppCommandsLog.Line(new DateTime(2026, 11, 17, 12, 0, 0), "keepawake set", "agent", 4321, session);
            Assert.Contains("keepawake set", line);
            Assert.Contains("system=false", line);
            Assert.Contains("screen=true", line);
            Assert.DoesNotContain("expiresUtc", line);
            Assert.DoesNotContain("23:00", line);

            AppCommandResponse theme = AppCommandResponse.Done("The theme is applied at once.",
                new Dictionary<string, object> { ["theme"] = "dark" });
            Assert.Contains("theme=dark", AppCommandsLog.Line(DateTime.Now, "theme set", "agent", 4321, theme));

            AppCommandResponse autostart = AppCommandResponse.Done("CyrFlip will start with Windows.",
                new Dictionary<string, object> { ["enabled"] = true });
            Assert.Contains("enabled=true", AppCommandsLog.Line(DateTime.Now, "autostart set", "agent", 4321, autostart));

            AppCommandResponse cursor = AppCommandResponse.Done("The default system cursors are reloaded.",
                new Dictionary<string, object> { ["restored"] = true });
            Assert.Contains("restored=true", AppCommandsLog.Line(DateTime.Now, "cursor restore", "agent", 4321, cursor));

            AppCommandResponse refused = AppCommandResponse.Refused(AppCommandReasons.ManagedByWindows,
                AppCommandExit.NotReady, "Windows itself owns startup.");
            Assert.Contains("managed-by-windows",
                AppCommandsLog.Line(DateTime.Now, "autostart set", "agent", 4321, refused));
        }
    }
}
