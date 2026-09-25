# Pointer - `ICON-RENDER`

| | |
| --- | --- |
| **Id** | `ICON-RENDER` |
| **Version** | 0.10, draft |
| **Home** | `iconography/README.md` in the shared contracts catalog, sections 3 and 10 |
| **Role here** | consumer - **not yet conformant** |

## What this repository must do to stay conformant

- **Glyphs come from the vendored set through one renderer**, never with baked colours; monochrome on
  tabs, menus and buttons, decorated only where the contract allows (Jump List tasks).
- **Every glyph-only control carries an accessible name.**
- **Generated glyph sources are never hand-edited**; they are regenerated (a net48 pipeline through
  `tools/IconGen` is the planned route).
- **The layout badge is out** - `LAYOUT-PALETTE` draws it.

Plan in `PLAN/` (local, not published).