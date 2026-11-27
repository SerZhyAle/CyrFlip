---
paths:
  - "src/CyrFlip/MouseHook.cs"
  - "src/CyrFlip/MouseChord.cs"
  - "src/CyrFlip/SelectionProbe.cs"
  - "src/CyrFlip/SelectionStats.cs"
  - "src/CyrFlip/LaunchTarget.cs"
  - "src/CyrFlip/TextContextMenu.cs"
  - "src/CyrFlip/ForegroundActivator.cs"
  - "src/CyrFlip/TextMenuLog.cs"
---

# Text context menu over the selection

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

7. **CyrFlip's own context menu over the selection ("Контекстное меню"), opt-in and off by default.**
   `EnableContextMenu=0` means the **`WH_MOUSE_LL` hook is not installed at all** - a hook that sits in
   the path of every mouse move on the machine must not exist for a feature nobody switched on. Enabled,
   a mouse chord (`MouseChord`, default **Ctrl + right click**, stored as an invariant token in
   `ContextMenuChord`) is swallowed - down **and** up (a low-level hook never sees a double-click message;
   the second down of a fast double click is swallowed like the first), because most apps open
   their menu on `WM_CONTEXTMENU`, which arrives after the release - and CyrFlip's own menu opens at the
   pointer: Copy/Cut/Paste first, then - only when the selection itself parses as something openable -
   **"Открыть «…»"** (`LaunchTarget`, below), then the live conversion rows, the case flip and the
   translation rows, then the Launcher submenu and the history window, then Settings, and **last** the
   two report lines ("Выделено строк: N / символов: M" - `SelectionStats`, drawn as `ToolStripLabel`s
   so they are captions rather than commands, and omitted entirely when the selection could not be
   read). Last because the menu opens *under the pointer*: whatever is at the top is what the hand is
   already on, and that place belongs to Copy, where every other context menu in Windows puts it.
   The chord list is **fixed** (six
   options) rather than a capture dialog because the right button **must** carry a modifier, or CyrFlip
   would eat every context menu in Windows; `MouseChord.TryParse` rejects a bare right click even from a
   hand-edited registry. **Why our own menu and not Windows'**: the menu of a modern app (Electron, WinUI,
   WPF, Qt) is drawn by the app and has no `HMENU` at all, and editing a real Win32 `EDIT` menu needs a
   native DLL injected into every process - the end of "one exe, no dependency" and an automatic Store
   rejection. `TextContextMenu` is a pure static builder (`ContextMenuStrip`, whose
   `ShowWithoutActivation` is already true, so **the menu never takes the focus and the selection
   survives**), and the launcher submenu comes from the very same `LauncherTrayMenu.Fill` as the tray and
   the taskbar button. Greying follows `SelectionProbe` (§ below); a drop-down owned by a non-foreground
   process never sees the click that should dismiss it, so `MouseHook.ForeignButtonDown` closes it -
   `SetForegroundWindow` (the taskbar button's trick) is forbidden here, it would take the focus this
   whole feature exists to keep. The keyboard opens the same menu with **`Ctrl+Shift+Alt+M`** (`TextMenuHotkey`, S0045 K2) at the caret; opened that way the menu does take the foreground - the arrows have to reach it - through a hidden window of ours, and hands it back when it closes without a command.
   Spec: [PLAN/done/ContextMenu_Spec_Idea_v0.1.md](../../PLAN/done/ContextMenu_Spec_Idea_v0.1.md).

## Modules (`src/CyrFlip/`)

- **MouseHook.cs** - `SetWindowsHookEx(WH_MOUSE_LL)`, installed **only while the text context menu is on**.
  Four rules are load-bearing: the callback sees **every mouse move** (up to 1000/s), so everything that
  is not one of our button messages leaves through a `switch` before a byte is marshalled - and a GC pause
  on this thread stalls the pointer system-wide, with Windows dropping a hook that exceeds
  `LowLevelHooksTimeout` (300 ms); nothing is **shown** from the callback (the subscriber posts to the UI
  thread - opening a menu inside a hook would run a foreign message loop there) and no thread is started
  there either - the press only stamps and signals `SelectionProbe.Worker` (S0030 HT-3) and posts the
  **mask key** for a chord with Alt or Shift (KC2-3 - Windows never saw the swallowed click, so the
  modifier's release would be a bare Alt/Shift tap: menu mode, an IME mode switch); **down and up**
  are both swallowed (the second down of a fast double click is swallowed like the first); and the up is
  swallowed by a **flag** (`SwallowUpGate`), not by re-checking modifiers, because the user often releases
  Ctrl before the button. The flag **expires after 3 s**: an up released
  over an elevated window is hidden from the hook by UIPI, and a flag that never cleared used to eat the
  next ordinary right click anywhere. `ForeignButtonDown` fires only while
  `UpdateForeignClickWatch(true)` (i.e. while the menu is open) - the outside click that dismisses it.
- **MouseChord.cs** - modifiers + button, deliberately **not** a `Hotkey` (a keyboard chord always needs a
  trigger VK, a mouse chord never has one). Registry token is invariant (`Ctrl+RightClick`), the label is
  translated (`Display(translate)` - modifiers stay Latin, only the button name is a localization key).
  `Choices` is the fixed six-option list the settings dropdown offers.
- **SelectionProbe.cs** - "is anything selected right now, and what does it say?", asked when the chord
  goes **down** and read when it comes **up**, so the 80-150 ms the user spends holding the button pays
  for the cross-process calls. The probing runs on **one long-lived MTA `Worker`** created and stopped
  with the mouse hook (ticket S0030 HT-3) - the hook callback used to create a thread per press; a
  request overtaken before the worker reached it (the second click of a double click) is answered
  Unknown at once. Three sources in cost order - `EM_GETSEL` via `SendMessageTimeout`
  (classic Edit/RichEdit only - **gated on the window class**, since `EM_GETSEL` to a non-edit reaches
  `DefWindowProc` and returns a confident, wrong 0), then `Ia2Caret` (Chromium/Electron), then managed
  UIA `TextPattern.GetSelection` (a **degenerate** range = caret only = nothing selected) - and **each
  is asked twice: about the focused element and about the element under the pointer**
  (`WindowFromPoint`, `AccessibleObjectFromPoint`, `AutomationElement.FromPoint`). Two rules follow from
  the same bug, which is what read-only text used to look like: the selection lives under the pointer
  while the keyboard focus sits in a search box, a container node or nowhere useful, so a source would
  answer confidently about an element the user was not pointing at and **Copy went grey beside a live
  selection**. Hence **no source may veto another's sighting** (`Present` from any source wins; `Absent`
  is the verdict only when every source that could answer agreed) and **`Unknown` still counts as
  `Present`** - a greyed command beside a live selection is the failure the user sees, while a command
  that runs and does nothing is what "no selection → no-op" already means everywhere else. The probe also
  brings back **the selected text itself** when a source hands it over (`WM_GETTEXT` on an edit control,
  IA2 `get_text`, UIA `range.GetText`) - capped at 64K chars (`MaxTextChars` = 65536; 2048 is `LaunchTargets.MaxLength` for candidate parsing), and the only reason it exists is the
  launch item below. **The longest text wins, not the first**, and that is why every source is asked
  rather than the scan stopping at the first answer: measured live in Chromium, IA2 returns only the
  part of the selection inside the element *under the pointer* - one styled run, one highlighted word -
  so a selected paragraph came back as `text 1 chars` in `context-menu.log`. No source announces that it
  is handing over a fragment, so length is the only honest tie-break. It **never** probes by synthesizing Ctrl+C - that would put text on the clipboard
  to draw a menu, and the history would record it as a real copy. `BudgetMs` (250) is how long the probe
  may work; **`MaxWaitMs` (90) is the separate cap on how long `Collect` may block the UI thread**, which
  the keyboard hook shares and which Windows drops after ~300 ms.
- **SelectionStats.cs** - the two numbers the menu prints at its foot, so the user knows how
  big the clipboard is about to get before pressing anything. Pure arithmetic on the text the probe
  already read - nothing is copied to produce it. Two rules are decisions rather than arithmetic and
  each has its own test: a **trailing** line break does not open a new line (dragging past the end of
  a line is one line, as it looks on screen), and `\r\n` is one break rather than two. A count taken
  from a read that hit `SelectionProbe.MaxTextChars` is printed with a **"+"** (`Format`): the cap is
  why that constant is 64K rather than link-sized, since a ceiling the user can reach silently turns
  the number into a lie.
- **LaunchTarget.cs** - "the user selected a link - offer to open it". Turns the probe's text into
  something the shell can open, and **refuses far more than it accepts**: the text came out of whatever
  window the pointer was over, so it is untrusted input the user is one click away from handing to
  `ShellExecute`. Exactly three shapes pass - an absolute URL in a whitelisted scheme (http/https/ftp/ftps/
  mailto; `javascript:`, `shell:`, `ms-settings:` are refused **by name**), a bare host whose TLD is in a
  curated list (a whitelist, because "two to twenty-four letters" turns `readme.md` and `setup.exe` into
  web addresses), and a path that **exists on this machine right now** - and anything else yields no menu
  item at all rather than one that fails. The selection is tried **whole first, then word by word**: a
  mouse selection picks up the space or the sentence around the link, and Chromium hands the text back
  with its neighbours attached, so "см. https://example.com тут" still offers the link - the caption
  names exactly what will open, and the first candidate wins. A path with spaces is found only by the
  whole-text pass, which is the right way round, since splitting on spaces cannot put it back together. Anything that runs code, or anything on a network share, comes
  back as `LaunchKind.Program`, which `CyrFlipContext.LaunchSelection` **confirms first, naming the full
  path** - the menu caption is elided, and a truncated path is exactly what a plausible-looking one
  relies on. The confirmation stays on the UI thread; the `ShellExecute` after it runs on the pool
  (S0030 HT-5), since a confirmed share would otherwise freeze input for the SMB connect.
  **A remote path is never probed** (ticket S0008 LS-1): asking whether `\\host\share\x`
  exists opens an SMB session to a host the author of the text chose and stalls the thread for as long
  as it does not answer - so UNC (either slash, `\\?\UNC\`), `file://host/..`, a mapped drive whose
  `GetDriveType` is `DRIVE_REMOTE` and a root-relative path on a network current directory are
  recognised from the text alone and come back as `Program` with no existence check; the device
  namespace (`\\?\C:\`, `\\.\`) is not a target at all. The parse runs on `SelectionProbe`'s worker
  (`SelectionSnapshot.Launch`), never on the UI thread the hooks share. A local path is classified by
  what **opens** (LS-3): its canonical form with Win32's trailing dots/spaces stripped and the on-disk
  name of its last segment, against our list plus `AssocIsDangerous`. A bidi override or mark refuses
  the candidate - checked again on the final string after `Uri.LocalPath` unescapes a `%E2%80%AE` and
  after the variables are expanded, and on the on-disk name (S0034 LS2-1) - a URL caption elides the
  path but never the host, and `&` is doubled in the caption (LS-5). A **remote** whole-text candidate
  whose last segment has whitespace and no extension (`\\server\share\x.txt for details`) goes to the
  word pass first, since nothing probes it (LS2-4). The probe's verdict is published before the parse,
  so a slow parse costs only the launch item (LS2-5). The parser takes its probes as a `LaunchProbes` argument, so the whole matrix is
  unit-tested without a disk (`LaunchTargetTests`).
- **TextContextMenu.cs** - the pure builder (`TextContextMenuState` in, `ContextMenuStrip` items out), so
  the whole "what is shown / what is grey / which separator disappears" matrix is unit-tested. Same
  copy-out-before-dispose discipline as `LauncherTrayMenu`, and for the same reason. The launch item is
  the one entry that is **absent rather than greyed** when it does not apply: every other command works
  on a selection it never has to read, while this one can only exist once the text has been read *and*
  parsed - and an item that appeared over every piece of prose and then failed would be worse than none.
- **ForegroundActivator.cs** - `Activate(form)` for a window opened from a surface that never held the
  focus. Windows grants `SetForegroundWindow` only to the foreground process or the receiver of the last
  input event, and **refuses silently**: `Form.Activate()` returns, the window is visible, and it sits
  behind the user's editor - which is exactly how "Настройки" in the text context menu read as broken.
  The fix is to share the foreground thread's input queue for the duration of the call
  (`AttachThreadInput`) - but only with a thread that is pumping (S0036 UI-1): a **hung** window
  (`IsHungAppWindow`, no wait) is never attached to, since the activation would then wait on it with the
  hooks' thread; one that answers `WM_NULL` within 50 ms is; and when activation is still refused (a busy
  editor, a recalculating Excel) the window is at least raised above the others without the focus
  (a `HWND_TOPMOST`/`HWND_NOTOPMOST` pair) instead of opening behind them - which is what the plain 50 ms
  gate of `eb9a156` had brought back. Used by `ShowSettings`, `ShowHistorySearch` and `ClipboardHistoryWindow.ToggleVisible`.
  Every action of the text context menu is also deferred one message-loop turn (`CyrFlipContext.Defer`):
  inside the `Click` the drop-down is still tearing down and still holds the mouse capture.
