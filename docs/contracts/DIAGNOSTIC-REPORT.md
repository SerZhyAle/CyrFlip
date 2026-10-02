# Pointer - `DIAGNOSTIC-REPORT`

| | |
| --- | --- |
| **Id** | `DIAGNOSTIC-REPORT` |
| **Version** | 0.12, draft (CyrFlip's proposal of 2026-09-26 was folded into section 8 on 2026-10-02) |
| **Home** | `diagnostic-report/README.md` in the shared contracts catalog |
| **Role here** | Producer - the "send logs to the author" archive and the logs it collects |

## What this repository must keep true

- **Rule 1, section 8 A** - `CyrFlip-logs-<version>-yyyyMMdd-HHmmss.zip` (the version segment is optional in
  the contract and ours) holding `report.txt` (human) and `environment.txt` (`key=value`) plus the whitelisted
  logs (`SupportBundle`). No JSON envelope: there is no copy-to-clipboard diagnostic surface.
- **Rule 2, section 8 B** - `environment.txt` carries platform facts and counts only, led by
  `schema_version=1` (`SupportBundle.BuildEnvironment`, `EnvironmentSchemaVersion`). The metric set is CyrFlip's
  own (flips, conversion rows, scenarios..); `report.txt` may carry settings because none of them holds
  user-authored text.
- **Rule 3, section 7, section 8 C** - every text in the archive passes `DiagnosticRedactor`: `<APP_DATA>`,
  `<USER>`, URL userinfo (also when the password holds `/ ? #`), secret query parameters, credential-in-path,
  credential `name=value` pairs and JSON fields **by value, whatever key they sit under**, auth headers, Bearer
  tokens and PEM private keys become `[REDACTED]`. A line that runs out of time becomes
  `[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=<n>`. **Redaction precedes truncation**: a file is read
  whole, redacted, and only then cut to its tail; one above `SupportBundle.MaxRedactableBytes` is omitted whole
  with `[Diag] LOG OMITTED | reason=oversized_untrusted | source_bytes=<n>`
  (`DiagnosticRedactorTests`, `SupportBundleTests`).
- **Rule 4, section 8 D** - logs read with `FileShare.ReadWrite | FileShare.Delete`; rotation and truncation
  markers are the contract's `[Diag] LOG COMPACTED | ...` / `[Diag] LOG TRUNCATED | ...` (`DiagnosticLog`).
  The ceilings are CyrFlip's own maxima (512 KB per file, 3 MB in all, tail only - `kept_head_bytes=0`, since
  the settings travel in `report.txt`); the logs are per feature and append-only, so they rotate in place
  rather than by session.
- **Rule 5** - built only on a click, behind the consent dialog; the user's own mail client sends it.
- `clipboard-history.log` and `quick-notes.log` are never collected (also `CLIPBOARD-GUARD` rule 6).

Nothing in this repository differs from the contract any more: the four deviations CyrFlip carried as a dated
exception (the version in the name, no envelope, the ceilings, no session rotation) are now what the text
permits. The `schema_version` key was named by the same fold and is written.
