---
paths:
  - "src/CyrFlip/SettingsForm*.cs"
  - "src/CyrFlip/SettingsPage*.cs"
  - "src/CyrFlip/SettingsRail.cs"
  - "src/CyrFlip/UiProfile.cs"
  - "src/CyrFlip/UiMetrics.cs"
  - "src/CyrFlip/PageNavigationTrace.cs"
  - "src/CyrFlip/SettingsLoadingNotice.cs"
  - "src/CyrFlip/ConfirmDialog.cs"
  - "src/CyrFlip/CollapsibleGroup.cs"
  - "src/CyrFlip/RowFocus.cs"
  - "src/CyrFlip/HotkeyDialog.cs"
  - "src/CyrFlip/Welcome*.cs"
  - "src/CyrFlip/FirstRun.cs"
  - "src/CyrFlip/SettingsTransferDialog.cs"
  - "src/CyrFlip/SettingsFile*.cs"
  - "src/CyrFlip/SettingsImportPlan.cs"
  - "src/CyrFlip/SettingsBackup.cs"
  - "src/CyrFlip/AboutResources.cs"
---

# Settings window, dialogs, size profile, welcome tour and settings file transfer

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Settings transfer and welcome tour

- **Settings file transfer (S0055 settings-file)** - General-page export/import/restore of private JSON v2 (S0072), with the v1 reader retained. The allow-list includes app chords, module preferences, geometry, traffic-light targets and verbatim scenario XML; recorded content, credentials and Windows input state stay local. Default-on rolling backups keep three files in Documents/CyrFlip/Backups, gated until the startup restore question is settled. Restore uses the import preview and transaction; deferral preserves the recovery backup. Import previews the resulting configuration, requires explicit recording/remote-server consent, and keeps three local snapshots. `SettingsFile*`, `SettingsImportPlan`, `SettingsSnapshot`, `SettingsTransferDialog` and `CyrFlipContext.SettingsTransfer` own the format, validation, transaction, preview and live refresh. `SettingsBackupEnabled` is exported; `BackupOfferOpen` and `BackupRestoreDeclined` are local machine history. `settings-file.log` holds counts, key names and reason codes only; backups never enter a support bundle.

- **First-run welcome tour (S0055)** - five pages on a new profile, immediate language/autostart edits and live feature switches (each flip applied like the settings window applies it), and a General-page settings handoff. Skip/close keeps defaults; About replays it. Existing or migrated profiles are never nagged; an explicit `WelcomeShown=0` requests replay.

## Features

14. **The settings pages follow the features (ticket S0062).** A setting and a chord live on the page of the feature they
   configure (rule D1, held by `ChordHomeTests` and the move ledger `SettingsLedgerTests`): the case fix (chord and CapsLock
   sync) is on **"Исправление текста"** (the old "Конвертация раскладок", group ids `conversions/table|after|symbols|case`),
   the history chord on **"Буфер обмена"** (`clipboard/recording|window|clear`), the context menu, its mouse chord and its
   keyboard chord on the new **"Меню для выделенного"** page (`selection/mouse|keyboard`, `SettingsPageKey.SelectionMenu`, tab kind 14,
   glyph `feature.selection-menu`, the record asked on 2026-10-11 - until then it drew its caption alone, D7), and "Общие" has four groups (`general/language|startup|power|transfer`). The old "Горячие клавиши" page is
   dissolved: its master switch and RDP option are the first group (`hotkeys/switches`) of the overview page, which
   keeps the key `SettingsPageKey.Hotkeys` and the glyph `app.shortcuts` (`HotkeysOverview` is a retired key, read as
   `Hotkeys`; tab kind 13 is gone). The page order is one table, `SettingsPageKeys.DisplayOrder`, in the pillar order of
   `POSITIONING.md`, About last. Navigation is remembered by **key**: `SettingsViewport` rows are written `<PageKey>|anchor|offset`
   and a legacy numeric row (and a bare `SettingsTab` index) is read once through `SettingsPageKeys.FromLegacyIndex`, the
   pre-S0062 order table. The auto "Параметры" group is keyed by the page key and only "Индикаторы" has loose rows. Settings
   file inventory group ids moved with the settings (preview captions only; the file format is untouched). Spec:
   [PLAN/done/CYRFLP-0062_settings-tab-regrouping.md](../../PLAN/done/CYRFLP-0062_settings-tab-regrouping.md).

