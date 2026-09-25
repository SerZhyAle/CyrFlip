# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.9.1, draft |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | Producer / Implementer - diagnostic log rotation, count-only environment reports, and support ZIP bundling |

## What this repository implements and guarantees

- **Append-only log rotation by tail** (`DiagnosticLog.cs:27-117`): 2 MB ceiling, keeps 512 KB tail per session, prefixed with a human-readable rotation marker line.
- **Support bundle generation** (`SupportBundle.cs:119-169`): Produces `<Product>-logs-<version>-<timestamp>.zip` containing `report.txt` and diagnostic logs.
- **Strict whitelist & user data exclusion** (`SupportBundle.cs:56-71`): Only approved logs (`launcher.log`, `context-menu.log`, `translate.log`, `quick-notes-diagnostics.log`, `clipboard-history-diagnostics.log`, `clipboard-flip.log`, `caret-diagnostics.txt`, `layout.txt`) are collected. `clipboard-history.log` and `quick-notes.log` are strictly excluded.
- **Count-only metadata summary** (`SupportBundle.cs:182-213`): `report.txt` contains OS, culture, runtime flags, and volume counters without user-authored text.
- **Concurrency resilience** (`SupportBundle.cs:249-287`): Reads logs with `FileShare.ReadWrite` so active logging is never interrupted.
