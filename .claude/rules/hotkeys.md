---
paths:
  - "src/CyrFlip/KeyboardHook.cs"
  - "src/CyrFlip/Chord*.cs"
  - "src/CyrFlip/KeyInjection.cs"
  - "src/CyrFlip/Hotkey*.cs"
  - "src/CyrFlip/TrayChordList.cs"
  - "src/CyrFlip/TrayHover*.cs"
  - "src/CyrFlip/TrayHotkeysPopup.cs"
  - "src/CyrFlip/SettingsForm.HotkeysOverview.cs"
  - "src/CyrFlip/CyrFlipContext.ChordOverview.cs"
---

# Keyboard hook, chords and the hotkeys overview

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

13. **The hotkeys overview - the "Горячие клавиши" page (tickets S0056 and S0062).** One settings page - the master
   switch and the RDP option in its first group `hotkeys/switches`, then a list of **every** chord CyrFlip binds in six groups (`keys/fixed`, `keys/mouse`, `keys/conversions`,
   `keys/translations`, `keys/launcher`, `keys/windows`), says for each whether it **really fires and if not why**
   (`ChordBindingState`: its own switch, its module, the master switch, no chord, a token that does not parse; a
   **clash** is a second fact shown on both rows, since a switched-off chord still owns its chord), and lets the
   user change, switch and reset each from there. **It stores nothing and has no second setter**: a chord change
   opens the owning page's own flow (`HotkeyDialog` + `ChordGuard`, then the same setter), a switch calls the same
   handler as the owning page, and a refresh re-reads every control without firing a handler, so the page and
   the owning page are one value. The state is `ChordBinding.Evaluate` - the predicate the tray hover popup uses - and the five
   `*ChordLive` properties of `CyrFlipContext` now call `ChordBinding.*Live`, so the hook and the overview are made
   from one condition. **Reset** (`ChordResetPlan`, pure): a chord token and that chord's own switch, nothing else -
   never a module switch, a row's language, the number of rows or a scenario's program; the seeded EN⇄RU and
   translator starter rows get their shipped chord back **only while they are still that row**
   (`ChordDefaults.IsSeeded*`), a row the user made is only **cleared** (worded that way); a shipped chord another
   CyrFlip action holds is **not stolen** but reported; Windows' owners are asked about once; the plan is computed
   once, shown (`ConfirmDialog`, Yes in the `danger` role only when a chord changes, No by default) and the **same
   object** applied with every row re-checked, a stale one skipped and reported. "Сбросить все..." also turns the
   master switch back on (owner decision D4). The mouse chord is edited through a menu of the fixed six choices. No
   registry value is added. Decisions taken by the recommended defaults: D1 name "Все комбинации" after the Hotkeys
   page (**superseded 2026-10-11 by S0062 D3**: the old page is dissolved - each chord moved to the page of its feature - so the
   overview took the title "Горячие клавиши" back and the master switch moved onto it, one control, no duplicate),
   D2 the one-time shift of a remembered page index (**no longer happens**: S0062 reads `SettingsPageId` by key and a bare
   `SettingsTab` index through the pre-S0062 order table once), D3 no tab glyph until the catalog has a record (the
   merged page keeps `app.shortcuts`), D4 reset turns the master on, D5 no filter, D6 (**gone with S0062**: the old
   page and its chord rows no longer exist). **Delete on a fixed chord does nothing** (it cannot be left empty; the spec's "asks first" is not
   built). Spec: [PLAN/done/S0056_hotkeys-overview-tab.md](../../PLAN/done/S0056_hotkeys-overview-tab.md).

## Modules (`src/CyrFlip/`)

