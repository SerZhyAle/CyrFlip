![CyrFlip — fix text typed on the wrong keyboard layout](assets/banner.png)

# CyrFlip

CyrFlip is a tiny Windows tray tool with two jobs:

1. **A live layout indicator where you type (the main feature).** A small **EN / RU / UK** marker rides both your mouse text cursor (the I-beam) **and the blinking text caret**, so you always know which layout you're about to type in — updated live as you switch. (The mouse cursor is often an arrow while typing, which is why the caret marker matters.)
2. **One-key transliteration.** Text typed in the wrong layout can be flipped in place between QWERTY and ЙЦУКЕН (EN ↔ RU, UK planned) with a hotkey.

![CyrFlip's layout-aware text cursor showing EN, RU and UK](assets/cursor-preview.png)

## Status

Early development. See [PLAN/KeyboardTransliterator_Specification_v1.0.md](PLAN/KeyboardTransliterator_Specification_v1.0.md)
for the full specification and [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

## How it works

1. CyrFlip watches the active keyboard layout and replaces the system **text cursor** with a caret that shows the layout marker (EN/RU/UK). The same marker also appears on the tray icon.
2. A global low-level keyboard hook listens for the flip hotkey (default **Ctrl+Shift+F12**); on trigger, the selection is copied, transliterated, and pasted back.

> The cursor change is system-wide (`SetSystemCursor`) and is restored when CyrFlip exits. Only the text I-beam is changed, not the normal arrow pointer.

## Using CyrFlip

- **Run it** — launch `CyrFlip.exe`. There's no window; it sits in the notification area (system tray). The icon shows the active keyboard layout (**EN/RU/UK**).
- **Flip text** — select text typed in the wrong layout, press **Ctrl+Shift+F12**, and it's replaced in place. Works in any app (Notepad, Word, browsers, …).
- **Tray menu** (right-click the icon):
  - **Flip EN ⇄ RU: Ctrl+Shift+F12** — shows the active flip hotkey.
  - **Start with Windows** — toggle launching CyrFlip at sign-in (per-user; no admin needed).
  - **Exit** — quit.

CyrFlip is a normal desktop app, not a Windows service: a global keyboard hook and the layout indicator must run in your interactive session, so "autostart" is a per-user startup entry rather than a service.

## Requirements

- Windows 10 / 11 (x64)
- .NET Framework 4.8 (preinstalled on Windows 10/11 — no extra runtime to install)

## Build & run

```powershell
dotnet build CyrFlip.sln -c Release
.\src\CyrFlip\bin\Release\net48\CyrFlip.exe
```

Or run the tests:

```powershell
dotnet test CyrFlip.sln
```

## Configuration

Optional `config.json` (next to the exe, or in `%APPDATA%\CyrFlip\`):

```json
{ "hotkey": "Ctrl+Shift+F12", "cursorSize": 24 }
```

## Known issues

- **The caret marker doesn't appear in some apps.** CyrFlip locates the text caret via the Windows system caret or UI Automation. A few apps expose neither — chiefly **console/terminal windows** (Command Prompt, PowerShell, Windows Terminal) and the occasional app with custom-drawn text and weak UI Automation support. There the caret marker is hidden; the tray icon and mouse-cursor marker still show the layout.
- **The mouse text cursor (I-beam) can stay changed after a force-kill.** CyrFlip replaces the system I-beam globally and restores it on exit. If the process is killed hard (e.g. *End task* in Task Manager), Windows can't restore it until you run CyrFlip again or sign out and back in.
- **Transliteration is EN ↔ RU only.** The layout indicator handles EN/RU/UK, but the one-key flip currently converts between QWERTY and ЙЦУКЕН; Ukrainian-specific letters aren't transliterated yet.
- **The flip preserves only clipboard text.** Running a flip restores text clipboard contents, but not images or files.

## License

MIT — see [LICENSE](LICENSE).
