# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.9.1, draft |
| **Home** | `app-activation/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - single-instance mutex, Jump List named pipe IPC, and foreground window activation |

## What this repository implements and guarantees

- **Per-session single-instance mutex** (`Program.cs:19`): Holds `Local\CyrFlipSingleInstance`. Secondary invocations route parameters via named pipe and exit cleanly with 0.
- **Named pipe IPC resilience** (`LauncherIpc.cs:21-122`): Server listens on `CyrFlip_Launcher_Pipe` with per-connection error boundaries. Failed connections never crash the listener.
- **Strict command parsing & normalization** (`LauncherIpc.cs:44-60`): Accepts `/launcher-run:{guid}`, `/launcher-settings`, `/exit`. Normalizes GUIDs to canonical 36-char `D` format. Unknown commands are discarded.
- **Autonomous one-shot execution** (`Program.cs:34-42`): When invoked with `/launcher-run:{guid}` without a running background instance, releases mutex and executes standalone scenario without initializing hooks or tray icons.
- **Foreground window activation & hang protection** (`ForegroundActivator.cs:21-61`): Uses `AttachThreadInput` with a 50 ms responsiveness check (`SMTO_ABORTIFHUNG`) to avoid deadlocks when activating across process boundaries.
