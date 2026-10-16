using System;
using System.Collections.Generic;
using System.Globalization;

namespace CyrFlip
{
    /// <summary>
    /// The single localization layer for the whole app: settings window, tray menu and every dialog.
    ///
    /// The key is the <b>English</b> source string (ticket S0070): code is English, English is the source
    /// language, and Russian is a translation like Ukrainian. Call sites read like plain text and no
    /// resource-id indirection is needed; the value is one translation per supported language. Adding
    /// a language means one column here instead of a ternary in every file - which is exactly why the
    /// old <c>ru ? ... : uk ? ...</c> pattern was replaced.
    ///
    /// The set of UI languages is the curated world set (see <see cref="WorldLayouts.Popular"/>) plus
    /// Russian, so a user who can type a language can also read the interface in it.
    ///
    /// A key never holds Cyrillic. When two surfaces need one English word in two grammatical forms of
    /// another language (<c>Large</c> for a marker and for the tiles), the second gets a distinct key with a
    /// context in parentheses and an <c>en:</c> display override that keeps the English text unchanged.
    /// </summary>
    internal static partial class Localization
    {
        /// <summary>Endonyms, in menu order. This exact string is what <c>AppConfig.UiLanguage</c> stores.</summary>
        public static readonly string[] Names =
        {
            "Русский", "English", "Українська", "Deutsch", "Italiano", "Español", "Français",
            "Português", "العربية", "हिन्दी", "বাংলা", "اردو", "中文",
        };

        /// <summary>ISO codes parallel to <see cref="Names"/>; index 1 is the source language (English).</summary>
        public static readonly string[] Codes =
        {
            "ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh",
        };

        private const int English = 1;

        // One row per key, parallel to Names: Values[i] is language i. Values[English] is the key itself, or its
        // `en:` display override, so it is never empty and is where an empty translation falls back to.
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.Ordinal);

        static Localization()
        {
            AddGeneralStrings();
            AddHotkeyStrings();
            AddClipboardStrings();
            AddConversionStrings();
            AddWindowsLanguageStrings();
            AddTrayStrings();
            AddLauncherStrings();
            AddTranslateStrings();
            AddContextMenuStrings();
            AddQuickNotesStrings();
            AddSupportStrings();
            AddThemeStrings();
            AddExchangeStrings();
            AddExchangeServerStrings();
            AddGraphicsStrings();
            AddSettingsStrings();
            AddWelcomeStrings();
            AddSettingsTransferStrings();
            AddSymbolsStrings();
            AddHotkeysOverviewStrings();
            AddTrayPopupStrings();
            AddTrafficLightStrings();
            AddSettingsRowStrings();
            AddActivationStrings();
            AddHelpStrings();
        }

        /// <summary>Every registered source string with its translations (parallel to <see cref="Names"/>) - used by the localization tests.</summary>
        public static IEnumerable<KeyValuePair<string, string[]>> All => Map;

        public static int IndexOf(string? language)
        {
            if (string.IsNullOrEmpty(language)) return English;
            int index = Array.IndexOf(Names, language);
            if (index >= 0) return index;
            index = Array.IndexOf(Codes, language);
            return index < 0 ? English : index;
        }

        /// <summary>
        /// Translate an English source string. An unknown key returns the source unchanged (so a new
        /// string is visible but never crashes), and a language with no translation yet falls back to
        /// the source language (<c>LOCALIZATION</c> section 1), never to Russian.
        /// </summary>
        public static string Translate(string language, string en)
        {
            if (string.IsNullOrEmpty(en)) return string.Empty;
            if (!Map.TryGetValue(en, out string[]? values)) return en;
            string value = values[IndexOf(language)];
            if (value.Length == 0) value = values[English];
            return value.Length == 0 ? en : value;
        }

        /// <summary>
        /// Safely formats a translated string with the given arguments (APP-BEHAVIOUR rule 7).
        /// If formatting fails (e.g. broken placeholder in template), catches <see cref="FormatException"/>
        /// and falls back to formatting the English template or returning the template as-is.
        /// </summary>
        public static string Format(string language, string? en, params object?[] args)
        {
            if (string.IsNullOrEmpty(en)) return string.Empty;
            return FormatTemplate(Translate(language, en!), en!, args);
        }

