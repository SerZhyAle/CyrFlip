using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The five verbs of the layout and text group (ticket S0109): <c>layout list</c>, <c>layout current</c>,
    /// <c>layout switch</c>, <c>text convert</c>, <c>text flip-case</c>. Each handler is a pure function, so they run
    /// over a fake host (<see cref="ILayoutCommandHost"/>: entries, the active layout, CapsLock, the requests it was
    /// asked for) and over a converter with a fixed layout list - no keyboard, no window, no registry. The cases that
    /// need a running tray copy (a real switch in Notepad, a browser and a console, the elevated window, a cold
    /// <c>layout list</c> beside a live one) are <c>tools/uitest/Test-AppCommands.ps1</c>'s and the release's.
    /// </summary>
    public class AppCommandsLayoutTextTests
    {
        private static string T(string en) => en;

        private static LayoutEntry E(long hkl, string klid, string code, string label)
            => new LayoutEntry(new IntPtr(hkl), klid, code, label);

        private static LayoutEntry Us() => E(0x04090409, "00000409", "EN", "US");
        private static LayoutEntry Ru() => E(0x04190419, "00000419", "RU", "Russian");
        private static LayoutEntry De() => E(0x04070407, "00000407", "DE", "German");
        private static LayoutEntry Dvorak() => E(0x00010409, "00010409", "EN", "United States-Dvorak");

        private sealed class FakeHost : ILayoutCommandHost
        {
            public List<LayoutEntry> Installed = new List<LayoutEntry>();
            public LayoutEntry? ActiveEntry;
            public bool Caps;
            public SwitchOutcome? Forced;
            public readonly List<IntPtr> Requests = new List<IntPtr>();
            public int AfterSwitchCalls;

            public IReadOnlyList<LayoutEntry> Entries() => Installed;
            public LayoutEntry? Active() => ActiveEntry;
            public bool CapsLockOn() => Caps;
            public SwitchOutcome Switch(IntPtr hkl)
            {
                Requests.Add(hkl);
                return Forced ?? SwitchOutcome.Requested;
            }
            public void AfterSwitch() => AfterSwitchCalls++;
        }

        private static FakeHost Host(LayoutEntry? active, params LayoutEntry[] installed)
            => new FakeHost { Installed = installed.ToList(), ActiveEntry = active };

        /// <summary>A command line through the shipped parser, so a case the parser refuses cannot be built by mistake.</summary>
        private static AppCommandInvocation Inv(params string[] args)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(args);
            Assert.Equal(AppCommandLineKind.Command, parsed.Kind);
            return parsed.Invocation!;
        }

        /// <summary>An invocation built past the parser, for what only the verb's own guard can see.</summary>
        private static AppCommandInvocation Raw(string verb, params string[] nameValuePairs)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nameValuePairs.Length; i += 2) options[nameValuePairs[i]] = nameValuePairs[i + 1];
            return new AppCommandInvocation(AppCommandManifest.Shipped.Find(verb)!, new string[0], options);
        }

        private static AppCommandResponse Switch(FakeHost host, string to)
            => AppCommandsLayoutText.LayoutSwitch(Inv("layout", "switch", "--to", to), host, T);

        /// <summary>The seam that makes the table fallback deterministic: no layout is installed as far as the converter knows.</summary>
        private static string TableOnly(string text, string source, string target, bool symbols)
            => KeyboardLayoutConverter.Convert(text, source, target, symbols, true, _ => IntPtr.Zero);

        private static AppCommandResponse Convert(IReadOnlyList<LayoutEntry> installed, params string[] options)
            => AppCommandsLayoutText.TextConvert(Inv(new[] { "text", "convert" }.Concat(options).ToArray()), installed, TableOnly, T);

        private static AppCommandResponse Flip(params string[] options)
            => AppCommandsLayoutText.TextFlipCase(Inv(new[] { "text", "flip-case" }.Concat(options).ToArray()), T);

        private static Dictionary<string, object> Item(object entry) => Assert.IsType<Dictionary<string, object>>(entry);

        // ------------------------------------------------------------------ the manifest and the parser

        [Fact]
        public void TheFiveVerbsAreInTheManifestWithTheirRiskAndColdStart()
        {
            var expected = new (string Name, string Risk, string Cold)[]
            {
                ("layout list", "safe", "oneshot"),
                ("layout current", "safe", "refuse"),
                ("layout switch", "writes-user-data", "refuse"),
                ("text convert", "safe", "oneshot"),
                ("text flip-case", "safe", "oneshot"),
            };
            foreach (var (name, risk, cold) in expected)
            {
                AppCommandSpec spec = AppCommandManifest.Shipped.Find(name)!;
                Assert.NotNull(spec);
                Assert.Equal(risk, spec.Risk);
                Assert.Equal(cold, spec.ColdStart);
                Assert.NotNull(spec.Arg("json"));
                Assert.NotNull(spec.Arg("result"));
                Assert.False(spec.HasLegacyAlias);
                Assert.Equal("always", spec.Forward);
            }
        }

        [Fact]
        public void TheOptionsOfTheVerbsAreTheOnesTheDraftNamed()
        {
            Assert.True(AppCommandManifest.Shipped.Find("layout switch")!.Arg("to")!.Required);
            AppCommandSpec convert = AppCommandManifest.Shipped.Find("text convert")!;
            Assert.True(convert.Arg("text")!.Required);
            Assert.True(convert.Arg("from")!.Required);
            Assert.True(convert.Arg("to")!.Required);
            Assert.False(convert.Arg("symbols")!.Required);
            // The base parser's bool takes no next word, so "--symbols false" needs a value-taking kind.
            Assert.Equal("enum", convert.Arg("symbols")!.Kind);
            Assert.Equal(new[] { "true", "false" }, convert.Arg("symbols")!.Values);
            Assert.True(AppCommandManifest.Shipped.Find("text flip-case")!.Arg("text")!.Required);
        }

        [Theory]
        [InlineData("layout|list")]
        [InlineData("layout|list|--result|out.json")]
        [InlineData("layout|current|--json")]
        [InlineData("LAYOUT|SWITCH|--to|next")]
        [InlineData("layout|switch|--to=ru|--result|out.json")]
        [InlineData("text|convert|--text|ghbdtn|--from|en|--to|ru")]
        [InlineData("text|convert|--text|ghbdtn|--from|en|--to|ru|--symbols|false|--result|t.json")]
        [InlineData("text|convert|--text|ghbdtn|--from|en|--to|ru|--symbols=FALSE")]
        [InlineData("text|convert|--text|ghbdtn|--from|en|--to|ru|--symbols|true")]
        [InlineData("text|flip-case|--text|hELLO")]
        public void GoodOptionsAreACommand(string line)
            => Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(line.Split('|')).Kind);

        [Theory]
        [InlineData("layout|switch")]
        [InlineData("layout|switch|--to")]
        [InlineData("layout|list|--to|en")]
        [InlineData("layout|current|extra")]
        [InlineData("text|convert|--text|a|--from|en")]
        [InlineData("text|convert|--text|a|--to|ru")]
        [InlineData("text|convert|--from|en|--to|ru")]
        [InlineData("text|convert|--text|a|--from|en|--to|ru|--symbols|maybe")]
        [InlineData("text|convert|--text|a|--from|en|--to|ru|--symbols")]
        [InlineData("text|flip-case")]
        [InlineData("text|flip-case|--text|a|--from|en")]
        public void AnythingElseIsRefusedAsABadArgument(string line)
        {
            AppCommandLineResult parsed = AppCommandLine.Parse(line.Split('|'));
            Assert.Equal(AppCommandLineKind.Refused, parsed.Kind);
            Assert.Equal(AppCommandReasons.BadArgument, parsed.Reason);
        }

        [Fact]
        public void TheParsersBoundIsTighterThanTheVerbsAndStopsATextFirst()
        {
            Assert.Equal(AppCommandLineKind.Command,
                AppCommandLine.Parse(new[] { "text", "flip-case", "--text", new string('a', 32768) }).Kind);
            AppCommandLineResult over = AppCommandLine.Parse(new[] { "text", "flip-case", "--text", new string('a', 32769) });
            Assert.Equal(AppCommandLineKind.Refused, over.Kind);
            Assert.Equal(AppCommandReasons.TooLarge, over.Reason);
        }

        [Fact]
        public void LayoutSwitchIsRefusedByTheClassGateWhileTheSettingIsOffWhateverItsValueIs()
        {
            // The gate runs before the handler, so a bad --to is 12, not 10, while other programs may not ask.
            AppCommandInvocation bad = Inv("layout", "switch", "--to", "banana");
            AppCommandResponse? refused = AppCommandVerbs.RiskGate(bad.Spec, false, false, T);
            Assert.NotNull(refused);
            Assert.Equal(AppCommandReasons.SwitchOff, refused!.Reason);
            Assert.Equal(12, refused.ExitCode);
            Assert.Null(AppCommandVerbs.RiskGate(bad.Spec, true, false, T));
            foreach (string safe in new[] { "layout list", "layout current", "text convert", "text flip-case" })
                Assert.Null(AppCommandVerbs.RiskGate(AppCommandManifest.Shipped.Find(safe)!, false, false, T));
        }

        // ------------------------------------------------------------------ layout list

        [Fact]
        public void ListAnswersEveryLayoutInWindowsOrderWithItsKlidCodeAndLabel()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutList(new[] { Dvorak(), Ru(), Us() }, Ru(), running: true, T);

            Assert.Equal(0, r.ExitCode);
            Assert.Equal("done", r.Status);
            Assert.Equal(3, r.Data!["count"]);
            object[] layouts = Assert.IsType<object[]>(r.Data["layouts"]);
            Assert.Equal(new[] { "00010409", "00000419", "00000409" }, layouts.Select(l => (string)Item(l)["klid"]));
            Assert.Equal(new[] { "EN", "RU", "EN" }, layouts.Select(l => (string)Item(l)["code"]));
            Assert.Equal(new[] { "United States-Dvorak", "Russian", "US" }, layouts.Select(l => (string)Item(l)["label"]));
        }

        [Fact]
        public void InTheRunningCopyExactlyOneLayoutIsCurrent()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutList(new[] { Us(), Ru(), De() }, Ru(), running: true, T);
            object[] layouts = Assert.IsType<object[]>(r.Data!["layouts"]);
            Assert.Equal(new[] { false, true, false }, layouts.Select(l => (bool)Item(l)["current"]));
        }

        [Fact]
        public void WithNoWindowToReadCurrentIsFalseEverywhereButStillThere()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutList(new[] { Us(), Ru() }, null, running: true, T);
            object[] layouts = Assert.IsType<object[]>(r.Data!["layouts"]);
            Assert.All(layouts, l => Assert.Equal(false, Item(l)["current"]));
        }

        [Fact]
        public void InAOneShotProcessThereIsNoCurrentAtAll()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutList(new[] { Us(), Ru() }, null, running: false, T);
            Assert.Equal(0, r.ExitCode);
            object[] layouts = Assert.IsType<object[]>(r.Data!["layouts"]);
            Assert.All(layouts, l => Assert.False(Item(l).ContainsKey("current")));
            Assert.Equal(2, r.Data["count"]);
        }

        [Fact]
        public void TheSameEntriesListTheSameInTheInstanceAndInAOneShotProcess()
        {
            LayoutEntry[] installed = { Us(), Ru(), Dvorak() };
            AppCommandResponse instance = AppCommandsLayoutText.LayoutList(installed, Us(), running: true, T);
            AppCommandResponse oneShot = AppCommandsLayoutText.LayoutList(installed, null, running: false, T);
            string[] Klids(AppCommandResponse r) => ((object[])r.Data!["layouts"]).Select(l => (string)Item(l)["klid"]).ToArray();
            Assert.Equal(Klids(instance), Klids(oneShot));
        }

        [Fact]
        public void NoLayoutsAtAllIsAFailureNeverAnEmptyList()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutList(new LayoutEntry[0], null, running: false, T);
            Assert.Equal(1, r.ExitCode);
            Assert.Equal("failed", r.Status);
            Assert.Null(r.Data);
        }

        [Fact]
        public void TheLiveEntriesFollowGetKeyboardLayoutListInItsOrder()
        {
            IReadOnlyList<LayoutEntry> entries = IndicatorLayoutHost.InstalledEntries();
            IntPtr[] installed = KeyboardLayoutConverter.InstalledLayouts();
            Assert.NotEmpty(entries);
            Assert.True(entries.Count <= installed.Length);
            // An entry is skipped only when no KLID could be worked out; the rest keep Windows' order.
            Assert.Equal(entries.Select(e => e.Hkl), installed.Where(h => LayoutIdentity.KlidForHkl(h).Length > 0));
            Assert.All(entries, e =>
            {
                Assert.Equal(8, e.Klid.Length);
                Assert.Equal(e.Klid, e.Klid.ToUpperInvariant());
                Assert.NotEmpty(e.Code);
                Assert.NotNull(e.Label);
            });
        }

        // ------------------------------------------------------------------ layout current

        [Fact]
        public void CurrentAnswersTheCodeTheKlidAndTheCapsLockOfTheWindow()
        {
            AppCommandResponse on = AppCommandsLayoutText.LayoutCurrent(Ru(), true, T);
            Assert.Equal(0, on.ExitCode);
            Assert.Equal("RU", on.Data!["code"]);
            Assert.Equal("00000419", on.Data["klid"]);
            Assert.Equal(true, on.Data["capsLock"]);
            AppCommandResponse off = AppCommandsLayoutText.LayoutCurrent(Us(), false, T);
            Assert.Equal("EN", off.Data!["code"]);
            Assert.Equal(false, off.Data["capsLock"]);
        }

        [Fact]
        public void CurrentWithNoUsableWindowIsElevenTargetMissing()
        {
            AppCommandResponse r = AppCommandsLayoutText.LayoutCurrent(null, false, T);
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, r.Reason);
            Assert.Null(r.Data);
        }

        // ------------------------------------------------------------------ layout switch

        [Fact]
        public void SwitchByCodeAsksForThatLayoutAndTheMarkerIsPolledAfter()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            AppCommandResponse r = Switch(host, "ru");

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(new[] { Ru().Hkl }, host.Requests);
            Assert.Equal("00000419", r.Data!["klid"]);
            Assert.Equal("RU", r.Data["code"]);
            Assert.Equal(true, r.Data["switched"]);
            Assert.Equal(1, host.AfterSwitchCalls);
        }

        [Fact]
        public void SwitchByKlidIsExactAndTakesAnAlternateLayout()
        {
            FakeHost host = Host(Ru(), Us(), Ru(), Dvorak());
            AppCommandResponse r = Switch(host, "00010409");

            Assert.Equal(new[] { Dvorak().Hkl }, host.Requests);
            Assert.Equal("00010409", r.Data!["klid"]);
            Assert.Equal("EN", r.Data["code"]);
            Assert.Equal(true, r.Data["switched"]);
        }

        [Fact]
        public void ACodeThatTwoInstalledLayoutsCarryTakesTheFirstInWindowsOrder()
        {
            FakeHost dvorakFirst = Host(Ru(), Dvorak(), Ru(), Us());
            Switch(dvorakFirst, "en");
            Assert.Equal(new[] { Dvorak().Hkl }, dvorakFirst.Requests);

            FakeHost usFirst = Host(Ru(), Us(), Ru(), Dvorak());
            Switch(usFirst, "EN");
            Assert.Equal(new[] { Us().Hkl }, usFirst.Requests);
        }

        [Fact]
        public void NextIsTheTrayClicksRotationAndWrapsAtTheEnd()
        {
            LayoutEntry[] installed = { Us(), Ru(), De() };
            FakeHost fromUs = Host(Us(), installed);
            Assert.Equal("00000419", Switch(fromUs, "next").Data!["klid"]);
            Assert.Equal(new[] { Ru().Hkl }, fromUs.Requests);

            FakeHost fromDe = Host(De(), installed);
            Assert.Equal("00000409", Switch(fromDe, "NEXT").Data!["klid"]);
            Assert.Equal(new[] { Us().Hkl }, fromDe.Requests);
        }

        [Fact]
        public void NextFromALayoutNotInTheListGoesToTheFirstOne()
        {
            FakeHost host = Host(Dvorak(), Us(), Ru());
            Switch(host, "next");
            Assert.Equal(new[] { Us().Hkl }, host.Requests);
        }

        [Fact]
        public void NextWithOneLayoutAnswersZeroNotSwitchedAndAsksForNothing()
        {
            // Nothing failed, and a command has no screen to say it on: answered, with the only layout.
            FakeHost host = Host(null, Us());
            AppCommandResponse r = Switch(host, "next");

            Assert.Equal(0, r.ExitCode);
            Assert.Equal(false, r.Data!["switched"]);
            Assert.Equal("00000409", r.Data["klid"]);
            Assert.Equal("EN", r.Data["code"]);
            Assert.Empty(host.Requests);
            Assert.Equal(0, host.AfterSwitchCalls);
        }

        [Fact]
        public void AskingForTheLayoutTheWindowAlreadyHasPostsNothingAndSaysSwitchedFalse()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            AppCommandResponse byCode = Switch(host, "en");
            AppCommandResponse byKlid = Switch(host, "00000409");

            Assert.Equal(0, byCode.ExitCode);
            Assert.Equal(false, byCode.Data!["switched"]);
            Assert.Equal(0, byKlid.ExitCode);
            Assert.Equal(false, byKlid.Data!["switched"]);
            Assert.Empty(host.Requests);
            Assert.Equal(0, host.AfterSwitchCalls);
        }

        [Fact]
        public void AHostThatFindsTheLayoutAlreadyThereAnswersTheSameWay()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            host.Forced = SwitchOutcome.AlreadyActive;
            AppCommandResponse r = Switch(host, "ru");
            Assert.Equal(0, r.ExitCode);
            Assert.Equal(false, r.Data!["switched"]);
            Assert.Equal(0, host.AfterSwitchCalls);
        }

        [Fact]
        public void AWellFormedLayoutThatIsNotInstalledIsElevenTargetMissingAndNothingIsRequested()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            // A KLID Windows can name, a curated code, and a culture that is not curated.
            foreach (string value in new[] { "0000040A", "de", "nl" })
            {
                AppCommandResponse r = Switch(host, value);
                Assert.Equal(11, r.ExitCode);
                Assert.Equal(AppCommandReasons.TargetMissing, r.Reason);
            }
            Assert.Empty(host.Requests);
            Assert.Equal(0, host.AfterSwitchCalls);
        }

        [Fact]
        public void AValueThatNamesNothingIsTenBadArgument()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            foreach (string value in new[] { "zz", "banana", "00000000", "FFFFFFFF", "e", "en-US" })
            {
                AppCommandResponse r = Switch(host, value);
                Assert.Equal(10, r.ExitCode);
                Assert.Equal(AppCommandReasons.BadArgument, r.Reason);
            }
            Assert.Empty(host.Requests);
        }

        [Fact]
        public void AnEmptyValueIsTenToo()
        {
            // Past the parser: --to= parses as an empty string, which names nothing.
            FakeHost host = Host(Us(), Us(), Ru());
            AppCommandResponse r = AppCommandsLayoutText.LayoutSwitch(Raw("layout switch", "to", ""), host, T);
            Assert.Equal(10, r.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, r.Reason);
        }

        [Fact]
        public void WithNoUsableWindowSwitchIsElevenTargetMissing()
        {
            FakeHost host = Host(null, Us(), Ru());
            foreach (string value in new[] { "ru", "00000419", "next" })
            {
                AppCommandResponse r = Switch(host, value);
                Assert.Equal(11, r.ExitCode);
                Assert.Equal(AppCommandReasons.TargetMissing, r.Reason);
            }
            Assert.Empty(host.Requests);
        }

        [Fact]
        public void AHostWhoseWindowVanishedMeanwhileIsElevenToo()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            host.Forced = SwitchOutcome.NoWindow;
            AppCommandResponse r = Switch(host, "ru");
            Assert.Equal(11, r.ExitCode);
            Assert.Equal(AppCommandReasons.TargetMissing, r.Reason);
            Assert.Equal(0, host.AfterSwitchCalls);
        }

        [Fact]
        public void AMessageEchoesOnlyTheLayoutValueTheCallerTypedAndNeverALineBreak()
        {
            FakeHost host = Host(Us(), Us(), Ru());
            // Eight hexadecimal digits is a KLID Windows can name (language 0x0407): named but absent, so the value is echoed.
            AppCommandResponse r = Switch(host, "00000407");
            Assert.Contains("00000407", r.Message);

            AppCommandResponse convert = AppCommandsLayoutText.TextConvert(
                Raw("text convert", "text", "a", "from", "de", "to", "fr"), new[] { Us() }, TableOnly, T);
            Assert.Equal(10, convert.ExitCode);
            Assert.DoesNotContain("\n", convert.Message);
        }

        // ------------------------------------------------------------------ text convert

        [Fact]
        public void ConvertTurnsGhbdtnIntoPrivetAndBackThroughTheTable()
        {
            AppCommandResponse forward = Convert(new[] { Us(), Ru() }, "--text", "ghbdtn", "--from", "en", "--to", "ru");
            Assert.Equal(0, forward.ExitCode);
            Assert.Equal("привет", forward.Data!["text"]);
            Assert.Equal(true, forward.Data["changed"]);

            AppCommandResponse back = Convert(new[] { Us(), Ru() }, "--text", "привет", "--from", "ru", "--to", "en");
            Assert.Equal("ghbdtn", back.Data!["text"]);
            Assert.Equal(true, back.Data["changed"]);
        }

        [Fact]
        public void TheSeededPairConvertsOnAMachineWithoutTheRussianKeyboard()
        {
            // No Russian layout installed: by code the language falls back to its curated primary KLID, by KLID it is exact,
            // and the pair is the one the product has a table for.
            AppCommandResponse byCode = Convert(new[] { Us() }, "--text", "ghbdtn", "--from", "en", "--to", "ru");
            Assert.Equal("привет", byCode.Data!["text"]);
            AppCommandResponse byKlid = Convert(new LayoutEntry[0], "--text", "ghbdtn", "--from", "00000409", "--to", "00000419");
            Assert.Equal("привет", byKlid.Data!["text"]);
        }

        [Fact]
        public void SymbolsIsTrueWhenAbsentAndFalseWhenAsked()
        {
            LayoutEntry[] installed = { Us(), Ru() };
            Assert.Equal(".привет", Convert(installed, "--text", "/ghbdtn", "--from", "en", "--to", "ru").Data!["text"]);
            Assert.Equal(".привет", Convert(installed, "--text", "/ghbdtn", "--from", "en", "--to", "ru", "--symbols", "true").Data!["text"]);
            Assert.Equal("/привет", Convert(installed, "--text", "/ghbdtn", "--from", "en", "--to", "ru", "--symbols", "false").Data!["text"]);
        }

        [Fact]
        public void MixedTextIsFixedPerCharacterInOnePress()
        {
            AppCommandResponse r = Convert(new[] { Us(), Ru() }, "--text", "ghbdtnпривет", "--from", "en", "--to", "ru");
            Assert.Equal("приветghbdtn", r.Data!["text"]);
        }

        [Fact]
        public void ATextWithNoCounterpartComesBackUnchangedWithChangedFalse()
        {
            AppCommandResponse r = Convert(new[] { Us(), Ru() }, "--text", "123", "--from", "en", "--to", "ru");
            Assert.Equal(0, r.ExitCode);
            Assert.Equal("123", r.Data!["text"]);
            Assert.Equal(false, r.Data["changed"]);
        }

        [Fact]
        public void ACharacterOutsideTheBasicPlanePassesThroughUnchanged()
        {
            AppCommandResponse r = Convert(new[] { Us(), Ru() }, "--text", "\U0001F600ghbdtn", "--from", "en", "--to", "ru");
            Assert.Equal("\U0001F600привет", r.Data!["text"]);
        }

        [Fact]
        public void ALayoutThatIsNotInstalledAndHasNoTableIsTenAndNamesTheValue()
        {
            LayoutEntry[] installed = { Us(), Ru() };

            AppCommandResponse from = Convert(installed, "--text", "a", "--from", "de", "--to", "en");
            Assert.Equal(10, from.ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, from.Reason);
            Assert.Contains("\"de\"", from.Message);

            AppCommandResponse to = Convert(installed, "--text", "a", "--from", "en", "--to", "de");
            Assert.Equal(10, to.ExitCode);
            Assert.Contains("\"de\"", to.Message);

            AppCommandResponse klid = Convert(installed, "--text", "a", "--from", "00000409", "--to", "0000040C");
            Assert.Equal(10, klid.ExitCode);
            Assert.Contains("0000040C", klid.Message);

            // A culture that is not curated is named but absent: 10 as well, with the same words.
            AppCommandResponse culture = Convert(installed, "--text", "a", "--from", "nl", "--to", "en");
            Assert.Equal(10, culture.ExitCode);
            Assert.Contains("\"nl\"", culture.Message);
        }

        [Fact]
        public void AnInstalledPairThatIsNotTheBuiltInOneConvertsThroughTheInjectedConverter()
        {
            // Both layouts are installed, so the handler lets the converter work; what it does is the converter's own
            // (the live Win32 path is KeyboardLayoutConverterTests'). Here the converter is a probe.
            string? seenFrom = null, seenTo = null; bool? seenSymbols = null;
            AppCommandResponse r = AppCommandsLayoutText.TextConvert(
                Inv("text", "convert", "--text", "abc", "--from", "de", "--to", "fr", "--symbols", "false"),
                new[] { De(), E(0x040C040C, "0000040C", "FR", "French") },
                (text, source, target, symbols) => { seenFrom = source; seenTo = target; seenSymbols = symbols; return text.ToUpperInvariant(); },
                T);

            Assert.Equal(0, r.ExitCode);
            Assert.Equal("ABC", r.Data!["text"]);
            Assert.Equal(true, r.Data["changed"]);
            Assert.Equal("00000407", seenFrom);
            Assert.Equal("0000040C", seenTo);
            Assert.Equal(false, seenSymbols);
        }

        [Fact]
        public void AnUnnamedSymbolsOptionReachesTheConverterAsTrue()
        {
            bool? seen = null;
            AppCommandsLayoutText.TextConvert(
                Inv("text", "convert", "--text", "abc", "--from", "en", "--to", "ru"),
                new[] { Us(), Ru() }, (text, source, target, symbols) => { seen = symbols; return text; }, T);
            Assert.Equal(true, seen);
        }

        [Fact]
        public void TheSameLayoutOnBothSidesIsTen()
        {
            LayoutEntry[] installed = { Us(), Ru() };
            Assert.Equal(10, Convert(installed, "--text", "a", "--from", "en", "--to", "en").ExitCode);
            Assert.Equal(10, Convert(installed, "--text", "a", "--from", "en", "--to", "00000409").ExitCode);
            Assert.Equal(10, Convert(installed, "--text", "a", "--from", "00000419", "--to", "RU").ExitCode);
            // Two codes resolving to one layout are one layout, even when only the curated KLID stands behind them.
            Assert.Equal(10, Convert(new LayoutEntry[0], "--text", "a", "--from", "ru", "--to", "00000419").ExitCode);
            Assert.Equal(AppCommandReasons.BadArgument, Convert(installed, "--text", "a", "--from", "en", "--to", "en").Reason);
        }

        [Fact]
        public void AValueThatNamesNothingOrIsNextIsTenOnEitherSide()
        {
            LayoutEntry[] installed = { Us(), Ru() };
            foreach (string bad in new[] { "banana", "zz", "next", "00000000", "FFFFFFFF", "" })
            {
                AppCommandResponse from = AppCommandsLayoutText.TextConvert(
                    Raw("text convert", "text", "a", "from", bad, "to", "ru"), installed, TableOnly, T);
                Assert.Equal(10, from.ExitCode);
                Assert.Equal(AppCommandReasons.BadArgument, from.Reason);
                AppCommandResponse to = AppCommandsLayoutText.TextConvert(
                    Raw("text convert", "text", "a", "from", "en", "to", bad), installed, TableOnly, T);
                Assert.Equal(10, to.ExitCode);
            }
        }

        // ------------------------------------------------------------------ text flip-case

        [Fact]
        public void FlipCaseTurnsHelloIntoHelloWithChangedTrue()
        {
            AppCommandResponse r = Flip("--text", "hELLO");
            Assert.Equal(0, r.ExitCode);
            Assert.Equal("Hello", r.Data!["text"]);
            Assert.Equal(true, r.Data["changed"]);
            Assert.Equal("пРИВЕТ", Flip("--text", "Привет").Data!["text"]);
            Assert.Equal("Ab1 Вг", Flip("--text", "aB1 вГ").Data!["text"]);
        }

        [Fact]
        public void ATextWithNoCasedLetterComesBackUnchangedWithChangedFalse()
        {
            AppCommandResponse r = Flip("--text", "123 !?");
            Assert.Equal(0, r.ExitCode);
            Assert.Equal("123 !?", r.Data!["text"]);
            Assert.Equal(false, r.Data["changed"]);
        }

        // ------------------------------------------------------------------ the bounds of a text

        [Fact]
        public void AnEmptyTextOrNoTextIsTenOnBothTextVerbs()
        {
            foreach (AppCommandResponse r in new[]
            {
                AppCommandsLayoutText.TextFlipCase(Raw("text flip-case", "text", ""), T),
                AppCommandsLayoutText.TextFlipCase(Raw("text flip-case"), T),
                AppCommandsLayoutText.TextConvert(Raw("text convert", "text", "", "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T),
                AppCommandsLayoutText.TextConvert(Raw("text convert", "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T),
            })
            {
                Assert.Equal(10, r.ExitCode);
                Assert.Equal(AppCommandReasons.BadArgument, r.Reason);
            }
        }

        [Fact]
        public void TheVerbsOwnLimitIsThirtyTwoThousandSevenHundredSixtyEightCharacters()
        {
            string atLimit = new string('1', AppCommandsLayoutText.MaxTextChars);
            Assert.Equal(0, AppCommandsLayoutText.TextFlipCase(Raw("text flip-case", "text", new string('a', AppCommandsLayoutText.MaxTextChars)), T).ExitCode);
            AppCommandResponse convert = AppCommandsLayoutText.TextConvert(
                Raw("text convert", "text", atLimit, "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T);
            Assert.Equal(0, convert.ExitCode);
            Assert.Equal(false, convert.Data!["changed"]);

            string over = new string('a', AppCommandsLayoutText.MaxTextChars + 1);
            AppCommandResponse flip = AppCommandsLayoutText.TextFlipCase(Raw("text flip-case", "text", over), T);
            Assert.Equal(10, flip.ExitCode);
            Assert.Equal(AppCommandReasons.TooLarge, flip.Reason);
            AppCommandResponse tooMuch = AppCommandsLayoutText.TextConvert(
                Raw("text convert", "text", over, "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T);
            Assert.Equal(10, tooMuch.ExitCode);
            Assert.Equal(AppCommandReasons.TooLarge, tooMuch.Reason);
        }

        [Fact]
        public void AConversionThatGrowsPastOneAnswerLineIsRefusedNotCutAndNotLeftUnreadable()
        {
            // 32768 Latin letters are 32768 bytes - the parser takes them - and the same keys in Cyrillic are 65536:
            // more than the 64 KiB line a caller reads. Refused with its own reason, in both the instance and a one-shot.
            string latin = new string('g', 32768);
            Assert.Equal(AppCommandLineKind.Command, AppCommandLine.Parse(new[] { "text", "convert", "--text", latin, "--from", "en", "--to", "ru" }).Kind);
            AppCommandResponse r = AppCommandsLayoutText.TextConvert(
                Raw("text convert", "text", latin, "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T);
            Assert.Equal(10, r.ExitCode);
            Assert.Equal(AppCommandReasons.TooLarge, r.Reason);
            Assert.Null(r.Data);

            // Half of it fits, and the answer line stays under the bound the caller reads.
            AppCommandResponse half = AppCommandsLayoutText.TextConvert(
                Raw("text convert", "text", new string('g', 16384), "from", "en", "to", "ru"), new[] { Us(), Ru() }, TableOnly, T);
            Assert.Equal(0, half.ExitCode);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(AppCommandProtocol.Serialize(half)) <= AppCommandLine.MaxRequestBytes);
        }

        [Fact]
        public void NoMessageCarriesTheCallersText()
        {
            const string marker = "SECRET-MARKER-4711";
            AppCommandResponse[] answers =
            {
                Flip("--text", marker),
                Convert(new[] { Us(), Ru() }, "--text", marker, "--from", "en", "--to", "ru"),
                Convert(new[] { Us(), Ru() }, "--text", marker, "--from", "de", "--to", "en"),
                Convert(new[] { Us(), Ru() }, "--text", marker, "--from", "en", "--to", "en"),
            };
            Assert.All(answers, a => Assert.DoesNotContain(marker, a.Message));
        }
    }
}
