# Pointer - `ICON-RENDER`

| | |
| --- | --- |
| **Id** | `ICON-RENDER` |
| **Version** | 0.15, draft |
| **Home** | `iconography/README.md` in the shared contracts catalog, sections 3 and 10 |
| **Role here** | consumer - conformant, with one dated exception for the touch half of the target floor |

## What this repository must do to stay conformant

- **Glyphs come from `Glyphs.g.cs` through `GlyphRenderer`**, never with baked colours: the colour is the
  caller's theme colour at draw time. Monochrome on tabs, menus and buttons; decorated (a plate in the
  accent) only on Jump List tasks (`LauncherShortcutIcons`). The renderer reads the SVG subset of rule 1:
  every path command, and `fill-rule` (`nonzero` or `evenodd`) - the generator refuses any other rendering
  attribute rather than draw the glyph wrongly.
- **Every glyph-only control carries an accessible name** (rule 8, `AccessibleName` in WinForms): the order
  buttons of the layout list, the launcher list and the notes window, and the owner-painted history strip by
  an `AccessibleObject` exposing its four actions (`AppBehaviourGateTests`).
- **`Glyphs.g.cs` is generated, never hand-edited**: `dotnet run --project tools/IconGen -- glyphs
  -CatalogRoot <catalog>`, from the id list in `tools/IconGen/glyph-ids.txt`.
- **A glyph-only target is at least 28 logical px under a mouse** (rule 5): the history strip's zones are 30
  to 48 px, the notes arrows 34 x 30, and the settings order buttons are held to `SettingsForm.MinTarget`
  (28). CyrFlip does not tell touch input from a mouse, so the 44 px half of the rule - "a product that cannot
  tell sizes to 44 px" - is the one dated exception.
- **The layout badge is out** - `LAYOUT-PALETTE` draws it; the tray icon is the full-colour live badge (rule 9).

The plan is in `PLAN/` (local, not published).
