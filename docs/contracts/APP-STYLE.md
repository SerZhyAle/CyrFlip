# Pointer - `APP-STYLE`

| | |
| --- | --- |
| **Id** | `APP-STYLE` |
| **Version** | 0.12, draft (read 2026-10-07; 0.12 adds section 10 - complete live theme coverage and the Windows profile; the sections 2-5 below were re-verified against it by S0048 on 2026-10-06) |
| **Home** | `desktop-app-ux/APP-STYLE.md` in the shared contracts catalog |
| **Role here** | consumer - sections 2-5 implemented and shipped (the app theme, since v26.10.1) |

## What this repository does to stay conformant

- **Section 2 - three themes, no restart.** The General tab offers "Как в Windows" / "Светлая" / "Тёмная",
  stored as the invariant token `Theme` = `system|light|dark` (default `system`). A change applies at once,
  and `system` follows Windows' `AppsUseLightTheme` while the app runs (`ThemeManager`).
- **Section 3 - one palette table, only dynamic references.** Every chrome colour is a role in
  `ThemePalette.cs`; WinForms has no resource scope, so the rule is held by a source gate (no colour
  literal outside that file and the layout marker's files - `ThemeSourceGateTests`) plus a re-apply walk
  over every open window on a change (`ThemeApply`, `ThemeCoverageTests`).
- **Section 4 - the role vocabulary.** The role names are the contract's, including `danger`, `warning` and
  `success` (promoted to contract in 0.11) and `surface.alternate` (added in 0.11 from CyrFlip's own
  proposal). `danger` is on the destructive answer of the app's own confirmation dialog, whose safe answer
  holds the default focus. Contrast is measured per text/surface pair (`ThemePaletteTests`, 4.5:1; 3:1 for
  disabled text). Five more roles are **proposed** and CyrFlip-only until the owner answers
  (`PROPOSAL-2026-10-13-cyrflip-status-fill-roles.md`, ticket S0069): `status.ok.fill`, `status.warning.fill`,
  `status.error.fill`, `status.unknown.fill` and `status.ink`, the fills of the traffic-light board's tiles and
  the ink drawn on them - **fills, not the `success` / `warning` / `danger` ink roles**, which are text colours.
  The state is never colour alone: each tile also carries its `status.*` vocabulary glyph
  (`TrafficLightPaletteTests` gates the pairs; high contrast draws the tile in system colours).
- **Section 5 - declared out of theme.** The layout marker: its colours are `LAYOUT-PALETTE`'s and never
  follow the window theme - the marker names a layout. Declared at `LayoutStyle.cs` and `CaretOverlay.cs`.
- **Section 10 (0.12) - complete live coverage.** Nested children, native internal editors, popups, menus and
  secondary dialogs take the palette (`ThemeApply`, `ThemedForm`, `ThemeToolStripRenderer`); a new control or a
  recreated handle is themed on arrival and a hidden window catches up before it is shown; repeated
  Light -> Dark -> Light leaves nothing behind (`ThemeCoverageTests` - light after dark restores every property;
  `tools/uitest/Test-DarkTheme.ps1 -FollowWindows` live). Disabled captions are repainted in `text.disabled`.
  The navigation identity inks of section 10 are a permission, not a duty, and this product does not use them.
- **Section 8, answered here rather than left open:** Windows' high contrast wins over every mode (system
  colours only), and the title bar follows the theme.

The Windows profile that section 10 introduces is [WINDOWS-UI](WINDOWS-UI.md). The plan and its decisions are in
`PLAN/` (local, not published). The catalog registry row is updated by the contract sync, not from here.
