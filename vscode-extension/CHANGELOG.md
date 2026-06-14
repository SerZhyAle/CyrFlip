# Changelog

All notable changes to the **CyrFlip - keyboard layout at the caret** extension are documented here.

## [0.1.1] - 2026-06-13

- Added a `winget` install command for the required CyrFlip desktop app to the Marketplace README.
- Added a short Russian explanation clarifying what the desktop app does and why the extension needs it.

## [0.1.0] - 2026-06-11

Initial release.

- Reads the active keyboard layout (EN / RU / UK) published by the [CyrFlip](https://github.com/SerZhyAle/CyrFlip) desktop app and shows it **at the editor caret** as a small coloured, black-outlined marker.
- Status-bar indicator (`⌨ EN/RU/UK`), toggleable via `cyrflip.showStatusBar`.
- Configurable layout-file path (`cyrflip.layoutFile`) and poll interval (`cyrflip.pollIntervalMs`).
