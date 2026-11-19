using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four read-only verbs of ticket S0111, driven through their handlers with the inputs given: the scenario answer and
    /// the chord answer from arguments, the bundle into a temporary folder with a temporary log folder. No window, no tray,
    /// no layout is read - the Windows chord records are handed in as an empty list. The cold start's and the running copy's
    /// wiring is <c>tools/uitest/Test-SupportBundle.ps1</c>'s and the release's.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsInspectionTests : IDisposable
    {
        private readonly string _root;

        public AppCommandsInspectionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-inspect-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static string T(string en) => en;

        private static AppCommandInvocation Invoke(string verb, params string[] args)
        {
            AppCommandSpec spec = AppCommandManifest.Shipped.Find(verb)!;
            AppCommandLineResult parsed = AppCommandLine.ParseOptions(spec, args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static AppCommandResponse Chords(AppConfig config) =>
            AppCommandsInspection.ChordsList(config, new LauncherScenario[0], new LanguageHotkeys.Entry[0], "", T);

        private static Dictionary<string, object> ChordOfKind(AppCommandResponse answer, string kind)
        {
            var items = (object[])answer.Data!["chords"];
            return items.Cast<Dictionary<string, object>>().Single(c => (string)c["kind"] == kind);
        }

        private static string? Reason(AppCommandResponse answer, string kind) => (string?)ChordOfKind(answer, kind)["reason"];

        /// <summary>A log folder with one whitelisted log and one history file that must never reach an archive.</summary>
        private string LogsWithAHistoryFile()
        {
            string logs = Path.Combine(_root, "logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, SupportBundle.LogFiles[0]), "one line of diagnostics\r\n");
            File.WriteAllText(Path.Combine(logs, "clipboard-history.log"), "SECRET-HISTORY-PAYLOAD\r\n");
            return logs;
        }

        [Fact]
        public void TheFourVerbsAreInTheManifestWithTheirRiskAndColdStart()
        {
            var expected = new[]
            {
                ("scenario list", "reads-user-data"),
                ("chords list", "safe"),
                ("diagnostics paths", "safe"),
                ("diagnostics bundle", "reads-user-data"),
            };
            foreach ((string verb, string risk) in expected)
            {
                AppCommandSpec? spec = AppCommandManifest.Shipped.Find(verb);
                Assert.NotNull(spec);
                Assert.Equal(risk, spec!.Risk);
                Assert.Equal("oneshot", spec.ColdStart);
            }
        }

        // ------------------------------------------------------------------ scenario list

        [Fact]
        public void ScenarioListNamesEachScenarioButNeverItsProgramOrItsArguments()
        {
            var scenario = new LauncherScenario
            {
                Name = "Calc",
                Path = @"C:\Tools\secret-tool.exe",
                Arguments = "--token SECRET-ARGUMENT",
                Hotkey = "Ctrl+Shift+F8",
                RunAsAdmin = true,
            };

            AppCommandResponse answer = AppCommandsInspection.ScenarioList(new[] { scenario }, T);

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            var items = (object[])answer.Data!["scenarios"];
            var entry = Assert.IsType<Dictionary<string, object>>(items[0]);
            Assert.Equal(5, entry.Count);
            Assert.Equal("Calc", entry["name"]);
            Assert.Equal("program", entry["kind"]);
            Assert.Equal(true, entry["runAsAdmin"]);
            Assert.Equal(true, entry["hasHotkey"]);
            string json = AppCommandProtocol.DataJson(answer);
            Assert.DoesNotContain("SECRET-ARGUMENT", json);
            Assert.DoesNotContain("secret-tool", json);
        }

        [Theory]
        [InlineData(@"C:\Tools\app.exe", LauncherScenarioType.Executable, "program")]
        [InlineData(@"C:\Scripts\run.ps1", LauncherScenarioType.Executable, "script")]
        [InlineData(@"C:\Scripts\run.cmd", LauncherScenarioType.Executable, "script")]
        [InlineData("", LauncherScenarioType.YtDlp, "yt-dlp")]
        public void ScenarioKindNamesAProgramAScriptOrADownload(string path, LauncherScenarioType type, string kind)
        {
            var scenario = new LauncherScenario { Path = path, Type = type };
            Assert.Equal(kind, AppCommandsInspection.ScenarioKind(scenario));
        }

        [Fact]
        public void ScenarioListWithTheLauncherSwitchedOffIsExitElevenAndReadsNothing()
        {
            var config = new AppConfig { EnableScenarioLauncher = false };

            AppCommandResponse answer = AppCommandsInspection.Run(Invoke("scenario list"), config, _root, DateTime.Now, T, _root);

            Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, answer.Reason);
        }

        // ------------------------------------------------------------------ chords list

        [Fact]
        public void AFixedChordThatFiresSaysSoWithoutAReason()
        {
            var config = new AppConfig();

            AppCommandResponse answer = Chords(config);

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Dictionary<string, object> row = ChordOfKind(answer, "Case");
            Assert.Equal(config.CaseHotkey, row["chord"]);
            Assert.Equal(true, row["enabled"]);
            Assert.Equal(true, row["live"]);
            Assert.Null(row["reason"]);
        }

        [Fact]
        public void EachReasonTokenIsTheOneTheOverviewGives()
        {
            Assert.Equal("own-switch", Reason(Chords(new AppConfig { EnableCaseHotkey = false }), "Case"));
            Assert.Equal("master-off", Reason(Chords(new AppConfig { EnableHotkeys = false }), "Case"));
            Assert.Equal("no-chord", Reason(Chords(new AppConfig { CaseHotkey = "" }), "Case"));
            Assert.Equal("unparsable", Reason(Chords(new AppConfig { CaseHotkey = "Ctrl+Shift+Hyper+F12" }), "Case"));
            Assert.Equal("module-off", Reason(Chords(new AppConfig { EnableQuickNotes = false }), "QuickNotes"));
        }

        [Fact]
        public void AChordThatIsSwitchedOffReadsNotLive()
        {
            Dictionary<string, object> row = ChordOfKind(Chords(new AppConfig { EnableCaseHotkey = false }), "Case");
            Assert.Equal(false, row["enabled"]);
            Assert.Equal(false, row["live"]);
        }

        [Fact]
        public void TwoChordsOnOneKeyFireAndSayClash()
        {
            var config = new AppConfig { CaseHotkey = "Ctrl+Shift+F10", ClipboardHistoryHotkey = "Ctrl+Shift+F10" };

            Dictionary<string, object> row = ChordOfKind(Chords(config), "Case");

            Assert.Equal(true, row["live"]);
            Assert.Equal("clash", row["reason"]);
        }

        [Fact]
        public void AConversionRowWithoutAUsableKlidIsIncomplete()
        {
            var config = new AppConfig();
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            {
                SourceKlid = "0409",
                TargetKlid = "00000419",
                Hotkey = "Ctrl+Alt+F1",
                Enabled = true,
            });

            Dictionary<string, object> row = ChordOfKind(Chords(config), "Conversion");

            Assert.Equal("incomplete", row["reason"]);
            Assert.Equal(false, row["live"]);
        }

        // ------------------------------------------------------------------ diagnostics paths

        [Fact]
        public void DiagnosticsPathsNamesTheThreeFoldersFromTheirOwners()
        {
            AppCommandResponse answer = AppCommandsInspection.DiagnosticsPaths(T);

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            Assert.Equal(DataFolder.Current, answer.Data!["dataFolder"]);
            Assert.Equal(LauncherScenarioStore.DefaultFolder, answer.Data["scenariosFolder"]);
            Assert.Equal(SupportBundle.ReportsDirectory, answer.Data["reportsFolder"]);
        }

        // ------------------------------------------------------------------ diagnostics bundle

        [Fact]
        public void TheBundleGoesToAFolderUnderItsMomentNameAndNeverCarriesTheHistory()
        {
            string logs = LogsWithAHistoryFile();
            string folder = Path.Combine(_root, "out");
            Directory.CreateDirectory(folder);

            AppCommandResponse answer = AppCommandsInspection.DiagnosticsBundle(folder + "\\", false, new AppConfig(), _root,
                DateTime.Now, logs, T);

            Assert.Equal(AppCommandExit.Done, answer.ExitCode);
            string path = (string)answer.Data!["path"];
            Assert.StartsWith("CyrFlip-logs-", Path.GetFileName(path));
            Assert.True(File.Exists(path));
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                var names = zip.Entries.Select(e => e.FullName).ToList();
                Assert.Contains(SupportBundle.LogFiles[0], names);
                Assert.DoesNotContain("clipboard-history.log", names);
            }
            Assert.Empty(Directory.GetFiles(folder, "*.part"));
        }

        [Fact]
        public void AnExistingFileIsRefusedUntilOverwriteIsGiven()
        {
            string logs = LogsWithAHistoryFile();
            string target = Path.Combine(_root, "bundle.zip");
            File.WriteAllText(target, "old");
            var config = new AppConfig();

            AppCommandResponse refused = AppCommandsInspection.DiagnosticsBundle(target, false, config, _root, DateTime.Now, logs, T);
            Assert.Equal(AppCommandExit.NotReady, refused.ExitCode);
            Assert.Equal(AppCommandReasons.Exists, refused.Reason);
            Assert.Equal("old", File.ReadAllText(target));

            AppCommandResponse replaced = AppCommandsInspection.DiagnosticsBundle(target, true, config, _root, DateTime.Now, logs, T);
            Assert.Equal(AppCommandExit.Done, replaced.ExitCode);
            Assert.True(new FileInfo(target).Length > 4);
        }

        [Fact]
        public void AMissingFolderIsRefusedAndIsNotCreated()
        {
            string logs = LogsWithAHistoryFile();
            string missing = Path.Combine(_root, "missing");

            AppCommandResponse answer = AppCommandsInspection.DiagnosticsBundle(Path.Combine(missing, "bundle.zip"), false,
                new AppConfig(), _root, DateTime.Now, logs, T);

            Assert.Equal(AppCommandExit.NotReady, answer.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, answer.Reason);
            Assert.False(Directory.Exists(missing));
        }

        [Fact]
        public void AWindowsRefusedNameIsExitTen()
        {
            AppCommandResponse answer = AppCommandsInspection.DiagnosticsBundle(Path.Combine(_root, "bad<name>.zip"), false,
                new AppConfig(), _root, DateTime.Now, _root, T);

            Assert.Equal(AppCommandExit.Usage, answer.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, answer.Reason);
        }

        [Fact]
        public void OnlyTheDiskFullErrorsAreExitThirteen()
        {
            Assert.True(AppCommandsInspection.IsDiskFull(new IOException("full", unchecked((int)0x80070070))));
            Assert.True(AppCommandsInspection.IsDiskFull(new IOException("full", unchecked((int)0x80070027))));
            Assert.False(AppCommandsInspection.IsDiskFull(new IOException("locked", unchecked((int)0x80070020))));
            Assert.False(AppCommandsInspection.IsDiskFull(new UnauthorizedAccessException()));
        }
    }
}
