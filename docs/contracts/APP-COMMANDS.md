# Pointer - `APP-COMMANDS`

| | |
| --- | --- |
| **Id** | `APP-COMMANDS` |
| **Version** | 0.3, draft (read 2026-11-19, items V-Z, two divergences open - see the end of this page; adopted by ticket S0107, first read 2026-11-15) |
| **Home** | `app-commands/README.md` in the shared contracts catalog |
| **Role here** | Adopter - the command line of CyrFlip: the request-response profile on the existing pipe, the manifest, the five base commands and the verbs behind CyrFlip's shipped spellings |

## What this repository implements

- **The manifest** (rule 18): `docs/cli-commands.json`, embedded in the exe byte for byte (`AppCommandManifest`);
  `CyrFlip.exe commands --json` prints those bytes. `docs/CLI.md` is rendered from it by
  `tools/cli/Render-CliDoc.ps1` (rule 20) and held to it by the fast tier (`build.ps1 -Fast -Area cli`) and the
  release preflight.
- **The grammar** (rules 1 to 3, `AppCommandLine`): verbs of one or two words, `--kebab-case` options, `--` ends the
  options, verbs and option names without regard to case, values never; an unknown verb, option or value, a request
  over 64 KiB or an argument over 32 KiB is exit 10 and nothing runs. The running copy reparses a request's `args`
  with the same parser.
- **The role before the interface** (rule 6, `Program.Main`): the parse comes first; `commands` and `version` are
  `forward: never` (item C) and touch no mutex; every other verb either forwards to the running copy or follows its
  `coldStart` (rule 10, `AppCommandRunner.ColdStart`): `start` starts the tray copy and then forwards, `oneshot` works
  in the process after releasing the mutex, `refuse` is exit 11 `not-running`.
- **The profile** (rule 7, `LauncherIpc.SendRequest` / `LauncherIpc.Answer`): one request line and one response
  line on the same connection of `CyrFlip_Launcher_{userSid}_{sessionId}`, now read-write. The running copy's UI
  thread has 800 ms to take a command or the answer is `busy` and the command is not run later (item J); a modal
  window or the first-run tour is `busy` (rule 10).
- **The gate** (rule 13, item F, `AppCommandVerbs.RiskGate`): `EnableExternalCommands`, off on every profile, on the
  General page, never in a settings file or a settings standard. The spellings shipped before the adoption keep
  their one-line token path and are gated by their module as before.
- **The older copy** (item K): a one-way pipe refuses the read-write open; the second process then sends the verb's
  legacy token where it has one and exits 2 with a sentence that the running copy is older.
- **The log** (rule 16): `app-commands.log` - verb, origin, caller pid, status, exit, reason; no argument. In the
  log bundle's whitelist.
- **The words that make the interface findable** (rule 13 last paragraph, rule 20, ticket S0112): the privacy page in
  all 13 languages names every risk class the manifest has verbs of as a `<code>` token, and says that no command can
  turn the setting on; the guides carry a `commands` chapter in EN, RU and UK and each of the 13 first pages links it;
  `CLAUDE.md` tells a development agent that `docs/CLI.md` and `commands --json` list what the product can be asked to
  do. `tools/cli/Render-CliDoc.ps1 -Check` holds all of it in the fast tier.

## Declarations

- **No console companion executable** (rule 9): the exit code and `--result` are the two reliable channels; output
  goes to the parent's console through `AttachConsole` when there is one.
