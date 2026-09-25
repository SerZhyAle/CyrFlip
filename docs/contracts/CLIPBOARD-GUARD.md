# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.9.1, draft |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - direct Win32 clipboard access, atomic multi-format backup/restore, in-place transform sequence verification, and DPAPI history exclusion |

## What this repository implements and guarantees

- **Pump-free raw Win32 access** (`Win32Clipboard.cs:17-98`): Bypasses COM/OLE on background threads to prevent UI deadlocks with Chromium/Electron windows.
- **Bounded retry loop on open** (`Win32Clipboard.cs:199-207`): 12 attempts with 15 ms delay on `OpenClipboard` to tolerate transient locks.
- **Multi-format atomic backup and single-pass restore** (`ClipboardHandler.cs:217-260`, `Win32Clipboard.cs:172-187`): Preserves `CF_UNICODETEXT`, `CF_DIB` (capped at 64 MB), and `CF_HDROP`. Restores all formats in a single `OpenClipboard` -> `EmptyClipboard` -> `SetClipboardData` pass.
- **Sequence tracking and modifier state preservation** (`ClipboardHandler.cs:45-59, 267-295`): Synthesizes `Ctrl+C`, polls `GetClipboardSequenceNumber` with 480 ms timeout, verifies foreground window, and preserves physical modifier key states across transformations.
- **DPAPI encryption & diagnostic archive exclusion** (`SupportBundle.cs:57-71`, `SupportBundleTests.cs:44-61`): Encrypts local clipboard history with DPAPI and strictly excludes history and notes from support ZIP archives via an explicit whitelist.
