# Pointer - `LAYOUT-PALETTE`

| | |
| --- | --- |
| **Id** | `LAYOUT-PALETTE` |
| **Version** | 1.1, active (2026-09-26, S0012/S0013 - rules 2, 4, 6 and the new rule 8 in section 9 of the contract; the table did not change) |
| **Home** | `layout-indicator/README.md` in the shared contracts catalog |
| **Role here** | owner and source of truth - `src/CyrFlip/LayoutStyle.cs`; the VS Code extension ships the machine-readable copy |

## What this repository must do to stay conformant

- **One source of truth, one copy, compared in both directions** (rule 6). `LayoutStyle.cs` is the source;
  `vscode-extension/src/layout-colors.json` is the copy the extension packages; `LayoutColorsTests` fails
  the build when they disagree either way. `tools/IconGen` needs no copy - it compiles `LayoutStyle.cs`.
- **Three rungs, in order** (rule 1): exact layout, then its language, then the one neutral colour. A new
  drawing surface calls `ColorForLayout`, never a table directly.
- **Every uncurated layout is the neutral colour** (rule 3). Do not reintroduce a computed or hashed hue:
  it meant nothing and could land beside a curated language's colour.
- **The constraints are tested, not judged by eye** (rule 4): redmean distance 40 between shades, none
  dark, black outline.
- **Opacity 0.6 on the badge over the user's text, never on the I-beam or the tray icon** (rule 5).
- **Each language colour is its primary layout's shade** (rule 4), which is what lets the distance and
  brightness tests over the layout table speak for the language table too
  (`EachLanguageColourIsItsPrimaryLayoutsShade`).
- **A shade stays within 12 degrees of its language's hue** (rule 2, measured in HSB); English and Italian
  share the blue band and Spanish and Urdu the pink one, told apart by lightness, saturation and the
  letters (`EveryShadeStaysNearItsLanguageHue`).
- **The copy's shape is fixed**: `curated`, `layouts`, `other`, `markerOpacity` and `_`-prefixed comments,
  every key upper case (`ThePaletteCopyHasOnlyKnownSections`). `0.6` itself is pinned
  (`TheMarkerOpacityIsTheContractsValue`).
- **The CapsLock decoration** is drawn in the ladder colour, and dark in dot mode, where a frame in the
  marker's own colour would be invisible.

Adding a language or a layout is additive - add it in both tables in the same change, which is what the
build test is for.
