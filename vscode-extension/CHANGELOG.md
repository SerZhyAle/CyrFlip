# Changelog

All notable changes to the **CyrFlip - keyboard layout at the caret** extension are documented here.

## [0.1.4] - 2026-08-19

- **No more double marker at the editor caret.** The desktop app can locate the Monaco caret through
  IAccessible2 and was drawing its own overlay next to this extension's marker. The extension now
  publishes `editor-caret.txt` beside `layout.txt` while it is drawing, and the app hides its overlay
  while that file is fresh. The claim lapses five seconds after the last editor activity, so the app's
  marker still appears in the chat box, the terminal and the search fields - places this extension
  cannot draw at all.
- **The marker is translucent** (60%), matching the app; the value comes from the shared
  `layout-colors.json` rather than being restated here.
- **The colour now names the keyboard layout, not only the language.** Each of the 25 layouts of the 13
  curated languages has its own shade of its language's colour (read from `layout-klid.txt`, published
  by the app); everything outside those languages shares one neutral colour instead of a per-code hash.

## [0.1.3] - 2026-07-28

- **The marker is now coloured for every language, not just three.** Version 0.1.2 fixed the wording
  but not the colours: the extension carried its own three-entry table (`EN`, `RU`, `UK`) and painted
  everything else grey, while the app had thirteen curated colours plus a deterministic bright colour
  for any other layout. So `DE`, `FR`, `ZH` and the rest were displayed - just not in the app's colours.
- The palette is no longer restated here. `src/layout-colors.json` is the shared copy of the app's
  `LayoutStyle`, and a test on the app side fails the build if the two ever disagree.

## [0.1.2] - 2026-07-26

- The marker is no longer described as EN/RU/UK only: the app now reports a two-letter code for **any**
  installed layout (`DE`, `FR`, `ZH`, `AR`, ..) and the extension has always displayed whatever it is told.
- Marketplace description and README updated to say so, in English and Russian.

## [0.1.1] - 2026-06-13

- Added a `winget` install command for the required CyrFlip desktop app to the Marketplace README.
- Added a short Russian explanation clarifying what the desktop app does and why the extension needs it.

## [0.1.0] - 2026-06-11

Initial release.

- Reads the active keyboard layout (EN / RU / UK) published by the [CyrFlip](https://github.com/SerZhyAle/CyrFlip) desktop app and shows it **at the editor caret** as a small coloured, black-outlined marker.
- Status-bar indicator (`⌨ EN/RU/UK`), toggleable via `cyrflip.showStatusBar`.
- Configurable layout-file path (`cyrflip.layoutFile`) and poll interval (`cyrflip.pollIntervalMs`).
