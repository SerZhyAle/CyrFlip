---
paths:
  - "src/CyrFlip/InputLayout*.cs"
  - "src/CyrFlip/LanguageHotkeys.cs"
  - "src/CyrFlip/LayoutCatalog.cs"
  - "src/CyrFlip/LayoutPickerDialog.cs"
  - "src/CyrFlip/PopularLayoutPlan.cs"
  - "src/CyrFlip/LayoutSwitcher.cs"
---

# The Windows Languages page (input layouts and language chords)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

4. **A self-contained "Языки Windows" tab (settings editor for Windows' input config).** Goal: the user
   never has to open the Windows "Language & region" pane. The tab in `SettingsForm` manages three Windows
   settings, all writing **system** state, not CyrFlip state:
   - **Keyboard layouts** (`InputLayouts.cs`) - install / remove / reorder / set-default any of the ~218
     layouts Windows ships (incl. the Ukrainian keyboard). **Nothing is downloaded** - layout DLLs are already
     present; installing a *display-language pack* stays Windows' job (deep-linked via `ms-settings:keyboard`).
   - **The whole-cycle switch chord** (`Keyboard Layout\Toggle`) - a dropdown: Alt+Shift / Ctrl+Shift / `` ` `` / off.
   - **Direct per-language hotkeys** (`LanguageHotkeys.cs`) - "Ctrl+1 → English", "Ctrl+2 → Russian" etc., which
     **Windows handles, not CyrFlip** (`KeyboardHook` never matches or swallows them; they survive the app
     being closed). Motivating case: a mouse-macro presses Ctrl+1 and can then count on Latin input.

   See [PLAN/done/LanguageHotkeys_Spec_Idea_v0.1.md](../../PLAN/done/LanguageHotkeys_Spec_Idea_v0.1.md). Both modules take a
   one-time pre-CyrFlip registry snapshot (`AppConfig.LanguageHotkeysBackup` / `InputLayoutsBackup`) with a
   "restore" button, and warn honestly that a change may need a Windows sign-out/in to fully settle.

## Modules (`src/CyrFlip/`)

- **InputLayouts.cs** - installs / removes / reorders **Windows keyboard layouts** for the "Языки Windows" tab
  (no downloads - the layout DLLs ship with Windows). Win11 keeps input layouts in **two** stores at once:
  legacy `HKCU\Keyboard Layout\Preload`+`Substitutes` (read by `GetKeyboardLayoutList`) and the modern,
  authoritative `HKCU\Control Panel\International\User Profile` (a `Languages` BCP-47 multi-string + a
  `<langid>:<klid>=1` value per keyboard). The modern store re-syncs the legacy one on sign-in, so both follow
  one canonical ordered KLID list on every successful change (`Persist` returns the failure and override):
  registry writes and restores first capture the complete affected trees and roll back on failure (S0063).
  On success, the legacy store is rebuilt, the modern one is
  **edited as a diff** (`ApplyLayouts` on a `ProfileModel`, unit-tested) that **never deletes an IME/TIP value**
  (`0804:{CLSID}{PROFILE}` - Pinyin, the Japanese/Korean IMEs; ticket S0007 WL-1), drops a language only when
  this edit removed its last layout and it holds no TIP, keeps a keyboard under the language it already lives
  in, never reorders `Languages` (the user's app/web language list) and makes a layout the default through
  `InputMethodOverride`. **Add, remove and "Default" go first through the documented `input.dll` API**
  (`InputLayoutApi.cs`, S0007 WL-1 phase B: `EnumEnabledLayoutOrTip` / `InstallLayoutOrTip` /
  `SetDefaultLayoutOrTip`, language + keyboard/TIP pairs, `LAYOUTORTIPPROFILE` checked live), each call
  **verified by enumerating again**; a missing API, a refusal or a "yes" that changed nothing returns null and
  the edit takes the registry path above. ↑↓ has no API call and stays on that path; a Pinyin-style stand-in row
  is still refused (it cannot say which TIP it is). `tools/uitest/Test-InputLayoutApi.ps1 -Apply` is the live
  check. The live session is driven by the documented
  `LoadKeyboardLayout`/`UnloadKeyboardLayout`; a removal unloads only the handles `LayoutIdentity` decodes to
  that KLID (`HklsToUnload`). Preload can't list two layouts of one language, so the
  2nd+ use a `d<nnn><langid>` device handle via `Substitutes` (`BuildPreload`, unit-tested). A KLID's low 4 hex
  = its language id. `BackupAll`/`RestoreAll` snapshot both stores - format 2 keeps every profile value with
  its registry kind; format 1 (before S0007) is still restorable - and the restore deletes only the language
  subkeys, never the profile root, then reloads the live layouts. Every settings handler that writes Windows
  state calls `SettingsForm.EnsureSystemBackups` first (`SettingsBackupInventoryTests` scans for it), and
  writes nothing when that snapshot cannot be taken; a stored backup that did not read is never retaken
  over. The S0007 audit additions: an edit refuses when Preload cannot be read **whole**
  (`TryReadEffectiveKlids`) and the modern store loses only the one KLID a removal names, never a layout
  the list merely missed (WL-10); ↑↓ move a layout only within its own language (`GroupOf`/`CanMove` -
  the `Languages` order rebuilds Preload at sign-in, WL-8); a row that stands for an IME (`IsManagedByWindows`)
  is not removed (WL-9); a backup is all-or-nothing and restore refuses one that could not put a layout
  back (`TryParseSnapshot`, WL-11); duplicate direct-switch slots of one HKL are one assignment
  (`LanguageHotkeys.SlotsFor`, WL-12).
  `LayoutPickerDialog.cs` is the type-to-filter "add layout" picker (an auto-sizing `TableLayoutPanel`).
- **LanguageHotkeys.cs** - editor for **Windows'** own "switch straight to this input language" chords, stored
  in `HKCU\Control Panel\Input Method\Hot Keys\<id>` (ids `0x100`-`0x11F` = `IME_HOTKEY_DSWITCH_FIRST/LAST`),
  each a subkey with three 4-byte little-endian `REG_BINARY` values: `Key Modifiers` (a `MOD_*` mask),
  `Virtual Key`, `Target IME` (the destination HKL). The switch-chord setter verifies all writes, returns failure and refreshes the dropdown from Windows;
  one-time backups are complete or absent. Written values are re-read by the OS via
  `SystemParametersInfo(SPI_SETLANGTOGGLE)`. **CyrFlip does not process these chords** - the hook doesn't
  look at them; that's the whole point (they survive the app being closed). Two things worth knowing:
  the format stores an *arbitrary* modifier mask + VK, so **plain `Ctrl+1` is representable even though the
  built-in Windows dialog only offers `Ctrl+Shift+digit` / `Left Alt+Shift+digit`** - that's the feature's
  added value; and ids outside the direct-switch range are Windows' built-in IME chords (`0x10`, `0x70`,
  `0x200`..) which are never touched. Windows ships orphaned records (e.g. `0x104` → a Japanese HKL that
  isn't installed), so the UI surfaces those for removal instead of clobbering them. `AppConfig.LanguageHotkeysBackup`
  holds a one-time JSON snapshot of the pre-CyrFlip state so "restore" always means the state the user arrived with
  - since S0007 (format 2) including the three `Keyboard Layout\Toggle` values. A stored `Target IME` is
  **sign**-extended into an `IntPtr` (`HklFromStored`) and HKLs are compared on their low 32 bits (`HklEquals`):
  `new IntPtr(uint)` zero-extends, so an alternate layout (`F0010409`) or an IME never matched its own live
  handle on x64 (WL-4). Changing the language switch chord leaves `Layout Hotkey` alone unless the two would
  collide (`LayoutHotkeyAfter`, WL-6).
