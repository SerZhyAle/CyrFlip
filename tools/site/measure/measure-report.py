"""Report half of tools/site/Measure-SiteContracts.ps1: turns the browser and source measurements of one run into
a per-rule list of findings (SITE-STRUCTURE, SITE-EXPERIENCE; rule numbers as the contracts number them).

usage: measure-report.py <out-dir> <kit-file> <expected-kit-sha256 or -> <declared-origins comma list> [<own host>]
Writes <out-dir>/findings.json: [{"rule": "EX 16", "group": "...", "ok": false, "text": "..."}]
"""
import collections
import hashlib
import json
import os
import sys

OUT, KIT, EXPECT_KIT, DECLARED = sys.argv[1], sys.argv[2], sys.argv[3], [h for h in sys.argv[4].split(",") if h]
# the site's own production host: 404.html names it absolutely (it is served at any depth), and the harness runs on localhost
OWN_HOST = sys.argv[5] if len(sys.argv) > 5 else ""
RTL = {"ar", "ur"}
# a declared host and the privacy-text probe (measure-run.html `privacyText`) that finds its Latin name on a page
NAMING = {"api.github.com": "apiGithub"}


def load(name):
    p = os.path.join(OUT, name)
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else None


static, full = load("static.json"), load("full.json")
motion, base = load("motion.json"), load("motion-baseline.json")
findings = []


def add(rule, group, ok, text):
    findings.append({"rule": rule, "group": group, "ok": bool(ok), "text": text})


def group(p):
    if p in static["forwarders"]:
        return "forwarder"
    for g in ("guide", "privacy", "trust"):
        if p.endswith(g + ".html"):
            return g
    return "landing"


def code(p):
    return p.split("/")[0] if "/" in p else "en"


# ---- source half
add("ST 3", "all", not static["unreached"] and not static["broken_internal_links"] and static["max_depth"] <= 3,
    "%d pages, deepest %d step(s) from the landing, %d unreached, %d broken internal links" %
    (len(static["pages"]), static["max_depth"], len(static["unreached"]), len(static["broken_internal_links"])))
add("ST 2", "all", not static["sitemap_without_file"] and not static["indexable_not_in_sitemap"],
    "sitemap against the files: %d listed without a file, %d indexable pages missing" %
    (len(static["sitemap_without_file"]), len(static["indexable_not_in_sitemap"])))
add("ST 8", "forwarder", all(v["noindex"] and v["canonical"] and v["refresh"] and v["link"] for v in static["forwarders"].values()),
    "%d retired addresses answer with a forwarder (link, canonical, refresh, noindex)" % len(static["forwarders"]))
add("ST 9", "all", not static["dangling_alternates"], "%d hreflang/canonical targets do not resolve" % len(static["dangling_alternates"]))
add("ST 15", "all", static["has_404"], "docs/404.html %s" % ("exists" if static["has_404"] else "is absent: an unknown address shows the host's default page"))
add("EX 7", "all", not static["resolver_not_first"] and static["resolver_text_variants"] == 1,
    "the pre-paint script stands after a stylesheet link on %d pages (%s); %d distinct resolver texts (the contract's is one)" %
    (len(static["resolver_not_first"]), ", ".join(static["resolver_not_first"][:4]) + (" .." if len(static["resolver_not_first"]) > 4 else ""),
     static["resolver_text_variants"]))
add("EX 10", "all", not static["pages_without_sza_lang"],
    "%d of %d pages never read or write sza-lang (the language links are plain links)" %
    (len(static["pages_without_sza_lang"]), len(static["pages"]) - len(static["forwarders"])))
guides = static.get("guide_content", {})
images = [i for p in guides.values() for i in p["images"]]
add("EX 18", "guide", bool(images) and all(i["alt"] and i["alt"].strip() for i in images),
    "%d image alternatives present; action and visible state are reviewed against the captures in the S0052 evidence" % len(images))
add("EX 5", "guide", bool(guides) and all(
    all(p["callouts"].get(k, 0) > 0 for k in ("note", "tip", "warning", "outcome")) and
    p["warnings_before_instructions"] == p["callouts"]["warning"] for p in guides.values()),
    "%d guides carry all four callout kinds; %d warnings precede section instructions (risk coverage reviewed in the S0052 evidence)" %
    (len(guides), sum(p["warnings_before_instructions"] for p in guides.values())))

# ---- rendered half
sha = hashlib.sha256(open(KIT, "rb").read()).hexdigest()
if EXPECT_KIT == "-":
    add("EX 1", "all", None, "kit sha256 %s - no catalog hash given, not compared" % sha[:16])
else:
    add("EX 1", "all", sha.lower() == EXPECT_KIT.lower(), "vendored kit sha256 %s against the catalog's %s" % (sha[:16], EXPECT_KIT[:16].lower()))

