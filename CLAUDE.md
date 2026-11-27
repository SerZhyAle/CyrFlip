# CLAUDE.md

Guidance for agents working in this repository. This file is the always-loaded core; the detail lives in
`.claude/rules/<area>.md` (see "Read this before touching X"). `.claude/` is gitignored: never link or test it.

> **Cross-project contracts live outside this repository.** The shared contract store is `P:\Contracts`, and **this line is the only place in this repository that names its location** - everywhere else cites a contract by its **id** and a rule or section number (`LAYOUT-SIGNAL rule 2`, `LAYOUT-PALETTE rule 5`, `SCENARIO-FILE rule 7`), never by path and never by drive letter, because whoever clones this repository has no `P:`. Its `README.md` is the index; `_meta/RULES.md` is the law, `_meta/VERSIONING.md` the compatibility rules, `_meta/REGISTRY.md` the live map of who implements what at which version. Nothing here ever keeps a **copy** of a contract: [docs/contracts/](docs/contracts/README.md) holds one short pointer per contract this product touches, and a disagreement between this repository and a contract is an amendment to write in the catalog or a dated exception to record in its registry - never a local edit. Aligned 2026-09-22; open conformance debts are [PLAN/Contract_Conformance_Backlog_v0.1.md](PLAN/Contract_Conformance_Backlog_v0.1.md).

> **What CyrFlip can be asked to do from a script or an agent** is listed in [docs/CLI.md](docs/CLI.md) and printed by `CyrFlip.exe commands --json`; read them before driving the product through its window (feature 18 below). The fast gate `tools/cli/Render-CliDoc.ps1 -Check` holds the list, its manifest and this line.

## Project status

Implemented: the net48 WinForms tray app in `src/CyrFlip/`, xUnit tests in `tests/CyrFlip.Tests/`, the site in
`docs/`, the companion extension in `vscode-extension/`. Interop (hook, clipboard flip, layout detection) is not
covered by unit tests and needs manual validation.

**Tickets.** `PLAN/` is local-only (gitignored): one ticket = `PLAN/CYRFLP-####_<slug>.md` with `**Ticket:**`,
`**Status:**` and `**Blocked by:**` lines; older `S####` ids stay names of their tickets. Order and package live in
`PLAN/RELEASE_QUEUE.md`, finished work in `PLAN/RELEASE_READY.md`, closed files in `PLAN/done/`. Full rule and the
specification link: `.claude/rules/tickets.md`.

## What CyrFlip is

CyrFlip is **a tray toolkit for your keyboard and desktop** (decided 2026-10-07, ticket S0058). The list below is the internal map of its features; **how the product is named and introduced, and the order its features take on every public surface, is [POSITIONING.md](POSITIONING.md)** - the one positioning source, held by `tools/site/Test-Positioning.ps1` (run by `build.ps1` and the release preflight), which also guards the frozen anchors (exe and assembly name, store names and identities, the extension id). The product name stays `CyrFlip` everywhere it identifies; the descriptor never enters a window title, an identifier or a store `Title`. The features:

Each entry names what you touch and the file in `.claude/rules/` to read first.

