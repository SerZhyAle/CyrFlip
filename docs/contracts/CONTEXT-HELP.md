# CONTEXT-HELP

- **Version:** 0.5, with 0.6 section 11 items Q and R adopted 2026-11-20 (CYRFLP-0139: the `help.head` picture before every title and the favicon, written by `tools/site/apply-identity.py` once `tools/IconGen` has run; the button's `ICON-SET` glyph before an element entry's name, written by the help generator at its next run). Earlier: re-read 2026-11-19, item P is a permission and owes nothing; re-read 2026-11-15, S0114.
- **Home:** shared contracts catalog, `in-app-help/`.
- **Role:** consumer; in-app F1, help buttons, help map and offline pages.
- **Implementation:** `help-map.json`, `ContextHelp`, `ContextHelpUi`, `HelpElements`, and `tools/help/`.
- **Conformance:** partial. Static coverage and locale/link gates are implemented. The complete interactive walk, package/browser hand-off checks and editorial review remain release evidence; see the S0075 implementation record. The captures are not regenerated for release v26.11.19.1230: 93 capture inputs changed since the last generator run (the finding `capture-stale`), waived for that release by the owner's order of 2026-11-19; the regeneration is deferred.
- **Verified:** 2026-11-15, S0114 (read against the tree, nothing run); 2026-10-16 (source and local build; no release certification).

## Synchronization read - 2026-10-18

The current clause-to-surface audit (ticket S0077) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The shared UI profile (ticket S0076) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.

## Rung 2 - 2026-10-29

Held in the static form the catalog allows from 0.2 (section 7 item D): `tools/help/Test-ContextHelp.ps1`
holds every capture to its digest in `docs/help/freshness.json`, and with `-Release` (the release preflight)
fails captures older than any input. The catalog registry row still reads `partial 0.1`; adopting 0.2 as a
whole (items A to J, among them help pages `noindex` and out of the sitemap) is the S0077 owner question.
