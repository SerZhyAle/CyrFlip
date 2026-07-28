# Changelog

All notable changes to the **CyrFlip - keyboard layout at the caret** extension are documented here.

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