17. **The size profile and the settings page rail (ticket S0076, `UI-SIZING` 1.1, `SETTINGS-NAVIGATION` 2.0).** Two things carry it.
   - **One size profile for every window** (`UiProfiles`, `UiProfileManager`, `UiMetrics`, `ThemedForm.OnUiProfileApplied`). The General
     page group "Size and input" (`general/display`) chooses the input profile (`UiInputProfile` = `pointer`|`touch`, never inferred from
     the device) and two scales (`UiInterfaceScale` 75/100/125/150/200, `UiTextScale` 100/125/150/200, per cent). Interface scale moves
     typography, glyphs, checkbox drawing, padding, gaps and targets together; text scale moves typography only. A value that is not
     an offered step reads as ordinary (100). The manager is **inert until `Initialize`** like `ThemeManager`, attaches every
     `ThemedForm` when its handle exists, applies a deliberate change to all open windows at once and re-measures the covered components
     at their final width (`UiProfileManager.Reflow`, UI-SIZING 1.1). The three values describe this PC and are **not** in the settings
     file (`SettingsFileInventory.Excluded`, "this PC\'s display"). A window that derives its own font (a language's script family)
     hands it to `ThemedForm.SetBaseFont` instead of assigning `Font`, so a language switch keeps the chosen size (`WindowScript.Apply`
     does it for its windows); a font assigned directly becomes the base of the next profile pass. The custom-painted tool windows follow
     the profile too: the history strip composes DPI x interface scale for geometry and x text scale for its cells and fonts, the
     traffic-light board and the symbols grid measure from the scaled window font and refit, the quick-notes editor face and the region
     overlay's size label compose both scales, the annotation toolbar's tools, swatches and dividers take the interface scale. The tray
     menu and the text context menu draw with Windows' menu font and are not scaled.
   - **The page rail** (`SettingsRail`, `SettingsRailLayout`). The native vertical tab strip is retired as a view: the `TabControl` stays
     as the page host with `HostOnly` (no display-area reservation for a strip, one accessible pane), and a native-scrolling owner-drawn
     list at the reading start lists the destinations - whole wrapped captions (never an ellipsis), the vocabulary glyph in one identity
     colour table (`ThemePalette.NavInk`, 3:1 held by `SettingsRailTests`), a bar and a bold caption for the selection, a click released
     on the pressed row, the keyboard, an accessible `List` of `ListItem`s. When the content pane would keep less than 480 logical px
     (interface-scaled) beside the rail, the same rail moves into a drawer opened by a "Pages" button in the header band
     (`SettingsRailLayout.Decide`). **Page changes are timed** from the rail\'s mouse release to a forced paint of the whole window
     (`PageNavigationTrace`, `CYRFLIP_NAV_TRACE=<file>`), a newer navigation supersedes an older one, and `tools/uitest/Measure-PageNavigation.ps1`
     repeats the measurement in fresh processes. **Opening a window and changing a setting are timed too** (ticket S0135, `OpenTrace`,
     `CYRFLIP_OPEN_TRACE=<file>`, off by default and free while off; `tools/uitest/Measure-WindowOpen.ps1` and `Measure-SettingsControls.ps1`; the
     first measured numbers - 1030 controls, a 2.3 s constructor, a 0.1-0.7 s change - are in `PLAN/done/CYRFLP-0135_ui-window-speed-optimization.md` section 3.4). **A page that is not the opening one keeps its layout back until it is first shown** (`SettingsPagePanel.DeferLayout`, released by the page's own `OnVisibleChanged`; every value and caption is still applied to every page - only the geometry of an unshown page waits), which is why a test that reads the geometry of every page builds the window through `TestForms.NewSettings` (it calls `SettingsForm.EnsureAllPagesLaidOut()`) and a timing test passes `allPagesLaidOut: false`; the hotkeys overview rebuilds its rows only when they differ from the ones on screen (`SameOverview`, and the live switches are checked too, since a refused switch relies on the rebuild to put its box back; S0135 section 3.9). **A changed value is `SettingsForm.RefreshState()`,
     not `Reload()`** (S0135 L1): `Reload()` is the full pass (`RefreshValues` + `ApplyLanguage`, the language, scale, font and theme work) and runs only when one of those changed, when the window is built and when
     it is shown again - a new caller that only changed a value must not bring the language pass back (`SettingsRefreshEquivalenceTests` holds the two to the same result). The measured budgets are **not** all met - see `PLAN/done/CYRFLP-0076_unified-ui-contracts/unified-ui.md` before
     claiming conformance.

## Current UI authority

S0076/S0077 current UI authority, component inventory and permanent profile decisions are
recorded in [PLAN/done/CYRFLP-0076_unified-ui-contracts/unified-ui.md](../../PLAN/done/CYRFLP-0076_unified-ui-contracts/unified-ui.md); the clause-by-clause
accepted-proposal read and explicit remaining exceptions are in
[PLAN/done/CYRFLP-0077_contract-sync-20261017/contract-sync-20261017.md](../../PLAN/done/CYRFLP-0077_contract-sync-20261017/contract-sync-20261017.md).
BUTTONS 2.0 supersedes older red-dismissal/tinted-answer descriptions below.
COLLAPSIBLE-GROUP 1.1 owns the native current-state triangle. `AboutResources` is
the resource inventory; About's local diagnostic export uses `SupportBundle` without
the separate mail handoff. No full UI conformance or release is claimed by these slices.

## Modules (`src/CyrFlip/`)

- **WelcomeDialog.cs / WelcomeVisuals.cs / WelcomeFacts.cs / FirstRun.cs** - modal five-page introduction; pure live-config facts and normalized-marker decision. The tour is not text on a form: every page opens with a solid plate and a large title - the vocabulary glyph everywhere but the welcome page, whose header carries the app's own icon (the `CyrFlip.app.ico` embedded resource, the same ico the exe carries) and whose body is the whole product in one index, one line per feature in POSITIONING.md's pillar order; on the later pages each feature is a row (`GlyphBadge` + bold title + the chord drawn as `KeyCaps` + a live `ToggleSwitch`); the conversions page ends in a `ConversionExample` (`ghbdtn ⇄ привет`), the indicators page shows the live layout badges and the progress is `StepDots`. These are small owner-drawn `IThemeAware` controls (`WelcomePainted`) that read `ThemePalette` roles only - no colour literal, no private picture: a meaning the vocabulary lacks (case flip, the context menu, the I-beam) gets a typographic mark ("Aa", "≡", "I") on the plate instead. `WelcomeFacts.ConversionRows()` (capped at `MaxConversionRows`) feeds the page. **The switches are live, not decoration** (owner, 2026-10-09): a person who has just installed CyrFlip meets its features and turns them on or off on the spot. A flip goes through `CyrFlipContext.ApplyWelcomeSwitch`, which calls the very handler the settings window calls (`OnQuickNotesChanged`, `SetCaseHotkeyEnabled`, the tray items for the caret marker, the cursor and the history, ...), so the hook, the tray and the config cannot tell the two apart; the tour then re-reads every switch from the configuration (`WelcomeFacts.IsOn`), so a flip another chord owner refused puts its knob back, and turning a chord on while the master switch is off turns the master on. Without a context behind it the dialog writes the value itself (`WelcomeFacts.Set`). The navigation buttons are `GlyphButton`s carrying `nav.back` / `nav.forward` / `nav.close` (the last step: `app.settings`), drawn at the theme text colour and mirrored for right-to-left. `SettingsPageKey` addresses settings pages independently of tab order. The context posts startup display, writes the marker on Shown, queues settings requests during the tour and defers the single handoff.

- **UiProfile.cs / SettingsRail.cs / PageNavigationTrace.cs / SettingsPagePanel.cs** (S0076, feature 17) - one concern each. `UiProfiles` is the pure half
  (tokens, the offered steps, `Metrics`, `FontPoints`; unit-tested on any machine); `UiProfileManager` is the inert-until-initialized,
  no-static-event list of attached windows and the `Reflow` walk of the covered components. `SettingsRail` is the page list control
  (rows measured with the bold caption at the final width, so a choice moves no other row; cached heights released by
  `InvalidateMetrics`; press and release on the same row; `InputStamp` is the start of the timing) with `SettingsRailLayout` the pure
  docked-or-drawer breakpoint. `PageNavigationTrace` times activation to a finished frame and records nothing unless switched on; its
  nearest-rank percentile is the contract's method. `ThemeTabControl.HostOnly` swallows `TCM_ADJUSTRECT` so the hosted pages fill the
  control. `SettingsForm` mirrors the host's selection into the rail (never the other way round for state), and skips the width clamp of
  a page whose width and control count are unchanged (`ClampPageWhenStale`) - that clamp had been 40 to 60 per cent of every warm page change.
  Two more levers (2026-10-30): `SettingsPagePanel`, the page's content panel, suspends its whole subtree's layout while a page whose
  every control already has its handle becomes visible (WinForms re-lays-out each nested auto-size panel on show, and a hidden page is
  never out of date - `PerformLayout` ignores visibility), and `ClampPage` sets every caption cap under one suspension, turning a clamp
  requested from inside the page's own layout into another pass. Each has a test seam (`SkipRelayoutOnShow`, `SuspendClampLayout`) that
  `SettingsPagePanelTests` compares against, control for control. Never pre-clamp a hidden page: `EditorsPreferredHeight` skips
  invisible children, so a hidden clamp gives a different result.

- **SettingsLoadingNotice.cs** - the "Loading settings.." picture shown while the settings window is built for
  the first time (`CyrFlipContext.ShowSettings`, only when the window does not exist yet). The build is
  controls, so it runs on the hooks' thread and nothing answers meanwhile: without a picture a tray double
  click read as "nothing happened" and was repeated, each repeat queuing behind the build (and the stray single
  clicks among them switched the layout). It is a **still** picture on purpose - the thread that would animate
  it is the busy one - painted with `Update()` *before* the build, a non-activating tool window, closed at the
  next `Application.Idle` (after the settings window has painted). **The first open used to take ~6 s**
  (measured on a real profile, 2026-10-15) and ~4 s of it was one defect: every page panel's `Resize`
  re-clamped the text widths of **all 13 pages** (`ClampPageTextWidths`), and showing the window resizes every
  panel. A panel's `Resize` now clamps only its own page, and only while it is the selected one
  (`ClampPageOnResize`); a page resized while hidden is flagged and clamped on selection. `ShowSettings` also
  no longer runs `Reload()` right after a constructor that has just run it. What is left (~2 s) is WinForms
  layout of ~850 controls on a cold process - JIT is **not** the lever (measured: pre-compiling every
  `SettingsForm` method changed nothing). `SettingsOpenCostTests` holds both halves.
- **ConfirmDialog.cs** - CyrFlip's own message and confirmation box (S0020 v0.2 item 6), in place of
  `MessageBox`: laid out by content, 13 languages (`DialogLayoutTests`), the system icon and sound,
  Ctrl+C copies the message, an ownerless box comes to the front with a taskbar button. The safe answer
  is the cancel role; `danger: true` puts Yes on the `danger` role and makes No the default.

- **HotkeyDialog.cs** - a modal `Form` (`FormBorderStyle.FixedDialog`) that captures a new hotkey from the user. `KeyPreview = true`; requires at least one modifier (Ctrl/Shift/Alt) plus a trigger key (A-Z, 0-9, F1-F24, or named keys). The constructor takes an optional window title (so the case-flip dialog reads "Set case hotkey"). Returns the hotkey string via `CapturedHotkey` on `DialogResult.OK`.
- **RowFocus.cs** - keeps the keyboard focus in a settings row table across the rebuild every row action does (S0045 K6), and `RowCheckBox`, the rows' caption-less switch that draws its focus rectangle round the box (K9). The keyboard routes S0045 added elsewhere: the history strip's cursor (arrows, Enter, Space pins, Delete, Ctrl+F), Esc/Enter/Delete in the history search, the region capture's arrows / Shift+arrows / Enter (whole monitor with no selection) / Ctrl+A (`RegionSelectionOverlay.OnKey`, `Nudge`), the settings page list's focus frame, and the launcher taskbar menu opening on the taskbar's edge when the pointer is not on it (`LauncherTaskbarWindow.MenuAnchor`). `KeyboardParityTests` holds the pure parts.
