---
paths:
  - "src/CyrFlip/ClipboardHistory*.cs"
---

# Clipboard history

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Modules (`src/CyrFlip/`)

- **ClipboardHistoryService.cs (+ `ClipboardHistoryWindow` / `ClipboardHistorySearchWindow`)** - the opt-in
  clipboard manager: a `NativeWindow` listening on `AddClipboardFormatListener`, an append-only log
  (`%LOCALAPPDATA%\CyrFlip\clipboard-history.log`, **every record DPAPI-protected whole** - text, id, source app, window title - ticket S0031) and the
  in-memory list the strip and the search window paint from.
  **Accepted design decision (2026-07-26, after a leak audit): the history is deliberately unbounded.**
  There is no entry cap, no age-based retention and no log compaction - the whole point is that nothing
  the user copied is ever silently dropped, and the memory/disk cost of that is the user's to spend.
  Do **not** "fix" this by adding a limit; it is a decision, not an oversight. What the decision *does*
  oblige is that nothing may cost O(history) per copy or per repaint - and that obligation is now
  discharged by **`ClipboardHistoryOrder`**, a separate class holding the list, the `_byUuid` index and
  the "what the clipboard holds now" flag: the display order is **maintained incrementally** (a binary
  search plus one list move, no comparers and no allocation) instead of the whole history being
  re-sorted after every copy, and `SetCurrent` touches only the two entries whose flag changes instead
  of walking every one. Each change still raises `Changed` exactly once. Splitting it out is also what
  makes the order unit-testable without a `NativeWindow`, the registry or DPAPI: `ClipboardHistoryOrderTests`
  replays a thousand random operations and compares against that very reference sort after each one.
  The window persists its position **once per drag** (`MouseUp`), not on every `Move` - each save is a
  full `AppConfig.Save()`, i.e. forty-odd registry values plus both profile tables serialized.
  Four rules from ticket S0005, each with its own class. **`ClipboardHistoryJournal`** is the file:
  **one** dedicated writer thread owns the `FileStream` and drains one queue, so records land in the
  order they were made (a pool item per record once wrote a `delete` before its `add`), `Clear` is a
  queued command in order with the appends around it (a failed delete raises `ClearFailed`, never a
  silent "empty until restart"), and `Dispose` drains it; records are **snapshots** taken on the UI
  thread and written as `"\n" + json`, and the replay is **per line** - a torn or undecryptable line
  is skipped and counted (`SkippedRecords`, one balloon), never the whole history, and never removed
  from disk. **`ClipboardHistoryGate`** decides what CyrFlip's own traffic is by the **clipboard
  sequence number**, not the clock: every clipboard worker brackets itself with
  `SuppressBegin`/`SuppressEnd` (the end mark is the sequence after the restore), and the history's own
  write from the strip is skipped by its **exact** number - the 2 s window it replaced dropped real
  copies after a flip, and the boolean dropped the next copy after a paused restore.
  **`ClipboardPrivacy`**: a copy carrying `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard
  Viewer Ignore`, or `CanIncludeInClipboardHistory`/`CanUploadToCloudClipboard` = 0 (password managers)
  is **not recorded**, and its text is never even read (`Win32Clipboard.TryReadForHistory` checks the
  markers inside the same open) - the one thing the unbounded history does not keep, at the copying
  app's explicit request. **`ClipboardHistoryEntry.Preview`** (first 1024 chars, one line, cached) is all
  the strip and the search list ever draw; the search window debounces 150 ms, matches the full text
  on a background task cancelled per keystroke, and is a `VirtualMode` `ListView`.
  `clipboard-history-diagnostics.log` (`ClipboardHistoryLog`) holds only counts and is in the support
  bundle's whitelist.
  Ticket S0031 added four rules. **Nothing readable on disk**: a record is `{"v":2,"blob":...}`, the blob
  the cipher of the whole record - the id is the SHA-256 of the text, which reverses a short code in
  milliseconds, so it never stands beside the blob (and needs no keyed hash inside it); v1 lines (payload
  encrypted, the rest in clear) are still read, never rewritten. **"×" erases**: a delete rewrites the file
  without any line of that entry (unreadable lines carried verbatim, no `.bak`), and only a failed rewrite
  falls back to a tombstone - a user-requested removal is not the silent dropping the unbounded decision
  forbids. **The owning thread by construction**: every result from the pool or the writer (capture,
  "too large", "clear failed", "write failed") is posted to the service's own window (`PostToOwner`, a
  private `WM_APP` message + a queue), never through a `SynchronizationContext` read in the constructor.
  **Losses are counted**: a failed write (`WriteFailures`, one balloon for background writes and a report for every failed erase), an import reports
  only after `FlushJournal`, and `Dispose` logs the records it abandoned. A privacy DWORD marker that is
  present but unreadable counts as 0 (`ClipboardPrivacy.MarkerValue`) - the flip backup's rule.
  **An entry's text is editable in the strip** (ticket S0089): a right click on a cell's text, or F2 on the
  keyboard cursor, opens one lazily created multiline `TextBox` over the cell with the **whole** text (the cell
  paints only the preview); Enter or any click on the strip outside it saves - that click is consumed in
  `WndProc` and does nothing else - Shift+Enter breaks the line, Escape discards, focus loss and hiding save,
  and while it is open every other key is the editor's. The strip's caption is now "Clipboard" (the window);
  every other surface still says "Clipboard history" (the feature). **`ClipboardHistoryService.UpdateText` keeps
  the id rule**: an id is the hash of its text (the exchange file refuses one that is not), so an edit is the
  old entry **erased** exactly as "×" erases it plus an `add` under the new text carrying the date, pin and
  source - no new journal action, so an older build reads the file as it is. A text another entry holds
  merges into it (its date, pinned when either was); empty or whitespace, unchanged, or over `MaxTextChars`
  (the capture cap; `ItemTooLarge` says so) changes nothing - an edit never deletes. An id erased before the
  replay merged is not brought back by it (`_erasedBeforeReplay`).