1. **A live layout indicator where you type** - on by default; cursor, caret, colours, `layout.txt`, `vscode-extension/` (`indicator-caret.md`).
2. **Secondary: one table of layout→layout conversions (open-ended, not a fixed option).** Clipboard pipeline, transliteration (`text-flip.md`).
3. **Case flip (fix CapsLock).** (`text-flip.md`)
4. **A self-contained "Языки Windows" tab (settings editor for Windows' input config).** `InputLayout*`, `LanguageHotkeys` (`windows-languages.md`).
5. **Scenario launcher ("Быстрый запуск")** - `Launcher*`; opt-in, off by default (`launcher.md`).
6. **Built-in translator on a local Ollama ("Перевод")** - `Ollama*`, `Translation*`; opt-in, off opens no socket (`translator.md`).
7. **CyrFlip's own context menu over the selection** - mouse hook, selection probe; opt-in, off installs no hook (`context-menu.md`).
8. **Быстрые заметки** - `QuickNote*`; opt-in, off never reads or creates the journal (`quick-notes.md`).
9. **The app theme** - `Theme*`, `Glyph*`, `assets/`, `tools/IconGen` (`theme-glyphs.md`).
10. **The open exchange file - "Перенос на другой компьютер"** - `CyrFlipExchange*` (`quick-notes.md`).
11. **The "Снимки экрана" settings tab and its capture tools** - screen capture, annotation (`screenshots.md`).
12. **The symbols window** - `Symbol*`; opt-in, off by default (`symbols.md`).
13. **The hotkeys overview - the "Горячие клавиши" page** - also the keyboard hook, chords, tray hover popup (`hotkeys.md`).
14. **The settings pages follow the features (ticket S0062).** `SettingsForm*`, dialogs, welcome tour, settings file (`settings-ui.md`).
15. **The traffic light - "Светофор доступности"** - `TrafficLight*`; opt-in, off by default (`traffic-light.md`).
16. **The clipboard on the exchange server** - `Exchange*`; opt-in, off by default (`exchange-server.md`).
17. **The size profile and the settings page rail** (`settings-ui.md`).
18. **The command line - commands from scripts, shortcuts and agents** - `AppCommand*`, `docs/cli-commands.json` (`app-commands.md`).
19. **Commands from other devices** - `Remote*`, command listener; the person's switch, off by default (`remote-control.md`).

## Read this before touching X

Claude Code loads a rule file when a matching file is opened; any other agent opens it by hand. All files are in
`.claude/rules/`; module notes of a class are in the file of its area. Features: see the list above. Other areas:

| Before touching | Read |
| --- | --- |
| Any UI string, `Localization*`, `tools/l10n` | `localization.md` |
| `Program`, `CyrFlipContext`, session end, autostart, keep awake, data folder, `AppConfig` | `app-shell.md` |
| Meaning of any registry value | `config.md` |
| `ClipboardHistory*` | `clipboard-history.md` |
| Support bundle, mail, diagnostic logs | `diagnostics-support.md` |
| Help, `help-map.json`, `tools/help` | `help.md` |
| `docs/` pages, `tools/site` | `site.md` |
| `PLAN/` tickets and the specification | `tickets.md` |
| `build.ps1`, check tiers and timings, UI sweeps | `build-test.md` |
| `tests/`, `tools/uitest`, `tools/audit` | `testing.md` |
| Release, `msix/`, `winget/`, workflows, Store preflight | `release.md` |

## Tech stack & hard constraints

- **Target framework: .NET Framework 4.8** (`net48`), C# with `<LangVersion>latest</LangVersion>`. Chosen because 4.8 is preinstalled on every supported Windows 10/11, so the app ships as a single small `.exe` with **nothing to install** - the user's "full Win10 support, one EXE, no dependency" requirement. **Caveat:** net48 caps usable C# features (officially ~7.3); `latest` is enabled but features needing runtime support (records, `init`, ranges) require polyfills. This is a deliberate trade vs the spec's "C# 11+".
- Built with the **.NET SDK** (`dotnet build`/`test`), not classic msbuild. net48 reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies` package, so no VS targeting pack is required.
- Heavy use of Win32 via **P/Invoke** (`[DllImport]`) - concentrated in `WindowInterop.cs`. This is the core of the app, not an edge case.
- **No NuGet runtime dependencies** in the app project - config parsing uses the framework's `System.Web.Script.Serialization.JavaScriptSerializer` (a `System.Web.Extensions` reference), not `System.Text.Json`, to keep the exe self-contained without ILMerge.
- Memory budget: **< 50 MB** idle.
- Keyboard hook callback must be **non-blocking and < 1 ms** - do real work off the hook thread; never block inside the `WH_KEYBOARD_LL` callback.
- `app.manifest` declares **PerMonitorV2 DPI awareness** (so rendered icons/labels are crisp on high-DPI) and Win10/11 `supportedOS`.

## Rules for every code change

Each class owns one concern (keep it this way - the spec prioritizes a minimal surface):

**Never lay a dialog out by pixel coordinates.** Captions exist in 13 languages and are drawn at whatever
display scaling makes of the UI font, so fixed geometry clips them - the bug that had "Assign" rendering as
"Assig" and `Ctrl+Shift+F12` as `Ctrl+Shift+F1`. `HotkeyDialog` and `LayoutConversionDialog` use an
auto-sizing `TableLayoutPanel` in an `AutoSize` form; `SettingsForm.RowHeight` derives from `Font.Height`
and `MeasureConversionColumns` measures the text that is actually going to be drawn. `DialogLayoutTests`
guards it.

**COM vtable gotchas (`UiaCaretCom.cs`, `Ia2Caret.cs`):** both use hand-declared COM interfaces on Windows components (`CUIAutomation`, `oleacc`/IAccessible2) - **no new dependency**, so winget/MSIX shipping is unaffected. Method order must match the headers exactly: in `IUIAutomation`, **`ElementFromHandle`/`ElementFromPoint` sit between `GetRootElement` and `GetFocusedElement`**, so `GetFocusedElement` is **slot 6** (not 4) - getting it wrong silently calls `ElementFromHandle` with a bogus HWND and returns `E_POINTER`. `IAccessible` is laid out after IDispatch's 4 methods (`get_accName` slot 8, `get_accFocus` slot 16). Both modules guard calls with `[HandleProcessCorruptedStateExceptions]` so a vtable mishap degrades to the next source instead of crashing.

- Every UI string goes through `Localization*.cs` in all 13 languages; code and keys are English (`localization.md`).
- A new Form, settings page or tour step gets a `help-map.json` row; help is regenerated with UI changes (`help.md`).
- A new or changed registry value goes into the Config table below and into `config.md`.
- Generated, never hand-edited: help PNGs, `Glyphs.g.cs`, `Symbols.g.cs`, `docs/CLI.md`, the listing copy in
  `msix/store-listings.md` and `store/listing-*.txt`, the landing release-facts block.
- `SetSystemCursor` is global: read `indicator-caret.md` before touching cursor code.
- A user-visible change updates every surface listed in `DOCS_SURFACES.md` (the README triplet, guides, site,
  listing sources). An architecture, module or invariant change is written into the area rule file, not into this file.

## Error-handling rules (from spec)

- No selection → no-op (skip silently).
- Clipboard locked → retry up to **12 attempts, 15 ms apart** (`Win32Clipboard.cs:444`).
- Foreground window changes mid-operation → **cancel** the operation.
- Clipboard that holds something but cannot be backed up → **refuse** the operation before a key is sent
  (the "clipboard had other plans" balloon), never overwrite it (S0009 FP-5).

## Config

Settings are stored in the Windows Registry under `HKCU\Software\CyrFlip`. All values have defaults and are written on first change. On first run with no registry key, the app migrates from a legacy `config.json` if one is present. Names, types and defaults are held here (a test reads this table); what each value means is in
`.claude/rules/config.md`.

| Registry value | Type | Default |
| --- | --- | --- |
|`CaseHotkey`|`REG_SZ`|`Ctrl+Shift+F11`|
|`UiLanguage`|`REG_SZ`|Windows UI language, else `English`|
|`ClipboardHistoryHotkey`|`REG_SZ`|`Ctrl+Shift+F10`|
|`EnableClipboardHistory`|`REG_DWORD`|`0`|
|`ClipboardHistoryNoticeShown`|`REG_DWORD`|`0`|
|`PauseClipboardHistory`|`REG_DWORD`|`0`|
|`ShowClipboardHistoryOnStartup`|`REG_DWORD`|`1`|
|`ClipboardHistoryX` / `ClipboardHistoryY`|`REG_DWORD`|`MinValue` / `MinValue`|
|`ClipboardHistoryWidth` / `ClipboardHistoryHeight`|`REG_DWORD`|`260` / `360`|
|`ClipboardHistoryOpacity`|`REG_DWORD`|`100`|
|`SettingsTab`|`REG_DWORD`|`0`|
|`SettingsPageId`|`REG_SZ`|*(empty)*|
|`SettingsGroupsState`|`REG_SZ`|*(empty)*|
|`SettingsViewport`|`REG_SZ`|*(empty)*|
|`SettingsWindowX` / `SettingsWindowY`|`REG_DWORD`|`int.MinValue`|
|`SettingsWindowWidth` / `SettingsWindowHeight`|`REG_DWORD`|`0`|
|`CursorSize`|`REG_DWORD`|`24`|
|`EnableCursorChange`|`REG_DWORD`|`0`|
|`CursorNoticeShown`|`REG_DWORD`|`0`|
|`EnableCaretOverlay`|`REG_DWORD`|`1`|
|`CaretDotMode`|`REG_DWORD`|`0`|
|`EnableLanguageSwitch`|`REG_DWORD`|`0`|
|`FlipCapsLockAfter`|`REG_DWORD`|`0`|
|`ConvertSymbols`|`REG_DWORD`|`1`|
|`KeepSystemAwake`|`REG_DWORD`|`0`|
|`KeepScreenOn`|`REG_DWORD`|`0`|
|`EnableHotkeys`|`REG_DWORD`|`1`|
|`EnableCaseHotkey`|`REG_DWORD`|`1`|
|`EnableHistoryHotkey`|`REG_DWORD`|`1`|
|`DeferToRemoteDesktop`|`REG_DWORD`|`0`|
|`EnableContextMenu`|`REG_DWORD`|`0`|
|`ContextMenuChord`|`REG_SZ`|`Ctrl+RightClick`|
|`TextMenuHotkey`|`REG_SZ`|`Ctrl+Shift+Alt+M`|
|`EnableTextMenuHotkey`|`REG_DWORD`|`1`|
|`Theme`|`REG_SZ`|`system`|
|`UiInputProfile`|`REG_SZ`|`pointer`|
|`UiInterfaceScale`|`REG_DWORD`|`100`|
|`UiTextScale`|`REG_DWORD`|`100`|
|`LanguageHotkeysBackup`|`REG_SZ`|*(empty)*|
|`InputLayoutsBackup`|`REG_SZ`|*(empty)*|
|`InputMethodOverrideWritten`|`REG_SZ`|*(empty)*|
|`LayoutConversionProfiles`|`REG_SZ`|*(seeded)*|
|`EnableScenarioLauncher`|`REG_DWORD`|`0`|
|`LauncherFirstEnableDone`|`REG_DWORD`|`0`|
|`DataFolderMigrated`|`REG_DWORD`|`0`|
|`WelcomeShown`|`REG_DWORD`|`0` on a new profile; read as `1` for an existing key|
|`ShortcutsCreated`|`REG_DWORD`|`0`|
|`EnableTranslate`|`REG_DWORD`|`0`|
|`TranslateSeeded`|`REG_DWORD`|`0`|
|`TranslateProfiles`|`REG_SZ`|*(empty)*|
|`TranslateEndpoint`|`REG_SZ`|*(empty)*|
|`TranslateModel`|`REG_SZ`|`aya-expanse:8b`|
|`TranslateTimeoutSeconds`|`REG_DWORD`|`120`|
|`TranslateKeepAliveMinutes`|`REG_DWORD`|`-1`|
|`TranslateAutoStartServer`|`REG_DWORD`|`1`|
|`TranslateCopyResult`|`REG_DWORD`|`0`|
|`TranslatePasteResult`|`REG_DWORD`|`0`|
|`TranslateShowSource`|`REG_DWORD`|`0`|
|`TranslateWindowTimeout`|`REG_DWORD`|`0`|
|`TranslateWindowWidth` / `TranslateWindowHeight`|`REG_DWORD`|`700` / `460`|
|`TranslateWindowOpacity`|`REG_DWORD`|`100`|
|`EnableQuickNotes`|`REG_DWORD`|`0`|
|`QuickNotesNoticeShown`|`REG_DWORD`|`0`|
|`QuickNotesHotkey`|`REG_SZ`|`Ctrl+Shift+Alt+N`|
|`EnableQuickNotesHotkey`|`REG_DWORD`|`1`|
|`QuickNotesWordWrap`|`REG_DWORD`|`0`|
|`QuickNotesLineNumbers`|`REG_DWORD`|`0`|
|`QuickNotesSync`|`REG_DWORD`|`0`|
|`EnableExternalCommands`|`REG_DWORD`|`0`|
|`QuickNotesSyncEntry`|`REG_SZ`|*(empty)*|
|`RemoteCommandsListen`|`REG_DWORD`|`0`|
|`ExternalControlX` / `ExternalControlY` / `ExternalControlWidth` / `ExternalControlHeight`|`REG_DWORD`|`MinValue` / `MinValue` / `0` / `0`|
|`RemoteCommandGrants`|`REG_SZ`|*(empty)*|
|`ExchangeNotesWarningOff`|`REG_DWORD`|`0`|
|`SettingsBackupEnabled`|`REG_DWORD`|`1`|
|`BackupOfferOpen`|`REG_DWORD`|`0`|
|`BackupRestoreDeclined`|`REG_SZ`|*(empty)*|
|`QuickNotesX` / `QuickNotesY` / `QuickNotesWidth` / `QuickNotesHeight`|`REG_DWORD`|`MinValue` / `MinValue` / `0` / `0`|
|`QuickNotesSelected`|`REG_SZ`|*(empty)*|
|`FlipCount`|`REG_DWORD`|`0`|
|`CaseFlipCount`|`REG_DWORD`|`0`|
|`TranslateCount`|`REG_DWORD`|`0`|
|`QuickNoteCount`|`REG_DWORD`|`0`|
|`ScreenshotHotkey`|`REG_SZ`|`Ctrl+F12`|
|`EnableScreenshotHotkey`|`REG_DWORD`|`1`|
|`ScreenshotFullHotkey`|`REG_SZ`|`Ctrl+F11`|
|`EnableScreenshotFullHotkey`|`REG_DWORD`|`1`|
|`EnableScreenshotAnnotate`|`REG_DWORD`|`1`|
|`ScreenshotAnnotateAreaHotkey`|`REG_SZ`|`Ctrl+Alt+F12`|
|`EnableScreenshotAnnotateAreaHotkey`|`REG_DWORD`|`1`|
|`ScreenshotFullAnnotateHotkey`|`REG_SZ`|`Ctrl+Alt+F11`|
|`EnableScreenshotFullAnnotateHotkey`|`REG_DWORD`|`1`|
|`ScreenshotFolderHotkey`|`REG_SZ`|`Ctrl+F10`|
|`EnableScreenshotFolderHotkey`|`REG_DWORD`|`1`|
|`ScreenshotSaveEnabled`|`REG_DWORD`|`1`|
|`ScreenshotKeyMigrationDone`|`REG_DWORD`|`0`|
|`ScreenshotFolder`|`REG_SZ`|*(empty)*|
|`ScreenshotCount`|`REG_DWORD`|`0`|
|`EnableSymbols`|`REG_DWORD`|`0`|
|`SymbolsHotkey`|`REG_SZ`|`Ctrl+Shift+F9`|
|`EnableSymbolsHotkey`|`REG_DWORD`|`1`|
|`SymbolsCategory`|`REG_SZ`|`all`|
|`SymbolsCloseOnPick`|`REG_DWORD`|`0`|
|`SymbolsHideTypable`|`REG_DWORD`|`0`|
|`SymbolsX` / `SymbolsY` / `SymbolsWidth` / `SymbolsHeight`|`REG_DWORD`|`MinValue` / `MinValue` / `0` / `0`|
|`SymbolsCount`|`REG_DWORD`|`0`|
|`EnableTrafficLight`|`REG_DWORD`|`0`|
|`TrafficLightHotkey`|`REG_SZ`|`Ctrl+Shift+Alt+T`|
|`EnableTrafficLightHotkey`|`REG_DWORD`|`1`|
|`TrafficLightTargets`|`REG_SZ`|*(empty; a brand-new profile is seeded with one `Google` example, `https://www.google.com`)*|
|`TrafficLightShowAtStart`|`REG_DWORD`|`1`|
|`TrafficLightVisible`|`REG_DWORD`|`0`|
|`TrafficLightPinned`|`REG_DWORD`|`1`|
|`TrafficLightTileSize`|`REG_DWORD`|`1`|
|`TrafficLightColumns`|`REG_DWORD`|`3`|
|`TrafficLightWindowTransparency`|`REG_DWORD`|`0`|
|`TrafficLightX` / `TrafficLightY`|`REG_DWORD`|`MinValue` / `MinValue`|
|`EnableExchangeClipboard`|`REG_DWORD`|`0`|
|`ExchangeClipboardSendHotkey`|`REG_SZ`|*(empty)*|
|`EnableExchangeClipboardSendHotkey`|`REG_DWORD`|`1`|
|`ExchangeClipboardGetHotkey`|`REG_SZ`|*(empty)*|
|`EnableExchangeClipboardGetHotkey`|`REG_DWORD`|`1`|
|`ExchangeClipboardReceivedFolder`|`REG_SZ`|*(empty)*|
|`ExchangeClipboardAccounts`|`REG_SZ`|*(empty)*|

Legacy `config.json` (still accepted on first run for migration):
```json
{ "hotkey": "Ctrl+Shift+F12", "cursorSize": 24 }
```

## Build, run, test

**Between releases only checks of 5 seconds or less run** (S0081, `TESTING_AND_QA` section 2): the full
build and every slower test run only at the release, or when the owner names that run. A slow rung a
ticket skips goes into its audit as a `RELEASE` item naming the command the release will run, and the
report says "not built, not tested until the release".

```powershell
.\build.ps1 -Fast                             # FAST tier, after every edit: the static gates, no build, no test (~4 s)
.\build.ps1 -Fast -Area help                  # one area: placement | help | positioning | site | listing | cli
.\build.ps1                                   # the owner's СБОРКА: app-only build + stage single exe + deploy to C:\GD\..
.\build.ps1 -Tests                            # RELEASE tier, owner-named only: whole solution + dotnet test + static gates
.\release.ps1                                 # the release preflight: full build, every tier, IconGen, listing and help gates
.\src\CyrFlip\bin\Release\net48\CyrFlip.exe   # run the app (tray icon → right-click → Exit)
dotnet run --project tools/IconGen            # regenerate icon/banner assets
```

- `tools/uitest/*` and `tools/site/Measure-SiteContracts.ps1` are owner-named only: desktop-bound, minutes long,
  some drive the running app or borrow the real clipboard.
- The 13-language UI sweeps run only with `CYRFLIP_SWEEP=full` set by hand, and only when the owner names that run:
  no script or workflow sets it. A new test that builds a `SettingsForm` or loops a window over languages or pages is
  `[UiSweepFact]` / `[UiSweepTheory]` from the start.
- Clipboard pipeline tests run over `FakeClipboard`, never the real clipboard; other test traps are in `testing.md`.
- The measured check list and its timings: `build-test.md`.

Version is stamped at build time as `YY.M.D.HHmm` via a `<Version>` property in the csproj (verified: e.g. `26.6.11.1649`).

## Unified Rules & release conventions

The **universal** SZA conventions (repository layout, versioning principle, documentation, development
discipline, testing, release & distribution, localization, security, support, AI-agent operation) live
in one canonical home - reference it, do not restate it here:

**Canon:** the **`sza-unified-rules` repo**, installed as the `sza` Claude Code plugin, so the rules and
the skills that apply them are in every session without a path to follow:

```
/plugin marketplace add SerZhyAle/sza-unified-rules
/plugin install sza@sza-unified-rules
```

Load a skill rather than the whole canon: `sza:release`, `sza:store-publish`, `sza:feature-to-site`,
`sza:spec-to-audit`, `sza:adopt-canon`, `sza:agent-cost`, `sza:caveman`. CyrFlip is **Overlay A - Windows desktop, no-installer variant**,
plus a **companion editor extension** on the VS Code Marketplace. The per-project record with full
evidence is `rules/contrib/cyrflip.md` in that repo; the adoption stamp is `.sza-canon.json` here. This
project **references** the canon (does not mirror it); only the CyrFlip-specific deltas below stay local.

**The stamp is written only by the `sza:adopt-canon` run** (`REPO-STAMP` rule 8). When the plugin moves but the
coreDigest does not, nothing is owed - never bump `canon.version` by hand to quiet the gate (commit `468ffb8`
once did exactly that). A `SZA-CANON03` warning means: re-read the changed rule docs, reconcile, then let the run
rewrite the version and the digest together.

**Two canon mechanisms nothing here re-implements:**
- **Enforcement hooks.** The plugin ships and registers its own guard family, so this repo registers
  **none**: `.claude/settings.json` carries permissions only, and there is no `hooks/` folder here. Never
  hand-wire a guard the plugin already ships - it would then run twice, and the canon's inventory gate
  cannot see a machine-local registration to tell you so.
- **The release package plan** (RELEASE_AND_DISTRIBUTION §8) is **adopted by hand (2026-09-25)**:
  `PLAN/RELEASE_QUEUE.md` (work remaining, owner's order, packages `1`/`2`/`3`/`--`) and
  `PLAN/RELEASE_READY.md`, one line per `CYRFLP-####` ticket. Status changes only through the shared ticket
  tool (`update.ps1 -Id <id> -Status <s>`); never edit the `**Status:**` line by hand. The tool syncs the
  spec header and journal together. An agent never reorders the queue or rewrites a package.

**Release anchors and traps** (full text: `.claude/rules/release.md`, read it before any release work):

- `build.ps1` is the free local build and **never tags**; `release.ps1` is the only tagger. Pushing the `vYY.M.D.HHmm`
  tag (no zero-padded month or day) starts the billable `release.yml`. A published tag is never rebuilt.
- Frozen anchors: winget `SerZhyAle.CyrFlip`; MSIX Identity `SZA.CyrFlip` with the publisher in `msix/identity.json`;
  Store ID `9NB4W41NGQJ4`; exe and assembly `CyrFlip`; VS Code extension id `SerZhyAle.cyrflip-vscode`.
- Store-listing source of truth is `msix/store-listing-export.csv` (plus upstream `msix/listing/<lang>.txt` - edit
  both). The Store upload is manual; the VS Code extension is published by hand on its own semver.
- Releases ship unsigned - do not claim they are signed.

**Local specifics (kept):**
- **Menu strings keep the Win32 trailing `...`** - `Настройки...`, `Импорт из OneClickRunner...`,
  `Diagnose caret position...`: roughly 200 strings across the 13 languages where the ellipsis is the
  platform's "this opens a dialog" signal rather than prose. The house style's `..` governs prose, and
  the docs quote those strings verbatim, which is why the compliance gate warns on four documentation
  lines - each of them is the gate's own "a quoted real UI string is legitimate" case. Changing them
  would be a user-visible UI decision in 13 languages, not a typography fix.
  <!-- canon-ok: a platform UI convention this repo follows, not a restatement of the house style -->
- Single-instance via a **path-independent named mutex** (`Local\CyrFlipSingleInstance`), so debug/release/
  renamed copies don't each install a hook.
- Code style is **wired in the csproj**: `<Nullable>enable</Nullable>` + a fixed `<WarningsAsErrors>` list
  (`CS0108;CS0162;CS0164;CS0168;CS0219;CS0414;CS0649;CS1998`). Keep clean builds warning-free.
- `build.ps1` deploys the staged exe to the local sync folders `C:\GD\i\` and `C:\GD\tc\SZA\_APP\`.
- Interop pieces (hook, clipboard flip, layout detection) still need **manual validation** - run the tray
  exe and exercise the hotkeys in real apps; xUnit only covers the pure logic.
