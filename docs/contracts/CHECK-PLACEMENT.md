# Pointer - `CHECK-PLACEMENT`

| | |
| --- | --- |
| **Id** | `CHECK-PLACEMENT` |
| **Version** | 0.9, draft |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 4 |
| **Role here** | producer - **conformant (0.9)**: placement declared in `tools/checks/check-placement.jsonl` |

## What this repository must do to stay conformant

- **A check runs where its inputs change.** The CI path filter re-includes the three extension files the
  suite reads (`layout-colors.json`, `extension.ts`, `package.json`), so a change to them runs the tests
  that guard them.
- **Each check has one declared runner class** - per-change local (`build.ps1`), CI, release preflight
  (`release.ps1`), release tag (`release.yml`) or operator-typed (`tools/uitest`) - checked both ways.

Verified by `tools/checks/Test-CheckPlacement.ps1`, executed from `build.ps1` and `release.ps1`.