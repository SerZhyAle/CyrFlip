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
        /// Any language code (see <see cref="TranslationLanguages.AllCodes"/>), or one of the live tokens
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
    /// Where one press of a translation chord sends the selection: the language to translate into and,
    /// for a fixed pair, the language the text is expected to be in (null = let the model work it out).
    /// </summary>
    internal readonly struct TranslationDirection
    {
        public readonly string TargetCode;
        public readonly string? SourceCode;

        public TranslationDirection(string targetCode, string? sourceCode)
        {
            TargetCode = targetCode; SourceCode = sourceCode;
        }
    }

    /// <summary>
    /// The languages a translation row can point at: the 13 CyrFlip UI languages plus two tokens that
    /// resolve at the moment the chord fires. The English name is what goes into the prompt (models
    /// follow "translate into German" far better than "translate into de"), the label is what the user
    /// sees, and both stay correct for a code outside the curated set.
    /// </summary>
    internal static class TranslationLanguages
    {
        /// <summary>Auto-detect the source, translate into the CyrFlip UI language (option 1).</summary>
        public const string UiToken = "ui";

        /// <summary>Follow the keyboard layout that is active in the target window right now.</summary>
        public const string ActiveToken = "active";

        /// <summary>Auto-detect the source, translate into the configured target language (option 2).</summary>
        public const string TargetToken = "target";

        /// <summary>Configured source → configured target (option 3).</summary>
        public const string SourceToTargetToken = "src>tgt";

        /// <summary>Configured target → configured source (option 4) - the other half of the pair.</summary>
        public const string TargetToSourceToken = "tgt>src";

        private static readonly Dictionary<string, string> EnglishNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ru", "Russian" }, { "en", "English" }, { "uk", "Ukrainian" }, { "de", "German" },
            { "it", "Italian" }, { "es", "Spanish" }, { "fr", "French" }, { "pt", "Portuguese" },
            { "ar", "Arabic" }, { "hi", "Hindi" }, { "bn", "Bengali" }, { "ur", "Urdu" },
            { "zh", "Chinese" },
        };

        /// <summary>
        /// Every language Windows knows, offered in the pickers as-is.
        ///
        /// <para>This used to be <c>Localization.Codes</c> - the 13 <b>interface</b> languages - which tied
        /// two unrelated sets together: those 13 were picked for translating CyrFlip's own UI, not for what
        /// a model can do. The result was wrong in both directions, measured on a live Ollama (spec
        /// §3.3.2): the default model garbles Ukrainian, Hindi, Bengali and Urdu, all of which were on
        /// offer, and translates Japanese perfectly, which was not.</para>
        ///
        /// <para><b>CyrFlip deliberately makes no claim about coverage.</b> There is no source to make one
        /// from - the GGUF <c>general.languages</c> key is empty on every model checked, and a model's
        /// prose sometimes gives a count without a list. More to the point, coverage is the model's
        /// property, not ours: CyrFlip dispatches the selection to whatever the user runs. So the list is
        /// open, nothing is greyed out or blocked, and the UI points at the model's own page instead
        /// (<see cref="ModelPageUrl"/>).</para>
        /// </summary>
        public static string[] AllCodes { get; } = BuildAllCodes();

        private static string[] BuildAllCodes()
        {
            var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                {
                    string code = culture.TwoLetterISOLanguageName;
                    // Two-letter ISO 639-1 only: the three-letter leftovers come back from Windows
                    // as "Unknown Language", which is a worse picker entry than no entry at all.
                    // "iv" is the invariant culture, which is not a language anyone translates into.
                    if (code.Length == 2 && !string.Equals(code, "iv", StringComparison.OrdinalIgnoreCase))
                        codes.Add(code.ToLowerInvariant());
                }
            }
            catch { /* a stripped-down .NET install - the 13 below still give a usable picker */ }

            // Whatever the OS answered, the languages CyrFlip itself ships in are always offerable.
            foreach (string code in Localization.Codes) codes.Add(code);

            var all = new List<string>(codes);
            return all.ToArray();
        }

        /// <summary>
        /// The page documenting what a model can do, opened when the user asks "does it know my
        /// language?". CyrFlip answers that question with a link rather than a claim of its own - see
        /// <see cref="AllCodes"/>.
        ///
        /// <para>The part after ':' is the size/quantization tag, not part of the library path:
        /// <c>qwen2.5:3b</c> is documented at <c>/library/qwen2.5</c>. A name carrying '/' came from
        /// somewhere else entirely (<c>hf.co/user/repo</c>), so there is no library page to guess and the
        /// user goes to the index instead of to a 404.</para>
        /// </summary>
        public static string ModelPageUrl(string? model)
        {
            const string index = "https://ollama.com/library";
            string value = (model ?? "").Trim();
            int tag = value.IndexOf(':');
            if (tag >= 0) value = value.Substring(0, tag);
            value = value.Trim();
            if (value.Length == 0 || value.IndexOf('/') >= 0) return index;
            return index + "/" + Uri.EscapeDataString(value);
        }

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

        /// <summary>
        /// The full direction a row means right now: what to translate <b>into</b>, and - for the two
        /// fixed-pair rows - what the text is <b>expected</b> to be in.
        ///
        /// The expectation is a hint for the prompt, never an assertion: a user who presses the other
        /// half of the pair by mistake must still get a translation rather than a model solemnly
        /// translating from a language that is not there.
        /// </summary>
        public static TranslationDirection ResolveDirection(string? targetLang, string? uiLanguage,
            string? activeCode, string? sourceSetting, string? targetSetting)
        {
            string value = (targetLang ?? "").Trim();
            string source = SettingCode(sourceSetting, uiLanguage);
            string target = SettingCode(targetSetting, "English");

            if (string.Equals(value, TargetToken, StringComparison.OrdinalIgnoreCase))
                return new TranslationDirection(target, null);
            if (string.Equals(value, SourceToTargetToken, StringComparison.OrdinalIgnoreCase))
                return new TranslationDirection(target, source);
            if (string.Equals(value, TargetToSourceToken, StringComparison.OrdinalIgnoreCase))
                return new TranslationDirection(source, target);

            return new TranslationDirection(Resolve(value, uiLanguage, activeCode), null);
        }

        /// <summary>A configured code, or the fallback language's code when it was never set.</summary>
        private static string SettingCode(string? code, string? fallbackLanguage)
        {
            string value = (code ?? "").Trim().ToLowerInvariant();
            return value.Length >= 2 ? value : UiCode(fallbackLanguage);
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
            if (string.Equals(value, TargetToken, StringComparison.OrdinalIgnoreCase))
                return Localization.Translate(uiLanguage, "Автоопределение → язык перевода");
            if (string.Equals(value, SourceToTargetToken, StringComparison.OrdinalIgnoreCase))
                return Localization.Translate(uiLanguage, "Мой язык → язык перевода");
            if (string.Equals(value, TargetToSourceToken, StringComparison.OrdinalIgnoreCase))
                return Localization.Translate(uiLanguage, "Язык перевода → мой язык");

            int index = Array.IndexOf(Localization.Codes, value.ToLowerInvariant());
            // The endonym is the label everywhere else in CyrFlip's language pickers.
            if (index >= 0) return Localization.Names[index];
            return EnglishName(value);
        }

        private static string UiCode(string? uiLanguage)
            => Localization.Codes[Localization.IndexOf(uiLanguage)];
    }
}
