# Pointer - `PAGE-STYLE`

| | |
| --- | --- |
| **Id** | `PAGE-STYLE` |
| **Version** | 1.0, active |
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

Not yet held on the secondary pages (guides, privacy, standalone locales): resolver order, the colour mark
in the header, expand/collapse-all, the copy fallback. Plan in `PLAN/` (local, not published).