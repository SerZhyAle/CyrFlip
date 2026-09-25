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
    /// The page a user reads in the thirty seconds after Windows or an antivirus stopped CyrFlip
    /// (<c>INSTALL-TRUST</c>). Its content is prose in thirteen languages and is reviewed by hand; what a
    /// test can hold is its shape - the four sections in order, the dialog quoted, no instruction to weaken a
    /// protection - and that every place a download starts links to it.
    /// </summary>
    public class TrustPageTests
    {
        private static readonly string Repo = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string Docs(params string[] parts) => Path.Combine(new[] { Repo, "docs" }.Concat(parts).ToArray());

        /// <summary>English lives at the site root; every other interface language has its own folder.</summary>
        private static string TrustPage(string code) => code == "en" ? Docs("trust.html") : Docs(code, "trust.html");

        public static IEnumerable<object[]> Locales() => Localization.Codes.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(Locales))]
        public void EveryLocaleHasATrustPage(string code)
            => Assert.True(File.Exists(TrustPage(code)), "missing " + TrustPage(code));

        /// <summary>Rule 1: what, why, what to click, what it never does - in that order, and nothing else
        /// at that level, so a reader skimming the headings meets them as the contract orders them.</summary>
        [Theory]
        [MemberData(nameof(Locales))]
        public void TrustPageHasTheFourSectionsInOrder(string code)
        {
            string html = File.ReadAllText(TrustPage(code));
            string[] ids = Regex.Matches(html, @"<h2\b[^>]*>", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => Regex.Match(m.Value, "id=\"([^\"]*)\"").Groups[1].Value)
                .ToArray();
            Assert.Equal(new[] { "what", "why", "click", "never" }, ids);
        }

        /// <summary>Rule 2: the heading is the dialog's own text, so the user can match what is on screen.</summary>
        [Fact]
        public void EnglishTrustPageQuotesTheDialog()
        {
            string html = File.ReadAllText(TrustPage("en"));
            Assert.Contains("Windows protected your PC", html);
            Assert.Contains("More info", html);
            Assert.Contains("Run anyway", html);
        }

        private static readonly string[] Forbidden =
        {
            "turn off smartscreen", "disable smartscreen", "turn smartscreen off",
            "disable your antivirus", "turn off your antivirus", "disable the antivirus", "turn off the antivirus",
        };

        private static string? FirstForbidden(string text)
        {
            string flat = Regex.Replace(text, @"\s+", " ").ToLowerInvariant();
            return Forbidden.FirstOrDefault(flat.Contains);
        }

        /// <summary>Rule 4: only per-file, per-folder, reversible steps - never "switch the protection off".</summary>
        [Fact]
        public void EnglishTrustPageForbidsNothingItShouldNot()
            => Assert.Null(FirstForbidden(File.ReadAllText(TrustPage("en"))));

        /// <summary>A matcher that inspects nothing passes as quietly as a clean page does.</summary>
        [Fact]
        public void TheForbiddenMatcherWouldFire()
            => Assert.Equal("disable smartscreen", FirstForbidden("To fix it, <b>Disable\n SmartScreen</b> first."));

        /// <summary>Rule 7: reachable from where the download is.</summary>
        [Fact]
        public void EveryDownloadSurfaceLinksTheTrustPage()
        {
            var surfaces = new List<string> { Docs("index.html"), Docs("guide.html"), Docs("ru", "guide.html"), Docs("uk", "guide.html") };
            surfaces.AddRange(new[] { "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh" }.Select(c => Docs(c, "index.html")));
            surfaces.AddRange(Localization.Codes.Select(c => c == "en" ? Docs("privacy.html") : Docs(c, "privacy.html")));
            surfaces.AddRange(new[] { "README.md", "README_RU.md", "README_UK.md" }.Select(r => Path.Combine(Repo, r)));

            foreach (string file in surfaces)
            {
                Assert.True(File.Exists(file), "missing " + file);
                Assert.True(File.ReadAllText(file).Contains("trust.html"), file + " does not link the trust page");
            }
        }
    }
}
