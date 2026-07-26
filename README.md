![CyrFlip - fix text typed on the wrong keyboard layout](assets/banner.png)

# CyrFlip

[![GitHub release](https://img.shields.io/github/v/release/SerZhyAle/CyrFlip)](https://github.com/SerZhyAle/CyrFlip/releases/latest)
[![winget](https://img.shields.io/winget/v/SerZhyAle.CyrFlip)](https://winget.run/pkg/SerZhyAle/CyrFlip)
[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-CyrFlip-0078D4)](https://apps.microsoft.com/detail/9NB4W41NGQJ4)
[![VS Code Marketplace](https://img.shields.io/visual-studio-marketplace/v/SerZhyAle.cyrflip-vscode)](https://marketplace.visualstudio.com/items?itemName=SerZhyAle.cyrflip-vscode)
[![License: MIT](https://img.shields.io/github/license/SerZhyAle/CyrFlip)](LICENSE)

CyrFlip is a tiny Windows tray tool with a few modest jobs:

1. **A live layout indicator where you type (the main feature).** The marker follows both the I-beam and blinking caret. The curated set covers EN, ZH, HI, ES, FR, AR, BN, PT, RU, UR, DE, IT and UK, while any Windows layout still gets its own live two-letter code.
2. **A table of layout conversions.** Keep the familiar EN ⇄ RU flip or add as many "from layout ⇄ to layout" rows as you like, each with its own global hotkey. Conversion follows **physical key positions** (so AZERTY and QWERTZ are handled correctly), works **in both directions** — if the pair's second layout is already active, the same chord converts back — and switches to the matching layout afterwards.
3. **Fix CapsLock.** A second hotkey (**Ctrl+Shift+F11**) inverts the case of the selection, optionally flipping the physical CapsLock key with it.
4. **A settings window that also replaces the Windows language pane.** Install, reorder and remove keyboard layouts, pick the whole-cycle switch chord, and assign per-language switch shortcuts that **Windows** handles (so they keep working when CyrFlip is closed). Nothing is downloaded — the layouts already ship with Windows.
5. **An opt-in quick-launch module (the absorbed [OneClickRunner](https://github.com/SerZhyAle/OneClickRunner)).** Your programs, scripts and yt-dlp downloads as scenarios, launched from the tray, the settings table, an optional per-scenario global hotkey, or the taskbar **Jump List**. Off by default — until you enable it, CyrFlip behaves exactly as before.
6. **An opt-in translator that runs on your own computer.** Select text anywhere in Windows, press a chord, and the translation appears in a small window next to the mouse pointer, filling in as the model writes it. It runs on [Ollama](https://ollama.com), a free program you install once yourself — no account, no key, and no text sent to the developer or to a cloud service. Off by default.
7. **Extras that fit a tray tool:** an opt-in encrypted clipboard history with search, two keep-awake switches, and a UI available in **13 languages**.

![CyrFlip's layout-aware text cursor showing EN, RU and UK](assets/cursor-preview.png)

## Status

Early development - which here means "it works, but we reserve the right to be humble about it." See
[PLAN/done/KeyboardTransliterator_Specification_v1.0.md](PLAN/done/KeyboardTransliterator_Specification_v1.0.md)
for the full specification and [CLAUDE.md](CLAUDE.md) for the architecture and conventions.

## How it works

1. CyrFlip watches the active keyboard layout and shows its two-letter code in three places: next to the blinking **text caret**, on the system **I-beam** mouse cursor (optional), and on the **tray icon**. When CapsLock is on, all three get a thin coloured frame.
2. A global low-level keyboard hook listens for the hotkeys; on trigger, the selection is copied, transformed, and pasted back. Two fixed chords — the **case fix** (**Ctrl+Shift+F11**) and the **clipboard manager** (**Ctrl+Shift+F10**) — plus every row of your conversion table, which starts life with **EN ⇄ RU on Ctrl+Shift+F12**.
3. **Clipboard history** is opt-in. It keeps Unicode text in a compact topmost strip; choose an item with the mouse, pin or delete it, search the whole history, or toggle the strip with **Ctrl+Shift+F10**. The encrypted local history is protected with Windows DPAPI and can be paused or cleared.

> The cursor change is system-wide (`SetSystemCursor`) and is restored when CyrFlip exits. Only the text I-beam is changed, not the normal arrow pointer.

## Using CyrFlip

- **Run it** - launch `CyrFlip.exe`. It sits quietly in the notification area (system tray) and its icon shows the active keyboard layout.
- **Convert text** - select the `ghbdtn` you meant as «привет», press **Ctrl+Shift+F12**, and it's replaced in place. Works in any app (Notepad, Word, browsers, ..). That chord is simply the EN ⇄ RU row the conversion table starts with; add rows for any other pair of installed layouts, each with its own chord.
- **Fix CapsLock** - select the `hELLO` you meant as `Hello` and press **Ctrl+Shift+F11**.
- **Tray menu** (right-click the icon) keeps the frequent switches: show/hide history, clipboard history on/off, pause capture, the three indicator toggles, the two keep-awake switches, the **Quick launch** submenu and **Translate clipboard** (each shown only while its module is enabled), **Settings…** and **Exit**. Double-clicking the icon opens Settings.
- **Settings** (nine tabs, every change applied at once, no restart):
  - **General** - start with Windows, keep the computer awake, keep the screen on, and the interface language (13 to choose from).
  - **Indicators** - the I-beam cursor marker (off by default), the caret marker (on by default), the compact dot style, "change the layout after converting text", and "synchronize CapsLock after the case fix".
  - **Hotkeys** - the master switch plus a separate on/off and chord for the case fix and the clipboard manager, and the option to yield the chords to a focused remote-desktop client.
  - **Layout conversions** - one table holding **every** chord that converts text between layouts, EN ⇄ RU included. Each row is a pair of installed layouts plus its own combination and on/off switch, and each works in both directions.
  - **Windows languages** - install / reorder / remove Windows keyboard layouts, choose the cycle chord (Alt+Shift, Ctrl+Shift, `` ` `` or off), and assign direct per-language shortcuts that Windows itself handles. Both sections take a one-time backup of your pre-CyrFlip state with a one-click restore.
  - **Quick launch** - the scenario launcher (see below).
  - **Translation** - the local translator (see below): the Ollama address and model, the buttons that install, start and check it, the table of translation directions, and what to do with the result.
  - **Clipboard** and **About & Advanced** - history options, its transparency and search, plus the caret-position diagnostics.

CyrFlip is a normal desktop app, not a Windows service: a global keyboard hook and the layout indicator must run in your interactive session, so "autostart" is a per-user startup entry rather than a service.

## Quick launch (scenario launcher)

An optional module that absorbs [OneClickRunner](https://github.com/SerZhyAle/OneClickRunner) into CyrFlip — one tray process instead of two. **Off by default**; enable it on Settings → **Quick launch**.

- **Scenarios** are either *program/script* (path, arguments, working folder, an optional "run as administrator") or *yt-dlp* (download folder + extra options; the link is asked for on every run, and the external `yt-dlp` tool must be on `PATH`). Add, edit, clone, reorder, search, export and import them in the settings table.
- **Four ways to run one:** the tray **Quick launch** submenu, the settings table (double-click / Enter), an optional **global hotkey per scenario**, and the taskbar **Jump List** — right-click the CyrFlip icon on the taskbar (pin it to have the list handy even when CyrFlip isn't running: a Jump List click then does a one-shot launch without starting the tray).
- **Storage:** one XML per scenario in `%APPDATA%\CyrFlip\Scenarios`, format-compatible with OneClickRunner. Disabling the module clears the tray/Jump List surfaces but keeps the files.
- **Migration:** on first enable CyrFlip offers to copy your existing OneClickRunner scenarios (`%APPDATA%\OneClickRunner\Scenarios`); the originals are never modified, and the "Import from OneClickRunner…" button repeats the import any time. `.ps1` runs via PowerShell with a one-off `-ExecutionPolicy Bypass`, `.bat`/`.cmd` via `cmd.exe`; elevation is asked only for scenarios marked "run as administrator".

### Interface languages

The UI ships in **English, Русский, Українська, Deutsch, Italiano, Español, Français, Português, العربية, हिन्दी, বাংলা, اردو and 中文**. A fresh install follows the Windows display language and falls back to English. Arabic and Urdu are mirrored right-to-left. Translations for the languages the author does not speak are machine-made and not proofread — corrections are welcome via an issue or a pull request.

## Translation (local Ollama)

An optional module that translates the selected text with a language model running **on your own computer**. **Off by default**; enable it on Settings → **Translation**. While it is off no hotkey is bound, there is no tray entry, and CyrFlip opens no network connection at all.

- **How it goes:** select text anywhere in Windows, press the chord (the row created on the first enable uses **Ctrl+Shift+F9**, since F12 is the layout flip, F11 the case fix and F10 the clipboard manager), and a small window appears next to the mouse pointer and fills in as the model writes.
- **[Ollama](https://ollama.com) is installed separately** — a free program you install once on your own machine. CyrFlip doesn't bundle it, has no account and no key, and no text is sent to the developer or to any cloud service. The address is `http://localhost:11434` by default, that is your own computer; the settings tab says plainly that entering another address sends the selected text to that machine.
- **Directions are an open-ended table**, like the layout conversions: each row is "translate into this language" plus its own global hotkey. Besides the 13 interface languages a row can target **the interface language** or **the language of the keyboard layout active in the target window**, both resolved at the moment the chord is pressed.
- **The result** can be copied to the clipboard — where the optional clipboard history records it like any other copy — or pasted straight over the selection. Both off by default.
- **One-press helpers** on the settings tab: install Ollama, start it, check the connection, and download a model. The recommended ones are `qwen2.5:3b` (~2 GB, the default), `gemma2:2b` (~1.6 GB, for weaker machines), `llama3.2:3b` (~2 GB) and `aya-expanse:8b` (~5 GB, translates noticeably better, wants 8 GB of RAM or more). The model is held in memory by the **Ollama** process, not by CyrFlip, which stays inside its 50 MB budget. In the Microsoft Store build the install button only opens ollama.com — a Store app must not download and run an installer.
- **Worth knowing:** the quality is the local model's; the first translation after a cold start takes a while, because the model has to load; Ollama and a model are a multi-gigabyte download you make once; and of a long selection the first 4000 characters are translated, which the window tells you.

## VS Code extension

Inside VS Code the external marker can't track the caret precisely (Monaco draws its own caret).
The companion extension reads the layout CyrFlip publishes and renders the marker **exactly at the
editor caret**.

**How they link up:** CyrFlip writes the current layout code to `%LOCALAPPDATA%\CyrFlip\layout.txt`
(see [LayoutPublisher.cs](src/CyrFlip/LayoutPublisher.cs)); the extension watches that file and draws
the two-letter marker of **any** layout at the caret, plus a status-bar indicator. **CyrFlip must be running** for the
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

All settings are stored in the Windows Registry (`HKCU\Software\CyrFlip`) and are changed through Settings — no config file to edit.

| Setting | Default | What it does |
| --- | --- | --- |
| Conversion table | one EN ⇄ RU row on `Ctrl+Shift+F12` | Every chord that converts text between layouts. The first row is created on the first run and can be edited or deleted like any other; add as many pairs as you like |
| Case hotkey | `Ctrl+Shift+F11` | Inverts the case of the selection — the "I left CapsLock on" fix |
| Clipboard hotkey | `Ctrl+Shift+F10` | Shows or hides the clipboard manager strip |
| Hotkey switches | on | A master switch plus per-hotkey toggles (fix-CapsLock / clipboard) in Settings → Hotkeys; each conversion row carries its own switch |
| Yield hotkeys to remote desktop | off | While a remote-desktop client (mstsc/msrdc) is focused, CyrFlip lets the chord pass to the remote session, so a CyrFlip running there handles it — avoids the double-instance clash on both ends of an RDP connection |
| Cursor indicator | off | Replaces the system I-beam with a layout-branded cursor |
| Caret overlay | on | Shows the layout marker next to the blinking text caret |
| Caret dot style | off | Coloured dot instead of the layout letters in the overlay |
| Change the layout after converting text | off | After a conversion, also switches the active window to the layout the text now reads in, so you can keep typing straight away |
| Synchronize CapsLock after the case fix | off | After a case fix, also toggles the physical CapsLock key so the next keystrokes match |
| Interface language | OS language | 13 languages; falls back to English when Windows runs in a language CyrFlip has no translation for |
| Keep awake / keep the screen on | off | Live-only switches (never persisted, off on every launch) that stop Windows sleeping or blanking the screen on idle |
| Clipboard history | off | Encrypted local text history; toggle it from the tray or Settings |
| Show clipboard manager window | on | Remembers whether the manager window is open — close it and it stays closed on the next launch, while history keeps capturing in the background |
| Quick launch | off | The scenario launcher: tray submenu, per-scenario hotkeys and taskbar Jump List tasks. Scenarios live in `%APPDATA%\CyrFlip\Scenarios` (one XML each) and survive the switch being turned off |
| Translation | off | The local translator. Holds the table of directions with their own chords (the first row gets `Ctrl+Shift+F9`), the Ollama address (`http://localhost:11434` by default) and model (`qwen2.5:3b`), and whether the result is copied to the clipboard or pasted over the selection (both off). Ollama itself is installed separately |

Settings → **Windows languages** writes **Windows'** own settings rather than CyrFlip's: the installed keyboard layouts (`HKCU\Keyboard Layout\Preload` plus the modern user-profile store), the cycle chord and the per-language switch shortcuts (`HKCU\Control Panel\Input Method\Hot Keys`). Each of the two sections snapshots your pre-CyrFlip state once and can restore it.

A legacy `config.json` (next to the exe or in `%APPDATA%\CyrFlip\`) is migrated to the registry automatically on first run.

## Known issues

- **The caret marker doesn't appear in some apps.** CyrFlip locates the text caret via the Windows system caret or UI Automation. A few apps keep their caret a closely guarded secret and expose neither - chiefly **console/terminal windows** (Command Prompt, PowerShell, Windows Terminal) and the occasional app with custom-drawn text and weak UI Automation support. There the caret marker bows out gracefully; the tray icon and mouse-cursor marker still show the layout. And in some editors (e.g. VS Code and other Monaco-based ones), UI Automation reports the caret position imprecisely, so the marker may appear toward the edge of the input rather than exactly at the caret - for VS Code, use the [companion extension](vscode-extension/), which places it exactly at the editor caret.
- **The mouse text cursor (I-beam) can stay changed after a force-kill.** CyrFlip replaces the system I-beam globally and politely restores it on exit. If the process is killed hard (e.g. *End task* in Task Manager), it never gets to say goodbye, so Windows keeps the fancy cursor until you run CyrFlip again or sign out and back in.
- **IME and dead-key input.** A conversion profile changes characters Windows can resolve to one physical key. Composed/dead-key output and already composed IME text (such as Chinese Pinyin) are ambiguous and are left unchanged; the target layout still switches after a successful conversion.
- **Two layouts of one language share a code.** The indicator shows the language, not the variant, so US and Dvorak both read `EN`, and the standard Russian layout and Russian Typewriter both read `RU`. The **Windows languages** tab shows the exact KLID of each installed layout, which is what conversion profiles bind to.
- **Some Windows-language changes need a sign-out.** Installing, reordering or removing a layout, and the per-language shortcuts, are applied to the live session immediately, but Windows occasionally only settles them after signing out and back in. The Microsoft Store build additionally runs in a container, so Windows may redirect those registry writes into the package — the tab warns about this and links to the Windows settings.

## Antivirus false positives

Some antivirus engines - notably **Avast / AVG**, which report it as `IDP.Generic` ("Behavior Shield") - may flag `CyrFlip.exe` as suspicious. This is a **heuristic false positive**, not malware, and it's **normal for every keyboard-layout indicator** (Punto Switcher and similar tools trip the same heuristics). In fairness to the antivirus, CyrFlip does look guilty on paper: by design, a layout indicator + transliterator does exactly what a behavioural keylogger heuristic watches for - it installs a global keyboard hook (`WH_KEYBOARD_LL`), synthesizes keystrokes (`SendInput`), reads/writes the clipboard, and swaps the system I-beam cursor. Same toolkit, very different intentions.

CyrFlip is open source - you can read exactly what it does in [src/CyrFlip/](src/CyrFlip/) - and **static scanners agree it's clean: a build scores 0/71 on [VirusTotal](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection)** (Avast and AVG included). The local flag is purely *behavioural* (Avast's runtime Behavior Shield) and reputational (unsigned exe, run from a temp folder) - things VirusTotal's static engines don't replicate.

What actually reduces the flags (in order of impact):

- **Don't run it from a temporary folder.** Launching the exe straight out of an archive or from `%TEMP%` (e.g. a `Temp\Rar$..` extraction path) is itself a strong reputation red flag. **Unpack the ZIP to a permanent location** such as `%LOCALAPPDATA%\Programs\CyrFlip\` and run it from there - this alone clears many behaviour-based detections.
- **Code signing.** Releases are currently published **unsigned** (the release pipeline has an Authenticode step, but no signing certificate is configured; the Microsoft Store build is re-signed by the Store). A valid code signature is the single biggest factor in lowering heuristic flags — installing from the Microsoft Store or winget therefore trips fewer of them.
- **Report the false positive** so the vendor whitelists the file (usually corrected within a few days): [Avast false-positive form](https://www.avast.com/false-positive-file-form.php) · [AVG false-positive form](https://www.avg.com/en-ww/report-false-positive). As the app's author you can also enrol in the [Avast/AVG Whitelisting Program](https://businesshelp.avast.com/Content/Products/General_Help/Whitelisting/WhitelistingProgram.htm) so future builds stay cleared.
- **Verify the binary yourself.** Check its SHA256 against the `.sha256` published alongside each release, and scan your own download on [VirusTotal](https://www.virustotal.com/) - or see the [report for a recent build](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection) (0/71).

**По-русски:** срабатывание `IDP.Generic` у Avast/AVG - это **ложная эвристика**, а не вирус, и это **норма для любого индикатора раскладки** (Punto Switcher ловится так же): приложение по своей природе использует глобальный хук клавиатуры, инъекцию нажатий и буфер обмена. Статические сканеры это подтверждают - файл показывает **0/71 на [VirusTotal](https://www.virustotal.com/gui/file/faa7534b168147a00854227c0787fbe0847d47ae82a70ab13327159b5b026dbc/detection)** (Avast и AVG в том числе); локальный флаг - чисто **поведенческий** (Behavior Shield) и репутационный (неподписан, запуск из временной папки). Что помогает: **не запускать из временной папки** (распакуйте архив в постоянный каталог, например `%LOCALAPPDATA%\Programs\CyrFlip\`), пользоваться подписанными релизами и отправить файл в белый список через формы Avast/AVG выше.

## Related project

- [Universal Agent Kit](https://serzhyale.github.io/universal-agent-kit/) - a companion toolkit by the same author.

## Author

**SerZhyAle** - [sza.od.ua](https://sza.od.ua) · [sza@ukr.net](mailto:sza@ukr.net)

## License

MIT - see [LICENSE](LICENSE).
