# Pointer - `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY`

| | |
| --- | --- |
| **Ids** | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` |
| **Version** | 0.9, draft (both) |
| **Home** | `documentation-quality/README.md` in the shared contracts catalog |
| **Role here** | consumer of both - adopted 2026-09-26; held as of 2026-10-01 (ticket `S0028`), most rules gated by tests - internal 5 (house style) and 6-7 (asset orphans, embedded third-party assets) are held by the cleanup and by reading, not gated |

## Why both

Internal: `CLAUDE.md`, the READMEs, `docs/contracts/`, `RELEASE.md`, `STORE_PUBLISHING.md`, `msix/`,
`winget/`, `tools/uitest/README.md`, the `.claude/skills` pages. External: the GitHub Pages site under
`docs/` - landing, guide, privacy and trust pages in 13 languages - plus the three READMEs a visitor
reads on GitHub.

## What this repository does to stay conformant

One xUnit class, [tests/CyrFlip.Tests/DocumentationQualityTests.cs](../../tests/CyrFlip.Tests/DocumentationQualityTests.cs),
holds most of both contracts:

- **internal 1-2** - `docs/DOCUMENT_REGISTRY.jsonl` declares every tracked document with its topic,
  area, triggers and a `source`/`render` role; the registry and the corpus are checked in both
  directions.
- **internal 3 and 6, external 7** - every relative link, anchor and image across the tracked `.md`
  and the `docs/**/*.html` pages must resolve; `http:` links are refused.
- **internal 4** - `CLAUDE.md`'s config table is checked against the registry keys `AppConfig` actually
  writes and their defaults. The render gates from adoption stay: `msix/render-listing-mirrors.ps1
  -Check` in the release preflight, `LayoutColorsTests` and `ExtensionContractConstantsTests` for the
  extension's copies, `TrustPageTests` for the trust page set in every locale.
- **internal 5** - the house style; the dash and ellipsis residue found at adoption is cleaned, and the
  Win32 trailing `...` in quoted menu strings remains the declared local exception (`CLAUDE.md`,
  "Local specifics").
- **external 1** - the landing is the portal; the guide is task-ordered and carries a subject index
  and a glossary.
- **external 2** - `docs/sitemap.xml` and `docs/search-index.json` are held against the 40 indexable
  pages: membership, plus each page's title and description.
- **external 3** - every translation carries `<meta name="source-sha256">` with the SHA-256 of its
  English source's body; the test fails when either side moves.
- **external 4** - `docs/termbase.json` fixes the five product terms for all 13 locales; the en/ru/uk
  guides must use them and the forbidden alternatives must not appear.
- **external 5** - title and description lengths, canonical, `hreflang`, the `og:`/Twitter block and
  JSON-LD are required on every indexable page.
- **external 6** - the guide's multi-step sections carry per-locale captures
  (`docs/assets/guide-settings-*-<locale>.png`); their references are held by the link gate.
