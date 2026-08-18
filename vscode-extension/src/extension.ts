import * as vscode from 'vscode';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

// Per-layout colour, shared with the app rather than restated here: layout-colors.json is the
// machine-readable copy of src/CyrFlip/LayoutStyle.cs, and a C# test fails the build if the two
// disagree. This file used to carry its own three-entry table (EN/RU/UK), so ten of the thirteen
// curated languages rendered grey in the editor while the app drew them in colour.
import * as palette from './layout-colors.json';

const COLORS: Record<string, string> = palette.curated;
const LAYOUT_COLORS: Record<string, string> = palette.layouts;
const OTHER: string = palette.other;
const OPACITY: number = palette.markerOpacity;

// How long after the last editor activity this extension still claims the caret (see writeSignal).
const EDITOR_ACTIVITY_TTL_MS = 5000;
// The signal file is rewritten no more often than this while that claim holds.
const SIGNAL_WRITE_INTERVAL_MS = 500;

// 4-direction black outline so the bright code stays legible on any background.
const OUTLINE = '-1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000';

let currentCode = '';
let currentKlid = '';
let lastEditorActivity = 0;
let lastSignalWrite = 0;
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

/**
 * The layout's own shade when the app told us which layout is active, else the language's colour,
 * else the one neutral colour for everything outside the thirteen curated languages. Mirrors
 * LayoutStyle.ColorForLayout in the app; both tables come from layout-colors.json, and a C# test
 * fails the build if this file's copy drifts from the app's.
 */
function colorFor(code: string, klid: string): string {
  return LAYOUT_COLORS[klid] ?? COLORS[code] ?? OTHER;
}

function decorationFor(code: string, klid: string): vscode.TextEditorDecorationType {
  const key = `${code}|${klid}`;
  const cached = decoCache.get(key);
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
    `opacity: ${OPACITY}; white-space: nowrap; z-index: 1;`;

  const deco = vscode.window.createTextEditorDecorationType({
    rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed,
    after: {
      contentText: code,
      color: colorFor(code, klid),
      textDecoration: css,
    },
  });
  decoCache.set(key, deco);
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
  activeEditor.setDecorations(decorationFor(currentCode, currentKlid), [new vscode.Range(caret, caret)]);
}

function updateStatus(): void {
  if (!statusItem) {
    return;
  }
  if (config<boolean>('showStatusBar', true) && currentCode) {
    statusItem.text = `$(keyboard) ${currentCode}`;
    statusItem.color = colorFor(currentCode, currentKlid);
    statusItem.show();
  } else {
    statusItem.hide();
  }
}

/**
 * The active layout's KLID, which the app publishes to layout-klid.txt beside layout.txt. It is a
 * separate file rather than a second line of layout.txt because this extension ships on its own
 * clock: an already-installed copy reads the first four characters of layout.txt as the code, so
 * anything appended there would break it. An absent file simply means an older CyrFlip - the marker
 * then uses the language colour, exactly as before.
 */
function readKlid(): string {
  try {
    const file = path.join(path.dirname(layoutFilePath()), 'layout-klid.txt');
    return fs.readFileSync(file, 'utf8').trim().toUpperCase().slice(0, 8);
  } catch {
    return '';
  }
}

/**
 * Tell the desktop app "the editor caret is mine right now", so it hides its own overlay and the user
 * does not see two markers stacked at the same caret.
 *
 * The claim is deliberately time-limited. VS Code's API cannot say whether the focus is in the editor
 * or in the chat/terminal - `activeTextEditor` keeps pointing at the last editor either way - so the
 * only honest signal is recent editor *activity*: a keystroke, a selection change, an editor switch.
 * Five seconds after the last one the file goes stale and the app's overlay comes back, which is what
 * should happen once the user has moved to the chat box (where this extension cannot draw at all).
 *
 * Written beside layout.txt, in the folder the app already publishes to, and by mtime alone - the
 * contents are only there to make the file readable by a human debugging it.
 */
function writeSignal(): void {
  const now = Date.now();
  if (!vscode.window.state.focused || !vscode.window.activeTextEditor || !currentCode) {
    return;
  }
  if (now - lastEditorActivity > EDITOR_ACTIVITY_TTL_MS || now - lastSignalWrite < SIGNAL_WRITE_INTERVAL_MS) {
    return;
  }
  lastSignalWrite = now;
  try {
    fs.writeFileSync(signalFilePath(), `${currentCode} ${new Date(now).toISOString()}\n`, 'utf8');
  } catch {
    // Best-effort: a signal we cannot write only means the app keeps drawing its own marker.
  }
}

function signalFilePath(): string {
  return path.join(path.dirname(layoutFilePath()), 'editor-caret.txt');
}

function noteEditorActivity(): void {
  lastEditorActivity = Date.now();
  writeSignal();
}

function clearSignal(): void {
  try {
    fs.unlinkSync(signalFilePath());
  } catch {
    // absent already, or not ours to delete
  }
}

function readLayout(): void {
  let code = '';
  try {
    code = fs.readFileSync(layoutFilePath(), 'utf8').trim().toUpperCase().slice(0, 4);
  } catch {
    code = ''; // file missing (CyrFlip not running) → no marker
  }
  const klid = code ? readKlid() : '';
  if (code !== currentCode || klid !== currentKlid) {
    currentCode = code;
    currentKlid = klid;
    render();
    updateStatus();
  }
}

function restartPoll(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  const interval = Math.max(50, config<number>('pollIntervalMs', 200));
  pollTimer = setInterval(() => { readLayout(); writeSignal(); }, interval);
}

export function activate(context: vscode.ExtensionContext): void {
  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusItem.tooltip = 'CyrFlip - active keyboard layout';
  context.subscriptions.push(statusItem);

  context.subscriptions.push(
    vscode.window.onDidChangeTextEditorSelection(() => { noteEditorActivity(); render(); }),
    vscode.window.onDidChangeActiveTextEditor(() => { noteEditorActivity(); render(); }),
    vscode.workspace.onDidChangeTextDocument(() => noteEditorActivity()),
    vscode.window.onDidChangeWindowState((s) => { if (!s.focused) { clearSignal(); } }),
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
      clearSignal();
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
  // Leaving a fresh signal behind would hide the app's overlay in a VS Code window that is no longer
  // drawing a marker of its own.
  clearSignal();
}
