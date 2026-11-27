---
paths:
  - "src/CyrFlip/Ollama*.cs"
  - "src/CyrFlip/Translation*.cs"
  - "src/CyrFlip/TranslateLog.cs"
---

# Translator on a local Ollama

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Feature

6. **Built-in translator on a local Ollama ("Перевод"), opt-in and off by default.** `EnableTranslate=0`
   means no chord is bound, no tray entry, and **CyrFlip opens no socket at all**. Enabled, it is a second
   open-ended table - rows of "translate the selection into this language, on this chord"
   (`TranslationProfile`, `AppConfig.TranslateProfiles`, bound by `KeyboardHook.UpdateTranslationProfiles`
   with the same immutable-snapshot discipline as the conversion table, and included in the RDP-deferral
   condition). A row's target is a language code or one of two live tokens resolved **when the chord
   fires**: `ui` (the CyrFlip UI language) and `active` (the layout live in the target window) -
   `TranslationLanguages.Resolve`. The answer streams into a popup **beside the mouse pointer**
   (`TranslationResultWindow`) that never steals focus, and can optionally be copied to the clipboard -
   where the history records it like any other copy, which is the point - or pasted over the selection.
   **The pipeline is three-phase on purpose** (spec §4): capture and delivery hold the existing `_busy`
   clipboard guard, the seconds-long model call runs **outside** it, so a translation never blocks the
   headline flip. Everything that owns the clipboard - the optional copy *and* the optional paste - runs
   on **one** guarded worker (`DeliverTranslation`): writing the result from the UI thread instead let a
   flip's copy poll pick the translation up as if it were the user's selection. Dismissing the popup
   (Esc, ×, the auto-close timer, switching windows) **cancels** a translation still in flight, and a
   result that arrives for a hidden window is dropped - otherwise an answer could paste itself into
   whatever the user moved on to. `TranslationService` owns the feature logic (prompt, one retry on an empty or echoed
   answer, model choice, the 4000-character cap), `OllamaClient` the protocol, `OllamaManager` the process.
   **An MSIX build never downloads or runs the Ollama installer** (`OllamaManager.CanInstallInPlace` -
   Store certification); it opens ollama.com instead. Ollama and the model are the user's own one-time
   install: CyrFlip bundles neither and has no key or account. Spec:
   [PLAN/done/Translator_Spec_Idea_v0.1.md](../../PLAN/done/Translator_Spec_Idea_v0.1.md).

## Modules (`src/CyrFlip/`)

- **OllamaManager.cs** - the *process* side of the translator: find `ollama.exe` (the usual install folders,
  then PATH), is it running, start it (`ollama app.exe`, else a hidden `ollama serve`), download and launch
  the official installer. `CanInstallInPlace` is `!PackageInfo.IsPackaged`: **a Store build never fetches or
  runs an installer**, its button opens ollama.com. **The installer is verified before it runs** (ticket
  S0010 TD-5): a random `%TEMP%\CyrFlip-ollama-<guid>\` folder, its length against `Content-Length`, and a
  valid Authenticode signature whose CN **and** O are `Ollama Inc.` (`AuthenticodeCheck`, `WinVerifyTrust`
  without an online revocation check); a failure deletes the file and opens ollama.com instead. The verified
  file is **held open read-only-shared** from before the signature check until the installer process exists
  (`InstallerDownload.Guard`, S0037 TR-4), its folder is removed once the installer exits and old ones
  whenever the translator page is shown (TR-5), and the download ends on the settings' **Cancel** button or
  after 60 s of silence (`CopyAsync`, TR-1 - net48's pending read ignores its token, so both dispose the
  stream on the pool; a stalled download used to disable every translator button until a restart). TLS goes
  through **`TlsPolicy.EnsureTls12`**, which adds TLS 1.2 **only when the value is not `SystemDefault`** - a
  net48 target already starts at `SystemDefault`, and `|= Tls12` there pinned the whole process to 1.2 and
  switched TLS 1.3 off (TD-4).
- **OllamaClient.cs** - the *protocol* side, behind `IOllamaTransport` so every parser above it is unit-tested
  without a server (`HttpOllamaTransport` is the real one: one shared `HttpClient`, per-request timeouts on a
  linked token, and a `CancellationTokenRegistration` that disposes the response stream because net48's
  `ReadLineAsync` takes no token - without it Esc would only be honoured after the model finished its
  sentence). That dispose is **queued to the pool, never run inline** (S0010 TD-3): `Cancel()` runs the
  registration on the UI thread, and closing a half-read chunked response on netfx **does** drain the rest of
  it - measured live on 2026-09-26 (`tools/uitest/Measure-TranslateCancel.ps1`, S0037 TR-7): an inline
  dispose took 504 ms, past the hooks' 300 ms timeout, while the app's `Cancel()` took 0 ms; the endpoint's
  `ConnectionLimit` is raised to 4 so a cancelled stream still generating cannot starve the
  next probe. `NormalizeBase` puts `http://` before an address without a scheme - `127.0.0.1:11434`,
  Ollama's own `OLLAMA_HOST` shape, used to be parsed as a scheme and reported as an unreachable remote
  server (TR-2). **The clock after the first line is silence, not a total** (TD-2): `PumpAsync` (the read loop,
  apart from HTTP so it is testable) re-arms a 30 s idle timer on every line, with a 10-minute ceiling for a
  runaway model; a deadline that fires after the first chunk makes `GenerateAsync` return the text so far
  with `LastStoppedEarly`, which becomes `TranslationResult.Partial` - shown with a note and never pasted
  automatically. `ProbeAsync`/`ListModelsAsync` (`GET /api/tags`), `PullModelAsync` (`POST /api/pull`, NDJSON
  progress), `GenerateAsync` (`POST /api/generate`, `stream:true`, chunks handed to a plain `Action<string>`
  on whatever thread the read completed on - **marshalling is the caller's job**, an `IProgress` created down
  here would capture the wrong context).
