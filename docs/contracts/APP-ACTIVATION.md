# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.15, draft (read 2026-11-15; section 11, the request-response profile of `APP-COMMANDS`, is held since ticket S0107; the 0.12 amendment, section 9, is CyrFlip's own: the two clipboard commands) |
| **Home** | `app-activation/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - single-instance mutex, Jump List named pipe IPC, and foreground window activation |

## What this repository implements and guarantees

- **Per-session single-instance mutex** (rule 2, `Program.Main`): Holds `Local\CyrFlipSingleInstance`. A secondary
  process forwards its token and returns at once, exit code 0, before any hook, tray icon or window exists
  (`Program.RunWithMutex`). A command of `APP-COMMANDS` is the declared second profile of section 11: its second
  process waits for the answer on the same connection and exits with the answer's code (`AppCommandRunner`).
- **Named pipe IPC resilience** (rule 3, `LauncherIpc.cs`): Server listens on `CyrFlip_Launcher_{userSid}_{sessionId}`
  (current user only, no network, one instance per session - ticket S0008 LS-2) with per-connection error
  boundaries. Failed connections never crash the listener. The sender connects with a 1000 ms bound
  (`TrySend`, and `SendRequest` for a command). Since S0107 the pipe is created read-write so that a command can be
  answered; an older second process still opens it write-only and its token is still taken. CyrFlip registers
  **no URI scheme**: the activation path here is the pipe and the command line only, so rule 1 is not applicable
  (declared in the registry row).
- **Strict command parsing & normalization** (rule 4, `LauncherIpc.ParseCommand`): Accepts `/launcher-run:{guid}`,
  `/launcher-settings`, `/exit` and, since 0.12 (section 9), `/clipboard-send` and `/clipboard-get` - at most 256 characters, on the command line and on every pipe line. Normalizes
  GUIDs to canonical 36-char `D` format. Unknown commands are discarded and logged. A line that opens with `{` is a
  request of `APP-COMMANDS` rule 7 and is read by `AppCommandProtocol` (section 11 item C: the JSON Lines payload is
  a subset of that request).
- **Autonomous one-shot execution** (rule 6, `Program.RunWithMutex`): When invoked with `/launcher-run:{guid}` without a
  running instance, releases and closes the mutex and executes the standalone scenario without initializing
  hooks or tray icons.
- **Foreground window activation & hang protection** (rule 5, `ForegroundActivator.cs`): `BringWindowToTop` +
  `SetForegroundWindow`, with `AttachThreadInput` only after `IsHungAppWindow` and a 50 ms `WM_NULL`
  `SendMessageTimeout(SMTO_ABORTIFHUNG)` probe, detached in a `finally`.

## Declarations under the 0.11 amendment (section 7)

- **Transport (item A):** the named pipe only. CyrFlip does not use `WM_COPYDATA`.
- **Frozen names (item B):** `Local\CyrFlipSingleInstance` and the pipe-name shape `CyrFlip_Launcher_{userSid}_{sessionId}`
  are shipped anchors; rule 3's third shape is the declared one.
- **Focus and fallback (item C):** a plain second launch is **not** a silent no-op: it forwards `/launcher-settings`,
  and the live instance opens its Settings window, the window CyrFlip treats as its main one (rule 7C, ticket S0060
  P7-n; this pointer said "silent no-op" until 2026-11-15, corrected by ticket S0107). A token the live instance did
  not accept in time is not started as a second instance - a second hook would be worse than a lost Jump List click -
  and the person is told once, in words (`Program.TellAlreadyRunning`), except for `/exit`, which build scripts send
  unattended. Both are declared product choices.

## Synchronization read - 2026-10-18

The current clause-to-surface audit (ticket S0077) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The shared UI profile (ticket S0076) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.
