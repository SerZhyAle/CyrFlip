# Pointer - `PAGE-STYLE`

| | |
| --- | --- |
| **Id** | `PAGE-STYLE` |
| **Version** | 1.6, active |
| **Home** | `product-web-pages/PAGE-STYLE.md` in the shared contracts catalog, with `reference/sza-kit.css` |
| **Role here** | consumer, role *App - small* - the vendored kit and the page scripts |

## What this repository must do to stay conformant

- **`docs/assets/sza-kit.css` is a byte copy of the reference and is served as one.** It is marked `-text`
  in `.gitattributes`, so no line-ending conversion touches it; it is never edited - local rules go in
  `docs/style.css`.
- **`sza-lang` holds only `ru`, `en` or `ua`.** The key is shared by every page on the
  `serzhyale.github.io` origin: a reader maps `uk` to `ua` and sends any other value to the
  `navigator.language` resolution, and a writer never stores anything else.
- **The pre-paint theme resolver runs before any stylesheet**, RU/EN/UA come first on every page, no
  emoji, monochrome marks only.

Held across all 40 rendered HTML pages.


## Conformance state

Read at 1.6 on 2026-10-07 (ticket S0050); refreshed by S0052 the same day. **The vendored kit matches the
current reference byte for byte:** 14521 bytes, SHA-256 `aea958f8..`, MD5 `e3075195..`, with `-text` retained.
The site uses its full-width gutters; the local conformance gate pins both hashes. The catalog states that a kit revision carrying the 1.4
targets (`.btn`, `.seg button`, `.theme-btn`, `.to-top` at 44px for every pointer), the 1.5 reduced-motion
selector (`*, *::before, *::after`) and the `nav.expand` marker is **still owed and unpublished**: the reference
file still lacks these revisions. The page stylesheet supplies 44px controls, reduced motion on pseudo-elements
and contrast corrections in both themes while retaining the reference bytes (recorded in the registry).
**Not held, in this repository's own pages:** the copy button's done state is the localized word
("Copied", "Kopiert") but is drawn after a check-mark glyph, which 1.4 rules out (the copy glyph, not a status
glyph, stands beside the word) - on the landing and the ten standalone pages; the theme control is the bare
`◐` with no visible label (1.4, 4.3). **1.6 picker:** the guides, privacy pages and trust pages carry a row of
endonym plus code (RU, EN, UA first, the current one `aria-current`) and write `sza-lang` for RU, EN and UA only on a choice
(S0053 item C2). **Held:** `-text` on the kit file, `sza-lang` holds `ru`/`en`/`ua` only, pre-paint resolver, no emoji.
