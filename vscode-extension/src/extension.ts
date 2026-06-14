import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

// Per-layout colour, matching the CyrFlip app (EN=blue, RU=red, UK=green).
const COLORS: Record<string, string> = {
  EN: '#4DA3FF',
  RU: '#FF5A5A',
  UK: '#5AD86A',
};
const DEFAULT_COLOR = '#CCCCCC';

// 4-direction black outline so the bright code stays legible on any background.
const OUTLINE = '-1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000';

let currentCode = '';
let pollTimer: NodeJS.Timeout | undefined;
let statusItem: vscode.StatusBarItem | undefined;
const decoCache = new Map<string, vscode.TextEditorDecorationType>();

function config<T>(key: string, fallback: T): T {
  return vscode.workspace.getConfiguration('cyrflip').get<T>(key, fallback);
}

function layoutFilePath(): string {
  const configured = config<string>('layoutFile', '');
  if (configured && configured.trim().length > 0) {
    return configured;
  }
  // The desktop app writes layout.txt to %LOCALAPPDATA% when unpackaged, but to %ProgramData%
  // when installed from the Microsoft Store (MSIX): a packaged process's %LOCALAPPDATA% write is
  // virtualized into the package container, where this (unpackaged) extension can't see it.
  // Pick the most recently written of the two so a stale leftover from the other install mode
  // (e.g. an old %LOCALAPPDATA% file after switching to the Store build) never wins. Default to
  // the unpackaged path when neither exists yet.
  const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  const programData = process.env.ProgramData || 'C:\\ProgramData';
  const candidates = [
    path.join(localAppData, 'CyrFlip', 'layout.txt'),
    path.join(programData, 'CyrFlip', 'layout.txt'),
  ];
  let best: string | undefined;
  let bestMtime = -1;
  for (const candidate of candidates) {
    try {
      const mtime = fs.statSync(candidate).mtimeMs;
      if (mtime > bestMtime) {
        bestMtime = mtime;
        best = candidate;
      }
    } catch {
      // not present; skip
    }
  }
  return best ?? candidates[0];
}

function colorFor(code: string): string {
  return COLORS[code] ?? DEFAULT_COLOR;
}

function decorationFor(code: string): vscode.TextEditorDecorationType {
  const cached = decoCache.get(code);
  if (cached) {
    return cached;
  }
  // The CSS in `textDecoration` is injected onto the ::after pseudo-element:
  //   - position: absolute  → taken out of flow, so it never shifts the document text
  //   - transform           → drop it diagonally below-right of the caret
  //   - text-shadow         → the black outline
  const css =
    'none; position: absolute; transform: translate(3px, 1em); font-size: 0.82em; ' +
    `font-weight: bold; text-shadow: ${OUTLINE}; pointer-events: none; ` +
    'white-space: nowrap; z-index: 1;';

  const deco = vscode.window.createTextEditorDecorationType({
    rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed,
    after: {
      contentText: code,
      color: colorFor(code),
      textDecoration: css,
    },
  });
  decoCache.set(code, deco);
  return deco;
}

function render(): void {
  const activeEditor = vscode.window.activeTextEditor;

  // Clear decorations on all visible editors that are not currently active
  for (const editor of vscode.window.visibleTextEditors) {
    if (editor !== activeEditor) {
      for (const deco of decoCache.values()) {
        editor.setDecorations(deco, []);
      }
    }
  }

  if (!activeEditor) {
    return;
  }

  // Clear and set decorations on the active editor
  for (const deco of decoCache.values()) {
    activeEditor.setDecorations(deco, []);
  }

  if (!currentCode) {
    return;
  }
  const caret = activeEditor.selection.active;
  activeEditor.setDecorations(decorationFor(currentCode), [new vscode.Range(caret, caret)]);
}

function updateStatus(): void {
  if (!statusItem) {
    return;
  }
  if (config<boolean>('showStatusBar', true) && currentCode) {
    statusItem.text = `$(keyboard) ${currentCode}`;
    statusItem.color = colorFor(currentCode);
    statusItem.show();
  } else {
    statusItem.hide();
  }
}

function readLayout(): void {
  let code = '';
  try {
    code = fs.readFileSync(layoutFilePath(), 'utf8').trim().toUpperCase().slice(0, 4);
  } catch {
    code = ''; // file missing (CyrFlip not running) → no marker
  }
  if (code !== currentCode) {
    currentCode = code;
    render();
    updateStatus();
  }
}

function restartPoll(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  const interval = Math.max(50, config<number>('pollIntervalMs', 200));
  pollTimer = setInterval(readLayout, interval);
}

export function activate(context: vscode.ExtensionContext): void {
  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusItem.tooltip = 'CyrFlip - active keyboard layout';
  context.subscriptions.push(statusItem);

  context.subscriptions.push(
    vscode.window.onDidChangeTextEditorSelection(() => render()),
    vscode.window.onDidChangeActiveTextEditor(() => render()),
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('cyrflip')) {
        restartPoll();
        updateStatus();
      }
    }),
  );

  readLayout();
  restartPoll();

  context.subscriptions.push({
    dispose: () => {
      if (pollTimer) {
        clearInterval(pollTimer);
      }
      for (const deco of decoCache.values()) {
        deco.dispose();
      }
      decoCache.clear();
    },
  });
}

export function deactivate(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  for (const deco of decoCache.values()) {
    deco.dispose();
  }
  decoCache.clear();
}
