# Pointer - `APP-STYLE`

| | |
| --- | --- |
| **Id** | `APP-STYLE` |
| **Version** | 0.9, draft |
| **Home** | `desktop-app-ux/APP-STYLE.md` in the shared contracts catalog |
| **Role here** | consumer - **not yet implemented**: CyrFlip has no theme of its own today |

## What this repository must do to stay conformant

- **When the theme lands**, it uses the contract's role names, the modes `system`, `light` and `dark`, and
  no colour literal outside the one palette file.
- **The layout marker is out of theme.** Its colours are `LAYOUT-PALETTE`'s and never follow the window
  theme: the marker names a layout, and a theme that recoloured it would make it name something else.

Today the windows draw with the WinForms system colours. The planned dark theme is to be based on this
contract's role names before any code is written; the plan is in `PLAN/` (local, not published).
