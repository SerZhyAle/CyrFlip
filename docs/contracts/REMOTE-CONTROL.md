# Pointer - `REMOTE-CONTROL`

| | |
| --- | --- |
| **Id** | `REMOTE-CONTROL` |
| **Version** | 0.7, draft (read 2026-11-23 with its companion `WINDOW.md`, ticket CYRFLP-0142; 0.4 read 2026-11-20 with its carrier, `DEVICE-EXCHANGE` 1.18 items GC to GF in `device-exchange/REMOTE-COMMAND-2026-11-20.md`, ticket CYRFLP-0140) |
| **Home** | `remote-control/README.md` in the shared contracts catalog |
| **Role here** | A **target**: one program on one device of an exchange account that another device of the same account (FastMediaSorter for Web, or the agent client) asks to run one verb of this product's `APP-COMMANDS` manifest. CyrFlip is never a controller |

## What this repository must do to stay conformant

- **Declare `command-v1` only beside `e2e-v1`, at `enroll` (rule 1, item GC).** `ExchangeMembers.Enroll` declares both;
  an entry enrolled before this build signs in again to gain it (`ExchangeAccountEntry.CommandCapable`).
- **Hold a control stream while the exchange module is on (sections 1 and 5).** `ExchangeCommandListener` holds one per
  signed-in `member` entry whose server lists `command` and `sealed`, with keepalive (`DEVICE-EXCHANGE` 7.3), and lets it go
  while an on-demand control stream of the same entry is open (`ExchangeControlLease`, one control stream per device).
- **The box is the only authority (rule 4).** The controller is the `from` of the box opened with the key trusted for it;
  the clear `fromDeviceId` is routing only and is logged unverified for a box that does not open; the answer is sealed
  under `command-result:<requestId>` to that `from` alone.
- **The payload (rule 5).** Read strictly (`RemoteControlRequest.TryParse`: an unknown member is `bad-argument`, a `v`
  above 1 is `schema`, `ttlSeconds` 10 to 600, default 120); an answer within 65536 bytes, `truncated` when its data is
  dropped (`RemoteControlAnswer`), with an English `message`.
- **The verbs (rules 6 and 7).** Only entries of `docs/cli-commands.json` that carry `remote`, `confirm` `grant` or
  `device`; `commands` answered remotely lists only those (`AppCommandManifest.RemoteView`); a `path` option, `--json` and
  `--result` are never remote arguments. `tools/cli/Render-CliDoc.ps1 -Check` refuses a remote verb above `safe` or one
  that is `interactive` in this first slice.
- **The switch, the grants, the consent (rules 8 to 10).** `RemoteCommandsListen` (off; shown in the External control
  group of the Exchange server page and in T1 of the window, never in the settings file or the settings standard);
  `RemoteCommandGrants` per account entry and controller, a newly ticked device getting `safe` `always`; `ask` and
  every `confirm: device` verb confirmed on this PC by `RemoteConsentPrompt` (topmost, non-activating, Decline the
  answer to Enter and Escape), `declined` after 60 seconds. A controller that left the directory or whose key changed
  loses its grant.
- **The External control window (rule 18, `WINDOW.md`).** `ExternalControlWindow`: one instance, T1 to T5 in the order
  and with the words of `WINDOW.md` sections 4 and 6 (EN, RU and UK in `Localization.RemoteCommands.cs`, the other ten
  languages translating the English), the three-way choice per class shown only for classes with a remote verb (`safe`
  today), T2 mirroring the prompt. Settings keeps one group, the switch and "Open External control"; turning the switch
  on there opens the window at T3.
- **The log (rule 11).** `remote-commands.log`: one line per request that opened, argument names only; T5 of the
  window shows the last 20, newest first; nothing of it goes to a server.
- **Once, in order, and not late (rule 12).** One worker takes requests in arrival order; `remote-commands-ring.dat`
  (DPAPI) is written before a verb runs, keeps every answer until its request's own `issuedAt + ttlSeconds + 300 s`,
  answers a repeat from there and is never trimmed early (a full ring answers `busy`); `expired` for a window that ended
  or an `issuedAt` more than 300 seconds ahead.
- **The refusals (rule 13).** `not-permitted` (12), `declined` (16), `expired` (2) inside the box; the local setting of
  `APP-COMMANDS` rule 13 is never asked for a remote request (`AnswerAppCommand(remote: true)`).

## Evidence and what is open

- Rules 4 to 13 of the decision core: `tests/CyrFlip.Tests/RemoteControlTests.cs` (release tier, not run until the release).
- Cases R1 to R10 on two live devices, against a server that lists `command` and FastMediaSorter for Web as the
  controller: owed, the owner's device test of ticket CYRFLP-0140. R2's `reads-user-data` half is owed with the first
  remote verb of that class; R11 is the agent client's.
- Cases W1 to W7 and R12 of section 4 (the window, the Settings group, the prompt): manual on a real desktop, owed, the
  owner's check of ticket CYRFLP-0142. The window's own help page is owed to the next run of the help generator; until
  then its help-map row inherits the Exchange server page (`CONTEXT-HELP`, `WINDOW.md` section 7).
- No vectors yet (section 4 of the contract): the first target and controller owe them, generated, never written by hand.
