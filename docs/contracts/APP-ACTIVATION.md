# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.12, draft (read 2026-10-16; the 0.12 amendment, section 9, is CyrFlip's own: the two clipboard commands) |
| **Home** | `app-activation/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - single-instance mutex, Jump List named pipe IPC, and foreground window activation |

## What this repository implements and guarantees

- **Per-session single-instance mutex** (rule 2, `Program.cs:19`): Holds `Local\CyrFlipSingleInstance`. A secondary
  process forwards its command and returns at once, exit code 0, before any hook, tray icon or window exists
  (`Program.cs:20-28`).
- **Named pipe IPC resilience** (rule 3, `LauncherIpc.cs`): Server listens on `CyrFlip_Launcher_{userSid}_{sessionId}`
  (current user only, no network, one instance per session - ticket S0008 LS-2) with per-connection error
  boundaries. Failed connections never crash the listener. The sender connects with a 1000 ms bound
  (`TrySend`). CyrFlip registers **no URI scheme**: the activation path here is the pipe and the command line
  only, so rule 1 is not applicable (declared in the registry row).
- **Strict command parsing & normalization** (rule 4, `LauncherIpc.ParseCommand`): Accepts `/launcher-run:{guid}`,
  `/launcher-settings`, `/exit` and, since 0.12 (section 9), `/clipboard-send` and `/clipboard-get` - at most 256 characters, on the command line and on every pipe line. Normalizes
  GUIDs to canonical 36-char `D` format. Unknown commands are discarded and logged.
- **Autonomous one-shot execution** (rule 6, `Program.cs:33-45`): When invoked with `/launcher-run:{guid}` without a
  running instance, releases and closes the mutex and executes the standalone scenario without initializing
  hooks or tray icons.
- **Foreground window activation & hang protection** (rule 5, `ForegroundActivator.cs`): `BringWindowToTop` +
  `SetForegroundWindow`, with `AttachThreadInput` only after `IsHungAppWindow` and a 50 ms `WM_NULL`
  `SendMessageTimeout(SMTO_ABORTIFHUNG)` probe, detached in a `finally`.

## Declarations under the 0.11 amendment (section 7)

- **Transport (item A):** the named pipe only. CyrFlip does not use `WM_COPYDATA`.
- **Frozen names (item B):** `Local\CyrFlipSingleInstance` and the pipe-name shape `CyrFlip_Launcher_{userSid}_{sessionId}`
  are shipped anchors; rule 3's third shape is the declared one.
- **Focus and fallback (item C):** a tray app has no main window to raise on a second start, so a plain second
  launch is a silent no-op and a command the live instance did not accept in time is dropped (logged,
  `LauncherIpc.TrySend`), not started as a second instance - a second hook would be worse than a lost Jump List
  click. Both are declared product choices.
