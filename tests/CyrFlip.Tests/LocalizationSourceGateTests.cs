using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Guards on the localization table that need no window (ticket S0029 RB-4, RB-10). The window walks
    /// only see what a window happens to build in a test; S0023 shipped 42 unregistered captions and only
    /// one of them tripped a walk. This scan reads the source instead.
    /// </summary>
    public class LocalizationSourceGateTests
    {
        private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private static readonly Regex CyrillicChar = new Regex("[Ѐ-ӿ]");

        // A translating call whose first argument - or second, after a language argument, as in
        // Localization.Translate(language, "...") - is a whole string literal (a literal followed by
        // "+" is only part of the key, and an interpolated one is not a key at all; both are skipped).
        private static readonly Regex TranslatingCall = new Regex(
            @"\b(?:T|Translate|translate|Localize|TranslateButton)\(\s*(?:[A-Za-z_][\w.]*\s*,\s*)?""((?:[^""\\\r\n]|\\.)*)""(?=\s*[,)])",
            RegexOptions.Compiled);

        /// <summary>
        /// Literals that go through a translating call on purpose but are not table keys, each with its
        /// reason. Keep this short: every entry is a string a non-Russian user may see in Russian.
        /// </summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        [Fact]
        public void EveryCyrillicLiteralPassedToATranslatingCallIsARegisteredKey()
        {
            Assert.True(Directory.Exists(SourceRoot), "source folder not found: " + SourceRoot);
            var known = new HashSet<string>(Localization.All.Select(e => e.Key), StringComparer.Ordinal);

            var missing = new List<string>();
            int scanned = 0;
            foreach (string file in Directory.GetFiles(SourceRoot, "*.cs"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("Localization", StringComparison.Ordinal)) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    foreach (Match m in TranslatingCall.Matches(lines[i]))
                    {
                        string literal = Unescape(m.Groups[1].Value);
                        if (!CyrillicChar.IsMatch(literal)) continue;
                        scanned++;
                        if (!known.Contains(literal) && !Allowed.Contains(literal))
                            missing.Add($"{name}:{i + 1}: \"{literal}\"");
                    }
                }
            }

            Assert.True(missing.Count == 0,
                "Passed to a translating call but not registered in Localization - every non-Russian user sees it in Russian:\n"
                + string.Join("\n", missing));
            // The scan has to be looking at something: hundreds of such calls exist.
            Assert.True(scanned > 200, "only " + scanned + " translating calls were found - the scan is looking at the wrong thing");
        }

        /// <summary>The scan can fail: a literal nobody registered is reported by name.</summary>
        [Fact]
        public void TheScanRecognisesTheCallShapesItClaimsTo()
        {
            string[] shapes =
            {
                "x = T(\"Привет\");",
                "x = Localization.Translate(_language, \"Привет\");",
                "x = translate(\"Привет\\nмир\");",
                "x = Localize(\"Привет\");",
            };
            foreach (string shape in shapes)
            {
                Match m = TranslatingCall.Match(shape);
                Assert.True(m.Success, shape);
                Assert.StartsWith("Привет", Unescape(m.Groups[1].Value));
            }
            Assert.Equal("Привет\nмир", Unescape(TranslatingCall.Match(shapes[2]).Groups[1].Value));
        }

        [Fact]
        public void NoKeyIsRegisteredTwice()
        {
            // Forces the static constructor, which is where the table is filled.
            Assert.NotEmpty(Localization.All);
            Assert.True(Localization.DuplicateKeys.Count == 0,
                "Registered more than once (the last registration silently wins):\n" + string.Join("\n", Localization.DuplicateKeys));
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
                        problems.Add($"[{Localization.Names[i + 1]}] \"{entry.Key}\": expected {{{expected}}}, found {{{actual}}}");
                }
            }
            Assert.True(problems.Count == 0, "Placeholder mismatch:\n" + string.Join("\n", problems));
        }

        private static string Placeholders(string s)
            => string.Join(",", Regex.Matches(s, @"\{(\d+)(?:[,:][^}]*)?\}").Cast<Match>()
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n));

        private static string Unescape(string literal)
        {
            var sb = new StringBuilder(literal.Length);
            for (int i = 0; i < literal.Length; i++)
            {
                char c = literal[i];
                if (c != '\\' || i + 1 >= literal.Length) { sb.Append(c); continue; }
                char e = literal[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case 'u' when i + 4 < literal.Length:
                        sb.Append((char)Convert.ToInt32(literal.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
