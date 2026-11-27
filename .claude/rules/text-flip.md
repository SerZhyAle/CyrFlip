---
paths:
  - "src/CyrFlip/ClipboardHandler.cs"
  - "src/CyrFlip/ClipboardOwner.cs"
  - "src/CyrFlip/ClipboardRestore.cs"
  - "src/CyrFlip/ClipboardFlipLog.cs"
  - "src/CyrFlip/ClipboardPrivacy.cs"
  - "src/CyrFlip/Win32Clipboard.cs"
  - "src/CyrFlip/LineCopyMarkers.cs"
  - "src/CyrFlip/RemoteDesktop.cs"
  - "src/CyrFlip/TransliterationEngine.cs"
  - "src/CyrFlip/CaseFlipEngine.cs"
  - "src/CyrFlip/KeyboardLayoutConverter.cs"
  - "src/CyrFlip/LayoutConversion*.cs"
---

# Layout conversion, case flip and the clipboard pipeline

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Features

2. **Secondary: one table of layout→layout conversions (open-ended, not a fixed option).** Any number of
   rows, each "from layout → to layout" with its own global chord - e.g. Ctrl+Shift+F12 = EN⇄RU,
   Alt+Shift+F12 = EN⇄UK, Ctrl+Alt+F12 = RU⇄UK. **This is the only home of these chords**: the original
   EN⇄RU flip is not a separate feature any more, it is an ordinary row seeded on first run
   (`AppConfig.NeedsFlipRow` / `FlipRow`, called from `Load()`; US `00000409` ⇄ Russian `00000419` on `AppConfig.DefaultFlipHotkey` =
   Ctrl+Shift+F12), editable and deletable like every other. An older config is migrated once: the old
   `Hotkey` / `EnableFlipHotkey` values become that row's chord and switch, then both registry values are
   deleted. **Those two values, not the emptiness of the table, are the migration marker**
   (`AppConfig.NeedsFlipRow`) - seeding fires when the table value is absent *or* when it is present but
   empty while the legacy values are still there (the config a build that had the table but not yet the
   merge left behind). Once they are gone a table the user empties stays empty.
   Rows live in `AppConfig.LayoutConversionProfiles` (`LayoutConversionProfile`, JSON in one registry value),
   are edited on the **"Исправление текста"** settings tab (S0062 - it was "Конвертация раскладок" until the case fix moved beside the table; the page key is still `SettingsPageKey.Conversions` and the Welcome tour keeps its own row caption), and are bound in the hook by
   `KeyboardHook.UpdateConversionProfiles` (an immutable snapshot array swapped atomically, so the hook never
   walks a list the UI is editing). The conversion carries **no alphabet table of its own**:
   `KeyboardLayoutConverter` asks Windows for both layouts' mappings, so it covers whatever the user has
   installed, and each pair is **bidirectional**: when the pair's target layout is already active,
   `ClipboardHandler.ConvertLayout` applies it in reverse (`KeyboardLayoutConverter.IsActiveLayout`), so one
   chord fixes text typed either way - hence the "⇄" in the UI. **The direction is then decided again per
   character**, not once per press: a character the source layout cannot produce at all was typed the other
   way round and is converted back, so `ghbdtnпривет` comes back as `приветghbdtn` instead of the doubled
   `приветпривет` a whole-string direction produced. Beside it sits the one part of the conversion that is a
   **preference rather than a fact**: a key that is punctuation in *both* layouts (`/` against `.`, `@`
   against `"`) carries no evidence of which layout the user meant - the numpad `/` is the same character
   everywhere and is indistinguishable once the text is on the clipboard - so `AppConfig.ConvertSymbols`
   (default **on**, i.e. the historic behaviour) decides whether it is swapped. A key whose other side is a
   **letter** (`,`→`б`, `[`→`х`) is never ambiguous and is converted whatever that switch says. The one
   exception is the **US ⇄ Russian pair**,
   which falls back to the hand-written `TransliterationEngine` table when Windows can't map the pair (the
   Russian keyboard was never installed) - CyrFlip's headline conversion has to keep working there, and it is
   the only pair we have a table for. `WorldLayouts.Popular` is the curated
   13-language starter set (the world's ten most-spoken - EN/ZH/HI/ES/FR/AR/BN/PT/RU/UR - plus DE, IT and UK, with
   the common variants per language) behind the "Add 10 popular languages" button, and `WorldLayouts.CodeForKlid`
   is the shared KLID → two-letter code used by the table and the tray tooltip.
