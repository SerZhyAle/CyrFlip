# tools/uitest - manual/interop checks

xUnit covers the pure logic. The tray mouse, the live settings window and the layout switch are
interop-bound, so they were tested by hand-writing a throwaway PowerShell script every time - and
re-hitting the same traps every time. This folder is that script, kept.

| File | What it is |
| --- | --- |
| `CyrFlip.UiTest.psm1` | The module: DPI, tray icon lookup (MSAA), synthesized clicks, window/layout queries, window screenshots |
| `TargetWindow.ps1` | A plain WinForms text box standing in for "the app the user was typing in" |
| `Test-TrayMouse.ps1` | End-to-end: single tray click switches the last active window's layout; double click opens Settings |
| `Test-KeepAwake.ps1` | End-to-end: the saved keep-awake state becomes a real Windows power request (`powercfg /requests`) and stops being one when saved off |
| `Test-SupportBundle.ps1` | The "Send logs to the author" archive: contents, truncation markers, retention. `-NoUi` builds the bundle itself (reflection into the built exe, real log folder and registry) so the disk half runs unattended; without it, you press the button and it checks what appeared. Either way the compose window is yours to look at |
| `Save-SettingsShots.ps1` | PNG of every settings tab - for layout/localization eyeballing |

Nothing here is wired into `dotnet test`, `build.ps1` or CI: these drive the real desktop (they
move the mouse and steal focus), so they are run deliberately, on a machine somebody is watching.

## Run

```powershell
dotnet build CyrFlip.sln -c Release
.\tools\uitest\Test-TrayMouse.ps1 -StartApp -Fresh      # exit code 0 = pass
.\tools\uitest\Test-KeepAwake.ps1                       # three UAC prompts (powercfg needs admin)
.\tools\uitest\Save-SettingsShots.ps1 -StartApp         # -> artifacts\uitest\settings-tab*.png
.\tools\uitest\Test-SupportBundle.ps1 -NoUi             # unattended: disk half only
.\tools\uitest\Test-SupportBundle.ps1                   # then press the About-tab button yourself
```

Ad hoc, in a session:

```powershell
Import-Module .\tools\uitest\CyrFlip.UiTest.psm1 -Force
Get-TrayIcons                                  # every tray icon: tooltip, rect, click point
Get-TrayIcon                                   # just ours (-NameLike '*CyrFlip*')
Invoke-TrayClick -Count 2                      # open Settings the way a user does
Wait-AppWindow                                 # the settings window, by process + size
$t = Start-TargetWindow; Get-WindowLayout -Handle $t.Handle
Save-WindowShot -Handle (Wait-AppWindow).Handle -Path shot.png
```

## Preconditions

- At least two installed keyboard layouts (else there is no switch to observe).
- The tray icon **visible on the taskbar**, not behind the chevron - icons in the hidden-icons
  flyout live in another window and `Get-TrayIcon` will not find them.
- Don't touch the mouse while a script runs; it drives the real cursor.

## Traps these scripts already handle

- **DPI.** PowerShell is DPI-unaware by default: UI Automation reports physical pixels, and
  `SetCursorPos` takes virtualized ones, so on a scaled display the click lands somewhere else.
  The module sets PerMonitorV2 on import (`Enable-UiTestDpi`) before reading any coordinate.
- **The tray click steals focus.** It focuses the taskbar, which is exactly why CyrFlip acts on
  `CursorIndicator.LastActiveWindow`. A test therefore needs a separate target window that is
  never re-focused after the click - and must read the layout of *that window's thread*
  (`GetKeyboardLayout(GetWindowThreadProcessId(...))`), not of the foreground window.
- **The single click is deferred** by `SystemInformation.DoubleClickTime` (the shell delivers the
  first click of a double click as an ordinary one), so a check must wait ~2 s, not 200 ms.
- **The layout is read while the target window stays in the background.** Windows re-applies the
  system-wide current layout to a window when it activates it, so re-focusing the target between
  clicks resets it and every click then reads as the same "RU → EN" (measured 2026-07-26: a switch
  applied while the window is in the *foreground* survives; one applied to a background window does
  not - and the tray click is always the background case, since clicking the notification area
  focuses the taskbar). `Set-WindowForeground` exists for setting *up* a check, not for use between
  the clicks of one.
- **The tray icon is found through MSAA, not UI Automation.** The notification area is a legacy
  `ToolbarWindow32`; its buttons are bridged into the UIA tree only under Windows PowerShell 5.1,
  and under pwsh 7 the pane comes back childless - so a UIA-based lookup "works" until it silently
  doesn't. `Get-TrayIcons` goes through `oleacc` (`AccessibleObjectFromWindow` → child ids), which
  answers in both shells and yields the tooltip as `accName`, matched with `-like '*CyrFlip*'`.
  It also lists every *other* app's icon - a hard-coded coordinate copied from a previous run
  points at whichever icon has since taken that slot.
- **The settings window is found by process + size, not by title.** The caption is localized
  ("Настройки CyrFlip" / "CyrFlip Settings"), and any editor with this repo open has "CyrFlip" in
  its title; the size test also rejects the launcher's 1x1 taskbar window.
- **Tab walking is driven by the registry.** The owner-drawn `TabControl` exposes no UIA
  `TabItem`s, so `Save-SettingsShots.ps1` sends Ctrl+Tab and reads the position back from
  `HKCU\Software\CyrFlip\SettingsTab` (which the app writes on every tab change) - it rewinds to
  tab 0 first and stops when the index wraps, so it needs no hard-coded tab count.
- **Screenshots** use `PrintWindow` with `PW_RENDERFULLCONTENT` (2); plain BitBlt returns blank
  for DWM-composed WinForms windows, and a background window captures fine this way.
- **A real `mouse_event` click, not a UIA `Invoke`.** The tray path is only meaningful when it is
  driven the way a user drives it.
- **Only `powercfg /requests` can confirm keep-awake, and it needs administrator rights.**
  `SetThreadExecutionState` returns the *previous* state, never a confirmation, so from inside the
  process an ignored request is indistinguishable from a granted one. `Test-KeepAwake.ps1` elevates
  that one call per reading and matches on `net48\CyrFlip.exe` - powercfg prints a device path
  (`\Device\HarddiskVolumeN\..`), not a drive letter, so a literal full-path comparison never hits.
  Expect other processes in the same lists (PowerToys Awake, "Legacy Kernel Caller"); they are not
  ours and must not be asserted on.
