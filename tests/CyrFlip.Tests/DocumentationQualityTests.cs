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
            // The registry tracks this one as a release-affecting source file (ICON-EXTERNAL rule 5
            // ships its content beside the exe), so the corpus has to see it for the two-way check.
            yield return Path.Combine(Repo, "THIRD-PARTY-NOTICES.txt");
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

        // SITE-STRUCTURE rule 8 (ticket S0051 A3): the addresses held outside the site - the app, the READMEs, the
        // listings, the winget manifests - are listed in docs/site-held-addresses.jsonl in a form a program reads,
        // and this is the gate that compares the list both ways with those sources and resolves every entry.
        private const string SiteBase = "https://serzhyale.github.io/CyrFlip/";

        private sealed class HeldEntry
        {
            public string Path = "";
            public string Anchor = "";
            public SortedSet<string> Files = new SortedSet<string>(StringComparer.Ordinal);
            public string Key => Path + (Anchor.Length > 0 ? "#" + Anchor : "");
        }

        private static List<HeldEntry> ReadHeldAddressList()
        {
            var parser = new JavaScriptSerializer();
            var entries = new List<HeldEntry>();
            foreach (string line in File.ReadAllLines(Path.Combine(Docs, "site-held-addresses.jsonl")).Where(l => l.Trim().Length > 0))
            {
                var row = parser.Deserialize<Dictionary<string, object>>(line);
                var entry = new HeldEntry { Path = (string)row["path"], Anchor = (string)row["anchor"] };
                foreach (Dictionary<string, object> holder in ((System.Collections.IEnumerable)row["held_by"]).Cast<Dictionary<string, object>>())
                {
                    Assert.False(string.IsNullOrWhiteSpace((string)holder["surface"]), entry.Key + ": a holder without a surface name");
                    entry.Files.Add((string)holder["file"]);
                }
                entries.Add(entry);
            }
            return entries;
        }

        // Every file that can carry an address a person or a store reads: the app's strings, the READMEs, the
        // extension's manifest and README, the winget manifests, the Store listing sources.
        private static IEnumerable<string> HeldAddressSources()
        {
            var sources = new List<string>();
            void Add(string directory, string pattern)
            {
                string folder = Path.Combine(Repo, directory);
                if (Directory.Exists(folder)) sources.AddRange(Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly));
            }
            Add("src/CyrFlip", "*.cs");
            Add("", "*.md");
            Add("vscode-extension", "*.md");
            Add("vscode-extension", "package.json");
            Add("vscode-extension/src", "*.ts");
            Add("winget", "*.yaml");
            Add("msix", "*.xml");
            Add("msix", "*.csv");
            Add("msix", "*.md");
            Add("msix/listing", "*.txt");
            Add("store", "listing-*.txt");
            return sources;
        }

        private static Dictionary<string, SortedSet<string>> FindHeldAddresses(IEnumerable<string> files)
        {
            var found = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var address = new Regex(@"https://serzhyale\.github\.io/CyrFlip/([^\s""'<>)\]`\\,;]*)");
            foreach (string file in files)
                foreach (Match match in address.Matches(File.ReadAllText(file)))
                {
                    string tail = match.Groups[1].Value.TrimEnd('.').Split('?')[0];
                    string key = tail;
                    if (!found.TryGetValue(key, out SortedSet<string>? holders)) found[key] = holders = new SortedSet<string>(StringComparer.Ordinal);
                    holders.Add(Relative(file));
                }
            return found;
        }

        private static string PageFileFor(string path)
        {
            string local = path.Length == 0 ? "index.html" : path.EndsWith("/") ? path + "index.html" : path;
            return Path.Combine(Docs, local.Replace('/', Path.DirectorySeparatorChar));
        }

        private static List<string> CompareHeldAddresses(List<HeldEntry> entries, Dictionary<string, SortedSet<string>> found, string sitemap)
        {
            var errors = new List<string>();
            foreach (var duplicate in entries.GroupBy(e => e.Key).Where(g => g.Count() > 1))
                errors.Add("listed twice: " + duplicate.Key);
            var listed = entries.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var key in found.Keys.Except(listed.Keys, StringComparer.Ordinal))
                errors.Add("held outside the site but not in the list: " + key + " (" + string.Join(", ", found[key]) + ")");
            foreach (var entry in entries)
            {
                if (!found.TryGetValue(entry.Key, out SortedSet<string>? holders)) { errors.Add("listed but held by no source: " + entry.Key); }
                else if (!holders.SetEquals(entry.Files))
                    errors.Add("holders differ for " + entry.Key + ": list says [" + string.Join(", ", entry.Files) + "], sources say [" + string.Join(", ", holders) + "]");
                string page = PageFileFor(entry.Path);
                if (!File.Exists(page)) { errors.Add("does not resolve to a page: " + entry.Key); continue; }
                string text = File.ReadAllText(page);
                if (Regex.IsMatch(text, "http-equiv=[\"']refresh[\"']", RegexOptions.IgnoreCase))
                    errors.Add("resolves to a forwarder, not a page: " + entry.Key);
                if (!sitemap.Contains("<loc>" + SiteBase + entry.Path + "</loc>"))
                    errors.Add("not in the sitemap: " + entry.Key);
                if (entry.Anchor.Length > 0 && !Regex.IsMatch(text, "\\bid=[\"']" + Regex.Escape(entry.Anchor) + "[\"']"))
                    errors.Add("anchor is missing on its page: " + entry.Key);
            }
            return errors;
        }

        [Fact]
        public void HeldAddressListMatchesItsSourcesAndResolves()
        {
            var entries = ReadHeldAddressList();
            Assert.NotEmpty(entries);
            var errors = CompareHeldAddresses(entries, FindHeldAddresses(HeldAddressSources()),
                File.ReadAllText(Path.Combine(Docs, "sitemap.xml")));
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
            // SITE-STRUCTURE rule 8: a section anchor an outside surface links is never renamed.
            Assert.Contains(entries, e => e.Anchor == "layout-colours" && e.Path == "guide.html");
        }

        [Fact]
        public void HeldAddressGateFailsOnAPlantedWrongEntry()
        {
            string sitemap = File.ReadAllText(Path.Combine(Docs, "sitemap.xml"));
            var found = FindHeldAddresses(HeldAddressSources());
            Assert.Empty(CompareHeldAddresses(ReadHeldAddressList(), found, sitemap));

            List<HeldEntry> Fresh() => ReadHeldAddressList();
            var typo = Fresh(); typo.First(e => e.Path == "privacy.html").Path = "privacy.htm";
            Assert.Contains(CompareHeldAddresses(typo, found, sitemap), e => e.Contains("does not resolve to a page: privacy.htm"));

            var lostAnchor = Fresh(); lostAnchor.First(e => e.Anchor == "layout-colours").Anchor = "layout-colors";
            Assert.NotEmpty(CompareHeldAddresses(lostAnchor, found, sitemap));

            var forwarder = Fresh(); forwarder.Add(new HeldEntry { Path = "ru/", Files = new SortedSet<string>(new[] { "README_RU.md" }) });
            Assert.Contains(CompareHeldAddresses(forwarder, found, sitemap), e => e.Contains("listed but held by no source: ru/"));

            var missing = Fresh(); missing.RemoveAt(0);
            Assert.Contains(CompareHeldAddresses(missing, found, sitemap), e => e.Contains("held outside the site but not in the list"));

            var wrongHolder = Fresh(); wrongHolder.First(e => e.Path == "trust.html").Files.Add("README_UK.md");
            Assert.Contains(CompareHeldAddresses(wrongHolder, found, sitemap), e => e.Contains("holders differ for trust.html"));

            var forwardedEntry = Fresh(); forwardedEntry.First(e => e.Path == "privacy.html").Path = "en/";
            Assert.Contains(CompareHeldAddresses(forwardedEntry, found, sitemap), e => e.Contains("forwarder"));
        }

        // SITE-STRUCTURE rule 15 (ticket S0051 A1): the host serves docs/404.html for any unknown address, at any
        // depth, so it must be noindex, built from absolute addresses, and offer what this tier has.
        [Fact]
        public void NotFoundPageIsNoindexAbsoluteAndOffersTheCorePages()
        {
            string page = File.ReadAllText(Path.Combine(Docs, "404.html"));
            Assert.Matches("<meta name=\"robots\" content=\"noindex\">", page);
            foreach (string lang in new[] { "ru", "en", "ua" })
                Assert.Contains("data-l=\"" + lang + "\"", page);
            var links = Regex.Matches(page, @"\b(?:href|src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.All(links, link =>
            {
                if (link.StartsWith("#"))
                    Assert.Contains("id=\"" + link.Substring(1) + "\"", page);
                else
                    Assert.True(link.StartsWith("https://") || link.StartsWith("mailto:"), "404.html: a relative address would break at depth: " + link);
            });
            foreach (string path in new[] { "", "guide.html", "privacy.html" })
            {
                Assert.Contains(SiteBase + path, links);
                Assert.True(File.Exists(PageFileFor(path)), "404.html offers a page that does not exist: " + path);
            }
            Assert.DoesNotContain("404.html", File.ReadAllText(Path.Combine(Docs, "sitemap.xml")));
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
                ["guide"] = new[] { "en", "ru", "uk" }, ["store"] = new[] { "en", "ru", "uk" }, ["privacy"] = Locales, ["trust"] = Locales };
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
            // The generated help set (S0075, docs/help/) is gated by tools/help/Test-ContextHelp.ps1; this inventory
            // counts the hand-authored public pages, so the help discovery entries are counted out here.
            var locations = Regex.Matches(sitemap, "<loc>([^<]+)</loc>").Cast<Match>().Select(m => m.Groups[1].Value).Where(u => !u.Contains("/help/")).ToList();
            Assert.Equal(groups.Sum(g => g.Value.Length), locations.Count);
            var rows = (new JavaScriptSerializer().DeserializeObject(index) as object[])!.Cast<Dictionary<string, object>>()
                .Where(r => !((string)r["url"]).Contains("/help/")).ToList();
            Assert.Equal(groups.Sum(g => g.Value.Length), rows.Count);
            foreach (var item in rows)
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
            var data = Assert.IsType<Dictionary<string, object>>(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Docs, "termbase.json"))));
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
        public void GuideNamesControlsInTheirOwnWords()
        {
            // SITE-REPRESENTATION rule 8 (ticket S0054 D5): a control the guide names is the product's own
            // string of that language, in the app's strings and quoted by the guide.
            var data = Assert.IsType<Dictionary<string, object>>(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(Docs, "termbase.json"))));
            var controls = (Dictionary<string, object>)data["controls"];
            string table = string.Join(" ", Directory.GetFiles(Path.Combine(Repo, "src", "CyrFlip"), "Localization*.cs").Select(File.ReadAllText));
            var appStrings = new Dictionary<string, HashSet<string>>
            {
                // The key is the English text (S0070); `en:` is only the display text of a key that carries a context in parentheses.
                ["en"] = new HashSet<string>(Regex.Matches(table, @"Add\(\s*""([^""\r\n]+)""").Cast<Match>().Select(m => m.Groups[1].Value)
                    .Concat(Regex.Matches(table, @"\ben:\s*""([^""\r\n]*)""").Cast<Match>().Select(m => m.Groups[1].Value))),
                ["ru"] = new HashSet<string>(Regex.Matches(table, @"\bru:\s*""([^""\r\n]*)""").Cast<Match>().Select(m => m.Groups[1].Value)),
                ["uk"] = new HashSet<string>(Regex.Matches(table, @"\buk:\s*""([^""\r\n]*)""").Cast<Match>().Select(m => m.Groups[1].Value)),
            };
            foreach (var locale in controls)
            {
                string visible = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(File.ReadAllText(Page("guide", locale.Key)), "<[^>]+>", " ")), @"\s+", " ");
                foreach (string name in ((object[])locale.Value).Cast<string>())
                {
                    Assert.True(visible.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        locale.Key + " guide does not name the control as '" + name + "'");
                    Assert.True(appStrings[locale.Key].Contains(name),
                        locale.Key + " termbase control '" + name + "' is not one of the app's own strings");
                }
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
