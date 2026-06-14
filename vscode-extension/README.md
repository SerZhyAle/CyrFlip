# CyrFlip - keyboard layout at the caret (VS Code)

Companion extension for the **[CyrFlip](https://github.com/SerZhyAle/CyrFlip)** Windows app.
It shows the active keyboard layout (**EN / RU / UK**) right at the **text caret** inside the
editor — accurately, because the extension reads Monaco's real caret position (something the
external CyrFlip overlay can't do reliably in VS Code / Electron).

> **Requires the CyrFlip desktop app to be running.** The app detects the layout; this extension
> only displays it. Get the app here: **https://github.com/SerZhyAle/CyrFlip**

## Install the desktop app with winget

If you don't have the **CyrFlip** Windows app yet, install it with:

```powershell
winget install --id SerZhyAle.CyrFlip -e
```

**По-русски:** эта команда устанавливает настольное приложение **CyrFlip** через
**[winget](https://learn.microsoft.com/windows/package-manager/winget/)** (Windows Package
Manager). Именно оно определяет текущую раскладку клавиатуры в Windows и
записывает её в файл. Это расширение VS Code само раскладку не определяет - оно только читает
данные от приложения и показывает `EN` / `RU` / `UK` прямо у текстового курсора.

## How the two pieces fit together

```
┌──────────────────────────┐        writes        ┌──────────────────────────┐
│  CyrFlip.exe (tray app)   │  ───────────────────▶ │  %LOCALAPPDATA%\CyrFlip\ │
│  detects EN / RU / UK     │   current layout code │       layout.txt         │
└──────────────────────────┘                       └─────────────┬────────────┘
                                                                  │ watches
                                                                  ▼
                                                   ┌──────────────────────────┐
                                                   │  This VS Code extension   │
                                                   │  draws the marker at the  │
                                                   │  editor caret             │
                                                   └──────────────────────────┘
```

- The **CyrFlip app** detects the keyboard layout and writes the current code to
  `%LOCALAPPDATA%\CyrFlip\layout.txt`.
- This extension watches that file and renders a small coloured marker (with a black outline)
  diagonally below-right of the caret, so it never shifts or covers your text. It also shows the
  layout in the status bar.

Everywhere **outside** the editor, CyrFlip's own tray icon and mouse-cursor marker keep working.

## Usage

1. **Install and run [CyrFlip](https://github.com/SerZhyAle/CyrFlip)** (the tray app). Its icon
   appears in the notification area and shows the active layout.
2. **Install this extension** and reload VS Code (`Developer: Reload Window`). It activates on
   startup.
3. **Click into a code editor and type.** Switch your keyboard layout — the coloured `EN`/`RU`/`UK`
   marker follows your caret, and the status bar shows `⌨ EN/RU/UK`.

If nothing appears: confirm the app is running and that `%LOCALAPPDATA%\CyrFlip\layout.txt` exists
and updates when you switch layout. The status-bar indicator is the quickest way to confirm the
extension is reading the file.

### Scope

The caret marker only renders inside **code editors** (Monaco text documents). VS Code **webviews and
custom widgets** — the integrated terminal, search boxes, chat panels, the Command Palette — can't
host editor decorations, so the marker can't appear there. In those spots, rely on CyrFlip's
**mouse-cursor marker** and tray icon. The status-bar indicator stays visible everywhere.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `cyrflip.layoutFile` | `""` | Override the layout file path. Empty = `%LOCALAPPDATA%\CyrFlip\layout.txt`. |
| `cyrflip.showStatusBar` | `true` | Also show the layout in the status bar. |
| `cyrflip.pollIntervalMs` | `200` | How often (ms) to check the layout file. |

## Build & package

```powershell
npm install
npm run compile        # tsc → out/
npm run package        # produces cyrflip-vscode-<version>.vsix
```

Press **F5** in VS Code to launch an Extension Development Host for live debugging.

## Publishing (maintainers)

```powershell
npx @vscode/vsce login SerZhyAle   # paste a Marketplace PAT (Azure DevOps → Marketplace: Manage)
npm run publish                    # builds + uploads to the VS Code Marketplace
# optional, for VSCodium / Cursor / etc.:
npx ovsx publish cyrflip-vscode-<version>.vsix -p <open-vsx-token>
```

## License

MIT — see [LICENSE](LICENSE).
