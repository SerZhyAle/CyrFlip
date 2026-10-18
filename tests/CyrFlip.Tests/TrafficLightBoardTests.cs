using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CyrFlip;
using Xunit;
using static CyrFlip.WindowInterop;

namespace CyrFlip.Tests
{
    /// <summary>The arrangement of the board and the walk of its keys - pure, no window (ticket S0069 section 6).</summary>
    public class TrafficLightBoardLayoutTests
    {
        private static readonly Size Tile = new Size(100, 40);

        private static TrafficLightTile T(string id, string group = "")
            => new TrafficLightTile { Target = new TrafficLightTarget { Id = id, Label = id, Group = group } };

        [Fact]
        public void GroupsFollowTheirFirstMemberAndOnlyANamedGroupHasACaption()
        {
            var tiles = new List<TrafficLightTile> { T("a", "Office"), T("b", ""), T("c", "Office"), T("d", "Home") };
            List<BoardItem> items = TrafficLightBoardLayout.Compute(tiles, 600, Tile, 8, 20, false, out _);

            Assert.Equal(new[] { "Office" }, items.Where(i => !i.IsTile && i.Caption == "Office").Select(i => i.Caption).ToArray());
            Assert.Equal(new[] { "Office", "Home" }, items.Where(i => !i.IsTile).Select(i => i.Caption).ToArray());
            Assert.Equal(new[] { 0, 2, 1, 3 }, TrafficLightBoardLayout.ReadingOrder(items).ToArray());
        }

        [Fact]
        public void TilesWrapAtTheWidthAndTheContentHeightCoversThem()
        {
            var tiles = Enumerable.Range(0, 7).Select(i => T("t" + i)).ToList();
            // 3 columns: 8 + 3 * (100 + 8) = 332
            List<BoardItem> items = TrafficLightBoardLayout.Compute(tiles, 340, Tile, 8, 20, false, out int height);

            Assert.Equal(new[] { 8, 116, 224, 8, 116, 224, 8 }, items.Select(i => i.Bounds.Left).ToArray());
            Assert.Equal(3, items.Select(i => i.Bounds.Top).Distinct().Count());
            Assert.True(height >= items.Max(i => i.Bounds.Bottom));
        }

        [Fact]
        public void ARightToLeftBoardIsTheMirrorImage()
        {
            var tiles = new List<TrafficLightTile> { T("a"), T("b") };
            List<BoardItem> ltr = TrafficLightBoardLayout.Compute(tiles, 340, Tile, 8, 20, false, out _);
            List<BoardItem> rtl = TrafficLightBoardLayout.Compute(tiles, 340, Tile, 8, 20, true, out _);

            Assert.Equal(8, ltr[0].Bounds.Left);
            Assert.Equal(340 - 8 - 100, rtl[0].Bounds.Left);
            Assert.True(rtl[0].Bounds.Left > rtl[1].Bounds.Left);
        }

        [Fact]
        public void TheArrowKeysWalkTheReadingOrderAndTheRowsAndStopAtTheEnds()
        {
            var tiles = Enumerable.Range(0, 5).Select(i => T("t" + i)).ToList();
            List<BoardItem> items = TrafficLightBoardLayout.Compute(tiles, 340, Tile, 8, 20, false, out _); // 3 per row

            Assert.Equal(1, TrafficLightBoardLayout.Navigate(items, 0, Keys.Right));
            Assert.Equal(0, TrafficLightBoardLayout.Navigate(items, 0, Keys.Left));
            Assert.Equal(3, TrafficLightBoardLayout.Navigate(items, 0, Keys.Down));
            Assert.Equal(1, TrafficLightBoardLayout.Navigate(items, 4, Keys.Up));
            Assert.Equal(4, TrafficLightBoardLayout.Navigate(items, 0, Keys.End));
            Assert.Equal(0, TrafficLightBoardLayout.Navigate(items, 4, Keys.Home));
            Assert.Equal(4, TrafficLightBoardLayout.Navigate(items, 4, Keys.Right));
            Assert.Equal(0, TrafficLightBoardLayout.Navigate(items, 0, Keys.Up));
        }

        [Fact]
        public void AClickLandsOnTheTileUnderItAndOnNothingInAGap()
        {
            var tiles = new List<TrafficLightTile> { T("a"), T("b") };
            List<BoardItem> items = TrafficLightBoardLayout.Compute(tiles, 340, Tile, 8, 20, false, out _);

            Assert.Equal(0, TrafficLightBoardLayout.HitTest(items, new Point(20, 20)));
            Assert.Equal(1, TrafficLightBoardLayout.HitTest(items, new Point(130, 20)));
            Assert.Equal(-1, TrafficLightBoardLayout.HitTest(items, new Point(110, 20))); // the gap
            Assert.Equal(-1, TrafficLightBoardLayout.HitTest(items, new Point(20, 200)));
        }
    }

    /// <summary>The status fills (ticket S0069 D15): fills, not inks, with their own contrast promises.</summary>
    public class TrafficLightPaletteTests
    {
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void TheMarkAndTheLabelHoldFourAndAHalfToOneOnEveryFill(string name)
        {
            ThemePalette palette = name == "Dark" ? ThemePalette.Dark : ThemePalette.Light;
            foreach (Color fill in new[] { palette.StatusOkFill, palette.StatusWarningFill, palette.StatusErrorFill, palette.StatusUnknownFill })
                Assert.True(ThemePalette.Contrast(palette.StatusInk, fill) >= 4.5, name + " " + fill);
        }

        [Fact]
        public void OnTheDarkSurfaceEveryFillIsAnEdgeOfAtLeastThreeToOne()
        {
            foreach (Color fill in new[] { ThemePalette.Dark.StatusOkFill, ThemePalette.Dark.StatusWarningFill,
                ThemePalette.Dark.StatusErrorFill, ThemePalette.Dark.StatusUnknownFill })
                Assert.True(ThemePalette.Contrast(fill, ThemePalette.Dark.SurfaceWindow) >= 3.0, fill.ToString());
        }

        [Fact]
        public void TheFillsAreNotTheInkRolesAndTheFourStatesDiffer()
        {
            ThemePalette light = ThemePalette.Light;
            Assert.NotEqual(light.Success, light.StatusOkFill);
            Assert.NotEqual(light.Warning, light.StatusWarningFill);
            Assert.NotEqual(light.Danger, light.StatusErrorFill);
            var fills = new[] { light.StatusOkFill, light.StatusWarningFill, light.StatusErrorFill, light.StatusUnknownFill };
            Assert.Equal(4, fills.Select(c => c.ToArgb()).Distinct().Count());
        }

        [Fact]
        public void HighContrastDrawsNoColourOfOurs()
        {
            ThemePalette hc = ThemePalette.HighContrast;
            foreach (Color c in new[] { hc.StatusOkFill, hc.StatusWarningFill, hc.StatusErrorFill, hc.StatusUnknownFill, hc.StatusInk })
                Assert.True(c.IsSystemColor, c.ToString());
        }
    }

    /// <summary>What Enter does with the recognized text: the plan, the duplicates, the one machine fallback.</summary>
    public class TrafficLightSmartAddTests
    {
        private static List<RecognizedLine> Lines(string text) => TargetRecognizer.Recognize(text).Lines;

        [Fact]
        public void EnterAddsEachLineWithThatKindsDefaultsAndNothingOptedIn()
        {
            SmartAddPlan plan = TrafficLightSmartAdd.Plan(Lines("www.google.com\n8.8.8.8\nD:\\"), new TrafficLightTarget[0]);

            Assert.Equal(new[] { "http", "ping", "drive" }, plan.Targets.Select(t => t.Kind).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, plan.Targets.Select(t => t.Ordinal).ToArray());
            Assert.All(plan.Targets, t =>
            {
                Assert.Equal(60, t.IntervalSeconds);
                Assert.Equal(1, t.FailuresBeforeDown);
                Assert.False(t.Notify);
                Assert.False(t.RecordHistory);
                Assert.True(t.Enabled);
                Assert.Equal(0, t.TimeoutMs); // the kind's own default, followed
            });
        }

        [Fact]
        public void AnAddressAlreadyOnTheBoardIsReportedAndNotAddedAgain()
        {
            var existing = new List<TrafficLightTarget>
            {
                new TrafficLightTarget { Id = "x", Kind = "ping", Address = "8.8.8.8", Ordinal = 4 },
            };
            SmartAddPlan plan = TrafficLightSmartAdd.Plan(Lines("8.8.8.8\n1.1.1.1\n1.1.1.1"), existing);

            Assert.Equal(new[] { "1.1.1.1" }, plan.Targets.Select(t => t.Address).ToArray());
            Assert.Equal(2, plan.Duplicates.Count);
            Assert.Equal(5, plan.Targets[0].Ordinal); // after the last one
        }

