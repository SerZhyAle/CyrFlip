# Pointer - `CHECK-VERDICT`

| | |
| --- | --- |
| **Id** | `CHECK-VERDICT` |
| **Version** | 0.12, draft (read 2026-10-07; 0.11 added the word `COULD NOT VERIFY` and rules 11-15, 0.12 the word `NOT APPLICABLE` and section 10) |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 2 and section 10 |
| **Role here** | producer of every gate and `tools/uitest` verdict; consumer of child exit codes (`release.ps1`) - **partial: exit codes follow the contract, the verdict word and three rules do not yet (see below)** |

## What this repository must do to stay conformant

- **Exit 0 pass, 1 defect, 2 could-not-verify, 3 advisory** - and never report a could-not-verify as 0.
- **End with one machine-readable verdict line** (rule 5), last on stdout, with a word out of the closed set
  `PASS`, `PASS WITH ADVISORIES`, `FAIL`, `COULD NOT VERIFY`, `NOT APPLICABLE`; print a reason for any non-zero exit.
- **Do not stop at the first failure** where later checks can still run.
- **Owe the line on the failure path too** (rule 11); **read the skips** of a wrapped test runner and print the
  count (rule 13); **quote the children and rank them FAIL, COULD NOT VERIFY, advisory, PASS** in an aggregator
  (rule 14).

Implemented via `tools/checks/CheckVerdict.psm1`, exercised by `tools/checks/Test-CheckVerdict.ps1`; the uitest
scripts and the site gates call `Complete-Check`.

## Where this repository is behind (2026-10-07, not yet fixed)

- **Rule 5, the word for `2`:** the module, `release.ps1` and the positioning gate print `NOT VERIFIED`, where
  the contract's word since 0.11 is `COULD NOT VERIFY` (`tools/checks/CheckVerdict.psm1`, `release.ps1`,
  `tools/site/Test-Positioning.ps1`). The exit codes are right; a reader matching the word would read these lines
  as an unknown word, i.e. not passed.
- **Rule 11:** the failure path of `build.ps1`, `release.ps1` (a gate that `throw`s) and `tools/Assert-TestRun.ps1`
  is a PowerShell `throw`, so those runs end with an error record and no verdict line.
- **Rule 13:** `tools/Assert-TestRun.ps1` reads the TRX executed and failed counters and refuses an empty run,
  but prints no skip count.
- **0.12 item A:** nothing prints `NOT APPLICABLE`; `release.ps1 -Offline` leaves the origin check unrun by the
  caller's choice and the final line says nothing about it.
- The dated exception for these is in the catalog registry, not here.
