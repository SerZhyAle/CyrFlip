using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class DocumentationQualityTests
    {
        private static readonly string Repo = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
        private static readonly string Docs = Path.Combine(Repo, "docs");
        private static string Relative(string path) => path.Substring(Repo.Length + 1).Replace('\\', '/');
        private static readonly string[] Locales = { "en", "ru", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh" };

        private static IEnumerable<string> Corpus()
        {
            foreach (string file in Directory.GetFiles(Repo, "*.md", SearchOption.TopDirectoryOnly)) yield return file;
            foreach (string subdir in new[] { "docs/contracts", "msix", "winget", "tools/uitest", "vscode-extension", ".github" })
                foreach (string file in Directory.GetFiles(Path.Combine(Repo, subdir), "*.md", SearchOption.TopDirectoryOnly)) yield return file;
            foreach (string file in Directory.GetFiles(Path.Combine(Repo, ".claude/skills"), "*.md", SearchOption.AllDirectories)) yield return file;
            foreach (string file in Directory.GetFiles(Docs, "*.html", SearchOption.AllDirectories)) yield return file;
            foreach (string file in Directory.GetFiles(Path.Combine(Repo, "store"), "listing-*.txt")) yield return file;
            yield return Path.Combine(Docs, "sitemap.xml");
            yield return Path.Combine(Docs, "search-index.json");
            yield return Path.Combine(Docs, "termbase.json");
        }

        [Fact]
        public void DocumentRegistryCoversBothDirections()
        {
            string registry = Path.Combine(Docs, "DOCUMENT_REGISTRY.jsonl");
            var parser = new JavaScriptSerializer();
            var rows = File.ReadAllLines(registry).Select(line => parser.Deserialize<Dictionary<string, object>>(line)).ToArray();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string name = (string)row["file"];
                Assert.True(names.Add(name), "duplicate registry entry: " + name);
                Assert.True(File.Exists(Path.Combine(Repo, name.Replace('/', Path.DirectorySeparatorChar))), "missing registry file: " + name);
                Assert.False(string.IsNullOrWhiteSpace((string)row["topic"]), name + " lacks topic");
                Assert.False(string.IsNullOrWhiteSpace((string)row["area"]), name + " lacks area");
                Assert.NotEmpty((System.Collections.ICollection)row["triggers"]);
                Assert.Contains((string)row["role"], new[] { "source", "render" });
            }
            Assert.Equal(Corpus().Select(Relative).OrderBy(x => x), names.OrderBy(x => x));
        }

        [Fact]
        public void LocalLinksAnchorsAndAssetsResolve()
        {
            var errors = new List<string>();
            foreach (string file in Corpus().Where(x => x.EndsWith(".md") || x.EndsWith(".html")))
            {
                string content = File.ReadAllText(file);
                IEnumerable<string> links = file.EndsWith(".md")
                    ? Regex.Matches(content, @"(?<!!)\[[^\]]*\]\(([^)]+)\)|!\[[^\]]*\]\(([^)]+)\)").Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    : Regex.Matches(content, @"\b(?:href|src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value);
                foreach (string raw in links)
                {
                    string link = WebUtility.HtmlDecode(raw.Trim().Trim('<', '>').Split(' ')[0]);
                    if (link.StartsWith("mailto:") || link.StartsWith("data:") || link.StartsWith("javascript:")) continue;
                    if (Uri.TryCreate(link, UriKind.Absolute, out Uri? absolute))
                    {
                        if (absolute.Scheme == "http") errors.Add(Relative(file) + ": insecure " + link);
                        continue;
                    }
                    string[] pieces = link.Split(new[] { '#' }, 2);
                    string target = pieces[0].Split('?')[0];
                    if (target.StartsWith("/")) continue; // site-root routes and project-relative links are separately covered by the sitemap gate
                    string resolved = target.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(target)));
                    if (Directory.Exists(resolved))
                    {
                        if (file.EndsWith(".md")) continue;
                        resolved = Path.Combine(resolved, "index.html");
                    }
                    // PLAN/ is the local-only ticket store (gitignored): a clean clone or a CI checkout has none, so a link into it cannot be judged there.
                    if (!File.Exists(resolved) && resolved.StartsWith(Path.Combine(Repo, "PLAN") + Path.DirectorySeparatorChar) && !Directory.Exists(Path.Combine(Repo, "PLAN"))) continue;
                    if (!File.Exists(resolved)) { errors.Add(Relative(file) + ": missing " + link); continue; }
                    if (pieces.Length == 2 && pieces[1].Length > 0 && (resolved.EndsWith(".md") || resolved.EndsWith(".html")))
                    {
                        string anchor = Uri.UnescapeDataString(pieces[1]);
                        string destination = File.ReadAllText(resolved);
                        if (!Regex.IsMatch(destination, "\\bid=[\"']" + Regex.Escape(anchor) + "[\"']", RegexOptions.IgnoreCase)
                            && !(resolved.EndsWith(".md") && MarkdownAnchors(destination).Contains(anchor)))
                            errors.Add(Relative(file) + ": missing anchor " + link);
                    }
                }
            }
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }

        private static HashSet<string> MarkdownAnchors(string markdown)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match heading in Regex.Matches(markdown, @"^#{1,6}\s+(.+)$", RegexOptions.Multiline))
            {
                string slug = Regex.Replace(heading.Groups[1].Value.ToLowerInvariant(), @"[^\p{L}\p{N}_ -]", "").Trim().Replace(' ', '-');
                string candidate = slug;
                int suffix = 1;
                while (!ids.Add(candidate)) candidate = slug + "-" + suffix++;
            }
            return ids;
        }

        private static string Page(string kind, string locale) => Path.Combine(Docs,
            locale == "en" ? kind + ".html" : locale + "/" + kind + ".html");
        private static string Url(string kind, string locale) => "https://serzhyale.github.io/CyrFlip/" +
            (kind == "index" ? locale == "en" ? "" : locale + "/" :
            (locale == "en" ? "" : locale + "/") + kind + ".html");

        [Fact]
        public void PublicPagesHaveCompleteSeoAndFreshTranslations()
        {
            var groups = new Dictionary<string, string[]> {
                ["index"] = Locales.Except(new[] { "ru", "uk" }).ToArray(),
                ["guide"] = new[] { "en", "ru", "uk" }, ["privacy"] = Locales, ["trust"] = Locales };
            var sitemap = File.ReadAllText(Path.Combine(Docs, "sitemap.xml"));
            var index = File.ReadAllText(Path.Combine(Docs, "search-index.json"));
            foreach (var group in groups)
            {
                string source = File.ReadAllText(Page(group.Key, "en"));
                string body = Regex.Match(source, @"<body\b[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;
                body = Regex.Split(body, @"<footer\b", RegexOptions.IgnoreCase)[0].Replace("\r\n", "\n"); // the digest is of the LF form, whatever the checkout's line endings are
                string digest;
                using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant();
                foreach (string locale in group.Value)
                {
                    string page = Page(group.Key, locale);
                    string text = File.ReadAllText(page);
                    string title = WebUtility.HtmlDecode(Regex.Match(text, @"<title>(.*?)</title>").Groups[1].Value);
                    string description = WebUtility.HtmlDecode(Regex.Match(text, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value);
                    Assert.True(title.Length > 0 && title.Length < 60, page + ": title length " + title.Length);
                    Assert.True(description.Length > 0 && description.Length < 160, page + ": description length " + description.Length);
                    Assert.Contains("<link rel=\"canonical\" href=\"" + Url(group.Key, locale) + "\">", text);
                    foreach (string code in group.Value)
                        Assert.Contains("hreflang=\"" + code + "\" href=\"" + Url(group.Key, code) + "\"", text);
                    foreach (string marker in new[] { "og:title", "og:description", "og:image", "og:url", "twitter:card", "twitter:title", "twitter:description", "twitter:image", "application/ld+json" })
                        Assert.Contains(marker, text);
                    Assert.Contains(Url(group.Key, locale), sitemap);
                    Assert.Contains(Url(group.Key, locale), index);
                    if (locale != "en") Assert.Contains("name=\"source-sha256\" content=\"" + digest + "\"", text);
                }
            }
            Assert.Equal(groups.Sum(g => g.Value.Length), Regex.Matches(sitemap, "<loc>").Count);
            var rows = new JavaScriptSerializer().DeserializeObject(index) as object[];
            Assert.NotNull(rows);
            Assert.Equal(groups.Sum(g => g.Value.Length), rows!.Length);
            foreach (var item in rows.Cast<Dictionary<string, object>>())
            {
                string address = (string)item["url"];
                string locale = (string)item["lang"];
                string kind = groups.Keys.Single(k => Url(k, locale) == address);
                string page = File.ReadAllText(Page(kind, locale));
                Assert.Equal(WebUtility.HtmlDecode(Regex.Match(page, @"<title>(.*?)</title>").Groups[1].Value), item["title"]);
                Assert.Equal(WebUtility.HtmlDecode(Regex.Match(page, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value), item["description"]);
            }
        }

        [Fact]
        public void TermbaseHasFiveTermsPerLocaleAndGuidesUseThem()
        {
            var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Docs, "termbase.json"))) as Dictionary<string, object>;
            Assert.NotNull(data);
            var concepts = (object[])data!["concepts"];
            Assert.Equal(new[] { "layout", "conversion", "case flip", "CapsLock", "quick notes" }, concepts.Cast<string>());
            var locales = (Dictionary<string, object>)data["locales"];
            Assert.Equal(Locales.OrderBy(x => x), locales.Keys.OrderBy(x => x));
            foreach (var locale in locales)
            {
                var terms = ((object[])locale.Value).Cast<string>().ToArray();
                Assert.Equal(concepts.Length, terms.Length);
                Assert.All(terms, term => Assert.False(string.IsNullOrWhiteSpace(term)));
                if (new[] { "en", "ru", "uk" }.Contains(locale.Key))
                {
                    string visible = WebUtility.HtmlDecode(Regex.Replace(File.ReadAllText(Page("guide", locale.Key)), "<[^>]+>", " "));
                    foreach (string term in terms) Assert.Contains(term, visible);
                }
            }
            var forbidden = (Dictionary<string, object>)data["forbidden"];
            foreach (var locale in forbidden)
            {
                string combined = string.Join(" ", Directory.GetFiles(Path.Combine(Docs, locale.Key), "*.html")
                    .Select(File.ReadAllText));
                if (locale.Key == "en") combined = string.Join(" ", Directory.GetFiles(Docs, "*.html", SearchOption.TopDirectoryOnly).Select(File.ReadAllText));
                foreach (string bad in (object[])locale.Value)
                    Assert.DoesNotContain(bad, combined, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ConfigTableTracksPersistedValues()
        {
            string code = File.ReadAllText(Path.Combine(Repo, "src/CyrFlip/AppConfig.cs"));
            string docs = File.ReadAllText(Path.Combine(Repo, "CLAUDE.md"));
            string table = docs.Split(new[] { "| Registry value |" }, StringSplitOptions.None)[1].Split(new[] { "Legacy `config.json`" }, StringSplitOptions.None)[0];
            var written = new HashSet<string>(Regex.Matches(code, @"(?:key\.SetValue\(|WritePreserved\(key,\s*)""([^""\r\n]+)""")
                .Cast<Match>().Select(m => m.Groups[1].Value));
            var documented = Regex.Matches(table, @"^\|\s*`([^`]+)`", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            foreach (string name in written) Assert.Contains("`" + name + "`", table);
            foreach (string name in documented.Where(n => !n.Contains(" / "))) Assert.Contains(name, written);
            var defaults = new AppConfig();
            foreach (Match row in Regex.Matches(table, @"^\|\s*`([^`]+)`\s*\|\s*`REG_(?:DWORD|SZ)`\s*\|\s*`([^`]+)`", RegexOptions.Multiline))
            {
                string name = row.Groups[1].Value;
                string expected = row.Groups[2].Value;
                var property = typeof(AppConfig).GetProperty(name);
                if (property == null || (!int.TryParse(expected, out _) && expected != "system" && !expected.StartsWith("Ctrl+"))) continue;
                object value = property.GetValue(defaults, null)!;
                string actual = value is bool flag ? flag ? "1" : "0" : value.ToString()!;
                Assert.Equal(expected, actual);
            }
        }
    }
}