        [Fact]
        public void ABackslashHostIsFlaggedAndOnlyThatOneIsAskedAboutPortFourFortyFive()
        {
            SmartAddPlan plan = TrafficLightSmartAdd.Plan(Lines("\\\\SZAMEDIA\nrouter"), new TrafficLightTarget[0]);

            TrafficLightTarget machine = plan.Targets.Single(t => t.Address == "SZAMEDIA");
            Assert.Contains(machine.Id, plan.MachineIds);
            Assert.Single(plan.MachineIds);
        }

        private sealed class FakeAsync : IAsyncTrafficProbe
        {
            private readonly Func<TrafficLightTarget, ProbeOutcome> _answer;
            public readonly List<string> Asked = new List<string>();
            public FakeAsync(Func<TrafficLightTarget, ProbeOutcome> answer) { _answer = answer; }

            public Task<ProbeOutcome> RunAsync(TrafficLightTarget target, CancellationToken ct)
            {
                lock (Asked) Asked.Add(target.Kind + " " + target.Address);
                return Task.FromResult(_answer(target));
            }
        }

        private static TrafficLightProbeSet Probes(FakeAsync ping, FakeAsync tcp)
            => new TrafficLightProbeSet(new Dictionary<string, IAsyncTrafficProbe>
            {
                [TrafficLightKinds.Ping] = ping, [TrafficLightKinds.Tcp] = tcp,
            }, null);

        private static TrafficLightTarget Machine()
            => new TrafficLightTarget { Id = "m", Kind = "ping", Address = "SZAMEDIA", Label = "SZAMEDIA", Ordinal = 3 };

        [Fact]
        public async Task AMachineThatAnswersPingStaysPingAndIsNeverProbedOnAnyPort()
        {
            var ping = new FakeAsync(_ => ProbeOutcome.Good(2));
            var tcp = new FakeAsync(_ => ProbeOutcome.Good(2));

            TrafficLightTarget result = await TrafficLightSmartAdd.ResolveMachineAsync(Machine(), Probes(ping, tcp), CancellationToken.None);

            Assert.Equal("ping", result.Kind);
            Assert.Equal("SZAMEDIA", result.Address);
            Assert.Empty(tcp.Asked);
        }

        [Fact]
        public async Task AMachineThatDropsIcmpButAnswersSmbBecomesTcp445AndNothingElseIsTried()
        {
            var ping = new FakeAsync(_ => ProbeOutcome.Fail(TrafficReasons.Timeout));
            var tcp = new FakeAsync(t => t.Address.EndsWith(":445", StringComparison.Ordinal) ? ProbeOutcome.Good(3) : ProbeOutcome.Fail(TrafficReasons.ConnectionRefused));

            TrafficLightTarget result = await TrafficLightSmartAdd.ResolveMachineAsync(Machine(), Probes(ping, tcp), CancellationToken.None);

            Assert.Equal("tcp", result.Kind);
            Assert.Equal("SZAMEDIA:445", result.Address);
            Assert.Equal("m", result.Id);
            Assert.Equal(new[] { "tcp SZAMEDIA:445" }, tcp.Asked.ToArray()); // one port, and it is 445
        }

        [Fact]
        public async Task AMachineThatAnswersNeitherIsAddedAsPingAndWillBeRed()
        {
            var ping = new FakeAsync(_ => ProbeOutcome.Fail(TrafficReasons.Timeout));
            var tcp = new FakeAsync(_ => ProbeOutcome.Fail(TrafficReasons.ConnectionRefused));

            TrafficLightTarget result = await TrafficLightSmartAdd.ResolveMachineAsync(Machine(), Probes(ping, tcp), CancellationToken.None);

            Assert.Equal("ping", result.Kind);
            Assert.Equal("SZAMEDIA", result.Address);
            Assert.Single(tcp.Asked);
        }
    }

    /// <summary>The opt-in slice of the settings file (ticket S0069 D10).</summary>
    public class TrafficLightSettingsFileTests
    {
        private static AppConfig WithTargets()
        {
            var config = new AppConfig();
            config.TrafficLightTargets.Add(new TrafficLightTarget
            {
                Id = "a", Label = "Office NAS", Kind = "sftp", Address = "nas.local:22", Notify = true, IntervalSeconds = 30,
            });
            return config;
        }

        [Fact]
        public void AnExportAlwaysCarriesTheTargets()
        {
            // S0072 D3: the S0069 D10 export ask retired - the file is declared complete, and the
            // privacy line lives in the confirmation and on the General page.
            string text = SettingsFileWriter.Write(WithTargets(), DateTime.UtcNow, "1.0");
            Assert.Contains("TrafficLightTargets", text);
            Assert.Contains("nas.local:22", text);
        }

        [Fact]
        public void TheModuleSwitchIsPartOfTheFileSinceV2()
        {
            var config = WithTargets();
            config.EnableTrafficLight = true;
            string text = SettingsFileWriter.Write(config, DateTime.UtcNow, "1.0");

            Assert.Contains("EnableTrafficLight", text);
            Assert.DoesNotContain("EnableTrafficLight", SettingsFileInventory.Excluded.Keys);
            Assert.Contains(SettingsFileInventory.Exported, e => e.Key == "EnableTrafficLight");
        }

        [Fact]
        public void TheTargetsImportWithoutASeparateAskAndTheModuleStaysAsItWas()
        {
            string text = SettingsFileWriter.Write(WithTargets(), DateTime.UtcNow, "1.0");
            SettingsFileDocument document = SettingsFileReader.Read(Encoding.UTF8.GetBytes(text));
            var local = new AppConfig();

            SettingsImportPlan plan = SettingsImportPlan.Build(local, document);

            SettingsImportChange change = Assert.Single(plan.Changes, c => c.Entry.Key == "TrafficLightTargets");
            Assert.False(change.OptIn); // S0072 5.3: the file is complete; the import's record is the consent
            Assert.Single(plan.Candidate.TrafficLightTargets);
            Assert.Equal("nas.local:22", plan.Candidate.TrafficLightTargets[0].Address);
            Assert.False(plan.Candidate.EnableTrafficLight); // the module is left as it was: off
        }

        [Fact]
        public void ARowOfTheWrongKindCostsTheWholeTableAndNothingElse()
        {
            string json = "{\"format\":\"CyrFlip Settings\",\"schemaVersion\":1,\"settings\":{\"Theme\":\"dark\"},"
                + "\"tables\":{\"TrafficLightTargets\":[{\"Id\":\"a\",\"Kind\":\"ping\",\"Address\":\"1.1.1.1\",\"Notify\":\"yes\"}]}}";
            SettingsFileDocument document = SettingsFileReader.Read(Encoding.UTF8.GetBytes(json));

            Assert.False(document.Values.ContainsKey("TrafficLightTargets"));
            Assert.Contains(document.Issues, i => i.Key == "TrafficLightTargets");
            Assert.Equal("dark", document.Values["Theme"]);
        }

        [Fact]
        public void AnImportedAddressThatDoesNotValidateIsLoadedAsWrittenAndNeverProbed()
        {
            var config = new AppConfig();
            config.TrafficLightTargets.Add(new TrafficLightTarget { Id = "bad", Kind = "tcp", Address = "no-port", Label = "x" });
            var engine = new TrafficLightEngine();
            engine.SetTargets(config.TrafficLightTargets);

            Assert.Equal("no-port", config.TrafficLightTargets[0].Address);   // not repaired behind the user's back
            Assert.Empty(engine.CollectDue());
            Assert.True(engine.Snapshot()[0].InvalidAddress);
        }
    }

    /// <summary>The chord (ticket S0069 D11, D16): bound only while the module and its own switch are on.</summary>
    public class TrafficLightChordTests
    {
        private static KBDLLHOOKSTRUCT Key(uint vk, uint time) => new KBDLLHOOKSTRUCT { vkCode = vk, time = time };

        private static KeyboardHook Hook()
        {
            var hook = new KeyboardHook(_ => (IntPtr)1, _ => true, new PhysicalModifiers());
            hook.Install(Hotkey.CaseDefault, Hotkey.Parse("Ctrl+Shift+F10"), Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey),
                deferInRemoteClient: false, enabled: true, caseEnabled: true, historyEnabled: true, quickNotesEnabled: false);
            return hook;
        }

        private static bool Press(KeyboardHook hook, uint time)
        {
            hook.Decide(Key(0xA2, time), down: true);       // LCtrl
            hook.Decide(Key(0xA0, time + 1), down: true);   // LShift
            hook.Decide(Key(0xA4, time + 2), down: true);   // LAlt
            return hook.Decide(Key(0x54, time + 3), down: true); // T
        }

