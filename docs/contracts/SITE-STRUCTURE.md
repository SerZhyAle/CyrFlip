# Pointer - `SITE-STRUCTURE`

| | |
| --- | --- |
| **Id** | `SITE-STRUCTURE` |
| **Version** | 0.1, draft |
| **Home** | `product-site/SITE-STRUCTURE.md` in the shared contracts catalog, with `SITE-CHECKLIST.md` beside it |
| **Role here** | consumer, opted in on 2026-10-07 (ticket `S0049`), **guide tier** - rules 4 to 7, 12 and 14 (portal pages, the showcase) are not bound |

## What this repository must do to stay conformant

- **The tier is guide, and never lower than the pages published.** Landing, privacy, trust, the guide (one page per
  language, task sections, a subject index and a glossary), the sitemap, the not-found page (`docs/404.html`,
  published and observed on the host 2026-10-14, ticket `S0051` A1). The release notes' declared home is the
  GitHub Releases page (owner O1, 2026-10-14, ticket `S0051` A2) - a dated exception until the contract rules
  on off-site homes.
- **Locale sets are declared, not implied** (rule 10): landing 13 languages, privacy 13, trust 13, the guide en, ru
  and uk (the core three); no reference group. A new locale counts only after it is declared here and in the registry.
- **Addresses are permanent** (rule 8): the primary language lives at the root, each sibling at
  `<code>/<name>.html` (`<code>/` for a landing, folder `uk` for Ukrainian). A moved page leaves a forwarder
  (`en/`, `ru/`, `uk/` are three); `guide.html#layout-colours` is held by the READMEs and is never renamed. The
  addresses held outside the site are listed in `docs/site-held-addresses.jsonl` and resolved by a two-way
  gate (ticket `S0051` A3).
- **No link or control leads to an address that does not answer** (rules 9, 11), and every page stays within three
  steps of the landing (rule 3). `tools/site/Measure-SiteContracts.ps1` reads both off a rendered run.

Guard: `DocumentationQualityTests` (sitemap, links, anchors, the held-address list, the not-found page),
`tools/site/Measure-SiteContracts.ps1` (operator-typed).

## Synchronization read - 2026-10-18

The [current clause-to-surface audit](../design/contract-sync-20261017.md) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The [shared UI profile](../design/unified-ui.md) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.