- **KeyboardHook.cs** - `SetWindowsHookEx(WH_KEYBOARD_LL)` wrapper. The callback never fires on an injected event (`LLKHF_INJECTED`) so our own `SendInput` can't re-enter it, matches the chords against a **physical modifier table** (`ChordMatcher`, below - never `GetAsyncKeyState`, which our own injected key-ups falsify), sees key-**ups** too so a fired trigger's auto-repeats and release are swallowed with it, raises `CaseHotkeyPressed` (case flip), `ClipboardHistoryHotkeyPressed`, `QuickNotesHotkeyPressed`, `ScreenshotHotkeyPressed`, `ScreenshotFolderHotkeyPressed`, `TextMenuHotkeyPressed`, `LayoutConversionHotkeyPressed(id)`, `LauncherHotkeyPressed`, `TranslateHotkeyPressed`, or `ChordFired`, and returns `1` to **swallow** the trigger key. **Every subscriber posts to the UI thread and does nothing inside the callback** (`CyrFlipContext` wraps all of them in `_ui.Post`) - file I/O, showing a window or starting a thread in there runs while the whole machine's keyboard waits. **`Reinstall()` exists because Windows drops a low-level hook that overran `LowLevelHooksTimeout` (~300 ms) and tells nobody**: there is no API to ask whether we are still hooked, so `CyrFlipContext._hookWatchdog` re-arms both hooks every 60 s (a WinForms timer, since a hook may only be removed by the thread that installed it), and three consecutive failures earn one balloon. That silent drop is the "my hotkeys stopped working after a while" report; the hook is also installed only *after* `_ui` is captured, or a chord pressed during startup would be swallowed and then dropped. Gated by a **master switch** (`_enabled` - when off the hook passes every key through) and **per-hotkey switches** (`_caseEnabled`/`_historyEnabled`/`_quickNotesEnabled` for the three fixed chords; each conversion row carries its own `Enabled`). On a real chord match, if **`_deferInRemoteClient`** is on and `RemoteDesktop.IsClientForeground()`, it does **not** swallow the key - it lets the chord travel to the remote session (avoids the double-instance clash when CyrFlip runs on both ends of an RDP connection; see `RemoteDesktop.cs`). `Install(caseFlip, history, quickNotes, deferInRemoteClient, enabled, caseEnabled, historyEnabled, quickNotesEnabled)`; `UpdateCaseHotkey`/`UpdateClipboardHistoryHotkey`/`UpdateQuickNotesHotkey`/`UpdateConversionProfiles`/`UpdateTranslationProfiles` and `UpdateEnabled`/`UpdateCaseEnabled`/`UpdateHistoryEnabled`/`UpdateQuickNotesEnabled`/`UpdateDeferInRemoteClient` swap fields at runtime (safe from any thread - the callback reads the fields on each invocation). One key is watched that is **not** a chord: while `UpdateCancelKeyWatch(true)` is on - only while a translation is streaming - a bare **Escape** raises `CancelKeyPressed` and is **passed through**, never swallowed. The translation popup deliberately never takes focus, so it can never receive a key of its own; without this branch the "Esc to cancel" it promises could not work at all. A second such key, **F6**, is watched only while the finished popup is up (`UpdateFocusKeyWatch`, S0045 K1) and, unlike Escape, **is** swallowed - a bare F6 only; the matcher then owns its repeats and release like a chord trigger's - raising `FocusKeyPressed`: the popup's way in and back out (`TranslationResultWindow.ToggleKeyboardFocus`), since it never takes the focus by itself.
- **ChordMatcher.cs / KeyInjection.cs / HotkeyRules.cs / ChordRegistry.cs** - the pure halves of the
  hook and chord rules (ticket S0004), each unit-tested without a desktop. `PhysicalModifiers` is the
  eight side-specific modifier flags the hook keeps from the key events it sees; **CyrFlip's own
  injections carry `KeyInjection.Tag` in `dwExtraInfo` and never touch it**, while another tool's
  injected modifiers do count (a CapsLock→Ctrl remapper has to keep working). AltGr's fake LCtrl (scan
  `0x21D`) is tracked apart and matches **no** chord - it is not Ctrl+Alt. **The table is reconciled
  downwards** before a chord is matched and before every injection plan (ticket S0033 KC2-1): an up the
  hook never saw (Ctrl+Alt+Del → Cancel, a release over an elevated window) is dropped when
  `GetAsyncKeyState` says the key is up - **except a key CyrFlip itself released**, marked when the hook
  sees our tagged up (`TrackOwn`) and trusted to the table until the system reports it down again,
  since that "up" is the one lie KC-1 is about. So a plan never reuses a Ctrl the system does not
  confirm, and never leaves one logically down. `ChordMatcher` owns a fired trigger until its key-up
  (repeats and release swallowed; a gap longer than the keyboard's initial repeat delay + 500 ms,
  `SPI_GETKEYBOARDDELAY`, means the up was lost - KC2-2), and a watchdog re-arm keeps a trigger still
  inside that window: the system never saw the swallowed down, so its own state cannot be asked.
  `HotkeyDialog` sets `KeyboardHook.SuspendChords` while it is open, so the capture sees CyrFlip's own
  chords instead of running them (KC2-4).
  `KeyInjection` plans every synthesized key: side-specific VKs with real scan codes and the extended
  flag, only what is held, and the **mask key `vkE8`** tapped while modifiers are down - the swallowed
  trigger otherwise leaves Windows a bare Ctrl+Shift / Alt+Shift / Alt / Win release, which switches
  the layout, opens the menu bar or the Start menu (`KeyboardHook.ChordFired` → the context taps it on
  the UI thread). `HotkeyRules` is what `HotkeyDialog` refuses: Shift alone off F1-F24, the editing
  and system chords (Ctrl+C/V/X/Z/Y/A/S/Insert, Ctrl+Space - the Pinyin toggle - Ctrl+Backspace/Delete/
  Home/End, Alt+F4, Alt+Space; KC2-7), and a Ctrl+Alt chord that types a character on **any installed** layout. `ChordRegistry` is
  the one ownership check behind every setter and every enable switch - **a switched-off chord still
  owns its chord**, the master switch does not gate the check, and Windows' direct-language hotkeys are
  read-only owners the user is *asked* about rather than refused. The re-arm rule both hooks share:
  `_wanted` says whether the hook should exist, and the new hook goes in before the old one comes out -
  a failure keeps the old handle and keeps returning false, so the watchdog's counter really climbs.

