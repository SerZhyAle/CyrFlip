using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Two source gates of the theme (ticket S0020 section 8, test 4). WinForms has no resource scope to
    /// hold <c>APP-STYLE</c> rule 3's "only dynamic references", so the rule is held here instead: a colour
    /// literal outside the palette table is a colour the theme cannot reach, and a system message box is
    /// a white flash in the dark theme with no Escape.
    /// </summary>
    public class ThemeSourceGateTests
    {
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        /// <summary>
        /// Where a colour literal may live: the palette table, and the layout marker - deliberately out of
        /// the theme (<c>LAYOUT-PALETTE</c>, <c>APP-STYLE</c> rule 5), declared at <c>LayoutStyle.cs</c>
        /// and <c>CaretOverlay.cs</c>.
        /// </summary>
        private static readonly HashSet<string> ColourFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ThemePalette.cs", "LayoutStyle.cs", "LayoutCursor.cs", "CaretOverlay.cs", "CursorIndicator.cs",
        };

        private static readonly Regex ColourLiteral = new Regex(
            @"SystemColors\.\w+|Color\.FromArgb\s*\(|Color\.(?!Empty\b|Transparent\b|FromArgb\b|FromName\b|FromKnownColor\b)[A-Z]\w*",
            RegexOptions.Compiled);

        private static readonly Regex Comment = new Regex(@"^\s*///?", RegexOptions.Compiled);

        [Fact]
        public void NoColourLiteralLivesOutsideThePalette()
        {
            var hits = new List<string>();
            foreach (string file in Directory.GetFiles(Source, "*.cs"))
            {
                string name = Path.GetFileName(file);
                if (ColourFiles.Contains(name)) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (!Comment.IsMatch(lines[i]) && ColourLiteral.IsMatch(lines[i]))
                        hits.Add($"{name}:{i + 1}: {lines[i].Trim()}");
            }
            Assert.True(hits.Count == 0,
                "A colour the theme cannot reach - use a ThemePalette role:\n" + string.Join("\n", hits));
        }

        /// <summary>
        /// Every message and confirmation goes through <c>ConfirmDialog</c>. The one exception is the
        /// fatal-error box in <c>Program</c>: the process is going down, and the system dialog is the one
        /// least likely to go down with it.
        /// </summary>
        [Fact]
        public void NoSystemMessageBoxIsLeft()
        {
            var hits = new List<string>();
            foreach (string file in Directory.GetFiles(Source, "*.cs"))
            {
                string name = Path.GetFileName(file);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (!Comment.IsMatch(lines[i]) && lines[i].Contains("MessageBox.Show("))
                        hits.Add($"{name}:{i + 1}");
            }
            string only = Assert.Single(hits);
            Assert.StartsWith("Program.cs:", only);
        }

        /// <summary>The gate has to see the source it guards, or it passes by reading nothing.</summary>
        [Fact]
        public void TheGatesReadTheRealSource()
        {
            Assert.True(File.Exists(Path.Combine(Source, "ThemePalette.cs")), "source not found at " + Source);
            Assert.True(Directory.GetFiles(Source, "*.cs").Length > 50);
            Assert.Matches(ColourLiteral, "ForeColor = SystemColors.GrayText");
            Assert.Matches(ColourLiteral, "new Pen(Color.Firebrick)");
            Assert.DoesNotMatch(ColourLiteral, "BackColor = Color.Empty");
        }
    }
}