3. **Case flip (fix CapsLock).** A second global hotkey (default **Ctrl+Shift+F11**) copies the selection, inverts each letter's case (UPPER↔lower, `CaseFlipEngine`), and pastes it back - the "I left CapsLock on" fix. Same copy→transform→paste pipeline as the flip; its own user-configurable hotkey, on the **"Исправление текста"** page beside the layout table (group "Регистр"). Optional **"Synchronize CapsLock after case correction"** toggle - in the same group - (the case-flip counterpart of "Change the language after the flip"): after a successful case flip it **sets** the physical CapsLock key to match the corrected text - on when it ends in a capital, off when it ends in a small letter (`CaseFlipEngine.DesiredCapsLock`, the **last cased letter** deciding because that is where typing continues) - so continued typing matches what is now on screen. It is an absolute state, not a blind toggle, and that is the whole point: a toggle is only right while CapsLock still holds the state that produced the wrong text, so correcting the same text twice, or correcting it after the user had already pressed CapsLock by hand, left the key exactly backwards. A selection with no cased letter at all (digits, punctuation, a caseless script) says nothing about CapsLock and leaves it alone.

## Modules (`src/CyrFlip/`)

- **RemoteDesktop.cs** - `IsClientForeground()`: true when the focused window belongs to a remote-desktop client (`mstsc`/`msrdc`/`msrdcw`), resolved via `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageName`. Used only by the hook's RDP-deferral path, and only on a real chord match, so the process lookup never runs on ordinary keystrokes. Beside it, **`IsSlowClipboardTarget(hwnd)`** - a wider, separate list (the RDP clients plus Citrix `wfica32`/`CDViewer`, Hyper-V `vmconnect`, VMware, VirtualBox, and the `VMware*`/`VirtualBox*` window classes): targets whose far side reads the clipboard late, which the flip pipeline gives its long waits (ticket S0009, FP-1). The deferral list stays RDP-only.

