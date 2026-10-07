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
            bool rowEnabled, bool moduleEnabled, ChordRegistry? registry = null, object? source = null)
        {
            if (string.IsNullOrEmpty(rawChord))
                return new ChordBinding(kind, id, rawChord, null, ChordBindingState.NoChord, null, source);

            if (!Hotkey.TryParse(rawChord, out Hotkey parsed))
                return new ChordBinding(kind, id, rawChord, null, ChordBindingState.Unparsable, null, source);

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

        public static bool IsCaseActive(AppConfig config)
            => config.EnableHotkeys && config.EnableCaseHotkey && Hotkey.TryParse(config.CaseHotkey, out _);

        public static bool IsHistoryActive(AppConfig config)
            => config.EnableHotkeys && config.EnableHistoryHotkey && Hotkey.TryParse(config.ClipboardHistoryHotkey, out _);

        public static bool IsQuickNotesActive(AppConfig config)
            => config.EnableHotkeys && config.EnableQuickNotes && config.EnableQuickNotesHotkey && Hotkey.TryParse(config.QuickNotesHotkey, out _);

        public static bool IsSymbolsActive(AppConfig config)
            => config.EnableHotkeys && config.EnableSymbols && config.EnableSymbolsHotkey && Hotkey.TryParse(config.SymbolsHotkey, out _);

        public static bool IsScreenshotActive(AppConfig config)
            => config.EnableHotkeys && config.EnableScreenshotHotkey && Hotkey.TryParse(config.ScreenshotHotkey, out _);

        public static bool IsScreenshotFolderActive(AppConfig config)
            => config.EnableHotkeys && config.ScreenshotSaveEnabled && config.EnableScreenshotFolderHotkey && Hotkey.TryParse(config.ScreenshotFolderHotkey, out _);

        public static bool IsTextMenuActive(AppConfig config)
            => config.EnableHotkeys && config.EnableContextMenu && config.EnableTextMenuHotkey && Hotkey.TryParse(config.TextMenuHotkey, out _);

        public static bool IsConversionActive(AppConfig config, LayoutConversionProfile profile)
            => config.EnableHotkeys && profile != null && profile.Enabled && profile.IsUsable && Hotkey.TryParse(profile.Hotkey, out _);

        public static bool IsTranslationActive(AppConfig config, TranslationProfile profile)
            => config.EnableHotkeys && config.EnableTranslate && profile != null && profile.Enabled && profile.IsUsable && Hotkey.TryParse(profile.Hotkey, out _);

        public static bool IsLauncherActive(AppConfig config, LauncherScenario scenario)
            => config.EnableHotkeys && config.EnableScenarioLauncher && scenario != null && !string.IsNullOrEmpty(scenario.Hotkey) && Hotkey.TryParse(scenario.Hotkey, out _);
    }
}
