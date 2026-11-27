---
paths:
  - "src/CyrFlip/Symbol*.cs"
  - "tools/IconGen/SymbolVendoring.cs"
  - "tools/symbols/*"
---

# Symbols window

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

12. **The symbols window - a palette of characters that are on no installed layout (ticket S0056), opt-in and off by
   default.** `EnableSymbols=0` means no chord is bound, no tray item, no context-menu item and **no window object
   exists**. Enabled, `SymbolsHotkey` (**Ctrl+Shift+F9**) toggles `SymbolsWindow`: a `SizableToolWindow` that is
   shown without activation and whose grid answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, so a click on a character
   **never takes the focus from the user's editor** - the one hard rule, because a palette that did would make
   Ctrl+V paste into the wrong place. Keyboard focus lives only in the search box and the text field, and is handed
   back after a copy (`ForegroundActivator`, `CursorIndicator.LastActiveWindow`). The window has a group list ("Все" and the twelve groups of the table, the last one remembered in `SymbolsCategory`; a search stays inside the chosen group), a
   text field above it that collects the picks and mirrors the clipboard (`SymbolsClipboardMirror` decides which read
   may replace it), a 12-column grid that scrolls, a hover that shows the code (`U+20AC`), and a remembered switch
   **"Закрыть по выбору"**: off, a pick appends the character to the field and copies the whole field; on, a pick
   copies the character alone and closes the window (ticket S0056, revision 2026-10-08). A pick copies **exactly** the
   character (`Win32Clipboard.TrySetText`, `TransientMarks.None`, on the clipboard worker under the `_busy` guard; a
   taken guard says so on the window's own status line, never a balloon); symbol picks are excluded from CyrFlip clipboard history. The data is
   a **generated** table of 501 characters (`Symbols.g.cs`, from `tools/symbols/symbols.src` + `UnicodeData.txt`
   by `tools/IconGen -- symbols`, notice in `THIRD-PARTY-NOTICES.txt`); search is by the character, a code point, a
   prefix of the English or Russian name or the category, **no relevance score**. The Russian name is shown only in
   the Russian UI. `SymbolsHideTypable` hides what an installed layout types with one key combination
   (`SymbolTypability`, computed on the pool). It is declared a tab with **no vocabulary glyph** (`ICON-SET` rule 7,
   see `TabGlyphTests`). The machine check (every entry drawable by some installed font) and the clipboard write
   are `tools/uitest/Test-Symbols.ps1`; the chord, the focus rule in real editors and the handle counts are the
   human checklist at its end. Spec: [PLAN/done/S0056_symbols-window.md](../../PLAN/done/S0056_symbols-window.md).

## Modules (`src/CyrFlip/`)

- **SymbolsData.cs / Symbols.g.cs / SymbolSearch.cs / SymbolsClipboardMirror.cs / SymbolTypability.cs / SymbolGrid.cs /
  SymbolsWindow.cs / Localization.Symbols.cs** - the symbols window (feature 12), one concern each: the entry type and
  the 12 categories beside the **generated** table (never edited by hand); the pure search (character / code point /
  name prefix / category, standard order); the clipboard mirror's rule (`SymbolsClipboardMirror`: a read replaces the
  text field only when it is newer than what the field shows, so a late read cannot undo a pick); the per-layout "can I
  already type this" answer behind a seam over the installed layouts, cached per layout set; the owner-drawn grid
  (`TextRenderer`, `ThemePalette` roles only, twelve columns, one accessible child per visible cell, combining marks on
  `◌`, blank characters as short labels) and the window - the only part that knows the focus rule and the geometry.
