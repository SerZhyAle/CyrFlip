---
paths:
  - "src/CyrFlip/QuickNote*.cs"
  - "src/CyrFlip/CyrFlipExchange*.cs"
  - "src/CyrFlip/CyrFlipContext.NotesSync.cs"
  - "src/CyrFlip/SettingsForm.NotesSync.cs"
  - "src/CyrFlip/ExchangeNotes*.cs"
---

# Quick notes and the open exchange file

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Status

- The **quick notes** (`QuickNote*`, feature 8 below) - the local RAW notepad - are implemented, unit-tested and built in all 13 languages; the chord, the window and the context-menu capture still want a human at the keyboard (`tools/uitest/Test-QuickNotes.ps1`).

## Features

8. **Быстрые заметки ("Быстрые заметки"), opt-in and off by default.** `EnableQuickNotes=0` means the
   journal file is **never read and never created**, there is no tray entry and no chord is bound - a
   place people put tokens and internal code does not get to exist because a checkbox was left at its
   default. Enabled, **Ctrl+Shift+Alt+N** (`AppConfig.QuickNotesHotkey`) opens `QuickNotesWindow` with a new
   note and the caret already in the body; the tray entry and the text context menu's **"Сохранить
   выделение в быстрые заметки"** are the other two ways in, the latter going through the translator's
   phase-A capture (synthesized Ctrl+C, clipboard handed straight back, history suppressed - the copy is
   scaffolding, not a copy the user made). Four decisions carry the feature:
   - **The text is never touched.** No Markdown, no highlighting, no smart quotes, no tab expansion; the
     store round-trips CRLF, LF, tabs and non-BMP text byte for byte (`QuickNotesStoreTests`). The one
     honest limit is the Win32 edit control: an **untouched** note is never rewritten (the editor's text
     is compared against what it was handed), but an **edited** one comes back with the control's CRLF.
   - **A name is optional and a blank one is `null`, not «Без имени».** The list caption is derived on
     the way to the screen (`QuickNote.Preview`) and never written back, which is what makes editing the
     first line of an unnamed note harmless.
   - **Order is creation time, newest first, and nothing else** (`QuickNotesOrder`) - editing an old note
     must not move it. `UpdatedAtUtc` is metadata. Ties break on `Id` descending so the order is total;
     the incremental `Insert` is proven equal to the reference sort over a thousand random operations,
     the same discipline as `ClipboardHistoryOrder`.
   - **Search is the index, not a convenience**: most notes have no name, so it looks in the title, the
     raw text and every checklist item at once, from the first character, keeping the standard order
     rather than inventing a relevance score.
   Storage is `quick-notes.log` in the MSIX-aware folder: an append-only journal of create/update/delete,
   **the whole record DPAPI-encrypted, not just the payload** (a title names a customer as often as a body
   holds a token), replayed **on the pool** at first use (the window opens in a "loading" state - the UI
   thread is the hook's thread), compacted **in the background** once the journal holds
   `QuickNotesStore.CompactAfterOperations` (500) records more than a fresh snapshot would - not a flat 500,
   which rewrote every note on every save for anyone with 500 notes - and on exit, via `File.Replace` -
   atomic on success, and the previous journal becomes the `.bak` in the same call; a failed replacement
   keeps its complete `.tmp` if recovery also fails. An append restores a missing journal before writing
   a new record, and a failed or partial replay disables writes until restart. A compaction that failed waits for
   another 500 records instead of re-encrypting everything on the next save (S0035 QN2-8). A line that will
   not read is skipped, counted, reported once **and carried through every compaction verbatim** when it is
   a whole ciphertext (a DPAPI key can come back) or opens but is no record (a later format); a **fragment**
   a kill left mid-append - told apart by walking the DPAPI blob's length-prefixed fields
   (`QuickNotesCipher.IsWholeBlob`, a cipher's optional `IQuickNotesCipherShape`) - is dropped by the
   compaction the load itself asks for, so it is warned about once, not at every start (QN2-2). A missing
   journal is restored from its complete `.tmp` or `.bak`; read or recovery failures have their own warning. Every
   record is written with a **leading** line break, so a torn tail costs only itself. A note is rewritten
   only when it was edited, and a note deleted this session is never committed again (ticket S0006). **A
   write that fails stays pending** - the note (or delete) goes back to the debounce, retried after 5 s or
   on the next save, and the window shows the failure until a write succeeds (`SaveFailing`/`SaveRecovered`,
   QN2-1); it used to be cleared as saved and overwritten on screen at once. "Delete every note" also
   removes the crash-left `.tmp` and the migration's kept journals (QN2-3). The
   count goes to `quick-notes-diagnostics.log` and the note text goes nowhere. `SupportBundle.ExcludedFiles` holds the journal too (four names in all, with `quick-notes-sync.json`): it is as
   off-limits as `clipboard-history.log`. Google Keep is a **manual clipboard transfer** and says so - the
   Keep REST API is a Workspace-administrator interface, unusable from a personal account, so CyrFlip does
   not pretend to sync. Spec: [PLAN/done/QuickNotes_Spec_Idea_v0.1.md](../../PLAN/done/QuickNotes_Spec_Idea_v0.1.md).
   **One deviation from the spec's §7 file list:** there is no `QuickNotesCapture.cs` - the capture is
   `CyrFlipContext.CaptureSelectionIntoQuickNote`, beside the translator's identical phase A, because it
   needs the context's `_busy` guard, `_clipboard` and history suppression and a separate class would only
   plumb all three back out.
   **Line numbers (S0078, `QuickNotesLineNumbers`, off by default, applied live):** a strip beside the editor,
   numbered by **logical** line (CRLF, LF and lone CR are one break each - `QuickNote.LineCount`'s rule). A
   wrapped line is numbered once, at its first row; when its start has scrolled out of view, its first
   visible row repeats the number once with a "↳". The numbers are never in the text, the clipboard, an
   export or the journal, and showing, scrolling or toggling them writes nothing. Spec:
   [PLAN/done/S0078_quick-notes-line-numbers.md](../../PLAN/done/S0078_quick-notes-line-numbers.md).
   **Sync with the account's other devices (S0105, `DEVICE-EXCHANGE` 1.13 items FG-FM, `QuickNotesSync`, off by
   default):** the quick notes travel through the exchange server (feature 16) as one sealed record per note
   (`ExchangeNotes` / `ExchangeNotePayload` - the item FH payload, 131072 bytes, LF, dates in ms; `ExchangeClient.NotesListAsync`
   / `NotesPutAsync` on the control stream; boxes sealed with context `note:<noteId>` to every device of the entry holding the
   role `notes` and a trusted key, **this one included** so a lost state file can adopt its own records back). The
   engine (`ExchangeNotesSync`) pulls with `more` / `full` (a `full` answer is re-read from `since: 0`), pushes each
   changed note with the revision it last saw, and settles every item FJ branch without a field merge: an older
   copy never resurrects a delete, two edits keep both (the local one survives as a new note titled with this PC's
   name), and the sync **never writes over a note with an unsaved edit** (`IExchangeNotesLocal.ApplyRemote` returns
   false, the note stays marked). The per-note state (`ExchangeNotesSyncState`: entry id, `notesRevision`, per note
   id / revision / changed) lives in **`quick-notes-sync.json`** - ids and numbers only, no title, no text - owned by
   `ExchangeNotesSyncLedger`, which queues marks made during a pass and saves on the pool. `QuickNotesService` raises
   `LocalChanged` / `LocalDeleted` for the user's own changes only; the sync's own writes (`ApplyRemote`, `AddLocal`,
   `DeleteRemote`) raise nothing and never rewrite `CreatedAtUtc`. Triggers (`CyrFlipContext.NotesSync`, item FM read
   without a resident stream): start and enabling, 3 s after the last local save or delete, the notes window
   shown, and "Sync now" - one pass at a time plus one queued; no timer. Live only when the quick notes, the
   exchange module and the option are on and one `member` entry is picked (`NotesSyncGate`); a guest, a server
   without `sealed` (`server-no-e2e`) or without `notes` (`server-no-notes`), or an entry enrolled before the role
   (`notes-role-missing`, sign in again) gets no notes frame. `enroll` declares `clipboard` and `notes` from this
   build on. "Delete every note" deletes each note on the account too. A note above the payload limit stays local
   and its window says so. `QuickNotesSync` / `QuickNotesSyncEntry` are consent on this device and never in the
   settings file. Spec: [PLAN/done/CYRFLP-0105_quick-notes-exchange-sync-fms.md](../../PLAN/done/CYRFLP-0105_quick-notes-exchange-sync-fms.md).

10. **The open exchange file - "Перенос на другой компьютер" (ticket S0023).** One plain UTF-8 text file,
   "CyrFlip Exchange Text v1", carrying quick notes and a chosen slice of the clipboard history to another
   PC or a phone: `Экспортировать...` / `Импортировать...` in the quick-notes window ("Перенос..."), the
   history search window and a row on both the clipboard and the quick-notes settings pages. It is **open
   on purpose** - no DPAPI, no base64, no password; the export dialog says so beside the button that
   writes it (every time history is included; `ExchangeNotesWarningOff` may silence it for notes-only), and
   the defaults take notes + pinned history only, never the whole history. It is a **snapshot, not a
   storage format**: the journals stay DPAPI and may change on their own, v1 is frozen by the files people
   already hold. Four rules carry it: **payloads are verbatim** inside a backtick fence longer than any run
   in the text (framing is LF, a payload's own CRLF survives; a file converted to CRLF as a whole is
   detected by its marker line); **a clipboard block's `Id` is `sha256:` of its raw UTF-8 text** - the
   history's own id - and a text that no longer hashes to it is refused; **the import is additive** -
   preview first (`PlanImport` on both services, re-planned at apply time), a note replaced only by a
   strictly newer `Updated-At` (a tie keeps the local one, `CreatedAtUtc` is never rewritten), a pin never
   removed, nothing deleted, a hand-written `## Note` without an id imported only after a yes; **a bad block
   costs itself** (reported with its line), except an unclosed fence, which swallows the rest. Caps: 25 MB
   file, 128 KB per history text, 512 KB per note, 4 KB per metadata line, 10 000 objects. Dates are written
   to the second and truncated, so a re-import never "updates" a note with itself. Notes are skipped while
   the quick notes are off, history entries while the history is off. CyrFlip never sends, opens or
   watches the file. Spec: [PLAN/done/S0023_history-notes-exchange-and-features.md](../../PLAN/done/S0023_history-notes-exchange-and-features.md).

## Modules (`src/CyrFlip/`)

- **QuickNote.cs / QuickNotesOrder.cs / QuickNotesStore.cs / QuickNotesService.cs / QuickNotesWindow.cs** -
  the quick notes, one concern each (feature 8 above). `QuickNote` is the model plus every rule that is a
  fact about text - the derived preview, the search, the two conversions and what each of them loses, the
  export shapes and the 512 KB cap. `QuickNotesOrder` is the order and the filter, with the binary
  `Insert` that keeps a save from costing O(n log n). `QuickNotesStore` is the DPAPI journal, replay,
  compaction and the `.bak`; its cipher is an **interface** (`IQuickNotesCipher`) for one reason - DPAPI is
  a per-user machine secret, so a test that used it would pass or fail for reasons unrelated to this code
  and would be unrunnable under a CI account. `QuickNotesService` owns the in-memory list, the draft that
  is nowhere until it has content, the 750 ms debounce and the flush-then-compact on exit.
  `QuickNotesWindow` is the only part that knows WinForms; `QuickNotesLog` is its one-line
  `DiagnosticLog` wrapper, and it records counts, never a note.
- **QuickNotesTextEditor.cs** (S0078) - the editor the window hosts: a native `TextBox` (`QuickNotesTextBox`,
  which tells its host after it handled a scroll, key, wheel, size, font or paint) beside the drawn
  `QuickNotesLineGutter`. Rules that are not obvious from the code: the numbers are **read from the edit
  control, never computed** - a row's Y is `EM_POSFROMCHAR` in the edit's client coordinates moved into the
  gutter's **through the screen** (the edit has a 3-D border: the same number is not the same pixel), the
  visible rows are those above `EM_GETRECT`'s bottom (the edit trims it to whole rows; a cut-off row has no
  text and so no number), and a row's height is the edit's (`tmHeight + tmExternalLeading`, measured from two
  rows or `GetTextMetrics`), **not** `Font.Height` (16 for Consolas 10 pt where the edit lays out 15). In an
  EDIT "line" means visual row under WordWrap, so rows map to logical lines through `QuickNotesLineIndex`
  (starts of lines, binary search), rebuilt **only when the text changes and only while the numbers are
  shown** - scrolling and resizing never scan the text (`IndexBuilds` is the test seam). Nothing polls: a
  cheap signature of the view (first row, row count, formatting rectangle) is compared after the messages
  above and after `WM_PAINT`, and the strip repaints **synchronously** so it never trails the text by a
  frame. An EDIT breaks a line only on CRLF (a bare LF or CR is a box glyph), which is why the window's
  `ForDisplay` always hands it CRLF. The strip is physically on the left in an RTL window (the editor passes
  no layout to its children); in high contrast the numbers use `text.primary`. `WordWrap` recreates the
  handle, so the edit's undo history does not survive that one switch (it never did); the numbers toggle does
  not recreate anything. `QuickNotesLineNumbersGeometryTests` compares pixels (ink of a number against ink of
  the row's first characters) against an oracle that counts breaks by hand.
- **CyrFlipExchangeModels.cs / CyrFlipExchangeWriter.cs / CyrFlipExchangeReader.cs / CyrFlipExchangeMerge.cs /
  ExchangeExportDialog.cs / ExchangeFlow.cs** - the open exchange file (feature 10). The first four know no
  WinForms and are what `CyrFlipExchangeTests` holds to the spec: the DTOs and caps, the writer (fence
  choice, one-line metadata, second-truncated dates, `SelectHistory`, the temp-file-then-replace write), the
  reader (splits on **LF only** - `TextReader.ReadLine` would eat a payload's CR - with the CRLF-file
  detection and the hash-decided line endings) and the merge planner that both the preview and the import
  run. `QuickNotesService.Import` changes an existing note **in place** and raises `Imported`, so the notes
  window reloads the one it holds open. A failed note batch returns `NotesUnwritten`; the dialog reports
  pending persistence instead of import completion. Quick-note copy feedback also follows the clipboard result.
  `ExchangeFlow` is only the dialogs around them, run through
  `CyrFlipContext.RunExchange`. `SettingsForm` takes the `exchange` callback as its last (nullable,
  required) parameter; every test builds the window through the one compiled `TestForms.NewSettings`, so
  a constructor change breaks the build, never the run (S0029 RB-1 - reflection binds no defaults).
