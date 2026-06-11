![CyrFlip — fix text typed on the wrong keyboard layout](assets/banner.png)

# CyrFlip

CyrFlip is a tiny background Windows tool that fixes text typed on the wrong keyboard
layout. Select the mangled text, press the hotkey, and CyrFlip transliterates it between
QWERTY and ЙЦУКЕН (EN ↔ RU, with UK planned) in place. It lives in the system tray, and
its icon shows the active layout (EN/RU/UK).

## Status

Early development. See [PLAN/KeyboardTransliterator_Specification_v1.0.md](PLAN/KeyboardTransliterator_Specification_v1.0.md)
for the full specification and [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

## How it works

1. A global low-level keyboard hook listens for the hotkey (default **Ctrl+Shift+T**).
2. On trigger, the current selection is copied, transliterated, and pasted back.
3. The tray icon tracks the active layout and updates live.

## Using CyrFlip

- **Run it** — launch `CyrFlip.exe`. There's no window; it sits in the notification area (system tray). The icon shows the active keyboard layout (**EN/RU/UK**).
- **Flip text** — select text typed in the wrong layout, press **Ctrl+Shift+T**, and it's replaced in place. Works in any app (Notepad, Word, browsers, …).
- **Tray menu** (right-click the icon):
  - **Flip EN ⇄ RU: Ctrl+Shift+T** — shows the active flip hotkey.
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
{ "hotkey": "Ctrl+Shift+T", "layouts": ["EN", "RU"], "cursorSize": 24 }
```

## License

MIT — see [LICENSE](LICENSE).