        [Fact]
        public void TheDefaultChordIsFreeAmongEveryShippedDefault()
        {
            Hotkey chord = Hotkey.Parse(AppConfig.DefaultTrafficLightHotkey);
            var config = new AppConfig();
            ChordRegistry registry = ChordRegistry.Build(config, new LauncherScenario[0], new LanguageHotkeys.Entry[0]);

            Assert.Null(registry.CyrFlipOwnerOf(chord, ChordKind.TrafficLight));
            Assert.Empty(registry.Duplicates());
        }

        [Fact]
        public void ADisabledModuleLeavesTheKeystrokeToWhateverElseWantsIt()
        {
            KeyboardHook hook = Hook();
            int fired = 0;
            hook.TrafficLightHotkeyPressed += (_, _) => fired++;
            hook.UpdateTrafficLightHotkey(Hotkey.Parse(AppConfig.DefaultTrafficLightHotkey));
            hook.UpdateTrafficLightEnabled(false);

            Assert.False(Press(hook, 100));
            Assert.Equal(0, fired);
        }

        [Fact]
        public void AnEnabledModuleSwallowsTheChordAndRaisesTheEvent()
        {
            KeyboardHook hook = Hook();
            int fired = 0;
            hook.TrafficLightHotkeyPressed += (_, _) => fired++;
            hook.UpdateTrafficLightHotkey(Hotkey.Parse(AppConfig.DefaultTrafficLightHotkey));
            hook.UpdateTrafficLightEnabled(true);

            Assert.True(Press(hook, 100));
            Assert.Equal(1, fired);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, true)]
        public void LiveMeansTheModuleAndItsOwnSwitch(bool module, bool row, bool live)
        {
            var config = new AppConfig { EnableTrafficLight = module, EnableTrafficLightHotkey = row };
            Assert.Equal(live, ChordBinding.TrafficLightLive(config));
            Assert.Equal(live, ChordBinding.IsTrafficLightActive(config));
            config.EnableHotkeys = false;
            Assert.False(ChordBinding.IsTrafficLightActive(config)); // the master switch gates it too
        }

        [Fact]
        public void TheOverviewResetPutsTheShippedChordBack()
        {
            Assert.True(ChordDefaults.IsFixed(ChordKind.TrafficLight));
            Assert.Equal("Ctrl+Shift+Alt+T", ChordDefaults.FixedChord(ChordKind.TrafficLight));
        }
    }

    /// <summary>The board window and the details panel in every language (ticket S0069 section 6).</summary>
    [Collection(SharedGdiCollection.Name)]
    public class TrafficLightWindowTests
    {
        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in All(child)) yield return deeper;
            }
        }

        private static bool HasCyrillic(string? text)
            => !string.IsNullOrEmpty(text) && text!.Any(c => c >= 'Ѐ' && c <= 'ӿ');

        private static TrafficLightTile Tile(string label, TrafficState state, string reason = "ok")
            => new TrafficLightTile
            {
                Target = new TrafficLightTarget { Id = label, Label = label, Kind = "ping", Address = "10.0.0.1", Group = "Office" },
                State = state, Reason = reason, CheckedAtUtc = DateTime.UtcNow, LatencyMs = state == TrafficState.Down ? (int?)null : 12,
            };

        [UiSweepFact]
        public void TheBoardBuildsInAllThirteenLanguagesWithNoSurvivingCyrillicInLatinBuilds()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    string code = Localization.Codes[i];
                    var config = new AppConfig { UiLanguage = language, EnableTrafficLight = true };
                    var actions = new TrafficLightBoardActions
                    {
                        Snapshot = () => new List<TrafficLightTile>
                        {
                            Tile("One", TrafficState.Ok), Tile("Two", TrafficState.Degraded, "slow"),
                            Tile("Three", TrafficState.Down, "timeout"), Tile("Four", TrafficState.Unknown, ""),
                        },
                        Summary = () => new TrafficLightSummary { Total = 4, Down = 1 },
                    };
                    using var window = new TrafficLightWindow(config, actions, language);
                    window.CreateControl();
                    window.RefreshBoard();

                    Assert.False(string.IsNullOrWhiteSpace(window.Text), language + ": no title");
                    if (code != "ru" && code != "uk")
                        foreach (Control c in All(window))
                        {
                            if (HasCyrillic(c.Text)) problems.Add(language + ": " + c.GetType().Name + " text '" + c.Text + "'");
                            if (HasCyrillic(c.AccessibleName)) problems.Add(language + ": " + c.GetType().Name + " name '" + c.AccessibleName + "'");
                        }

                    // One accessible child per tile, each with a name and a value that carries the state word.
                    AccessibleObject board = window.Board.AccessibilityObject;
                    Assert.Equal(4, board.GetChildCount());
                    for (int child = 0; child < 4; child++)
                    {
                        AccessibleObject tile = board.GetChild(child)!;
                        Assert.False(string.IsNullOrEmpty(tile.Name));
                        Assert.False(string.IsNullOrEmpty(tile.Value));
                        if (code != "ru" && code != "uk" && HasCyrillic(tile.Value)) problems.Add(language + ": value '" + tile.Value + "'");
                    }
                }
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void TheTooltipOfATileSaysWhatTheTileIsInEveryLanguage()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    string T(string ru) => Localization.Translate(language, ru);
                    string tip = TrafficLightText.Tooltip(Tile("Router", TrafficState.Down, "timeout"), T, DateTime.UtcNow, TimeZoneInfo.Local);
                    if (!tip.Contains("Router")) problems.Add(language + ": no label");
                    if (!tip.Contains("10.0.0.1")) problems.Add(language + ": no address");
                    if (tip.Contains("{0}") || tip.Contains("{1}")) problems.Add(language + ": a placeholder survived");
                }
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void TheBoardIsPinnedOnTopOrNotByTheSwitchAndKeepsItsGeometryWhenHidden()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { TrafficLightPinned = true };
                using var window = new TrafficLightWindow(config, new TrafficLightBoardActions(), "English");
                Assert.True(window.TopMost);
                config.TrafficLightPinned = false;
                window.ApplyPinned();
                Assert.False(window.TopMost);
            });
        }

        [Fact]
        public void TheMarkIsAShapeNotAHue()
        {
            var marks = new[] { TrafficState.Ok, TrafficState.Degraded, TrafficState.Down, TrafficState.Unknown }
                .Select(s => TrafficLightBoard.MarkOf(Tile("x", s))).ToArray();
            Assert.Equal(4, marks.Distinct().Count());
            Assert.All(marks, m => Assert.NotEmpty(m));
            Assert.Equal("", TrafficLightBoard.MarkOf(new TrafficLightTile { Target = new TrafficLightTarget { Enabled = false } }));
        }

        [Fact]
        public void TheStateIsAVocabularyGlyphAndEachStateIsItsOwnVendoredShape()
        {
            TrafficState[] states = { TrafficState.Ok, TrafficState.Degraded, TrafficState.Down, TrafficState.Unknown };
            string?[] ids = states.Select(s => TrafficLightBoard.GlyphOf(Tile("x", s))).ToArray();
            Assert.Equal(new[] { "status.ok", "status.warning", "status.error", "status.unknown" }, ids);
            Assert.All(ids, id => Assert.True(GlyphRenderer.Has(id!), id));

            // Four ids are not four shapes if the catalog ever maps two of them to one path.
            string[] signatures = ids.Select(id =>
            {
                using Bitmap bitmap = GlyphRenderer.Render(id!, 24, Color.Black)!;
                var sb = new StringBuilder();
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++) sb.Append(bitmap.GetPixel(x, y).A > 127 ? '#' : '.');
                return sb.ToString();
            }).ToArray();
            Assert.Equal(4, signatures.Distinct().Count());

            Assert.Null(TrafficLightBoard.GlyphOf(new TrafficLightTile { Target = new TrafficLightTarget { Enabled = false } }));
            Assert.Equal("status.error", TrafficLightBoard.GlyphOf(new TrafficLightTile { Target = new TrafficLightTarget(), InvalidAddress = true }));
        }

        [Fact]
        public void TheMarkCellOfATilePaintsTheStatesGlyphInTheInkRole()
        {
            // Painted for real, the mark square of a tile must be the state's vocabulary glyph in the ink role:
            // its ink pixels agree with the glyph rendered on its own, and agree with it better than with the
            // glyph of any other state - so a typographic fallback, or a swapped id, cannot pass.
            OnUiThread(() =>
            {
                Color ink = ThemePalette.Light.StatusInk;
                TrafficState[] states = { TrafficState.Ok, TrafficState.Degraded, TrafficState.Down, TrafficState.Unknown };
                var painted = new List<bool[]>();
                var references = new List<bool[]>();
                int side = 0;
                foreach (TrafficState state in states)
                {
                    using var board = new TrafficLightBoard { Size = new Size(420, 220) };
                    board.SetTranslator(s => s, () => DateTime.UtcNow);
                    board.SetTiles(new List<TrafficLightTile>
                    {
                        new TrafficLightTile
                        {
                            Target = new TrafficLightTarget { Id = "t", Label = "Router", Kind = "ping", Address = "10.0.0.1" },
                            State = state, Reason = state == TrafficState.Down ? "timeout" : "ok", CheckedAtUtc = DateTime.UtcNow, LatencyMs = 12,
                        },
                    });
                    board.CreateControl();
                    using var bitmap = new Bitmap(board.Width, board.Height);
                    board.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

                    BoardItem item = TrafficLightBoardLayout.Compute(board.Tiles.ToList(), board.ClientSize.Width, board.TileSize, board.Gap,
                        board.Font.Height + 4, false, out _).Single(i => i.IsTile);
                    Rectangle mark = TrafficLightBoard.MarkRect(item.Bounds, false);
                    side = mark.Width;

                    var cells = new bool[mark.Width * mark.Height];
                    for (int y = 0; y < mark.Height; y++)
                        for (int x = 0; x < mark.Width; x++)
                        {
                            Color p = bitmap.GetPixel(mark.Left + x, mark.Top + y);
                            cells[y * mark.Width + x] = Math.Abs(p.R - ink.R) < 70 && Math.Abs(p.G - ink.G) < 70 && Math.Abs(p.B - ink.B) < 70;
                        }
                    painted.Add(cells);

                    using Bitmap reference = GlyphRenderer.Render(TrafficLightBoard.GlyphOf(new TrafficLightTile { Target = new TrafficLightTarget(), State = state })!, side, ink)!;
                    var expected = new bool[side * side];
                    for (int y = 0; y < side; y++)
                        for (int x = 0; x < side; x++) expected[y * side + x] = reference.GetPixel(x, y).A > 127;
                    references.Add(expected);
                }

                static double Agreement(bool[] a, bool[] b)
                {
                    int same = 0;
                    for (int i = 0; i < a.Length; i++) if (a[i] == b[i]) same++;
                    return (double)same / a.Length;
                }

                Assert.True(side >= 16, "the mark square is " + side + " px - too small to tell shapes apart");
                for (int s = 0; s < states.Length; s++)
                {
                    Assert.True(references[s].Count(c => c) >= 40, states[s] + ": the reference glyph is nearly empty");
                    double own = Agreement(painted[s], references[s]);
                    Assert.True(own >= 0.93, states[s] + ": the painted mark agrees with its glyph on only " + own.ToString("P0"));
                    for (int other = 0; other < states.Length; other++)
                    {
                        if (other == s) continue;
                        double crossed = Agreement(painted[s], references[other]);
                        Assert.True(own > crossed, states[s] + ": the painted mark agrees with " + states[other] + "'s glyph as much as its own");
                    }
                }
            });
        }

        private static List<TrafficLightTile> Four() => new List<TrafficLightTile>
        {
            Tile("One", TrafficState.Ok), Tile("Two", TrafficState.Degraded, "slow"),
            Tile("Three", TrafficState.Down, "timeout"), Tile("Four", TrafficState.Unknown, ""),
        };

        [Fact]
        public void TheWindowIsTheGridsSizeAndAnotherRowComesAtOnce()
        {
            OnUiThread(() =>
            {
                List<TrafficLightTile> tiles = Four();
                var config = new AppConfig { TrafficLightColumns = 3, TrafficLightTileSize = 1 };
                var actions = new TrafficLightBoardActions { Snapshot = () => tiles.ToList(), Summary = () => new TrafficLightSummary { Total = 4 } };
                using var window = new TrafficLightWindow(config, actions, "English");
                window.CreateControl();

                tiles = Four().Take(3).ToList();
                window.RefreshBoard();
                Size oneRow = window.FitSize();
                Assert.Equal(3, window.Board.EffectiveColumns);

                tiles = Four();                       // four targets on three columns: two rows, no scrolling
                window.RefreshBoard();
                Size twoRows = window.FitSize();

                Assert.Equal(oneRow.Width, twoRows.Width);
                Assert.Equal(window.Board.TileSize.Height + window.Board.Gap, twoRows.Height - oneRow.Height);

                // The same four on two columns are two rows too; on four columns, one.
                config.TrafficLightColumns = 4;
                window.ApplyBoardOptions();
                window.RefreshBoard();
                Assert.Equal(4, window.Board.EffectiveColumns);
                Assert.True(window.FitSize().Width > twoRows.Width);
                Assert.Equal(oneRow.Height, window.FitSize().Height);
            });
        }

        [Fact]
        public void TheBareSquareStepHasNoStatusLineNoTitleTextAndNoLabelInsideTheTile()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { TrafficLightColumns = 3, TrafficLightTileSize = 0 };
                var actions = new TrafficLightBoardActions
                {
                    Snapshot = () => Four(), Summary = () => new TrafficLightSummary { Total = 4, Down = 1 },
                };
                using var window = new TrafficLightWindow(config, actions, "English");
                window.CreateControl();
                window.RefreshBoard();

                Assert.False(window.StatusLineVisible);
                Assert.False(window.TitleBar.ShowTitle);
                Assert.True(window.Board.IsBareSquare);
                Assert.Equal(window.Board.TileSize.Width, window.Board.TileSize.Height);

                // The same targets on the medium step have the status line and are taller by it.
                Size bare = window.FitSize();
                config.TrafficLightTileSize = 1;
                window.ApplyBoardOptions();
                window.RefreshBoard();
                Assert.True(window.StatusLineVisible);
                Size medium = window.FitSize();
                Assert.True(medium.Height > bare.Height, "bare " + bare + ", medium " + medium + ", tile " + window.Board.TileSize
                    + ", gap " + window.Board.Gap + ", columns " + window.Board.EffectiveColumns);
            });
        }

        [Fact]
        public void MicroscopicSquaresAreHalfTheSmallSizeAndTheWindowHasOnlyTheGrid()
        {
            OnUiThread(() =>
            {
                var tiles = Four();
                foreach (var tile in tiles) tile.Target.Group = "Office";
                var config = new AppConfig { TrafficLightColumns = 3, TrafficLightTileSize = 0 };
                using var window = new TrafficLightWindow(config, new TrafficLightBoardActions { Snapshot = () => tiles }, "English");
                Size small = window.Board.TileSize;
                Assert.True(OwnVisibility.Of(window.TitleBar));

                config.TrafficLightTileSize = AppConfig.MicroscopicTrafficLightTileSize;
                window.RefreshBoard();
                Assert.Equal(new Size(small.Width / 2, small.Height / 2), window.Board.TileSize);
                Assert.False(OwnVisibility.Of(window.TitleBar));
                Assert.False(window.StatusLineVisible);
                Assert.Equal(window.Board.GridWidth + 2, window.FitSize().Width);
                Assert.Equal(window.Board.ContentHeightFor(window.Board.GridWidth) + 2, window.FitSize().Height);
                Assert.Equal(2 * window.Board.TileSize.Height + 4 * window.Board.Gap,
                    window.Board.ContentHeightFor(window.Board.GridWidth));

                config.TrafficLightTileSize = 0;
                window.RefreshBoard();
                Assert.True(OwnVisibility.Of(window.TitleBar));
                Assert.Equal(small, window.Board.TileSize);
            });
        }

        private static void Pointer(Control control, string method, MouseButtons button, Point at, int clicks = 1)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Control).GetMethod(method, flags)!.Invoke(control,
                new object[] { new MouseEventArgs(button, clicks, at.X, at.Y, 0) });
        }

        [Fact]
        public void MicroscopicTilesKeepTooltipsDragAfterMovementAndOpenSettingsOnDoubleClick()
        {
            OnUiThread(() =>
            {
                int edits = 0, checks = 0, drags = 0;
                var config = new AppConfig { TrafficLightTileSize = AppConfig.MicroscopicTrafficLightTileSize };
                using var window = new TrafficLightWindow(config, new TrafficLightBoardActions
                {
                    Snapshot = Four, EditTargets = () => edits++, CheckNow = _ => checks++,
                }, "English");
                var board = window.Board;
                board.Size = new Size(400, 200);
                board.CreateControl();
                var items = TrafficLightBoardLayout.Compute(board.Tiles.ToList(), board.Width,
                    board.EffectiveColumns, board.TileSize, board.Gap, 0, false, out _);
                // Test the gesture without entering the native window-move loop.
                using var gestures = new TrafficLightBoard { Size = board.Size };
                gestures.SetSizeStep(AppConfig.MicroscopicTrafficLightTileSize);
                gestures.SetTiles(Four());
                gestures.CreateControl();
                gestures.DragRequested += () => drags++;
                foreach (var item in items.Where(i => i.IsTile))
                {
                    Point at = Centre(item.Bounds);
                    MoveTo(board, at);
                    Assert.Equal(board.TooltipFor(item.TileIndex), board.HoverTipText);
                    Pointer(gestures, "OnMouseDown", MouseButtons.Left, at);
                    Assert.Equal(item.TileIndex, gestures.FocusedTile);
                    int before = drags;
                    Pointer(gestures, "OnMouseMove", MouseButtons.Left, at);
                    Assert.Equal(before, drags);
                    Pointer(gestures, "OnMouseMove", MouseButtons.Left,
                        new Point(at.X + SystemInformation.DragSize.Width + 1, at.Y));
                    Assert.Equal(before + 1, drags);
                    Pointer(gestures, "OnMouseUp", MouseButtons.Left, at);

                    Pointer(board, "OnMouseDown", MouseButtons.Left, at);
                    Pointer(board, "OnMouseUp", MouseButtons.Left, at);
                    Pointer(board, "OnMouseDoubleClick", MouseButtons.Left, at, 2);
                    Pointer(board, "OnMouseUp", MouseButtons.Left, at);
                }
                Assert.Equal(4, edits);
                Assert.Equal(0, checks);
                config.TrafficLightTileSize = 0;
                window.RefreshBoard();
                var smallItems = TrafficLightBoardLayout.Compute(board.Tiles.ToList(), board.Width,
                    board.EffectiveColumns, board.TileSize, board.Gap, board.Font.Height + 4, false, out _);
                Pointer(board, "OnMouseDoubleClick", MouseButtons.Left,
                    Centre(smallItems.First(i => i.IsTile).Bounds), 2);
                Assert.Equal(4, edits);
                Assert.Equal(1, checks);
            });
        }

        private static TrafficLightTile TileOf(TrafficState state, bool enabled = true) => new TrafficLightTile
        {
            Target = new TrafficLightTarget { Id = "t", Label = "Router", Kind = "ping", Address = "10.0.0.1", Enabled = enabled },
            State = state, Reason = state == TrafficState.Down ? "timeout" : "ok", CheckedAtUtc = DateTime.UtcNow, LatencyMs = 12,
        };

        /// <summary>One tile painted for real on a board of the given step; the bitmap, the tile's bounds and its mark square.</summary>
        private static (Bitmap Picture, Rectangle Bounds, Rectangle Mark) PaintOne(int step, TrafficLightTile tile)
        {
            using var board = new TrafficLightBoard { Size = new Size(420, 220) };
            board.SetTranslator(s => s, () => DateTime.UtcNow);
            board.SetSizeStep(step);
            board.SetColumns(3);
            board.SetTiles(new List<TrafficLightTile> { tile });
            board.CreateControl();
            var bitmap = new Bitmap(board.Width, board.Height);
            board.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

            BoardItem item = TrafficLightBoardLayout.Compute(board.Tiles.ToList(), board.ClientSize.Width,
                board.EffectiveColumns, board.TileSize, board.Gap, board.Font.Height + 4, false, out _).Single(i => i.IsTile);
            return (bitmap, item.Bounds, TrafficLightBoard.MarkRect(item.Bounds, false, centered: step == 0));
        }

        private static int Differing(Bitmap picture, Rectangle area, Color fill, Rectangle? except = null)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    if (except.HasValue && except.Value.Contains(x, y)) continue;
                    Color p = picture.GetPixel(x, y);
                    if (Math.Abs(p.R - fill.R) > 40 || Math.Abs(p.G - fill.G) > 40 || Math.Abs(p.B - fill.B) > 40) count++;
                }
            return count;
        }

        /// <summary>The most colour changes along one of the three rows through a tile's top edge, corners left out.</summary>
        private static int EdgeChanges(Bitmap picture, Rectangle bounds)
        {
            int best = 0;
            int from = bounds.Left + 8, to = bounds.Right - 8;
            for (int y = bounds.Top - 1; y <= bounds.Top + 1; y++)
            {
                int changes = 0;
                for (int x = from; x < to - 1; x++)
                {
                    Color a = picture.GetPixel(x, y), b = picture.GetPixel(x + 1, y);
                    if (Math.Abs(a.R - b.R) > 24 || Math.Abs(a.G - b.G) > 24 || Math.Abs(a.B - b.B) > 24) changes++;
                }
                best = Math.Max(best, changes);
            }
            return best;
        }

        [Fact]
        public void ABareSquareOfAnAvailableTargetIsPlainGreenAndEveryOtherStateKeepsItsGlyph()
        {
            // Owner, 2026-10-13: a tick on a green square says nothing the green does not. So on the bare square
            // "available" has no mark; every other state keeps its glyph, and no mark is itself the reading.
            Assert.False(TrafficLightBoard.MarkShown(TileOf(TrafficState.Ok), bareSquare: true));
            Assert.True(TrafficLightBoard.MarkShown(TileOf(TrafficState.Ok), bareSquare: false));
            foreach (TrafficState state in new[] { TrafficState.Degraded, TrafficState.Down, TrafficState.Unknown })
                Assert.True(TrafficLightBoard.MarkShown(TileOf(state), bareSquare: true), state.ToString());
            var invalid = TileOf(TrafficState.Ok);
            invalid.InvalidAddress = true;
            Assert.True(TrafficLightBoard.MarkShown(invalid, bareSquare: true));     // an invalid address is never "plain green"

            OnUiThread(() =>
            {
                // Painted: the available tile is the fill from edge to edge, mark square included.
                (Bitmap ok, Rectangle okBounds, _) = PaintOne(0, TileOf(TrafficState.Ok));
                using (ok)
                    Assert.Equal(0, Differing(ok, Rectangle.Inflate(okBounds, -3, -3), ThemePalette.Light.StatusOkFill));

                // The check can fail: a medium tile has its label and detail on the fill.
                (Bitmap medium, Rectangle mediumBounds, _) = PaintOne(1, TileOf(TrafficState.Ok));
                using (medium)
                    Assert.True(Differing(medium, Rectangle.Inflate(mediumBounds, -3, -3), ThemePalette.Light.StatusOkFill) > 0);

                // The other three states draw their glyph in the mark square, and nothing outside it.
                var fills = new Dictionary<TrafficState, Color>
                {
                    { TrafficState.Degraded, ThemePalette.Light.StatusWarningFill },
                    { TrafficState.Down, ThemePalette.Light.StatusErrorFill },
                    { TrafficState.Unknown, ThemePalette.Light.StatusUnknownFill },
                };
                foreach (KeyValuePair<TrafficState, Color> pair in fills)
                {
                    (Bitmap picture, Rectangle bounds, Rectangle mark) = PaintOne(0, TileOf(pair.Key));
                    using (picture)
                    {
                        Rectangle inner = Rectangle.Inflate(bounds, -3, -3);
                        Assert.True(Differing(picture, mark, pair.Value) >= 20, pair.Key + ": the glyph is missing from the bare square");
                        Assert.Equal(0, Differing(picture, inner, pair.Value, except: mark));
                    }
                }
            });
        }

        [Fact]
        public void ASwitchedOffTargetOnTheBareSquareIsToldFromAvailableByItsDashedEdge()
        {
            // With no mark on "available", a switched-off target (muted plate, no glyph either) must not rest on
            // the plate's hue: its edge is dashed on the bare square, and solid on every other step.
            OnUiThread(() =>
            {
                (Bitmap off, Rectangle offBounds, _) = PaintOne(0, TileOf(TrafficState.Unknown, enabled: false));
                using (off) Assert.True(EdgeChanges(off, offBounds) >= 4, "the switched-off edge is not dashed");

                (Bitmap ok, Rectangle okBounds, _) = PaintOne(0, TileOf(TrafficState.Ok));
                using (ok) Assert.True(EdgeChanges(ok, okBounds) <= 1, "an available tile's edge is solid");

                (Bitmap medium, Rectangle mediumBounds, _) = PaintOne(1, TileOf(TrafficState.Unknown, enabled: false));
                using (medium) Assert.True(EdgeChanges(medium, mediumBounds) <= 1, "a medium tile keeps its solid edge");
            });
        }

        [Fact]
        public void TheTitleBarHasItsFourButtonsAtTheTrailingEdgeAndHelpBeforeThemAndMirrorsForRightToLeft()
        {
            OnUiThread(() =>
            {
                using var bar = new TrafficLightTitleBar { Width = 400 };
                bar.Height = bar.Side;
                int side = bar.Side;

                Assert.Equal(new[] { TitleCommand.Add, TitleCommand.Edit, TitleCommand.Pin, TitleCommand.Close },
                    Enumerable.Range(0, 4).Select(bar.CommandAt).ToArray());
                Assert.Equal(400, bar.ZoneOf(3).Right);                         // close is the last, at the edge
                Assert.Equal(bar.ZoneOf(0).Right, bar.ZoneOf(1).Left);          // the four are adjacent
                Assert.Equal(4 * side, 400 - bar.ZoneOf(0).Left);

                Assert.Equal(TitleCommand.Help, bar.CommandAt(4));
                Assert.Equal(bar.ZoneOf(4).Right, bar.ZoneOf(0).Left);
                bar.RightToLeft = RightToLeft.Yes;
                Assert.Equal(0, bar.ZoneOf(3).Left);                            // mirrored: close at the leading edge
                Assert.Equal(4 * side, bar.ZoneOf(0).Right);
                Assert.True(bar.ZoneOf(0).Left > bar.ZoneOf(3).Left);
            });
        }

        [Fact]
        public void EachButtonIsAVocabularyGlyphAndThePinFollowsItsState()
        {
            Assert.Equal("action.add", TrafficLightTitleBar.GlyphOf(TitleCommand.Add, true));
            Assert.Equal("action.edit", TrafficLightTitleBar.GlyphOf(TitleCommand.Edit, true));
            Assert.Equal("nav.close", TrafficLightTitleBar.GlyphOf(TitleCommand.Close, true));
            Assert.Equal("action.pin", TrafficLightTitleBar.GlyphOf(TitleCommand.Pin, true));
            Assert.Equal("action.pin--off", TrafficLightTitleBar.GlyphOf(TitleCommand.Pin, false));
            foreach (TitleCommand command in Enum.GetValues(typeof(TitleCommand)))
                Assert.True(GlyphRenderer.Has(TrafficLightTitleBar.GlyphOf(command, true)), command.ToString());
        }

        private static void MoveTo(Control control, Point at)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Control).GetMethod("OnMouseMove", flags)!.Invoke(control, new object[] { new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, 0) });
        }

        [Fact]
        public void HoveringATileOrAButtonSetsTheTipOfTheControlItselfAndLeavingClearsIt()
        {
            // ToolTip.Show(.., window, ..) shows only while the window is the active one, and the board is never
            // activated by the mouse - so the hover tips must be the control's own tool (SetToolTip), whose text
            // follows the pointer. This is the part a test can reach; that a tip really appears is the live probe.
            OnUiThread(() =>
            {
                using var board = new TrafficLightBoard { Size = new Size(420, 220) };
                board.SetTranslator(s => s, () => DateTime.UtcNow);
                board.SetColumns(3);
                board.SetTiles(Four());
                board.CreateControl();
                List<BoardItem> tiles = TrafficLightBoardLayout.Compute(board.Tiles.ToList(), board.ClientSize.Width,
                    board.EffectiveColumns, board.TileSize, board.Gap, board.Font.Height + 4, false, out _).Where(i => i.IsTile).ToList();

                Assert.Equal("", board.HoverTipText);
                MoveTo(board, Centre(tiles[0].Bounds));
                Assert.NotEqual("", board.HoverTipText);
                Assert.Equal(board.TooltipFor(0), board.HoverTipText);
                MoveTo(board, Centre(tiles[2].Bounds));
                Assert.Equal(board.TooltipFor(2), board.HoverTipText);
                Assert.NotEqual(board.TooltipFor(0), board.HoverTipText);
                MoveTo(board, new Point(board.ClientSize.Width - 2, board.ClientSize.Height - 2));
                Assert.Equal("", board.HoverTipText);

                using var bar = new TrafficLightTitleBar { Width = 400 };
                bar.Height = bar.Side;
                bar.CreateControl();
                Assert.Equal("", bar.HoverTipText);
                MoveTo(bar, Centre(bar.ZoneOf(2)));
                Assert.Equal(bar.CaptionOf(TitleCommand.Pin), bar.HoverTipText);
                MoveTo(bar, Centre(bar.ZoneOf(0)));
                Assert.Equal(bar.CaptionOf(TitleCommand.Add), bar.HoverTipText);
                MoveTo(bar, new Point(2, bar.Height / 2));
                Assert.Equal("", bar.HoverTipText);
            });
        }

        private static void Press(Control control, Point at)
        {
            var args = new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Control).GetMethod("OnMouseDown", flags)!.Invoke(control, new object[] { args });
            typeof(Control).GetMethod("OnMouseUp", flags)!.Invoke(control, new object[] { args });
        }

        private static Point Centre(Rectangle r) => new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);

        [Fact]
        public void AButtonOfTheBarIsACommandAndAnyOtherPressDragsTheWindow()
        {
            OnUiThread(() =>
            {
                using var bar = new TrafficLightTitleBar { Width = 400 };
                bar.Height = bar.Side;
                bar.CreateControl();
                var commands = new List<TitleCommand>();
                int drags = 0;
                bar.CommandInvoked += commands.Add;
                bar.DragRequested += () => drags++;

                for (int i = 0; i < 4; i++) Press(bar, Centre(bar.ZoneOf(i)));
                Assert.Equal(new[] { TitleCommand.Add, TitleCommand.Edit, TitleCommand.Pin, TitleCommand.Close }, commands.ToArray());
                Assert.Equal(0, drags);

                Press(bar, new Point(10, bar.Height / 2));
                Assert.Equal(4, commands.Count);
                Assert.Equal(1, drags);
            });
        }

        [Fact]
        public void TheWindowAnswersTheBarsFourButtonsAndNeverTakesTheFocusForIt()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { TrafficLightPinned = true };
                int added = 0, edited = 0, hidden = 0;
                bool? pinnedTo = null;
                var actions = new TrafficLightBoardActions
                {
                    AddTarget = () => added++, EditTargets = () => edited++, Hidden = () => hidden++,
                    PinnedChanged = value => pinnedTo = value,
                };
                using var window = new TrafficLightWindow(config, actions, "English");
                window.CreateControl();
                TrafficLightTitleBar bar = window.TitleBar;
                Assert.False(bar.CanSelect);          // a click is a command, not a change of focus
                bar.Width = 400;

                Press(bar, Centre(bar.ZoneOf(0)));
                Press(bar, Centre(bar.ZoneOf(1)));
                Assert.Equal(1, added);
                Assert.Equal(1, edited);

                Press(bar, Centre(bar.ZoneOf(2)));    // the pin: a switch
                Assert.False(config.TrafficLightPinned);
                Assert.False(window.TopMost);
                Assert.False(bar.Pinned);
                Assert.Equal(false, pinnedTo);
                Press(bar, Centre(bar.ZoneOf(2)));
                Assert.True(config.TrafficLightPinned);
                Assert.True(window.TopMost);
                Assert.True(bar.Pinned);

                Press(bar, Centre(bar.ZoneOf(3)));    // close = hide
                Assert.Equal(1, hidden);
            });
        }

        [UiSweepFact]
        public void TheBarsButtonsAreNamedForAScreenReaderInEveryLanguage()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    string code = Localization.Codes[i];
                    using var window = new TrafficLightWindow(new AppConfig { UiLanguage = language }, new TrafficLightBoardActions(), language);
                    window.CreateControl();
                    AccessibleObject bar = window.TitleBar.AccessibilityObject;
                    if (bar.GetChildCount() != 4) { problems.Add(language + ": " + bar.GetChildCount() + " buttons"); continue; }
                    var names = new List<string>();
                    for (int child = 0; child < 4; child++)
                    {
                        AccessibleObject button = bar.GetChild(child)!;
                        if (button.Role != AccessibleRole.PushButton) problems.Add(language + ": child " + child + " is " + button.Role);
                        names.Add(button.Name ?? "");
                        if (string.IsNullOrWhiteSpace(button.Name)) problems.Add(language + ": a nameless button");
                        if (code != "ru" && code != "uk" && HasCyrillic(button.Name)) problems.Add(language + ": '" + button.Name + "'");
                    }
                    if (names.Distinct().Count() != 4) problems.Add(language + ": two buttons share a name");
                }
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    /// <summary>The grid is a number of columns the person chose; the rows are what the targets make of it.</summary>
    public class TrafficLightColumnsTests
    {
        private static readonly Size Tile = new Size(100, 40);

        private static TrafficLightTile T(string id, string group = "")
            => new TrafficLightTile { Target = new TrafficLightTarget { Id = id, Label = id, Group = group } };

        private static List<TrafficLightTile> Many(int count, string group = "")
            => Enumerable.Range(0, count).Select(i => T("t" + i, group)).ToList();

        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 3)]
        public void ThreeColumnsAreAsManyRowsAsTheTargetsNeedAtOnce(int count, int rows)
        {
            List<BoardItem> items = TrafficLightBoardLayout.Compute(Many(count), 332, 3, Tile, 8, 20, false, out int height);

            Assert.Equal(rows, items.Where(i => i.IsTile).Select(i => i.Bounds.Top).Distinct().Count());
            Assert.True(height >= items.Max(i => i.Bounds.Bottom));
            Assert.True(items.Where(i => i.IsTile).All(i => i.Bounds.Right <= 332));
        }

        [Fact]
        public void TheWidthOfTheWindowDoesNotChangeTheColumns()
        {
            List<BoardItem> narrow = TrafficLightBoardLayout.Compute(Many(6), 200, 3, Tile, 8, 20, false, out _);
            List<BoardItem> wide = TrafficLightBoardLayout.Compute(Many(6), 900, 3, Tile, 8, 20, false, out _);

            Assert.Equal(narrow.Select(i => i.Bounds.Left).ToArray(), wide.Select(i => i.Bounds.Left).ToArray());
            Assert.Equal(2, wide.Select(i => i.Bounds.Top).Distinct().Count());
        }

        [Theory]
        [InlineData(0, 3, 3)]     // no targets: the chosen number, the empty message wants the width
        [InlineData(1, 3, 1)]
        [InlineData(2, 3, 2)]
        [InlineData(5, 3, 3)]
        [InlineData(5, 0, 1)]     // a number below one is one
        public void NoMoreColumnsThanTheTargetsFill(int count, int configured, int expected)
            => Assert.Equal(expected, TrafficLightBoardLayout.EffectiveColumns(Many(count), configured));

        [Fact]
        public void TheWidestGroupDecidesTheColumnsInUse()
        {
            var tiles = new List<TrafficLightTile> { T("a", "Office"), T("b", "Office"), T("c", "Office"), T("d", "Home") };
            Assert.Equal(3, TrafficLightBoardLayout.EffectiveColumns(tiles, 5));
            Assert.Equal(2, TrafficLightBoardLayout.EffectiveColumns(tiles, 2));
        }

        [Fact]
        public void AGridIsItsColumnsOfTilesAndTheGapsBetweenThem()
            => Assert.Equal(8 + 3 * (100 + 8), TrafficLightBoardLayout.GridWidth(3, 100, 8));

        [Fact]
        public void TheColumnsAreARegistryValueClampedOnLoadAndWrittenWithTheModule()
        {
            Assert.Equal(AppConfig.DefaultTrafficLightColumns, new AppConfig().TrafficLightColumns);
            Assert.Equal(3, AppConfig.DefaultTrafficLightColumns);

            var config = new AppConfig { EnableTrafficLight = true, TrafficLightColumns = 5 };
            var written = new FakeColumnsKey();
            config.SaveTo(written);
            Assert.Equal(5, written.Values["TrafficLightColumns"]);
            Assert.DoesNotContain("TrafficLightWidth", written.Values.Keys);
            Assert.DoesNotContain("TrafficLightHeight", written.Values.Keys);

            foreach (int[] pair in new[] { new[] { 0, 1 }, new[] { 99, 12 }, new[] { 4, 4 } })
            {
                var read = new FakeColumnsKey();
                read.Values["EnableTrafficLight"] = 1;
                read.Values["TrafficLightColumns"] = pair[0];
                Assert.Equal(pair[1], AppConfig.LoadFrom(read, out _).TrafficLightColumns);
            }
        }

        [Fact]
        public void MicroscopicTileSizeSurvivesSavingAndLoading()
        {
            var config = new AppConfig { EnableTrafficLight = true, TrafficLightTileSize = AppConfig.MicroscopicTrafficLightTileSize };
            var key = new FakeColumnsKey();
            config.SaveTo(key);
            Assert.Equal(AppConfig.MicroscopicTrafficLightTileSize, key.Values["TrafficLightTileSize"]);
            Assert.Equal(AppConfig.MicroscopicTrafficLightTileSize, AppConfig.LoadFrom(key, out _).TrafficLightTileSize);
        }

        [Fact]
        public void TheColumnsAreExportedWithTheTrafficLightsModule()
        {
            SettingsFileEntry entry = Assert.Single(SettingsFileInventory.Exported, e => e.Key == "TrafficLightColumns");
            Assert.Equal("trafficlight/module", entry.Group);
            Assert.Equal("Tiles in a row", entry.Caption);
            Assert.DoesNotContain("TrafficLightWidth", SettingsFileInventory.Excluded.Keys);
        }

        private sealed class FakeColumnsKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, Microsoft.Win32.RegistryValueKind kind) => Values[name] = value;
        }
    }

    /// <summary>
    /// The board window's plate fades with the setting while the title bar's buttons and the tiles stay
    /// solid: 0 the usual opaque window, 100 only the buttons and the tiles (the plate is see-through
    /// there and lets clicks pass to whatever is under it).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class TrafficLightWindowTransparencyTests
    {
        [Fact]
        public void NativeLayeredBitmapPreservesTransparentAndPremultipliedPixels()
        {
            using var surface = new Bitmap(2, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            surface.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
            surface.SetPixel(1, 0, Color.FromArgb(1, 1, 0, 0));
            surface.SetPixel(0, 1, Color.FromArgb(128, 64, 32, 16));
            surface.SetPixel(1, 1, Color.FromArgb(255, 50, 200, 80));
            IntPtr bitmap = TrafficLightWindow.CreateLayeredBitmap(surface);
            try
            {
                Assert.NotEqual(0, GetObject(bitmap, Marshal.SizeOf(typeof(NativeBitmap)), out NativeBitmap native));
                Assert.NotEqual(IntPtr.Zero, native.Bits);
                var pixels = new byte[16];
                Marshal.Copy(native.Bits, pixels, 0, pixels.Length);
                Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 1, 1, 16, 32, 64, 128, 80, 200, 50, 255 }, pixels);
            }
            finally { WindowInterop.DeleteObject(bitmap); }
        }

        [Fact]
        public void BoardIsPinnedByDefaultAndAnExplicitUnpinSurvivesLoading()
        {
            Assert.True(new AppConfig().TrafficLightPinned);
            var key = new FakeTransparencyKey();
            key.Values["EnableTrafficLight"] = 1;
            Assert.True(AppConfig.LoadFrom(key, out _).TrafficLightPinned);
            key.Values["TrafficLightPinned"] = 0;
            Assert.False(AppConfig.LoadFrom(key, out _).TrafficLightPinned);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeBitmap
        {
            public int Type, Width, Height, WidthBytes;
            public ushort Planes, BitsPixel;
            public IntPtr Bits;
        }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        private static extern int GetObject(IntPtr bitmap, int size, out NativeBitmap value);

        [Fact]
        public void ATransparentWindowIsLayeredWhileShownAndPlainAgainAtZero()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var config = new AppConfig { TrafficLightWindowTransparency = 100 };
                    using var window = new TrafficLightWindow(config, new TrafficLightBoardActions(), "English");
                    window.CreateControl();
                    // Born transparent: the style is on the handle before the window is ever shown.
                    Assert.NotEqual(0, GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED);
                    window.Show();
                    Application.DoEvents();
                    Assert.NotEqual(0, GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED);
                    config.TrafficLightWindowTransparency = 0;   // the settings page's own path
                    window.ApplyBoardOptions();
                    Application.DoEvents();
                    Assert.Equal(0, GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED);

                    config.TrafficLightWindowTransparency = 40;
                    window.ApplyBoardOptions();
                    Application.DoEvents();   // a pump that must drain: the compose pass may not feed itself
                    Assert.NotEqual(0, GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        [Fact]
        public void TheTransparencyIsARegistryValueClampedOnLoadAndWrittenWithTheModule()
        {
            Assert.Equal(0, new AppConfig().TrafficLightWindowTransparency);   // 0: the window paints the usual opaque way

            var config = new AppConfig { EnableTrafficLight = true, TrafficLightWindowTransparency = 60 };
            var written = new FakeTransparencyKey();
            config.SaveTo(written);
            Assert.Equal(60, written.Values["TrafficLightWindowTransparency"]);

            foreach (int[] pair in new[] { new[] { -5, 0 }, new[] { 105, 100 }, new[] { 40, 40 } })
            {
                var read = new FakeTransparencyKey();
                read.Values["EnableTrafficLight"] = 1;
                read.Values["TrafficLightWindowTransparency"] = pair[0];
                Assert.Equal(pair[1], AppConfig.LoadFrom(read, out _).TrafficLightWindowTransparency);
            }
        }

        [Fact]
        public void TheTransparencyIsExportedWithTheTrafficLightsModule()
        {
            SettingsFileEntry entry = Assert.Single(SettingsFileInventory.Exported, e => e.Key == "TrafficLightWindowTransparency");
            Assert.Equal("trafficlight/module", entry.Group);
            Assert.Equal("Board window transparency", entry.Caption);
        }

        [Fact]
        public void AllButtonsAndTilesHaveNoSolidBackgroundAtFullTransparency()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var config = new AppConfig { TrafficLightWindowTransparency = 100 };
                    var actions = new TrafficLightBoardActions
                    {
                        Snapshot = () => new List<TrafficLightTile>
                        {
                            new TrafficLightTile
                            {
                                Target = new TrafficLightTarget { Id = "t1", Label = "Host1", Kind = "ping", Address = "1.1.1.1", Enabled = true },
                                State = TrafficState.Ok,
                                LatencyMs = 15
                            }
                        }
                    };
                    using var window = new TrafficLightWindow(config, actions, "English");
                    window.CreateControl();
                    window.ShowBoard(takeFocus: false, IntPtr.Zero);
                    Application.DoEvents();

                    Assert.Equal(5, window.TitleBar.ButtonCount);

                    using var surface = new Bitmap(window.Width, window.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    window.DrawToBitmap(surface, new Rectangle(0, 0, window.Width, window.Height));
                    window.ApplyPlateAlpha(surface);

                    // Check all 5 buttons: for each button, background pixels must NOT be 255 (must be <= 1),
                    // and there must be glyph pixels with alpha == 255.
                    Point barAt = window.TitleBar.Location;
                    for (int i = 0; i < window.TitleBar.ButtonCount; i++)
                    {
                        Rectangle zone = window.TitleBar.ZoneOf(i);
                        zone.Offset(barAt);
                        int glyphPixels = 0;
                        int transparentPixels = 0;
                        for (int y = zone.Top; y < zone.Bottom; y++)
                        {
                            for (int x = zone.Left; x < zone.Right; x++)
                            {
                                Color c = surface.GetPixel(x, y);
                                if (c.A == 255) glyphPixels++;
                                else if (c.A <= 1) transparentPixels++;
                            }
                        }
                        Assert.True(glyphPixels > 0, $"Button {i} ({window.TitleBar.CommandAt(i)}) must have solid glyph pixels.");
                        Assert.True(transparentPixels > 0, $"Button {i} ({window.TitleBar.CommandAt(i)}) must have transparent background pixels.");
                    }

                    // Check tile: inside rounded rect must be alpha 255, corners outside must be alpha 0.
                    Point boardAt = window.Board.Location;
                    var tileBounds = window.Board.TileBounds();
                    Assert.Single(tileBounds);
                    Rectangle tile = tileBounds[0];
                    tile.Offset(boardAt);

                    Color centerPixel = surface.GetPixel(tile.Left + tile.Width / 2, tile.Top + tile.Height / 2);
                    Assert.Equal(255, centerPixel.A);

                    Color cornerPixel = surface.GetPixel(tile.Left, tile.Top);
                    Assert.Equal(0, cornerPixel.A);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private sealed class FakeTransparencyKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, Microsoft.Win32.RegistryValueKind kind) => Values[name] = value;
        }
    }

    /// <summary>What the board's "add" button makes of its two fields - pure and offline (ticket S0069 D24).</summary>
    public class TrafficLightQuickAddTests
    {
        private static QuickAddEvaluation Evaluate(string address, string label = "", params TrafficLightTarget[] existing)
            => TrafficLightQuickAdd.Evaluate(address, label, existing, s => s);

        [Fact]
        public void NothingTypedIsNothingToAdd()
        {
            Assert.Equal(QuickAddVerdict.Empty, Evaluate("").Verdict);
            Assert.Equal(QuickAddVerdict.Empty, Evaluate("   ", "NAS").Verdict);
        }

        [Fact]
        public void ARecognizedAddressWithALabelIsOneTargetCarryingThatLabel()
        {
            QuickAddEvaluation e = Evaluate("8.8.8.8", "Google DNS");

            Assert.Equal(QuickAddVerdict.Ready, e.Verdict);
            TrafficLightTarget target = Assert.Single(e.Plan.Targets);
            Assert.Equal("Google DNS", target.Label);
            Assert.Equal("ping", target.Kind);
            Assert.Equal("8.8.8.8", target.Address);
            Assert.False(target.Notify);            // nothing opted in by a quick add
            Assert.False(target.RecordHistory);
        }

        [Fact]
        public void WithoutALabelTheRecognizersOwnIsKeptAndALongOneIsCut()
        {
            Assert.NotEqual("", Assert.Single(Evaluate("8.8.8.8").Plan.Targets).Label);

            string tooLong = new string('x', 200);
            QuickAddEvaluation e = Evaluate("8.8.8.8", tooLong);
            Assert.Equal(TrafficLightTargetList.MaxLabelLength, Assert.Single(e.Plan.Targets).Label.Length);
        }

        [Fact]
        public void WhatIsAlreadyOnTheBoardIsNotAddedTwice()
        {
            var existing = new TrafficLightTarget { Id = "x", Kind = "ping", Address = "8.8.8.8", Ordinal = 0 };
            QuickAddEvaluation e = Evaluate("8.8.8.8", "again", existing);

            Assert.Equal(QuickAddVerdict.Duplicate, e.Verdict);
            Assert.Empty(e.Plan.Targets);
        }

        [Fact]
        public void ABackslashHostStillAsksItsOneQuestionOnConfirm()
        {
            QuickAddEvaluation e = Evaluate("\\\\SZAMEDIA", "Media PC");

            Assert.Equal(QuickAddVerdict.Ready, e.Verdict);
            TrafficLightTarget machine = Assert.Single(e.Plan.Targets);
            Assert.Equal("Media PC", machine.Label);
            Assert.Contains(machine.Id, e.Plan.MachineIds);
        }

        [Fact]
        public void TextThatIsNoneOfTheShapesIsSaidSoAndAddsNothing()
        {
            QuickAddEvaluation e = Evaluate("###", "x");

            Assert.Equal(QuickAddVerdict.NotRecognized, e.Verdict);
            Assert.Empty(e.Plan.Targets);
        }

        [Theory]
        [InlineData("FMSEXCH9:abcdef")]
        [InlineData("FMSSFTP3:abcdef")]
        [InlineData("fmscfg9:abcdef")]
        public void AContractStringOfAnUnknownVersionIsRefusedAndNeverEchoed(string text)
        {
            QuickAddEvaluation e = Evaluate(text, "x");

            Assert.NotEqual(QuickAddVerdict.Ready, e.Verdict);
            Assert.Empty(e.Plan.Targets);
            Assert.DoesNotContain(e.Notes, n => n.Contains("abcdef"));
        }

        [Fact]
        public void TheBoardHoldsAtMostTwoHundredTargets()
        {
            TrafficLightTarget[] full = Enumerable.Range(0, TrafficLightTargetList.MaxTargets)
                .Select(i => new TrafficLightTarget { Id = "t" + i, Kind = "ping", Address = "10.0.0." + i, Ordinal = i }).ToArray();

            Assert.Equal(QuickAddVerdict.TooMany, Evaluate("8.8.4.4", "", full).Verdict);
        }
    }

    /// <summary>The page, the tray and the journal's place in the app's own gates (ticket S0069 sections 6 and 7).</summary>
    [Collection(SharedGdiCollection.Name)]
    public class TrafficLightSettingsPageTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 3)]
        [InlineData(AppConfig.MicroscopicTrafficLightTileSize, 0)]
        public void SizeChoicesKeepExistingSavedValuesAndApplyTheNewSize(int saved, int selected)
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var config = RegroupingHarness.Config();
                config.EnableTrafficLight = true;
                config.TrafficLightTileSize = saved;
                using SettingsForm form = TestForms.NewSettings(config);
                var combo = Assert.IsType<ComboBox>(Assert.Single(form.Controls.Find("setting.traffic-light-tile-size", true)));
                Assert.Equal(4, combo.Items.Count);
                Assert.Equal("Микроскопический", combo.Items[0]);
                Assert.Equal(selected, combo.SelectedIndex);
                Assert.Equal(saved, config.TrafficLightTileSize);
                combo.SelectedIndex = 0;
                Assert.Equal(AppConfig.MicroscopicTrafficLightTileSize, config.TrafficLightTileSize);
                combo.SelectedIndex = 1;
                Assert.Equal(0, config.TrafficLightTileSize);
                combo.SelectedIndex = 2;
                Assert.Equal(1, config.TrafficLightTileSize);
                combo.SelectedIndex = 3;
                Assert.Equal(2, config.TrafficLightTileSize);
            });
        }

        [Fact]
        public void ThePageHasItsFourGroupsAndTheChordSwitchIsNamed()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config());
                TabPage page = form.Tabs!.TabPages.Cast<TabPage>().Single(p => Equals(p.Tag, SettingsPageKey.TrafficLight));
                Assert.Equal("Светофор доступности", page.Text);

                var groups = new List<string>();
                void Walk(Control control)
                {
                    foreach (Control child in control.Controls)
                    {
                        if (child is CollapsibleGroup g) groups.Add(g.Id);
                        Walk(child);
                    }
                }
                Walk(page);
                // S0060 M4: the module switch leads in a group of its own, like the hotkeys page's.
                Assert.Equal(new[] { "trafficlight/switches", "trafficlight/module", "trafficlight/targets", "trafficlight/history" }, groups.ToArray());
                Assert.Single(form.Controls.Find("setting.traffic-light-chord", true));
                Assert.Single(form.Controls.Find("setting.traffic-light", true));
                Assert.Single(form.Controls.Find("setting.traffic-light-transparency", true));
            });
        }
    }
}
