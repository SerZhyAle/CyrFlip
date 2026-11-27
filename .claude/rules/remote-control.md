---
paths:
  - "src/CyrFlip/RemoteCo*.cs"
  - "src/CyrFlip/ExchangeCommandListener.cs"
  - "src/CyrFlip/ExternalControlWindow.cs"
  - "src/CyrFlip/CyrFlipContext.RemoteCommands.cs"
  - "src/CyrFlip/SettingsForm.RemoteCommands.cs"
---

# Commands from other devices (REMOTE-CONTROL)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

19. **Commands from other devices - the target of `REMOTE-CONTROL` 0.7 (tickets CYRFLP-0140 and CYRFLP-0142, `DEVICE-EXCHANGE` 1.18 items GC to GF).**
   Another device of the same exchange account (FastMediaSorter for Web, or an agent through its own client) asks this
   CyrFlip to run one verb of its manifest, sealed end to end; CyrFlip decides on this PC. Six rules carry it:
   - **The resident stream follows the module, not the switch** (A1): while `EnableExchangeClipboard` is on, every signed-in
     `member` entry this build enrolled (`ExchangeAccountEntry.CommandCapable`, set at `enroll`, which now declares
     `command-v1` beside `e2e-v1`) whose server's `welcome` lists `command` and `sealed` holds one control stream
     (`ExchangeCommandListener`, keepalive proposed 30 s, a stream silent for three intervals is reopened, back-off 5 s to
     5 min, a server without the feature asked again after 6 h). A device keeps one control stream (7.3), so every on-demand
     stream (`ExchangeClient.OpenAsync`: send, get, notes sync, the settings standard) takes `ExchangeControlLease` and the
     resident one lets go until the last lease ends (A10). An entry enrolled before signs in again once (A2).
   - **The switch is the person's, on this PC** (rule 8): `RemoteCommandsListen`, off by default, one setting shown in the
     External control group of the Exchange server page and in section T1 of the External control window - never a command,
     a request, the settings file or the standard. Off, every request is answered
     `not-permitted` and logged. `status` answers it as `remoteCommands`.
   - **Grants and consent** (rules 9 and 10, `RemoteControlGrants` in `RemoteCommandGrants`): per account entry and
     controller `deviceId`, a class to `always` or `ask`. The window's T3 shows a class only when CyrFlip has a remote
     verb of it - `safe` alone today - with the three-way choice No access / Ask each time / Always allow (no class left =
     the device not allowed); the model holds all four. A controller that left the directory or whose key changed loses
     its grant when the directory is read. `ask` is `RemoteConsentPrompt`: a small topmost non-activating window, Decline
     the answer to Enter and Escape, `declined` after 60 s, mirrored in T2 (answering one answers both).
   - **One window, Settings keeps the entry** (rule 18, `WINDOW.md`, CYRFLP-0142): `ExternalControlWindow`, one instance,
     720 by 600 by default and 560 by 480 at the least (bounds in `ExternalControlX/Y/Width/Height`, written only by
     `AppConfig.SaveExternalControlWindow`), sections T1 This device (switch, state line, one action), T2 Waiting for your
     answer, T3 Who can control this device (name, program of item CX, presence from the directory record), T4 Commands
     this program accepts (collapsed), T5 Recent requests (the last 20, newest first, the outcome words of section 6;
     `RemoteControlWords`); no Save and no Refresh - the context redraws it. The Exchange server page keeps one group
     `exchange/commands` with the switch and "Open External control"; turning the switch on there opens the window at T3.
     Its help-map row inherits the Exchange server page until the generator writes its own page.
   - **The decision is pure** (`RemoteControlPolicy.Decide`, `RemoteControlTests`): the payload read strictly (an unknown
     member is never ignored), the switch, the grant, the time window (`issuedAt + ttl + 300 s`, or more than 300 s
     ahead: `expired`), the verb (not `remote` = `unknown-verb`), its arguments (`path` options, `json` and `result` are
     never remote) through the local parser, the class, the consent. The box is opened with the key trusted for its
     `from`, never by the clear `fromDeviceId`, and the answer is sealed to that `from` alone. A run goes through
     `AnswerAppCommand(remote: true)`, which skips the local rule-13 setting; the answer's `message` is English.
   - **Once, in order, on disk** (rule 12): one worker thread takes requests in arrival order; `RemoteControlRing`
     (`remote-commands-ring.dat`, DPAPI) is written with an `interrupted` answer before a verb runs, keeps every answer
     until its request's own window ends and answers a repeat from there; a full ring (10000) answers `busy`, never drops.
   The first remote slice (`remote.confirm` `grant`, `cliVersion` 1.1; `Render-CliDoc.ps1 -Check` refuses a remote verb
   above `safe` or interactive): `commands` (the remote entries only, `AppCommandManifest.RemoteView`), `version`,
   `status`, `show`, `quit`, `layout current`, `layout list`, `keepawake set` and the new `hotkeys pause --paused <bool>`
   (the master switch's own apply, `ApplyHotkeysEnabled`). `remote-commands.log` (`RemoteCommandsLog`) holds one line per
   request - time, controller name and short id, verb, class, outcome, exit, argument names only - and a `sealed-failed`
   line for a box that did not open; the window shows its last 20 lines. Not in the log bundle (A6). The live cases R1 to
   R10 need a server that lists `command` and FastMediaSorter for Web: the owner's device test. Spec:
   [PLAN/done/CYRFLP-0140_remote-control-adoption.md](../../PLAN/done/CYRFLP-0140_remote-control-adoption.md).
