# Pointer - `ICON-RENDER`

| | |
| --- | --- |
| **Id** | `ICON-RENDER` |
| **Version** | 0.18, draft (read 2026-10-07; 0.16 identity inks, 0.17 the pointer-first declaration, 0.18 the web floor and paging direction) |
| **Home** | `iconography/README.md` in the shared contracts catalog, sections 3 and 10 to 13 |
| **Role here** | consumer - conformant with one dated exception for the touch half of the target floor, **and one open deviation: the Jump List task icons (rule 9, see below)** |

## What this repository must do to stay conformant

- **Glyphs come from `Glyphs.g.cs` through `GlyphRenderer`**, never with baked colours: the colour is the
  caller's theme colour at draw time. Monochrome on tabs, menus and buttons. The renderer reads the SVG subset
  of rule 1: every path command, and `fill-rule` (`nonzero` or `evenodd`) - the generator refuses any other
  rendering attribute rather than draw the glyph wrongly.
- **Every glyph-only control carries an accessible name** (rule 8, `AccessibleName` in WinForms): the order
  buttons of the layout list, the launcher list and the notes window, and the owner-painted history strip by
  an `AccessibleObject` exposing its four actions (`AppBehaviourGateTests`).
- **`Glyphs.g.cs` is generated, never hand-edited**: `dotnet run --project tools/IconGen -- glyphs
  -CatalogRoot <catalog>`, from the id list in `tools/IconGen/glyph-ids.txt`.
- **A glyph-only target is at least 28 logical px under a mouse** (rule 5): the history strip's zones are 30
  to 48 px, the notes arrows 34 x 30, and the settings order buttons are held to `SettingsForm.MinTarget`
  (28). CyrFlip does not detect touch input, so the 44 px half of the rule is the one dated exception; since 0.17
  (section 12 item H) a product that **declares itself pointer-first in its registry row** sizes to 28 without
  detecting touch, which is the declaration the exception is waiting for.
- **The layout badge is out** - `LAYOUT-PALETTE` draws it; the tray icon is the full-colour live badge (rule 9).
- **Navigation identity inks (0.16, section 11)** are a permission; this product does not use them.

## Open deviation (2026-10-07, not yet fixed)

- **Rule 9, Jump List task icons.** The contract puts a Jump List task in the Explorer menu entry's position: the
  shell draws it on a menu the product cannot see, so the task shows its meaning's glyph in the **mono look in one
  tone, `#808080`**, at 16 px with 20, 24 and 32 px forms. `LauncherShortcutIcons` draws the **decorated** look
  instead (the glyph on a round accent plate, sizes 16 to 48). The glyphs themselves are the right records
  (`app.settings`, `nav.exit`, `action.download`, `content.apps`); only the look differs.
