# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` |
| **Version** | 0.2, draft |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | Producer of the `screenshot` kind (ticket S0026's screen region capture); no other kind is produced or consumed |

## What this repository does to stay conformant

The saver is `src/CyrFlip/ScreenshotSaver.cs` (behind `IScreenshotFileSystem` for tests), pinned by
`ScreenshotSaverTests`: the `screenshot_yyMMdd_HHmmss` name with invariant digits at the freeze instant
(rule 3), the `.png` extension (4), the ` (n)` ordinal checked against the destination folder and a
rename that never replaces (5, 6), the folder chain user choice -> Screenshots -> Downloads and
nothing else (9, 10), the balloon that names a fallback in the moment (11), the flush-to-disk write
before the final name exists (12), the same PNG bytes the clipboard got (13), and the capture time
inside the file - the PNG `tIME` chunk carrying the name's own local second (16; the carrier is asked
in `PROPOSAL-2026-10-01-cyrflip-screenshot-time-chunk.md` in the catalog). Saver failures are named in
`screenshot.log` - failure kinds only, never a path.
