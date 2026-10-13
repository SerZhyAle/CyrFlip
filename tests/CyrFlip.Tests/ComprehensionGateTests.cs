using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0067 - the comprehension gates of the S0061 CU audit (spec section 3; the heuristics, the
    /// population and today's numbers are PLAN/S0061.../research/CU-2_findings.md section 3, facts
    /// G-CU1..G-CU7). A gate is a ratchet on a count, the way <see cref="ErrorBoundaryGateTests"/>
    /// is for catches: today's number is pinned, a change may only lower it, and each scanner has a
    /// guard fact that it sees what it claims to see - a gate that cannot fail is a finding.
    ///
    /// No wording is judged or rewritten here. The one item still red is the termbase concept term
    /// the app's English never says, carried in <see cref="ConceptTermKnownMissing"/> with its
    /// finding id and the owner decision it waits for (S0067 section 2.3, D-CU3): the gate fails
    /// the day a **new** red item arrives, and the entry is deleted the day the wording lands.
    /// The audit's other two carried items (the history hint, the ScreenshotFolder tooltip) landed
    /// in S0067 and their exception sets are gone.
    /// </summary>
    public class ComprehensionGateTests
    {
        private static readonly string Repo = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
        private static readonly string SourceRoot = Path.Combine(Repo, "src", "CyrFlip");
        private static readonly string TermbasePath = Path.Combine(Repo, "docs", "termbase.json");

        // ------------------------------------------------------------------ G-CU1: jargon

        // The audit's mechanism-word list (CU-2 section 2a), whole words, case-insensitive, with the
        // plural / participle endings the English strings actually use; the acronyms stay
        // case-sensitive so a word like "tip" or "ime" inside a sentence cannot trip them.
        private static readonly Regex JargonWord = new Regex(
            @"\b(hook|journal|snapshot|fence|registry|mutex|thread|payload|tokens?|handler|pipe|preload|delay-render|low-level)(s|ed|ing)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JargonAcronym = new Regex(
            @"(?<![A-Za-z])(KLID|HKL|DPAPI|TIP|IME|IPC|WH_)(?![A-Za-z])",
            RegexOptions.Compiled);

        /// <summary>
        /// 22 registered English strings carry a mechanism word today (CU-2-01..05). The audit's
        /// snapshot held 21; the quick-notes save-failure texts that landed after it added the
        /// 22nd ("Could not read the quick notes journal..." - the wording CU-2-03 suggests for
        /// exactly this word is the owner's). Every hit is an explanation or an error text; none
        /// is a switch or action caption. The ratchet: no new one, the goal is 0, each deletion
        /// (starting with that one) lowers this number.
        /// </summary>
        [Fact]
        public void NoNewMechanismWordReachesAnEnglishString()
        {
            var flagged = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                string en = EnglishOf(entry);
                if (JargonWord.IsMatch(en) || JargonAcronym.IsMatch(en))
                    flagged.Add(en);
            }

            Assert.True(flagged.Count <= 22,
                "A string gained a mechanism word (hook, DPAPI, journal, registry, ...) - the audit's jargon list grew from "
                + "22 (S0061 CU-2a). Say the outcome instead, or - if the word is the honest one - replace a counted string "
                + "with it and keep the ratchet at 22 by lowering another:" + Environment.NewLine
                + string.Join(Environment.NewLine, flagged));
        }

        /// <summary>
        /// The guard fact: the scan flags the strings it was built from - the DPAPI page description,
        /// the two hook balloons' word shape, an acronym inside a sentence, a participle - and does
        /// not flag an ordinary word that merely contains one of the letters sequences.
        /// </summary>
        [Fact]
        public void TheJargonScanRecognisesItsShapes()
        {
            Assert.Matches(JargonWord, "Could not hook the mouse");
            Assert.Matches(JargonWord, "two payloads and a registry write");
            Assert.Matches(JargonWord, "the journaling");
            Assert.DoesNotMatch(JargonWord, "a healthy journalist");   // "journalist" is not "journal" + a counted ending
            Assert.DoesNotMatch(JargonWord, "a fenced-off area");      // "fenced" is "fence" + "d", outside the counted endings
            Assert.Matches(JargonAcronym, "encrypted with Windows DPAPI for your account");
            Assert.DoesNotMatch(JargonAcronym, "a tip of the pointer");
            Assert.DoesNotMatch(JargonAcronym, "someIMEword");

            // And over the real table: the clipboard page description the audit counted (CU-2-01) is found.
            string en = Localization.Translate("English",
                "History is stored locally only and encrypted with Windows DPAPI for your account.");
            Assert.Contains("DPAPI", en);
            Assert.True(JargonWord.IsMatch(en) || JargonAcronym.IsMatch(en));
        }

        // ------------------------------------------------------------------ G-CU2: length

        // The plain-language ceiling of CU-2h: 20 whitespace-separated words / 140 characters,
        // counted over every registered English string (the audit's own two ratchets; hint-only and
        // per-area numbers need a classification a machine does not have).
        private static int WordCount(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>
        /// 76 English strings are over 20 words and 58 over 140 characters today (CU-2-34..35; the
        /// longest, the punctuation hint, is 114 words). The audit's snapshot held 75 over 20 words;
        /// the quick-notes save-failure texts that landed after it added the 76th. A new long
        /// string must replace a deleted one; a wording ticket that shortens texts lowers these
        /// numbers.
        /// </summary>
        [Fact]
        public void EnglishStringsOverThePlainLanguageCeilingAreRatcheted()
        {
            var overWords = new List<string>();
            var overChars = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                string en = EnglishOf(entry);
                if (WordCount(en) > 20) overWords.Add(entry.Key);
                if (en.Length > 140) overChars.Add(entry.Key);
            }

            Assert.True(overWords.Count <= 76,
                "English strings over 20 words: " + overWords.Count + " (ratchet 76, S0061 CU-2h). Split the text at the first "
                + "full stop - the first sentence says the outcome - or lower the ratchet by removing one:" + Environment.NewLine
                + string.Join(Environment.NewLine, overWords));
            Assert.True(overChars.Count <= 58,
                "English strings over 140 characters: " + overChars.Count + " (ratchet 58, S0061 CU-2h):" + Environment.NewLine
                + string.Join(Environment.NewLine, overChars));
        }

        /// <summary>The guard fact: the ceiling sees the audit's longest string, the punctuation hint.</summary>
        [Fact]
        public void TheLengthScanSeesTheLongestHint()
        {
            string en = Localization.Translate("English",
                "A key that gives a symbol rather than a letter in both layouts says nothing about the layout it was pressed in: the Russian key behind «/» carries a full stop, and the «/» of the numeric keypad is the same everywhere and cannot be told from the ordinary one once the text is copied. While this is ticked, such symbols are converted along with the text - the way CyrFlip has always behaved. Clear it if you type them on purpose more often: «/ghbdtn» then keeps its slash instead of starting with a full stop. Symbols whose key carries a letter in the other layout (the comma, the brackets) are converted either way.");
            Assert.True(WordCount(en) > 20 && en.Length > 140,
                "the punctuation hint (CU-2-34, 114 words) is not over the ceiling - the scan is looking at the wrong string");
        }

        // ------------------------------------------------------------------ G-CU3: hint coverage

        private static readonly Regex CheckboxFieldPattern = new Regex(
            @"CheckBox\s+([A-Za-z_]\w*)\s*=\s*Check\(", RegexOptions.Compiled);

        /// <summary>
        /// Every checkbox of the settings window carries a hint beside it: every field built by
        /// <c>Check(...)</c> is passed to a <c>Setting(field, hint)</c> or <c>HotkeyRow(field, ...,
        /// hint)</c> call somewhere in the SettingsForm partials (the master hotkey switch and the
        /// RDP toggle live in SettingsForm.HotkeysOverview.cs). Zero tolerance - 36 of 36 today
        /// (CU-2 section 4), and a checkbox with no hint is a settings page a person cannot read.
        /// </summary>
        [Fact]
        public void EveryCheckboxOfSettingsHasAHintBesideIt()
        {
            var sources = new List<KeyValuePair<string, string>>();   // file name -> content
            foreach (string file in Directory.GetFiles(SourceRoot, "SettingsForm*.cs"))
                sources.Add(new KeyValuePair<string, string>(Path.GetFileName(file), File.ReadAllText(file)));
            string all = string.Concat(sources.Select(s => s.Value + "\n"));

            var unhinted = new List<string>();
            int checkboxes = 0;
            foreach (Match field in CheckboxFieldPattern.Matches(all))
            {
                checkboxes++;
                string name = field.Groups[1].Value;
                bool hinted = new Regex(@"\b(?:Setting|HotkeyRow)\(\s*" + Regex.Escape(name) + @"\s*,").IsMatch(all);
                if (!hinted) unhinted.Add(name);
            }

            // The scan has to be looking at something: 36 checkboxes exist today (CU-1).
            Assert.True(checkboxes >= 30,
                "only " + checkboxes + " Check() fields were found - the scan is looking at the wrong thing");
            Assert.True(unhinted.Count == 0,
                "A settings checkbox was built without a hint - pass it to Setting(field, hint) or HotkeyRow: "
                + string.Join(", ", unhinted));
        }

        /// <summary>The guard fact: the source scan recognises the two call shapes that carry a hint.</summary>
        [Fact]
        public void TheHintCoverageScanRecognisesItsShapes()
        {
            string source =
                "private readonly CheckBox _a = Check(\"A\");\n" +
                "panel.Controls.Add(Setting(_a, \"hint a\"));\n" +
                "private readonly CheckBox _b = Check(\"B\");\n" +
                "panel.Controls.Add(HotkeyRow(_b, _v, () => {}, \"hint b\"));\n" +
                "private readonly CheckBox _c = Check(\"C\");\n" +
                "_c.CheckedChanged += (_, _) => {};\n";
            var names = CheckboxFieldPattern.Matches(source).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(new[] { "_a", "_b", "_c" }, names);
            Assert.Matches(new Regex(@"\b(?:Setting|HotkeyRow)\(\s*_a\s*,"), source);
            Assert.Matches(new Regex(@"\b(?:Setting|HotkeyRow)\(\s*_b\s*,"), source);
            Assert.DoesNotMatch(new Regex(@"\b(?:Setting|HotkeyRow)\(\s*_c\s*,"), source);
        }

        // ------------------------------------------------------------------ G-CU4: module defaults

        // The opt-in modules (default off, CU-2 section 2d) and the registered hint that is their
        // first-screen text. Hand-kept like DynamicStrings: the audit's seven, each named by the
        // config switch that makes the module exist and the localization key that explains it.
        private static readonly (string Switch, string HintKey)[] OptInModules =
        {
            ("EnableContextMenu",
                "CyrFlip's own menu over the selected text: copy, cut, paste, conversion, case, translation and quick launch. Off by default: while it is off, CyrFlip does not watch the mouse."),
            ("ScreenshotSaveEnabled",
                "Off by default: a capture goes to the clipboard only. With this on, every selected region is also saved as a PNG into the folder below."),
            ("EnableQuickNotes",
                "While it is off, CyrFlip neither reads nor creates the notes file, the tray has no notes entry and the chord is not bound. Notes already saved stay on disk."),
            ("EnableTranslate",
                "While this is off no translation chord is bound, the tray has no translate entry, and CyrFlip opens no network connection at all."),
            ("EnableScenarioLauncher",
                "Your programs, scripts and yt-dlp downloads: run them from the tray menu, from this table, with a global shortcut, or from the Jump List of the CyrFlip taskbar icon. While the switch is off, CyrFlip behaves exactly as before — no tray entry and no Jump List tasks."),
            ("EnableSymbols",
                "While disabled, the symbols window is not created, no symbols item appears in the tray or context menu, and the hotkey is not bound."),
            ("EnableTrafficLight",
                "While off, there is no timer, no shortcut, no tray item and no window, and CyrFlip makes no network checks."),
            ("EnableClipboardHistory",
                "Off by default: while off, nothing is kept. When on: text only, never images or files, and nothing is trimmed."),
        };

        // The audit's one red item here (CU-2-16, P2) landed in S0067: the history hint now says
        // that it is off and that nothing is trimmed, so no exception is carried - a module whose
        // first-screen text goes silent fails the test below the day it happens.

        // Bool config values that default to off and are NOT opt-in modules of the audit's 2d table -
        // each needs its reason or it belongs in the module list above.
        private static readonly Dictionary<string, string> NotModules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The I-beam marker is one of the three surfaces of the always-on layout indicator (CLAUDE.md
            // feature 1), not a module that appears when switched on; its own gate finding is CU-2-08.
            { "EnableCursorChange", "a surface of the on-by-default indicator, not an opt-in module" },
            // A behaviour of the conversion table (which characters a row converts), not a module.
            { "EnableLanguageSwitch", "a conversion-table behaviour, not an opt-in module" },
        };

        private static readonly Regex SaysOff = new Regex(
            @"off by default|while (it|this|the switch|disabled|off)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Every opt-in module's first-screen text says that it is off and what that costs (CU-2d):
        /// the hint matches "off by default" or a "while it/this/the switch/disabled/off ..."
        /// clause. 7 of 7 since S0067 worded the history hint (the audit's day-one count was 6).
        /// </summary>
        [Fact]
        public void EveryOptInModulesFirstScreenTextSaysWhatIsOff()
        {
            var problems = new List<string>();
            foreach ((string switchName, string hintKey) in OptInModules)
            {
                string en = Localization.Translate("English", hintKey);
                if (!Localization.All.Any(e => e.Key == hintKey))
                    problems.Add(switchName + ": the hint key is not registered - fix the key in ComprehensionGateTests.OptInModules");
                else if (!SaysOff.IsMatch(en))
                    problems.Add(switchName + ": \"" + en + "\" says neither that the module is off nor what is lost while it is (CU-2d)");
            }

            Assert.True(problems.Count == 0,
                "An opt-in module's first-screen text went silent about being off:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// The module list is hand-kept, so it needs the guard of the audit's own rule: it must equal
        /// the set of bool config values that default to off and look like a module switch (Enable* /
        /// *Enabled), minus the documented non-modules. A module added tomorrow fails this test by
        /// being absent - add its hint key or, if it is not a module, record why in NotModules.
        /// </summary>
        [Fact]
        public void TheModuleListMatchesEveryOffByDefaultModuleSwitch()
        {
            var expected = new SortedSet<string>(StringComparer.Ordinal);
            var config = new AppConfig();
            foreach (var property in typeof(AppConfig).GetProperties())
            {
                if (property.PropertyType != typeof(bool)) continue;
                string name = property.Name;
                bool isSwitchShape = name.StartsWith("Enable", StringComparison.Ordinal)
                    || name.EndsWith("Enabled", StringComparison.Ordinal);
                if (!isSwitchShape) continue;
                if (!(bool)property.GetValue(config)!)
                {
                    Assert.True(NotModules.ContainsKey(name) || OptInModules.Any(m => m.Switch == name),
                        name + " defaults to off and looks like a module switch - give its first-screen hint a row in " +
                        "ComprehensionGateTests.OptInModules, or record why it is not a module in NotModules");
                    if (!NotModules.ContainsKey(name)) expected.Add(name);
                }
            }

            var listed = new SortedSet<string>(OptInModules.Select(m => m.Switch));
            Assert.Equal(expected, listed);
            Assert.True(expected.Count >= 7, "the audit's seven opt-in modules (CU-2d) shrank to " + expected.Count);
        }

        // ------------------------------------------------------------- G-CU5: chord in tooltip

        // The audit's one gap here (CU-2-29, P3) landed in S0067: BuildTooltipLines carries the
        // screenshots-folder line, so no exception is carried - the test below fails the day a
        // popup chord loses its tooltip line.

        /// <summary>
        /// With every chord and module switched on, every Text / Conversions / Translation /
        /// Launcher row of the tray popup (TrayChordList.Build) has its line in the native tooltip
        /// (BuildTooltipLines) - a person who never opens the popup still learns the chord. The
        /// Mouse and Windows groups are the tooltip's by-design exceptions.
        /// </summary>
        [Fact]
        public void EveryChordRowOfTheTrayPopupHasItsTooltipLine()
        {
            AppConfig config = FullChordConfig();
            var scenarios = new List<LauncherScenario>
            {
                new LauncherScenario { Name = "Calc", Hotkey = "Ctrl+Alt+C", Path = "calc.exe" },
            };

            TrayChordList popup = TrayChordList.Build(config, scenarios, null, null, "Русский");
            List<string> tooltip = TrayChordList.BuildTooltipLines(config, scenarios, "Русский");

            // The guard fact: the config really is "every chord on" - the popup builds the audit's
            // seven text chords and the tooltip is not empty, or the gate below sees nothing.
            var textRows = popup.AllRows.Where(r => r.Group == TrayChordGroupKind.Text).ToList();
            Assert.True(textRows.Count >= 7,
                "only " + textRows.Count + " text rows were built - the full-chord config no longer switches everything on");
            Assert.NotEmpty(tooltip);

            var missing = new List<string>();
            foreach (TrayChordRow row in popup.AllRows)
            {
                if (row.Group != TrayChordGroupKind.Text
                    && row.Group != TrayChordGroupKind.Conversions
                    && row.Group != TrayChordGroupKind.Translation
                    && row.Group != TrayChordGroupKind.Launcher) continue;
                if (string.IsNullOrEmpty(row.ChordDisplay)) continue;
                if (tooltip.Any(l => l.StartsWith(row.ChordDisplay + " - ", StringComparison.Ordinal))) continue;
                missing.Add(row.ChordDisplay + " - " + row.ActionDisplay);
            }

            Assert.True(missing.Count == 0,
                "A chord of the tray popup has no line in the native tray tooltip - the tooltip is the one surface a " +
                "person reads without a click (S0061 CU-2g):" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }

        private static AppConfig FullChordConfig()
        {
            var config = new AppConfig();
            config.EnableHotkeys = true;
            config.EnableCaseHotkey = true;
            config.EnableHistoryHotkey = true;
            config.EnableQuickNotes = true;
            config.EnableQuickNotesHotkey = true;
            config.EnableSymbols = true;
            config.EnableSymbolsHotkey = true;
            config.EnableTrafficLight = true;
            config.EnableTrafficLightHotkey = true;
            config.EnableScreenshotHotkey = true;
            config.ScreenshotSaveEnabled = true;
            config.EnableScreenshotFolderHotkey = true;
            config.EnableContextMenu = true;
            config.EnableTextMenuHotkey = true;
            config.EnableScenarioLauncher = true;
            config.EnableTranslate = true;
            config.LayoutConversionProfiles = new List<LayoutConversionProfile>
            {
                new LayoutConversionProfile { Id = "c1", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true },
            };
            config.TranslateProfiles = new List<TranslationProfile>
            {
                new TranslationProfile { Id = "t1", TargetLang = "en", Hotkey = "Ctrl+Alt+T", Enabled = true },
            };
            return config;
        }

        // ------------------------------------------------------------------ G-CU7: termbase

        private static Dictionary<string, object> Termbase()
        {
            Assert.True(File.Exists(TermbasePath), "docs/termbase.json not found: " + TermbasePath);
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(TermbasePath));
        }

        private static string[] StringsOf(Dictionary<string, object> map, string key)
        {
            Assert.True(map.ContainsKey(key), "termbase has no \"" + key + "\" section");
            return ((System.Collections.IEnumerable)map[key]).Cast<object>().Select(o => (string)o!).ToArray();
        }

        /// <summary>The values a given language shows: its column, the key's own English text for English.</summary>
        private static HashSet<string> RegisteredValues(string code)
        {
            int index = Array.IndexOf(Localization.Codes, code);
            Assert.True(index >= 0, "termbase names a language the app does not have: " + code);
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                if (index < entry.Value.Length) values.Add(entry.Value[index]);
            }
            return values;
        }

        private static List<string> AllShownTexts(string code)
        {
            int index = Array.IndexOf(Localization.Codes, code);
            var texts = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                texts.Add(entry.Value[index]);
            }
            return texts;
        }

        /// <summary>
        /// Every control caption of the termbase is a registered string of the app, verbatim, in
        /// each language the termbase carries controls for (en, ru, uk - 25 of 25 today, CU-2c
        /// section 4). Zero tolerance: a caption that stops existing is a control renamed behind
        /// the site's back.
        /// </summary>
        [Fact]
        public void EveryTermbaseControlCaptionIsARegisteredValue()
        {
            Dictionary<string, object> termbase = Termbase();
            var controls = (Dictionary<string, object>)termbase["controls"];

            var problems = new List<string>();
            foreach (KeyValuePair<string, object> pair in controls)
            {
                HashSet<string> registered = RegisteredValues(pair.Key);
                foreach (string caption in StringsOf(controls, pair.Key))
                    if (!registered.Contains(caption))
                        problems.Add("[" + pair.Key + "] \"" + caption + "\"");
            }

            Assert.True(problems.Count == 0,
                "Termbase control captions that no longer exist in the app (docs/termbase.json vs the Localization table):"
                + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// No forbidden phrase of the termbase occurs in any string the app shows in that language
        /// (0 hits today, in all three languages that carry a forbidden list). Zero tolerance.
        /// </summary>
        [Fact]
        public void NoForbiddenTermbasePhraseOccurs()
        {
            Dictionary<string, object> termbase = Termbase();
            var forbidden = (Dictionary<string, object>)termbase["forbidden"];

            var problems = new List<string>();
            foreach (KeyValuePair<string, object> pair in forbidden)
            {
                foreach (string phrase in StringsOf(forbidden, pair.Key))
                    foreach (string text in AllShownTexts(pair.Key))
                        if (text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                            problems.Add("[" + pair.Key + "] \"" + phrase + "\" in \"" + text + "\"");
            }

            Assert.True(problems.Count == 0,
                "A forbidden termbase phrase reached a UI string:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        // The termbase concept term that the app's English never says (CU-2-13): "Case flip" is the
        // termbase's English name for the case fix, and the app uses five other words for it. Which
        // side wins is the owner's decision (S0067 section 2.3, D-CU3) - until it is taken, the
        // absence is carried here rather than judged.
        private static readonly HashSet<string> ConceptTermKnownMissing = new HashSet<string>(StringComparer.Ordinal)
        {
            "Case flip",
        };

        /// <summary>
        /// Every English concept term of the termbase occurs somewhere in the app's English (4 of 5
        /// today); the exception is documented above and waits on the owner's naming decision.
        /// </summary>
        [Fact]
        public void EveryTermbaseConceptTermOccursInEnglish()
        {
            Dictionary<string, object> termbase = Termbase();
            var locales = (Dictionary<string, object>)termbase["locales"];
            var english = AllShownTexts("en");

            var missing = new List<string>();
            foreach (string term in StringsOf(locales, "en"))
            {
                if (ConceptTermKnownMissing.Contains(term)) continue;
                if (!english.Any(t => t.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                    missing.Add(term);
            }

            Assert.True(missing.Count == 0,
                "Termbase concept terms the app's English never uses - either the UI says them or the termbase concept is "
                + "renamed (docs/termbase.json):" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }

        // ------------------------------------------------------------------ shared

        private static string EnglishOf(KeyValuePair<string, string[]> entry)
            => entry.Value.Length > 1 ? entry.Value[Array.IndexOf(Localization.Codes, "en")] : string.Empty;
    }
}
