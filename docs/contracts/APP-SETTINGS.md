# Pointer - `APP-SETTINGS`

| | |
| --- | --- |
| **Id** | `APP-SETTINGS` |
| **Version** | 0.1, draft |
| **Home** | `desktop-app-ux/APP-SETTINGS.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and reference implementation - the persistent settings surface |

## What this repository must keep true

- **Rule 1 - One instance, found where the user is.** Single instance; opens on the active monitor and restores the last selected page (`_config.SettingsTab`).
- **Rule 2 - Navigation.** Vertical page list with icons and measured captions; unselected and selected tabs rendered cleanly across all 13 scripts; "About" is the last page.
- **Rule 3 - Page anatomy.** Short description followed by settings blocks; each setting has a caption, control, and muted hint line.
- **Rule 4 - Live-apply commit model.** Changes apply immediately without Save/Cancel; irreversible operations are confirmed individually via `ConfirmDialog` with `danger` role.
- **Rule 5 - Language selector on first page.** 13 languages with endonyms; live switch across all windows and menus without restart; right-to-left layout for Arabic and Urdu.
- **Rule 6 - Theme selector on first page.** Three modes (`system`, `light`, `dark`); dynamic palette references; high contrast wins over custom themes.
- **Rule 7 - Scaling & per-monitor DPI.** Font-derived measurements, no clipped captions or dropdowns across 100%, 150%, 175%, 200% DPI scales.
- **Rule 8 - Compactness.** Sized to content with defined minimums and margins.
- **Rule 9 - OS cooperation.** System fonts with script fallbacks; dark title bar via DWM; Escape key closes/hides the window; deep links to Windows Settings (`ms-settings:keyboard`).
- **Rule 10 - Accessibility.** Tab navigation, focus rectangles on keyboard focus, and `AccessibleName` on glyph-only controls.
- **Rule 11 - Documentation.** Every setting inventoried and kept aligned with user documentation and guides.
- **Rule 12 - About page.** Version line, copy details, support bundle log diagnostic exporter, and privacy policy link.
