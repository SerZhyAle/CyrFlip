# Pointer - `SITE-EXPERIENCE`

| | |
| --- | --- |
| **Id** | `SITE-EXPERIENCE` |
| **Version** | 0.3, draft |
| **Home** | `product-site/SITE-EXPERIENCE.md` in the shared contracts catalog, with `SITE-CHECKLIST.md` beside it |
| **Role here** | consumer, opted in on 2026-10-07 (ticket `S0049`), **guide tier** - the portal layer's rules (2, 3, 9, 11, 12) are not bound |

## What this repository must do to stay conformant

- **Every page is built on the kit and fills the screen** (rules 1, 20): `docs/assets/sza-kit.css` is the catalog's
  file byte for byte (refreshed by `S0052` B1, SHA-256 `aea958f8..`) and no page wrapper imposes a width cap or a centred margin.
- **Theme and language are one shared state** (rules 7, 10): the pre-paint resolver runs before the first stylesheet
  on every page; `sza-lang` holds `ru`, `en` or `ua` only and is written by the language control of every page
  (a small inline script on each content page, ticket `S0053` C2); the control is a row of endonym plus code, with
  the trust pages carrying it too.
- **Every third-party origin is declared and named on the privacy page** (rule 14). Declared today:
  `api.github.com` only (the landing, the release link; named in the privacy page's "This website" section in all 13
  languages). The typefaces Outfit and Plus Jakarta Sans are served from `docs/assets/fonts` (SIL OFL, notice in
  `THIRD-PARTY-NOTICES.txt`), so no font host is contacted. No advertising, no analytics. A new origin is declared
  here and on all 13 privacy pages in the same change (ticket `S0053` C1).
- **Keyboard, landmarks, contrast, alt text, reduced motion** (rules 15 to 19): one `main`, one `h1`, a skip link,
  `lang` equal to the page's language and `dir` on `ar` and `ur`; the kit's global `:focus-visible` ring is never
  overridden; motion stops under the preference. `S0052` B1-B7 are implemented and measured on the local tree.
- **Layout is in the stylesheet** (rule 4): no `style` attribute on a page.

Guard: `tools/site/Test-SiteConformance.ps1` (pins the kit by hash), `tools/site/Measure-SiteContracts.ps1`
(operator-typed, rendered in headless Chrome).

Verified locally on 2026-10-07 (S0052): strict measurement over 44 files / 41 content pages exits 0;
rules 1, 4, 5, 7, 15-20 read `ok`. Nine image alternatives were checked against the actual captures;
each guide has six warnings before the instructions they concern, plus tip and outcome callouts.
The conformance gate also guards landmarks, skip links, headings, locale attributes and inline styles.
Publication of the refreshed assets, a real Tab walk, RTL visual review and the backdrop classification
remain operator checks; local measurements do not certify the published host.

## Synchronization read - 2026-10-18

The [current clause-to-surface audit](../design/contract-sync-20261017.md) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The [shared UI profile](../design/unified-ui.md) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.
