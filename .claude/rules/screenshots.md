---
paths:
  - "src/CyrFlip/Screen*.cs"
  - "src/CyrFlip/RegionSelectionOverlay.cs"
  - "src/CyrFlip/ClipboardImage.cs"
  - "src/CyrFlip/Annotation*.cs"
  - "src/CyrFlip/PngTime.cs"
  - "src/CyrFlip/SettingsForm.Graphics.cs"
  - "src/CyrFlip/CyrFlipContext.AppCommandsScreenshots.cs"
---

# Screen captures and drawing on a capture

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

11. **The "Снимки экрана" settings tab (it was "Графика" until S0062; page key `SettingsPageKey.Graphics`) and its capture tools: the screen region capture (ticket S0026), the full capture and the draw-on-capture module (ticket S0071).** Deliberately
   **no module switch for the captures and no tray submenu** (owner, 2026-09-26: an extra switch and one more menu level make
   the command slower to reach) - the tab groups the settings, the tray has top-level
   **"Снимок области экрана"** and **"Снимок всего экрана"** items that are always there, and each chord has only its own
   switch (`EnableScreenshotHotkey`, `EnableScreenshotFullHotkey`, both on). The area chord (`ScreenshotHotkey`, default
   **Ctrl+F12** since S0071 - the shipped **Ctrl+PrintScreen** "never fired" for the owner, so the capture defaults moved to
   the F-key family the hook provably intercepts; `Hotkey` still parses `PrintScreen`/`PrtSc` for a manual bind, and
   `HotkeyDialog` captures PrintScreen on the key-**up**, since Windows posts no key-down for it), the tray item or the
   settings button **freeze every screen first** (`ScreenCapture.Grab`, one
   `BitBlt` with `SRCCOPY | CAPTUREBLT` - `CopyFromScreen` drops layered windows, i.e. the tooltip the chord
   exists to catch), then `RegionSelectionOverlay` shows **one window per monitor** painting its slice of
   that one frame (a single window across mixed DPI is rescaled by Windows), with one selection rectangle in
   virtual-screen physical pixels. The **full capture** (`StartFullCapture`, `ScreenshotFullHotkey`, default **Ctrl+F11**,
   tray item "Снимок всего экрана") skips the selection: one freeze of the virtual screen straight to `DeliverScreenshot`.
   **Re-entry is guarded, not queued** (S0071 5): no capture of any kind starts while the selection overlay or the
   annotation editor is up (`CaptureBusy`) - a `BitBlt` with `CAPTUREBLT` over a live overlay would put the overlay's own
   dimming into the picture. The cropped region goes to the clipboard as **`PNG` + `CF_DIB`** of the
   same pixels in one open (`Win32Clipboard.TrySetImage`, `ClipboardImage`), under the `_busy` guard for the
   write only; the user's previous clipboard is replaced, not restored, and on cancel it is never opened.
   **"Also save to a folder"** (`ScreenshotSaveEnabled`, **on by default since S0071** - existing profiles keep their
   stored value; the flip obliged the privacy wording on the settings page) writes the same PNG bytes per
   `CAPTURE-OUTPUT` (kind `screenshot`): `screenshot_yyMMdd_HHmmss[ (n)].png` with invariant digits and the
   freeze time, the ordinal checked against the folder itself, temp-then-no-replace-rename, default
   `FOLDERID_Screenshots` with no product subfolder, fallback Screenshots -> Downloads **always told** by a
   balloon (`ScreenshotSaver`, file system behind `IScreenshotFileSystem`). The folder the captures
   land in opens on **its own chord** (ticket S0057): `ScreenshotFolderHotkey`, default
   **Ctrl+F10** since S0071 (was Ctrl+Shift+PrintScreen - same dead-key problem), **bound only while saving is on**
   and its own switch (`EnableScreenshotFolderHotkey`) is - with saving off the keystroke passes through
   (`ScreenshotFolderChordLive`, the text menu's D2 discipline; going live is checked like assigning it,
   KC-6). `ScreenshotFolderTarget` picks the target - the folder the settings show
   (`ScreenshotSaver.DisplayFolder`), else the remembered folder of this session's last fallback save
   (D4, `_lastScreenshotFolder`, in memory only), else one of two balloons; a remote path
   (`LaunchTargets.IsRemotePath`) is never probed but handed to the shell. `ScreenshotFolderOpener` runs
   the `Directory.Exists` probe on a **pool worker** (S0030 HT-1: the UI thread is the keyboard hook's
   thread) and opens through the shell; the settings button "Открыть папку" goes through the same opener,
   so the chord and the button can never disagree about the target. The chord is ignored while the region
   overlay is up. The caret badge and the
   translation popup are `WDA_EXCLUDEFROMCAPTURE` (S0026 4.2) - they also vanish from other tools' captures
   and screen shares.
   **The draw-on-capture module (S0071, on by default - `EnableScreenshotAnnotate`):** two more chords
   (`ScreenshotAnnotateAreaHotkey` **Ctrl+Alt+F12**, `ScreenshotFullAnnotateHotkey` **Ctrl+Alt+F11**, each with its own
   switch, both bound only while the module is on - the first shipped chords on the Ctrl+Alt plane, safe because
   `ChordMatcher` tells AltGr from a real Ctrl) run the capture and hand the frozen frame to
   `AnnotationEditor` instead of delivering: the region at full brightness, everything outside dimmed, and a
   translucent (`Opacity` 0.90 - S0093's computed floor: the lowest at which its body text and button captions hold 4.5:1 over any desktop) draggable `ScreenshotToolbar` near the pointer - six shape tools (ellipse,
   filled ellipse, rectangle, filled rectangle, line, arrow), an object eraser, eight fixed swatches
   (`AnnotationPalette` - content, not theme, the LAYOUT-PALETTE precedent), Clear, Cancel, OK, and Ctrl+Z.
   **Shapes are objects** (`AnnotationShape`), never pixels: OK composes them onto a copy of the frame in commit
   order (`AnnotationComposer`), so the toolbar and the dimming cannot enter the picture by construction and no
   `WDA_EXCLUDEFROMCAPTURE` is needed; the composed copy goes to `DeliverScreenshot` with the **original freeze
   instant** in the name. Cancel (button, Esc, focus lost, `WM_DISPLAYCHANGE`) delivers nothing. One stroke width:
   3 physical px x the DPI of the monitor under the pointer at editor open. **The toolbar is a topmost window of the
   same band as the per-monitor editor windows, and activating one of those raises it over the toolbar** - each
   covers its whole monitor, so the toolbar was shown and unreachable. `AnnotationEditor.RaiseToolbar` (no
   activation) runs after `Show` and, a message later, after every activation of a monitor window; a pick on the
   toolbar hands the foreground back to the editor window (`FocusCanvas`), since Esc / Enter / Ctrl+Z are the
   monitor windows' keys. The tool buttons have no caption: each draws its shape (`AnnotationToolGlyph`, the
   theme's text ink - a white or black swatch as ink would vanish on the button) and a build cannot tell that
   from a blank square, so `AnnotationToolGlyphTests` reads the pixels. `Test-ScreenRegion.ps1 -EditorWindows`
   asks Windows what is at the toolbar's centre and checks every button against its own face on the real screen.
   The one-time chord re-key
   (`ScreenshotKeyMigrationDone`, machine history, never exported) moves profiles still carrying the old shipped
   Ctrl+PrintScreen / Ctrl+Shift+PrintScreen to the new defaults exactly once; everything else - customised chords,
   every switch, the case flip, the seeded EN⇄RU row - is never touched. Spec:
   [PLAN/done/S0071_screenshot-full-and-annotate.md](../../PLAN/done/S0071_screenshot-full-and-annotate.md);
   the region capture's own ticket: [PLAN/done/S0026_screen-region-capture.md](../../PLAN/done/S0026_screen-region-capture.md).

## Modules (`src/CyrFlip/`)

- **ScreenCapture.cs / RegionSelectionOverlay.cs / ClipboardImage.cs / ScreenshotSaver.cs** - the Graphics
  module's region capture (feature 11), one concern each: the grab, the crop and the pure rectangle math
  (`Normalize`, `ClampToVirtualScreen`, `IsClick`, `MonitorSlice` - `ScreenCaptureTests`); the per-monitor
  overlay windows, the only part that knows WinForms (declared out of theme - it paints the user's own
  screen; its veil colours live in `ThemePalette` as `Capture*`); the PNG and bottom-up 32bpp DIB
  encodings (`ClipboardImageTests`); and the `CAPTURE-OUTPUT` saver (`ScreenshotSaverTests` - conformance
  step 1, the name in ar-SA/hi-IN/bn-BD). The frame is the one large allocation (~66 MB for two 4K
  monitors) and is disposed the moment the region is cropped out of it.
