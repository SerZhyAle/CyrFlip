# CyrFlip unified UI migration

Recorded 2026-10-18 for S0076. This is an implementation inventory and migration
record, not a declaration of portfolio conformance.

## Authority

Catalog text and document logs read on 2026-10-18: UI-SIZING 1.0 sections 2-4,
BUTTONS 2.0 sections 2-8, BOOLEAN-OPTION 2.0 sections 2-6,
COLLAPSIBLE-GROUP 1.1 sections 2-6, SETTINGS-NAVIGATION 2.0 sections 2-6,
WINDOWS-UI 0.9, APP-SETTINGS 0.7, ICON-RENDER 0.24 and PAGE-STYLE 3.1
(including its consolidation amendment). The ticket's PAGE-STYLE 3.0 is superseded.
Contract texts remain in the shared catalog; this repository retains pointers only.

## Reconciliation and milestones

S0060 owns the existing settings shell, search, reflow and remembered context.
S0062 owns stable destination ids and task order. S0075 owns surface F1/help,
the help map and offline authored content. Reuse these implementations and their
checks. S0076 changes component behavior under the new contracts without reopening
their product decisions or treating their outstanding acceptance walks as complete.

The ordered tactical migration starts 2026-10-18 and has a final deadline of
**2026-10-31**. This schedule does not change release packages or older exceptions.

| Milestone | Due | Deliverable and acceptance |
| --- | --- | --- |
| M0 | 2026-10-18 | Current catalog audit, surface inventory, profile decisions and registry migration reference, before component edits |
| M1 | 2026-10-20 | Shared answer palette, disclosure state geometry, boolean geometry and complete About resources/standalone diagnostic export; real-control regression checks |
| M2 | 2026-10-24 | Shared profile selection and compact-to-very-large scaling across settings and every tool/dialog; content-fit wrapped command rows with row-local height normalization; scrolling cancellation |
| M3 | 2026-10-27 | Scrollable responsive settings rail, complete-frame navigation instrumentation and stale-result checks; web adapter and companion host applicability checks |
| M4 | 2026-10-31 | Real captures, geometry/contrast/input/accessibility walks and raw activation-to-frame samples on named reference configurations; only then advance individually proven adoption rows |

## Inventory and reproducible checks

The source tree, rather than a screenshot, defines the inventory. `rg` expressions
below enumerate dynamic construction sites as well as statically declared controls.
All rows are applicable unless their narrower platform ownership is stated.

