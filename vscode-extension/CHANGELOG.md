# Changelog

All notable changes to the **CyrFlip - keyboard layout at the caret** extension are documented here.

## [0.1.5] - 2026-10-01

- **Store installs: the layout comes from your own profile.** The Microsoft Store build of the app now
  writes `layout.txt` to its per-user package folder
  (`%LOCALAPPDATA%\Packages\SZA.CyrFlip_fdk7e19xt9z9j\LocalCache\Local\CyrFlip`), and the extension reads
  it there. The old machine-wide `%ProgramData%\CyrFlip\layout.txt` is shared by every account on the PC,
  so a second Windows user saw the first user's layout; it is now read only when no per-user file exists
  (an older Store version of the app), and `editor-caret.txt` follows whichever file is read.
- **Works in remote windows.** The extension now declares itself a UI extension
  (`"extensionKind": ["ui"]`), so it runs on the local machine in Remote-SSH, WSL, Dev Containers and
  Codespaces windows, where the layout file actually is. Before, it was treated as a workspace
  extension: the local copy did not run, and a copy installed on the remote side found no layout file.
- **The app's marker stays in the chat box.** Only the user's own typing and caret moves in the active
  editor now renew the `editor-caret.txt` claim; a language server's edits, Output channel appends,
  reloads from disk and programmatic selection changes no longer keep it alive - and neither does an
  editor that becomes active by itself, when a chat agent or a command opens a file while you type in
  the chat.
- **Two VS Code windows no longer delete each other's claim.** The claim carries the window's session
  id, and a window that loses the focus deletes the file only when it still holds its own id.

- **README brought up to date:** both layout-file locations (`%LOCALAPPDATA%` and, for a Store
  install, `%ProgramData%`, newest wins), any layout rather than EN/RU/UK, the colour table, the 60%
  opacity and the `editor-caret.txt` claim. The `cyrflip.layoutFile` setting's description names both
  locations. The app now removes `layout.txt` when it exits, so the marker disappears with it.

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
