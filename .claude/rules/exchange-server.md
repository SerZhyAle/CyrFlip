---
paths:
  - "src/CyrFlip/Exchange*.cs"
  - "src/CyrFlip/TlsPolicy.cs"
  - "src/CyrFlip/CyrFlipContext.ExchangeClipboard.cs"
  - "src/CyrFlip/SettingsForm.ExchangeServer.cs"
---

# Exchange server clipboard

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Status

- The **exchange server clipboard** (`Exchange*`, feature 16 below, ticket S0074) is implemented, unit-tested against a scripted server and the real server's transcripts, built in all 13 languages; the live check against the owner's server (`tools/uitest/Test-ExchangeClipboard.ps1`) and the joint runs between programs want a human.

## Feature

16. **The clipboard on the exchange server - "Буфер на сервере" (ticket S0074), opt-in and off by default.** CyrFlip is
   **a device of an exchange account for one role, `clipboard`** (product `cyrflip-windows`, capability `e2e-v1`): one
   press sends what the Windows clipboard holds to the server of every account the device is signed in to, another
   puts the server's newest value onto the clipboard. `EnableExchangeClipboard=0` means **no connection is ever
   opened for it**, no tray item, no chord, no Jump List task, and a desktop shortcut that outlives the switch is
   answered in words. The owner's decisions (2026-10-14) are the shape: the slot is **personal** (one account's
   devices), the server keeps **only the last value, in memory**, **one object at a time** (a text, a picture or one
   file - several files or a folder are refused with "put them into one archive first"), **no preview, no marker
   filter, no confirmation** (a password copied a minute ago goes if the person sends it - `CLIPBOARD-GUARD` item M,
   said in the settings page, the guide and the privacy page), **CyrFlip only logs in** (registration is the server's;
   the empty state says to ask its host for an invitation). Five rules carry it:
   - **Always sealed, no clear fallback** (`DEVICE-EXCHANGE` 27, item DP). The value is sealed on this PC for the other
     devices of the account that hold the capability and a trusted key (HPKE Auth mode P-256 + AES-256-GCM segments of
     64 KiB: `ExchangeE2e`, `ExchangeHpke`, `ExchangeDeviceKey`, `ExchangeE2eNative`, held by `ExchangeE2eTests` against
     RFC 9180 A.3, the catalog's `e2e-v1.json` and the server corpus). Recipients are other devices only
     (`ExchangeRecipients`): keys are learned on first use per entry and **never replaced** - a changed key is
     `key-changed`, a device without one `e2e-missing`, both named in the result. A server whose `welcome` lacks
     `sealed` is `server-no-e2e`: nothing is sent, nothing clear is ever placed. The server sees the account, the
     device names, that a value exists and its size, and the settings page says so in the contract's own words
     (`ExchangeMessages.HonestText`, EN/RU/UK verbatim).
   - **Trust before a password** (items D, AY, BN). The dialog reads the string without connecting, shows the
     destination `host:port`, **probes the certificate with a handshake that sends no byte**, shows a self-signed
     leaf's fingerprint and pins it only on a yes, and only then sends the one `enroll`
     (`ExchangeAccountDialog`, `ExchangeClient.ProbeAsync`, `ExchangeTrust`). A pin holds the leaf and the key
     fingerprint; a renewal with the same key is accepted without a prompt, another key is refused with both
     fingerprints. The password is never stored; the **device token and the private device key live only in DPAPI**
     (`ExchangeSecrets` under `HKCU\Software\CyrFlip\ExchangeSecrets`, never in the JSON of the entries, a settings
     export, a support bundle or a log).
   - **Two streams, never one.** The control stream carries `hello`, `list` (the keys of the account's devices) and
     `clipboard-info`; `clipboard-put` / `clipboard-get` must be the **first envelope after `welcome`** of a stream of
     their own (the FMS_Win server closes a control stream that gets a put). Frames carry `schemaVersion` 2 first,
     16384 bytes until `welcome`, 262144 after (`ExchangeWire`, replayed against the real server's transcripts in
     `ExchangeWireTests`). A stream that closes with no frame after our envelope is "the server did not answer",
     never "unreachable"; a silent stream is closed by a watchdog and a cancel closes the socket
     (`ExchangeConnection` - a netfx `SslStream` read cannot be cancelled by a token).
   - **Three phases, each on the guard it needs** (the translator's discipline). The clipboard is read **once, in one
     open, under `_busy`, on its own thread** and closed before any file or network I/O; a program that owns it and
     does not render in 5 s is abandoned (`owner-timeout`). Order: one file by path (streamed from disk, hashed once,
     checked unchanged), else a picture (`PNG`, `CF_DIBV5`, `CF_DIB`), else a text (LF ends on the wire) - `CLIPBOARD-GUARD`
     item S (0.12, ticket S0115): **the first format present decides the kind and is final**, so a picture that is on
     the clipboard but cannot be read (`owner-timeout`) or converted to PNG (`nothing-to-send`) fails the send and the
     text beside it is not sent in its place; a spreadsheet range or a web selection travels as the picture, and the
     settings page, the guide and the privacy page say so. The network holds no guard. A received value is verified (the server's size and SHA-256 over the sealed bytes, then the box's
     over the clear bytes), written to the clipboard in **one open with no marker and no backup**, and a file lands
     first in the received-files folder (`Downloads\CyrFlip`, never overwriting, `.cyrflip-part` while it arrives;
     a name that cleans to nothing becomes `clipboard-<yyyyMMdd-HHmmss><ext>` from `storedAt` in UTC - `DEVICE-EXCHANGE`
     1.11 item EL, `ExchangeClipboardSink.NamelessName`)
     (`ExchangeClipboardSource`, `ExchangeClipboardSink`, `CyrFlipContext.ExchangeClipboard`). A received text is
     recorded by the history like any copy.
   - **What a person reads is the contract's table.** A failure shows what happened, then what to try, with the
     code only in a details line and the log (`ExchangeMessages`: the rows of item CZ and the 15-step ladder of item CY
     as one-at-a-time hints); the EN/RU/UK words are the contract's and are held character for character by
     `ExchangeMessagesTests`; the plain-language ratchet of `ComprehensionGateTests` exempts exactly this table.
   Surfaces: the **"Сервер обмена"** page (`SettingsPageKey.ExchangeServer`, tab glyph `source.exchange-server` -
   what the feature is, the module switch, the accounts with their states, the two chords - shipped unassigned - the
   desktop shortcuts, the received-files folder, two portal links), the two tray items (and "Cancel the clipboard
   transfer" while one runs), the two pipe commands `/clipboard-send` and `/clipboard-get` (`APP-ACTIVATION` 0.12,
   section 9) that the desktop shortcuts and the Jump List tasks run, and the hotkeys overview. The portal pages
   (`ExchangePortalLinks`, hosted by the FastMediaSorter site, EN/RU/UK, every other language gets English) are
   published - all six answered HTTP 200 on 2026-10-25. Log: `exchange-clipboard.log` (site, kind, size, ordinal, code - whitelisted in the
   support bundle). Not built: a QR picture (the text of one is pasted), the Drive path of the clipboard. Spec:
   [PLAN/done/S0074_exchange-server-clipboard.md](../../PLAN/done/S0074_exchange-server-clipboard.md); the owner-run live check is
   `tools/uitest/Test-ExchangeClipboard.ps1`.
   **The settings standard (`DEVICE-EXCHANGE` 1.12, items EN to ER, ticket S0095):** beside an account whose server lists
   `settings`, "Save as STANDARD" / "Read STANDARD" send `settings_save_standard` / `settings_get_standard` (snake case,
   the contract's) on the control stream under `appId` `cyrflip`; never to a server without the feature, never for a guest
   (`ExchangeSettingsStandardService`). `SettingsStandard` is the pure half: ten groups mapped from the settings file's
   inventory groups (`general`, `indicators`, `shortcuts`, `clipboard`, `translate`, `quick-notes`, `graphics`, `symbols`,
   `traffic-light`, `exchange-server`), `LocalOnly` plus the file's `Excluded`, every `navigation/` value and the launcher
   never written and ignored on read, a payload of at most 65536 bytes. A read is parsed whole first, then goes through the
   **same** preview and transaction as a settings file (`ImportDocument(load, source)`). Both acts confirm first; nothing
   runs on a change, at start or on connecting; the last-seen `updatedAt` is in memory only. The server's operator can read
   a stored standard (unsealed, item EN) - said on the page, in the guide and the privacy pages.
   **The exchange server's mark (`EXCHANGE-MARK` 0.4, ticket S0079):** the coloured star the portfolio owner ordered for
   the server stands beside its name - the settings page's header band, the sign-in dialog, each account entry, the About
   credit "Media Exchange Server", two help pages, the guide chapter and the README headings - drawn by `ExchangeMark` /
   `ExchangeMarkPicture` from **byte copies of the catalog's artwork, never redrawn**: `assets/exchange-mark/` (PNG steps,
   embedded in the exe, plus the glyph master), `docs/assets/exchange-mark/` (site) and `docs/help/exchange-mark/` (help
   set), all `-text` in `.gitattributes` and pinned by SHA-256 in `ExchangeMarkTests`. The page strip's one-tone glyph is
   the same star through `tools/IconGen/glyph-overrides.txt` (delete the line once the catalog's generated `glyphs/source.exchange-server.svg` draws the star - `ICON-SET` 0.37 already carries it in `vocabulary.jsonl`); the
   launcher icon, the open exchange file (S0023) and the `sftp` kind never wear it.

## Modules (`src/CyrFlip/`)

- **Exchange*.cs / ExchangeClipboardIo.cs / ExchangeShortcuts.cs / ExchangePortalLinks.cs** - the exchange server clipboard (feature 16),
  one concern each. `ExchangeWire` is the pure framing and the envelopes; `ExchangeTrust` the pin rule and the two
  fingerprints; `ExchangeConnection` + `TlsExchangeTransport` the TLS stream with a watchdog; `ExchangeClient` the
  operations (`ProbeAsync`, `EnrollAsync`, `OpenAsync`, `ListAsync`, `InfoAsync`, `PutSealedAsync`, `GetAsync`,
  `RevokeAsync`) returning an `ExchangeOutcome` with a code, never throwing for the network; `ExchangeEnrollment` the
  join (identity, key, `id-collision` retry); `ExchangeAccountEntry` + `ExchangeSecrets` the entry (no secret in its
  JSON); `ExchangeRecipients` the trust-on-first-use rule; `ExchangeClipboardService` the send and the fetch across
  entries; `ExchangeMessages` / `ExchangeCodes` the contract's messages; `ExchangeClipboardSource` / `Sink` the two
  clipboard sides; `ExchangeE2e*` / `ExchangeHpke` / `ExchangeDeviceKey` the crypto (CNG + bcrypt P/Invoke, no
  dependency). `FakeExchangeServer` (tests) is an in-memory server that stores only sealed bytes.
