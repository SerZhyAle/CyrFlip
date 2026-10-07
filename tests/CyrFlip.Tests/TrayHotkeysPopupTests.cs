using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(SharedGdiCollection.Name)]
    public class TrayHotkeysPopupTests
    {
        private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]");
        private static readonly HashSet<string> CyrillicUi = new HashSet<string> { "Русский", "Українська" };

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread execution failed: " + failure.Message, failure);
        }

        private static AppConfig CreateRealisticConfig(string language)
        {
            return new AppConfig
            {
                UiLanguage = language,
                EnableHotkeys = true,
                EnableCaseHotkey = true,
                EnableHistoryHotkey = true,
                EnableQuickNotes = true,
                EnableQuickNotesHotkey = true,
                EnableSymbols = true,
                EnableSymbolsHotkey = true,
                EnableScreenshotHotkey = true,
                ScreenshotSaveEnabled = true,
                EnableScreenshotFolderHotkey = true,
                EnableContextMenu = true,
                EnableTextMenuHotkey = true,
                ContextMenuChord = "Ctrl+RightClick",
                LayoutConversionProfiles = new List<LayoutConversionProfile>
                {
                    new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true },
                    new LayoutConversionProfile { Id = "c2", SourceKlid = "00000409", TargetKlid = "00000422", Hotkey = "Ctrl+Shift+F8", Enabled = true },
                },
                EnableTranslate = true,
                TranslateProfiles = new List<TranslationProfile>
                {
                    new TranslationProfile { Id = "t1", TargetLang = "en", Hotkey = "Ctrl+Alt+T", Enabled = true },
                },
                EnableScenarioLauncher = true,
            };
        }

        [Fact]
        public void PopupIsThemedFormAndNeverActivates()
        {
            OnUiThread(() =>
            {
                using var popup = new TrayHotkeysPopup("English");
                Assert.IsAssignableFrom<ThemedForm>(popup);

                // Verify non-activating style properties
                PropertyInfo? prop = typeof(TrayHotkeysPopup).GetProperty("ShowWithoutActivation", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(prop);
                Assert.True((bool)prop!.GetValue(popup)!);

                Assert.Equal(FormBorderStyle.None, popup.FormBorderStyle);
                Assert.False(popup.ShowInTaskbar);
                Assert.True(popup.TopMost);

                // Check CreateParams
                PropertyInfo? cpProp = typeof(TrayHotkeysPopup).GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(cpProp);
                CreateParams cp = (CreateParams)cpProp!.GetValue(popup)!;

                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_TRANSPARENT = 0x00000020;
                Assert.True((cp.ExStyle & WS_EX_NOACTIVATE) != 0, "WS_EX_NOACTIVATE must be set");
                Assert.True((cp.ExStyle & WS_EX_TOOLWINDOW) != 0, "WS_EX_TOOLWINDOW must be set");
                Assert.True((cp.ExStyle & WS_EX_TRANSPARENT) != 0, "WS_EX_TRANSPARENT must be set");
            });
        }

        [Fact]
        public void PopupBuildsInAll13LanguagesWithoutClipping()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                foreach (string lang in Localization.Names)
                {
                    AppConfig config = CreateRealisticConfig(lang);
                    var scenarios = new List<LauncherScenario>
                    {
                        new LauncherScenario { Name = "Launch PowerShell with elevated privileges", Hotkey = "Ctrl+Shift+P", Path = "powershell.exe" }
                    };
                    var windowsHotkeys = new List<LanguageHotkeys.Entry>
                    {
                        new LanguageHotkeys.Entry { Id = 0x100, Modifiers = LanguageHotkeys.MOD_CONTROL, VirtualKey = 0x31, TargetHkl = new IntPtr(0x04090409) }
                    };

                    using var popup = new TrayHotkeysPopup(lang);
                    TrayChordList model = TrayChordList.Build(config, scenarios, windowsHotkeys, "1", lang);
                    popup.SetModel(model, new Point(500, 500));

                    if (Localization.IsRightToLeft(lang))
                        Assert.Equal(RightToLeft.Yes, popup.RightToLeft);
                    else
                        Assert.Equal(RightToLeft.No, popup.RightToLeft);

                    if (popup.Width <= 0 || popup.Height <= 0)
                        problems.Add($"{lang}: popup has empty bounds");
                }
            });

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void PopupShowsCapRowWhenRowsExceedLimit()
        {
            OnUiThread(() =>
            {
                AppConfig config = CreateRealisticConfig("English");
                var profiles = new List<LayoutConversionProfile>();
                for (int i = 1; i <= 60; i++)
                {
                    profiles.Add(new LayoutConversionProfile
                    {
                        Id = "row_" + i,
                        SourceKlid = "00000409",
                        TargetKlid = "00000419",
                        Hotkey = "Ctrl+Shift+F12",
                        Enabled = true,
                    });
                }
                config.LayoutConversionProfiles = profiles;

                using var popup = new TrayHotkeysPopup("English");
                TrayChordList model = TrayChordList.Build(config, null, null, null, "English");
                popup.SetModel(model, new Point(500, 500));

                // Find TableLayoutPanel control
                TableLayoutPanel table = popup.Controls.OfType<TableLayoutPanel>().Single();
                bool hasCapLabel = false;
                foreach (Control ctrl in table.Controls)
                {
                    if (ctrl is Label lbl && lbl.Text.Contains("more in Settings"))
                    {
                        hasCapLabel = true;
                        break;
                    }
                }

                Assert.True(hasCapLabel, "Cap overflow label must be present when rows exceed budget");
            });
        }

        [Fact]
        public void PopupRendersClashMarkOnClashingRows()
        {
            OnUiThread(() =>
            {
                AppConfig config = CreateRealisticConfig("English");
                config.CaseHotkey = "Ctrl+Shift+F12";
                config.EnableCaseHotkey = true;
                config.LayoutConversionProfiles = new List<LayoutConversionProfile>
                {
                    new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true }
                };

                using var popup = new TrayHotkeysPopup("English");
                TrayChordList model = TrayChordList.Build(config, null, null, null, "English");
                popup.SetModel(model, new Point(500, 500));

                TableLayoutPanel table = popup.Controls.OfType<TableLayoutPanel>().Single();
                bool hasWarningMark = false;
                foreach (Control ctrl in table.Controls)
                {
                    if (ctrl is Label lbl && lbl.Text.StartsWith("⚠ "))
                    {
                        hasWarningMark = true;
                        break;
                    }
                }

                Assert.True(hasWarningMark, "Clashing rows must carry warning mark ⚠");
            });
        }

        [Fact]
        public void PositionBesideClampsToWorkArea()
        {
            OnUiThread(() =>
            {
                using var popup = new TrayHotkeysPopup("English");
                AppConfig config = CreateRealisticConfig("English");
                TrayChordList model = TrayChordList.Build(config, null, null, "1", "English");
                popup.SetModel(model);

                Screen primary = Screen.PrimaryScreen;
                Rectangle work = primary.WorkingArea;

                // Anchor near bottom right
                popup.PositionBeside(new Point(work.Right - 10, work.Bottom - 10));
                Assert.True(work.Contains(popup.Bounds), "Popup bounds must be completely within work area");

                // Anchor near top left
                popup.PositionBeside(new Point(work.Left + 5, work.Top + 5));
                Assert.True(work.Contains(popup.Bounds), "Popup bounds must be completely within work area");
            });
        }

        [Fact]
        public void NoSurvivingCyrillicInNonCyrillicLocales()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                foreach (string lang in Localization.Names)
                {
                    if (CyrillicUi.Contains(lang)) continue;

                    AppConfig config = CreateRealisticConfig(lang);
                    using var popup = new TrayHotkeysPopup(lang);
                    TrayChordList model = TrayChordList.Build(config, null, null, "1", lang);
                    popup.SetModel(model, new Point(500, 500));

                    TableLayoutPanel table = popup.Controls.OfType<TableLayoutPanel>().Single();
                    foreach (Control ctrl in table.Controls)
                    {
                        if (ctrl is Label lbl)
                        {
                            // Skip layout codes like "RU" or native language names
                            if (lbl.Text.Contains("RU") && lbl.Text.Length <= 10) continue;
                            if (Cyrillic.IsMatch(lbl.Text))
                                problems.Add($"{lang}: surviving Cyrillic in '{lbl.Text}'");
                        }
                    }
                }
            });

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
