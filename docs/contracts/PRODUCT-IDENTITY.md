# Pointer - `PRODUCT-IDENTITY`

| | |
| --- | --- |
| **Id** | `PRODUCT-IDENTITY` |
| **Version** | 0.3, draft (read 2026-11-20, ticket CYRFLP-0139) |
| **Home** | `product-identity/README.md` in the shared contracts catalog; the checker is its `reference/Test-ProductIdentity.ps1` |
| **Role here** | Consumer, `partial 0.3` - rungs 3, 4 (in the tree), 6 and 7 held by code; rungs 1 and 2 wait for the generator run at the next release |

## What this repository holds

- **One master, drawn as code (rule 33).** `tools/IconGen` (`IconVariants.ShapeA`, the S0058 variant A) paints the
  mark; the same run writes `assets/identity/master/mark.svg` and the hand-hinted reduced forms `mark-16/24/32.svg`
  (rule 34). Nothing under `assets/identity/` is edited by hand.
- **One generator command:** `dotnet run --project tools/IconGen`. It writes every rendition (the exe ICO, the MSIX
  logos in `assets/identity/msix/`, the Store logos, the VS Code icon, the README banner, the social preview and the
  site's `social-card.png`, the favicons, the help set's `product-mark.svg` and `favicon.ico`) and then
  `assets/identity/identity-manifest.json`. `tools/site/apply-identity.py` points the site's identity block and the
  help pages at them.
- **Surfaces.** `msix/build-msix.ps1` copies the four logos and never resamples; `og:image` follows
  `tools/site/sync-doc-quality.py` (the 1200 x 630 card once it exists); every site page's brand carries
  `site.identity`; every help page carries `help.head` before its title and the favicon (`CONTEXT-HELP` 0.6 item Q,
  held by `tools/help/Test-ContextHelp.ps1`), and an element entry shows its button's `ICON-SET` glyph (item R,
  `HelpElements.GlyphSvg`).
- **Not the mark:** the tray icon is the live layout indicator (`LAYOUT-PALETTE`), so there is no `app.tray` row; the
  winget manifests carry no icon. Diagrams: none in README, docs, help or site (rung 7 holds by inventory).

## Check

`pwsh -NoProfile -File <catalog>/product-identity/reference/Test-ProductIdentity.ps1 -RepoRoot .` exits 0, and
`-Sheet temp/<ticket>/sheet.html` shows every rendition side by side.
