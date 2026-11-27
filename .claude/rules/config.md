---
paths:
  - "src/CyrFlip/AppConfig.cs"
  - "src/CyrFlip/RuntimeRegistry.cs"
  - "src/CyrFlip/SettingsFile*.cs"
  - "src/CyrFlip/SettingsSnapshot.cs"
  - "src/CyrFlip/SettingsImportPlan.cs"
---

# Registry configuration: what every value means

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Registry values (`HKCU\Software\CyrFlip`)

Names, types and defaults are the table in `CLAUDE.md` "Config" - the only copy (`DocumentationQualityTests.ConfigTableTracksPersistedValues` reads it there). Add a new value to both tables.

| Registry value | Description |
| --- | --- |
| `CaseHotkey` | Case-flip hotkey (user-configurable on the "Исправление текста" tab, group "Регистр") |
| `UiLanguage` | Interface language endonym |
| `ClipboardHistoryHotkey` | Opens clipboard history |
| `EnableClipboardHistory` | Enables local clipboard history |
| `ClipboardHistoryNoticeShown` | One-time marker: the "records everything you copy, local only, never trimmed" notice was shown on the first enable (S0067) |
| `PauseClipboardHistory` | Temporarily stops recording clipboard changes |
| `ShowClipboardHistoryOnStartup` | Opens the history window at startup when history is enabled |
| `ClipboardHistoryX` / `ClipboardHistoryY` | Last history window position |
| `ClipboardHistoryWidth` / `ClipboardHistoryHeight` | Last history window width and height |
| `ClipboardHistoryOpacity` | History window opacity in percent (78..100; the floor is `ThemePalette.TranslucentWindowMinOpacity`, below which its text loses 4.5:1 over a bright or dark desktop - a stored lower value is raised to it on load and written back, S0093) |
| `SettingsTab` | Index of the settings tab last left open; the window reopens on it (clamped against the live page count) |
| `SettingsPageId` | Stable key (`SettingsPageKey` name) of the page last left open; it wins over `SettingsTab` when it still names a page, so the tab strip growing between versions no longer moves the remembered page |
| `SettingsGroupsState` | Expansion of the settings window's collapsible groups, as `id=0\|1` pairs joined by `;` under the groups' stable internal ids (WINDOWS-UI section 3); sanitized on read - malformed rows dropped, 64 rows max |
| `SettingsViewport` | Where each settings page was scrolled to, as `page\|anchor\|offset` rows (`;`-joined): a group id or stable row ordinal plus the offset from it in logical (96-DPI) px; sanitized on read |
| `SettingsWindowX` / `SettingsWindowY` | The settings window's last position; `int.MinValue` = never placed (then it opens centred). Written on hide/close only, restored clamped to a work area |
| `SettingsWindowWidth` / `SettingsWindowHeight` | The settings window's last normal-state size; `0` = never user-sized. A maximized close never overwrites these |
| `CursorSize` | Base size of the I-beam cursor and caret overlay marker (px at 100%); the "Размер метки" slider on the "Индикаторы" tab writes 18 / 24 / 32. The badge is scaled by its monitor's DPI, the I-beam by the primary monitor's DPI and the Windows pointer size (`CursorBaseSize`) - `MarkerSize`, S0011 LI-3 |
| `EnableCursorChange` | 1 = replace the system I-beam with the layout-branded cursor |
| `CursorNoticeShown` | One-time marker: the "replaces the I-beam in every program" notice was shown on the first enable (S0067) |
| `EnableCaretOverlay` | 1 = show the layout marker next to the blinking text caret |
| `CaretDotMode` | 1 = use a solid colour dot instead of EN/RU/UK text in the overlay |
| `EnableLanguageSwitch` | 1 = after a conversion, also switch the target window to the layout the text now reads in |
| `FlipCapsLockAfter` | 1 = after a case flip, also set the physical CapsLock key to match the corrected text (on when it ends in a capital, off when it ends in a small letter, untouched when it has no cased letter) |
| `ConvertSymbols` | 1 = a conversion also swaps a key that is **punctuation in both layouts** (`/` against `.`, `@` against `"`) - what every release before this switch did. 0 = such a key is left alone, so `/ghbdtn` becomes `/привет` rather than `.привет`. A key whose other side is a **letter** (`,`→`б`, `[`→`х`) is never ambiguous and is converted either way |
| `KeepSystemAwake` | 1 = hold `ES_SYSTEM_REQUIRED` (Windows won't sleep on idle); restored at startup |
| `KeepScreenOn` | 1 = hold `ES_DISPLAY_REQUIRED` (screen won't blank/lock on idle); restored at startup |
| `EnableHotkeys` | Master switch; 0 = the hook passes every key through (no hotkey acts) |
| `EnableCaseHotkey` | 1 = the case-flip chord is active (only while `EnableHotkeys`=1) |
| `EnableHistoryHotkey` | 1 = the clipboard-history chord is active |
| `DeferToRemoteDesktop` | 1 = while an RDP client (mstsc/msrdc) is focused, let the chord pass to the remote session |
| `EnableContextMenu` | 1 = CyrFlip's own menu over the selection is on (the `WH_MOUSE_LL` hook is installed). 0 = no mouse hook at all |
| `ContextMenuChord` | The chord that opens it, as an invariant token; an unparsable value (incl. a bare `RightClick`) falls back to the default |
| `TextMenuHotkey` | The keyboard's way to the same menu (S0045 K2, `INPUT-PARITY` rule 1): opens it at the caret (the system caret, else the caret overlay's last sighting in that window, else the pointer), taking the foreground while it is open and handing it back on close. Checked against every other chord owner; an update that finds the chord already taken switches it off instead |
| `EnableTextMenuHotkey` | 1 = that chord is bound (only while `EnableContextMenu`=1) |
| `Theme` | The app theme as an invariant token: `system` (follow Windows' `AppsUseLightTheme`, live), `light`, `dark`. Anything else reads as `system`. Windows' high contrast wins over every value |
| `UiInputProfile` | The input profile every window opens with (S0076): `pointer` or `touch`, chosen on the General page, never inferred from the device; unknown reads as `pointer`. Per-PC, not in the settings file |
| `UiInterfaceScale` | Interface scale in per cent: 75, 100, 125, 150 or 200 (typography, glyphs, checkboxes, spacing and targets together); any other value reads as 100. Per-PC, not in the settings file |
| `UiTextScale` | Text enlargement in per cent on top of the interface scale: 100, 125, 150 or 200; any other value reads as 100. Per-PC, not in the settings file |
| `LanguageHotkeysBackup` | One-time JSON snapshot of Windows' own language hotkeys, taken before CyrFlip first edits them; empty = never touched |
| `InputLayoutsBackup` | One-time JSON snapshot of the keyboard-layout stores (Preload/Substitutes + User Profile), taken before CyrFlip first edits them |
| `InputMethodOverrideWritten` | The `InputMethodOverride` CyrFlip last wrote through "По умолчанию"; restoring a format-1 layout backup (which never captured that value) removes it only while it still equals this (S0007 WL-13) |
| `LayoutConversionProfiles` | JSON array of the layout→layout conversion table (source KLID, target KLID, chord, enabled). Absent (or empty while the legacy `Hotkey`/`EnableFlipHotkey` values survive) = seed one EN⇄RU row on Ctrl+Shift+F12; `[]` after the migration = the user emptied it, left alone |
| `EnableScenarioLauncher` | 1 = the scenario launcher is on (tray submenu + taskbar button with its Jump List and left-click menu + per-scenario hotkeys). 0 = every surface cleared, button closed; scenario XMLs stay on disk |
| `LauncherFirstEnableDone` | One-time marker: the first enable's migration offer / Calculator seeding ran; an emptied list is never re-nagged or reseeded |
| `DataFolderMigrated` | One-time marker (packaged builds only): the move out of the machine-wide `%ProgramData%\CyrFlip` into the per-user package folder finished with nothing left to retry (S0016) |
| `WelcomeShown` | One-time marker: the first-run tour was shown; an update never shows it. Explicit `0` requests replay. |
| `ShortcutsCreated` | One-time marker (unpackaged builds only): the first run from a stable folder created the Start menu and desktop shortcuts (`AppShortcuts`, S0041); a shortcut the user deletes is never recreated by a start |
| `EnableTranslate` | 1 = the translator is on (chords bound, tray entry visible). 0 = no chord, no tray entry, no socket |
| `TranslateSeeded` | One-time marker: the starter row was offered on the first enable; a table the user empties is never refilled |
| `TranslateProfiles` | JSON array of the translation table (target language or the `ui`/`active` token, chord, enabled) |
| `TranslateEndpoint` | Ollama address; empty = `http://localhost:11434`. Another address means the selection is sent to that machine |
| `TranslateModel` | Model name; empty = use whichever model is installed. **Chosen by measurement (2026-07-28), not by size:** every model under 4 GB failed English ⇄ Russian ⇄ Ukrainian - `qwen2.5:3b` (the old default) even emitted Chinese characters inside a Russian translation |
| `TranslateTimeoutSeconds` | Budget for the first token; then the 30 s idle timer and the 10-min ceiling |
| `TranslateKeepAliveMinutes` | Ollama's `keep_alive` (-1..120 minutes); -1 = keep loaded (default), 0 = unload at once; the RAM is the Ollama process's, not CyrFlip's |
| `TranslateAutoStartServer` | 1 = try to start Ollama once (waiting ~8 s) when it doesn't answer |
| `TranslateCopyResult` | 1 = put the translation on the clipboard, where the history records it like any other copy |
| `TranslatePasteResult` | 1 = paste the translation over the selection (skipped, with a note, if the focus moved) |
| `TranslateShowSource` | 1 = show the source text above the translation |
| `TranslateWindowTimeout` | Auto-close the result window after N seconds; 0 = never |
| `TranslateWindowWidth` / `TranslateWindowHeight` | Remembered size of the result window (its position is always at the cursor). A stored `460`/`260` - the first build's default, i.e. "never resized" - is replaced by today's, and any size is clamped to the monitor the pointer is on |
| `TranslateWindowOpacity` | Opacity of the result window, 78..100 (the same floor and load-time raise as the history window, S0093) |
| `EnableQuickNotes` | 1 = the quick notes are on. 0 = the journal is never read or created, no tray entry, no chord |
| `QuickNotesNoticeShown` | One-time marker: the "local, DPAPI, not a secret store" notice was shown on the first enable |
| `QuickNotesHotkey` | Opens a new note with the caret in the body; checked against every other chord owner (`ChordRegistry`). The default was `Ctrl+Alt+N` until S0004 - Ctrl+Alt is AltGr, and AltGr+N is "ń" on Polish; a stored `Ctrl+Alt+N` is kept, since the hook no longer reads AltGr as Ctrl+Alt |
| `EnableQuickNotesHotkey` | 1 = that chord is bound (only while `EnableQuickNotes`=1) |
| `QuickNotesWordWrap` | 1 = the editor wraps long lines. Off by default: the body is more often code than prose |
| `QuickNotesLineNumbers` | 1 = show line numbers gutter along the left edge of the editor (S0078). Off by default |
| `QuickNotesSync` | 1 = the quick notes sync with the other devices of one exchange account (S0105, `DEVICE-EXCHANGE` 1.13). Consent on this device: never in the settings file |
| `EnableExternalCommands` | 1 = "Let other programs ask CyrFlip to work with my files, screen, clipboard or the network" (APP-COMMANDS rule 13, S0107): a command of any risk class but `safe` is taken. Only the person turns it on, on the General page; no command, settings file or settings standard carries it. A pre-adoption spelling (`/clipboard-send`, `/launcher-run:{guid}`, ..) is gated by its module only (item F) |
| `QuickNotesSyncEntry` | Local id of the exchange account entry the notes sync with; empty = the only `member` entry when there is exactly one. Never in the settings file |
| `RemoteCommandsListen` | 1 = "Listen to commands from my other devices" (REMOTE-CONTROL rule 8, CYRFLP-0140): requests from other devices of the exchange account are decided by the grants; 0 = every request is refused `not-permitted`. Changed only by the person, in the External control group of the Exchange server page or the External control window; never in the settings file or the settings standard |
| `ExternalControlX` / `ExternalControlY` / `ExternalControlWidth` / `ExternalControlHeight` | The External control window's last bounds (CYRFLP-0142), written on a move or resize and on close by `SaveExternalControlWindow` only (never by `Save()`, never in the settings file); a position no live monitor covers is clamped |
| `RemoteCommandGrants` | JSON array of the controllers this PC accepts (account entry id, device id, name, risk class to `always` or `ask`; rules 9 and 10). Kept on this PC only; never in the settings file or the settings standard |
| `ExchangeNotesWarningOff` | 1 = a **notes-only** export to the exchange file no longer shows the "plain unencrypted text" warning. An export that carries clipboard history always shows it (S0023) |
| `SettingsBackupEnabled` | 1 = the rolling settings backup in `%USERPROFILE%\Documents\CyrFlip\Backups` is written (S0072): debounced after settings changes, keep-last-3, the writer silent while the restore question is open. Exported in the settings file v2 |
| `BackupOfferOpen` | Marker: the "restore the backup found in Documents?" question is unanswered - it re-asks next start, and the backup writer stays silent while it is set (S0072). Machine history: never in the settings file |
| `BackupRestoreDeclined` | The stamp of the backup the user answered "No - start fresh" to; the question never re-asks on that profile (S0072). Machine history |
| `QuickNotesX` / `QuickNotesY` / `QuickNotesWidth` / `QuickNotesHeight` | Window geometry, saved at the end of a move or resize (ResizeEnd) and on hide (APP-BEHAVIOUR rule 10). A position no live monitor covers is clamped to the nearest surviving monitor (`ScreenPlacement.Clamp`); a size of `0` (never resized by the user) opens the window at its **measured minimum** - `QuickNotesWindow.ApplyMinimumSize` adds up the real width of the button row, the top row and the privacy hint in the current language at the current display scaling, so the floor is never the pair of constants that used to clip the buttons in most of the 13 languages. A stored size is only ever **grown** to that minimum, never shrunk |
| `QuickNotesSelected` | The note the window was last left on, so it reopens where the user was |
| `FlipCount` | Usage counter; incremented on each successful transliteration |
| `CaseFlipCount` | Usage counter; incremented on each successful case flip |
| `TranslateCount` | Usage counter; incremented on each successful translation |
| `QuickNoteCount` | Usage counter; incremented when a note is created for the first time |
| `ScreenshotHotkey` | Starts the screen region capture (re-keyed by S0071 from the dead Ctrl+PrintScreen; the one-time migration moves stored old defaults); checked against every other chord owner (`ChordRegistry`) |
| `EnableScreenshotHotkey` | 1 = that chord is bound. The only switch the region capture has: no module switch, the tray item is always there |
| `ScreenshotFullHotkey` | The instant all-displays capture (S0071): one freeze of the virtual screen, no selection; checked against every other chord owner |
| `EnableScreenshotFullHotkey` | 1 = the full capture's chord is bound (its own switch only, no module switch) |
| `EnableScreenshotAnnotate` | 1 = the draw-on-capture module is on (S0071, default on); 0 = neither annotate chord is bound and no editor can appear |
| `ScreenshotAnnotateAreaHotkey` | Area capture that opens the annotation editor (S0071); bound only while `EnableScreenshotAnnotate`=1 |
| `EnableScreenshotAnnotateAreaHotkey` | 1 = that chord is bound (only while `EnableScreenshotAnnotate`=1) |
| `ScreenshotFullAnnotateHotkey` | Full capture that opens the annotation editor (S0071); same double gate |
| `EnableScreenshotFullAnnotateHotkey` | 1 = that chord is bound (only while `EnableScreenshotAnnotate`=1) |
| `ScreenshotFolderHotkey` | Opens the folder the captures are saved into (S0057, re-keyed by S0071 from the dead Ctrl+Shift+PrintScreen); checked against every other chord owner (`ChordRegistry`) |
| `EnableScreenshotFolderHotkey` | 1 = that chord is bound (only while `ScreenshotSaveEnabled`=1; the probe and the open are `ScreenshotFolderOpener`'s, never on the hooks' thread) |
| `ScreenshotSaveEnabled` | 1 = every capture is also saved as a PNG (the cropped region, the clipboard's bytes) into `ScreenshotFolder`. On by default since S0071 - fresh profiles save from the first capture; existing profiles keep their stored value, and the privacy wording on the page was corrected in the same change |
| `ScreenshotKeyMigrationDone` | The one-time S0071 chord re-key marker (machine history, never exported): present = the stored Ctrl+PrintScreen / Ctrl+Shift+PrintScreen defaults were moved once |
| `ScreenshotFolder` | Where saved captures go; empty = the `FOLDERID_Screenshots` known folder, resolved at save time (`CAPTURE-OUTPUT` rule 9) |
| `ScreenshotCount` | Usage counter; incremented on each capture that reached the clipboard |
| `EnableSymbols` | 1 = the module is on; 0 = no surface opens it, no chord bound, no window created |
| `SymbolsHotkey` | Shows / hides the palette; checked against every other chord owner (`ChordRegistry`) |
| `EnableSymbolsHotkey` | 1 = that chord is bound (only while `EnableSymbols`=1) |
| `SymbolsCategory` | The group the palette last showed: `all`, or a group key (`punctuation`, `currency`, `math`, `arrows`, `greek`, `latin`, `cyrillic`, `combining`, `symbols`, `numbers`, `invisible`, `box`); an unknown key reads as `all` |
| `SymbolsCloseOnPick` | 1 = a pick copies the character alone and closes the window; 0 = a pick appends it to the window's text field and copies the whole field |
| `SymbolsHideTypable` | 1 = hide the characters a single key combination of an installed layout already types |
| `SymbolsX` / `SymbolsY` / `SymbolsWidth` / `SymbolsHeight` | Window geometry, saved at the end of a gesture and on hide; a position no live monitor covers is clamped, a size only ever grown to the measured minimum |
| `SymbolsCount` | Usage counter; incremented on each successful copy |
| `EnableTrafficLight` | 1 = the traffic light module is on; 0 = no timer, no chord, no tray item, no window, no network or power event subscription (S0069). The block is written only once the module has been on |
| `TrafficLightHotkey` | Shows and focuses the board, and hides it from itself; checked against every other chord owner |
| `EnableTrafficLightHotkey` | 1 = that chord is bound (only while `EnableTrafficLight`=1) |
| `TrafficLightTargets` | JSON array of the targets (label, kind, address, optional fingerprint, group, ordinal, switch, interval, timeout, thresholds, failures before down, accepted HTTP codes, untrusted-certificate option, balloon, journal). `[]` after the user emptied it; an unreadable value is never saved over until the user edits it |
| `TrafficLightShowAtStart` | 1 = the board opens by itself (without taking the focus) at every start while `EnableTrafficLight`=1; 0 = it reopens only if it was left visible (`TrafficLightVisible`). The rule is `TrafficLightLaunch.ShowBoard` |
| `TrafficLightVisible` | The board was left visible. With `TrafficLightShowAtStart`=1 it opens at every start anyway; with 0 this value alone decides whether it reopens (without taking the focus) |
| `TrafficLightPinned` | 1 = the board stays on top of other windows |
| `TrafficLightTileSize` | 0 small (a bare square: no label, no mark on "available", the glyph of any other state, no status line), 1 medium, 2 large |
| `TrafficLightColumns` | Tiles in a row, 1..12; the rows follow from the number of targets and the window fits the grid |
| `TrafficLightWindowTransparency` | The board window's plate transparency, 0..100: 0 the usual opaque window, 100 only the title bar's buttons and the tiles - the plate is see-through and lets clicks pass to whatever is under it |
| `TrafficLightX` / `TrafficLightY` | The board's position, written at the end of a drag and on hide; a position no live monitor covers is clamped. Its size is the grid's and is never stored (`TrafficLightWidth` / `TrafficLightHeight` of the first build are no longer read) |
| `EnableExchangeClipboard` | 1 = the exchange server clipboard module is on; 0 = no connection opened, chords unbound, tray items hidden (S0074) |
| `ExchangeClipboardSendHotkey` | Chord for "send clipboard to server" (to every account entry that can take part); shipped unassigned (D6); checked against every other chord owner |
| `EnableExchangeClipboardSendHotkey` | 1 = send chord is bound (only while `EnableExchangeClipboard`=1) |
| `ExchangeClipboardGetHotkey` | Chord for "get clipboard from server" (the newest value by the servers' own time); shipped unassigned (D6); checked against every other chord owner |
| `EnableExchangeClipboardGetHotkey` | 1 = get chord is bound (only while `EnableExchangeClipboard`=1) |
| `ExchangeClipboardReceivedFolder` | Where a received file is saved before it is put on the clipboard; empty = `Downloads\CyrFlip` of the user (S0074 D4). Never the data or log folder |
| `ExchangeClipboardAccounts` | JSON array of the account entries (local id, host, port, the pinned leaf and key fingerprints, login, device id, label, account kind, sign-in state, the server's features, this entry's public device key, the trusted keys of the other devices). **No token, no private key, no password**: those are in `HKCU\Software\CyrFlip\ExchangeSecrets`, one DPAPI value per entry. An unreadable value is never saved over. Excluded from the settings file |
