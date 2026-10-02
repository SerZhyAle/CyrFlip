# Pointer - `APP-BEHAVIOUR`

| | |
| --- | --- |
| **Id** | `APP-BEHAVIOUR` |
| **Version** | 0.12, draft |
| **Home** | `desktop-app-ux/APP-BEHAVIOUR.md` in the shared contracts catalog |
| **Role here** | consumer - CyrFlip and its launcher module; **conformant, with one open owner decision** (rule 5, row deletes) |

## What this repository must do to stay conformant

- **Every dialog is content-sized** with `AcceptButton`/`CancelButton`, and covered by `DialogLayoutTests`
  in all 13 languages (rules 1, 2). Never lay a dialog out by pixel coordinates. Every message and
  confirmation is `ConfirmDialog`, whose safe answer is the cancel role and, in a destructive question, the
  default - Enter declines, the acting button is painted `danger` (rule 5, 0.11).
- **A long operation has a Cancel button**, not a wait cursor (rule 3).
- **Nothing touches the network** unless a feature switch is on and the user acted (rule 4) - the
  translator and the Ollama installer are the only two paths.
- **Confirm the irreversible, inform on the empty** (rule 5): an empty history or notepad answers with a
  message, never a question.
- **No raw `ex.Message` to the user** (rule 6): cause, action and "send logs" - gated by
  `NoExceptionTextReachesTheUser`.
- **A translated template is formatted by `Localization.Format`, never by `string.Format`** (rule 7): a
  broken placeholder in one language shows the source sentence instead of throwing inside an event handler -
  gated by `NoTranslatedTemplateIsFormattedRaw` and `EveryTranslationKeepsThePlaceholdersOfItsKey`.
- **Direction comes from `Localization.IsRightToLeft` only**, menus included (rule 8, `AppBehaviourGateTests`).
- **Glyph-only buttons carry an `AccessibleName`** (rule 9).
- **Window geometry is saved at the end of a gesture** and clamped to a live display (rule 10,
  `ScreenPlacement`).
- **Every outward feature is opt-in** (rule 11); settings apply live and hold only reversible value edits,
  anything irreversible keeps its own button and its confirmation (rule 12, `APP-SETTINGS`).

The tray application's tool windows (settings, history, notes, search) and the translation popup are the
companion-surface class of rule 1 (0.11): not modal, close box = Escape = hide, state kept, the popup never
takes the focus and is dismissed by a key the hook watches.

Open: whether deleting a row of the conversion or translation table needs the rule 5 confirmation (owner
decision D1; the layout removal, the scenario delete and the orphan language hotkey are confirmed). The plan is
in `PLAN/` (local, not published); the deviations belong in the catalog's registry as dated exceptions.
