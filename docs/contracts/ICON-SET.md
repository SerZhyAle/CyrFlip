# Pointer - `ICON-SET`

| | |
| --- | --- |
| **Id** | `ICON-SET` |
| **Version** | 0.10, draft |
| **Home** | `iconography/README.md` in the shared contracts catalog, section 2 |
| **Role here** | consumer, contributor by proposal - **not yet conformant** |

## What this repository must do to stay conformant

- **Every glyph the app, the extension or the site shows maps to a vocabulary id.** A new glyph starts
  with a record in the vocabulary, never with a private drawing.
- **A glyph means what the vocabulary says** - a tab glyph that reads as another meaning is a defect.
- **The layout badge is not a glyph**: it is `LAYOUT-PALETTE`'s and stays out of this contract.
- **Names follow the record** in all 13 interface languages.

Today the settings tab strip and menus draw hand-made glyphs that do not map to the vocabulary. The audit
and the plan are in `PLAN/` (local, not published), to be done together with the dark theme.