G = collections.defaultdict(list)
for r in full:
    # a forwarder replaces itself with the landing in the iframe, so its rendered numbers would be the landing's:
    # it is judged by the source pass only (ST 8)
    if group(r["page"]) != "forwarder":
        G[group(r["page"])].append(r)

for g, rows in G.items():
    no_control = [r["page"] for r in rows if not r["dark1920"]["lang"]["links"] and not r["dark1920"]["lang"]["seg"]]
    if no_control:
        add("EX 10", g, False, "%d of %d pages carry no language control although the group exists in several languages" % (len(no_control), len(rows)))
    S = lambda r: r["dark1920"]["structure"]
    bad_main = [r["page"] for r in rows if S(r)["mains"] != 1]
    bad_h1 = [r["page"] for r in rows if S(r)["h1"] != 1]
    skipped = [r["page"] for r in rows if S(r)["skippedLevels"]]
    bad_lang = [r["page"] for r in rows if S(r)["lang"] != code(r["page"])]
    bad_dir = [r["page"] for r in rows if (S(r)["dir"] == "rtl") != (code(r["page"]) in RTL)]
    add("EX 16", g, not (bad_main or bad_h1 or skipped or bad_lang or bad_dir),
        "%d pages: no single main on %d, h1 count wrong on %d, a heading level skipped on %d, lang wrong on %d, dir wrong on %d" %
        (len(rows), len(bad_main), len(bad_h1), len(skipped), len(bad_lang), len(bad_dir)))
    no_skip = [r["page"] for r in rows if not S(r)["skipLink"]]
    pos_tab = sum(S(r)["posTab"] for r in rows)
    add("EX 15", g, not no_skip and not pos_tab, "%d of %d pages have no skip link to the main content; %d positive tabindex" % (len(no_skip), len(rows), pos_tab))
    styled = sum(S(r)["styleAttrsLayout"] for r in rows)
    add("EX 4", g, styled == 0, "%d style attributes carrying layout or colour" % styled)
    for k, label in (("dark1920", "dark"), ("light1920", "light")):
        bad = [r for r in rows if r[k].get("contrast", {}).get("fail")]
        agg = collections.Counter(f["sel"] + " " + str(f["r"]) for r in bad for f in r[k]["contrast"]["fail"])
        add("EX 17", g, not bad, "text contrast %s theme at 1920: %d of %d pages below 4.5:1 (3:1 large); lowest %s; %s" %
            (label, len(bad), len(rows), min(r[k]["contrast"]["min"] for r in rows), dict(agg.most_common(5))))
    small = sum(r["dark360"]["targets"]["smallControls"] for r in rows)
    add("EX 17", g, small == 0, "%d controls under 44 px at 360 (inline links excluded); e.g. %s" % (small, rows[0]["dark360"]["targets"]["sample"][:3]))
    for k, label in (("dark1920", "1920"), ("light2560", "2560")):
        ok = sum(1 for r in rows if r[k]["width"]["ok"])
        caps = collections.Counter(c for r in rows for c in r[k]["width"]["caps"])
        add("EX 20", g, ok == len(rows), "main fills the viewport less 80 px at %s on %d of %d pages; wrappers capped: %s" % (label, ok, len(rows), dict(caps.most_common(2))))
    bad_t = [(r["page"], r["dark1920"]["lang"]["bad"]) for r in rows if r["dark1920"]["lang"]["bad"]]
    add("ST 9", g, not bad_t, "language-control targets that do not answer: %d" % len(bad_t))
    origins = collections.Counter(h for r in rows for h in r["dark1920"]["origins"] if h != OWN_HOST)
    undeclared = sorted(h for h in origins if h not in DECLARED)
    add("EX 14", g, not undeclared, "origins contacted %s; undeclared: %s" % (dict(origins), undeclared))
    if g == "privacy":
        unnamed = {h: sum(1 for r in rows if not r["privacyText"].get(NAMING.get(h, ""), False)) for h in DECLARED}
        add("EX 14", g, all(v == 0 for v in unnamed.values()), "declared origins not named on the privacy page (pages out of %d): %s" % (len(rows), unnamed))

if motion and base:
    def tot(m, k):
        return sum(r["motion"]["motion"][k] for r in m)
    add("EX 19", "all", tot(motion, "runningAnimations") == 0 and tot(motion, "transitionEls") == 0 and tot(base, "runningAnimations") > 0,
        "preference forced to reduce: %d running animations, %d transitions (without it: %d and %d, so the check can fail)" %
        (tot(motion, "runningAnimations"), tot(motion, "transitionEls"), tot(base, "runningAnimations"), tot(base, "transitionEls")))
else:
    add("EX 19", "all", None, "motion passes not run")

json.dump(findings, open(os.path.join(OUT, "findings.json"), "w", encoding="utf-8"), indent=1)
for f in findings:
    print("%-6s %-9s %-9s %s" % (f["rule"], f["group"], {True: "ok", False: "DEVIATES", None: "not run"}[f["ok"]], f["text"]))
