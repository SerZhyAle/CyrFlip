![CyrFlip - fix text typed on the wrong keyboard layout](assets/banner.png)

# CyrFlip

CyrFlip is a tiny Windows tray tool with two modest jobs:

1. **A live layout indicator where you type (the main feature).** A small **EN / RU / UK** marker rides both your mouse text cursor (the I-beam) **and the blinking text caret**, so you always know which layout you're about to type in - updated live as you switch. The idea being that you glance at it *before* committing a whole paragraph to the wrong alphabet. (The mouse cursor is often an arrow while typing, which is why the caret marker matters.)
2. **One-key transliteration.** For when you ignored feature #1 anyway: text typed in the wrong layout can be flipped in place between QWERTY and ЙЦУКЕН (EN ↔ RU, UK planned) with a hotkey - no retyping, no shame.

![CyrFlip's layout-aware text cursor showing EN, RU and UK](assets/cursor-preview.png)

## Status

Early development - which here means "it works, but we reserve the right to be humble about it." See
[PLAN/KeyboardTransliterator_Specification_v1.0.md](PLAN/KeyboardTransliterator_Specification_v1.0.md)
for the full specification and [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

## How it works

1. CyrFlip watches the active keyboard layout and replaces the system **text cursor** with a caret that shows the layout marker (EN/RU/UK). The same marker also appears on the tray icon.
2. A global low-level keyboard hook listens for the flip hotkey (default **Ctrl+Shift+F12**); on trigger, the selection is copied, transliterated, and pasted back.

> The cursor change is system-wide (`SetSystemCursor`) and is restored when CyrFlip exits. Only the text I-beam is changed, not the normal arrow pointer.

## Using CyrFlip

- **Run it** - launch `CyrFlip.exe`. There's no window to admire; it sits quietly in the notification area (system tray). The icon shows the active keyboard layout (**EN/RU/UK**).
- **Flip text** - select the `ghbdtn` you meant as «привет», press **Ctrl+Shift+F12**, and it's replaced in place. Works in any app (Notepad, Word, browsers, ..).
- **Tray menu** (right-click the icon):
  - **Flip EN ⇄ RU: \<hotkey\>** - shows the active flip hotkey (disabled header).
  - **Set hotkey…** - opens a dialog where you press the new combination; saved immediately, no restart needed.
  - **Cursor: layout indicator** - toggle the system I-beam cursor replacement (off by default).
  - **Caret: overlay label** - toggle the EN/RU/UK marker next to the blinking caret (on by default).
  - **Caret: dot style** - when the overlay is on, show a small colour dot instead of the EN/RU/UK letters.
  - **Start with Windows** - toggle launching CyrFlip at sign-in (per-user; no admin needed).
  - **Exit** - quit.

CyrFlip is a normal desktop app, not a Windows service: a global keyboard hook and the layout indicator must run in your interactive session, so "autostart" is a per-user startup entry rather than a service.

## VS Code extension

Inside VS Code the external marker can't track the caret precisely (Monaco draws its own caret).
The companion extension reads the layout CyrFlip publishes and renders the marker **exactly at the
editor caret**.

**How they link up:** CyrFlip writes the current layout code to `%LOCALAPPDATA%\CyrFlip\layout.txt`
(see [LayoutPublisher.cs](src/CyrFlip/LayoutPublisher.cs)); the extension watches that file and draws
the `EN/RU/UK` marker at the caret, plus a status-bar indicator. **CyrFlip must be running** for the
marker to appear.

- **Install** - from the VS Code Marketplace: search **"CyrFlip"** (publisher *SerZhyAle*), or
  install the prebuilt `vscode-extension/cyrflip-vscode-<version>.vsix` via
  *Extensions ▸ .. ▸ Install from VSIX..*.
- **Source & docs** - [vscode-extension/](vscode-extension/) (build, package, settings, publishing).

The marker only renders inside code editors; webviews (terminal, search, chat) can't host editor
decorations, so there the mouse-cursor marker and tray icon apply instead.

## Requirements

- Windows 10 / 11 (x64)
- .NET Framework 4.8 (preinstalled on Windows 10/11 - no extra runtime to install)

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

All settings are stored in the Windows Registry (`HKCU\Software\CyrFlip`) and are changed through the tray menu — no config file to edit.

| Setting | Default | What it does |
| --- | --- | --- |
| Hotkey | `Ctrl+Shift+F12` | Flip hotkey; change via **Set hotkey…** in the tray menu |
| Cursor indicator | off | Replaces the system I-beam with a layout-branded cursor |
| Caret overlay | on | Shows the layout marker next to the blinking text caret |
| Caret dot style | off | Coloured dot instead of EN/RU/UK letters in the overlay |

A legacy `config.json` (next to the exe or in `%APPDATA%\CyrFlip\`) is migrated to the registry automatically on first run.

## Known issues

- **The caret marker doesn't appear in some apps.** CyrFlip locates the text caret via the Windows system caret or UI Automation. A few apps keep their caret a closely guarded secret and expose neither - chiefly **console/terminal windows** (Command Prompt, PowerShell, Windows Terminal) and the occasional app with custom-drawn text and weak UI Automation support. There the caret marker bows out gracefully; the tray icon and mouse-cursor marker still show the layout. And in some editors (e.g. VS Code and other Monaco-based ones), UI Automation reports the caret position imprecisely, so the marker may appear toward the edge of the input rather than exactly at the caret - for VS Code, use the [companion extension](vscode-extension/), which places it exactly at the editor caret.
- **The mouse text cursor (I-beam) can stay changed after a force-kill.** CyrFlip replaces the system I-beam globally and politely restores it on exit. If the process is killed hard (e.g. *End task* in Task Manager), it never gets to say goodbye, so Windows keeps the fancy cursor until you run CyrFlip again or sign out and back in.
- **Transliteration is EN ↔ RU only.** The layout indicator handles EN/RU/UK, but the one-key flip currently converts between QWERTY and ЙЦУКЕН; Ukrainian-specific letters aren't transliterated yet.

## Antivirus false positives

Some antivirus engines - notably **Avast / AVG**, which report it as `IDP.Generic` ("Behavior Shield") - may flag `CyrFlip.exe` as suspicious. This is a **heuristic false positive**, not malware, and it's **normal for every keyboard-layout indicator** (Punto Switcher and similar tools trip the same heuristics). In fairness to the antivirus, CyrFlip does look guilty on paper: by design, a layout indicator + transliterator does exactly what a behavioural keylogger heuristic watches for - it installs a global keyboard hook (`WH_KEYBOARD_LL`), synthesizes keystrokes (`SendInput`), reads/writes the clipboard, and swaps the system I-beam cursor. Same toolkit, very different intentions.

CyrFlip is open source - you can read exactly what it does in [src/CyrFlip/](src/CyrFlip/) - and **static scanners agree it's clean: a build scores 0/71 on [VirusTotal](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection)** (Avast and AVG included). The local flag is purely *behavioural* (Avast's runtime Behavior Shield) and reputational (unsigned exe, run from a temp folder) - things VirusTotal's static engines don't replicate.

What actually reduces the flags (in order of impact):

- **Don't run it from a temporary folder.** Launching the exe straight out of an archive or from `%TEMP%` (e.g. a `Temp\Rar$..` extraction path) is itself a strong reputation red flag. **Unpack the ZIP to a permanent location** such as `%LOCALAPPDATA%\Programs\CyrFlip\` and run it from there - this alone clears many behaviour-based detections.
- **Signed releases.** Tagged releases are Authenticode-signed when a signing certificate is configured in the release pipeline. A valid code signature is the single biggest factor in lowering heuristic flags and building reputation.
- **Report the false positive** so the vendor whitelists the file (usually corrected within a few days): [Avast false-positive form](https://www.avast.com/false-positive-file-form.php) · [AVG false-positive form](https://www.avg.com/en-ww/report-false-positive). As the app's author you can also enrol in the [Avast/AVG Whitelisting Program](https://businesshelp.avast.com/Content/Products/General_Help/Whitelisting/WhitelistingProgram.htm) so future builds stay cleared.
- **Verify the binary yourself.** Check its SHA256 against the `.sha256` published alongside each release, and scan your own download on [VirusTotal](https://www.virustotal.com/) - or see the [report for a recent build](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection) (0/71).

**По-русски:** срабатывание `IDP.Generic` у Avast/AVG - это **ложная эвристика**, а не вирус, и это **норма для любого индикатора раскладки** (Punto Switcher ловится так же): приложение по своей природе использует глобальный хук клавиатуры, инъекцию нажатий и буфер обмена. Статические сканеры это подтверждают - файл показывает **0/71 на [VirusTotal](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection)** (Avast и AVG в том числе); локальный флаг - чисто **поведенческий** (Behavior Shield) и репутационный (неподписан, запуск из временной папки). Что помогает: **не запускать из временной папки** (распакуйте архив в постоянный каталог, например `%LOCALAPPDATA%\Programs\CyrFlip\`), пользоваться подписанными релизами и отправить файл в белый список через формы Avast/AVG выше.

## Related project

- [Universal Agent Kit](https://serzhyale.github.io/universal-agent-kit/) - a companion toolkit by the same author.

## Author

**SerZhyAle** - [sza.od.ua](https://sza.od.ua) · [sza@ukr.net](mailto:sza@ukr.net)

## License

MIT - see [LICENSE](LICENSE).
