# Pointer - `APP-SETTINGS`

| | |
| --- | --- |
| **Id** | `APP-SETTINGS` |
| **Version** | 0.4, draft |
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

## Section 9 (0.3) - an irreversible operation on a page

CyrFlip's settings window offers no Commit (every value applies as it is touched), so section 9 is the
rule its operations stand under: each irreversible operation is **a dedicated action button whose caption
names what it does, with `APP-BEHAVIOUR` rule 5's confirmation, and never a value control**. Held in the
tree by reading the handlers (a live walk of the buttons is owed to the next S0048-style pass): "Очистить всю
историю", "Удалить все заметки", the layout "Удалить" and the two "Вернуть .. в исходное состояние" restores,
the scenario delete and the table-row deletes all sit on their own `Button` and ask through `ConfirmDialog`
with `danger: true` (the safe answer is the default, Escape and the close box decline); an operation with
nothing to do - an empty history, no notes - says so with an information box and does not ask. No value
control (check box, drop-down, slider, group expansion) starts one. Nothing here needed a code change.

S0055: the welcome tour links the one settings instance through `SettingsPageKey`. Requests during the modal tour are queued and delivered once after it closes; the completion button selects General. About offers replay and the settings search indexes that button.

Section 10 (0.4) records the owner’s private settings-transfer practice. S0055 settings-file uses action buttons, a safe-default preview and a pre-import snapshot; no wire contract is introduced.

Section 11 records S0072's private persistence and complete-transfer practice. The v2 allow-list,
scenario snapshots, Documents rolling backup and restore preview implement it; the v1 reader remains.
The automated gates cover format and persistence. Both distribution channels still need the owner-run
G6 reinstall evidence before this ticket is Verified.
