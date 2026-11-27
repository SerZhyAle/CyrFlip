---
paths:
  - "src/CyrFlip/Localization*.cs"
  - "tools/l10n/*.cs"
  - "tools/l10n/*.ps1"
---

# Localization layer (13 UI languages)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Modules (`src/CyrFlip/`)

- **Localization.cs (+ `Localization.*.cs` partials)** - the **single localization layer** for the whole
  app: settings window, tray menu and every dialog read from it, so adding a language is one column
  instead of a ternary in each file (the old `ru ? ... : uk ? ...` pattern is gone). The **key is the
  English source string** (ticket S0070: code is English, English is the source language, Russian is a
  translation like Ukrainian) - call sites stay readable and no resource ids are needed - and the value
  is one translation per language, `ru:` and `uk:` being columns like the others. A row is `Add("Recording",
  ru: "Запись", uk: ..., zh: ...)` and `Values` is parallel to `Names`. `Names`/`Codes` list the **13 UI
  languages** (the curated world set plus Russian, matching `WorldLayouts.Popular`); an unknown key returns
  the source unchanged and an empty translation falls back to the key itself (English), never to Russian.
  **A key never holds Cyrillic**, and a Cyrillic string literal lives only in a `ru:`/`uk:` column, in
  `Symbols.g.cs` or in the declared samples of `LocalizationSourceGateTests.DeclaredData` - the gate scans
  every string literal, not only the ones in a translating call. Two surfaces that need one English word
  in two grammatical forms of another language (`Large` for the marker, `Large (tiles)` for the traffic-light
  tiles; seven pairs, `PLAN/S0070_english-source-strings/research/decisions.tsv`) get distinct keys, the
  second with a context in parentheses and an `en:` display text that keeps the English UI unchanged.
  **Rewording an English caption renames its key**: `tools/l10n/Rename-SourceKey.ps1`. Three helpers carry
  the script-specific bits:
  `IsRightToLeft` (ar/ur → `RightToLeft` + `RightToLeftLayout` on every form), `FontFamily`
  (Nirmala UI for hi/bn, Microsoft YaHei UI for zh; null = the default font is fine) and
  `WidthScale`/`Scaled` (the tables' fixed pixel columns were measured against Russian, and German or
  Devanagari overflow them). `SettingsForm.ApplyScript` applies all three on every language change, and
  `RefreshBoldFonts` re-derives the section headers, whose explicit **bold** font would otherwise not
  inherit the new family. **A translated template is formatted by `Localization.Format`** (a language, or the
  window's own `T` function) and never by `string.Format`: a translation with a broken `{n}` would otherwise
  throw inside whatever event handler asked, so a broken one shows the source sentence instead
  (`APP-BEHAVIOUR` rule 7; gated by `AppBehaviourGateTests.NoTranslatedTemplateIsFormattedRaw`, and the
  placeholder parity of every translation by `LocalizationSourceGateTests`).
