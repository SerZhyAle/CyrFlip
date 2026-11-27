---
paths:
  - "src/CyrFlip/SupportBundle*.cs"
  - "src/CyrFlip/MailSender.cs"
  - "src/CyrFlip/Diagnostic*.cs"
  - "src/CyrFlip/CaretDiagnostics.cs"
---

# Support bundle, mail hand-off and diagnostics

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Modules (`src/CyrFlip/`)

- **SupportBundle.cs / MailSender.cs / SupportBundleDialog.cs** - the About tab's **"Отправить логи автору.."**:
  pack the diagnostic logs into one ZIP and hand it to the user's own mail client. Three things here are
  decisions, not implementation details. **`clipboard-history.log` is never collected** - the file list is an
  explicit whitelist (`SupportBundle.LogFiles`), never a directory glob, because a glob is exactly how that
  file gets in one day; it has its own test, and so does the fact that its payload appears nowhere in the
  archive. **What the whitelisted files may hold is a rule too** (ticket S0010 TD-1): `launcher.log` names a
  launch by the scenario's id, name and kind - never `FileName` or `Arguments`, where tokens live - and on
  collection the file is held to a **whitelist of today's record shapes** (`ScrubLauncherLog`, S0034
  LS2-3): a record of a known safe shape goes as written, a known legacy one is cut after its diagnostic
  head, any other keeps only the words before a quote/slash/`%`, and a line with no time stamp (the
  continuation of a record whose name held a line break) is dropped - a list of legacy shapes had missed
  the `Started yt-dlp .. in '<folder>'` line every release up to v26.9.16.2132 wrote. The report counts
  scenarios through a **read-only** store that never re-identifies or renumbers a file (LS2-9);
  `caret-diagnostics.txt` records window classes and processes, a title or element name only as its length;
  and the dialog shows, per file, what kind of data it holds (`SupportBundle.Contents`, 13 languages).
  **`mailto:` cannot carry an attachment** (RFC 2368 has no such field and `attach=` is deliberately
  ignored), so `MailSender.Send` is a three-rung ladder - Simple MAPI `MAPISendMail(MAPI_DIALOG)` with the
  file attached, else a `mailto:` message plus `explorer /select` on the archive, else the path and the
  address for the user to do it by hand - and `MAPI_USER_ABORT` **stops** the ladder, since a user who closed
  the compose window has finished, not failed. MAPI is ANSI, so a non-ASCII path (a Cyrillic Windows account
  name) goes through `GetShortPathName` or drops a rung rather than arriving as mojibake; a 32-bit Outlook
  answers our 64-bit process with `MAPI_E_FAILURE`, which is unfixable and is why rung 2 exists. The ladder
  runs on a dedicated **STA** thread (`SendOnStaAsync`, S0010 TD-8) - classic Outlook's compose window
  expects one, and a pool thread (MTA) could fail silently or hang. The compose window gets **no owner**
  (an owner on the hooks' UI thread attached the two threads' input queues while it was open), and
  `SendWithAttachment` carries `[HandleProcessCorruptedStateExceptions]` so a Simple MAPI provider that
  faults drops a rung instead of taking the tray process down (S0036 UI-8). **The dialog
  is the consent step** - file list, sizes, path, an "open the folder" button and the plain statement about
  file paths (the usual ones no longer carry the Windows account name, one in an unusual shape still can) - so
  there are no checkboxes and, deliberately, **no
  new registry values**: the feature has no state. Since S0040 (`DIAGNOSTIC-REPORT` rules 1-4) every text in
  the archive passes **`DiagnosticRedactor`** (the data folders become `<APP_DATA>`, the profile and any
  `X:\Users\<name>` `<USER>`, a URL loses its userinfo), `environment.txt` (`key=value`, counts and platform
  facts only, led by `schema_version=1`) sits beside `report.txt`, the name carries seconds, and the markers are the contract's
  `[Diag] LOG COMPACTED | ...` / `[Diag] LOG TRUNCATED | ...`. Since 0.12 (S0040 A7-A9) a **secret is redacted by
  its value's shape, whatever it sits under** - a secret query parameter, a credential `name=value` pair or JSON
  field, an auth header, a Bearer token, a PEM private key block, the account segments of `/live/user/pass/id`, a
  URL password holding `/ ? #` - each becomes `[REDACTED]`, name kept; the pass runs line by line under a
  250 ms budget (a line that overruns is replaced by `[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=<n>`);
  and **redaction precedes truncation** (`SupportBundle.Collect`: read whole, scrub, redact, only then
  `KeepTail`), a file above 16 MB being omitted whole with `[Diag] LOG OMITTED | reason=oversized_untrusted |
  source_bytes=<n>`. Long logs are kept by their **tail** with a marker line
  (512 KB per file, 3 MB of collected bytes; a whole log left out at that budget stands in the archive as
  `[Diag] LOG OMITTED | reason=archive_cap | source_bytes=<n>` - DIAGNOSTIC-REPORT 0.15, S0077 - and is still
  named in `report.txt`), archives live in `reports\` beside `layout.txt` (the MSIX-aware
  folder, because a mail client is a foreign process) and the five newest are kept. Subject and body are
  **English whatever the UI language is** - the artefact is addressed to the author - and the address is
  `sza@ukr.net`, the one already published in the site footer and the privacy policy. Nothing is ever sent
  automatically, not even after a crash. Spec: [PLAN/done/SendLogsToAuthor_Spec_Idea_v0.1.md](../../PLAN/done/SendLogsToAuthor_Spec_Idea_v0.1.md).
