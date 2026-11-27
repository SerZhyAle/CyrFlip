---
paths:
  - "docs/*.html"
  - "docs/*/*.html"
  - "tools/site/*"
  - "POSITIONING.md"
---

# Public site (GitHub Pages in `docs/`)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Project status: the site

- `docs/` - GitHub Pages site (served from `/docs`). `index.html` is the English landing and `ru/index.html` / `uk/index.html` the Russian and Ukrainian ones (ticket `S0024` P3.6: three standalone pages with their own `lang`, hreflang alternates and the 13-language row, no CSS `data-lang` switch and no script that rewrites the share-card tags; `docs/en/index.html` stays a `noindex` forwarder; `?lang=ru|uk|ua` on the root is forwarded by its pre-paint script); the **full guide stays EN/RU/UK** (`guide.html`, `docs/ru/`, `docs/uk/`) - as does `store.html`, the Microsoft Store channel page (`SITE-REPRESENTATION` rule 4, ticket `S0054` D2), and the Support and FAQ pages (`support.html`, `faq.html`, ticket `S0024` Q13; every page's footer links them, the other 10 locales to the English pages), while each of the other 10 UI languages gets a **standalone first page** at `docs/<lang>/index.html` with its own `lang`/`dir`, hreflang alternates and the same 13-language row. The **privacy policy exists in all 13** (`docs/<lang>/privacy.html`; `privacy.html` at
  the root is the English original and every translation links back to it as the authoritative text, saying outright
  that it prevails on any discrepancy) - it is the one page with an outside obligation, since the Store listing points
  at it in every language. All twelve translations are structurally identical (19 list items - the "Commands from other programs" item (S0112) names the four risk classes of the command line, and the last of the first list is the Store package's `runFullTrust` - and the "Usage counts" paragraph after the second list, 8 sections - the eighth, "This website", names the one third-party origin the site contacts, `api.github.com`), which is the
  cheap way to catch a section dropped in translation. Those pages are generated copy - edit them directly, there is no
  build step. The site serves its own typefaces (`docs/assets/fonts/`, OFL, notice in `THIRD-PARTY-NOTICES.txt`), so no font host is contacted; every content page carries the language row (endonym plus code) and writes `sza-lang` for RU/EN/UA on a click (ticket S0053). The 13 landings state the release (version, date, the versioned ZIP and its `.sha256`, the landing JSON-LD `softwareVersion`) only from `tools/site/release-facts.json`, which `tools/site/Render-ReleaseFacts.ps1 -Version <ver>` writes and `tools/site/sync-doc-quality.py` renders; the release preflight checks HEAD states the version it releases (S0024 F16, owner decision 3 of 2026-11-10), and the block is outside the content fingerprint, so a release moves no translation stamp. The site is declared at the **guide tier** of `SITE-STRUCTURE` / `SITE-EXPERIENCE` / `SITE-REPRESENTATION`
  (ticket S0049: pointers in `docs/contracts/`, the measured gaps in S0051-S0054); `tools/site/Measure-SiteContracts.ps1`
  (operator-typed, headless Chrome) reads the rendered pages against the rules a machine can decide, and
  `tools/site/Test-SiteConformance.ps1` is the older S0017 gate. `winget/` - manifest templates. `.github/` - CI + release workflows, issue/PR templates, dependabot. `build.ps1` - local build + deploy.
