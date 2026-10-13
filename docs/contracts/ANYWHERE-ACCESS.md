# Pointer - `ANYWHERE-ACCESS`

| | |
| --- | --- |
| **Id** | `ANYWHERE-ACCESS` |
| **Version** | 0.13, draft (read 2026-10-13; the part read is section 4.1, which no amendment since the rung-1 vectors of 2026-10-06 has changed) |
| **Home** | `anywhere-access/README.md` in the shared contracts catalog, section 4.1, with `vectors/descriptor-v2.json` beside it |
| **Role here** | consumer of one text format - the traffic light's one-field add reads the `FMSSFTP1` and `FMSSFTP2` share code. CyrFlip holds no share, opens no tunnel, speaks no frame of the exchange wire and writes no code |

## What this repository must do to stay conformant

- **Read, never write.** No `FMSSFTP1` or `FMSSFTP2` code is produced here.
- **Fields read.** Every host of `h` becomes one `sftp` target, all on the port `p`; in a v2 code `x` becomes
  one `exchange` target. Nothing else is carried onto a target.
- **Fields validated and dropped on the spot.** `u`, `pw`, `fp`, `sid` and `drv`: a code that breaks the rule of
  any of them is refused whole, and none of them is kept. `fp` is the **share's** host-key pin - a different
  thing from the exchange server's pin, and not comparable without a key exchange - so it is never copied onto
  the `exchange` target, which gets no fingerprint from a share code (`SftpDescriptor.Parse`).
- **Refused whole, never partly read** (section 4.1): a missing `h`, `p`, `u` or `fp`, an `x` without a `sid` or a
  `sid` without an `x`, an `x` with no port or a bare IPv6 literal, a padded share id or one with its spare bits
  set, a `drv` other than `1`.
- **A `FMSSFTP1:` code is read by the v1 rules only**: `x`, `sid` and `drv` inside it are ignored whatever they
  hold (the vector's `v1IgnoresV2Keys`).
- **The port is not guessed.** A host of `h` is dialled on `p`, the share's own SFTP port; the third-party port
  of section 5.5 is not in a code (amendment G) and this product never derives one.
- **No secret survives.** What leaves the parser is the hosts, the port and the exchange endpoint. The login,
  the password, the share's pin and the share id are in no stored target, settings file, change journal or log
  (`TrafficLightLog` records sites and exception types only); the raw line exists in the add field only. The
  recognizer's own result keeps the text of a line **only** when nothing recognized it and it is not of the
  `FMSEXCH` / `FMSSFTP` / `FMSCFG` family (`RecognizedLine.Text`, for the details panel to start from); a
  recognized or refused line, and a code of a version this reader does not know, leaves it empty
  (`ARecognizedLineKeepsNoRawTextAndNoSecret`, `AContractStringOfAnUnknownVersionIsNeitherRecognizedNorEchoed`).
- **A `.fmscfg` document is not consumed** (`FMSCFG`, including the vector's `fmscfgTunnelPath`): a JSON
  document in the field is recognized by its braces and refused unread.
- **The vector is pinned by copy.** `tests/CyrFlip.Tests/Fixtures/AnywhereAccess/descriptor-v2.json` is a byte
  copy of the catalog artifact, held by its SHA-256 as a literal
  (`04354e86fa5700c1fafd9fac7842949ea9a8824cb4b09587d9c849c29e9d89b6`;
  `TheVectorFilesAreByteIdenticalToTheCatalogsCopies`) - a re-issue of the file updates the literal in the same
  change. `TrafficLightContractStringTests.EveryDescriptorVectorIsReadAsTheContractSays` replays its 5 valid
  codes (hosts, port, exchange endpoint), its 9 refused codes and its v1 case; `NoSecretIsKeptFromAnSftpCode`
  is the canary.

## What the probe does with the host it was given

An `sftp` target is checked by a TCP connect, one read of the server's identification line (`SSH-2.0-` or
`SSH-1.99-`, at most 255 bytes) and a close. **Nothing is sent** and no key exchange happens, so a green tile
means "an SSH server answers", not "this is the share" - the host-key pin is not compared and the editor's hint
says so ("The resource and the server's key are not checked").
Whether a connect that reads the banner and closes takes a connection slot of the server is not written in the
contract; the question is `PROPOSAL-2026-10-13-cyrflip-banner-read.md`.

## Open debts

1. The contract's `consumers` header does not name this product (asked in the proposal above).
