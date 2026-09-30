# Pointer - `INPUT-PARITY`

| | |
| --- | --- |
| **Id** | `INPUT-PARITY` |
| **Version** | 0.2, draft |
| **Home** | `input-controls/README.md` in the shared contracts catalog (section 3 the actions, section 4 the device table) |
| **Role here** | Producer; steward of the **keyboard** and **mouse** columns. The other columns belong to other products |

## What this repository must keep true

- **Rule 1** - every action reachable with the mouse is reachable from the keyboard (tray menu, text context
  menu, settings, quick notes, history). Walked in the code by S0044 A3; the gaps it found were closed by
  S0045 (`F6` into the translation popup, a chord that opens the text menu at the caret, the history strip's
  keyboard cursor, the region capture's arrows). The pure parts are held by `KeyboardParityTests`; a live
  keyboard-only walk is still owed.
- **Rule 2** - the focus stays visible after keyboard navigation.
- **Rule 3** - the Windows key alone is never bound.
- The keyboard and mouse cells of the section 4 table describe what CyrFlip does; changing that behaviour
  changes the column first. The shared page is edited directly by its column steward, with a log row.
