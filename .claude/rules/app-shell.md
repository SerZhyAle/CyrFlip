---
paths:
  - "src/CyrFlip/Program.cs"
  - "src/CyrFlip/CyrFlipContext.cs"
  - "src/CyrFlip/SessionEndWatcher.cs"
  - "src/CyrFlip/CloseToHide.cs"
  - "src/CyrFlip/Autostart.cs"
  - "src/CyrFlip/AppShortcuts.cs"
  - "src/CyrFlip/KeepAwake.cs"
  - "src/CyrFlip/WindowInterop.cs"
  - "src/CyrFlip/AppConfig.cs"
  - "src/CyrFlip/DataFolder*.cs"
  - "src/CyrFlip/PackageInfo.cs"
---

# Tray shell, lifecycle, autostart, config storage and the data folder

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Project status

**Implemented, builds + tests pass, launches as a tray app.** The full pipeline is coded (hook → flip → paste) and the app runs in the system tray under the 50 MB budget. The copy→paste flip still needs interactive manual verification with a real selection (per the spec's manual-test plan); unit tests cover the pure logic. What exists:
- `src/CyrFlip/` - the app (net48 WinForms): `TransliterationEngine`, `CaseFlipEngine`, `Hotkey`, `KeyboardHook`, `ClipboardHandler`, `CursorIndicator` (layout indicator), `Autostart`, `AppConfig`, `WindowInterop`, the `CyrFlipContext` tray shell, the `TrayChordList` / `TrayHoverStateMachine` / `TrayHotkeysPopup` tray hover overview, the `Launcher*` module family (the absorbed OneClickRunner scenario launcher, feature 5 below) and the text context menu (`MouseHook`, `MouseChord`, `SelectionProbe`, `LaunchTarget`, `TextContextMenu`, feature 7 below), plus the log bundle the user mails to the author (`SupportBundle`, `MailSender`, `SupportBundleDialog`) - all implemented.
- `tests/CyrFlip.Tests/` - xUnit (`TransliterationEngine` + `CaseFlipEngine` + `Hotkey`). Internals are visible to tests via `InternalsVisibleTo`.

Note: the empty `dev/` directory predates the scaffold and is unused - code lives in `src/`.

## Tray menu and tray mouse

It runs in the **system tray** (the icon also shows the layout; right-click menu = the history controls, the three indicator toggles, the two keep-awake switches, the **Launcher** submenu and **"Перевести буфер обмена"** - each visible only while its module is enabled - then Settings and Exit). **Tray mouse:** a **single left click switches the input language to the next one in Windows' rotation** (`LayoutSwitcher.SwitchToNext`), a **double click opens Settings**. Since the shell also delivers the first click of a double click as an ordinary click, the switch is deferred by `SystemInformation.DoubleClickTime` and cancelled when the double click arrives - and it targets `CursorIndicator.LastActiveWindow`, not the foreground window, because clicking the notification area moves the focus to the taskbar. See [PLAN/done/Spec_v1.1_Cursor_and_Caret.md](../../PLAN/done/Spec_v1.1_Cursor_and_Caret.md) for the indicator design.

## Deviation from the spec

**One deliberate deviation from the spec:** **tray app, not a Windows service.** A global `WH_KEYBOARD_LL` hook and the active-window/layout APIs only work in the user's interactive desktop session; a service runs in session 0 with no desktop, so AUTO/MANUAL service start types don't apply. "Autostart" is therefore a per-user `HKCU\..\Run` entry (`Autostart.cs`).

## Modules (`src/CyrFlip/`)

- **Program.cs** - entry point (`[STAThread]` Main). Enforces **single instance** via a named mutex (`Local\CyrFlipSingleInstance`) - a second copy would install a second hook and fight over the system cursor - then runs `CyrFlipContext`.
- **CyrFlipContext.cs** - the tray app shell (`ApplicationContext`). Builds the tray `NotifyIcon` + menu (history controls, cursor/caret/dot-mode toggles, keep-awake toggles, Settings, Exit), subscribes to `LayoutChanged(code, klid, capsOn)` (forwarding the CapsLock state to all three surfaces), and on any hotkey runs the matching clipboard op via the shared `RunClipboardOp` helper **on a dedicated background thread** guarded by a single `Interlocked` `_busy` flag - it serializes every conversion and the case-flip (all own the clipboard) and blocks auto-repeat re-entry. Tracks usage via `AppConfig.IncrementFlipCount` / `IncrementCaseFlipCount`. The three fixed chords (case flip, clipboard history, quick notes) are changed at runtime via `HotkeyDialog` and rejected when any other chord owner holds them (`ChordRegistry`, below); the conversion chords are set from the table itself.
- **SessionEndWatcher.cs / CloseToHide.cs** - how CyrFlip ends (ticket S0003). An `ApplicationContext`
  with no main form is simply terminated after `WM_ENDSESSION`, so neither `Dispose` nor
  `ApplicationExit` runs at sign-out/shutdown; the watcher is a hidden **top-level** `NativeWindow`
  (a message-only window never receives the end-session messages) that always answers
  `WM_QUERYENDSESSION` with yes and, on `WM_ENDSESSION(TRUE)`, lets `CyrFlipContext.OnSessionEnding`
  run `SessionEndSequence` inside a 3 s budget: retract the layout files, append the last note edit,
  drain the queued history writes, restore the cursor, and only then - when no background compaction is
  running and under a second has gone - compact the notes journal, then flush the logs (S0035 QN2-6:
  the compaction used to come second, wait for a running one on the journal lock and leave the history
  no time). `CloseToHide.Intercept` is the rule every "close means hide" window
  follows: **only a `UserClosing` close is cancelled** - cancelling `WindowsShutDown` vetoed the
  sign-out, and cancelling `ApplicationExitCall` kept the app running after the fatal-error box said
  it would close. `CyrFlipContext.Dispose` is guarded (it runs twice), and every clipboard worker is
  started through `StartClipboardWorker`, which releases `_busy` if the thread never starts.
  The same window also raises `ThemeSignal` - `WM_SETTINGCHANGE` carrying `"ImmersiveColorSet"` or
  `SPI_SETHIGHCONTRAST`, `WM_SYSCOLORCHANGE`, `WM_THEMECHANGED` - which is how the theme follows Windows.

- **AppShortcuts.cs** - the unpackaged build's Start menu and desktop `CyrFlip.lnk` (ticket S0041): there is no installer, so the first run from a stable folder creates both (`ShortcutsCreated` marker), later runs re-point an existing link at the running exe when the folder moved, a start from `%TEMP%` or `bin` touches nothing, and a deleted link comes back only through the General-page checkbox. The packaged build writes none - Windows lists the package in Start itself; an MSIX desktop shortcut (`desktop7:Shortcut`, build 19645+) is still open.
- **Autostart.cs** - `HKCU\..\Run` toggle (per-user; not a service - see deviations). **Packaged (Store) builds
  are different in both directions:** the `Run` write is virtualized, so `Set` is a no-op and the settings
  checkbox opens *Settings ▸ Apps ▸ Startup* instead - but the state is **read** from the startupTask record
  (`..\AppModel\SystemAppData\<PackageFamilyName>\CyrFlipStartup\State`, `StartupTaskState` 2/4 = on) and
  re-read when the settings window is activated. Returning a hard `false` there is what made the checkbox look
  broken: it opened the system page and then "unticked" itself even though Windows was starting CyrFlip.
- **KeepAwake.cs** - the two "don't sleep / don't blank the screen (like video)" toggles. Two independent switches (`KeepSystemAwake`/`KeepScreenOn`) OR-ed into one `SetThreadExecutionState` call: `ES_SYSTEM_REQUIRED` (system won't sleep) and `ES_DISPLAY_REQUIRED` (display won't blank/lock on idle). `ES_CONTINUOUS` makes the request sticky (one call per change, no polling); it is bound to the calling thread (the tray UI thread), so a hard kill clears it for free, and `Reset()` restores the normal idle policy on exit and when both are off. **Both switches are persisted** (`AppConfig.KeepSystemAwake`/`KeepScreenOn`) and re-applied at startup by `Restore(system, screen)` - **one** call carrying both bits, never two, so a launch with both on never asks Windows for half the policy first. That reverses spec §5's original "memory only, off on every launch" (changed 2026-07-28): a switch that silently forgot itself read as broken, which is exactly how it was reported. The old safety argument now lives in the UI text instead - the settings hint says outright that a forgotten switch keeps the machine awake after a restart too and that CyrFlip will not watch your battery for you. `KeepAwake` still persists nothing itself: `CyrFlipContext` writes the config and hands the state back, so the P/Invoke seam (`SetExecutionState`) stays the only thing to fake in tests. No admin rights; the two registry values are ordinary `AppConfig` state. Driven by two checkable tray items + two "General"-tab checkboxes; see [PLAN/done/KeepAwake_Spec_Idea_v0.1.md](../../PLAN/done/KeepAwake_Spec_Idea_v0.1.md). **Only `powercfg /requests` (admin) can confirm the request landed** - `SetThreadExecutionState` returns the previous state, never a confirmation; `tools/uitest/Test-KeepAwake.ps1` is that check.
- **WindowInterop.cs** - all `[DllImport]`s + interop structs. The `INPUT` union includes `MOUSEINPUT` so `Marshal.SizeOf(INPUT)` matches the real struct on x64 (else `SendInput` silently fails).
- **AppConfig.cs** - persists settings to `HKCU\Software\CyrFlip`. A failed key open marks the entire config unreadable and blocks every save, snapshot capture and first-run tour until restart (S0063). On first run with no registry key, migrates from a legacy `config.json` (from `%APPDATA%\CyrFlip\` or beside the exe) and seeds the conversion table (`NeedsFlipRow`/`FlipRow`, called from `Load()`; see feature 2). Fields: `CaseHotkey` (default **Ctrl+Shift+F11**), `CursorSize`, `EnableCursorChange` (default **false**), `EnableCaretOverlay` (default **true**), `CaretDotMode` (default **false**), `EnableLanguageSwitch` (default **false**), `FlipCapsLockAfter` (default **false**), `ConvertSymbols` (default **true**), the hotkey switches `EnableHotkeys`/`EnableCaseHotkey`/`EnableHistoryHotkey` (all default **true**), `DeferToRemoteDesktop` (default **false**), `FlipCount` + `CaseFlipCount` + `TranslateCount` (usage counters), and the translator block (`EnableTranslate`, `TranslateSeeded`, `TranslateProfiles`, `TranslateEndpoint`, `TranslateModel`, the timeouts and the result-window options - see the config table). `UiLanguage` holds the language **endonym** ("Русский", "Deutsch", "中文", ..) and defaults to the OS UI language via `Localization.DefaultLanguage()` (any of the 13 translated languages, else **English**, the source language of the translation table - never Russian). The **user's display language** (`CurrentUICulture`) wins over the language Windows was installed in (`InstalledUICulture`), which is the only one that is right for an English Windows switched to a Russian desktop; regional variants resolve to their language, so de-AT, pt-BR and zh-Hans-CN all find their translation. `SettingsTab` remembers which settings page was open (restored on the next launch too, clamped against the live page count - the tab strip grows between versions). `Save()` writes all fields; `IncrementFlipCount()`/`IncrementCaseFlipCount()`/`IncrementTranslateCount()`/`SaveSettingsTab()` write only that one value (cheap). `SaveSettingsUiState()` is the settings window's UI-context write (ticket S0048, WINDOWS-UI section 3): group expansion (`SettingsGroupsState`), per-page scroll anchors (`SettingsViewport`) and the window rectangle (`SettingsWindowX/Y/Width/Height`), written when the window hides, never while the user drags it; anchors are stable group ids/row ordinals, not translated captions. **Every value is read on its own** (`LoadFrom`/`SaveTo` over the `IConfigKey` seam, `AppConfigLoadTests`; ticket S0007 CF-1): a value of the wrong kind falls back to its own default and lands in `UnreadableValues` (one balloon per start), where it used to abort the whole load and let the next save wipe the conversion table and the one-time Windows snapshots. A table or snapshot that is present but unreadable is **never saved over** until the user edits it; window geometry values are clamped by their windows rather than on load, other numbers are clamped to their UI's range; and the two profile tables are sanitized on load - null rows dropped, null strings made `""`, duplicate ids regenerated, the repaired form written back (CF-2).

- **DataFolder.cs / DataFolderMigration.cs** - the one decision of where every per-user file lives (ticket
  S0016): `%LOCALAPPDATA%\CyrFlip` unpackaged; packaged, the package's own per-user folder
  `%LOCALAPPDATA%\Packages\<family>\LocalCache\Local\CyrFlip`, built from the **unvirtualized**
  `%LOCALAPPDATA%` string plus `PackageInfo.FamilyName` and written **explicitly** - it is where MSIX puts
  the package's own `%LOCALAPPDATA%` writes, so the VS Code extension and a mail client find the files at
  the same path. `LayoutPublisher`, `DiagnosticLog` (every log and the quick-notes journal),
  `SupportBundle` and `CaretDiagnostics` all read it. The packaged build used to use the machine-wide
  `%ProgramData%\CyrFlip`, shared by every account on the PC: the second user's writes failed on the
  first user's files, B replayed A's journal (every line a DPAPI failure, every append refused) and
  anyone could read anyone's logs. `DataFolderMigration.RunOnce` (first packaged start, marker
  `DataFolderMigrated`) moves the **current user's own** journal and `reports\` archives out and deletes
  their old logs; a file another account owns is never touched, and a journal meeting an existing one is
  kept as `quick-notes.migrated-<date>.log` and replayed once, the newer `UpdatedAtUtc` winning per note
  and a note the live journal deleted staying deleted (S0035 QN2-7). Every pass merges each kept journal
  without a `.merged` marker, so a merge that failed is retried like every other step (QN2-4), and every
  moved file has its inherited ACEs reset to the new folder's - a same-volume move kept
  `%ProgramData%`'s "Users: read" (QN2-5).
  **The live gate is still open**: that the explicit LocalCache path is not redirected a second time was
  reasoned, not observed - `tools/uitest/Test-PerUserFolder.ps1` is the check, to run on Windows 10 22H2
  and 11 with the package installed.
