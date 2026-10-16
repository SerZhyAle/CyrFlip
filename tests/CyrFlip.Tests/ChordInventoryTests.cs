using System;
using System.Collections.Generic;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Inventory gate ensuring every <see cref="ChordKind"/> and chord-bearing <see cref="AppConfig"/>
    /// property is mapped to a tray popup group and handled (ticket S0059 section 8).
    /// </summary>
    public class ChordInventoryTests
    {
        private static readonly Dictionary<ChordKind, TrayChordGroupKind> KindToGroup = new Dictionary<ChordKind, TrayChordGroupKind>
        {
            [ChordKind.Case] = TrayChordGroupKind.Text,
            [ChordKind.History] = TrayChordGroupKind.Text,
            [ChordKind.QuickNotes] = TrayChordGroupKind.Text,
            [ChordKind.Symbols] = TrayChordGroupKind.Text,
            [ChordKind.TrafficLight] = TrayChordGroupKind.Text,
            [ChordKind.ExchangeSend] = TrayChordGroupKind.Text,
            [ChordKind.ExchangeGet] = TrayChordGroupKind.Text,
            [ChordKind.Screenshot] = TrayChordGroupKind.Text,
            [ChordKind.ScreenshotFull] = TrayChordGroupKind.Text,
            [ChordKind.ScreenshotAnnotateArea] = TrayChordGroupKind.Text,
            [ChordKind.ScreenshotFullAnnotate] = TrayChordGroupKind.Text,
            [ChordKind.ScreenshotFolder] = TrayChordGroupKind.Text,
            [ChordKind.TextMenu] = TrayChordGroupKind.Text,
            [ChordKind.Conversion] = TrayChordGroupKind.Conversions,
            [ChordKind.Translation] = TrayChordGroupKind.Translation,
            [ChordKind.Launcher] = TrayChordGroupKind.Launcher,
            [ChordKind.WindowsLanguage] = TrayChordGroupKind.Windows,
        };

        [Fact]
        public void EveryChordKindIsMappedToExactlyOneGroup()
        {
            var unmapped = new List<string>();
            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)))
            {
                if (!KindToGroup.ContainsKey(kind))
                    unmapped.Add(kind.ToString());
            }

            Assert.True(unmapped.Count == 0,
                "New ChordKind values not mapped to a tray popup group:\n" + string.Join("\n", unmapped));
        }

        [Fact]
        public void EveryAppConfigHotkeyPropertyIsAccountedFor()
        {
            // Known chord-bearing properties mapped in TrayChordList
            var mappedChordProperties = new HashSet<string>
            {
                nameof(AppConfig.CaseHotkey),
                nameof(AppConfig.ClipboardHistoryHotkey),
                nameof(AppConfig.QuickNotesHotkey),
                nameof(AppConfig.SymbolsHotkey),
                nameof(AppConfig.TrafficLightHotkey),
                nameof(AppConfig.ExchangeClipboardSendHotkey),
                nameof(AppConfig.ExchangeClipboardGetHotkey),
                nameof(AppConfig.ScreenshotHotkey),
                nameof(AppConfig.ScreenshotFullHotkey),
                nameof(AppConfig.ScreenshotAnnotateAreaHotkey),
                nameof(AppConfig.ScreenshotFullAnnotateHotkey),
                nameof(AppConfig.ScreenshotFolderHotkey),
                nameof(AppConfig.TextMenuHotkey),
                nameof(AppConfig.ContextMenuChord),
                nameof(AppConfig.LayoutConversionProfiles),
                nameof(AppConfig.TranslateProfiles),
            };

            // Known hotkey switch properties
            var mappedSwitchProperties = new HashSet<string>
            {
                nameof(AppConfig.EnableHotkeys),
                nameof(AppConfig.EnableCaseHotkey),
                nameof(AppConfig.EnableHistoryHotkey),
                nameof(AppConfig.EnableQuickNotesHotkey),
                nameof(AppConfig.EnableSymbolsHotkey),
                nameof(AppConfig.EnableTrafficLightHotkey),
                nameof(AppConfig.EnableExchangeClipboardSendHotkey),
                nameof(AppConfig.EnableExchangeClipboardGetHotkey),
                nameof(AppConfig.EnableScreenshotHotkey),
                nameof(AppConfig.EnableScreenshotFullHotkey),
                nameof(AppConfig.EnableScreenshotAnnotateAreaHotkey),
                nameof(AppConfig.EnableScreenshotFullAnnotateHotkey),
                nameof(AppConfig.EnableScreenshotFolderHotkey),
                nameof(AppConfig.EnableTextMenuHotkey),
            };

            var unhandled = new List<string>();
            foreach (PropertyInfo prop in typeof(AppConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                bool isHotkeyRelated = prop.Name.EndsWith("Hotkey", StringComparison.Ordinal)
                    || prop.Name.EndsWith("Chord", StringComparison.Ordinal)
                    || prop.Name.EndsWith("Profiles", StringComparison.Ordinal);

                if (isHotkeyRelated)
                {
                    if (prop.PropertyType == typeof(bool))
                    {
                        if (!mappedSwitchProperties.Contains(prop.Name))
                            unhandled.Add($"Switch property: {prop.Name}");
                    }
                    else
                    {
                        if (!mappedChordProperties.Contains(prop.Name))
                            unhandled.Add($"Chord property: {prop.Name}");
                    }
                }
            }

            Assert.True(unhandled.Count == 0,
                "Unhandled hotkey properties on AppConfig:\n" + string.Join("\n", unhandled));
        }
    }
}
