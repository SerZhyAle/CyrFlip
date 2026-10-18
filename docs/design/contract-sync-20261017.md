# CyrFlip contract synchronization, 2026-10-18

S0077 coordinates accepted proposals; [S0076's component migration](unified-ui.md)
owns the overlapping UI work. No release, Store submission, site deployment or
change to another product's implementation is part of this run. Existing consumer
implementation versions remain at their last measured level.

## Current read and scope

The current contract metadata, document logs, consolidation clauses, existing
pointer set and CyrFlip's registry rows were audited on 2026-10-18.
`tools/checks/Audit-ContractTargets.ps1 -CatalogRoot <catalog> -OutFile <report>`
reproduces the metadata/hash inventory without changing any stamp or adoption row.
The report includes every local pointer and every CyrFlip registry row, including
closed, partial, not-bound and exception records. It is evidence of a read, not
evidence that every rule is implemented.

| Contract read | Clause / actual surface | Outcome / reproducible check |
| --- | --- | --- |
| APP-ACTIVATION 0.13 | Consolidation, focus-only profile; `Program`, `LauncherActivation`, pipe/URI and clipboard commands | CyrFlip retains its payload-carrying named pipe and frozen identities; no new focus-only window-message profile is introduced. Existing activation/parser tests apply; hung/other-session runtime walk remains E2 |
| APP-STYLE 0.13 | Consolidation, five status roles; `ThemePalette`, traffic-light tiles | Existing four fills and one ink are now accepted roles. Values remain unchanged; `TrafficLightPaletteTests` and theme checks measure words/glyphs and contrast, including system high contrast. Pixel/device walk remains E1 |
| BOOLEAN-OPTION 2.0 | Sections 2-5; settings and editor booleans | Shared visible checkbox geometry, caption activation, accessible state and drag cancellation implemented for settings; full per-surface rollout and touch walk E1 |
| BUTTONS 2.0 | Sections 2-5; `ButtonGlyphs`, command panels and confirmations | Neutral dismissals and answer faces, 8-unit glyph gap, content wrapping and row-local height normalization; safe confirmation default retained. Mixed editor strips, toolbar semantics and remaining dialog panels E1 |
| COLLAPSIBLE-GROUP 1.1 | Sections 2-4; settings topics | Native vector current-state triangles, stronger headings, full-width bodies, independent state, retained values/focus and activation anchoring. Packing disabled by design. Full locale/touch walk E1 |
| SETTINGS-NAVIGATION 2.0 | Sections 2-5; settings/launcher, About | Existing task order, stable keys and monochrome glyphs retained. About now has downloads, support, resources, companions and standalone diagnostic export. Responsive rail and complete-frame performance E1 |
| UI-SIZING 1.0 | Sections 2-4; shared components/tool surfaces | Shared bounded token resolver composes DPI and distinguishes interface/text scale. Production selector/whole-product propagation and measured compact/touch support remain E1; no unmeasured profile is advertised |
| WINDOWS-UI 0.9 / APP-SETTINGS 0.7 | Current shared authorities and commit model | Reuse S0060/S0062 page shell, grouping, search, viewport persistence and immediate apply. Staged editors, consent, backups and ids remain intact. E1 owns presentation debt |
| CAPTURE-OUTPUT 0.6 | Consolidation, text without metadata; `ScreenshotSaver`, `PngTime` | Only PNG screenshot captures are produced here; their local-time filename/tIME behavior remains covered by capture tests. No text/OCR/translation capture writer exists, so the new plain-text metadata clause is not applicable |
| CHECK-VERDICT 0.13 | Consolidation, sections 2 and 10; module/TRX wrapper/build/preflight | Canonical `COULD NOT VERIFY`, explicit `NOT APPLICABLE`, `run=N skipped=M`, zero-run and skip refusal, failure precedence implemented in shared helper. Synthetic exit/last-line tests run in `Test-ContractSync.ps1`. Remaining outer-runner exceptional paths/child aggregation E3 |
| CONTEXT-HELP 0.1 | Sections 2-4; F1/button/map/offline corpus | Reuse S0075; no tour substituted for help. Source/locale/link gate passes; exact app.help Arabic/Bengali names corrected in the authored deck. Fresh screenshots, package handoff and full interaction walk E1/E2 |
| DEVICE-EXCHANGE 1.4 | Consolidation, 5.5 and native clipboard role; browser/admin appendices | 1.4 supersedes ticket target 1.3. Native clipboard-only client does not host the server, WebSocket UI or admin stream: browser origin/storage/operator checks are not applicable to this shipped role. Canonical newer-format/wire-version wording implemented; sealed/clipboard old/new server corpus replay and cross-product cleanup/half-close evidence E2 |
| DIAGNOSTIC-REPORT 0.14 | Consolidation and section 10; log sink/export | Existing omission marker is exact, redaction precedes truncation, oversized untrusted sources are omitted whole, account files/content excluded. `DiagnosticRedactorTests`/`SupportBundleTests` replay synthetic secrets and unsafe tails; new export reuses the same builder |
| DOC-INTERNAL-QUALITY 0.10 | Consolidation; tracked docs vs local engineering corpus | Published design/pointer docs registered with reverse coverage. PLAN remains a local engineering corpus; independent local engineering registry/reverse-link validation E3. No inaccessible PLAN index is published |
| DOC-EXTERNAL-QUALITY 0.12 | Consolidation; public locale URL/metadata corpus | Existing independent locale files remain valid; single-URL query consolidation and translated content freshness checks use site generators/tests. Full editorial/translation freshness remains E4 |
| HARNESS-PROFILE 0.12 | Consolidation; optional dev-log tools | Not applicable: no harness profile or harness dev-log writer is used. Existing ledger shape 4 and absent dev-log preserved; no foreign changelog is created or derived |
| ICON-RENDER 0.24 | Sections 14, 20, 21; shared renderer/headers | Button authority and native state indicators implemented; vector paths rendered at target size. Whole-product scale/input verification E1. Earlier Jump List decoration exception is unchanged, not renewed |
| ICON-SET 0.34 | Consolidation; app.help, action.paste, tour/reveal | 61 glyphs regenerated from current catalog, including paste inset, using the real vendor tool. Authored help names match the normative table. app.tour drawing filed by proposal; existing info mapping explicitly E5. Reveal's incomplete exported pair remains the existing mapping debt, no invented shape |
| INPUT-CHORD 0.4 / INPUT-PARITY 0.4 | Consolidation, product profiles; hooks/chord parser | New browser/viewer-specific action vocabulary does not turn CyrFlip into a viewer or steal new chords. Existing strict token/duplicate-trigger/refusal behavior retained and replayed; device/screen input walks E1 |
| PAGE-CONTENT 1.5 / PAGE-STYLE 3.1 | Consolidation items 1-6; public landing/child/help pages | Re-vendored current kit byte-identically for public site and offline help. Identified BUTTONS/group adapter in `style.css`; separate help source gate, rather than applying scripted landing rules to offline help. Theme icon/label, query migration, offline adapter and browser evidence E4 |
| SITE-FAMILY-MAP 1.5 | Consolidation items 2-3; footers/companions | CyrFlip role is App - medium. Landings retain sibling grid/contact; child pages retain current contact/hub. No media-exchange portal URL guessed; it is not a shipped destination here. Footer label/current-route audit E4 |
| SITE-STRUCTURE 0.3 / SITE-EXPERIENCE 0.4 | Consolidation; support page and release history | Existing published help/support route triggers conditional Support pages in core three locales and footer links; this work is E4. No new release-history renderer is introduced; landing's runtime latest-release link is preserved |
| UPDATE-MANIFEST 0.12 | Consolidation; release link only | Not applicable: CyrFlip ships no manifest producer/consumer or update installer; browser Releases links are explicitly outside the parser/cache/hash profile |
| WINDOWS-STORE 0.2 | Consolidation; listing builder | Existing 21-word cap retained. Added isolated overflow/no-write and BOM/no-BOM round trips; blank-cell bisection cannot generate an import. Existing no-BOM writer unchanged. Fresh live draft import/re-export requires the next authorized Store submission (E2); prior 2026-10-11 evidence is historical |
| SCENARIO-FILE 0.12 | Ownership amendment, section 7 G and section 9 H; launcher store | OneClickRunner is primary owner; CyrFlip is absorbing consumer. Real current-writer corpus regenerated through `ScenarioFileWriterVectorTests`; defaults, encoding, all legacy negatives, repeated migration and round trip checked. No owner repository edited or owner replay claimed; joint replay E2 |

## Compatibility audit

No stored key, credential, account id, program identity, artifact schema or user
preference default is renamed. Scenario XML is still generated by the existing
serializer, including Hotkey; all legacy fixtures stay in place. Known optional
names remain name-based, unknown additions are ignored, unknown scenario type and
malformed values are refused without a partial import, and missing scenario ids
are persisted in CyrFlip's own store before use. Missing producer rows never
delete the source or user-authored store. Descriptor/schema/size caps keep their
existing strict readers; unsupported UI scale values use ordinary metrics.

The exchange parser's `newer-major` reason is retained as the old generated
connection-string corpus vocabulary. At the user boundary it maps to the new
canonical local format meaning. `format-unsupported` is local; existing wire
`version-unsupported` remains wire, carries the same canonical text, and no
longer instructs an update. The old/new server corpora remain distinct.

## Explicit remaining outcomes

These are temporary migration/evidence exceptions owned by **CyrFlip**, reviewed
when their named proof passes, with a final deadline of **2026-10-31**. They add
no release permission and do not extend or replace older registry deadlines.

| Id | Exact scope and rationale | Impact and compensation | Remediation / review condition |
| --- | --- | --- | --- |
| E1 | S0076 component rollout, responsive rail, full matrix, real touch/accessibility and frame timings are incomplete; native tabstrip still caps captions | Presentation/accessibility debt; stored values and commit semantics preserved. Do not advertise compact/touch support or complete conformance. Keep existing keyboard/search/help routes | S0076 M2-M4; all applicable component/page checks, raw 20 cold +20 warm samples per destination, p95/max and named minimum/reference configurations |
| E2 | Fresh runtime activation, package/help handoff, native sealed clipboard transcript replay/joint cleanup, owner scenario replay and live Store draft re-export are not established by unit/source evidence | No protocol weakening, no clear fallback, no credential upload, no unattended Store action. Retain last verified implementation rows and historical evidence separately | S0077; actual native/client/server negative cases and package/Store/operator records before release |
| E3 | Outer build/release exceptional-path aggregation and separate local engineering document-registry validation still require completion | Some outer failures may lack canonical verdict; helper/TRX producer now fails closed. No false pass from missing or skipped tests; published registry covers only accessible docs | S0077; synthetic failure/skip/child-precedence tests for every outer path; independent local reverse coverage |
| E4 | Remaining public/help CSS, labelled catalog theme glyphs, canonical query precedence, Support pages/footer links and browser/editorial validation | Site remains local; existing links and content preserved. Kit byte provenance and dedicated offline-help source gate pass; no full PAGE-STYLE/site-tier claim | S0077 with S0051-S0054 and S0075 acceptance; browser/zoom/RTL/keyboard/contrast, core-locale Support and full source/locale checks |
| E5 | ICON-SET consolidation requires app.tour; catalog does not yet export its drawing. Existing replay command uses app.info while F1/help use app.help | No help/tour execution-path ambiguity; current tour command has a localized caption. Geometry is not invented or borrowed as a new meaning | S0077; proposal `PROPOSAL-2026-10-18-cyrflip-tour-glyph.md`, then generated provisional/accepted glyph and rendered check |

## Evidence commands and limits

Run `dotnet build CyrFlip.sln -c Release --no-restore`, the focused and UI regression
filters recorded in the execution log, `tools/checks/Test-ContractSync.ps1`,
`tools/checks/Test-CheckPlacement.ps1`, `tools/site/Test-SiteConformance.ps1`, and
`tools/help/Test-ContextHelp.ps1`. Run the writer test with
`CYRFLIP_WRITE_SCENARIO_VECTORS=1`; capture actual shared components with
`CYRFLIP_UI_EVIDENCE=<output folder>` and `UnifiedUiTests`.

Local artifacts are under `artifacts/s0076/` (not published): TRX, raw self-test
output, catalog/pointer/registry hash inventory, component PNGs and device/build
metadata. The fresh focused run passed **237 tests, 0 skipped**; isolated script
checks passed **28 checks, 0 skipped**. Component evidence is not the whole-product
walk or complete-frame timing evidence. A full suite attempt exposed outdated
header/answer/theme assertions and was stopped to fix them; it is not recorded as
a full-suite pass. Final regression outcomes are appended after verification.