- **TransliterationEngine.cs** - `static`; two `Dictionary<char,char>` (EN→RU, RU→EN) covering the 34 letter *and* punctuation key positions. `Transliterate` auto-detects direction **per character**, so one pass fixes either direction and mixed text; case preserved; unmapped chars pass through. O(n). A **non-letter** is the one ambiguous case, and it follows **the converter's rule** (S0009 FP-9): `TransliterateFrom(input, fromLatin)` - what the `KeyboardLayoutConverter` fallback calls, since it knows which layout of the pair is the source - tries the pair's own direction first, per character, and the opposite one only when the first has nothing to say; `Transliterate(input)` without a pair picks that direction from the text's dominant script. The old dominant-script-only rule made `привет?` come back as `ghbdtn&` without the Russian keyboard and `ghbdtn,` with it. `convertSymbols` (from `AppConfig.ConvertSymbols`, default true) says whether a key that is punctuation on *both* sides is swapped at all - see feature 2.
- **CaseFlipEngine.cs** - `static`; `Flip` inverts each cased letter (UPPER↔lower) via invariant-culture mapping, leaving digits/punctuation/whitespace untouched. Works for Latin and Cyrillic; it is its own inverse for every character it changes - a letter is flipped only when its case partner maps back to it, so µ, ı, ς, ſ and the Kelvin sign K (whose partners come back as μ, i, σ, s and K) are left alone (S0009 FP-10). The transform behind the case-flip hotkey (undo an accidental CapsLock). O(n). Beside it, `DesiredCapsLock` answers **which state the CapsLock key should end up in** for a given text - the last cased letter decides, and `null` means "nothing cased here, leave the key alone". It lives here because it is a pure fact about text, which is what makes the whole "sync CapsLock" behaviour unit-testable without a keyboard.
- **ClipboardHandler.cs** - the selection transform pipeline: back up clipboard → clean synthesized Ctrl+C (`SendInput`, releasing held modifiers first) → poll for the selection → apply the transform → cancel if the foreground window changed → set clipboard → Ctrl+V → restore clipboard. The core `Run(transform, syncCapsAfter, targetKlid)` is shared: `ConvertLayout(profile, switchLayoutAfter)` passes the row's physical-key conversion (and, when the setting is on, switches the window to the layout the text now reads in), `FlipCase(syncCapsAfter)` passes `CaseFlipEngine.Flip` (never switches layout; optionally sets CapsLock to match the pasted text via `SetCapsLock`, which reads the live state and sends the synthesized `VK_CAPITAL` down+up **only when it differs** - Windows has no API that sets a lock state directly, and that comparison is what makes the call idempotent). Both post-actions are absolute: `ReplaceSelection` takes a `bool? capsAfter` (null = leave it alone) beside `targetKlid`, exactly as the layout switch names a layout rather than "the next one"; with "Press SHIFT to turn off Caps Lock" (`KLLF_SHIFTLOCK`) the CapsLock key cannot turn the lock off, so `SetCapsLock` re-reads it and taps a masked Shift (`KeyInjection.ShiftTap`) when it is still on - never while the user holds a Shift (S0009 FP-11). Clipboard ops retry up to 12 attempts, 15 ms apart on lock (`Win32Clipboard.cs:444`). Both halves are also available on their own - **`TakeSelection`** (backup, capture, hand the clipboard straight back - the translator's and the quick notes' phase A) and **`ReplaceSelection`** - because the translator needs seconds of network time between them and cannot hold the clipboard that long; `Run` is capture + replace inside one backup, so the flips keep their single-backup behaviour. **The backup covers three formats, not just text** (`CF_UNICODETEXT` + `CF_DIB` + `CF_HDROP`, handed back in one open/empty/refill pass via `Win32Clipboard.RestoreIf` - restoring them one at a time cannot work, since each write empties the clipboard again): the copy half ends in `EmptyClipboard`, so backing up text alone meant a user who had a screenshot or a set of copied files on the clipboard and then fixed a word with a chord **lost them for good**. `CF_DIB` is what covers images, because Windows synthesizes `CF_BITMAP`/`CF_DIBV5` from it; an image over `MaxBackupImageBytes` (64 MB) is skipped rather than carried through memory twice on every flip. **Ticket S0009 made the hand-back exact**, and each rule has its own test: the backup is read in **one** open behind the `IClipboardReader` seam (`FakeClipboard` in the tests), text as raw bytes, with its **companions** - a password manager's four "do not record" markers (a marker that is present but unreadable goes back as DWORD 0, never dropped: dropping it published the secret to Win+V), `Preferred DropEffect` + `Shell IDList Array` (a Cut of files stays a move) and `CF_LOCALE`; a clipboard that held formats but **could not be read** stops the flip with `Failed` before a key is sent (FP-5), where it used to be overwritten and never restored; the restore is decided **inside its own open** by `ClipboardRestore.Plan` - **not at all** when the sequence number never moved (FP-3: the chord with nothing selected no longer rewrites the clipboard, which had destroyed RTF, HTML and Excel's live cell copy), and not over a clipboard somebody wrote after our paste (FP-1); an editor's whole-line copy on an empty selection (`LineCopyMarkers`) counts as no selection (FP-4); a selection over `MaxFlipChars` (1 M) is refused unread with its own balloon (FP-6); the pasted text carries `CF_LOCALE` - the target layout's language for a conversion, the selection's own for a case flip, the translation's for a translation (FP-8). **Ticket S0032 closed four edges**: the backup's sequence number is the one read *after* its own reads and again after the close (FP2-1 - reading a delay-rendered format makes Excel render and move it, which turned "unchanged" into a rewrite of the live cell copy); only the **text** can make a backup unreadable, and so can text over `MaxBackupTextBytes` (64 MB) - a picture or file list that fails to render is logged and not carried (FP2-2); a clipboard that held **no format at all** is handed back empty (`ClipboardBackup.WasEmpty`/`Restorable`, FP2-4); `TakeSelection` takes the consumer's own cap (the quick notes' 512 K) and `RunEdit` never marshals the selection at all - a one-character cap with `presenceOnly` (FP2-5). **The paste is delay-rendered** (`ClipboardOwner`, below): the restore waits for the target's own read instead of a fixed 140 ms (FP-1B). Which modifiers the user is physically holding comes from the hook's table (`PhysicalModifiers.Shared`), not from `GetAsyncKeyState`, which our own key-ups falsify; every synthesized chord is planned by `KeyInjection` (side-specific keys, only what is held, the mask key) and the released keys are pressed again only while still physically held. Runs on the dedicated background clipboard thread (MTA - `Win32Clipboard` is raw Win32, not OLE, so no STA/message pump is needed).
- **ClipboardOwner.cs** - the STA thread with a message-only window that owns the clipboard while a
  flip's text is on it, so the text can be **delay-rendered** (ticket S0009, FP-1B): `Offer` announces
  CF_UNICODETEXT with no data (plus `CF_LOCALE` and the scaffolding's "do not record" markers,
  `TransientMarks`), and the target's read arrives as `WM_RENDERFORMAT` - the one reliable proof that it
  has taken the paste. A fixed 140 ms let a busy Word, a Java IDE and above all an RDP/Citrix/VM session
  read the clipboard *after* the restore and paste the user's previous clipboard over the selection.
  `PasteOffer` is one paste's state machine: a render **before** `Arm()` (i.e. before the Ctrl+V) was a
  clipboard monitor, not the target, and makes the target's read unobservable - the wait then falls
  back to the fixed settle time, and `RenderedBy` names the process in `clipboard-flip.log`. Remote/VM
  targets get `TransientMarks.HistoryOnly` - never `ExcludeClipboardContentFromMonitorProcessing`, since
  their clipboard bridge is itself a monitor. The restore judges "still ours" by **ownership**
  (`OwnsClipboard`: any other writer empties the clipboard, which takes it from this window), a plain
  write by its sequence number. A render counts as the paste only when the requester is the target's process or a remote/VM bridge (`PasteOffer.IsTargetRead`, S0032 FP2-3) - a clipboard manager that ignores the markers is an early render, i.e. the fixed wait; a failed owner start is retried after a back-off (`RetryDelayMs`, 30 s doubling to 30 min) and a window created too late stops itself (FP2-6). Everything degrades rather than fails: an owner that cannot start or
  cannot take the clipboard makes the caller write the text directly and wait the fixed time. Stopped by
  `ShutdownShared` in `CyrFlipContext.Dispose`, so a promised paste is rendered for good
  (`WM_RENDERALLFORMATS`). **Verified live 2026-09-25** (`tools/uitest/Test-ClipboardFlip.ps1`, inside an
  RDP session): the offer takes the clipboard, the target receives the delay-rendered text, `CF_LOCALE`
  sits beside it; on that machine a QEMU VM's clipboard sharing fetched every offer at once despite the
  markers, so there the flips used the fixed-wait fallback - which is the designed behaviour.
