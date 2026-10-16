# S0075 implementation and evidence

2026-10-16. Contract: CONTEXT-HELP 0.1. Adoption remains partial until the release walk is complete.

## Decisions

- Every Form is enumerated by assembly reflection in `ContextHelpTests`; the standalone gate also reads
  the source inventory. Settings pages come from `SettingsPageKeys.DisplayOrder`; tour steps use `PageCount`.
- Permanent IDs and dispositions live in the embedded `help-map.json`. All settings pages, companion
  windows and nontrivial dialogs use pages. The two capture canvases and capture toolbar use a child layer,
  preserving the frozen capture and the active editor. Message/progress/tooltip surfaces have explicit reasons.
- All pages ship in all 13 locales. Names and element explanations reuse UI captions, accessible names,
  tooltips and settings hints. Longer generic guidance is in the help copy deck, registered into Localization.
- Browser pages and images are embedded resources, extracted asynchronously into a per-user versioned cache.
  A file URI carries anchors; only generated targets are accepted. Cache corruption is repaired. A help
  request never registers F1 globally; the hook explicitly passes bare F1 even with a hand-edited binding.
- The app's theme preference is a local stylesheet in the extracted copy; the site follows the system.
  No help page needs script or requests a remote resource. External links require a deliberate click.
- Screenshots use fresh in-memory configuration, a fake user registry and temporary stores. The renderer
  never reads a real account, note, clipboard entry or server configuration. The full help budget is
  **128 MiB uncompressed**, including both theme captures, all locales, copy and metadata.
- The help glyph is reserved for help; the replay-tour button uses the existing information glyph.
- There is no accepted-debt baseline. Static failures fail the normal build checks. Release evidence is
  kept honest rather than represented as a static PASS.

## Checks and release evidence

The final implementation report records the commands and results. `docs/help/captures.json` records the
surface, locale, theme and UI-derived marker bounds; per-locale capture metadata records build version and
date. `freshness.json` holds screenshot and rendering-input digests. These are automated capture evidence,
not a claim that a person pressed every key in every window.

Before release, record the interactive surface × locale × theme matrix for F1, the button, modal-dialog
retention, keyboard focus, capture-layer dismissal and screen readers. Verify browser fragment hand-off
and network-disabled reading from ZIP, the winget payload and an installed MSIX. The common dialog help
column preserves all existing commands; the before/after corner audit must also include narrow layouts.
The generated element explanations need owner editorial review, especially commands without an existing
hint. These checks are pending; no published site deploy or release was performed by this change.

## Run report, 2026-10-16 (the complete set)

The interrupted capture run left `zh` without pages and `bn`, `hi`, `ur`, `zh` without `index.html`, and
`finalize.py` had never run. Completed:

1. `dotnet build CyrFlip.sln -c Release` - 0 warnings, 0 errors.
2. `python tools/help/render-strings.py --check` - the copy deck matches `Localization.Help.cs`.
3. `powershell -File tools/help/Generate-Help.ps1 -Locale "hi,bn,ur,zh"` - all 13 locale folders now hold
   the same 129 files (559 pages, 1092 captures, 36.0 MiB, inside the 128 MiB budget).
4. `python tools/help/finalize.py` twice (after adding the translation-freshness digest refresh below) -
   cross-links, termbase `helpSurfaces`, landing/guide/README help links, sitemap, search-index,
   `DOCUMENT_REGISTRY`, `site-held-addresses`, `freshness.json`.
5. `dotnet test CyrFlip.sln -c Release --no-build` - Failed: 0, Passed: 2806, Skipped: 22 discovery
   artifacts, Total: 2828.
6. `tools/checks/Test-CheckPlacement.ps1` - placement-registry: PASS.
   `tools/help/Test-ContextHelp.ps1` - context-help: PASS.
   `tools/site/Test-Positioning.ps1` - positioning: PASS.
   `tools/site/Test-SiteFacts.ps1` (pwsh; the file needs PowerShell 7) - site-facts: PASS.

Three integration fixes this run: `finalize.py` now refreshes the `source-sha256` translation-freshness
meta of the ten standalone landings and the RU/UK guides after it edits their English sources;
`SettingsRowAnatomyTests` no longer holds the settings header's 44-px `contextHelp` button to the
32-px command-arrow metric (it is the help target, not a strip arrow); `ClipboardHistoryWindowTests`
expects the 44-logical-px header band (88 at the 192-DPI probe) the history window now paints for the
help button.
