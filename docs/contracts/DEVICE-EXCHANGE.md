# Pointer - `DEVICE-EXCHANGE`

| | |
| --- | --- |
| **Id** | `DEVICE-EXCHANGE` |
| **Version** | 1.1, active (read 2026-10-16; sections 5.2, 5.5, 6.1, 7.1-7.4, 7.10, 9 items BR, CY, CZ, DA, 22 and 27 items DE-DR) |
| **Home** | `device-exchange/README.md` in the shared contracts catalog, with `vectors/e2e-v1.json` and the FMS_Win server corpus beside it |
| **Role here** | A device of an exchange account for one role: `clipboard` (product `cyrflip-windows`, capability `e2e-v1`, ticket S0074); and a reader of the connection string of section 5.5 for the traffic light (ticket S0069). CyrFlip registers no account, publishes no resource, broadcasts nothing and keeps no directory |

## What this repository must do to stay conformant

- **Log in only (5.2, items AA, AD, BN).** The account entry is a connection string (or the text of its QR), a login
  and a password; `ExchangeAccountDialog` shows the destination `host:port` before the password field takes the
  focus, reads the string without connecting, looks at the certificate **before** a password exists
  (`ExchangeClient.ProbeAsync`, a handshake that sends no byte - item BQ) and sends the one `enroll` to that endpoint
  and to no other (`ExchangeEnrollment.JoinAsync`). The password is cleared after the exchange and never stored.
  The `deviceId` is 16 random bytes, base64url; an `id-collision` answer to a fresh id regenerates it once. An
  identity already on the device is the same entry signing in again, not a second one.
- **Trust (7.1, items D and AY, 5.5).** `ExchangeTrust.Evaluate`: Windows validates the chain first and pins nothing
  when it accepts; a leaf it rejects is pinned only after the person was shown its fingerprint and accepted it; a
  pin holds the leaf and the key fingerprint, a renewal with the same key is accepted with no prompt, another key
  is refused with both fingerprints shown. TLS 1.2 at least, checked after the handshake
  (`TlsExchangeTransport`).
- **Framing (7.1, item T).** A 4-byte big-endian length, one UTF-8 JSON object, `schemaVersion` 2 first; 16384
  bytes until `welcome` or `enrolled`, 262144 after (`ExchangeWire`). An unknown member is ignored.
- **Streams (7.2).** The control stream (`hello`, `list`, `clipboard-info`, `revoke`) and a **separate** data stream
  for `clipboard-put` and `clipboard-get`, which must be the first envelope after `welcome`: a `list` followed by a
  `clipboard-put` on one stream is a control stream that sends an envelope it does not accept.
- **The clipboard (22, items BU-CD).** One object at a time (a file, a text or a picture), refused when it is
  several files or a folder; a send goes to every `member` entry and succeeds with one (CA), a get takes the newest
  by the server's own time (CA); no preview, no filter, no confirmation, no history, no notification (CD).
  Text travels with LF ends (CB); the received file lands in a folder the person can see and never overwrites.
- **Sealed content, always (27, items DF-DQ).** No clear fallback through the server (DP): a server whose `welcome`
  lacks `sealed` is `server-no-e2e`, a clear value is never placed, a clear value is never sent. The device key
  is a P-256 pair per entry made before `enroll`, its public half in `e2eKey`, its private half and the token in
  DPAPI (`ExchangeSecrets`). Recipients are the other capable devices of the entry whose key is the one first seen
  (trust on first use, `ExchangeRecipients`; a changed key is `key-changed`, never replaced). HPKE Auth mode,
  AES-256-GCM segments of 65536 bytes (`ExchangeE2e`, `ExchangeHpke`, `ExchangeE2eNative`).
- **What a person reads (7.10, items CY, CZ, DA, DE, DR).** The table of messages in English, Russian and Ukrainian
  word for word, the other ten languages translated (`ExchangeMessages`, `Localization.ExchangeServer.cs`); what
  happened first, what to try second, the code only in the details line; the honest text of item DE is shown whole on
  the exchange page of the settings, the guide and the privacy page. No secret in a message, a details line or a log.

## How it is held

- **Vectors, replayed.** `tests/CyrFlip.Tests/Fixtures/DeviceExchange/`: `e2e-v1.json` and `fmsw-234-sealed.json`
  (every box opens, every failing case fails, the clipboard values reproduce their keys and sealed sizes -
  `ExchangeE2eTests`), `fmsw-234-clipboard.json` (four transcripts of the real server's corpus, held by their SHA-256:
  every frame decodes, the frames this program writes are the server's own - `ExchangeWireTests`) and
  `connection-string-v1.json` (the traffic light's reading, `TrafficLightContractStringTests`).
- **Behaviour, against a scripted server.** `ExchangeClientTests` and `ExchangeClipboardServiceTests` run the whole
  path over an in-memory server that stores only the sealed bytes, like the real one.
- **The traffic light keeps its own paragraph.** An `exchange` target is still a TCP connect and a TLS handshake with
  SNI, the SHA-256 of the leaf and a compare; **no application byte is ever sent** (`ExchangeProbe`), and the login hint
  `u` of a string is dropped there. The clipboard's join (`ExchangeConnectionString.Parse(text, keepLoginHint: true)`)
  is the one place the hint is read, and it fills the login field and nothing else.

## Open items

1. The portal page `exchange-clipboard.html` (and `-ru`, `-uk`) is another product's and was not published when this
   was written; the settings page and the guide link to it and say what the feature is in their own words.
2. `CLIPBOARD-GUARD` item O reads the clipboard as file, text, picture; item CB of this contract lists file, picture,
   text and says to convert line ends to LF. This program follows item O for the order and item CB for the wire
   (recorded as a dated exception in the registry until one text is amended).
3. The Android reference client sends `list` and `clipboard-put` on one stream; the FMS_Win server accepts a put
   only as the first envelope after `welcome`. To be settled in the joint run (FMS_A S4213, S4244).
4. Not built: a QR picture (the text of one is pasted), the Drive path of the clipboard (items CL, CP).