- **ClipboardRestore.cs** - the pure half of the hand-back: `ClipboardRestore.Plan` (Restore /
  Unchanged / TakenOver / NothingToRestore), `PasteTiming` (140 ms settle, 1500 ms for a slow target;
  2 s / 5 s caps; 480 / 1500 ms copy poll; 100 ms grace), `PasteWait.Run` (waits on an `IPasteSignal`
  against an injectable clock - consumed at 50 ms means a restore at ~150 ms, never consumed means the
  cap) and `BusyNotice` (the "another clipboard operation" balloon a flip now shows when `_busy` is
  taken, at most once per 5 s - it used to return in silence, like a dead hotkey).
- **LineCopyMarkers.cs** - "the editor copied the whole line because nothing was selected" (FP-4):
  `MSDEVLineSelect` (Scintilla, old VS), `VisualStudioEditorOperationsLineCutCopyClipboardTag` (VS 2010+)
  and VS Code's `vscode-editor-data` with `"isFromEmptySelection":true` inside Chromium's custom-data
  pickle (decoded as UTF-16 - every pickle field is 4-byte aligned, so no pickle parser is needed).
  Checked inside the capture's own open. **JetBrains is deliberately absent** until its marker is
  identified live with a clipboard viewer - a guessed name would never match or would match an ordinary copy.
- **ClipboardFlipLog.cs** - `clipboard-flip.log`, in the support bundle's whitelist: how the hand-back
  went (a paste not consumed within the cap, a restore skipped, a clipboard too busy to back up, which
  process rendered a paste early) - lengths and process names, never a character of the text.

