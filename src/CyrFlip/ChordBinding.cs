using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The runtime binding state of a chord (tickets S0056 and S0059).
    /// </summary>
    internal enum ChordBindingState
    {
        /// <summary>The keyboard hook actively matches and binds this chord right now.</summary>
        Active,
        /// <summary>Its own row/feature switch is off.</summary>
        OffByRow,
        /// <summary>The owning module is disabled in settings (e.g. translator, launcher, quick notes).</summary>
        OffByModule,
        /// <summary>The global master switch <see cref="AppConfig.EnableHotkeys"/> is off.</summary>
        OffByMaster,
        /// <summary>The row has no chord configured (empty).</summary>
        NoChord,
        /// <summary>The stored chord token failed to parse as a valid hotkey.</summary>
        Unparsable,
        /// <summary>
        /// The row's own fields do not make a binding the hook takes: a KLID that is not eight characters, a
        /// translation with no target (<see cref="LayoutConversionProfile.IsUsable"/>, <see cref="TranslationProfile.IsUsable"/>).
        /// </summary>
        Incomplete,
        /// <summary>Another active CyrFlip owner shares this chord.</summary>
        Clash,
    }

    /// <summary>
    /// Pure model for a hotkey binding and its active evaluation (rules B1-B2 of S0056/S0059).
    /// Both <see cref="CyrFlipContext"/>, the tray hover popup and the hotkeys overview tab
    /// evaluate chord states from this single source of truth.
    /// </summary>
    internal sealed class ChordBinding
    {
        public ChordKind Kind { get; }
        public string Id { get; }
        public string? RawChord { get; }
        public Hotkey? ParsedChord { get; }
        public ChordBindingState State { get; }
        public ChordOwner? ClashOwner { get; }
        public object? Source { get; }

        public bool IsActive => State == ChordBindingState.Active || State == ChordBindingState.Clash;

        public ChordBinding(ChordKind kind, string id, string? rawChord, Hotkey? parsedChord,
            ChordBindingState state, ChordOwner? clashOwner = null, object? source = null)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            RawChord = rawChord;
            ParsedChord = parsedChord;
            State = state;
            ClashOwner = clashOwner;
            Source = source;
        }

        /// <summary>
        /// Evaluates the runtime state of a chord.
        /// </summary>
        public static ChordBinding Evaluate(AppConfig config, ChordKind kind, string id, string? rawChord,
            bool rowEnabled, bool moduleEnabled, ChordRegistry? registry = null, object? source = null, bool usable = true)
        {
            if (string.IsNullOrEmpty(rawChord))
                return new ChordBinding(kind, id, rawChord, null, ChordBindingState.NoChord, null, source);

            if (!Hotkey.TryParse(rawChord, out Hotkey parsed))
                return new ChordBinding(kind, id, rawChord, null, ChordBindingState.Unparsable, null, source);

            // The hook takes no row that is not usable, whatever its switch says (S0056 B1): the overview must say so too.
            if (!usable)
                return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.Incomplete, null, source);

            if (!moduleEnabled)
                return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.OffByModule, null, source);

            if (!rowEnabled)
                return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.OffByRow, null, source);

            if (!config.EnableHotkeys && kind != ChordKind.WindowsLanguage)
                return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.OffByMaster, null, source);

            ChordOwner? clash = registry?.CyrFlipOwnerOf(parsed, kind, id);
            if (clash != null)
                return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.Clash, clash, source);

            return new ChordBinding(kind, id, rawChord, parsed, ChordBindingState.Active, null, source);
        }

        // ---- Pure predicates matching CyrFlipContext / KeyboardHook ----

        // The module-and-own-switch half of "is this chord bound", without the master switch and without
        // the chord text. CyrFlipContext's *ChordLive properties call these, so the hook and the overview
        // (ticket S0056, B1) are made from one condition and cannot drift apart when a module gets a switch.
        public static bool QuickNotesLive(AppConfig config) => config.EnableQuickNotes && config.EnableQuickNotesHotkey;
        public static bool SymbolsLive(AppConfig config) => config.EnableSymbols && config.EnableSymbolsHotkey;
        public static bool TrafficLightLive(AppConfig config) => config.EnableTrafficLight && config.EnableTrafficLightHotkey;
        public static bool ScreenshotLive(AppConfig config) => config.EnableScreenshotHotkey;
        public static bool ScreenshotFolderLive(AppConfig config) => config.ScreenshotSaveEnabled && config.EnableScreenshotFolderHotkey;
        public static bool TextMenuLive(AppConfig config) => config.EnableContextMenu && config.EnableTextMenuHotkey;

        public static bool IsCaseActive(AppConfig config)
            => config.EnableHotkeys && config.EnableCaseHotkey && Hotkey.TryParse(config.CaseHotkey, out _);

        public static bool IsHistoryActive(AppConfig config)
            => config.EnableHotkeys && config.EnableHistoryHotkey && Hotkey.TryParse(config.ClipboardHistoryHotkey, out _);

        public static bool IsQuickNotesActive(AppConfig config)
            => config.EnableHotkeys && QuickNotesLive(config) && Hotkey.TryParse(config.QuickNotesHotkey, out _);

        public static bool IsSymbolsActive(AppConfig config)
            => config.EnableHotkeys && SymbolsLive(config) && Hotkey.TryParse(config.SymbolsHotkey, out _);

        public static bool IsTrafficLightActive(AppConfig config)
            => config.EnableHotkeys && TrafficLightLive(config) && Hotkey.TryParse(config.TrafficLightHotkey, out _);

        public static bool IsScreenshotActive(AppConfig config)
            => config.EnableHotkeys && ScreenshotLive(config) && Hotkey.TryParse(config.ScreenshotHotkey, out _);

        public static bool IsScreenshotFolderActive(AppConfig config)
            => config.EnableHotkeys && ScreenshotFolderLive(config) && Hotkey.TryParse(config.ScreenshotFolderHotkey, out _);

        public static bool IsTextMenuActive(AppConfig config)
            => config.EnableHotkeys && TextMenuLive(config) && Hotkey.TryParse(config.TextMenuHotkey, out _);

        public static bool IsConversionActive(AppConfig config, LayoutConversionProfile profile)
            => config.EnableHotkeys && profile != null && profile.Enabled && profile.IsUsable && Hotkey.TryParse(profile.Hotkey, out _);

        public static bool IsTranslationActive(AppConfig config, TranslationProfile profile)
            => config.EnableHotkeys && config.EnableTranslate && profile != null && profile.Enabled && profile.IsUsable && Hotkey.TryParse(profile.Hotkey, out _);

        public static bool IsLauncherActive(AppConfig config, LauncherScenario scenario)
            => config.EnableHotkeys && config.EnableScenarioLauncher && scenario != null && !string.IsNullOrEmpty(scenario.Hotkey) && Hotkey.TryParse(scenario.Hotkey, out _);
    }
}
