# Pointer - `CHECK-VERDICT`

| | |
| --- | --- |
| **Id** | `CHECK-VERDICT` |
| **Version** | 0.9, draft |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 2 |
| **Role here** | producer of every gate and `tools/uitest` verdict; consumer of child exit codes (`release.ps1`) - **not yet conformant** |

## What this repository must do to stay conformant

- **Exit 0 pass, 1 defect, 2 could-not-verify, 3 advisory** - and never report a could-not-verify as 0.
- **End with one machine-readable verdict line**, and print a reason for any non-zero exit.
- **Do not stop at the first failure** where later checks can still run.

Today every script exits 0 or 1 only, a few could-not-verify paths exit 0, and no verdict line follows the
grammar. Plan in `PLAN/` (local, not published).