# Pointer - `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY`

| | |
| --- | --- |
| **Ids** | `DOC-INTERNAL-QUALITY`, `DOC-EXTERNAL-QUALITY` |
| **Version** | 0.9, draft (both) |
| **Home** | `documentation-quality/README.md` in the shared contracts catalog |
| **Role here** | consumer of both - adopted 2026-09-26, partly conformant |

## Why both

Internal: `CLAUDE.md`, the READMEs, `docs/contracts/`, `RELEASE.md`, `STORE_PUBLISHING.md`, `msix/`,
`winget/`, `tools/uitest/README.md`. External: the GitHub Pages site under `docs/` - landing, guide,
privacy and trust pages in 13 languages - plus the three READMEs a visitor reads on GitHub.

## What this repository does to stay conformant

- **Generated copies are checked, never hand-kept**: `msix/render-listing-mirrors.ps1 -Check` in the
  release preflight; `LayoutColorsTests` and `ExtensionContractConstantsTests` for the extension's copies;
  `TrustPageTests` for the trust page set in every locale.
- **Every indexable page is in `docs/sitemap.xml`**; `docs/ru/` and `docs/uk/` index pages are `noindex`
  redirects and are not.
- **The house style** is enforced by the canon's compliance gate; the Win32 trailing `...` in quoted menu
  strings is the declared local exception (`CLAUDE.md`, "Local specifics").

Not yet held - no document registry and so no reverse coverage, no repo-owned link or asset gate, no
translation-freshness tracking, no termbase, and an incomplete SEO block (Twitter card, JSON-LD,
`hreflang` on privacy/trust/guide, over-long titles and descriptions). Plan in `PLAN/` (local, not
published); the gaps are dated exceptions in the catalog registry.
