---
paths:
  - "tests/**/*.cs"
  - "tools/uitest/*"
  - "tools/audit/*"
---

# Testing strategy and desktop-driving checks

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Testing strategy

- **Unit** (`tests/CyrFlip.Tests`, xUnit): `TransliterationEngine` (EN→RU, RU→EN, case, punctuation/digits pass-through, empty/null, bijection, and the two halves of a mixed string each flipping their own way), `KeyboardLayoutConverter` (`KeyboardLayoutConverterTests` - the per-character direction, the pair being symmetric, and the `ConvertSymbols` matrix; **every case is chosen to hold on both paths**, the live layout lookup where the Russian keyboard is installed and the `TransliterationEngine` fallback where it is not, so a clean CI runner proves the same thing this machine does), `CaseFlipEngine` (Latin/Cyrillic case inversion, the CapsLock fix, pass-through, self-inverse, empty/null, and `DesiredCapsLock` - the last cased letter deciding, trailing punctuation looked past, a caseless selection leaving the key alone, and the answer being the same however many times the correction is re-applied, which a blind toggle was not), `Hotkey` (modifier/key parsing, named keys, fallback, case-insensitivity, `CaseDefault`, `SameChord`, `NameForVk`), `LanguageHotkeys` (chord ↔ `MOD_*` mask round trip, the observed `0xC006`/`0x30` record from the registry, chord equality ignoring the side bits, slot allocation incl. a full range, toggle codes), `InputLayouts` (KLID→langid decode, `BuildPreload` device-handle scheme, `GroupByLanguageTag`), `KeepAwake` (mask composition per switch, idempotent Set, `Reset` → bare `ES_CONTINUOUS`; the P/Invoke is swapped for a recorder), and the conversion table's seeding/migration (`ConversionTableSeedTests`: a fresh install gets the EN⇄RU row on Ctrl+Shift+F12, an upgrade carries over the old chord *and* its off state, an already-empty table whose legacy values survive is still seeded, and a table emptied after the migration is never refilled). `DialogLayoutTests` builds both modal dialogs **in each of the 13 languages** and fails when any caption needs more room than its control got - with a companion fact proving the guard can fail, since a walk that inspects nothing passes just as quietly as a clean one. `DefaultLanguageTests` covers the fresh-install language pick (`Localization.MatchLanguage` takes the culture as an argument, so the whole matrix - all 13 languages, regional variants, untranslated languages, the invariant culture - is testable on any machine). The launcher: `LauncherScenarioStoreTests` (XML round trip incl. the `Hotkey` extension, an as-OneClickRunner-writes-it legacy file, corrupt-file isolation, contiguous ordering + move, delete-all-stays-empty, import = fresh Guid, seed-once), `LauncherMigrationTests` (run against the **byte-exact OneClickRunner fixtures** in `tests/CyrFlip.Tests/Fixtures/OneClickRunner/` - Guid preservation, collision → fresh id, `SPECIAL_YTDLP` → yt-dlp type, corrupt skipped by name, and a hash check proving the source directory is never modified), `LauncherExecutionTests` (target validation matrix, PATHEXT resolution, the `.ps1`/`.cmd` quoting shapes verbatim, yt-dlp link rejection, and the command-shape test proving a hostile `&`-bearing link reaches cmd only through the environment variable), `LauncherIpcAndJumpListTests` (strict command parsing - OneClickRunner's own `/run:` prefix is *not* ours, a live pipe round trip on a test-private pipe name that survives an empty connection, `BuildTasks` order + the Manage/Exit tail, no blank icon sources, hook binding snapshot ignores corrupt chords), and `LauncherTrayMenuTests` (**repeated** rebuilds - the first one can never fail, so only the second catches the dispose-while-enumerating bug; plus per-item closure capture, the empty-list shape, the conditional Import item, and the standalone `ContextMenuStrip` the taskbar button drops down, which is rebuilt on *every* click). `LauncherBrandTests` proves the embedded icon is actually reachable: a wrong `LogicalName` throws nothing, it just silently leaves both branded surfaces blank. The translator: `OllamaClientTests` (endpoint normalization, `/api/tags` parsing incl. every unreadable shape, one NDJSON stream stitched back together and reported chunk by chunk, the request body's model/prompt/`keep_alive`, a server error mid-stream, pull progress and the success/error lines - all through a fake `IOllamaTransport`, because **no test may reach a real Ollama**), `TranslationServiceTests` (prompt contents, the 4000-character cut, the echo/empty retry and that a bad retry keeps the first answer, model preference and tag matching, and the whole `TranslateAsync` matrix: healthy server, not installed, auto-start off, start failed, no models, HTTP failure), and `TranslationProfileTests` (the starter row offered once and never after, a row created chordless when the default chord is taken, the stored-table reader incl. id backfill and unreadable JSON, `[ScriptIgnore]` on the computed flag, the `ui`/`active` token resolution, that `AllCodes` really is every language Windows knows rather than the 13 interface ones, and the `ModelPageUrl` shapes - the size tag dropped, a `hf.co/..` name sent to the index). `LayoutColorsTests` is the cross-language guard: both tables the VS Code extension ships (languages and layouts) must match `LayoutStyle` entry for entry **in both directions**, every curated layout in `WorldLayouts.Popular` must have a colour and no colour may describe a layout nobody can pick, no two shades may be closer than a redmean distance of 40 (in the overlay's dot mode the colour *is* the marker), none may be dark, and `ColorForLayout` must walk its three rungs in order. `LayoutPublisherTests` pins what the extension reads, byte for byte: both file names, no BOM or newline, the empty sidecar, the folder per install mode, every code `CodeForLangId` can emit fitting the consumer's four-character prefix (Hawaiian is `HAW`), the last of 200 racing publishes winning, and the retract deleting only the producer's two files. `LayoutColorsTests` also proves each language colour is its primary layout's shade and every shade stays within 12 degrees of its language's hue; `ExtensionContractConstantsTests` reads the extension's signal timings and prefixes out of its TypeScript source (it has no test runner of its own); `TrustPageTests` guards the 13 trust pages (four sections in order, the quoted dialog, no "turn off SmartScreen", linked from every download surface); `NoTestRetryPackageIsReferenced` keeps the suite free of retry-to-green. `LayoutIdentityTests` pins the HKL → KLID decode against the shapes read off a live machine, incl. the three ways it refuses to guess (an unknown layout id, an IME handle, an id belonging to another language). `LayoutMarkerTests` covers the two things only pixels can answer: the letters leave **one pixel** of margin and no more than two along the axis that limits the fit (measured in pixels, not per cent - a 14px badge and a 32px one owe the same one pixel), and the badge is drawn translucent while the I-beam beside it stays fully opaque. `WorldLayoutsTests` additionally pins `CodeForLangId`, the single decode the marker and the settings tables now share (incl. a language Windows cannot name coming back as its hex id, not as `"??"`). The text context menu: `MouseChordTests` (every offered token
round-trips, a bare right click is refused while a bare middle click is not, and `Matches` demands
*exactly* the configured modifiers - Win never matches), `SelectionProbeTests` (a sighting with its text
ends the scan, the text may arrive from a later source than the verdict, an empty string is not text, a
throwing source is skipped rather than fatal, the class gate that keeps `EM_GETSEL` away from non-edit
windows, and the two counter-intuitive rules that each have a test of their own: **`Unknown` stays
`Unknown`**, and **no source may veto another's sighting** - the read-only-text bug, where the focused
element's confident "nothing here" greyed Copy beside a live selection under the pointer),
`LaunchTargetTests` (what "Запустить" will and will not open: the allowed schemes, a bare host needing a
known TLD so `readme.md` and `setup.exe` stay filenames, `javascript:`/`shell:`/`ms-settings:` refused by
name, a path accepted only when it exists, a network share or anything that runs code coming back as
`Program` for confirmation, the prose trimmed off a link, a multi-line selection refused, and the caption
elided in the middle so both ends survive) and `TextContextMenuTests` (the full item order, greyed-not-hidden without a
selection, everything enabled on `Unknown`, Paste following the clipboard rather than the selection,
disabled modules leaving no dangling separator, the launch item absent - not greyed - with no target and
taking its separator with it, per-row closure capture, and - as with the tray submenu -
**repeated** rebuilds, since the first can never fail). The log bundle: `SupportBundleTests` (the
clipboard-history exclusion **and** that its payload is nowhere in the archive - the test the whole file
exists for; an absent log skipped rather than fatal; the tail kept with a marker line and starting on a line
boundary; a log held open for writing still collected; the total budget dropping from the end and *saying so*
in the report; the archive name; retention keeping five) and `MailSenderTests` (every rung against every MAPI
code, `MAPI_USER_ABORT` **not** falling through, the RFC 2368 shape, an overlong body trimmed while the
subject survives, and that a non-ASCII path never reaches the ANSI API as-is). Long-run stability
(2026-07-30): `ClipboardHistoryOrderTests` - the incremental display order proven **equal to the full
reference sort it replaced**, by replaying a thousand random add/touch/pin/unpin/delete operations and
comparing after every one (the stream is weighted 3:2 towards adds on purpose: with adds and deletes
equally likely it is a symmetric random walk that hovers near an empty history and proves almost
nothing, and the test asserts every branch actually ran); `DiagnosticLogTests` (a file under the cap
untouched, the tail kept behind a marker on a line boundary, the dropped byte count honest, and **a log
held open for writing still rotated** - the case that failed the first implementation, which replaced
the file instead of rewriting it); `ClipboardBackupTests` (an image or a file selection with no text
still counts as content, and a clipboard whose text could not be read does not - restoring an empty
string would clear it rather than leave it be). The flip pipeline's hand-back (S0009), all over
`FakeClipboard` so no test ever opens the real clipboard: `ClipboardBackupReadTests` (a password
manager's markers go back with the secret, one that cannot be read goes back as DWORD 0 and an explicit
"yes" stays a yes, markers alone are not content, a Cut's `Preferred DropEffect` and `Shell IDList Array`
survive, `CF_LOCALE` is restored, the text byte for byte with its padding, one open at the content's
sequence number, locked-with-formats is unreadable while locked-and-empty is merely empty, and a flip
over an unreadable clipboard **sends not a single key**), `ClipboardRestoreTests` (the restore planner's
four answers, the receipt by sequence, the long waits for a remote target, the paste wait on a fake
clock - taken at 50 ms → restored at 150 ms, never taken → restored at the cap, an early render → the
fixed wait, a lost clipboard → at once - the real `PasteOffer` state machine driven by hand, which
markers each `TransientMarks` level puts up, and the busy notice's interval), `LineCopyMarkersTests`
(each editor's marker on a realistic Chromium pickle, a real VS Code selection accepted, another page's
custom data not mistaken for one) and `RemoteDesktopTests` (which targets are slow). The engines' S0009
cases sit in their own classes: the ambiguous-punctuation rule on both paths, the memo's exactness and
a million characters under a second, and the five letters without a case round trip. The clipboard history's file and gate (S0005):
`ClipboardHistoryJournalTests` (a half line between two good ones, an undecryptable payload and a
torn tail each cost only themselves and stay on disk; a file an earlier release wrote loads unchanged;
a thousand random add/touch/pin/delete snapshots through a writer that yields at random **replay equal
to the in-memory order**; add, add, Clear, add leaves exactly the last add; a clear that cannot delete
says so; `Dispose` writes all 50 records still queued) and `ClipboardHistoryGateTests` (an update inside
an operation or at its end mark skipped, a copy 100 ms later recorded, a five-second operation leaking
nothing, the counter wrapping, the own write skipped by its exact number so a copy after a paused
restore is recorded, every privacy marker and the DWORD ones only on 0, and the preview of a 64 K text
short and on one line). The quick notes: `QuickNoteTests` (a blank name is
`null` and not a caption, the preview derived and never stored, the search over all three fields,
both conversions **and what each of them reports as lost before it happens**, the export shapes, the
Keep text dropping the ticks, and the cap measured in UTF-8 bytes so Cyrillic fits half as much),
`QuickNotesOrderTests` (newest-first by creation with an edit never moving a note, the `Id`
tie-breaker giving the same answer whichever way the list arrives, and - the load-bearing one - the
incremental insert proven **equal to the reference sort** over a thousand random operations, with
the timestamps drawn from a deliberately narrow range so the tie-breaker actually runs, asserted),
`QuickNotesStoreTests` (replay of create/update/delete, a fragment of code back **byte for byte**
including mixed CRLF/LF, tabs and non-BMP text, a damaged line **between two good ones** skipped and
counted, a record that decrypts but is not a record skipped too, compaction leaving one record per
note plus a `.bak`, "delete everything" taking the `.bak` with it, and nothing readable written
beside the encrypted record - title included) and `QuickNotesServiceTests` (an empty draft never
created while an existing note the user emptied still is, the creation date assigned once, the
debounce flushed by `Dispose` rather than lost). `QuickNotesWindowTests` builds the real window in
**each of the 13 languages** and walks it for surviving Cyrillic - the window lives outside the settings control tree, so nothing else would catch a Cyrillic literal that bypassed the table in it. The theme (S0020):
`ThemeModeTests` (the token round trip incl. the registry, unknown -> `system`, the resolve matrix with
high contrast winning), `ThemePaletteTests` (every role in all three palettes, every text/surface pair at
4.5:1 - the ratios are printed - dark dark, light light, the marks distinguishable from body text, every
design colour read back to its role), **`ThemeCoverageTests`** (every window built and walked: dark leaves
no system colour and only palette colours of the right kind - a surface colour behind, a text colour in
front, since the dark text grey equals the light control grey by value; high contrast leaves only system
colours; light after both restores every recorded property; every `Form` is a `ThemedForm` or declared,
and every `ThemedForm` is built there; a late row arrives themed; and the guard fact that the walk can
fail), `ThemeSourceGateTests` (no colour literal outside the palette and the marker's files, one
`MessageBox.Show` left - `Program.Fatal`), `ThemeMenuTests` (the dark renderer's background and the three
caption colours) and, in `DialogLayoutTests`, `ConfirmDialog` in 13 languages x every button set plus the
fact that every dialog has an Escape and a destructive question answers No to Enter. The symbols window (S0056): `SymbolTableTests` (501 entries across 12 categories, uniqueness, invisible labels, combining marks), `SymbolSearchTests` (character, code points `U+20AC`/`20ac`/`0x20AC`, prefixes, standard order without relevance reordering, surrogate pairs), `SymbolRecentTests` (MRU deduplication, 24-cap, JSON sanitization, surrogate pairs), `SymbolTypabilityTests` (fake layout provider: plain key, Shift, AltGr, dead-key reporting as not typable, layout cache), `SymbolsClipboardTests` (exact text copy, taken `_busy` guard rejection), `SymbolsWindowTests` (13 languages, no surviving Cyrillic, accessible names, minimum size, `MA_NOACTIVATE`).

**A test that builds a `SettingsForm` or asks `LauncherBrand` for an image must join the
`SharedGdiCollection`** (`[Collection(SharedGdiCollection.Name)]`). xUnit gives each test class its
own thread and runs the classes in parallel, while `LauncherBrand` caches its icon/bitmap in plain
static dictionaries and hands out the **same instance** to everyone - correct for an app with one UI
thread, false in a test runner. Racing over it produced three unrelated-looking flakes with one
cause: GDI+ answering concurrent use of a single `Bitmap` with *"Object is currently in use
elsewhere"*, a `NullReferenceException` out of a `Dictionary` corrupted by concurrent writes, and a
settings window that came back half-built (an empty version label). The collection makes those
classes run one at a time; the fix is deliberately in the tests, since the app has no concurrency
here to fix. It reproduced in 5 of 8 full runs before, and 0 of 15 after.

**Verified live on 2026-07-25** (the tech plan's Фаза 3 gate - a build alone never proves this): with one scenario staged, the shell's own `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms` came back holding the task title, `/launcher-run:{guid}`, `/launcher-settings`, the exe path and the resolved icon path - i.e. every `IShellLinkW`/`IPropertyStore` slot landed where intended (a wrong vtable slot fails *silently*, so this is the only real proof). A Jump List click on a live instance dispatched over the pipe without creating a second process; with no instance it one-shot launched and left no process; switching the launcher off **deleted** that shell file and a stale task was then received but not executed. Two localization guards in `SettingsLocalizationTests`: every string registered in `Localization` has a non-empty translation in **all 13** languages, every dynamically built string is actually registered, and the live settings window is constructed **in each language** and walked for surviving Cyrillic (a Latin/Arabic/Indic/CJK build showing Cyrillic = a Russian literal that bypassed the table; "ЙЦУКЕН" is allowed - an untranslated label now reads English, which `LocalizationSourceGateTests` catches at the source: no Cyrillic literal outside the translation data, every literal of a translating call a registered key, every row with its `ru` and `uk`). The hook, clipboard flip, layout detection, and the live layout install/remove are interop-bound - verify those manually (see below).

**Translator verified live on 2026-07-26** against the real Ollama on the dev machine (`qwen2.5:3b`): probe, model listing and a streamed completion all came back through the production code path - RU→EN, EN→RU, DE→UK and a two-letter string that legitimately translates to itself. First call 26 s (the model being loaded), then ~300-400 ms, which is `keep_alive` doing its job. What that check does **not** cover, and still needs a human at the keyboard: the chord itself, the synthesized copy/paste, the popup's placement and focus behaviour, and the clipboard-history interaction (spec §12.2).
- **Desktop-driving checks** (`tools/uitest/`, PowerShell): the interop that xUnit cannot reach -
  `Test-TrayMouse.ps1` (single tray click advances the last active window's layout, double click
  opens Settings), **`Test-DarkTheme.ps1`** (the theme on the live app: starts it in dark and in light
  with `/launcher-settings`, so no mouse, captures the settings window - chosen pages via the saved
  `SettingsTab` - and judges each shot and its title bar by mean luminance; `-FollowWindows` flips
  Windows' own `AppsUseLightTheme` with the window open and broadcasts `ImmersiveColorSet`, restoring it
  in `finally`; `-TrayMenu` right-clicks the tray icon. `Audit-SettingsWindow.ps1 -Theme dark` renders
  every page off-screen in the theme, no mouse at all), **`Test-CapsSync.ps1`** (the "synchronize CapsLock" behaviour: `-InteropOnly`
  proves unattended that `GetKeyState` answers honestly on a **queue-less thread** - the clipboard
  worker is where CyrFlip reads it, and a stale reading would invert the result rather than skip it;
  the full run stages three scenes and asks a human for the chord, one of them being the
  already-off case a blind toggle got backwards, plus a fourth when "Press SHIFT to turn off Caps
  Lock" is set), **`Test-ClipboardFlip.ps1`** (the S0009 hand-back, `-InteropOnly` unattended:
  reflection into the built exe drives the real `ClipboardHandler` against a text box in its own
  process - the delay-rendered paste reaching the target with `CF_LOCALE` beside it, a whole case flip
  giving the user's clipboard back with its privacy markers, the chord with nothing selected not
  rewriting the clipboard at all, a Cut's move effect surviving. It **borrows the real clipboard**:
  it backs it up with CyrFlip's own backup first, refuses to start when it cannot, and resolves
  everything the hand-back needs *before* the first scene, so a scene that throws cannot take the way
  back with it - the first draft lost a clipboard exactly that way. Everything it stages is marked "do
  not record"; the rest is a checklist for RDP, Word, Excel, VS Code, KeePass, Explorer and an ANSI
  app), **`Test-QuickNotes.ps1`** (the quick notes: the
  two things the unit tests deliberately cannot reach - **real DPAPI** against the real journal,
  since they fake the cipher, and that journal surviving an actual restart - plus the checklist for
  the chord, the window and the context-menu capture. Like the support-bundle check it **never
  writes into the data file**: that file is the user's real notes, and a canary dropped into it
  would corrupt what the app reads back), `Test-SupportBundle.ps1` (waits for the log archive, then checks its contents,
  truncation markers and retention - whether a real mail client opened with the file attached is the
  one thing only a human can confirm), `Save-SettingsShots.ps1` (a PNG per settings tab, for
  layout/localization eyeballing) and **`Test-LongRun.ps1`** - the hours-long watch: GDI/USER handle
  counts, private bytes and threads sampled to CSV while a throwaway window's layout is switched to
  drive the icon/cursor/overlay rendering. It **fails on handle growth** (a leak there is invisible
  in the memory column) and reports private bytes without judging them, since the unbounded history
  is *meant* to grow. It deliberately never synthesizes a chord to prove the keyboard hook is alive:
  CyrFlip ignores injected keystrokes by design, so a synthetic `Ctrl+Shift+F10` would prove nothing
  - it asks a human to press one at the end instead, which is the only real check that Windows has
  not silently dropped the hook. Deliberately outside `dotnet test`/CI: they move the real mouse and steal focus.
  `CyrFlip.UiTest.psm1` holds the reusable seams (PerMonitorV2 DPI, MSAA tray-icon lookup,
  synthesized clicks, per-window HKL, `PrintWindow` screenshots, `GetGuiResources` handle counts,
  per-window layout switching) - **write new manual checks on top
  of it instead of hand-rolling P/Invoke in a scratch script**; `tools/uitest/README.md` lists the
  traps it already absorbs (UIA can't see the tray toolbar under pwsh 7, the settings caption is
  localized, the tray click is deferred by `DoubleClickTime`).
- **Integration** (planned): full hotkey→copy→transliterate→paste path across Notepad, Word, Chrome; live layout switching.
- **Manual** (planned): cursor visibility/non-obstruction; edge cases (multiline, selection at boundaries). Memory stability over 1+ hour is no longer on this list - it is `tools/uitest/Test-LongRun.ps1` above.
