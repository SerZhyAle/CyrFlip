using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// One row of the translation table: "translate the selection into this language, on this chord".
    /// Same shape as <see cref="LayoutConversionProfile"/> - a string id that survives edits, its own
    /// switch, and no position field (the order in the list is the order in the table).
    /// </summary>
    internal sealed class TranslationProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// A language code from <see cref="Localization.Codes"/>, or one of the two live tokens
        /// <see cref="TranslationLanguages.UiToken"/> / <see cref="TranslationLanguages.ActiveToken"/>.
        /// </summary>
        public string TargetLang { get; set; } = TranslationLanguages.UiToken;

        public string Hotkey { get; set; } = "";
        public bool Enabled { get; set; } = true;

        /// <summary>A row with no chord is inert: it is shown in the table but never fires.</summary>
        [ScriptIgnore]
        public bool IsUsable => TargetLang.Length > 0 && Hotkey.Length > 0;

        public TranslationProfile Clone() => new TranslationProfile
        {
            Id = Id, TargetLang = TargetLang, Hotkey = Hotkey, Enabled = Enabled,
        };
    }

    /// <summary>
    /// The languages a translation row can point at: the 13 CyrFlip UI languages plus two tokens that
    /// resolve at the moment the chord fires. The English name is what goes into the prompt (models
    /// follow "translate into German" far better than "translate into de"), the label is what the user
    /// sees, and both stay correct for a code outside the curated set.
    /// </summary>
    internal static class TranslationLanguages
    {
        /// <summary>Follow the CyrFlip UI language.</summary>
        public const string UiToken = "ui";

        /// <summary>Follow the keyboard layout that is active in the target window right now.</summary>
        public const string ActiveToken = "active";

        private static readonly Dictionary<string, string> EnglishNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ru", "Russian" }, { "en", "English" }, { "uk", "Ukrainian" }, { "de", "German" },
            { "it", "Italian" }, { "es", "Spanish" }, { "fr", "French" }, { "pt", "Portuguese" },
            { "ar", "Arabic" }, { "hi", "Hindi" }, { "bn", "Bengali" }, { "ur", "Urdu" },
            { "zh", "Chinese" },
        };

        /// <summary>The codes offered in the picker, in UI-language order (Russian first).</summary>
        public static string[] Codes => Localization.Codes;

        /// <summary>
        /// The concrete language code a row means right now. <paramref name="activeCode"/> is the
        /// two-letter code of the layout in the target window (as the tray indicator reports it);
        /// an unusable one falls back to the UI language, never to silence.
        /// </summary>
        public static string Resolve(string? targetLang, string? uiLanguage, string? activeCode)
        {
            string value = (targetLang ?? "").Trim();
            if (value.Length == 0 || string.Equals(value, UiToken, StringComparison.OrdinalIgnoreCase))
                return UiCode(uiLanguage);

            if (string.Equals(value, ActiveToken, StringComparison.OrdinalIgnoreCase))
            {
                string active = (activeCode ?? "").Trim().ToLowerInvariant();
                return active.Length >= 2 ? active : UiCode(uiLanguage);
            }
            return value.ToLowerInvariant();
        }

        /// <summary>The language name to put in the prompt, in English.</summary>
        public static string EnglishName(string? code)
        {
            string value = (code ?? "").Trim();
            if (value.Length == 0) return "English";
            if (EnglishNames.TryGetValue(value, out string? known)) return known;
            try
            {
                // A layout outside the curated set (Polish, Turkish, ..) still deserves a real name.
                string name = new CultureInfo(value).EnglishName;
                int bracket = name.IndexOf(" (", StringComparison.Ordinal);
                if (bracket > 0) name = name.Substring(0, bracket);
                // Windows answers an unknown tag with the literal "Unknown Language" instead of
                // throwing - and "translate into Unknown Language" is a worse prompt than the code.
                if (name.Length == 0 || name.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0)
                    return value.ToUpperInvariant();
                return name;
            }
            catch { return value.ToUpperInvariant(); }
        }

        /// <summary>What the table and the pickers show for a row's target.</summary>
        public static string Label(string? code, string uiLanguage)
        {
            string value = (code ?? "").Trim();
            if (value.Length == 0 || string.Equals(value, UiToken, StringComparison.OrdinalIgnoreCase))
                return Localization.Translate(uiLanguage, "Язык интерфейса");
            if (string.Equals(value, ActiveToken, StringComparison.OrdinalIgnoreCase))
                return Localization.Translate(uiLanguage, "Язык активной раскладки");

            int index = Array.IndexOf(Localization.Codes, value.ToLowerInvariant());
            // The endonym is the label everywhere else in CyrFlip's language pickers.
            if (index >= 0) return Localization.Names[index];
            return EnglishName(value);
        }

        private static string UiCode(string? uiLanguage)
            => Localization.Codes[Localization.IndexOf(uiLanguage)];
    }
}
