# Pointer - `ICON-SET`

| | |
| --- | --- |
| **Id** | `ICON-SET` |
| **Version** | 0.30, draft (the version `tools/IconGen` read when `Glyphs.g.cs` was generated) |
| **Home** | `iconography/README.md` in the shared contracts catalog, section 2 |
| **Role here** | consumer, contributor by proposal - **conformant on every surface the app draws** |

## What this repository must do to stay conformant

- **Every glyph the app, the extension or the site shows maps to a vocabulary id listed in `Glyphs.g.cs`**,
  spelled once in `AppGlyphs`. A new glyph starts with a record in the vocabulary, never with a private
  drawing.
- **A glyph means what the vocabulary says** - a tab glyph that reads as another meaning is a defect; two
  meanings never share a picture (close is not delete).
- **The layout badge is not a glyph**: it is `LAYOUT-PALETTE`'s and stays out of this contract.
- **A `proposed` record may be shipped** (rule 6): the first product to draw it turns it `active` by
  amendment. Twelve of CyrFlip's asked meanings are such records (nine of them since 0.17, `action.convert-layout` and `system.input-language` since 0.23, `feature.selection-menu` asked 2026-10-11), and the app draws eight of them.
- **Names follow the record** in all 13 interface languages.

Held: all thirteen settings tabs draw a vocabulary glyph - Settings (`app.settings`), Indicators
(`feature.layout-indicator`), Hotkeys (`app.shortcuts`), Conversions (`action.convert-layout`), Windows languages
(`system.input-language`), the selection menu (`feature.selection-menu`, asked 2026-10-11; the tab drew its
caption alone until the record existed - S0062 D7), Symbols (`feature.symbols`), Clipboard
(`feature.clipboard-history`), Quick notes (`content.note`), Translation
(`action.translate`), Quick launch (`feature.quick-launch`), Graphics (`system.screenshot`) and About
(`app.info`); no private tab drawing is left in the code. The clipboard strip's four glyphs, the order buttons of
the layout list, the launcher list and the notes window (`action.move-up` / `action.move-down`) are vocabulary
glyphs too, and the guides show a tab's glyph beside its name.

**Declared (decision of 2026-10-07, item 2, option (a)):** the launcher's page header and CyrFlip's own taskbar
button carry OneClickRunner's mark - the absorbed product's identity artwork, kept deliberately
(`LauncherBrand`); the launcher's tab, menu and settings-row glyph is `feature.quick-launch`, never that mark.

Decided 2026-10-07: the tray's exit item, the quick-notes checklist kind and the link-opening item use the
records' names ("Выйти" / "Вийти" after `nav.exit`, "Чек-лист" after `content.checklist`, "Открыть" - the other
twelve languages already said "Open"), and the READMEs and the Store listing stay text-only. `action.fix-case`,
`feature.keep-awake`, `action.cut` and `content.checklist` are recorded but no surface of the app draws them. The
plan is in `PLAN/` (local, not published).

## The extension and the site (read 2026-10-07 against 0.28)

- **OneClickRunner's mark (0.23, rule 7)** is drawn in exactly two places, both entry points of the feature it came
  with: the launcher page header (`SettingsForm`, `LauncherBrand.GetImage`) and CyrFlip's own taskbar button
  (`LauncherTaskbarWindow`, `LauncherBrand.GetIcon`). It is declared in the registry row; the launcher's tab, menu
  and settings-row glyph is `feature.quick-launch`.
- **VS Code extension:** its one control glyph is the host's `$(keyboard)` codicon on the status-bar item
  (`vscode-extension/src/extension.ts`), the picture `feature.layout-indicator` names ("keyboard"); a status-bar item
  can carry no drawing of ours. The marker at the caret is `LAYOUT-PALETTE`, not a glyph. `icon.png` is the
  product mark (rule 7). Not checked: whether the tooltip's wording ("CyrFlip - active keyboard layout") is a
  qualification of the record's name or a substitute for it (rule 3).
- **Website:** the guides show the vocabulary glyph beside each tab's name (rule 8). Two kit affordances still
  differ from the vocabulary and wait for the kit refresh of ticket S0052: the copy button's done state is the word
  "Copied" beside an `action.confirm` check (`docs/index.html`, `var CHECK`, 11 pages) where the `action.copy`
  record says the word goes beside the copy glyph and the check "never stands for it", and the theme control is the
  bare text glyph `◐` (40 pages) where `app.theme` is the meaning and `PAGE-STYLE` 1.4 asks for a labelled control.
  The native `<details>` marker is outside the vocabulary (0.22).
