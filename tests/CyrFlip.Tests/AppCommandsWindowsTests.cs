using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four module-window verbs of ticket S0119 - <c>note show</c>, <c>clipboard-history show</c>,
    /// <c>symbols show</c> and <c>trafficlight show</c> - over a fake host. The handlers answer the module gate,
    /// the show and the sentence; the cases that need real windows (the board raised without the focus, the palette
    /// beside a real editor, the strip's placement) are the release's and the live checks at the desk.
    /// </summary>
    public class AppCommandsWindowsTests
    {
        private sealed class FakeHost : IWindowCommandsHost
        {
            public bool QuickNotesOn = true;
            public bool ClipboardHistoryOn = true;
            public bool SymbolsOn = true;
            public bool TrafficLightOn = true;

            public readonly List<bool> NotesShown = new List<bool>();
            public int HistoryShown, SymbolsShown, BoardShown;

            bool IWindowCommandsHost.QuickNotesOn => QuickNotesOn;
            bool IWindowCommandsHost.ClipboardHistoryOn => ClipboardHistoryOn;
            bool IWindowCommandsHost.SymbolsOn => SymbolsOn;
            bool IWindowCommandsHost.TrafficLightOn => TrafficLightOn;

            void IWindowCommandsHost.ShowQuickNotes(bool startNew) => NotesShown.Add(startNew);
            void IWindowCommandsHost.ShowClipboardHistory() { HistoryShown++; }
            void IWindowCommandsHost.ShowSymbols() { SymbolsShown++; }
            void IWindowCommandsHost.ShowTrafficLight() { BoardShown++; }
        }

        private static string T(string en) => en;

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        private static AppCommandResponse NoteShow(FakeHost host, params string[] options)
            => AppCommandsWindows.NoteShow(Inv(new[] { "note", "show" }.Concat(options).ToArray()), host, T);

        private static AppCommandResponse HistoryShow(FakeHost host) =>
            AppCommandsWindows.HistoryShow(host, T);

        private static AppCommandResponse SymbolsShow(FakeHost host) =>
            AppCommandsWindows.SymbolsShow(host, T);

        private static AppCommandResponse TrafficLightShow(FakeHost host) =>
            AppCommandsWindows.TrafficLightShow(host, T);

        private static void ShownNothing(FakeHost host)
        {
            Assert.Empty(host.NotesShown);
            Assert.Equal(0, host.HistoryShown);
            Assert.Equal(0, host.SymbolsShown);
            Assert.Equal(0, host.BoardShown);
        }

        // ------------------------------------------------------------------ module on: exit 0, the show, nothing else

        [Fact]
        public void NoteShowOpensAndAnswersDone()
        {
            var host = new FakeHost();
            AppCommandResponse response = NoteShow(host);
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal("done", response.Status);
            Assert.Equal(new List<bool> { false }, host.NotesShown);
            Assert.Null(response.Data);
            Assert.Equal(0, host.HistoryShown + host.SymbolsShown + host.BoardShown);
        }

        [Fact]
        public void NewNoteIsPassedThroughToTheChordPath()
        {
            var host = new FakeHost();
            AppCommandResponse response = NoteShow(host, "--new");
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal(new List<bool> { true }, host.NotesShown);
        }

        [Fact]
        public void HistoryShowOpensAndAnswersDone()
        {
            var host = new FakeHost();
            AppCommandResponse response = HistoryShow(host);
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal(1, host.HistoryShown);
        }

        [Fact]
        public void SymbolsShowOpensAndAnswersDone()
        {
            var host = new FakeHost();
            AppCommandResponse response = SymbolsShow(host);
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal(1, host.SymbolsShown);
        }

        [Fact]
        public void TrafficLightShowOpensAndAnswersDone()
        {
            var host = new FakeHost();
            AppCommandResponse response = TrafficLightShow(host);
            Assert.Equal(AppCommandExit.Done, response.ExitCode);
            Assert.Equal(1, host.BoardShown);
        }

        // ------------------------------------------------------------------ module off: exit 11, asked before anything

        [Fact]
        public void NoteShowWithTheModuleOffAnswersModuleOffAndShowsNothing()
        {
            var host = new FakeHost { QuickNotesOn = false };
            AppCommandResponse response = NoteShow(host);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            Assert.Contains("quick notes", response.Message);
            ShownNothing(host);
        }

        [Fact]
        public void NoteShowNewWithTheModuleOffAnswersModuleOffAndShowsNothing()
        {
            var host = new FakeHost { QuickNotesOn = false };
            AppCommandResponse response = NoteShow(host, "--new");
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            ShownNothing(host);
        }

        [Fact]
        public void HistoryShowWithTheModuleOffAnswersModuleOffAndShowsNothing()
        {
            var host = new FakeHost { ClipboardHistoryOn = false };
            AppCommandResponse response = HistoryShow(host);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            Assert.Contains("clipboard history", response.Message);
            ShownNothing(host);
        }

        [Fact]
        public void SymbolsShowWithTheModuleOffAnswersModuleOffAndShowsNothing()
        {
            var host = new FakeHost { SymbolsOn = false };
            AppCommandResponse response = SymbolsShow(host);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            Assert.Contains("symbols window", response.Message);
            ShownNothing(host);
        }

        [Fact]
        public void TrafficLightShowWithTheModuleOffAnswersModuleOffAndShowsNothing()
        {
            var host = new FakeHost { TrafficLightOn = false };
            AppCommandResponse response = TrafficLightShow(host);
            Assert.Equal(AppCommandExit.NotReady, response.ExitCode);
            Assert.Equal(AppCommandReasons.ModuleOff, response.Reason);
            Assert.Contains("traffic light", response.Message);
            ShownNothing(host);
        }

        // ------------------------------------------------------------------ show, never hide: twice is twice shown

        [Theory]
        [InlineData("note")]
        [InlineData("history")]
        [InlineData("symbols")]
        [InlineData("board")]
        public void ShownTwiceTheWindowIsShownTwice(string which)
        {
            var host = new FakeHost();
            for (int run = 0; run < 2; run++)
            {
                AppCommandResponse response = which switch
                {
                    "note" => NoteShow(host),
                    "history" => HistoryShow(host),
                    "symbols" => SymbolsShow(host),
                    _ => TrafficLightShow(host),
                };
                // Show, never hide: the second run answers done exactly like the first - no verb of
                // this group ever hides its window, so a shortcut that opens it cannot close it.
                Assert.Equal(AppCommandExit.Done, response.ExitCode);
            }
            switch (which)
            {
                case "note":
                    Assert.Equal(new List<bool> { false, false }, host.NotesShown);
                    break;
                case "history":
                    Assert.Equal(2, host.HistoryShown);
                    break;
                case "symbols":
                    Assert.Equal(2, host.SymbolsShown);
                    break;
                default:
                    Assert.Equal(2, host.BoardShown);
                    break;
            }
        }

        // ------------------------------------------------------------------ the base parser: an unknown option is exit 10

        [Theory]
        [InlineData("note|show|--style")]
        [InlineData("note|show|--new|extra")]
        [InlineData("clipboard-history|show|--all")]
        [InlineData("symbols|show|--target|nas")]
        [InlineData("trafficlight|show|--target|nas")]
        public void AnUnknownOptionIsRefusedByTheBaseParser(string line)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(line.Split('|'));
            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, parsed.Reason);
        }

        [Fact]
        public void EveryWindowVerbIsInTheShippedManifestWithTheAgreedFields()
        {
            foreach (string verb in new[] { "note show", "clipboard-history show", "symbols show", "trafficlight show" })
            {
                AppCommandSpec? spec = AppCommandManifest.Shipped.Find(verb);
                Assert.NotNull(spec);
                Assert.True(spec!.IsSafe, verb + " is a safe verb");
                Assert.Equal("refuse", spec.ColdStart);
                Assert.Equal("done", spec.Wait);
                Assert.Equal(5000, spec.TimeoutMs);
            }
        }
    }
}