- **`show`** is the Settings window (CyrFlip's main window, `APP-ACTIVATION` rule 7C).
- **`clipboard send` and `clipboard get`** are `wait: accepted`: the transfer ends in a tray balloon, so the answer
  is given once it has started.
- **`screenshot region` and `screenshot full`** (ticket S0110) are `reads-user-data`, `coldStart: refuse`. `--out` is
  item P paragraph 1's reading: used exactly as given, an existing file refused without `--overwrite` (exit 11), a
  folder (existing, or a path ending in a separator) gets the `CAPTURE-OUTPUT` rule 3 name; the folder is never
  created. `screenshot region` is `interactive` (120 s bound, exit 16 on Escape, the overlay closed by the instance at
  the bound); the reasons it emits beyond the table are `exists`, `capture-failed` and `clipboard-failed`
  (rule 12's last sentence), and it uses the contract's own `no-space` (13), `io-error` (14) and `network` (15).
- **`screenshot window` and the `--annotate` option of `screenshot region`** (ticket S0130, the rows C45 and C46; the adopted
  0.3 rules read before the verbs were added) are `reads-user-data`, `coldStart: refuse`, bounds 30000 and - the region
  entry's own member, carrying the row's long run - 600000 ms. `screenshot window` names its window by item Q's options
  (`--window-pid`/`--window-title`, the selection family's own matcher, sentences and window list reused, the two given
  together narrowing) and takes `--out`/`--overwrite` under item P paragraph 1; it grabs the window's visible on-screen
  rectangle (`DWMWA_EXTENDED_FRAME_BOUNDS`, `GetWindowRect` fallback, clamped to the virtual screen) out of the one frozen
  frame - a covered window shows what the screen shows and the entry's summary says so - and a window with no on-screen
  rectangle (minimized, entirely outside the screens) is the table's own `target-missing` (11) with the restore cure in the
  message; it never touches the clipboard (the row lists no `--clipboard`), is not interactive, and beside the table emits
  `exists`, `capture-failed` (rule 12's last sentence). `--annotate` opens the draw-on-capture editor the annotate chords
  use instead of delivering: the module switch `EnableScreenshotAnnotate` gates it with `module-off` (11, item M, behind the
  rule-13 class gate), OK saves the composed picture to the `--out` target under the ORIGINAL freeze instant (the drawing
  minutes never move the name or the `tIME` chunk), Escape in the selection and Cancel in the editor answer 16 and write
  nothing, `--clipboard` works on the composed picture as without, the plain spelling keeps its 120-second selection budget
  (the dispatch arms it for a plain run and the annotate handler rations its selection phase the same way), and at either
  budget the instance closes what it opened - the overlay or the editor - so `CaptureBusy` clears and nothing is written
  after the answer.
- **`trafficlight status` and `trafficlight check`** (ticket S0117) are `reads-user-data` and `leaves-machine`, both
  `coldStart: refuse`, bounds 5000 and 60000 ms. A target is selected by its **label** (the whole label, case-insensitive)
  and an answer carries labels, never addresses: a request that covers a target whose label is also its address
  (`TrafficLightLabels`) is refused whole - nothing is listed, no label is echoed - with the product's own reason
  `label-unsafe` (rule 12's last sentence), `refused`, exit 11, until the person gives that target a label of its own (an
  unfiltered `status` and `check --all` cover every target; `--target` covers the targets of that label). An answer that
  would pass the 64 KiB response line is `response-too-large` (exit 1), never a partial board. `status` starts no check;
  `check` asks the engine's own single-check path, joins a check already running instead of starting a second (a target
  whose check has reported but whose call is still out after a hard timeout is `busy` and named in the message, not
  joined), raises no balloon for a check no one but the command asked for, and answers 2 `timeout` when its checks do not
  finish inside the bound.
- **`note get` and `note export`** (ticket S0120) are `reads-user-data`, `coldStart: refuse`, bounds 10000 and 60000 ms.
  `note get` answers one note as it is stored - a text note as `text` verbatim, a checklist as `kind` and `items[]` with each
  item's `done` state - and, over the answer bound, cuts the content alone, surrogate-safe, telling the cut (`truncated`,
  `length` the full character count). `note export` writes every note to the caller-named file as a "CyrFlip Exchange Text v1"
  file (S0023's writer, Quick notes section only, newest first) through S0110's own `--out` discipline - item P paragraph 1's
  reading, with the file, not the picture, as the target: no folder is named or created, whole or not at all - and never trims
  to the importer's caps, which the manifest says; an id no note carries is 11 `target-missing`. The reasons it emits beyond
  the table are `write-failed` (exit 1) and `disk-full` (exit 13; rule 12's last sentence).
- **`clipboard-history list`, `clipboard-history put`, `clipboard-history pause`, `clipboard send --file` / `--text` and
  `clipboard get --out`** (ticket S0121, the rows C28, C29, C30, C32 and C33): `list`, `put` and `pause` are `coldStart: refuse`
  (`reads-user-data` 10 s, `writes-user-data` 5 s, `safe` 2 s - the S0113 spelling decision: `clipboard-history` is one word plus
  the verb), and the answer bound and list discipline are S0108's (48 KiB, `count` / `total` / `truncated`). The history is the
  most private store in the product, so the answer of `list` carries previews only - the strip's 1024-character one-line preview,
  the store's SHA-256 id, the pin, the copy date - never a whole text, never a source app, never a source window title, and no
  entry character reaches a message or the log. `put` is the strip's own restore (the own-write mark, the pin kept), `pause` is
  the one persisted state the tray item and the settings page own, applied through the tray item's own path; rule 13 gates the two
  store verbs as their class says and never the `safe` pause. `clipboard send --file` or `--text` (exactly one of the two; neither
  is the wave-1 verb unchanged) builds the module's own exchange payload instead of reading the clipboard - a missing file is 11
  `target-missing`, a file over the module's payload cap is 10 naming the cap and never the path, a share that does not answer is
  15 - and the answer is `accepted` with `kind` and `sizeBytes`, the account report staying in the module's own balloons.
  `clipboard get --out` is `wait: done` at a 300 s bound: the value is received straight into the file the caller names through
  S0110/S0120's own `--out` rules (`exists` 11 without `--overwrite`, a missing folder 11 never created, a refused name 10, disk
  full 13, whole or not at all, a share 15), the clipboard is not touched, and the answer carries `kind`, `sizeBytes` and
  `savedTo`. All five ride the sealed `DEVICE-EXCHANGE` frames and the existing pipe: no protocol byte changed.
- **`translate text`** (ticket S0128, the row C38; the adopted 0.3 rules read before the verb was added) is `leaves-machine` and
  `coldStart: refuse`, bound 130000 ms (`wait: done`, idempotent): the configured model endpoint may be another machine, so the
  caller's text may leave this one, and the service instance, the module state and the enabled check belong to the running copy.
  Text in, text out: no selection is taken, no window opens, the clipboard is never touched and no balloon is raised. `--text`
  is required; `--to` names a language code or `ui` (resolved against the running copy's own UI language), `--from` optionally
  names the source language in the prompt (a null keeps the chord's prompt byte-identical), and the token `active` is refused
  (10) - it names the window a selection is taken from, and a command reads no window. The input is cut at the service's own
  4000-character cap and the answer says so (`truncated`); one command translation runs at a time and a second answers `busy`
  with no queue. The reasons it emits beyond the table are the product's own `no-model` (rule 12's last sentence, 11 - a server
  that answers with no models installed, filed before the code that emits it, the `managed-by-windows` precedent), and it uses
  the contract's own `network` (15, the four server states each named in the message), `timeout` (2) and the base's tables. The
  module's own log gains the command line (the target code, the source mode, the outcome - never a character of the text);
  rule 16 keeps the base's line (the verb and the outcome only).
- **`selection convert`, `selection flip-case`, `selection note` and `selection translate`** (ticket S0129, the rows C34 to C37; the
  adopted 0.3 rules read before the verbs were added, item Q's first CyrFlip verbs): each carries the manifest member
  `target: "focused-window"` and acts on the selection of the window the caller names by `--window-pid` and/or
  `--window-title` (a substring, case-insensitive, the two given together narrowing each other - the one way to name one
  window of a program that has several; no open window matching is 11 `target-missing`, more than one 10 `bad-argument` with a
  message that says how to be more exact). The window is brought to the foreground first - the raise the context menu's focus
  hand-back uses - and one that will not take it answers the product's own reason `window-not-foreground` (rule 12's last
  sentence, 11, the person's one click named as the cure). The three local verbs are `writes-user-data`, `coldStart: refuse`,
  15000 ms; `selection translate` is `leaves-machine`, 130000 ms. `selection convert` takes `--from`/`--to` in `text convert`'s
  own grammar and the stored symbol preference; `selection flip-case` applies the stored CapsLock sync; `selection note`
  writes through the `note add` path and answers `noteId`; and `selection translate` captures, then rides the same no-sink
  service call `translate text` rides, with the shared one-at-a-time claim, the input cut at the service's cap answered
  (`truncated`), the same outcome table (`network` 15, `no-model` 11, 1, 2) - and on this one verb the `active` token is taken,
  resolved from the named window's live layout, the one referent that window gives it. An empty selection answers the product's
  own reason `no-selection` (rule 12's last sentence, 11, beside `window-not-foreground`); the clipboard claim is the flips' own
  `_busy` guard, taken before a window is touched; and rule 16 keeps its line (the verb and the outcome only - never a title
  part, never a selection character).
- **`keepawake set`, `theme set`, `autostart set` and `cursor restore`** (ticket S0125, the rows C41 to C44; the adopted
  0.3 rules read before the verbs were added): `keepawake set` and `theme set` are `safe` and `coldStart: refuse` -
  the live instance owns the execution-state request (it is bound to the tray's own thread) and the live re-apply of
  the theme on open windows. Both write through the same one-setter path the General page and the tray items own
  (never a second persisted path), and - item W, an answer that repeats what the caller's own command supplied does
  not raise the class - the answers repeat the switches they set and `theme` the stored token, so the class stays
  `safe`. `--minutes <1..480>` makes the keep-awake request session-only: the given switches are applied live,
  nothing is written, and the tray's own timer returns the saved state when it ends (a process death clears it the
  way the thread-bound request is cleared); a request that names no switch at all, or a bound outside 1..480, is 10.
  `autostart set` and `cursor restore` are `writes-user-data` and `coldStart: oneshot`: the rule-13 setting gates them
  (12) live and in the one-shot second process both. `autostart set` writes the Run key and the machine-local intent
  together through the page's own call, and answers 11 with the product's own reason `managed-by-windows` (rule 12's
  last sentence) on a packaged build, writing nothing - the page's own honesty, given a reason word. `cursor restore`
  reloads the whole default scheme through the exit path's own `SPI_SETCURSORS` seam, unconditionally (the kill
  scenario's fresh process has replaced nothing to guard on), idempotent and safe beside a live instance. The log
  line gains the closed state tokens of this group (`system`, `screen`, `theme`, `enabled`, `restored`); rule 16
  unchanged - `expiresUtc` and no argument text ever reach it.
- **No verb of this command set takes a secret**, so case C12 holds vacuously.
- **Not yet driven on a real build**: C3, C5, C6, C9 and the `start` half of C10 - `tools/uitest/Test-AppCommands.ps1`,
  a person at the desk.
- **Open against 0.3 (2026-11-19)**: item V.3 gives the reason `exists` the status `failed` and six answers still say `refused` (the exit code 11 is right); item X wants a line that is neither a legacy token nor a request answered `refused` / `bad-argument`, and `LauncherIpc` ignores an unknown line that does not start with `{`. A dated exception stands in the registry; the code follows the contract in a ticket, not in a catch-up pass.