        /// <summary>
        /// The same safe formatting for a caller that holds a translating function rather than a language -
        /// the <c>T</c> helper of a window, or the <c>translate</c> seam the launcher and the menu builder
        /// take so a test can run them without a UI language.
        /// </summary>
        public static string Format(Func<string, string> translate, string? en, params object?[] args)
        {
            if (string.IsNullOrEmpty(en)) return string.Empty;
            string template = translate(en!);
            return FormatTemplate(string.IsNullOrEmpty(template) ? en! : template, en!, args);
        }

        private static string FormatTemplate(string template, string en, object?[] args)
        {
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                try { return string.Format(en, args); }
                catch { return template; }
            }
        }

        /// <summary>Arabic and Urdu are written right-to-left; WinForms has to mirror for them.</summary>
        public static bool IsRightToLeft(string language)
        {
            string code = Codes[IndexOf(language)];
            return code == "ar" || code == "ur";
        }

        /// <summary>
        /// UI font family for a script Segoe UI does not cover. Windows ships Nirmala UI for the Indic
        /// scripts and Microsoft YaHei UI for Chinese; Arabic and Urdu are covered by Segoe UI itself
        /// (Urdu reads better in Nastaliq, but Urdu Typesetting is not a UI font, so Segoe UI stays).
        /// Returns null when the default font is fine.
        /// </summary>
        public static string? FontFamily(string language)
        {
            switch (Codes[IndexOf(language)])
            {
                case "hi":
                case "bn": return "Nirmala UI";
                case "zh": return "Microsoft YaHei UI";
                default: return null;
            }
        }

        /// <summary>
        /// Multiplier for the fixed column widths of the layout / conversion / hotkey tables. German,
        /// French, Spanish and Portuguese run visibly longer than Russian for the same phrase, and the
        /// Indic scripts need more vertical and horizontal room per glyph cluster; without this the
        /// AutoEllipsis on those labels would eat the end of half the rows.
        /// </summary>
        public static float WidthScale(string language)
        {
            switch (Codes[IndexOf(language)])
            {
                case "de":
                case "fr":
                case "es":
                case "pt":
                case "it": return 1.15f;
                case "hi":
                case "bn": return 1.10f;
                case "ar":
                case "ur": return 1.05f;
                default: return 1f;
            }
        }

        /// <summary>Column width in pixels for the current language (see <see cref="WidthScale"/>).</summary>
        public static int Scaled(string language, int width) => (int)Math.Round(width * WidthScale(language));

        /// <summary>
        /// UI language for a fresh install (no saved value): the language Windows' own interface is in,
        /// when CyrFlip is translated into it - otherwise English, never Russian.
        ///
        /// The user's <b>display</b> language comes first (<see cref="CultureInfo.CurrentUICulture"/>,
        /// i.e. GetUserDefaultUILanguage), and the language Windows was installed in only after it:
        /// someone who installed an English Windows and then switched the display language to Russian is
        /// reading a Russian desktop, and that is what "the OS language" means to them - but
        /// InstalledUICulture would still say English. The second is kept as a fallback because a
        /// restricted or unusual configuration can leave the first at the invariant culture.
        /// </summary>
        public static string DefaultLanguage()
        {
            try
            {
                return MatchLanguage(CultureInfo.CurrentUICulture)
                    ?? MatchLanguage(CultureInfo.InstalledUICulture)
                    ?? "English";
            }
            catch { return "English"; }
        }

        /// <summary>
        /// The endonym for a culture, or null when CyrFlip has no translation for it. Regional variants
        /// resolve to their language, so de-AT, pt-BR and zh-Hans-CN all find their translation.
        /// </summary>
        internal static string? MatchLanguage(CultureInfo? culture)
        {
            if (culture == null) return null;
            int index = Array.IndexOf(Codes, culture.TwoLetterISOLanguageName);
            return index >= 0 ? Names[index] : null;
        }

        /// <param name="source">The English source text - the key.</param>
        /// <param name="en">The English display text, only for a key that carries a context in parentheses
        /// to tell it from another key of the same English wording; otherwise the key is the English text.</param>
        private static void Add(string source, string ru, string uk, string de, string it, string es, string fr,
            string pt, string ar, string hi, string bn, string ur, string zh, string? en = null)
        {
            // Last wins, so a second registration would silently replace the first translation;
            // the test suite asserts this list stays empty (S0029 RB-10).
            if (Map.ContainsKey(source)) DuplicateKeys.Add(source);
            Map[source] = new[] { ru, en ?? source, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh };
        }

        /// <summary>Keys registered more than once - always empty in a correct build.</summary>
        internal static readonly List<string> DuplicateKeys = new List<string>();
    }
}