- **KeyboardLayoutConverter.cs** - the engine behind the conversion table: converts a string **by physical key
  position** between two installed layouts, with no alphabet table of its own (except the US ⇄ Russian fallback
  described in feature 2). `VkKeyScanEx` (source layout) gives
  the VK+modifier state that produces the character; the VK is turned into a **scan code in the source layout**
  and read back as the **target layout's VK** (`MapVirtualKeyEx` both ways) - the step that makes AZERTY/QWERTZ
  correct, since French `a` sits on the US `q` key. `ToUnicodeEx` then renders it in the target layout. Two
  non-obvious rules: a negative `ToUnicodeEx` result means a **dead key latched inside the layout**, so it is
  flushed with Space (else the user's *next real keystroke* comes out composed), and the original character is
  kept whenever the result isn't exactly one char (IME/composed text is left alone). `TryConvertChar` answers
  **false** rather than echoing the character back, which is what lets `Convert` try the **opposite direction
  for that one character** and fix mixed text in a single press; it is also where `convertSymbols` refuses a
  key that is punctuation on both sides (feature 2). `Convert` **memoizes per distinct character** for the
  length of the call (S0009 FP-6): each character costs four P/Invokes and two allocations and text has a
  few dozen distinct ones, so a million characters convert in milliseconds; the memo is exact because a
  character's answer never depends on its neighbours (a latched dead key is flushed on the spot), which
  `TheMemoChangesNothingButTheCost` proves on a random corpus. `ResolveInstalled` is the
  only way KLIDs become HKLs here: `LoadKeyboardLayout` *adds* an unknown layout to the user's Windows input
  list, so anything not already installed is unloaded again and the conversion simply does nothing rather than
  silently reinstalling a layout the user removed. `LayoutSwitcher.SwitchTo` goes through the same resolver.
- **LayoutConversionProfile.cs** - one table row (`SourceKlid`, `TargetKlid`, `Hotkey`, `Enabled`, plus an `Id`
  that survives edits) and, beside it, `WorldLayouts`: the curated ten-language KLID set, `LabelForKlid`, and
  `CodeForKlid` (curated code, else the language id decoded through the OS - a non-curated layout still reads as
  e.g. "PL", never as a raw KLID). `LayoutConversionDialog.cs` edits a row and rejects source == target.

## End-to-end flow

End-to-end flow: hotkey (KeyboardHook, UI thread) → `OnLayoutConversionHotkeyPressed(id)`/`OnCaseHotkeyPressed` → `RunClipboardOp` spins a background thread → `ClipboardHandler.ConvertLayout` (copy → KeyboardLayoutConverter, or TransliterationEngine for an unresolvable US⇄RU pair → paste) or `ClipboardHandler.FlipCase` (copy → CaseFlipEngine → paste). CursorIndicator updates the tray icon independently on its timer.

## Critical detail: the transliteration table

Now a **fallback**, not the main path: the conversion table asks Windows for both layouts' mappings, and this
hand-written table is used only for the US ⇄ Russian row when those layouts can't be resolved (the Russian
keyboard was never installed). It is still the exact behaviour every pre-table release shipped, so it stays
correct. Bidirectional, based on the standard ЙЦУКЕН layout. The mapping used in `TransliterationEngine` (26↔26, aligned):

```
QWERTY:  q w e r t y u i o p a s d f g h j k l z x c v b n m
ЙЦУКЕН:  й ц у к е н г ш щ з ф ы в а п р о л д я ч с м и т ь
```

⚠️ **Spec discrepancy (§5.1):** the spec lists **28** Cyrillic letters for these **26** keys - it includes `ж` and `э`, which on ЙЦУКЕН are the `;` and `'` keys, *not* letter keys. Pasting the spec's two rows together mis-aligns everything from `z` onward (you'd wrongly get `z→ж`). The engine drops `ж`/`э` to keep the rows aligned (`z→я .. m→ь`) and has a static-ctor length assertion guarding against future drift. The punctuation row (`[ ] ; ' , . / \`` ⇄ `х ъ ж э б ю . ё`) and the shifted symbols (`{ } : " < > ? ~ @ # $ ^ & |`) **are** mapped - the table is 34 key positions, not 26 letters - which is why the two rows above are only the letter half of it.

Map case-insensitively but preserve case; pass through characters with no mapping (punctuation/digits) unchanged.
