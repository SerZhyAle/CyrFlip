# Pointer - `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY`

| | |
| --- | --- |
| **Ids** | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` |
| **Version** | `DOC-INTERNAL-QUALITY` 0.9, `DOC-EXTERNAL-QUALITY` 0.10, draft |
| **Home** | `documentation-quality/README.md` in the shared contracts catalog |
| **Role here** | consumer of both - adopted 2026-09-26; held as of 2026-10-01 (ticket `S0028`), most rules gated by tests - internal 5 (house style) and 6-7 (asset orphans, embedded third-party assets) are held by the cleanup and by reading, not gated. `DOC-INTERNAL-QUALITY` is still 0.9 in the catalog; `DOC-EXTERNAL-QUALITY` 0.10 was read on 2026-10-07 and widens only what conforms (section 8 of the contract), see the last section |

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

## Read at `DOC-EXTERNAL-QUALITY` 0.10 (2026-10-07)

The five points of section 8 each widen what conforms, so nothing above moved:

- **A (rule 1):** the landing is a portal, the guide a task-ordered page with its own index and glossary - the
  stricter form, which still conforms.
- **B (rule 2):** 40 indexable pages is above the 25-page threshold, so a regenerated-or-verified search index
  is owed and exists (`docs/search-index.json`, held against the pages with their titles and descriptions).
- **C (rule 5):** the 60 and 160 limits count Unicode code points after entity decoding. The gate decodes
  entities and counts UTF-16 units, which is never fewer than code points, so it is equal or stricter and
  cannot pass what the contract would fail.
- **D (rule 5):** not used - every locale has its own URL and its own `hreflang`.
- **E (rule 6):** not used - every step in the guide is in the product's own UI and has its capture.

The pages around the landing (which exist, how they look and behave, how they present the product) are
`SITE-STRUCTURE`, `SITE-EXPERIENCE` and `SITE-REPRESENTATION`, which cite these rules and repeat none of them.

## Synchronization read - 2026-10-18

The [current clause-to-surface audit](../design/contract-sync-20261017.md) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The [shared UI profile](../design/unified-ui.md) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.
