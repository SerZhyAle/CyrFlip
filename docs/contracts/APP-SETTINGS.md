# Pointer - `APP-SETTINGS`

| | |
| --- | --- |
| **Id** | `APP-SETTINGS` |
| **Version** | 0.1 draft |
| **Home** | `desktop-app-ux/APP-SETTINGS.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and reference implementation - the persistent settings surface |
| **Adopted** | 2026-10-02, rung 1 (file and line pins) owed, rungs 3-4 to be scheduled by owner |

## State

CyrFlip filed the founding proposal (`PROPOSAL-2026-10-01-cyrflip-found-app-settings.md`, drafted from the
settings window of tickets S0019 and S0020), and the portfolio owner founded the contract on 2026-10-02 with
CyrFlip as its owner and reference implementation. The twelve rules live only in the catalog - a pointer that
lists rules is a copy. The commit model is `APP-BEHAVIOUR` rule 12 (settings apply as they are touched and
hold only reversible edits); the language selector shows endonyms, never a flag (`ICON-EXTERNAL` rule 6);
the hit-target floor is `ICON-RENDER` section 3 rule 5.

## What this repository must do

Keep the settings window the reference the other products read themselves against: one navigation list with
About last, a caption / control / hint block per setting, Escape hides the window, an accessible name on every
glyph-only control, content-measured layout in all 13 languages and in right-to-left, the theme and the
language switched live. `DialogLayoutTests`, `ThemeCoverageTests`, `SettingsLocalizationTests` and
`AppBehaviourGateTests` hold the parts that can be held by a test.

Owed to the contract: rung 1 of its section 5 - the surface read against the twelve rules with the file and
line written into the registry row, as pins and not as a claim. Until then the row stays `pending`.
