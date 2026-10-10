using System;

namespace CyrFlip
{
    /// <summary>
    /// What each binding shipped with - the one place the hotkeys overview's "reset" reads (ticket S0056,
    /// rule R-defaults). Every value is an existing constant of the code; nothing is copied into the UI.
    ///
    /// <para>A binding is one of three shapes. A <b>fixed</b> chord (case flip, history, quick notes,
    /// symbols, capture, capture folder, text menu) always has a chord and a shipped one. A <b>row</b>
    /// (conversion, translation) has a shipped chord only while it is still the seeded row. Everything else
    /// (a scenario, a row the user made) has no shipped chord, and its reset is "clear the chord".</para>
    /// </summary>
    internal static class ChordDefaults
    {
        /// <summary>True for the seven chords that always exist and always have a chord.</summary>
        public static bool IsFixed(ChordKind kind)
        {
            switch (kind)
            {
                case ChordKind.Case:
                case ChordKind.History:
                case ChordKind.QuickNotes:
                case ChordKind.Screenshot:
                case ChordKind.ScreenshotFolder:
                case ChordKind.TextMenu:
                case ChordKind.Symbols:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The shipped chord token of a fixed chord. Throws for a kind that has none - the inventory
        /// test (<c>ChordDefaultsTests</c>) fails first, which is how a new fixed chord without an
        /// entry here is caught.
        /// </summary>
        public static string FixedChord(ChordKind kind)
        {
            switch (kind)
            {
                case ChordKind.Case: return AppConfig.DefaultCaseHotkey;
                case ChordKind.History: return AppConfig.DefaultHistoryHotkey;
                case ChordKind.QuickNotes: return AppConfig.DefaultQuickNotesHotkey;
                case ChordKind.Screenshot: return AppConfig.DefaultScreenshotHotkey;
                case ChordKind.ScreenshotFolder: return AppConfig.DefaultScreenshotFolderHotkey;
                case ChordKind.TextMenu: return AppConfig.DefaultTextMenuHotkey;
                case ChordKind.Symbols: return AppConfig.DefaultSymbolsHotkey;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a fixed chord");
            }
        }

        /// <summary>The shipped mouse chord of the text menu, as the invariant token.</summary>
        public static string MouseChordToken => MouseChord.Default.Token;

        /// <summary>
        /// The seeded EN ⇄ RU conversion row: still that row when it converts between the same two
        /// layouts it was seeded with, whatever chord it carries now (R3).
        /// </summary>
        public static bool IsSeededConversion(LayoutConversionProfile profile)
            => profile != null
               && string.Equals(profile.SourceKlid, AppConfig.UsKlid, StringComparison.OrdinalIgnoreCase)
               && string.Equals(profile.TargetKlid, AppConfig.RussianKlid, StringComparison.OrdinalIgnoreCase);

        /// <summary>The translator's starter row: the one that translates into the language of the UI.</summary>
        public static bool IsSeededTranslation(TranslationProfile profile)
            => profile != null
               && string.Equals(profile.TargetLang, TranslationLanguages.UiToken, StringComparison.Ordinal);

        /// <summary>The chord a seeded conversion row ships with.</summary>
        public static string SeededConversionChord => AppConfig.DefaultFlipHotkey;

        /// <summary>The chord the translator's starter row ships with.</summary>
        public static string SeededTranslationChord => AppConfig.DefaultTranslateHotkey;

        /// <summary>
        /// Two stored chord tokens name the same chord: they parse and press the same keys, or neither
        /// parses and the text is the same. An empty token equals only another empty one.
        /// </summary>
        public static bool SameChordToken(string? a, string? b)
        {
            a ??= ""; b ??= "";
            bool parsedA = Hotkey.TryParse(a, out Hotkey chordA);
            bool parsedB = Hotkey.TryParse(b, out Hotkey chordB);
            if (parsedA && parsedB) return chordA.SameChord(chordB);
            if (parsedA || parsedB) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
