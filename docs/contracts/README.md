# Contract pointers

Every file in this folder is a **pointer**, never a copy. The contracts themselves live in the shared
catalog, whose location is named in exactly one file in this repository - [`CLAUDE.md`](../../CLAUDE.md),
the line under the title. Everything else, here and in the source, cites a contract by its **id** and a
rule or section number: `LAYOUT-SIGNAL rule 2`, `LAYOUT-PALETTE rule 5`, `APP-ACTIVATION rule 3`, `CLIPBOARD-GUARD rule 4`. Never a
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
| [`SCENARIO-FILE.md`](SCENARIO-FILE.md) | `SCENARIO-FILE` | Producer and Consumer - launcher store, export and OneClickRunner import |
| [`DIAGNOSTIC-REPORT.md`](DIAGNOSTIC-REPORT.md) | `DIAGNOSTIC-REPORT` | Producer / Implementer - session log rotation, count-only environment report, support bundle ZIP |
| [`APP-BEHAVIOUR.md`](APP-BEHAVIOUR.md) | `APP-BEHAVIOUR` | Consumer - the shared desktop-app moments; partly conformant |
| [`APP-STYLE.md`](APP-STYLE.md) | `APP-STYLE` | Consumer - no theme yet; the layout marker is out of theme |
| [`ICON-SET.md`](ICON-SET.md) | `ICON-SET` | Consumer - glyphs and names map to the vocabulary; meanings it lacks are proposed |
| [`ICON-RENDER.md`](ICON-RENDER.md) | `ICON-RENDER` | Consumer - how glyphs are drawn and coloured; the layout badge is out |
| [`ICON-EXTERNAL.md`](ICON-EXTERNAL.md) | `ICON-EXTERNAL` | Consumer - launcher scenario icons and their fallbacks |
| [`INSTALL-TRUST.md`](INSTALL-TRUST.md) | `INSTALL-TRUST` | Producer - adopted: `docs/trust.html` in 13 languages |
| [`PAGE-CONTENT.md`](PAGE-CONTENT.md) | `PAGE-CONTENT` | Consumer - the landing page's content order and copy |
| [`PAGE-STYLE.md`](PAGE-STYLE.md) | `PAGE-STYLE` | Consumer, *App - small* - the vendored kit and the page scripts |
| [`SITE-FAMILY-MAP.md`](SITE-FAMILY-MAP.md) | `SITE-FAMILY-MAP` | Consumer and map row - the footer grid and the contact |
| [`CHECK-VERDICT.md`](CHECK-VERDICT.md) | `CHECK-VERDICT` | Producer of every gate and uitest verdict; consumer of child exit codes |
| [`CHECK-BASELINE.md`](CHECK-BASELINE.md) | `CHECK-BASELINE` | None - no accepted-debt file, declared |
| [`CHECK-PLACEMENT.md`](CHECK-PLACEMENT.md) | `CHECK-PLACEMENT` | Producer - where each check runs |
| [`BUILD-EVIDENCE.md`](BUILD-EVIDENCE.md) | `BUILD-EVIDENCE` | Producer - the release ZIP carries its build's version and commit |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-INTERNAL-QUALITY` | Consumer - internal docs; partly conformant |
| [`DOC-QUALITY.md`](DOC-QUALITY.md) | `DOC-EXTERNAL-QUALITY` | Consumer - the `docs/` site in 13 languages; partly conformant |
| [`UPDATE-MANIFEST.md`](UPDATE-MANIFEST.md) | `UPDATE-MANIFEST` | Not bound - CyrFlip has no update check |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `REPO-STAMP` | Producer - `.sza-canon.json`, written only by the adoption run |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `REPO-LAYOUT` | Producer - the names tools address |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `RULE-DELIVERY` | Consumer - the `sza` plugin |
| [`REPO-STAMP.md`](REPO-STAMP.md) | `HARNESS-PROFILE` | Not bound - no profile, no harness tool |
