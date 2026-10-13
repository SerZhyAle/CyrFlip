# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 0.19, draft (read 2026-10-13; the part read is section 5.5, item Y of section 16) |
| **Home** | `device-exchange/README.md` in the shared contracts catalog, section 5.5, with `vectors/connection-string-v1.json` beside it |
| **Role here** | consumer of one text format - the traffic light's one-field add reads the `FMSEXCH1` connection string. CyrFlip is not a device of an account: it enrolls nothing, publishes nothing, speaks no frame of the wire and writes no connection string |

## What this repository must do to stay conformant

- **Read, never write.** No connection string, no `.fmsexch` file and no QR is produced here.
- **Fields.** `x` (host and port), `fp` (the pin of the server's leaf certificate) and `n` (a label hint) are
  read; `u`, the login hint, is checked against the form of 5.1 and **dropped on the spot**. An unknown key is
  ignored whatever its value (`ExchangeConnectionString.Parse`).
- **Refused whole.** A known key that repeats or breaks its rule, a field that is empty or has no key or no `=`,
  white space inside, a non-ASCII byte or more than 512 characters after trimming: nothing of the string is
  read and the add line gives the artifact's reason word. A later prefix (`FMSEXCH2:` and up) is refused as
  `newer-major`.
- **Recognition is by content** - the prefix after one leading U+FEFF and the trimming
  (`TargetRecognizer.RecognizeLine`) - never by a file name.
- **No secret survives.** What leaves the parser is host, port, the pin and the label. Nothing in the stored
  target, the settings file, the change journal or any log can hold the login hint or the raw text
  (`TrafficLightLog` records sites and exception types only); the raw line exists in the add field only. The
  recognizer's own result keeps the text of a line **only** when nothing recognized it and it is not of this
  family (`RecognizedLine.Text`, for the details panel to start from); a recognized or refused line, and any
  `FMSEXCH`, `FMSSFTP` or `FMSCFG` text of a version this reader does not know, leaves it empty
  (`ARecognizedLineKeepsNoRawTextAndNoSecret`, `AContractStringOfAnUnknownVersionIsNeitherRecognizedNorEchoed`).
- **The pin is the user's.** A target carries the `fp` it was added with, or the one the user typed; no probe
  rewrites it. A different leaf is shown red as `certificate-changed` with the pinned and the observed value,
  and nothing is pinned, remembered or accepted on the user's behalf (`ExchangeProbe.Verdict`).
- **A file is not read.** No path of this product opens a `.fmsexch` file, so the file cases of the vector are
  not exercised; the text of one pasted into the field is read like any other string. A `.fmscfg` document
  (`FMSCFG`) is not consumed at all: a JSON document in the field is recognized by its braces and refused unread.
- **The vector is pinned by copy.** `tests/CyrFlip.Tests/Fixtures/DeviceExchange/connection-string-v1.json` is a
  byte copy of the catalog artifact, held by its SHA-256 as a literal
  (`4e75c0da11515f4b4e77a46e24751b55f874d699a0362de26cd591df79a8d26d`, the value in the contract's header;
  `TheVectorFilesAreByteIdenticalToTheCatalogsCopies`) - a re-issue of the file updates the literal in the same
  change. `TrafficLightContractStringTests` replays its 93 text cases against the verdict and the reason word and
  checks the `u` canary (`EveryTextVectorOfTheConnectionStringIsReadAsTheContractSays`,
  `NoSecretIsKeptFromAConnectionString`).

## What the probe does with the server it was given

An `exchange` target is checked by a TCP connect and a TLS client handshake with SNI set to the host; the leaf
is hashed and the stream closed. **No application byte is ever sent**, so no envelope of section 7 is
spoken. Whether a handshake with no frame is permitted, and counts as no failed authentication, is not written
in the contract; the question is `PROPOSAL-2026-10-13-cyrflip-reachability-handshake.md`.

## Open debts

1. The 11 `file` cases of the vector have no reader here (see above).
2. The contract's `consumers` header does not name this product; the same proposal asks.
3. Section 5.5's trust rule is applied by the quick join of 16 item AD before a password reaches a stream. This
   product joins no account and sends none, and the probe's verdict table is its own (the `exchange` rows of
   `PLAN/S0069_network-traffic-light.md`, local, not published).
