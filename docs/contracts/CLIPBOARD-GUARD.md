# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.10, draft (0.9.1 was never a valid version; section 7 of the contract, the owner amendment of 2026-09-26, binds where it and section 2 disagree) |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - Win32 clipboard access, backup and restore, the in-place pipeline, the privacy markers |

## What this repository must keep true

- **Rules 1-3, items A-C** - raw Win32 on the clipboard worker (`Win32Clipboard`), 12 x 15 ms open, the
  `GlobalSize` bound and the cap checked before marshalling; a transformed selection keeps its own line endings.
- **Rule 4, item D** - the backup (`ClipboardHandler.BackupClipboard`): three contents plus companions in one
  open, the sequence number after the reads; only the text can make it unreadable.
- **Rule 5, items E-F** - the pipeline (`ClipboardHandler.Run`), refusal before any key, line-copy markers
  (`LineCopyMarkers`).
- **Items G-H** - the delay-rendered paste and its wait (`ClipboardOwner`, `PasteWait`), `CF_LOCALE` beside
  every pasted text.
- **Rule 6, item I** - markers written by `TransientMarks`, honoured by `ClipboardPrivacy.ShouldSkip`; the log
  bundle's whitelist (`SupportBundle.ExcludedFiles`).
- **Items J-K** - the in-open restore decision (`ClipboardRestore.Plan`), own traffic excluded by sequence
  (`ClipboardHistoryGate`).

A change to any of these is a change to the contract first: CyrFlip owns it, so the owner amends it in the
catalog (a dated section, a version) and then the code follows.
