---
paths:
  - "src/CyrFlip/TrafficLight*.cs"
  - "src/CyrFlip/CyrFlipContext.TrafficLight.cs"
  - "src/CyrFlip/SettingsForm.TrafficLight.cs"
---

# Traffic light (reachability board)

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

15. **The traffic light - "Светофор доступности", a board of last known reachability (ticket S0069), opt-in and off
   by default.** `EnableTrafficLight=0` means **no engine, no scheduler (so no timer and no network or power event
   subscription), no journal, no window object, no tray item and no chord** - a user who never switches it on pays
   nothing. A brand-new profile (no registry key yet, `AppConfig.LoadFrom`) starts the table with **one working example**,
   `TrafficLightTargetList.StarterTarget` - an `http` target `https://www.google.com`, labelled `Google` (not `google.com`:
   a label that reads as an address makes `trafficlight status` refuse the whole board, `TrafficLightLabels.IsSafe`); an
   existing profile never gets it, since the list is written on every save and an empty one there may be the user's own
   emptying. Enabled, the user lists **their own** targets - seven kinds: `ping` (one ICMP echo), `tcp` (one connect),
   `http` (HEAD, then GET headers-only on a 4xx/5xx **that is not already accepted**, sent to where HEAD's redirects
   ended; redirects up to 5; system proxy; no cookies, no credentials; the **final** status is judged against
   `acceptedStatusCodes`, default `200-399`), `folder` (one enumeration - exists and enumerable, **at most one entry
   read**, nothing written), `drive` (by **letter only**), `sftp` (TCP connect and the server's
   identification line - proves "an SSH server answers", not "this is the share") and `exchange` (TLS handshake only,
   SHA-256 of the leaf compared with an optional `fingerprint`; **no application byte is ever sent**) - and each is
   polled on its own interval (default 60 s, **floor 2 s - a typo guard, not a speed limit**, 0 = manual only). Six
   decisions carry it:
   - **The engine is pure** (`TrafficLightEngine`): a clock and the targets in, lights out. A tile keeps its last
     known state while a re-check is in flight and changes only on a **completed** check; a failure reaching
     `failuresBeforeDown` (default 1) turns it red, below that the tile keeps its state ("confirming k/N") and the next
     check is due after `min(interval, 10 s)`; one success clears the streak. The interval counts from the
     **completion** of the previous check, so checks never overlap. **Stale** is derived, not stored (3 intervals,
     manual-only 1 hour, or a network target while offline) and dims a tile without changing its colour. Results live
     in memory only. A result that comes back for a target whose identity changed meanwhile is dropped (`Generation`).
   - **One scheduler timer, bounded workers** (`TrafficLightScheduler`): a 1-second tick, network probes asynchronous
     and capped at **16 in flight** (a due target over the cap simply waits for the next tick - that is the cold-start
     stagger), `folder` and `drive` calls on **4 dedicated threads** created only once such a target exists. Those
     calls cannot be cancelled, so the hard timeout **abandons** them: the check completes as `timeout` at once while
     the worker and the target's slot stay occupied until the call really returns. Network events
     (`NetworkChange`, `PowerModeChanged`) are subscribed only between `Start` and `Stop`, debounced 2 s, and make
     every target due. While Windows reports no network the network kinds are **paused, not red** (`computer-offline`
     is a note, not a verdict) and no balloon fires.
   - **Recognition is pure and offline** (`TargetRecognizer`, D24): one text field, Enter adds with that kind's
     defaults, Shift+Enter opens the details panel first. `D:\` a drive; `\\HOST` a machine (`ping`, else `tcp` 445 -
     **the one fallback, no other port is ever tried**); `\\HOST\SHARE` / `//nas/x` a folder; `192,168,1,100` an IP
     (commas read as dots, said so on the status line); `google.com` a website; `host:port` a `tcp` target; `sftp://`
     links, `FMSEXCH1:` and `FMSSFTP1/2:` strings are read **by their contracts' vectors** (`ExchangeConnectionString`,
     `SftpDescriptor`; byte-exact copies under `tests/CyrFlip.Tests/Fixtures/`) and **every secret - `u`, `pw`, `fp`
     of a share, `sid`, a link's login - is dropped at parse time and the raw text is never stored or logged**. A
     `.fmscfg` document is refused unread. A broken contract string is refused **whole**.
   - **The board** (`TrafficLightWindow`, `TrafficLightBoard`, `TrafficLightTitleBar`): one non-activating,
     **borderless** tool window - `ShowWithoutActivation` and `WM_MOUSEACTIVATE`->`MA_NOACTIVATE`, so the mouse
     never steals the focus - opened by itself at every start while the module is on (`TrafficLightShowAtStart`, default
     on, `TrafficLightLaunch.ShowBoard`; off = only when it was left visible, `TrafficLightVisible`, and never focused),
     and **the moment the module is switched on in the settings** (ticket S0084): a change on the settings page that
     the board draws - the switch turned on, the pin, the tile size, the columns, the transparency, the targets - shows
     the board without the focus, **a board the user hid included**, and raises it above the settings window
     (`TrafficLightWindow.RaiseWithoutFocus`, the topmost/not-topmost pair, skipped while the board is topmost; position
     and pin untouched), while the chord, its switch and the startup preference leave it where it is
     (`TrafficLightPresentation`, held by a test that fails on an unclassified setting; the import and the chord
     reset never present it). An automatic reopen is recorded in `TrafficLightVisible` like any other show,
     pinned on top as a **switch** (`TrafficLightPinned`), **excluded from every capture and
     screen share** (`WDA_EXCLUDEFROMCAPTURE`, D9: it is a map of the user's network, so a balloon names the
     **label, never the address**). It has **its own title area** (`TrafficLightTitleBar`) that drags the window,
     as does any empty part of the board, and carries four **icon buttons** from the vocabulary: add a target
     (`action.add` - a small window for an address and a label, `TrafficLightQuickAddDialog`, the recognition live
     under it; every other option of a target stays on the settings page), edit (`action.edit` - the settings window,
     on this module's page), pin on top (`action.pin` / `action.pin--off`, a switch) and close (`nav.close`).
     **Its size is the grid's**: `TrafficLightColumns` (default 3, 1..12) tiles in a row and as many rows as the
     targets make - three columns and four to six targets are two rows at once - never more columns than the
     widest group has tiles; the window refits whenever the targets, the columns or the tile size change, stays on
     the screen, and only its **position** is remembered. With the plate transparency above zero, the board's words - the title, the status line, the group captions - keep a **solid underlay** of their own extent while the plate around them fades (S0093 Q5, `TrafficLightWindow.TextUnderlay`), the way the tiles and the title-bar glyphs stay solid. The small tile size is a **bare square**: no label, and
     "available" is plain green with **no mark** (a tick on green repeats the hue; owner, 2026-10-13), while every
     other state keeps its glyph - no mark is itself the reading, so the state still never rests on colour alone -
     and a switched-off target has a dashed edge (`TrafficLightBoard.MarkShown`); the name and the state are on
     hover - and on that step there is no status line ("all available: ..") and no title text, only the buttons. The **chord** (`Ctrl+Shift+Alt+T`) and the tray item take
     the focus onto it (arrows, Enter = check, F5 = all, Esc = hand the focus back, the Menu key = the context menu,
     which holds every button's command too): hidden - show and focus; visible and
     unfocused - focus; focused - hide. **A left click on a tile checks that target at once**, between its scheduled
  checks (`TrafficLightBoard.OnMouseClick`: press and release on the same tile; a drag of the microscopic board is
  not a click; a double click checks once and, on the microscopic board, also opens the settings; a check already
  running is not started a second time). The state is **never colour alone**: each tile carries the vocabulary glyph
     of its state (`status.ok`, `status.warning`, `status.error`, `status.unknown`, drawn by `GlyphRenderer` in
     `StatusInk`; a switched-off target has none, an invalid address shows `status.error` on the muted plate -
     `TrafficLightBoard.GlyphOf`; the typographic marks of `MarkOf` remain only for the settings table's text
     column and as the fallback of a glyph that cannot be drawn) and the tooltip and the accessible value carry
     the word. The hover tips of the tiles and of the title bar's buttons are the controls' **own tools**
     (`ToolTip.SetToolTip`, `ShowAlways`), never `ToolTip.Show(.., window, ..)`: that one answers only while the
     window is the active one, and the mouse never activates the board - it showed nothing until the chord had
     focused it (`Test-TrafficLight.ps1` hovers a real pointer over a board nobody focused). The fills are new theme roles (`StatusOkFill`, `StatusWarningFill`,
     `StatusErrorFill`, `StatusUnknownFill`, `StatusInk`), **not** the `success`/`warning`/`danger` ink roles.
   - **Balloons** are per target (`notify`, default off): only a **settled** change into or out of `down`, never a
     baseline (the first settled state after start), never while offline, coalesced to one per 15 s naming up to three
     labels; the tray item carries the summary ("недоступно: 1 из 12").
   - **The change journal** (`TrafficLightJournal`, per-target `recordHistory`, default off) is **plain unencrypted
     text** in `traffic-light-history.txt` of the data folder, written by one background writer in order, split at
     5 MB, never deleted automatically and **never in a support bundle, a settings export or a diagnostic log**
     (`SupportBundle.ExcludedFiles` holds four names now, the notes sync state being the fourth). Keys are invariant codes, so the file reads the same in
     every UI language. A swallowed fault anywhere in the feature goes to `traffic-light.log` by site and exception
     type only (`TrafficLightLog`, one line a minute per site).
   The list of targets is **opt-in on both ends of the settings file** (D10): an export asks before it includes it,
   an import takes it only on an explicit yes, and the module switch itself is never part of the slice (importing
   into an off module leaves it off). An imported or hand-edited address that fails validation is shown as an
   invalid tile and **never probed**. Spec: [PLAN/done/S0069_network-traffic-light.md](../../PLAN/done/S0069_network-traffic-light.md).
