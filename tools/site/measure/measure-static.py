"""Source-level half of tools/site/Measure-SiteContracts.ps1: the link graph and the page set of docs/.

Reads the HTML as written (no browser): reach from the landing and broken internal links
(SITE-STRUCTURE rule 3), the sitemap against the files (rule 2), the retired addresses' forwarders (rule 8),
hreflang and canonical targets (rule 9), the head order and the page scripts that touch `sza-lang`
(SITE-EXPERIENCE rules 7 and 10).

usage: measure-static.py <docs-dir> <out.json> <site-base-url>
"""
import collections
import hashlib
import json
import os
import re
import sys
from html.parser import HTMLParser
from urllib.parse import unquote

DOCS = os.path.abspath(sys.argv[1])
BASE = sys.argv[3] if sys.argv[3].endswith("/") else sys.argv[3] + "/"


def norm(rel):
    return rel + "index.html" if (rel == "" or rel.endswith("/")) else rel


class Page(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links, self.head, self.scripts = [], [], []
        self._script = None
        self.noindex = False
        self.refresh = False
        self.canonical = None
        self.images = []
        self.callouts = collections.Counter()

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "img":
            self.images.append({"src": a.get("src"), "alt": a.get("alt")})
        classes = (a.get("class") or "").split()
        if "note" in classes:
            kind = next((k for k in ("tip", "warning", "outcome") if "callout-" + k in classes), "note")
            self.callouts[kind] += 1
        if tag == "a" and a.get("href"):
            self.links.append(a["href"])
        if tag == "link":
            rel = (a.get("rel") or "").lower()
            if rel == "canonical":
                self.canonical = a.get("href")
            if rel in ("stylesheet", "alternate"):
                self.head.append(("stylesheet" if rel == "stylesheet" else "alternate", a.get("href")))
        if tag == "meta":
            if (a.get("name") or "").lower() == "robots" and "noindex" in (a.get("content") or "").lower():
                self.noindex = True
            if (a.get("http-equiv") or "").lower() == "refresh":
                self.refresh = True
        if tag == "script" and not a.get("src") and a.get("type") != "application/ld+json":
            self._script = []
            self.head.append(("inline-script", None))

    def handle_data(self, data):
        if self._script is not None:
            self._script.append(data)

    def handle_endtag(self, tag):
        if tag == "script" and self._script is not None:
            self.scripts.append("".join(self._script))
            self._script = None


files = sorted(
    os.path.relpath(os.path.join(d, f), DOCS).replace("\\", "/")
    for d, _, fs in os.walk(DOCS) for f in fs if f.endswith(".html")
)
pages = {}
for f in files:
    p = Page()
    p.feed(open(os.path.join(DOCS, f), encoding="utf-8").read())
    pages[f] = p


def resolve(src, href):
    if href.startswith(("mailto:", "javascript:", "tel:", "#")):
        return None
    if href.startswith("http"):
        if not href.startswith(BASE):
            return None
        return norm(unquote(href[len(BASE):].split("#")[0].split("?")[0]))
    href = unquote(href.split("#")[0].split("?")[0])
    if href == "":
        return None
    joined = os.path.normpath(os.path.join(os.path.dirname(src), href)).replace("\\", "/")
    if href.endswith("/"):
        joined = "index.html" if joined == "." else joined.rstrip("/") + "/index.html"
    return "index.html" if joined == "." else joined


edges, broken = collections.defaultdict(set), []
for f, p in pages.items():
    for h in p.links:
        t = resolve(f, h)
        if t is None:
            continue
        edges[f].add(t)
        if not os.path.exists(os.path.join(DOCS, t)):
            broken.append([f, h])

depth, queue = {"index.html": 0}, ["index.html"]
while queue:
    c = queue.pop(0)
    for t in edges.get(c, ()):
        if t in pages and t not in depth:
            depth[t] = depth[c] + 1
            queue.append(t)

sitemap = open(os.path.join(DOCS, "sitemap.xml"), encoding="utf-8").read()
in_sitemap = {norm(l[len(BASE):]) for l in re.findall(r"<loc>([^<]+)</loc>", sitemap) if l.startswith(BASE)}
forwarders = sorted(f for f, p in pages.items() if p.refresh)
# The not-found page (rule 15) is served by the host at any depth: no link reaches it and the sitemap never lists it.
NOT_FOUND_PAGES = {"404.html"}

dangling = []
for f in pages:
    text = open(os.path.join(DOCS, f), encoding="utf-8").read()
    for m in re.finditer(r'<link rel="(?:alternate|canonical)"[^>]*href="([^"]+)"', text):
        t = resolve(f, m.group(1))
        if t is not None and not os.path.exists(os.path.join(DOCS, t)):
            dangling.append([f, m.group(1)])


def first_inline_before_stylesheet(p):
    order = [k for k, _ in p.head if k in ("inline-script", "stylesheet")]
    return bool(order) and order[0] == "inline-script"


def resolver_text(p):
    return re.sub(r"\s+", " ", p.scripts[0]).strip() if p.scripts else ""


# PAGE-STYLE 3.1 consolidation item 4: context help is scriptless, in the app's
# language, with its own theme layer. It remains in link/asset/discovery checks.
res_hash = {f: hashlib.sha256(resolver_text(p).encode("utf-8")).hexdigest()[:12] for f, p in pages.items() if f not in forwarders and not f.startswith("help/")}
guides = {}
for f, p in pages.items():
    if f.endswith("guide.html"):
        text = open(os.path.join(DOCS, f), encoding="utf-8").read()
        guides[f] = {"images": p.images, "callouts": dict(p.callouts),
                     "warnings_before_instructions": len(re.findall(
                         r'<section\b[^>]*>\s*<h2\b[^>]*>[^<]*</h2>\s*<aside class="note callout-warning"', text, re.S))}
out = {
    "guide_content": guides,
    "pages": files,
    "reach_depth": dict(collections.Counter(depth.values())),
    "unreached": sorted(f for f in pages if f not in depth and f not in NOT_FOUND_PAGES),
    "max_depth": max(depth.values()),
    "broken_internal_links": broken,
    "sitemap_without_file": sorted(f for f in in_sitemap if f not in pages),
    "indexable_not_in_sitemap": sorted(f for f in pages if f not in in_sitemap and f not in forwarders and f not in NOT_FOUND_PAGES),
    "forwarders": {f: {"noindex": pages[f].noindex, "canonical": bool(pages[f].canonical), "refresh": pages[f].refresh,
                       "link": any(resolve(f, h) == "index.html" for h in pages[f].links)} for f in forwarders},
    "dangling_alternates": dangling,
    "resolver_not_first": [f for f, p in pages.items() if not f.startswith("help/") and not first_inline_before_stylesheet(p)],
    "resolver_text_variants": len(set(res_hash.values())),
    "pages_without_sza_lang": sorted(f for f, p in pages.items() if f not in forwarders and not f.startswith("help/") and not any("sza-lang" in s for s in p.scripts)),
    "pages_reading_or_writing_sza_theme": sum(1 for p in pages.values() if any("sza-theme" in s for s in p.scripts)),
    "has_404": os.path.exists(os.path.join(DOCS, "404.html")),
}
json.dump(out, open(sys.argv[2], "w", encoding="utf-8"), indent=1)
print("static: %d pages, depth <= %d, %d unreached, %d broken links, %d dangling alternates" %
      (len(files), out["max_depth"], len(out["unreached"]), len(broken), len(dangling)))
