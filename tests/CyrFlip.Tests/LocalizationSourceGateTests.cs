using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CyrFlip;
using L10nKeys;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Guards on the localization table that need no window (tickets S0029 RB-4/RB-10, S0070). The window
    /// walks only see what a window happens to build in a test; S0023 shipped 42 unregistered captions and
    /// only one of them tripped a walk. These scans read the source instead, with the same lexer the
    /// migration tool uses (<c>tools/l10n/CsLexer.cs</c>), so a string literal is found wherever it stands:
    /// in a call, a field, an array or an argument of a helper.
    ///
    /// The key of the table is the English source text (S0070 D1). Cyrillic is data - the Russian and
    /// Ukrainian columns, the generated symbol names, a handful of declared samples - and never a key.
    /// </summary>
    public class LocalizationSourceGateTests
    {
        private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

        private static readonly string SourceRoot = Path.Combine(RepositoryRoot, "src", "CyrFlip");
        private static readonly string TestsRoot = Path.Combine(RepositoryRoot, "tests", "CyrFlip.Tests");

        /// <summary>
        /// A <c>Name(</c> call whose argument list starts with a whole string literal - or, after a language
        /// argument, as in <c>Localization.Translate(language, "...")</c>, has one second. These are the
        /// seams a literal is translated through (a literal followed by "+" is only part of a key and an
        /// interpolated one is not a key at all; both are skipped).
        /// </summary>
        private static readonly HashSet<string> TranslatingNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "T", "Translate", "translate", "Localize", "TranslateButton", "Format",
        };

        /// <summary>
        /// Cyrillic string literals outside the translation columns that are data on purpose (S0070 D7):
        /// each is a file and the exact value, so a new one has to be declared here and a stale entry
        /// fails the test. Nothing here is ever shown through <c>Translate</c>.
        /// </summary>
        private static readonly (string File, string Value, string Reason)[] DeclaredData =
        {
            ("Localization.cs", "Русский", "the endonym of the key language - persisted as UiLanguage, never a key"),
            ("Localization.cs", "Українська", "the endonym of Ukrainian - persisted as UiLanguage, never a key"),
            ("TransliterationEngine.cs", "йцукенгшщзхъфывапролджэячсмитьбю.ё", "the ЙЦУКЕН key row - the transliteration table"),
            ("WelcomeDialog.cs", "привет", "the example word of the conversion strip - the same in every language"),
            ("SettingsForm.cs", "Аg", "a width probe: a Cyrillic capital and a Latin descender"),
        };

        /// <summary>Files that are Cyrillic by construction: generated data, not source text.</summary>
        private static readonly string[] DeclaredDataFiles = { "Symbols.g.cs" };

        // ---------------------------------------------------------------- A3

        /// <summary>
        /// S0070 A3. A Cyrillic string literal in the application is allowed in exactly three places: the
        /// <c>ru:</c> and <c>uk:</c> columns of the localization tables, a generated data file, and a
        /// declared sample. Anywhere else it is a Russian key or a Russian caption that bypassed the table.
        /// </summary>
        [Fact]
        public void NoCyrillicStringLiteralLivesOutsideTheTranslationData()
        {
            Assert.True(Directory.Exists(SourceRoot), "source folder not found: " + SourceRoot);
            var offenders = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            int scanned = 0;

            foreach (string file in SourceFiles(SourceRoot))
            {
                string name = Path.GetFileName(file);
                if (DeclaredDataFiles.Contains(name)) continue;
                bool isTable = name.StartsWith("Localization", StringComparison.Ordinal);
                List<Tok> toks = Lex(file);
                for (int i = 0; i < toks.Count; i++)
                {
                    Tok t = toks[i];
                    if (t.Kind != TokKind.Str || t.Interpolated || t.Raw || !CsLexer.HasCyrillic(t.Value)) continue;
                    scanned++;
                    if (isTable && i >= 2 && toks[i - 1].IsPunct(':')
                        && (toks[i - 2].IsIdent("ru") || toks[i - 2].IsIdent("uk"))) continue;
                    int declared = Array.FindIndex(DeclaredData, d => d.File == name && d.Value == t.Value);
                    if (declared >= 0) { used.Add(DeclaredData[declared].File + "|" + DeclaredData[declared].Value); continue; }
                    offenders.Add($"{name}:{t.Line}: {Shorten(t.Value)}");
                }
            }

            Assert.True(offenders.Count == 0,
                "A Cyrillic string literal outside the translation data (write the English text and register it in Localization,"
                + " or declare it in DeclaredData with its reason):\n" + string.Join("\n", offenders));
            foreach (var d in DeclaredData)
                Assert.True(used.Contains(d.File + "|" + d.Value), "declared data no longer in the source, remove it: " + d.File + " " + d.Value);
            Assert.True(scanned > 1500, "only " + scanned + " Cyrillic literals were found - the scan is looking at the wrong thing");
        }

        /// <summary>
        /// A test never hands a Cyrillic literal to a translating call as a key: the keys are English. (A test
        /// may - and many do - assert the Russian text the Russian UI shows; that is a translation, not a key.)
        /// </summary>
        [Fact]
        public void NoTestPassesACyrillicKeyToATranslatingCall()
        {
            Assert.True(Directory.Exists(TestsRoot), "tests folder not found: " + TestsRoot);
            var offenders = new List<string>();
            foreach (string file in SourceFiles(TestsRoot))
            {
                foreach (Tok literal in TranslatedLiterals(Lex(file)))
                    if (CsLexer.HasCyrillic(literal.Value))
                        offenders.Add($"{Path.GetFileName(file)}:{literal.Line}: {Shorten(literal.Value)}");
            }
            Assert.True(offenders.Count == 0, "A Cyrillic literal as the key of a translating call:\n" + string.Join("\n", offenders));
        }

        // ---------------------------------------------------------------- the reach test

        /// <summary>
        /// A literal that is the key argument of a translating call is a registered key (or one of the few
        /// that go through a seam on purpose). A typo in an English key is an untranslated string in twelve
        /// languages and no window walk fails for it unless that window happens to be built.
        /// </summary>
        [Fact]
        public void EveryLiteralPassedToATranslatingCallIsARegisteredKey()
        {
            Assert.True(Directory.Exists(SourceRoot), "source folder not found: " + SourceRoot);
            var known = new HashSet<string>(Localization.All.Select(e => e.Key), StringComparer.Ordinal);

            var missing = new List<string>();
            int scanned = 0;
            foreach (string file in SourceFiles(SourceRoot))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("Localization", StringComparison.Ordinal) || DeclaredDataFiles.Contains(name)) continue;
                foreach (Tok literal in TranslatedLiterals(Lex(file)))
                {
                    if (literal.Value.Length == 0) continue;
                    scanned++;
                    if (!known.Contains(literal.Value))
                        missing.Add($"{name}:{literal.Line}: {Shorten(literal.Value)}");
                }
            }

            Assert.True(missing.Count == 0,
                "Passed to a translating call but not registered in Localization - every non-English user sees it untranslated:\n"
                + string.Join("\n", missing));
            // The scan has to be looking at something: hundreds of such calls exist.
            Assert.True(scanned > 400, "only " + scanned + " translating calls were found - the scan is looking at the wrong thing");
        }

        /// <summary>The scan can fail: the call shapes it claims to read are read.</summary>
        [Fact]
        public void TheScanRecognisesTheCallShapesItClaimsTo()
        {
            string[] shapes =
            {
                "x = T(\"Hello\");",
                "x = Localization.Translate(_language, \"Hello\");",
                "x = Localization.Translate(\"Deutsch\", \"Hello\");",
                "x = translate(\"Hello\\nworld\");",
                "x = Localize(\"Hello\");",
                "x = Localization.Format(T, \"Hello {0}\", 1);",
                "x = Localization.Format(_config.UiLanguage, \"Hello {0}\", 1);",
            };
            foreach (string shape in shapes)
            {
                var literals = TranslatedLiterals(CsLexer.Lex(shape, new List<string>(), "shape")).ToList();
                Assert.True(literals.Count == 1, shape);
                Assert.StartsWith("Hello", literals[0].Value);
            }
            Assert.Equal("Hello\nworld", TranslatedLiterals(CsLexer.Lex(shapes[3], new List<string>(), "shape")).Single().Value);

            // not a translating call: a plain composite format, a literal that is only part of a key
            foreach (string notACall in new[] { "x = string.Format(\"{0} Hello\", 1);", "x = T(\"Hello \" + name);", "x = Other(\"Hello\");" })
                Assert.Empty(TranslatedLiterals(CsLexer.Lex(notACall, new List<string>(), "shape")));
        }

        // ---------------------------------------------------------------- the table

        [Fact]
        public void NoKeyIsRegisteredTwice()
        {
            // Forces the static constructor, which is where the table is filled.
            Assert.NotEmpty(Localization.All);
            Assert.True(Localization.DuplicateKeys.Count == 0,
                "Registered more than once (the last registration silently wins):\n" + string.Join("\n", Localization.DuplicateKeys));
        }

        /// <summary>
        /// S0070 A4. Every key is non-empty English text without Cyrillic; every row has a Russian and a
        /// Ukrainian translation (a missing one used to be invisible in the Russian UI, which showed the key);
        /// and the one row shape that carries an English display text apart from its key - a context in
        /// parentheses, for two keys of one English wording - is exactly the declared set.
        /// </summary>
        [Fact]
        public void EveryKeyIsEnglishAndEveryRowHasItsRussianAndUkrainianText()
        {
            var problems = new List<string>();
            var withDisplay = new List<string>();
            int english = Array.IndexOf(Localization.Codes, "en");
            int russian = Array.IndexOf(Localization.Codes, "ru");
            int ukrainian = Array.IndexOf(Localization.Codes, "uk");
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                string key = entry.Key;
                if (entry.Value.Length != Localization.Names.Length)
                    problems.Add($"\"{Shorten(key)}\": {entry.Value.Length} translations for {Localization.Names.Length} languages");
                if (key.Trim().Length == 0) problems.Add("an empty key");
                if (CsLexer.HasCyrillic(key)) problems.Add($"\"{Shorten(key)}\": the key holds Cyrillic");
                if (entry.Value[russian].Length == 0) problems.Add($"\"{Shorten(key)}\": no Russian text");
                if (entry.Value[ukrainian].Length == 0) problems.Add($"\"{Shorten(key)}\": no Ukrainian text");
                if (entry.Value[english].Length == 0) problems.Add($"\"{Shorten(key)}\": no English text");
                if (entry.Value[english] != key) withDisplay.Add(key);
            }
            Assert.True(problems.Count == 0, "Table rows out of shape:\n" + string.Join("\n", problems));

            // A display override exists only for a key that spells its context out in parentheses and
            // whose display text is that key without them.
            foreach (string key in withDisplay)
            {
                string display = Localization.All.First(e => e.Key == key).Value[english];
                Assert.True(Regex.IsMatch(key, @"^.+ \([^()]+\)$"), "an English display text on a key without a context: " + key);
                Assert.Equal(key.Substring(0, key.LastIndexOf(" (", StringComparison.Ordinal)), display);
            }
            Assert.Equal(8, withDisplay.Count);   // Context-qualified display names, including the microscopic tile size.
        }

        /// <summary>
        /// Every translation carries exactly the placeholders of its key: a lost <c>{0}</c> drops a value
        /// from the sentence, an extra <c>{1}</c> throws a FormatException at the moment it is shown.
        /// </summary>
        [Fact]
        public void EveryTranslationKeepsThePlaceholdersOfItsKey()
        {
            var problems = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                string expected = Placeholders(entry.Key);
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    if (entry.Value[i].Length == 0) continue;   // a missing translation has its own test
                    string actual = Placeholders(entry.Value[i]);
                    if (actual != expected)
                        problems.Add($"[{Localization.Names[i]}] \"{entry.Key}\": expected {{{expected}}}, found {{{actual}}}");
                }
            }
            Assert.True(problems.Count == 0, "Placeholder mismatch:\n" + string.Join("\n", problems));
        }

        // ---------------------------------------------------------------- helpers

        private static string Placeholders(string s)
            => string.Join(",", Regex.Matches(s, @"\{(\d+)(?:[,:][^}]*)?\}").Cast<Match>()
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n));

        private static IEnumerable<string> SourceFiles(string root)
            => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .OrderBy(p => p, StringComparer.Ordinal);

        private static List<Tok> Lex(string file)
        {
            var problems = new List<string>();
            List<Tok> toks = CsLexer.Lex(File.ReadAllText(file, Encoding.UTF8), problems, Path.GetFileName(file));
            Assert.True(problems.Count == 0, "the lexer could not read " + file + ": " + string.Join("; ", problems));
            return toks;
        }

        /// <summary>
        /// The string literals that are the key argument of a translating call: <c>Name("...")</c> or
        /// <c>Name(language, "...")</c> where <c>Name</c> is one of <see cref="TranslatingNames"/> and the
        /// literal is followed by a comma or the closing parenthesis.
        /// </summary>
        private static IEnumerable<Tok> TranslatedLiterals(List<Tok> t)
        {
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Kind != TokKind.Str || t[i].Interpolated || t[i].Raw) continue;
                if (i + 1 >= t.Count || !(t[i + 1].IsPunct(',') || t[i + 1].IsPunct(')'))) continue;
                // the language of a call that takes one first (Translate("Deutsch", ...)) is not a key
                if (t[i + 1].IsPunct(',') && (Array.IndexOf(Localization.Names, t[i].Value) >= 0 || Array.IndexOf(Localization.Codes, t[i].Value) >= 0)) continue;
                int p = i - 1;
                if (p >= 0 && t[p].IsPunct('('))
                {
                    // directly after the opening parenthesis
                }
                else if (p >= 2 && t[p].IsPunct(',') && (t[p - 1].Kind == TokKind.Ident || t[p - 1].Kind == TokKind.Str))
                {
                    // a language argument first: an identifier chain (a.b.c) or a language literal
                    int q = p - 1;
                    while (q >= 2 && t[q - 1].IsPunct('.') && t[q - 2].Kind == TokKind.Ident) q -= 2;
                    p = q - 1;
                    if (p < 0 || !t[p].IsPunct('(')) continue;
                }
                else continue;
                if (p < 1 || t[p - 1].Kind != TokKind.Ident || !TranslatingNames.Contains(t[p - 1].Value)) continue;
                // string.Format("{0}", x) is a composite format, not a translation
                if (t[p - 1].Value == "Format" && p >= 3 && t[p - 2].IsPunct('.')
                    && (t[p - 3].IsIdent("string") || t[p - 3].IsIdent("String"))) continue;
                // a literal that continues into a concatenation is only part of a key
                yield return t[i];
            }
        }

        private static string Shorten(string value)
        {
            string flat = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return "\"" + (flat.Length > 70 ? flat.Substring(0, 70) + "..." : flat) + "\"";
        }
    }
}