- **TranslationService.cs** - the feature logic: make sure the server is up (one auto-start attempt, ~8 s),
  resolve the model (the configured one if installed, else the best installed by `PickPreferredModel`), build
  the prompt, stream one completion, and judge the answer. The judgement is deliberately small (spec §5.3):
  an **empty or echoed** answer earns exactly one retry with a firmer prompt, and a bad retry keeps the first
  answer - a retry that times out included (S0037 TR-3). **An echo is accepted without a retry when the text already is in the target language** (S0010
  TD-7, `IsAcceptedEcho`): the source's dominant script is the target's and no letter contradicts it (`ы`
  is not Ukrainian, `ї` is not Russian); a retry replaces the first answer only when it is not a near-echo
  (95 % edit similarity) and is written in the target's script. **Only a loopback endpoint is auto-started**
  (TD-6): a remote one that does not answer is `Unreachable`, named by host; a cancel anywhere in
  `TranslateAsync` - probe, start, model list - returns `Cancelled`, not a logged failure.
  `MaxChars = 4000`; longer selections are cut and the window says so. `TranslationSink` is two
  callbacks (chunk / reset) rather than an `IProgress`, because a retry has to clear what the first attempt
  already painted.
- **TranslationProfile.cs** - one table row (`TargetLang`, `Hotkey`, `Enabled`, an `Id` that survives edits)
  and, beside it, `TranslationLanguages`: the two live tokens (`ui`, `active`), `Resolve` (token → concrete
  code at press time), `EnglishName` (what goes into the prompt - a curated table, else the OS name, and
  never Windows' literal "Unknown Language") and `Label` (the endonym the UI shows). **`AllCodes` is every
  language Windows knows**, and the pickers offer all of it. It used to be `Localization.Codes` - the 13
  *interface* languages - which fused two unrelated sets: measured on a live Ollama, that list was wrong in
  both directions, offering languages the default model garbles (Ukrainian, Hindi, Bengali, Urdu on
  `qwen2.5:3b`) and hiding Japanese, which it does perfectly. **CyrFlip claims nothing about coverage and
  blocks nothing:** there is no source to claim from (the GGUF `general.languages` key is null or absent on
  every model checked, and a model page sometimes gives a count with no list), and coverage belongs to the
  model, not to us - we dispatch, it translates. `ModelPageUrl` builds the link the UI offers instead
  (`qwen2.5:3b` → `ollama.com/library/qwen2.5`; a name with `/` came from outside the library, so it goes to
  the index). The browser makes that request, not the app - CyrFlip still opens no socket of its own.
- **TranslationResultWindow.cs** - the popup beside the mouse pointer: a `SizableToolWindow` (native close
  button and resize grip, no taskbar/Alt+Tab entry) with `ShowWithoutActivation` so it never steals focus,
  built from real controls in a `TableLayoutPanel` so `DialogLayoutTests` can prove nothing is clipped in 13
  languages. A window that never activates never gets `Deactivate`, so "close when the user moves on" is a
  400 ms poll of the foreground window, armed only **after** the answer is in - closing it mid-translation
  would throw away what the user is waiting for.
- **TranslationDialog.cs** - the row editor (language + chord), same auto-sizing `TableLayoutPanel` shape as
  `LayoutConversionDialog`.
