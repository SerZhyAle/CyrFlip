# Pointer - `LAYOUT-SIGNAL`

| | |
| --- | --- |
| **Id** | `LAYOUT-SIGNAL` |
| **Version** | 1.1, active (1.1 written 2026-09-26 by ticket S0016; the shipped app and extension still carry 1.0 until the next release) |
| **Home** | `layout-indicator/README.md` in the shared contracts catalog |
| **Role here** | producer of `layout.txt` and `layout-klid.txt`; consumer of the editor's caret claim |

## What this repository must do to stay conformant

- **Never append to `layout.txt`** (rule 2). Every installed copy of the VS Code extension reads a fixed
  prefix of that file, and those copies ship on their own clock. A new fact is a new file beside it - which
  is why the layout id went into `layout-klid.txt` rather than into a second line.
- **Keep writing both files in both install modes** (rule 1, 1.1): `%LOCALAPPDATA%\CyrFlip` unpackaged; packaged,
  the package's own per-user folder `%LOCALAPPDATA%\Packages\SZA.CyrFlip_fdk7e19xt9z9j\LocalCache\Local\CyrFlip`,
  addressed by its real path (`DataFolder`) - MSIX virtualizes the plain name away from an unpackaged reader.
- **Keep the deprecated mirror until the contract retires it** (rule 1, 1.1): the packaged build also writes
  both files to `%ProgramData%\CyrFlip`, best-effort, and retracts them with the primary files, so an
  extension older than 0.1.5 keeps working. Until 2027-03-31 and two extension releases; retiring it is a
  catalog step first. The extension reads that folder only when no per-user `layout.txt` exists.
- **Read the claim in both folders** (rule 9, 1.1): `EditorCaretSignal.FilePaths` - an older extension claims
  beside the mirror.
- **Keep publishing best-effort and off the detecting thread** (rules 5, 6): a failed write is swallowed,
  never surfaced, and never allowed to affect the app.
- **One writer, latest state wins** (rule 5): `LayoutPublisher.Channel` drains pending values on a single
  worker, so two publishes never race and leave an older value - or a code and a KLID from two different
  states - on disk. The producer writes only on change, so a stale value would not heal on the next poll.
- **Retract on a clean exit, primary instance only** (rule 6): `LayoutPublisher.Retract` deletes both files
  from the context's disposal and exit handlers, so their absence means "not running". Never from the
  `/launcher-run` forwarding process or the one-shot launch, and never `editor-caret.txt`, which is the
  extension's. A killed process leaves its last value behind.
- **The code can be three letters** (rule 3): Windows names a few languages with three (`HAW`); a language
  it cannot name comes back as four hex digits. All fit the consumer's four-character prefix.
- **Honour the caret claim only on both halves** (rule 11): the file younger than 1500 ms *and* the
  foreground process in the editor family. Never parse the claim file's contents (rule 9).
- **Nothing but the layout goes in that folder's published files** (rule 8). The packaged mirror is readable
  by every account on the machine.

`LayoutPublisherTests` is the channel half's evidence: both file names, their exact bytes, the folder per
install mode, the empty sidecar, the last publish winning and the retract leaving every other file alone.
`EditorCaretSignalTests` pins the 1500 ms window, and `ExtensionContractConstantsTests` reads the
extension's own timings (500 ms, 5 s, poll 200 ms) and its 4- and 8-character prefixes out of its source.
