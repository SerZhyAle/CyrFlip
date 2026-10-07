# Contract pointers

Every file in this folder is a **pointer**, never a copy. The contracts themselves live in the shared
catalog, whose location is named in exactly one file in this repository - [`CLAUDE.md`](../../CLAUDE.md),
the line under the title. Everything else, here and in the source, cites a contract by its **id** and a
rule or section number: `LAYOUT-SIGNAL rule 2`, `LAYOUT-PALETTE rule 5`, `APP-ACTIVATION rule 3`, `CLIPBOARD-GUARD rule 4`, `INPUT-CHORD rule 6`. Never a
path, because whoever clones this repository does not have the drive the catalog sits on.

A pointer that grows a second page has become a copy. If this repository ever disagrees with a contract,
that difference is an amendment to write in the catalog or a dated exception to record in its registry -
not a local edit and not a local copy kept "in sync".

| Pointer | Id | This product's role |
| --- | --- | --- |
| [`LAYOUT-SIGNAL.md`](LAYOUT-SIGNAL.md) | `LAYOUT-SIGNAL` | Owner & Producer of layout files (`layout.txt`, `layout-klid.txt`), consumer of caret claim |
| [`LAYOUT-PALETTE.md`](LAYOUT-PALETTE.md) | `LAYOUT-PALETTE` | Owner & Producer (`LayoutStyle.cs`); VS Code extension ships copy |
| [`APP-ACTIVATION.md`](APP-ACTIVATION.md) | `APP-ACTIVATION` | Owner, Producer and Consumer - single instance mutex, Jump List IPC pipe, foreground activation |
| [`CLIPBOARD-GUARD.md`](CLIPBOARD-GUARD.md) | `CLIPBOARD-GUARD` | Owner, Producer and Consumer - Win32 pump-free clipboard access, atomic multi-format backup/restore, DPAPI isolation |
| [`INPUT-CHORD.md`](INPUT-CHORD.md) | `INPUT-CHORD` | Owner, Producer and Consumer - chord token, refusals, chord ownership, global hooks |
| [`INPUT-PARITY.md`](INPUT-PARITY.md) | `INPUT-PARITY` | Producer; steward of the keyboard and mouse columns of a shared device table; rules 1-2 held in the code since S0045, live walk owed |
| [`SCENARIO-FILE.md`](SCENARIO-FILE.md) | `SCENARIO-FILE` | Producer and Consumer - launcher store, export and OneClickRunner import; 0.11 read, rule 8 on a repeated migration is in the text (item H) and held |
| [`DIAGNOSTIC-REPORT.md`](DIAGNOSTIC-REPORT.md) | `DIAGNOSTIC-REPORT` | Producer / Implementer - session log rotation, count-only environment report, support bundle ZIP |
| [`APP-BEHAVIOUR.md`](APP-BEHAVIOUR.md) | `APP-BEHAVIOUR` | Consumer - the shared desktop-app moments; the tray tool windows are the rule-1 companion-surface class (0.11); every irreversible delete is confirmed, table rows included (rule 5) |
| [`APP-STYLE.md`](APP-STYLE.md) | `APP-STYLE` | Consumer - the app theme (sections 2-5, shipped since v26.10.1); the layout marker is out of theme |
| [`APP-SETTINGS.md`](APP-SETTINGS.md) | `APP-SETTINGS` | Owner, Producer and reference implementation - the persistent settings surface; 0.3 read 2026-10-07 - sections 1-8 held (S0048 walk 2026-10-06), section 9 (an irreversible operation on a page) held by reading the handlers, rung 1 (file and line pins) owed |
| [`WINDOWS-UI.md`](WINDOWS-UI.md) | `WINDOWS-UI` | Consumer - the shared Windows UI profile; sections 2-7 implemented 2026-10-06 (ticket `S0048`), section 8 not applicable; open items in the pointer |
| [`CAPTURE-OUTPUT.md`](CAPTURE-OUTPUT.md) | `CAPTURE-OUTPUT` | Producer of the `screenshot` kind - the region capture's file name, folder chain and PNG `tIME` chunk (rule 16, 0.5, held) |
| [`ICON-SET.md`](ICON-SET.md) | `ICON-SET` | Consumer - glyphs and names map to the vocabulary (all eleven tabs); two product-text decisions open |
| [`ICON-RENDER.md`](ICON-RENDER.md) | `ICON-RENDER` | Consumer - how glyphs are drawn and coloured; the layout badge is out |
| [`ICON-EXTERNAL.md`](ICON-EXTERNAL.md) | `ICON-EXTERNAL` | Consumer - launcher scenario icons and their fallbacks |
| [`WINDOWS-STORE.md`](WINDOWS-STORE.md) | `WINDOWS-STORE` | Producer - the MSIX and the Partner Center listing import; adopted 2026-10-03, the six exceptions closed the same day (ticket `S0047`); rules 15 and 18 still await their artifacts |
| [`INSTALL-TRUST.md`](INSTALL-TRUST.md) | `INSTALL-TRUST` | Producer - adopted: `docs/trust.html` in 13 languages |
| [`PAGE-CONTENT.md`](PAGE-CONTENT.md) | `PAGE-CONTENT` | Consumer - the landing page's content order and copy |
| [`PAGE-STYLE.md`](PAGE-STYLE.md) | `PAGE-STYLE` | Consumer, *App - small* - the vendored kit and the page scripts |
| [`SITE-FAMILY-MAP.md`](SITE-FAMILY-MAP.md) | `SITE-FAMILY-MAP` | Consumer and map row - the footer grid and the contact |
| [`SITE-STRUCTURE.md`](SITE-STRUCTURE.md) | `SITE-STRUCTURE` | Consumer, guide tier (opted in 2026-10-07, ticket `S0049`) - the page set, the address scheme, the locale sets; three gaps in `S0051` |
| [`SITE-EXPERIENCE.md`](SITE-EXPERIENCE.md) | `SITE-EXPERIENCE` | Consumer, guide tier - the kit, full width, shared language and theme, declared origins, keyboard and contrast; gaps in `S0052` and `S0053` |
| [`SITE-REPRESENTATION.md`](SITE-REPRESENTATION.md) | `SITE-REPRESENTATION` | Consumer, guide tier - positioning source, facts typed once, editions, the guide's anatomy; gaps and two owner decisions in `S0054` |
| [`CHECK-VERDICT.md`](CHECK-VERDICT.md) | `CHECK-VERDICT` | Producer of every gate and uitest verdict; consumer of child exit codes |
| [`CHECK-BASELINE.md`](CHECK-BASELINE.md) | `CHECK-BASELINE` | None - no accepted-debt file, declared |
| [`CHECK-PLACEMENT.md`](CHECK-PLACEMENT.md) | `CHECK-PLACEMENT` | Producer - where each check runs |
| [`BUILD-EVIDENCE.md`](BUILD-EVIDENCE.md) | `BUILD-EVIDENCE` | Producer - the release ZIP carries its build's version and commit |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-INTERNAL-QUALITY` | Consumer - internal docs; held (most rules gated), ticket `S0028` |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-EXTERNAL-QUALITY` | Consumer - the `docs/` site in 13 languages; held and gated, ticket `S0028` |
| [`UPDATE-MANIFEST.md`](UPDATE-MANIFEST.md) | `UPDATE-MANIFEST` | Not bound - CyrFlip has no update check |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `REPO-STAMP` | Producer - `.sza-canon.json`, written only by the adoption run |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `REPO-LAYOUT` | Producer - the names tools address |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `RULE-DELIVERY` | Consumer - the `sza` plugin |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `HARNESS-PROFILE` | Not bound - no profile, no harness tool |
