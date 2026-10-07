# Pointer - `SITE-REPRESENTATION`

| | |
| --- | --- |
| **Id** | `SITE-REPRESENTATION` |
| **Version** | 0.1, draft |
| **Home** | `product-site/SITE-REPRESENTATION.md` in the shared contracts catalog, with `SITE-CHECKLIST.md` beside it |
| **Role here** | consumer, opted in on 2026-10-07 (ticket `S0049`), **guide tier** - rules 9 and 11 (availability marks, the showcase) are not applicable |

## What this repository must do to stay conformant

- **One positioning source, the pillars in its order** (rules 1, 2): [`POSITIONING.md`](../../POSITIONING.md) at the
  repository root, declared 2026-10-07 (ticket `S0058`, closing `S0054` D1) - the product name, the descriptor in 13
  languages and five pillars in order. `tools/site/Test-Positioning.ps1` holds the landing pages, the guide leads, the
  READMEs, the Store listing, the winget locales, the MSIX manifest, the About text and the extension to it.
- **A fact is typed once, in its source** (rules 3, 4): [`tools/site/site-facts.json`](../../tools/site/site-facts.json)
  is the facts file (ticket `S0054` D2-D3, owner decision O4 2026-10-07): **one edition** - the same portable exe - obtained
  through three channels (Microsoft Store, winget, Portable ZIP, each under one display name per language), with the VS Code
  extension as a companion surface; the channel ids are the stamp's, the Windows minimum is the MSIX `MinVersion` and the
  language count is the localization table's `Codes`. `tools/site/Test-SiteFacts.ps1` (run by `build.ps1` and the release
  preflight) compares the pages with the file. The Store build, whose rules differ, is presented on its own page
  `docs/store.html` (en, ru, uk; `SITE-STRUCTURE` section 2), linked from the guide's install section.
- **The guide names the interface in the interface's words** (rule 8): the 5 terms of `docs/termbase.json` and the 22
  quoted control names of its `controls` block are held both ways by `DocumentationQualityTests.GuideNamesControlsInTheirOwnWords`
  (ticket `S0054` D5): each name is one of the app's strings of that language and is quoted by the guide of that language.
- **The guide is a manual** (rule 7): one page with anchored task sections, a lead and steps per section - which is what
  `SITE-STRUCTURE` section 2 calls a guide. The function-page anatomy does not bind its sections at this tier
  (owner and catalog decision 2026-10-07, ticket `S0054` D4; the exception in the registry says the same).
- **A setting's value is stated once** (rule 10): this tier has no settings reference - `SITE-STRUCTURE` asks for one at
  portal tier only (owner decision 2026-10-07, ticket `S0054` D6); the guide states a default inline only where the task
  needs it, and the `CLAUDE.md` config table is held against `AppConfig` by `ConfigTableTracksPersistedValues`.
- **The trust surfaces say one thing** (rule 12): the privacy page, the trust page and the guide agree on what is
  stored and what is sent (read on 2026-10-07 for the English pages). A behaviour default that changes is changed on
  all three in one edit (`INSTALL-TRUST` rule 6 says the same of two of them).

Guard: `TrustPageTests` and `DocumentationQualityTests` hold the pages they cover (the termbase, its controls, the registry,
the source hashes), `Test-Positioning.ps1` holds rules 1 and 2, `Test-SiteFacts.ps1` holds rules 3 and 4; rules 5 and 12 are manual.
