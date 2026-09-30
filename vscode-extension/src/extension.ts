// This extension is the consuming half of LAYOUT-SIGNAL (it reads the layout, rules 1, 3, 5, 6, 7) and
// the claiming half of its rules 9 and 10 (editor-caret.txt), and it draws with LAYOUT-PALETTE rule 1's
// ladder from the copy of the table it packages. Contracts are cited by id and rule number; the shared
// catalog's location is named once, in the repository's CLAUDE.md.
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

// The Store package's family name: SZA.CyrFlip plus the hash of its publisher - a frozen anchor, so
// the app's per-user folder can be computed here without asking anyone (LAYOUT-SIGNAL 1.1 rule 1).
const STORE_PACKAGE_FAMILY = 'SZA.CyrFlip_fdk7e19xt9z9j';

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
  // LAYOUT-SIGNAL 1.1 rule 1. The desktop app writes layout.txt to %LOCALAPPDATA%\CyrFlip when
  // unpackaged, and to its package's own per-user folder when installed from the Microsoft Store
  // (MSIX) - the path is computed from the package family name, a frozen identity. Pick the most
  // recently written of these per-user files, so a stale leftover from the other install mode never
  // wins. The machine-wide %ProgramData%\CyrFlip is where a Store build before 1.1 wrote (and a 1.1
  // one still mirrors to): it is read only when no per-user file exists, because every account on
  // the PC shares it and another user's layout must never outrank this user's own. Default to the
  // unpackaged path when nothing exists yet.
  const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  const programData = process.env.ProgramData || 'C:\\ProgramData';
  const perUser = [
    path.join(localAppData, 'CyrFlip', 'layout.txt'),
    path.join(localAppData, 'Packages', STORE_PACKAGE_FAMILY, 'LocalCache', 'Local', 'CyrFlip', 'layout.txt'),
  ];
  const machineWide = [path.join(programData, 'CyrFlip', 'layout.txt')];
  return newest(perUser) ?? newest(machineWide) ?? perUser[0];
}

function newest(candidates: string[]): string | undefined {
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
  return best;
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
  //   - transform           → drop it below-right of the caret, pressed against it (-1px) and half
  //                           its own height lower than the next line's top (1.5em), so it clears
  //                           the text right after the caret - the rule the desktop overlay follows
  //   - text-shadow         → the black outline
  const css =
    'none; position: absolute; transform: translate(-1px, 1.5em); font-size: 0.82em; ' +
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
 * Written beside layout.txt, in the folder the app already publishes to, and by mtime alone - the app
 * never parses the contents (LAYOUT-SIGNAL rule 9). The one reader of the contents is this extension
 * itself: the last field is the writing window's session id, so a window that loses the focus deletes
 * only a claim it wrote, never the one another VS Code window has just made (see clearSignal).
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
    fs.writeFileSync(signalFilePath(), `${currentCode} ${new Date(now).toISOString()} ${vscode.env.sessionId}\n`, 'utf8');
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

/**
 * Withdraw this window's claim. There is one claim file for every VS Code window on the machine, so
 * window A losing the focus used to delete the claim window B had written a moment earlier - and the
 * app then drew its overlay beside B's marker until B's next write. Only a file that still carries this
 * window's session id is deleted.
 */
function clearSignal(): void {
  const file = signalFilePath();
  try {
    const owner = fs.readFileSync(file, 'utf8').trim().split(/\s+/).pop();
    if (owner !== vscode.env.sessionId) {
      return;
    }
    fs.unlinkSync(file);
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
    // Only the user's own work in the active editor renews the claim. A language server's edit, an
    // Output channel append, a reload from disk or an agent editing another file in the background
    // fire these events too, and counting them kept the claim alive all the time VS Code had the
    // focus - hiding the app's marker in the chat box, where this extension cannot draw.
    vscode.window.onDidChangeTextEditorSelection((e) => {
      if (e.textEditor === vscode.window.activeTextEditor && e.kind !== undefined) {
        noteEditorActivity();
      }
      render();
    }),
    // Not activity: an editor also becomes active when an agent or a command opens a file while the
    // user types in the chat box, and renewing the claim then hid the app's marker there. The user's
    // first click or key in the editor is a selection change with a kind, and that renews it.
    vscode.window.onDidChangeActiveTextEditor(() => { render(); }),
    vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document === vscode.window.activeTextEditor?.document && e.contentChanges.length > 0) {
        noteEditorActivity();
      }
    }),
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