- **Hotkey.cs** - parses `"Ctrl+Shift+F12"` → modifiers + VK (+ named keys: Space/Enter/F1-F24/..); `Display` round-trips it. **The admitted trigger families of INPUT-CHORD 0.4 (S0116)** - the arrows, `Numpad0`-`Numpad9` and the keypad operators, `BracketLeft`/`BracketRight`/`Period`/`Comma`/`Equal`/`Minus`/`Backslash` and the six media and volume keys - are one table (`AdmittedTriggers`) read case-insensitively (0.5 item A) and named back by `NameForVk`; `HotkeyDialog.TryGetKeyName` captures them, `HotkeyRules` refuses `Ctrl` alone with an arrow (caret movement), and `HotkeyDialog.LongestAdmittedChord` sizes the capture preview (`LongestChord` still aligns the settings columns). A keypad digit with Shift never arrives as itself while NumLock is on (Windows reports the navigation key), so such a stored chord never fires. `Hotkey.Default` is **Ctrl+Shift+F12** - the `Parse` fallback, and the chord the seeded EN⇄RU conversion row carries (Ctrl+Shift+T was dropped - it conflicts with browser "reopen tab" and Windows text-extraction tools). `Hotkey.CaseDefault` is **Ctrl+Shift+F11** (case flip). `SameChord` compares trigger key + modifiers (used to reject duplicate hotkeys). **The stored token is `INPUT-CHORD` rules 1-4**: an unknown token or a second trigger makes `TryParse` fail - the chord is inert, never the rest of it bound (`Ctrl+Shift+Hyper+F12` must not become `Ctrl+Shift+F12`) - and stored chords are re-written in the canonical spelling on load (`AppConfig.ReadHotkey`, `AppConfig.CanonicalizeChord`). `InputChordContractTests` generates the contract's vectors (`tests/CyrFlip.Tests/Fixtures/InputChord/chord-tokens.jsonl`, rewritten with `CYRFLIP_WRITE_CHORD_VECTORS=1`) and fails when file and parser disagree; the catalog holds a byte-exact copy.

- **ChordDefaults.cs / ChordOverviewModel.cs / ChordResetPlan.cs / SettingsForm.HotkeysOverview.cs /
  CyrFlipContext.ChordOverview.cs / Localization.HotkeysOverview.cs** - the hotkeys overview (feature 13), one concern
  each. `ChordDefaults` is the shipped chord per binding (the constants of `AppConfig`, `MouseChord.Default`) and the
  seeded-row identity; `ChordConfigAccess` (in `ChordOverviewModel.cs`) is the one reader/writer of a fixed chord and
  its switch on `AppConfig`, so a `ChordKind` added without an entry fails loudly. `ChordOverviewModel.Build` takes the
  config, the scenarios and Windows' records as arguments and returns `ChordOverviewRow`s - no WinForms, no registry.
  `ChordResetPlan` is the value described under feature 13 (`ForAll`, `ForRow`, `Apply`). `SettingsForm` is a
  **partial** class so the page lives in its own file (the form is already large); it builds rows from the model on
  every `ApplyLanguage`, keeps its focus through `RowFocus`, and routes edits through the owning page's setters.
  `CyrFlipContext.OnChordsEdited` is what a reset ends in: save, hand the hook **every** chord and switch again,
  refresh the launcher surfaces. `ChordOverviewModelTests`, `ChordResetPlanTests` and `HotkeysOverviewPageTests` hold it
  (the inventory gate fails the build when a chord has no row; the page is built in 13 languages with nothing clipped;
  "both views one value" with no handler fired by a refresh); `tools/uitest/Test-HotkeysOverview.ps1` stages a state on
  the real settings (backup first, refuses without it, restores in `finally`) and ends in the human checklist.