| Surface/component | Source inventory | Applicable contracts | Evidence and remaining work |
| --- | --- | --- | --- |
| Settings destinations | `SettingsPageKeys.DisplayOrder`, `SettingsForm.Page`, `ThemeTabControl` | SETTINGS-NAVIGATION, WINDOWS-UI, APP-SETTINGS, UI-SIZING | Existing page-key, search, edit-preservation, row and accessibility tests are regression checks; capped/ellipsized native strip and frame budgets require M3/M4 |
| Settings booleans | `SettingsForm.Check`, `CaptionedBoolean`, dynamic `RowCheckBox` sites | BOOLEAN-OPTION, UI-SIZING | Heading activation is native; descriptions have no click handler. Native checkbox square, wrapping and scroll/tap acceptance require shared geometry and M2/M4 |
| Settings topics | `SettingsForm.Group`, `FlattenSingleGroups`, `CollapsibleGroup` | COLLAPSIBLE-GROUP, UI-SIZING | Existing independent state/focus/value behavior retained; state-triangle and body geometry checked in M1; scrolling and viewport-anchor checks M2/M4 |
| Shared command/answer rendering | `ButtonGlyphs`, `ConfirmDialog`, `rg 'new Button|ButtonGlyphs' src/CyrFlip` | BUTTONS, ICON-RENDER, UI-SIZING, WINDOWS-UI | Neutral answers and semantic glyphs M1; every panel's wrapping, rank/behavior, widest-state reservation and keyboard/focus checks M2/M4 |
| Tool windows | `ClipboardHistoryWindow`, `ClipboardHistorySearchWindow`, `QuickNotesWindow`, `SymbolsWindow`, `TrafficLightWindow`, `TranslationWindow`, `ScreenshotToolbar`, `AnnotationEditor`, `TrayHotkeysPopup`, `TextContextMenu` | BUTTONS, UI-SIZING, ICON-RENDER, WINDOWS-UI where product-owned controls exist | Existing window suites retained. Custom-painted actions, toolbars, compact modes, large text, targets and RTL require per-surface M2/M4 evidence |
| Editors and dialogs | `rg 'class .*: ThemedForm' src/CyrFlip`, including launcher/scenario, exchange, target, hotkey, welcome, settings transfer/backup, support, confirm and busy dialogs | BUTTONS, BOOLEAN-OPTION where present, UI-SIZING, WINDOWS-UI | Preserve staged dialog commit, safe destructive defaults and consent. Dialog answers stay visible; per-panel migration M2 |
| Absorbed launcher | `SettingsForm.LauncherPage`, `LauncherScenarioDialog`, launcher command/dialog families | All Windows component contracts | Uses the settings shell and shared renderer; retain scenario ids, XML, original OneClickRunner files and opt-in module semantics |
| Tray menu | `CyrFlipContext`, `ToolStripMenuItem` sites | ICON-RENDER, WINDOWS-UI | Native menu geometry remains platform-owned; commands retain shared execution and accessible names; no settings-rail requirement for a transient command menu |
| Companion extension | `vscode-extension/package.json` configuration properties, `src/extension.ts` | BOOLEAN-OPTION/SETTINGS-NAVIGATION/UI-SIZING applicability limited by host | All three editors are rendered by VS Code's Settings UI, not an extension webview. Do not replace the host's settings navigation or native boolean renderer. Status bar/decorations remain product-owned under ICON-RENDER; verify host zoom/keyboard behavior in M4 |
| Official site | `.sza-canon.json` site roots, `docs/**/*.html`, `docs/style.css`, `docs/assets/sza-kit.css`, help corpus | PAGE-STYLE 3.1, BUTTONS, COLLAPSIBLE-GROUP, ICON-RENDER, UI-SIZING | Preserve kit provenance; identified adapter required for new geometry. Site-owned navigation is outside SETTINGS-NAVIGATION. Browser/zoom/long-locale/RTL/contrast checks M3/M4 |
| OS prompts/pickers | File/folder pickers, OS autostart settings, permission dialogs | Platform ownership under BUTTONS section 1 | Do not restyle system-owned controls; retain safe defaults and deliberate invocation |

## Permanent profile choices

Windows surfaces default to the stable pointer/keyboard profile at opening. No
mouse-move or touch event changes the selected profile. Monochrome navigation glyphs
are the coherent settings policy. The page order remains S0062's task order, with
General first (language/theme) and About last. No extra child destinations are needed.
Groups are independent, expanded on first use, subsequently remembered by stable id;
single-topic wrappers are removed. Packed headers are disabled: the existing pages
have short topic lists and normal-flow full-width headers preserve their reading order.
Markers are native vector filled triangles indicating state, mirrored in RTL.

Simple dialog answers may retain a semantic confirmation glyph; Cancel/No/Close
use neutral ink and faces. Substantive commands retain their vocabulary glyphs.
No automatic secondary overflow is selected: wrap commands and keep every dialog
answer visible. Diagnostic export collects local files only after activation and
does not send mail or upload; the existing mail-preview workflow remains separate.

Until M2 is measured, no new compact/touch or very-large profile is advertised as
supported or conformant. The target range to validate is 0.75-3.0 interface scale,
1.0-2.0 independent text scale; compact pointer target 24 logical units, compact
touch target 36, ordinary pointer/touch 32/48. Glyphs follow interface scale, not
text-only enlargement. OS DPI composes once. These are proposed product bounds,
not measured claims. Unknown profile/scale tokens must fall back to ordinary.

## Evidence policy

Actual real-control tests are recorded with each implementation slice. Required
M4 coverage: smallest/ordinary/largest, independent enlarged text, narrow/wide,
English/German/Arabic plus every shipped locale, light/dark/system high contrast,
100/150/200% DPI and risky combinations. Record build/device/runtime/locale/profile
and dimensions. Native pixel captures cannot substitute for interaction or trace
evidence. Test help versus toggle, scroll versus tap, repeated activation, disabled
paths, focus return, hidden validation, no operations on navigation and stale async
results. Report contrast for actual normal/hover/pressed/selected faces.

For each destination record at least 20 independent first visits and 20 warm visits,
activation through complete usable frame, all raw samples, nearest-rank p95 and max.
Warm p95 <150 ms, first p95 <500 ms; investigate outliers and fail repeated delays.
No such timings or full-corpus captures are claimed by this document.
