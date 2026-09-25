# Pointer - `APP-STYLE`

| | |
| --- | --- |
| **Id** | `APP-STYLE` |
| **Version** | 0.10, draft |
| **Home** | `desktop-app-ux/APP-STYLE.md` in the shared contracts catalog |
| **Role here** | consumer - sections 2-5 implemented in the working tree (the app theme), **not yet in a release** |

## What this repository does to stay conformant

- **Section 2 - three themes, no restart.** The General tab offers "Как в Windows" / "Светлая" / "Тёмная",
  stored as the invariant token `Theme` = `system|light|dark` (default `system`). A change applies at once,
  and `system` follows Windows' `AppsUseLightTheme` while the app runs (`ThemeManager`).
- **Section 3 - one palette table, only dynamic references.** Every chrome colour is a role in
  `ThemePalette.cs`; WinForms has no resource scope, so the rule is held by a source gate (no colour
  literal outside that file and the layout marker's files - `ThemeSourceGateTests`) plus a re-apply walk
  over every open window on a change (`ThemeApply`, `ThemeCoverageTests`).
- **Section 4 - the role vocabulary.** The role names are the contract's, plus the proposed `warning`,
  `danger` and `success` - `danger` is on the destructive answer of the app's own confirmation dialog - and
  one CyrFlip proposal, `surface.alternate` (the alternate list row). Contrast is measured per text/surface
  pair (`ThemePaletteTests`, 4.5:1; 3:1 for disabled text).
- **Section 5 - declared out of theme.** The layout marker: its colours are `LAYOUT-PALETTE`'s and never
  follow the window theme - the marker names a layout. Declared at `LayoutStyle.cs` and `CaretOverlay.cs`.
- **Section 8, answered here rather than left open:** Windows' high contrast wins over every mode (system
  colours only), and the title bar follows the theme.

The plan and its decisions are in `PLAN/` (local, not published). The catalog registry row is updated by
the contract sync, not from here.
