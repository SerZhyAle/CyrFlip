# Pointer - `ICON-SET`

| | |
| --- | --- |
| **Id** | `ICON-SET` |
| **Version** | 0.16, draft (the version `tools/IconGen` read when `Glyphs.g.cs` was generated) |
| **Home** | `iconography/README.md` in the shared contracts catalog, section 2 |
| **Role here** | consumer, contributor by proposal - **partly conformant** |

## What this repository must do to stay conformant

- **Every glyph the app, the extension or the site shows maps to a vocabulary id listed in `Glyphs.g.cs`**,
  spelled once in `AppGlyphs`. A new glyph starts with a record in the vocabulary, never with a private
  drawing.
- **A glyph means what the vocabulary says** - a tab glyph that reads as another meaning is a defect; two
  meanings never share a picture (close is not delete).
- **The layout badge is not a glyph**: it is `LAYOUT-PALETTE`'s and stays out of this contract.
- **Names follow the record** in all 13 interface languages.

Held: the clipboard strip's four glyphs, the notes window's move buttons and the Settings, About and
Translate tabs are vocabulary glyphs. Open: eight tabs and the launcher's mark still draw private
pictures for meanings that have no record yet (proposed in the catalog by CyrFlip, 2026-09-26). The plan
is in `PLAN/` (local, not published).
