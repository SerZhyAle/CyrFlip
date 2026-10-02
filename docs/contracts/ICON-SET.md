# Pointer - `ICON-SET`

| | |
| --- | --- |
| **Id** | `ICON-SET` |
| **Version** | 0.17, draft (the version `tools/IconGen` read when `Glyphs.g.cs` was generated) |
| **Home** | `iconography/README.md` in the shared contracts catalog, section 2 |
| **Role here** | consumer, contributor by proposal - **conformant except two tab meanings the vocabulary still lacks** |

## What this repository must do to stay conformant

- **Every glyph the app, the extension or the site shows maps to a vocabulary id listed in `Glyphs.g.cs`**,
  spelled once in `AppGlyphs`. A new glyph starts with a record in the vocabulary, never with a private
  drawing.
- **A glyph means what the vocabulary says** - a tab glyph that reads as another meaning is a defect; two
  meanings never share a picture (close is not delete).
- **The layout badge is not a glyph**: it is `LAYOUT-PALETTE`'s and stays out of this contract.
- **A `proposed` record may be shipped** (rule 6): the first product to draw it turns it `active` by
  amendment. Nine of CyrFlip's eleven asked meanings are such records since 0.17, and the app draws five of them.
- **Names follow the record** in all 13 interface languages.

Held: nine of the eleven settings tabs draw a vocabulary glyph - Settings (`app.settings`), Indicators
(`feature.layout-indicator`), Hotkeys (`app.shortcuts`), Clipboard (`feature.clipboard-history`), Quick notes
(`content.note`), Translation (`action.translate`), Quick launch (`feature.quick-launch`), Graphics
(`system.screenshot`) and About (`app.info`); the clipboard strip's four glyphs, the order buttons of the
layout list, the launcher list and the notes window (`action.move-up` / `action.move-down`) are vocabulary
glyphs too, and the guides show a tab's glyph beside its name. The launcher's own page header and taskbar
button keep OneClickRunner's mark (the feature that absorbed it; whether that is allowed is the one open item
of the catalog's decision of 2026-10-02), and the tab is not that mark.

Open: **Conversions** (`action.convert-layout`) and **Languages** (`system.input-language`) keep their own
drawing until the catalog records those two meanings - both are asked for in
`PROPOSAL-2026-09-26-cyrflip-windows-tray-app.md` and still open there. The tray's "Выход" / "Вихід" is a noun
where `nav.exit` says "Выйти" / "Вийти" (a product debt, owner decision O4). The plan is in `PLAN/` (local, not
published).
