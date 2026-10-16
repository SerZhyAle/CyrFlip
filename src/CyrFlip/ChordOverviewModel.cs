using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>The six groups of the hotkeys overview (ticket S0056 section 3). Their ids are stable.</summary>
    internal enum ChordGroup
    {
        /// <summary>The seven chords that always exist: case flip, history, notes, symbols, capture, capture folder, text menu.</summary>
        Fixed,
        /// <summary>The text menu's mouse chord.</summary>
        Mouse,
        Conversions,
        Translations,
        Launcher,
        /// <summary>Windows' own direct-language hotkeys and the layout-cycle chord - read-only.</summary>
        Windows,
    }

    internal static class ChordGroups
    {
        /// <summary>The group's stable id, the one its expansion is remembered under (WINDOWS-UI section 3).</summary>
        public static string Id(ChordGroup group)
        {
            switch (group)
            {
                case ChordGroup.Fixed: return "keys/fixed";
                case ChordGroup.Mouse: return "keys/mouse";
                case ChordGroup.Conversions: return "keys/conversions";
                case ChordGroup.Translations: return "keys/translations";
                case ChordGroup.Launcher: return "keys/launcher";
                default: return "keys/windows";
            }
        }

        /// <summary>The group's caption, a Russian-source localization key.</summary>
        public static string Caption(ChordGroup group)
        {
            switch (group)
            {
                case ChordGroup.Fixed: return "Main hotkeys";
                case ChordGroup.Mouse: return "Mouse";
                case ChordGroup.Conversions: return "Layout conversions";
                case ChordGroup.Translations: return "Translation";
                case ChordGroup.Launcher: return "Quick launch";
                default: return "Windows";
            }
        }

        /// <summary>The overview group a chord kind is listed in - exactly one per kind (the inventory gate).</summary>
        public static ChordGroup Of(ChordKind kind)
        {
            switch (kind)
            {
                case ChordKind.Conversion: return ChordGroup.Conversions;
                case ChordKind.Translation: return ChordGroup.Translations;
                case ChordKind.Launcher: return ChordGroup.Launcher;
                case ChordKind.WindowsLanguage: return ChordGroup.Windows;
                // The fixed kinds are named, not lumped into the default: a new fixed kind must be
                // spelled out here (and in ChordDefaults.IsFixed) before the overview shows it.
                case ChordKind.Case:
                case ChordKind.History:
                case ChordKind.QuickNotes:
                case ChordKind.Symbols:
                case ChordKind.TrafficLight:
                case ChordKind.ExchangeSend:
                case ChordKind.ExchangeGet:
                case ChordKind.Screenshot:
                case ChordKind.ScreenshotFull:
                case ChordKind.ScreenshotAnnotateArea:
                case ChordKind.ScreenshotFullAnnotate:
                case ChordKind.ScreenshotFolder:
                case ChordKind.TextMenu:
                    return ChordGroup.Fixed;
                default: return ChordGroup.Fixed;
            }
        }
    }

    /// <summary>One line of the overview: a binding, its chord and why it does or does not fire.</summary>
    internal sealed class ChordOverviewRow
    {
        public ChordGroup Group { get; set; }
        /// <summary>The chord kind; null for the text menu's mouse chord and for Windows' layout-cycle line.</summary>
        public ChordKind? Kind { get; set; }
        /// <summary>The row / scenario / Windows slot id; empty for a fixed chord and for the mouse chord.</summary>
        public string Id { get; set; } = "";
        /// <summary>The action in words, in the UI language.</summary>
        public string Caption { get; set; } = "";
        /// <summary>The chord token as stored; empty when the row has none. Mouse: the invariant token.</summary>
        public string StoredChord { get; set; } = "";
        /// <summary>The chord as shown - the app's display spelling, or the stored text when it does not parse.</summary>
        public string ChordText { get; set; } = "";
        /// <summary>
        /// Why the chord does not fire, or <see cref="ChordBindingState.Active"/>. Never
        /// <see cref="ChordBindingState.Clash"/>: a clash is a second fact, <see cref="ClashWith"/>.
        /// </summary>
        public ChordBindingState State { get; set; }
        /// <summary>The other CyrFlip action that owns the same chord, switched on or not; null when none.</summary>
        public string? ClashWith { get; set; }
        /// <summary>The row has a switch of its own (a scenario, the mouse chord and Windows' chords have none).</summary>
        public bool HasSwitch { get; set; }
        public bool SwitchOn { get; set; }
        /// <summary>The settings page of the module that gates the row, for the "module is off" link.</summary>
        public SettingsPageKey? ModulePage { get; set; }
        /// <summary>The shipped chord token, or null when the row has none and its reset is "clear the chord".</summary>
        public string? DefaultChord { get; set; }
        /// <summary>The shipped state of the row's switch; only meaningful with <see cref="HasSwitch"/> and a shipped chord.</summary>
        public bool DefaultSwitchOn { get; set; } = true;
        /// <summary>The row is not what it shipped as (or, for a row without a shipped chord, carries a chord).</summary>
        public bool Differs { get; set; }
        /// <summary>Windows' own chords: listed, never edited here.</summary>
        public bool ReadOnly { get; set; }
        public bool IsMouse { get; set; }
        /// <summary>The profile, scenario or Windows entry behind the row.</summary>
        public object? Source { get; set; }

        /// <summary>A conversion row, translation row or scenario can be left without a chord; a fixed chord cannot.</summary>
        public bool CanClear => !ReadOnly && !IsMouse && Kind.HasValue && !ChordDefaults.IsFixed(Kind.Value) && StoredChord.Length > 0;

        /// <summary>The reset has something to do.</summary>
        public bool CanReset => !ReadOnly && Differs;

        /// <summary>The reset puts the shipped chord back (true) or only clears the chord (false).</summary>
        public bool ResetRestoresDefault => DefaultChord != null;

        /// <summary>A stable key for tests and focus: kind (or group) plus id.</summary>
        public string Key => (Kind.HasValue ? Kind.Value.ToString() : Group.ToString()) + ":" + Id;

        /// <summary>The state in words, localized through <paramref name="translate"/>.</summary>
        public string StateText(Func<string, string> translate)
        {
            string text;
            switch (State)
            {
                case ChordBindingState.Active: text = ""; break;
                case ChordBindingState.OffByRow: text = translate("off"); break;
                case ChordBindingState.OffByModule: text = translate("module is off"); break;
                case ChordBindingState.OffByMaster: text = translate("hotkeys are off"); break;
                case ChordBindingState.NoChord: text = translate("not assigned"); break;
                case ChordBindingState.Unparsable: text = translate("not recognised"); break;
                case ChordBindingState.Incomplete: text = translate("incomplete"); break;
                default: text = ""; break;
            }
            if (ClashWith != null)
            {
                string clash = Localization.Format(translate, "same as: {0}", ClashWith);
                text = text.Length == 0 ? clash : text + "; " + clash;
            }
            return text;
        }

        /// <summary>Everything the settings search matches on: the words, the chord, the state.</summary>
        public string SearchText(Func<string, string> translate)
            => Caption + " " + ChordText + " " + StoredChord + " " + StateText(translate);
    }

    /// <summary>
    /// The pure model behind the hotkeys overview tab (ticket S0056 sections 3-4): one row per binding,
    /// six groups, and for each row the answer to "is this chord bound right now, and if not, why".
    /// The answer comes from <see cref="ChordBinding.Evaluate"/> - the one predicate the tray popup uses
    /// too - and the registry for the clash, so the page cannot say anything the hook would not do.
    /// No WinForms, no registry: the three inputs are arguments, as <see cref="ChordRegistry.Build"/> takes them.
    /// </summary>
    internal static class ChordOverviewModel
    {
        public static List<ChordOverviewRow> Build(AppConfig config, IEnumerable<LauncherScenario>? scenarios,
            IEnumerable<LanguageHotkeys.Entry>? windowsHotkeys, string? windowsToggleCode, string uiLanguage,
            Func<string, string> layoutName)
        {
            string T(string key) => Localization.Translate(uiLanguage, key);
            var scenarioList = new List<LauncherScenario>(scenarios ?? new LauncherScenario[0]);
            var windowsList = new List<LanguageHotkeys.Entry>(windowsHotkeys ?? new LanguageHotkeys.Entry[0]);
            ChordRegistry registry = ChordRegistry.Build(config, scenarioList, windowsList);
            var rows = new List<ChordOverviewRow>();

            // --- fixed chords ---------------------------------------------------------------------
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.Case,
                config.CaseHotkey, config.EnableCaseHotkey, true, SettingsPageKey.Conversions);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.History,
                config.ClipboardHistoryHotkey, config.EnableHistoryHotkey, true, SettingsPageKey.Clipboard);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.QuickNotes,
                config.QuickNotesHotkey, config.EnableQuickNotesHotkey, config.EnableQuickNotes, SettingsPageKey.QuickNotes);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.Symbols,
                config.SymbolsHotkey, config.EnableSymbolsHotkey, config.EnableSymbols, SettingsPageKey.Symbols);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.TrafficLight,
                config.TrafficLightHotkey, config.EnableTrafficLightHotkey, config.EnableTrafficLight, SettingsPageKey.TrafficLight);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.Screenshot,
                config.ScreenshotHotkey, config.EnableScreenshotHotkey, true, SettingsPageKey.Graphics);
            // The full capture: its own switch only, no module switch - the tray item is always there.
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ScreenshotFull,
                config.ScreenshotFullHotkey, config.EnableScreenshotFullHotkey, true, SettingsPageKey.Graphics);
            // The annotate pair: bound only while the draw module is on (S0071 6.1), so the module-off
            // state reads from EnableScreenshotAnnotate like every other opt-in feature's.
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ScreenshotAnnotateArea,
                config.ScreenshotAnnotateAreaHotkey, config.EnableScreenshotAnnotateAreaHotkey, config.EnableScreenshotAnnotate, SettingsPageKey.Graphics);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ScreenshotFullAnnotate,
                config.ScreenshotFullAnnotateHotkey, config.EnableScreenshotFullAnnotateHotkey, config.EnableScreenshotAnnotate, SettingsPageKey.Graphics);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ScreenshotFolder,
                config.ScreenshotFolderHotkey, config.EnableScreenshotFolderHotkey, config.ScreenshotSaveEnabled, SettingsPageKey.Graphics);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.TextMenu,
                config.TextMenuHotkey, config.EnableTextMenuHotkey, config.EnableContextMenu, SettingsPageKey.SelectionMenu);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ExchangeSend,
                config.ExchangeClipboardSendHotkey, config.EnableExchangeClipboardSendHotkey, config.EnableExchangeClipboard, SettingsPageKey.ExchangeServer);
            AddFixed(rows, config, registry, uiLanguage, layoutName, ChordKind.ExchangeGet,
                config.ExchangeClipboardGetHotkey, config.EnableExchangeClipboardGetHotkey, config.EnableExchangeClipboard, SettingsPageKey.ExchangeServer);

            // --- the text menu's mouse chord ---------------------------------------------------------
            MouseChord mouse = MouseChord.Parse(config.ContextMenuChord);
            rows.Add(new ChordOverviewRow
            {
                Group = ChordGroup.Mouse, Kind = null, Id = "", IsMouse = true,
                Caption = T("Menu over a selection"),
                StoredChord = mouse.Token, ChordText = mouse.Display(T),
                State = config.EnableContextMenu ? ChordBindingState.Active : ChordBindingState.OffByModule,
                ModulePage = SettingsPageKey.SelectionMenu,
                DefaultChord = ChordDefaults.MouseChordToken,
                Differs = !string.Equals(mouse.Token, ChordDefaults.MouseChordToken, StringComparison.Ordinal),
            });

            // --- conversion rows ----------------------------------------------------------------------
            foreach (LayoutConversionProfile profile in config.LayoutConversionProfiles)
            {
                if (profile == null) continue;
                bool seeded = ChordDefaults.IsSeededConversion(profile);
                // An incomplete row is fixed on the conversion page, so its state text links there (see the settings page).
                ChordOverviewRow row = Row(config, registry, uiLanguage, layoutName, ChordGroup.Conversions,
                    ChordKind.Conversion, profile.Id, profile.Hotkey, profile.Enabled, true,
                    SettingsPageKey.Conversions, profile, usable: profile.IsUsable);
                row.HasSwitch = true; row.SwitchOn = profile.Enabled;
                if (seeded)
                {
                    row.DefaultChord = ChordDefaults.SeededConversionChord;
                    row.Differs = !ChordDefaults.SameChordToken(profile.Hotkey, row.DefaultChord) || !profile.Enabled;
                }
                else row.Differs = !string.IsNullOrEmpty(profile.Hotkey);
                rows.Add(row);
            }

            // --- translation rows ---------------------------------------------------------------------
            foreach (TranslationProfile profile in config.TranslateProfiles)
            {
                if (profile == null) continue;
                bool seeded = ChordDefaults.IsSeededTranslation(profile);
                ChordOverviewRow row = Row(config, registry, uiLanguage, layoutName, ChordGroup.Translations,
                    ChordKind.Translation, profile.Id, profile.Hotkey, profile.Enabled, config.EnableTranslate,
                    SettingsPageKey.Translate, profile, usable: profile.IsUsable);
                row.HasSwitch = true; row.SwitchOn = profile.Enabled;
                if (seeded)
                {
                    row.DefaultChord = ChordDefaults.SeededTranslationChord;
                    row.Differs = !ChordDefaults.SameChordToken(profile.Hotkey, row.DefaultChord) || !profile.Enabled;
                }
                else row.Differs = !string.IsNullOrEmpty(profile.Hotkey);
                rows.Add(row);
            }

            // --- scenarios that carry a chord -----------------------------------------------------------
            foreach (LauncherScenario scenario in scenarioList)
            {
                if (scenario == null || string.IsNullOrEmpty(scenario.Hotkey)) continue;
                ChordOverviewRow row = Row(config, registry, uiLanguage, layoutName, ChordGroup.Launcher,
                    ChordKind.Launcher, scenario.Id.ToString(), scenario.Hotkey, true, config.EnableScenarioLauncher,
                    SettingsPageKey.Launcher, scenario);
                row.Differs = true; // a scenario ships with no chord: any chord is the user's
                rows.Add(row);
            }

            // --- Windows' own chords (read-only) ----------------------------------------------------------
            foreach (LanguageHotkeys.Entry entry in windowsList)
            {
                if (entry == null || entry.VirtualKey == 0) continue;
                rows.Add(new ChordOverviewRow
                {
                    Group = ChordGroup.Windows, Kind = ChordKind.WindowsLanguage, Id = entry.Id.ToString(),
                    Caption = ChordRegistry.LabelFor(ChordKind.WindowsLanguage, entry, uiLanguage, layoutName),
                    StoredChord = LanguageHotkeys.FormatChord(entry.Modifiers, entry.VirtualKey),
                    ChordText = LanguageHotkeys.FormatChord(entry.Modifiers, entry.VirtualKey),
                    State = ChordBindingState.Active, ReadOnly = true, ModulePage = SettingsPageKey.WindowsLanguages,
                    Source = entry,
                });
            }
            string? toggle = windowsToggleCode;
            string? cycle = toggle == "1" ? "Alt+Shift" : toggle == "2" ? "Ctrl+Shift" : toggle == "4" ? "`" : null;
            if (cycle != null)
                rows.Add(new ChordOverviewRow
                {
                    Group = ChordGroup.Windows, Kind = null, Id = "toggle",
                    Caption = T("Windows layout switch"),
                    StoredChord = cycle, ChordText = cycle,
                    State = ChordBindingState.Active, ReadOnly = true, ModulePage = SettingsPageKey.WindowsLanguages,
                });

            return rows;
        }

        private static void AddFixed(List<ChordOverviewRow> rows, AppConfig config, ChordRegistry registry,
            string uiLanguage, Func<string, string> layoutName, ChordKind kind, string stored, bool rowOn,
            bool moduleOn, SettingsPageKey? modulePage)
        {
            ChordOverviewRow row = Row(config, registry, uiLanguage, layoutName, ChordGroup.Fixed, kind, "",
                stored, rowOn, moduleOn, modulePage, null);
            row.HasSwitch = true; row.SwitchOn = rowOn;
            row.DefaultChord = ChordDefaults.FixedChord(kind);
            row.Differs = !ChordDefaults.SameChordToken(stored, row.DefaultChord) || !rowOn;
            rows.Add(row);
        }

        private static ChordOverviewRow Row(AppConfig config, ChordRegistry registry, string uiLanguage,
            Func<string, string> layoutName, ChordGroup group, ChordKind kind, string id, string? stored,
            bool rowOn, bool moduleOn, SettingsPageKey? modulePage, object? source, bool usable = true)
        {
            // The clash is not asked of Evaluate (it would hide why the chord does not fire): it is its own fact.
            ChordBinding binding = ChordBinding.Evaluate(config, kind, id, stored, rowOn, moduleOn, registry: null, source, usable);
            string? clash = null;
            if (binding.ParsedChord.HasValue)
            {
                ChordOwner? other = registry.CyrFlipOwnerOf(binding.ParsedChord.Value, kind, id);
                if (other != null) clash = ChordRegistry.Label(other, uiLanguage, layoutName);
            }
            return new ChordOverviewRow
            {
                Group = group, Kind = kind, Id = id,
                Caption = ChordRegistry.LabelFor(kind, source, uiLanguage, layoutName),
                StoredChord = stored ?? "",
                ChordText = binding.ParsedChord.HasValue ? binding.ParsedChord.Value.Display : (stored ?? ""),
                State = binding.State,
                ClashWith = clash,
                ModulePage = modulePage,
                Source = source,
            };
        }
    }

    /// <summary>
    /// The one place that reads and writes the chord and the switch of a fixed binding on
    /// <see cref="AppConfig"/>. The overview's reset and its tests go through it, so a fixed chord added
    /// to <see cref="ChordKind"/> without an entry here fails loudly instead of being skipped.
    /// </summary>
    internal static class ChordConfigAccess
    {
        public static string GetChord(AppConfig config, ChordKind kind)
        {
            switch (kind)
            {
                case ChordKind.Case: return config.CaseHotkey;
                case ChordKind.History: return config.ClipboardHistoryHotkey;
                case ChordKind.QuickNotes: return config.QuickNotesHotkey;
                case ChordKind.Screenshot: return config.ScreenshotHotkey;
                case ChordKind.ScreenshotFull: return config.ScreenshotFullHotkey;
                case ChordKind.ScreenshotAnnotateArea: return config.ScreenshotAnnotateAreaHotkey;
                case ChordKind.ScreenshotFullAnnotate: return config.ScreenshotFullAnnotateHotkey;
                case ChordKind.ScreenshotFolder: return config.ScreenshotFolderHotkey;
                case ChordKind.TextMenu: return config.TextMenuHotkey;
                case ChordKind.Symbols: return config.SymbolsHotkey;
                case ChordKind.TrafficLight: return config.TrafficLightHotkey;
                case ChordKind.ExchangeSend: return config.ExchangeClipboardSendHotkey;
                case ChordKind.ExchangeGet: return config.ExchangeClipboardGetHotkey;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a fixed chord");
            }
        }

        public static void SetChord(AppConfig config, ChordKind kind, string token)
        {
            switch (kind)
            {
                case ChordKind.Case: config.CaseHotkey = token; break;
                case ChordKind.History: config.ClipboardHistoryHotkey = token; break;
                case ChordKind.QuickNotes: config.QuickNotesHotkey = token; break;
                case ChordKind.Screenshot: config.ScreenshotHotkey = token; break;
                case ChordKind.ScreenshotFull: config.ScreenshotFullHotkey = token; break;
                case ChordKind.ScreenshotAnnotateArea: config.ScreenshotAnnotateAreaHotkey = token; break;
                case ChordKind.ScreenshotFullAnnotate: config.ScreenshotFullAnnotateHotkey = token; break;
                case ChordKind.ScreenshotFolder: config.ScreenshotFolderHotkey = token; break;
                case ChordKind.TextMenu: config.TextMenuHotkey = token; break;
                case ChordKind.Symbols: config.SymbolsHotkey = token; break;
                case ChordKind.TrafficLight: config.TrafficLightHotkey = token; break;
                case ChordKind.ExchangeSend: config.ExchangeClipboardSendHotkey = token; break;
                case ChordKind.ExchangeGet: config.ExchangeClipboardGetHotkey = token; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a fixed chord");
            }
        }

        public static bool GetSwitch(AppConfig config, ChordKind kind)
        {
            switch (kind)
            {
                case ChordKind.Case: return config.EnableCaseHotkey;
                case ChordKind.History: return config.EnableHistoryHotkey;
                case ChordKind.QuickNotes: return config.EnableQuickNotesHotkey;
                case ChordKind.Screenshot: return config.EnableScreenshotHotkey;
                case ChordKind.ScreenshotFull: return config.EnableScreenshotFullHotkey;
                case ChordKind.ScreenshotAnnotateArea: return config.EnableScreenshotAnnotateAreaHotkey;
                case ChordKind.ScreenshotFullAnnotate: return config.EnableScreenshotFullAnnotateHotkey;
                case ChordKind.ScreenshotFolder: return config.EnableScreenshotFolderHotkey;
                case ChordKind.TextMenu: return config.EnableTextMenuHotkey;
                case ChordKind.Symbols: return config.EnableSymbolsHotkey;
                case ChordKind.TrafficLight: return config.EnableTrafficLightHotkey;
                case ChordKind.ExchangeSend: return config.EnableExchangeClipboardSendHotkey;
                case ChordKind.ExchangeGet: return config.EnableExchangeClipboardGetHotkey;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a fixed chord");
            }
        }

        public static void SetSwitch(AppConfig config, ChordKind kind, bool on)
        {
            switch (kind)
            {
                case ChordKind.Case: config.EnableCaseHotkey = on; break;
                case ChordKind.History: config.EnableHistoryHotkey = on; break;
                case ChordKind.QuickNotes: config.EnableQuickNotesHotkey = on; break;
                case ChordKind.Screenshot: config.EnableScreenshotHotkey = on; break;
                case ChordKind.ScreenshotFull: config.EnableScreenshotFullHotkey = on; break;
                case ChordKind.ScreenshotAnnotateArea: config.EnableScreenshotAnnotateAreaHotkey = on; break;
                case ChordKind.ScreenshotFullAnnotate: config.EnableScreenshotFullAnnotateHotkey = on; break;
                case ChordKind.ScreenshotFolder: config.EnableScreenshotFolderHotkey = on; break;
                case ChordKind.TextMenu: config.EnableTextMenuHotkey = on; break;
                case ChordKind.Symbols: config.EnableSymbolsHotkey = on; break;
                case ChordKind.TrafficLight: config.EnableTrafficLightHotkey = on; break;
                case ChordKind.ExchangeSend: config.EnableExchangeClipboardSendHotkey = on; break;
                case ChordKind.ExchangeGet: config.EnableExchangeClipboardGetHotkey = on; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a fixed chord");
            }
        }
    }
}
