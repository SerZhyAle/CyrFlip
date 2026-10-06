# Pointer - `WINDOWS-UI`

| | |
| --- | --- |
| **Id** | `WINDOWS-UI` |
| **Version** | 0.1, draft |
| **Home** | `desktop-app-ux/WINDOWS-UI.md` in the shared contracts catalog |
| **Role here** | consumer - sections 2-7 implemented 2026-10-06 (ticket `S0048`); section 8 does not apply (no content viewer) |

## What this repository does to stay conformant

- The settings window is the profile's surface here: a vertical navigation list (glyph + caption,
  soft accent selected fill, hover fill, accent bar, focus frame, About last), a page title and one
  purpose description, independently collapsible groups (`CollapsibleGroup` - chevron from
  `nav.expand`/`nav.collapse`, expanded state in the accessible object, focus handed to the header
  before a body hides), header commands (find, expand all, collapse all) and a settings search that
  reveals a row (page, group expanded, scrolled into view, focused).
- Context is remembered under stable internal ids, never translated captions: the last page
  (`SettingsTab`), every group's expansion (`SettingsGroupsState`) and each page's viewport as a
  group/row anchor plus a logical-pixel offset (`SettingsViewport`). The window's own rectangle is
  written on hide only and restored clamped to a work area (`SettingsWindowX/Y/Width/Height`).
- Editors follow the row conventions: checkbox at the reading start with the caption beside it and
  the description under the caption's own text inset; numeric editors carry the logical 110 px
  width, a pixel-sized digit font and spin halves at the 28 px pointer floor
  (`SettingsNumeric.Apply`); the mouse wheel over an unfocused editor scrolls the page instead of
  turning it (`SettingsWheelFilter`).
- The three theme modes stay live (`APP-STYLE`); the nav fills and group header bands are
  `ThemePalette.Mix` of existing roles, never literals, so a switch re-mixes them.
- Non-adoptions and open items live in [PLAN/S0048_windows-ui-unification.md](../../PLAN/S0048_windows-ui-unification.md)
  (section 6): no navigation identity inks, flat pages not re-grouped, the owner-drawn page list's
  missing UIA `TabItem`s, the standing touch-target exception.

The plan is in `PLAN/` (local, not published).
