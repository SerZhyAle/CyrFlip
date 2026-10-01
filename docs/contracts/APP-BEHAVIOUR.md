# Pointer - `APP-BEHAVIOUR`

| | |
| --- | --- |
| **Id** | `APP-BEHAVIOUR` |
| **Version** | 0.10, draft |
| **Home** | `desktop-app-ux/APP-BEHAVIOUR.md` in the shared contracts catalog |
| **Role here** | consumer - CyrFlip and its launcher module; **partly conformant** |

## What this repository must do to stay conformant

- **Every dialog is content-sized** with `AcceptButton`/`CancelButton`, and covered by `DialogLayoutTests`
  in all 13 languages (rules 1, 2). Never lay a dialog out by pixel coordinates.
- **A long operation has a Cancel button**, not a wait cursor (rule 3).
- **Nothing touches the network** unless a feature switch is on and the user acted (rule 4) - the
  translator and the Ollama installer are the only two paths.
- **Confirm the irreversible, inform on the empty** (rule 5).
- **No raw `ex.Message` to the user** (rule 6): cause, action and "send logs".
- **Translated templates go through `Localization`** (rule 7); direction comes from
  `Localization.IsRightToLeft` only, menus included (rule 8).
- **Glyph-only buttons carry an `AccessibleName`** (rule 9).
- **Window geometry is saved at the end of a gesture** and clamped to a live display (rule 10).
- **Every outward feature is opt-in** (rule 11); settings apply live (rule 12).

The four tool windows (settings, history, notes, search) and the translation popup are the tray-app
exemption class of rule 1 - hide on close, never steal the focus - asked as
`PROPOSAL-2026-10-01-cyrflip-tray-app-tool-windows.md` in the catalog; rule 12's live-apply model is
the shape `PROPOSAL-2026-09-28-autosave-settings-surface.md` asks rule 12 to permit. The audit and the
plan are in `PLAN/` (local, not published); the deviations belong in the catalog's registry as dated
exceptions.
