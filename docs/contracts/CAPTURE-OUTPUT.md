# Pointer - `CAPTURE-OUTPUT`

| | |
| --- | --- |
| **Id** | `CAPTURE-OUTPUT` |
| **Version** | 0.5, draft |
| **Home** | `capture-output/README.md` in the shared contracts catalog |
| **Role here** | Producer of the `screenshot` kind (ticket S0026's screen region capture, and ticket S0071's full and annotated captures; CyrFlip composes annotations before saving; the contract sees the final pixels); no other kind is produced or consumed |

## What this repository does to stay conformant

The saver is `src/CyrFlip/ScreenshotSaver.cs` (behind `IScreenshotFileSystem` for tests), pinned by
`ScreenshotSaverTests`: the `screenshot_yyMMdd_HHmmss` name with invariant digits at the freeze instant
(rule 3), the `.png` extension (4), the ` (n)` ordinal checked against the destination folder and a
rename that never replaces (5, 6), the folder chain user choice -> Screenshots -> Downloads and
nothing else (9, 10), the balloon that names a fallback in the moment (11), the flush-to-disk write
before the final name exists (12), and the format per kind - PNG for a screenshot (13).

**Rule 16 (0.5) is met.** The saved file is the clipboard's PNG with one `tIME` chunk inserted right after
`IHDR` (`src/CyrFlip/PngTime.cs`): 7 bytes of data holding the **local wall-clock second the name was formed
from**, not UTC, with the PNG CRC-32. `ScreenRegionCaptureTests` pins the chunk's place, its bytes against the
name's own second, its CRC, and that anything that is not a PNG with an `IHDR` comes back unchanged.
Decoration never fails a capture. The clipboard still gets the plain PNG bytes; the saved file differs from
them by exactly that chunk.

CyrFlip produces no text kind (`text`, `ocr_text`, `translation`) and no other format, so there is no
format without a time carrier and **no exception is owed** under rule 16's last sentence. The support bundle
ZIP and `screenshot.log` are a report about the tool's own run (section 5, 0.3); the exchange file and the
quick-notes export are exports whose format CyrFlip owns itself - all outside this contract. Saver failures
are named in `screenshot.log` - failure kinds only, never a path.
