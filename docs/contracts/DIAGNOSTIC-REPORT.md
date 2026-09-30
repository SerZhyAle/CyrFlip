# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.9, draft (the 0.9.1 this pointer once named was never a contract version; corrected 2026-09-26) |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | Producer - the "send logs to the author" archive and the logs it collects |

## What this repository must keep true

- **Rule 1** - `CyrFlip-logs-<version>-yyyyMMdd-HHmmss.zip` holding `report.txt` (human) and `environment.txt`
  (`key=value`) plus the whitelisted logs (`SupportBundle`).
- **Rule 2** - `environment.txt` carries platform facts and counts only (`SupportBundle.BuildEnvironment`).
- **Rule 3** - every text in the archive passes `DiagnosticRedactor`: `<APP_DATA>`, `<USER>`, URL userinfo.
- **Rule 4** - logs read with `FileShare.ReadWrite | FileShare.Delete`; rotation and truncation markers are
  the contract's `[Diag] LOG COMPACTED | ...` / `[Diag] LOG TRUNCATED | ...` (`DiagnosticLog`).
- **Rule 5** - built only on a click, behind the consent dialog; the user's own mail client sends it.
- `clipboard-history.log` and `quick-notes.log` are never collected (also `CLIPBOARD-GUARD` rule 6).

Where CyrFlip still differs - the version in the name, no JSON envelope, tail-only ceilings below the
contract's, no per-session rotation - is a dated exception in the catalog's registry and a proposal to the
owner (StreamsPlayer), not a local decision.
