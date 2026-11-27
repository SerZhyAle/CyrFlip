---
paths:
  - "src/CyrFlip/AppCommand*.cs"
  - "src/CyrFlip/CyrFlipContext.AppCommands*.cs"
  - "src/CyrFlip/Localization.AppCommands*.cs"
  - "docs/cli-commands.json"
  - "docs/CLI.md"
  - "tools/cli/*.ps1"
---

# Command line (APP-COMMANDS)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

18. **The command line - commands from scripts, shortcuts and agents (ticket S0107, `APP-COMMANDS` 0.2, `APP-ACTIVATION` 0.15 section 11).**
   `CyrFlip.exe <verb> [options]` is handled by the copy that is already running: the started process decides its role
   before it loads anything (`Program.Main` parses first), carries the command over the existing pipe and exits with the
   answer's code. The verbs are the manifest `docs/cli-commands.json` (embedded byte for byte, `AppCommandManifest`;
   `docs/CLI.md` is rendered from it by `tools/cli/Render-CliDoc.ps1`, never by hand): `commands`, `version` (both answered
   by the process itself, `forward: never`), `status`, `show [--page]`, `quit`, `clipboard send`, `clipboard get`
   (`wait: accepted`), `scenario run (--id | --name) [--link]`, and the two quick-notes verbs of ticket S0108
   (`AppCommandsNotes`, handlers over `QuickNotesService` that never touch the window): `note add --text [--title]`
   (risk `safe`, `coldStart: start`; the text byte for byte; `QuickNotesService.TryAdd` lists the note, writes it and
   takes it out again when the write fails - never retried, so exit 1 `store-failed` leaves no note and no queued
   retry (the list-first order is the store's own contract with a background compaction); `module-off` 11 reads and
   creates no journal) and `note list [--query] [--limit 1..500]` (`reads-user-data`, `coldStart: refuse`; the window's own
   `Search`, `preview` = `QuickNote.BodyPreview(120)`, an unreadable journal is 1, never an empty list; the answer is
   cut at 48 KiB because the caller reads a response line of at most 64 KiB; the log line adds `count=n` and no text),
   and the two read verbs of ticket S0120: `note get --id <guid>` (`reads-user-data`, `refuse`; the whole note as stored -
   a text as `text` verbatim, a checklist as `kind` and `items[]` with each item's `done` state; over the 48 KiB answer bound
   the content alone is cut, surrogate-safe, and the cut is told (`truncated`, `length` the full character count); an id no
   note carries is 11 `target-missing`) and `note export --out <path> [--overwrite]` (`reads-user-data`, `refuse`, 60 s; the
   S0023 writer, Quick notes section only, newest first by creation, every note the store holds - never trimmed to the
   importer's caps (25 MB, 10,000 objects), which the manifest says; `--out` used exactly as given with S0110's exists /
   target-missing / 10 / 13 / 15 answers, the path checked before the journal is read, and the file whole or not at all
   (a temporary file in the target's folder, then a rename); its log line adds `count=n`),
   the three clipboard-history verbs and the two exchange option sets of ticket S0121
   (`AppCommandsClipboardHistory` and `AppCommandsClipboardExchange`, pure handlers over the strip's own
   `ClipboardHistoryService` and the module's own exchange seams; `clipboard-history list [--query] [--limit 1..50]
   [--pinned]` (`reads-user-data`, `refuse`, 10 s): previews only - the strip's 1024-char one-line preview, the store's
   SHA-256 id, the pin, the copy date; never the whole text, never a source app or source window title, the 48 KiB
   answer bound and `truncated` of S0108's list discipline; `clipboard-history put --id` (`writes-user-data`, `refuse`,
   5 s) puts the entry back exactly as the strip's restore does (the own-write mark, the pin kept) through the
   service's own path; `clipboard-history pause --paused <bool>` (`safe`, `refuse`, 2 s) sets the one persisted paused
   state through the tray item's own `CheckedChanged` path; and on the wave-1 exchange verbs `clipboard send
   --file <path> | --text <text>` (one of the two, 600 s bound; a missing file 11, over the payload cap 10 naming the
   cap, a share 15) and `clipboard get --out <path> [--overwrite]` (300 s, wait done: the value straight into the
   caller's file through the S0110/S0120 `--out` discipline, the clipboard untouched, nothing written on failure),
   and the five layout and text verbs of ticket S0109 (`AppCommandsLayoutText`, pure handlers over an `ILayoutCommandHost`
   seam - `IndicatorLayoutHost` in the app, a fake in the tests - and `LayoutCatalog`, the pure "which layout does this
   value mean" resolver): `layout list` (`safe`, `coldStart: oneshot`; `GetKeyboardLayoutList` in Windows' order, `current`
   on every object only in the running copy), `layout current` and `layout switch --to en|ru|<klid>|next` (`refuse`; they
   act on `CursorIndicator.CommandTargetWindow`, the tray's last-active window minus our own and the taskbar - from a
   console that is the console - and **activate nothing**; `switch` is `writes-user-data`, resolves over the installed
   list only and never calls `LoadKeyboardLayout`, a code takes the first layout of that code in Windows' order, a KLID is
   exact, a named-but-absent layout is 11 `target-missing`, a value that names nothing is 10, `next` is
   `LayoutSwitcher.NextAfter`, the tray click's own arithmetic; exit 0 means *requested*) and `text convert --text --from
   --to [--symbols true|false]` / `text flip-case --text` (`safe`, `oneshot`, run on the pool, no window and no clipboard;
   `--symbols` is true when absent and the stored setting is never read; a code no installed layout carries falls back to
   its curated primary KLID so the seeded US and Russian pair converts by table without the Russian keyboard; the same
   layout twice is 10; an answer that would pass one 64 KiB response line is refused `too-large`, since Latin letters
   come back as two-byte Cyrillic ones), and the two screenshot verbs of ticket S0110 (`AppCommandsScreenshots`,
   handlers over an `IScreenshotCommandHost` seam - the production host is a thin object in
   `CyrFlipContext.AppCommandsScreenshots.cs` over the chord's own capture core): `screenshot region --out` (the
   selection overlay over what the person is doing; `interactive`, 120 s; Escape is 16 `cancelled`; at the bound the
   instance closes the overlay itself and writes nothing) and `screenshot full --out [--delay-ms 0..30000]` (the delay
   holds no thread and the freeze instant is the capture, not the command). Both are `reads-user-data`,
   `coldStart: refuse`, take `--overwrite` and `--clipboard` (default **off**: a script that did not ask for the
   clipboard does not change it; on, it is written **after** the file is on disk, and a held clipboard after a
   saved file is 1 `clipboard-failed` with the file still named in `data`). **`--out` is used exactly as given**
   (`OutputPlan.Resolve`, pure over `IScreenshotFileSystem`; `APP-COMMANDS` 0.2 item P): a `.png` file name, an existing
   folder or a path ending in a separator - a folder gets the `CAPTURE-OUTPUT` rule 3 name, with rule 5's ordinal and
   `--overwrite` without effect; an existing file is 11 `exists` without `--overwrite`, a missing folder 11
   `target-missing` and is never created, a name Windows refuses 10, a full disk 13 `no-space`, any other write failure
   14 `io-error`, a share that does not answer 15 `network`. The target is checked before a person is bothered and again
   when the file is written (`ScreenshotSaver.SaveTo`: the temporary file renamed in, `MoveReplace` for an overwrite),
   always on the pool under the saver's 10 s budget; the PNG carries the `tIME` chunk of the freeze instant in both
   forms; the verbs write only the named target (no capture-folder save, no fallback, no balloon) and read neither
   `ScreenshotSaveEnabled` nor a chord switch. Since `CAPTURE-OUTPUT` 0.8 (2026-11-15, accepted from this
   product's own proposal) a caller-named path is outside rules 5 and 7, so the verbs conform and the dated exception is closed (S0114).
   The capture extras of ticket S0130 (`AppCommandsScreenshotExtras`, handlers over `IScreenshotExtrasCommandHost` - the
   same host object in `CyrFlipContext.AppCommandsScreenshots.cs`, now also over the window walk the selection family shares, a
   window-capture core and the command-side editor hand-off): `screenshot window (--window-pid <int>, --window-title <text>, or
   both narrowing) --out [--overwrite]` (`reads-user-data`, `refuse`, 30 s, not interactive; the window's visible on-screen
   rectangle - DWM extended frame bounds, `GetWindowRect` fallback - cropped out of the ONE frozen virtual screen: a covered
   window shows what covers it, a minimized or off-screen window is 11 `target-missing` naming the restore cure; the naming
   rides `AppCommandsSelection.Named` so the two families that name windows cannot drift; no `--clipboard` - the row lists
   none, the clipboard is never touched and `ScreenshotCount` rises never) and the `--annotate` option of `screenshot region`
   (the region entry's `timeoutMs` becomes the row's 600000; the module switch `EnableScreenshotAnnotate` gates the option -
   off is 11 `module-off`; the selection ends into the chord's own `AnnotationEditor` instead of delivering, OK saving the
   composed picture under the ORIGINAL freeze instant; Escape and the editor's Cancel are 16 and write nothing; `--clipboard`
   works on the composed picture as without; the selection keeps the wave-1's own 120-second budget - the dispatch arms it
   for the plain spelling, the annotate handler's `AwaitAnnotateRun` rations its selection phase the same way - and at either
   budget the instance closes the overlay or the editor on the UI thread so `CaptureBusy` clears and nothing is written after
   the answer).
   The two traffic light verbs of ticket S0117 (`AppCommandsTrafficLight`, handlers over an `ITrafficLightCommandHost` seam -
   `TrafficLightCommandHost` in the app over the live engine and scheduler, a fake in the tests - null while the module is off,
   11 `module-off`): `trafficlight status [--target]` (`reads-user-data`, `coldStart: refuse`, 5 s) reads the engine's last
   known states without starting a check, and `trafficlight check (--target | --all)` (`leaves-machine`, `refuse`, 60 s) asks
   for fresh looks and waits for them. **A label selects a target, and an answer carries labels, never addresses**: `--target`
   is the whole label, trimmed, case-insensitive; a target whose label is also its address, host, port, a segment of its path,
   its drive, a fingerprint or anything written like an IP, a URL or a path (`TrafficLightLabels.IsSafe`, strict and blunt by
   the owner's decision of 2026-11-17) refuses the **whole request that covers it** - an unfiltered `status`, `check --all` or a
   `--target` naming it alike: the product's own reason `label-unsafe`, exit 11, nothing listed and no word of the label in the
   message - until the person gives that target a label of its own; an answer that would pass the 64 KiB response line is
   `response-too-large` (exit 1), never a partial board. The answer's `state` is `ok`, `degraded`, `down`,
   `unknown`, `off` (switched off) or `invalid` (an address that does not validate); no reason, note or address is in it. The
   check is the engine's own single-check path with correlation added to it (`TrafficLightEngine.RequestCommandCheck` returns a
   `TrafficCheckTicket` that completes with that very check, `Complete` settles the waiters after the result is in; a check
   already running is joined, never started twice; a target that is switched off, invalid or - while offline - a network target is
   refused and named in the message, and so is one whose flight has already reported while its call is still out after a hard
   timeout (`TrafficCheckStart.Busy`, `Entry.FlightReported`: nothing is coming that a command could wait for); an edit, a
   removal, going offline or `Stop` lets a wait go as `Abandoned`), and **a check no
   one but the command asked for settles a `TrafficLightChange.Silent` change**: journalled and drawn like any other, but
   `TrafficLightNotifier.Counts` raises no balloon for it (so that transition is not announced later either). Past the
   bound the answer is 2 `timeout` and the waits are dropped; the checks themselves run on.
   The four state-switch verbs of ticket S0125 (`AppCommandsStateSwitches`, pure handlers over three host seams -
   `StateSwitchCommandsHost` in the context over the surfaces' own paths, `OneShotAutostartHost` in the runner, fakes in the
   tests): `keepawake set` (`safe`, `refuse`, 2000 ms; without `--minutes` each given switch goes through the tray items' own
   `CheckedChanged` paths - the live call, the config value, the save - so the items, the page and the answer hold one value;
   with `--minutes <1..480>` the request is session-only: the given switches applied live on the tray's thread, nothing
   written, one `Forms.Timer` on that thread returning the saved state, a request naming no switch 10, `expiresUtc` in the
   answer), `theme set` (`safe`, `refuse`, 3000 ms; the page's own choose path - token, save, live `SetMode` - and an
   unknown token is the parser's enum refusal, never folded to `system` the way a hand-edited config value is), `autostart
   set` (`writes-user-data`, `oneshot`, 5000 ms; the page's own call - Run key plus machine-local intent together, never one
   without the other; a packaged build is 11 with the product's own reason `managed-by-windows` and writes nothing) and
   `cursor restore` (`writes-user-data`, `oneshot`, 5000 ms; `LayoutCursor.ReloadScheme` - the exit path's own
   `SPI_SETCURSORS` seam unconditionally, without the replaced-guard, idempotent beside a live instance; the repair for a
   branded I-beam a killed run left behind). The `app-commands.log` line gains the closed state tokens (`system`, `screen`,
   `theme`, `enabled`, `restored`) beside the list counts; `expiresUtc` and no argument text reach it.
   The translator verb of ticket S0128 (`AppCommandsTranslate`, a pure handler over an `ITranslateCommandsHost` seam - the
   context's own adapter over the `_translation` service the chord already uses): `translate text --text <text> --to
   <language or ui> [--from <language or ui>]` (`leaves-machine`, `coldStart: refuse`, 130 s - the configured endpoint may be
   another machine; text in, text out: no selection is taken, no window opens, the clipboard is never touched and no balloon
   is raised, so the rule-13 setting answers 12 before the `module-off` check of `EnableTranslate` answers 11). The `active`
   token is refused 10 (it names the window a selection comes from, and a command reads no window); the input is cut at the
   service's own 4000-character cap and the answer says so (`truncated`); one command translation runs at a time and a second
   answers `busy` with no queue; the four server states answer 15 `network` each by name, a server that answers with no
   models installed answers the product's own reason `no-model` (11, rule 12's last sentence, beside `label-unsafe` and
   `managed-by-windows`), a model that answered nothing usable answers 1, and the bound's overrun answers 2 `timeout` with the
   model call cancelled through the family's `timeoutMs - 1000` token. The service gained a named-source overload
   (a null keeps the chord's prompt byte-identical), and `translate.log` gains the command line - the target code, the source
   mode and the outcome, never a character of the text.
   The selection family of ticket S0129 (`AppCommandsSelection`, pure handlers over an `ISelectionCommandsHost` seam - the
   context's `SelectionCommandsHost` adapter over the window list, the raise and the engines): `selection convert --from
   <code|klid> --to <code|klid>`, `selection flip-case`, `selection note` and `selection translate --to
   <language|ui|active>` (rows C34-C37, APP-COMMANDS item Q's `target: "focused-window"` manifest member), all
   `coldStart: refuse`, 15 s (13 s for the model call), `wait: done`. A verb acts on the selection of the window the caller
   names by `--window-pid` and/or `--window-title` (a substring, case-insensitive; no match 11 `target-missing`, more than
   one 10 `bad-argument`), brings it to the foreground through `ForegroundActivator` and refuses - 11, the product's own
   reason `window-not-foreground` - a window that will not take it; the two edit verbs ride the flip pipeline's own
   `ClipboardHandler.ConvertSelection` (explicit direction, never the chord's auto-reverse) and `FlipCase` with the stored
   `ConvertSymbols` / `FlipCapsLockAfter` preferences, `selection note` captures through `TakeSelection` and writes through
   the `note add` path (`TryAdd`), and `selection translate` captures, then rides the same no-sink service call as
   `translate text` with the shared model claim - the `active` token is honest on this verb and resolved from the named
   window's live layout. The clipboard claim is the flips' own `_busy` guard, taken before a window is touched and
   bracketing the history suppression; an empty selection answers the product's own reason `no-selection` (11, beside
   `window-not-foreground` in `AppCommandProtocol`); the capture caps are `MaxFlipChars` and the notes' own `QuickNote.MaxBytes`;
   and `translate.log` gains the `selection command:` line beside the chord's and the text verb's.
   Four rules carry it:
   - **One parser, strict** (`AppCommandLine`): an unknown verb, option or value, a request over 64 KiB or an argument
     over 32 KiB is exit 10 and nothing runs; the running copy reparses a request's `args` with the same parser.
   - **One request, one answer on one connection** (`LauncherIpc.SendRequest` / `Answer`, `AppCommandProtocol`): the
     pipe is read-write since S0107; a line that opens with `{` is a request, anything else is the old token and is
     answered with nothing. The UI thread has 800 ms to take a command or the answer is `busy` and the command is never
     run later (`CyrFlipContext.AppCommands`); a modal window or the first-run tour is `busy`.
   - **One setting, off** (`EnableExternalCommands`, General page, group `general/commands`): a verb of any class but
     `safe` is refused 12 `switch-off` while it is off; only the person turns it on - it is excluded from the settings
     file and the settings standard. The shipped spellings (`/clipboard-send`, `/launcher-run:{guid}`, ..) keep their
     token path and are gated by their module only (item F).
   - **The older copy is told apart, not guessed** (item K): a one-way pipe refuses the read-write open, the second
     process sends the legacy token where the verb has one and exits 2 with a sentence. No first instance: `coldStart`
     decides (`AppCommandRunner.ColdStart` - `start` starts the tray copy and forwards, `oneshot` works in the process
     after releasing the mutex, `refuse` is 11). An elevated first instance is `elevation` 12. Output goes to the
     parent's console (`AttachConsole`); the exit code and `--result <path>` (written whole) are the reliable channels.
   Every command taken is one line of `app-commands.log` (verb, origin, caller pid, outcome - no argument; in the log
   bundle). The cases that need a real tray copy are `tools/uitest/Test-AppCommands.ps1`.
   **The words that make it findable (ticket S0112, `Render-CliDoc.ps1 -Check` holds them):** the guides' `commands` chapter
   (EN, RU, UK; the manifest's four risk classes in plain words, the setting, the exit codes), one bullet or paragraph with a
   link to it on each of the 13 first pages, the privacy item "Commands from other programs" in all 13 languages (every risk
   class of the manifest as a `<code>` token, and the sentence that the shortcut spellings of before depend only on their own
   feature - canon invariant 14), the one line near the top of this file for development agents, and the in-app help, which is
   generated from the live General page (`help-map.json` has page rows only; the setting is a control of `settings.General`).
