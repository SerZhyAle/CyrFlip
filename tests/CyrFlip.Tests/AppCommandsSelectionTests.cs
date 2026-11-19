using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four selection verbs of ticket S0129: the manifest rows C34-C37 with item Q's naming options and the
    /// <c>focused-window</c> target member, the parser lines, the window matcher, the raise and its refusal, and the
    /// handlers over fake hosts and the real notes service - no context, no window, no keyboard and no clipboard. The
    /// cases that need a live desktop (a real wrong-layout selection in a background window, a live Ollama, the raise
    /// against a real foreground) are the owner's desk runs and the release's (<c>tools/uitest/Test-AppCommands.ps1</c>).
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class AppCommandsSelectionTests : IDisposable
    {
        private readonly string _root;
        private readonly string _path;

        public AppCommandsSelectionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-selectioncmd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, QuickNotesStore.FileName);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static AppCommandLineResult Parse(params string[] args) => AppCommandLine.Parse(args);

        // ------------------------------------------------------------------ the fake

        /// <summary>
        /// The host is the verb's only seam, so whatever it records is everything the verb did: no window, no synthesized
        /// key and no clipboard call can hide behind anything.
        /// </summary>
        private sealed class FakeSelectionHost : ISelectionCommandsHost
        {
            public readonly List<SelectionWindow> Windows = new List<SelectionWindow>();
            public bool RaiseOk = true;
            public int RaiseCalls;
            public IntPtr RaisedWindow;
            public string UiLanguage { get; set; } = "ru";
            public readonly List<LayoutEntry> Layouts = new List<LayoutEntry>();
            public bool StoredConvertSymbols { get; set; } = true;
            public bool StoredFlipCapsLock { get; set; }
            public bool ClipboardBusy;
            public int Claims, Releases;
            public ClipboardHandler.FlipResult ConvertAnswer = ClipboardHandler.FlipResult.Flipped;
            public (string from, string to, bool symbols)? ConvertCall;
            public ClipboardHandler.FlipResult FlipAnswer = ClipboardHandler.FlipResult.Flipped;
            public bool? FlipSync;
            public ClipboardHandler.CaptureResult CaptureAnswer = ClipboardHandler.CaptureResult.Captured;
            public string CaptureText = "ghbdtn";
            public readonly List<int> CaptureCaps = new List<int>();
            public string LayoutCode = "ru";
            public bool RejectModelClaim;
            public int ModelClaims, ModelReleases;
            public TranslationResult? Result;
            public readonly List<(string text, string code)> ModelCalls = new List<(string, string)>();

            public IReadOnlyList<SelectionWindow> TopLevelWindows() => Windows;

            public bool BringToFront(IntPtr window)
            {
                RaiseCalls++;
                RaisedWindow = window;
                return RaiseOk;
            }

            public string LayoutCodeOf(IntPtr window) => LayoutCode;

            public IReadOnlyList<LayoutEntry> InstalledLayouts() => Layouts;

            public bool BeginClipboardWork()
            {
                if (ClipboardBusy) return false;
                Claims++;
                return true;
            }

            public void EndClipboardWork() => Releases++;

            public ClipboardHandler.FlipResult ConvertSelection(string fromKlid, string toKlid, bool convertSymbols)
            {
                ConvertCall = (fromKlid, toKlid, convertSymbols);
                return ConvertAnswer;
            }

            public ClipboardHandler.FlipResult FlipCaseSelection(bool syncCapsAfter)
            {
                FlipSync = syncCapsAfter;
                return FlipAnswer;
            }

            public ClipboardHandler.CaptureResult TakeSelection(out string text, int maxChars)
            {
                CaptureCaps.Add(maxChars);
                text = CaptureAnswer == ClipboardHandler.CaptureResult.Captured ? CaptureText : "";
                return CaptureAnswer;
            }

            public bool BeginCommandTranslation()
            {
                if (RejectModelClaim) return false;
                ModelClaims++;
                return true;
            }

            public void EndCommandTranslation() => ModelReleases++;

            public Task<TranslationResult> TranslateAsync(string text, string targetCode, CancellationToken ct)
            {
                ModelCalls.Add((text, targetCode));
                return Task.FromResult(Result ?? new TranslationResult { Status = TranslationStatus.Ok, Text = "привет", Model = "aya-expanse:8b" });
            }
        }

        /// <summary>Two installed layouts the matcher can name: US English and Russian, with their real KLIDs.</summary>
        private static void TwoLayouts(FakeSelectionHost host)
        {
            host.Layouts.Add(new LayoutEntry(new IntPtr(0x04090409), "00000409", "en", "US"));
            host.Layouts.Add(new LayoutEntry(new IntPtr(0x04190419), "00000419", "ru", "Russia"));
        }

        /// <summary>Three windows of two programs, the shape a busy desktop gives the matcher.</summary>
        private static FakeSelectionHost Desktop()
        {
            var host = new FakeSelectionHost();
            host.Windows.Add(new SelectionWindow(new IntPtr(11), 100, "Notepad"));
            host.Windows.Add(new SelectionWindow(new IntPtr(12), 200, "Report - Notepad"));
            host.Windows.Add(new SelectionWindow(new IntPtr(13), 200, "Notepad"));
            return host;
        }

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheFourRowsAreInTheManifestWithTheRowsFieldsAndTheTargetMember()
        {
            foreach (string verb in new[] { "selection convert", "selection flip-case", "selection note", "selection translate" })
            {
                AppCommandSpec spec = AppCommandManifest.Shipped.Find(verb)!;
                Assert.NotNull(spec);
                Assert.Equal(verb == "selection translate" ? "leaves-machine" : "writes-user-data", spec.Risk);
                Assert.Equal("refuse", spec.ColdStart);
                Assert.Equal("done", spec.Wait);
                int bound = verb == "selection translate" ? 130000 : 15000;
                Assert.Equal(bound, spec.TimeoutMs);
                Assert.Equal("int", spec.Arg("window-pid")!.Kind);
                Assert.Equal("string", spec.Arg("window-title")!.Kind);
            }

            // Item Q's member rides the manifest itself: every command of the family is a focused-window verb.
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                Encoding.UTF8.GetString(AppCommandManifest.Shipped.Raw));
            foreach (Dictionary<string, object> command in (object[])root["commands"])
            {
                string name = (string)command["name"];
                if (!(name == "selection convert" || name == "selection flip-case"
                      || name == "selection note" || name == "selection translate")) continue;
                Assert.True(command.TryGetValue("target", out object? target) && (string)target == "focused-window",
                    name + " has no target: \"focused-window\" member");
            }

            AppCommandSpec convert = AppCommandManifest.Shipped.Find("selection convert")!;
            Assert.True(convert.Arg("from")!.Required);
            Assert.True(convert.Arg("to")!.Required);
            AppCommandSpec translate = AppCommandManifest.Shipped.Find("selection translate")!;
            Assert.True(translate.Arg("to")!.Required);
        }

        [Fact]
        public void TheParserRefusesANonNumberPidAndAMissingRequiredSide()
        {
            Assert.Equal(AppCommandLineKind.Refused, Parse("selection flip-case", "--window-pid", "not-a-number").Kind);
            // The naming options are a pair the handler judges: both given is the narrowing case, neither is refused
            // by the handler with its own sentence, not by the parser.
            Assert.Equal(AppCommandLineKind.Command, Parse("selection flip-case", "--window-pid", "100",
                "--window-title", "notepad").Kind);
            Assert.Equal(AppCommandLineKind.Refused, Parse("selection convert", "--from", "en").Kind);
            Assert.Equal(AppCommandLineKind.Refused, Parse("selection translate", "--window-title", "notepad").Kind);
        }

        // ------------------------------------------------------------------ the window matcher

        [Fact]
        public void TheMatcherAnswersItemQsTwoRefusalsAndNarrowsPidWithTitle()
        {
            FakeSelectionHost host = Desktop();
            // No match: 11 target-missing.
            AppCommandResponse? refusal = null;
            IntPtr none = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case", "--window-pid", "999"), T, out refusal);
            Assert.Equal(IntPtr.Zero, none);
            Assert.Equal("target-missing", refusal!.Reason);
            Assert.Equal(11, refusal.ExitCode);
            // More than one: 10, and the message says how to be more exact.
            IntPtr many = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case", "--window-title", "notepad"), T, out refusal);
            Assert.Equal(IntPtr.Zero, many);
            Assert.Equal("bad-argument", refusal!.Reason);
            Assert.Equal(10, refusal.ExitCode);
            Assert.Contains("pid", refusal.Message);
            // One by pid.
            IntPtr byPid = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case", "--window-pid", "100"), T, out refusal);
            Assert.Equal(new IntPtr(11), byPid);
            Assert.Null(refusal);
            // A pid and a title together narrow each other, without regard to case.
            IntPtr narrow = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case", "--window-pid", "200",
                "--window-title", "REPORT"), T, out refusal);
            Assert.Equal(new IntPtr(12), narrow);
            Assert.Null(refusal);
            // Neither option given.
            IntPtr neither = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case"), T, out refusal);
            Assert.Equal(IntPtr.Zero, neither);
            Assert.Equal("bad-argument", refusal!.Reason);
            // An empty title.
            IntPtr empty = AppCommandsSelection.Named(host.Windows, Inv("selection flip-case", "--window-title", " "), T, out refusal);
            Assert.Equal(IntPtr.Zero, empty);
            Assert.Equal("bad-argument", refusal!.Reason);
        }

        // ------------------------------------------------------------------ the gates

        [Fact]
        public void TheRuleThirteenGateRefusesTheFamilyBeforeAnythingElse()
        {
            AppCommandResponse? refusal = AppCommandVerbs.RiskGate(AppCommandManifest.Shipped.Find("selection convert")!,
                settingOn: false, legacy: false, T);
            Assert.Equal("switch-off", refusal!.Reason);
            Assert.Equal(12, refusal.ExitCode);
            refusal = AppCommandVerbs.RiskGate(AppCommandManifest.Shipped.Find("selection translate")!,
                settingOn: false, legacy: false, T);
            Assert.Equal("switch-off", refusal!.Reason);
        }

        [Fact]
        public async Task AnOffTranslatorModuleAnswersModuleOffBeforeAnythingIsAsked()
        {
            AppCommandResponse answer = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), null, T, CancellationToken.None);
            Assert.Equal("module-off", answer.Reason);
            Assert.Equal(11, answer.ExitCode);
        }

        [Fact]
        public async Task AnOffNotesModuleAnswersModuleOffBeforeAnythingIsAsked()
        {
            FakeSelectionHost host = Desktop();
            AppCommandResponse answer = await AppCommandsSelection.SelectionNote(
                Inv("selection note", "--window-pid", "100"), host, null, T, CancellationToken.None);
            Assert.Equal("module-off", answer.Reason);
            Assert.Equal(11, answer.ExitCode);
            Assert.Equal(0, host.RaiseCalls);
        }

        [Fact]
        public async Task ATakenClipboardGuardAnswersBusyBeforeAWindowIsTouched()
        {
            FakeSelectionHost host = Desktop();
            host.ClipboardBusy = true;
            AppCommandResponse answer = await AppCommandsSelection.SelectionFlipCase(
                Inv("selection flip-case", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("busy", answer.Reason);
            Assert.Equal(11, answer.ExitCode);
            Assert.Equal(0, host.RaiseCalls);
            Assert.Null(host.FlipSync);
        }

        [Fact]
        public async Task AWindowThatWillNotComeToFrontAnswersWindowNotForeground()
        {
            FakeSelectionHost host = Desktop();
            host.RaiseOk = false;
            AppCommandResponse answer = await AppCommandsSelection.SelectionFlipCase(
                Inv("selection flip-case", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("window-not-foreground", answer.Reason);
            Assert.Equal(11, answer.ExitCode);
            Assert.Equal(1, host.RaiseCalls);
            Assert.Equal(1, host.Releases); // the claim is released, whatever the refusal was
        }

        // ------------------------------------------------------------------ the two edit verbs

        [Fact]
        public async Task SelectionConvertRunsThePipelineWithTheNamedLayoutsAndTheStoredSymbols()
        {
            FakeSelectionHost host = Desktop();
            TwoLayouts(host);
            AppCommandResponse answer = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal(0, answer.ExitCode);
            Assert.True((bool)answer.Data!["changed"]);
            Assert.Equal(("00000409", "00000419", true), host.ConvertCall);
            Assert.Equal(1, host.Releases);

            host.StoredConvertSymbols = false;
            AppCommandResponse second = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(("00000409", "00000419", false), host.ConvertCall);
        }

        [Fact]
        public async Task SelectionConvertAnswersNothingConvertedAndTheLayoutRefusalsOfTextConvert()
        {
            FakeSelectionHost host = Desktop();
            TwoLayouts(host);
            host.ConvertAnswer = ClipboardHandler.FlipResult.NoChange;
            AppCommandResponse answer = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal(0, answer.ExitCode);
            Assert.False((bool)answer.Data!["changed"]);

            // The same layout on both sides: text convert's own sentence.
            AppCommandResponse same = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "en", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("bad-argument", same.Reason);
            Assert.Equal(10, same.ExitCode);
            Assert.Contains("Name two different layouts", same.Message);

            // A value that names no layout at all.
            AppCommandResponse nothing = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "xx", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("bad-argument", nothing.Reason);
            Assert.Equal(10, nothing.ExitCode);

            // A layout Windows knows but this PC has not, outside the built-in pair.
            AppCommandResponse absent = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "0000041a", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("bad-argument", absent.Reason);
            Assert.Contains("is not installed on this PC", absent.Message);
        }

        [Fact]
        public async Task SelectionFlipCasePassesTheStoredCapsPreferenceAndAnswersChanged()
        {
            FakeSelectionHost host = Desktop();
            AppCommandResponse answer = await AppCommandsSelection.SelectionFlipCase(
                Inv("selection flip-case", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal(0, answer.ExitCode);
            Assert.True((bool)answer.Data!["changed"]);
            Assert.False(host.FlipSync!.Value);

            host.StoredFlipCapsLock = true;
            host.FlipAnswer = ClipboardHandler.FlipResult.NoChange;
            AppCommandResponse second = await AppCommandsSelection.SelectionFlipCase(
                Inv("selection flip-case", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal(0, second.ExitCode);
            Assert.False((bool)second.Data!["changed"]);
            Assert.True(host.FlipSync!.Value);
        }

        [Fact]
        public async Task TheCaptureTableIsAnsweredPerOutcome()
        {
            FakeSelectionHost host = Desktop();
            host.FlipAnswer = ClipboardHandler.FlipResult.NoSelection;
            AppCommandResponse nothing = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("no-selection", nothing.Reason);
            Assert.Equal(11, nothing.ExitCode);

            host.FlipAnswer = ClipboardHandler.FlipResult.TooLarge;
            AppCommandResponse tooLarge = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("too-large", tooLarge.Reason);
            Assert.Equal(10, tooLarge.ExitCode);

            host.FlipAnswer = ClipboardHandler.FlipResult.Cancelled;
            AppCommandResponse cancelled = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("error", cancelled.Reason);
            Assert.Equal(1, cancelled.ExitCode);
            Assert.Contains("lost the focus", cancelled.Message);

            host.FlipAnswer = ClipboardHandler.FlipResult.Failed;
            AppCommandResponse failed = await AppCommandsSelection.SelectionConvert(
                Inv("selection convert", "--from", "en", "--to", "ru", "--window-pid", "100"),
                host, T, CancellationToken.None);
            Assert.Equal("error", failed.Reason);
            Assert.Equal(1, failed.ExitCode);
        }

        // ------------------------------------------------------------------ the translate verb

        [Fact]
        public async Task SelectionTranslateResolvesUiAndActiveAndCutsAtTheServicesCap()
        {
            FakeSelectionHost host = Desktop();
            host.UiLanguage = "uk";
            host.LayoutCode = "ru";
            host.CaptureText = new string('a', 5000);
            AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ui", "--window-pid", "100"), host, T, CancellationToken.None).Wait();
            Assert.Equal("uk", host.ModelCalls[0].code);
            Assert.Equal(TranslationService.MaxChars, host.ModelCalls[0].text.Length);

            AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "active", "--window-pid", "100"), host, T, CancellationToken.None).Wait();
            Assert.Equal("ru", host.ModelCalls[1].code);

            AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "de", "--window-title", "notepad"), host, T, CancellationToken.None).Wait();
            Assert.Equal("de", host.ModelCalls[2].code);
            Assert.Equal(3, host.Claims);
            Assert.Equal(3, host.Releases);
            Assert.Equal(3, host.ModelClaims);
            Assert.Equal(3, host.ModelReleases);
        }

        [Fact]
        public async Task SelectionTranslateAnswersTheCutAndTheOutcomeTable()
        {
            FakeSelectionHost host = Desktop();
            host.CaptureText = new string('a', 5000);
            AppCommandResponse answer = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal(0, answer.ExitCode);
            Assert.Equal("привет", (string)answer.Data!["text"]);
            Assert.Equal("aya-expanse:8b", (string)answer.Data!["model"]);
            Assert.False((bool)answer.Data!["partial"]);
            Assert.True((bool)answer.Data!["truncated"]);
            Assert.Contains("covers the first", answer.Message);

            host.Result = new TranslationResult { Status = TranslationStatus.NoModel };
            AppCommandResponse noModel = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("no-model", noModel.Reason);
            Assert.Equal(11, noModel.ExitCode);

            host.Result = new TranslationResult { Status = TranslationStatus.NotRunning };
            AppCommandResponse network = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("network", network.Reason);
            Assert.Equal(15, network.ExitCode);
        }

        [Fact]
        public async Task SelectionTranslateAnswersBusyOnASharedClaimAndRefusesAnAnswerThatWillNotFit()
        {
            FakeSelectionHost host = Desktop();
            host.RejectModelClaim = true;
            AppCommandResponse busy = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("busy", busy.Reason);
            Assert.Equal(11, busy.ExitCode);

            host.RejectModelClaim = false;
            host.Result = new TranslationResult { Status = TranslationStatus.Ok, Text = new string('в', 70000) };
            AppCommandResponse huge = await AppCommandsSelection.SelectionTranslate(
                Inv("selection translate", "--to", "ru", "--window-pid", "100"), host, T, CancellationToken.None);
            Assert.Equal("response-too-large", huge.Reason);
        }

        [Fact]
        public void TheSelectionCommandLineNamesTheTargetAndTheCutNeverTheText()
        {
            string line = AppCommandsSelection.SelectionCommandLine("ru", truncated: true);
            Assert.Contains("into ru", line);
            Assert.Contains("selection command", line);
            Assert.Contains("cut at " + TranslationService.MaxChars, line);
            Assert.DoesNotContain("ghbdtn", line);
        }

        // ------------------------------------------------------------------ the note verb

        /// <summary>Built with no synchronization context, so the service's owner-thread posts run inline.</summary>
        private QuickNotesService Service(bool loadNow = true)
        {
            var store = new QuickNotesStore(_path, new FakeCipher());
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new QuickNotesService(store, loadNow); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        [Fact]
        public async Task SelectionNoteSavesTheSelectionThroughTheServiceAndAnswersTheNoteId()
        {
            FakeSelectionHost host = Desktop();
            host.CaptureText = "the captured selection";
            QuickNotesService service = Service();
            AppCommandResponse answer = await AppCommandsSelection.SelectionNote(
                Inv("selection note", "--window-pid", "100"), host, service, T, CancellationToken.None);
            Assert.Equal(0, answer.ExitCode);
            string id = (string)answer.Data!["noteId"];
            Assert.True(Guid.TryParse(id, out Guid parsed) && parsed != Guid.Empty);
            Assert.Equal("the captured selection", service.Search(null!).Single().Text);
            // The capture claim is released before the write, and the capture cap is the notes' own body cap.
            Assert.Equal(1, host.Claims);
            Assert.Equal(1, host.Releases);
            Assert.Equal(QuickNote.MaxBytes, host.CaptureCaps[0]);
        }

        [Fact]
        public async Task SelectionNoteAnswersTheCaptureTableLikeTheEditVerbs()
        {
            FakeSelectionHost host = Desktop();
            host.CaptureAnswer = ClipboardHandler.CaptureResult.NoSelection;
            AppCommandResponse nothing = await AppCommandsSelection.SelectionNote(
                Inv("selection note", "--window-pid", "100"), host, Service(), T, CancellationToken.None);
            Assert.Equal("no-selection", nothing.Reason);
            Assert.Equal(11, nothing.ExitCode);
        }

        // ------------------------------------------------------------------ the log discipline

        [Fact]
        public void AppCommandsLogCarriesNoSelectionCharacterAndNoDataMember()
        {
            AppCommandResponse done = AppCommandResponse.Done(T("Converted the selection."),
                new Dictionary<string, object> { ["changed"] = true });
            string line = AppCommandsLog.Line(new DateTime(2026, 11, 17, 23, 0, 0), "selection convert", "cli", 42, done);
            Assert.Contains("selection convert", line);
            Assert.DoesNotContain("changed", line);
            Assert.DoesNotContain("noteId", line);
        }
    }
}
