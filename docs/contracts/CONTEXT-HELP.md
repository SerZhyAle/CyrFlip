# CONTEXT-HELP

- **Version:** 0.1 (draft).
- **Home:** shared contracts catalog, `in-app-help/`.
- **Role:** consumer; in-app F1, help buttons, help map and offline pages.
- **Implementation:** `help-map.json`, `ContextHelp`, `ContextHelpUi`, `HelpElements`, and `tools/help/`.
- **Conformance:** partial. Static coverage and locale/link gates are implemented. The complete interactive walk, package/browser hand-off checks and editorial review remain release evidence; see the S0075 implementation record.
- **Verified:** 2026-10-16 (source and local build; no release certification).

## Synchronization read - 2026-10-18

The [current clause-to-surface audit](../design/contract-sync-20261017.md) records
the current catalog target, implementation changes, scoped exceptions and evidence.
Earlier version/evidence statements above are historical and do not certify the
new target. The [shared UI profile](../design/unified-ui.md) governs precedence
for covered components. Previous verified implementation levels stay unchanged
until their remaining evidence is recorded.
