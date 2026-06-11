# CyrFlip — keyboard layout at the caret (VS Code)

Companion extension for [CyrFlip](https://github.com/SerZhyAle/CyrFlip). It shows the active
keyboard layout (**EN / RU / UK**) right at the **text caret** inside the editor — accurately,
because the extension reads Monaco's real caret position (something the external CyrFlip overlay
can't do reliably in VS Code/Electron).

## How it works

- The **CyrFlip app** detects the keyboard layout and writes the current code to
  `%LOCALAPPDATA%\CyrFlip\layout.txt`.
- This extension watches that file and renders a small coloured marker (with a black outline)
  diagonally below-right of the caret, so it never shifts or covers your text.

So **CyrFlip must be running** for the marker to appear. Everywhere outside the editor, CyrFlip's
own tray icon and mouse-cursor marker keep working.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `cyrflip.layoutFile` | `""` | Override the layout file path. Empty = `%LOCALAPPDATA%\CyrFlip\layout.txt`. |
| `cyrflip.showStatusBar` | `true` | Also show the layout in the status bar. |
| `cyrflip.pollIntervalMs` | `200` | How often (ms) to check the layout file. |

## Build

```powershell
npm install
npm run compile
```

Press **F5** in VS Code to launch an Extension Development Host, or package a `.vsix`:

```powershell
npx @vscode/vsce package
```

## License

MIT.
