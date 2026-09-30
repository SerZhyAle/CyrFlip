# Pointer - `ICON-RENDER`

| | |
| --- | --- |
| **Id** | `ICON-RENDER` |
| **Version** | 0.13, draft |
| **Home** | `iconography/README.md` in the shared contracts catalog, sections 3 and 10 |
| **Role here** | consumer - **partly conformant** |

## What this repository must do to stay conformant

- **Glyphs come from `Glyphs.g.cs` through `GlyphRenderer`**, never with baked colours: the colour is the
  caller's theme colour at draw time. Monochrome on tabs, menus and buttons; decorated (a plate in the
  accent) only on Jump List tasks (`LauncherShortcutIcons`).
- **Every glyph-only control carries an accessible name** - the notes window's move buttons by
  `AccessibleName`, the owner-drawn history strip by an `AccessibleObject` exposing its four actions.
- **`Glyphs.g.cs` is generated, never hand-edited**: `dotnet run --project tools/IconGen -- glyphs
  -CatalogRoot <catalog>`, from the id list in `tools/IconGen/glyph-ids.txt`.
- **The layout badge is out** - `LAYOUT-PALETTE` draws it.

Open: the 44 px target rule is not applied to pointer-first Windows surfaces (proposed in the catalog),
and the settings row arrows are still text. Plan in `PLAN/` (local, not published).
