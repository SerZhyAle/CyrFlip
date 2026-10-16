using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// APP-BEHAVIOUR rule 1, ticket S0060 P7-b: a dialog the context raises is modal, owned and centred
    /// on its owner. The policy is pure; the gate scans the context for a dialog raised with no owner and
    /// names, one by one, the places where nothing exists to own it.
    /// </summary>
    public class DialogOwnerTests
    {
        // ---- the policy ----

        [Theory]
        [InlineData(false, false, false, "None")]       // the tray is the only surface
        [InlineData(false, true, false, "Settings")]    // settings open: it owns the question
        [InlineData(false, true, true, "None")]         // minimized: nowhere to centre on
        [InlineData(true, false, false, "Welcome")]     // the tour is modal and wins
        [InlineData(true, true, false, "Welcome")]
        [InlineData(true, true, true, "Welcome")]
        public void TheOwnerIsTheTourThenSettingsThenNothing(bool tour, bool settings, bool minimized, string expected)
            => Assert.Equal(expected, DialogOwnerPolicy.Choose(tour, settings, minimized).ToString());

        // ---- the gate ----

        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        /// <summary>
        /// The only places the context raises a dialog with no owner, each with its reason. Anything
        /// else is a dialog that opens in the middle of the screen behind the window it was asked from.
        /// </summary>
        private static readonly Dictionary<string, string> DeclaredOwnerless = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LaunchSelection"] = "the text menu opens over another application's selection; no window of ours is involved",
            ["OfferToStopStartedOllama"] = "asked on the way out of the tray's Exit; the settings window is going away with the process",
            ["ShowWelcome"] = "the tour is its own top-level window with a taskbar button, placed by itself (APP-BEHAVIOUR 1, welcome)",
        };

        private static readonly Regex MethodHead = new Regex(
            @"^[ \t]*(?:private|internal|public|protected)[^=;(){}]*?\b(?<name>\w+)\s*\(", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Every call that raises a dialog with no owner: (file, method, line, text).</summary>
        internal static List<(string File, string Method, int Line, string Text)> OwnerlessCalls(string fileName, string source)
        {
            var found = new List<(string, string, int, string)>();
            string[] lines = source.Split('\n');
            int offset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string code = line.TrimStart();
                int here = offset;
                offset += line.Length + 1;
                if (code.StartsWith("//", StringComparison.Ordinal)) continue;

                bool ownerless = false;
                // ConfirmDialog.Show: the first argument is the owner (or the language, in the ownerless overload).
                foreach (Match m in Regex.Matches(code, @"ConfirmDialog\.Show\(\s*([^,]*)"))
                {
                    string first = m.Groups[1].Value.Trim();
                    if (first.Length == 0) continue; // the argument is on the next line: judged below
                    if (first != "DialogOwner()" && first != "owner" && first != "_settings") ownerless = true;
                }
                if (Regex.IsMatch(code, @"ConfirmDialog\.Show\(\s*$"))
                {
                    string next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
                    if (!(next.StartsWith("DialogOwner()", StringComparison.Ordinal) || next.StartsWith("owner,", StringComparison.Ordinal)
                          || next.StartsWith("_settings,", StringComparison.Ordinal))) ownerless = true;
                }
                // A modal dialog shown with no argument goes to whichever window happens to be active.
                if (Regex.IsMatch(code, @"\.ShowDialog\(\s*\)")) ownerless = true;
                // The chord guard's two entry points take the owner first; null is "none".
                if (Regex.IsMatch(code, @"ChordGuard\.(IsFree|WarnDuplicates)\(\s*null\b")) ownerless = true;

                if (!ownerless) continue;
                Match head = MethodHead.Matches(source.Substring(0, here)).Cast<Match>().LastOrDefault(m => m.Success);
                string method = head?.Groups["name"].Value ?? "?";
                found.Add((fileName, method, i + 1, code.Trim()));
            }
            return found;
        }

        [Fact]
        public void EveryDialogTheContextRaisesIsOwnedOrDeclaredOwnerless()
        {
            var problems = new List<string>();
            var seenDeclared = new HashSet<string>();
            foreach (string path in Directory.GetFiles(Source, "CyrFlipContext*.cs"))
            {
                string name = Path.GetFileName(path);
                foreach (var call in OwnerlessCalls(name, File.ReadAllText(path)))
                {
                    if (DeclaredOwnerless.ContainsKey(call.Method)) { seenDeclared.Add(call.Method); continue; }
                    problems.Add($"{call.File}:{call.Line} in {call.Method}: {call.Text}");
                }
            }
            Assert.True(problems.Count == 0,
                "A dialog is raised with no owner (APP-BEHAVIOUR rule 1) - pass DialogOwner(), or declare the site in "
                + "DeclaredOwnerless with its reason:\n" + string.Join("\n", problems));

            // A declaration that no longer matches a call is stale and would hide the next one.
            foreach (string declared in DeclaredOwnerless.Keys)
                Assert.True(seenDeclared.Contains(declared), "DeclaredOwnerless names " + declared + " but no ownerless call is there any more");
        }

        [Fact]
        public void TheGateSeesEveryShapeItClaimsTo()
        {
            const string source = @"
        private void A()
        {
            ConfirmDialog.Show(_config.UiLanguage, ""x"");
            ConfirmDialog.Show(DialogOwner(), _config.UiLanguage, ""y"");
            using var dlg = new HotkeyDialog(""a"");
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (dlg.ShowDialog(DialogOwner()) != DialogResult.OK) return;
            ChordGuard.IsFree(null, Chords(), c, k, null, l, f);
            ChordGuard.WarnDuplicates(DialogOwner(), Chords(), l, f);
        }
        private void B()
        {
            ConfirmDialog.Show(
                _config.UiLanguage,
                ""z"");
            ConfirmDialog.Show(
                DialogOwner(), _config.UiLanguage, ""w"");
            // ConfirmDialog.Show(_config.UiLanguage, ""a comment"");
        }";
            var calls = OwnerlessCalls("Fake.cs", source);
            Assert.Equal(new[] { "A", "A", "A", "B" }, calls.Select(c => c.Method).ToArray());
            Assert.Equal(4, calls.Count);
        }

        [Fact]
        public void TheOwnerHelperIsTheOneEveryDialogUses()
        {
            // The inventory: the helper exists and is what the dialogs use (a rename would leave the gate green).
            string context = File.ReadAllText(Path.Combine(Source, "CyrFlipContext.cs"));
            Assert.Contains("private IWin32Window? DialogOwner()", context);
            Assert.True(Regex.Matches(context, @"DialogOwner\(\)").Count >= 15,
                "fewer owned dialogs than the audit counted - did a call site go back to no owner?");
        }
    }
}
