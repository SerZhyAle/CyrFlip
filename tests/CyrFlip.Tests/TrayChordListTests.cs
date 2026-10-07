using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class TrayChordListTests
    {
        private static AppConfig CreateConfig(Action<AppConfig>? customize = null)
        {
            var config = new AppConfig();
            customize?.Invoke(config);
            return config;
        }

        [Fact]
        public void TooltipLinesEquivalenceAcrossSwitchMatrix()
        {
            // Test matrix: combinations of master, module and row switches
            bool[] bools = { false, true };
            foreach (bool master in bools)
            {
                foreach (bool caseOn in bools)
                {
                    foreach (bool histOn in bools)
                    {
                        foreach (bool qnModule in bools)
                        {
                            foreach (bool qnRow in bools)
                            {
                                foreach (bool symModule in bools)
                                {
                                    foreach (bool symRow in bools)
                                    {
                                        foreach (bool shotRow in bools)
                                        {
                                            foreach (bool menuModule in bools)
                                            {
                                                foreach (bool menuRow in bools)
                                                {
                                                    foreach (bool transModule in bools)
                                                    {
                                                        foreach (bool scnModule in bools)
                                                        {
                                                            AppConfig config = CreateConfig(c =>
                                                            {
                                                                c.EnableHotkeys = master;
                                                                c.EnableCaseHotkey = caseOn;
                                                                c.EnableHistoryHotkey = histOn;
                                                                c.EnableQuickNotes = qnModule;
                                                                c.EnableQuickNotesHotkey = qnRow;
                                                                c.EnableSymbols = symModule;
                                                                c.EnableSymbolsHotkey = symRow;
                                                                c.EnableScreenshotHotkey = shotRow;
                                                                c.EnableContextMenu = menuModule;
                                                                c.EnableTextMenuHotkey = menuRow;
                                                                c.EnableTranslate = transModule;
                                                                c.EnableScenarioLauncher = scnModule;
                                                                c.LayoutConversionProfiles = new List<LayoutConversionProfile>
                                                                {
                                                                    new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true },
                                                                    new LayoutConversionProfile { Id = "c2", SourceKlid = "00000409", TargetKlid = "00000422", Hotkey = "Ctrl+Shift+F8", Enabled = false },
                                                                };
                                                                c.TranslateProfiles = new List<TranslationProfile>
                                                                {
                                                                    new TranslationProfile { Id = "t1", TargetLang = "en", Hotkey = "Ctrl+Alt+T", Enabled = true },
                                                                };
                                                            });

                                                            var scenarios = new List<LauncherScenario>
                                                            {
                                                                new LauncherScenario { Name = "Calc", Hotkey = "Ctrl+Shift+C", Path = "calc.exe" },
                                                            };

                                                            List<string> lines = TrayChordList.BuildTooltipLines(config, scenarios, "Русский");
                                                            if (!master)
                                                            {
                                                                Assert.Single(lines);
                                                                Assert.Equal("горячие клавиши выключены", lines[0]);
                                                            }
                                                            else
                                                            {
                                                                Assert.DoesNotContain("горячие клавиши выключены", lines);
                                                                if (caseOn) Assert.Contains(lines, l => l.Contains("регистр"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("регистр"));

                                                                if (histOn) Assert.Contains(lines, l => l.Contains("менеджер буфера"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("менеджер буфера"));

                                                                if (qnModule && qnRow) Assert.Contains(lines, l => l.Contains("быстрые заметки"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("быстрые заметки"));

                                                                if (symModule && symRow) Assert.Contains(lines, l => l.Contains("окно символов"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("окно символов"));

                                                                if (shotRow) Assert.Contains(lines, l => l.Contains("снимок области экрана"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("снимок области экрана"));

                                                                if (menuModule && menuRow) Assert.Contains(lines, l => l.Contains("контекстное меню текста"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("контекстное меню текста"));

                                                                Assert.Contains(lines, l => l.Contains("EN ⇄ RU"));
                                                                Assert.DoesNotContain(lines, l => l.Contains("UK"));

                                                                if (transModule) Assert.Contains(lines, l => l.Contains("English"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("English"));

                                                                if (scnModule) Assert.Contains(lines, l => l.Contains("Calc"));
                                                                else Assert.DoesNotContain(lines, l => l.Contains("Calc"));
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void UnparsableHotkeysAreExcludedFromTooltipAndList()
        {
            AppConfig config = CreateConfig(c =>
            {
                c.EnableHotkeys = true;
                c.CaseHotkey = "INVALID_KEY_CHORD";
                c.LayoutConversionProfiles = new List<LayoutConversionProfile>
                {
                    new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "BAD_HOTKEY", Enabled = true },
                };
            });

            TrayChordList list = TrayChordList.Build(config, null, null, null, "Русский");
            Assert.DoesNotContain(list.AllRows, r => r.ChordDisplay == "INVALID_KEY_CHORD" || r.ChordDisplay == "BAD_HOTKEY");
            Assert.DoesNotContain(list.AllRows, r => r.Kind == ChordKind.Case);
            Assert.DoesNotContain(list.AllRows, r => r.Kind == ChordKind.Conversion);
        }

        [Fact]
        public void MasterOffShowsSingleTextRowAndKeepsMouseAndWindowsGroups()
        {
            AppConfig config = CreateConfig(c =>
            {
                c.EnableHotkeys = false;
                c.EnableContextMenu = true;
            });

            var windowsHotkeys = new List<LanguageHotkeys.Entry>
            {
                new LanguageHotkeys.Entry { Id = 0x100, Modifiers = LanguageHotkeys.MOD_CONTROL, VirtualKey = 0x31, TargetHkl = new IntPtr(0x04090409) }
            };

            TrayChordList list = TrayChordList.Build(config, null, windowsHotkeys, "1", "Русский");

            // Text group has single muted row
            TrayChordGroup textGroup = Assert.Single(list.Groups, g => g.Group == TrayChordGroupKind.Text);
            Assert.Single(textGroup.Rows);
            Assert.Equal("горячие клавиши выключены", textGroup.Rows[0].ActionDisplay);
            Assert.True(textGroup.Rows[0].IsMuted);

            // Mouse group present
            TrayChordGroup mouseGroup = Assert.Single(list.Groups, g => g.Group == TrayChordGroupKind.Mouse);
            Assert.Contains(mouseGroup.Rows, r => r.ActionDisplay == "следующая раскладка");

            // Windows group present
            TrayChordGroup winGroup = Assert.Single(list.Groups, g => g.Group == TrayChordGroupKind.Windows);
            Assert.Contains(winGroup.Rows, r => r.ChordDisplay == "Ctrl+1");
            Assert.Contains(winGroup.Rows, r => r.ChordDisplay == "Alt+Shift");
        }

        [Fact]
        public void ClashDetectionMarksDuplicateActiveChords()
        {
            AppConfig config = CreateConfig(c =>
            {
                c.EnableHotkeys = true;
                c.CaseHotkey = "Ctrl+Shift+F12"; // clashing with conversion profile below
                c.EnableCaseHotkey = true;
                c.LayoutConversionProfiles = new List<LayoutConversionProfile>
                {
                    new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true }
                };
            });

            TrayChordList list = TrayChordList.Build(config, null, null, null, "Русский");

            TrayChordRow caseRow = Assert.Single(list.AllRows, r => r.Kind == ChordKind.Case);
            TrayChordRow convRow = Assert.Single(list.AllRows, r => r.Kind == ChordKind.Conversion);

            Assert.True(caseRow.IsClash, "Case row should be marked as clash");
            Assert.True(convRow.IsClash, "Conversion row should be marked as clash");
        }

        [Fact]
        public void LargeConversionTablePopulatesAllRowsInModel()
        {
            var profiles = new List<LayoutConversionProfile>();
            for (int i = 1; i <= 40; i++)
            {
                profiles.Add(new LayoutConversionProfile
                {
                    Id = "row_" + i,
                    SourceKlid = "00000409",
                    TargetKlid = "00000419",
                    Hotkey = "Ctrl+Shift+F" + (i <= 12 ? i : 12),
                    Enabled = true,
                });
            }

            AppConfig config = CreateConfig(c =>
            {
                c.EnableHotkeys = true;
                c.LayoutConversionProfiles = profiles;
            });

            TrayChordList list = TrayChordList.Build(config, null, null, null, "Русский");
            TrayChordGroup convGroup = Assert.Single(list.Groups, g => g.Group == TrayChordGroupKind.Conversions);
            Assert.Equal(40, convGroup.Rows.Count);
        }

        [Fact]
        public void BuildIsDeterministicAndIdempotent()
        {
            AppConfig config = CreateConfig(c =>
            {
                c.EnableHotkeys = true;
                c.EnableCaseHotkey = true;
                c.EnableHistoryHotkey = true;
                c.EnableQuickNotes = true;
                c.EnableQuickNotesHotkey = true;
            });

            TrayChordList list1 = TrayChordList.Build(config, null, null, "1", "Русский");
            TrayChordList list2 = TrayChordList.Build(config, null, null, "1", "Русский");

            Assert.Equal(list1.AllRows.Count, list2.AllRows.Count);
            for (int i = 0; i < list1.AllRows.Count; i++)
            {
                Assert.Equal(list1.AllRows[i].Group, list2.AllRows[i].Group);
                Assert.Equal(list1.AllRows[i].ChordDisplay, list2.AllRows[i].ChordDisplay);
                Assert.Equal(list1.AllRows[i].ActionDisplay, list2.AllRows[i].ActionDisplay);
                Assert.Equal(list1.AllRows[i].IsClash, list2.AllRows[i].IsClash);
            }
        }
    }
}
