# Pointer - `CLIPBOARD-GUARD`

| | |
| --- | --- |
| **Id** | `CLIPBOARD-GUARD` |
| **Version** | 0.12, draft (re-read 2026-11-19: item S, the read order, is in the source since ticket S0115 - read in `ExchangeClipboardIo.cs`, not built or tested - and its dated exception in the registry closes at the release that carries it; earlier re-read 2026-11-15, S0114; first read 2026-10-29; section 8 of the contract, the owner amendment of 2026-10-14, binds for the exchange clipboard participant and is implemented, ticket S0074; item O.2 corrected 2026-10-29, ticket S0073) |
| **Home** | `clipboard-guard/README.md` in the shared contracts catalog |
| **Role here** | Owner, Producer and Consumer - Win32 clipboard access, backup and restore, the in-place pipeline, the privacy markers, and the exchange server clipboard participant |

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
- **Items L-R (0.11)** - the exchange server clipboard participant, split by concern:
  - **M** - an explicit press (tray item, chord, desktop shortcut, Jump List task) sends what the clipboard holds
    **whatever markers it carries**: `ExchangeClipboardSource` reads none, honours none, and no preview, no filter and
    no confirmation exists (`ExchangeClipboardIoTests.NoPrivacyMarkerStopsASend`). The settings page, the guide and the
    privacy page say so in words. Nothing is ever sent automatically: no monitor, no timer, no start-up send.
  - **N** - the read runs on its own thread under the shared `_busy` guard, in one open
    (`CyrFlipContext.ReadClipboardForExchange`, `ExchangeClipboardSource.Read`); the clipboard is closed **before** the
    file is looked at or any byte goes out; a program that owns the clipboard and does not render in 5 s is abandoned
    (`owner-timeout`); one operation at a time (`operation-running`).
  - **O, S (0.12)** - a one-file `CF_HDROP` (several files or a folder is refused whole), else an image (the registered
    `PNG`, `CF_DIBV5`, `CF_DIB` in that order, converted to PNG), else `CF_UNICODETEXT`; a file is streamed from disk. The
    first format present decides the kind and is final: a picture that is there but cannot be read (`owner-timeout`) or
    converted (`nothing-to-send`) fails the send and the text beside it is not sent in its place
    (`ExchangeClipboardIoTests`, ticket S0115). The settings page of the exchange server, the guide and the privacy
    page say that a picture goes before the text beside it. Line ends of a text are converted to LF: since the
    2026-10-29 correction item O.2 hands the wires line ends to `DEVICE-EXCHANGE` item CB, so this is conformance, not
    an exception (`ExchangeClipboardSource.NormalizeLineEnds`).
  - **P** - the cap checked before anything is marshalled is the default 5 GiB; the server does not announce its
    own limit, so its `limit-reached` answer, given before a byte flows, is the final word.
  - **Q** - a received value is placed in one open: text as `CF_UNICODETEXT`, a picture as `PNG` + `CF_DIB`, a file saved
    first in the received-files folder and then as `CF_HDROP` with `Preferred DropEffect` = copy; no backup, no restore,
    no marker (`ExchangeClipboardSink`, `CyrFlipContext.PlaceReceivedValue`).
  - **R** - no content, name, login, token or address in any log (`ExchangeClipboardLog` writes a site, a kind, a size, an
    ordinal and a code); the log is whitelisted in the support bundle, no temporary file exists outside the folder
    received files go to (a part is a `.cyrflip-part` file there, swept after a day).

A change to any of these is a change to the contract first: CyrFlip owns it, so the owner amends it in the
catalog (a dated section, a version) and then the code follows.
