# Pointer - `CHECK-PLACEMENT`

| | |
| --- | --- |
| **Id** | `CHECK-PLACEMENT` |
| **Version** | 0.11, draft (read 2026-10-07; 0.11 names the runner classes in rule 8 and makes the CI path filter part of the runner in rule 2) |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 4 |
| **Role here** | producer - placement declared in `tools/checks/check-placement.jsonl` (35 records), checked backward by `tools/checks/Test-CheckPlacement.ps1`; **partial against 0.11 (see below)** |

## What this repository must do to stay conformant

- **A check runs where its inputs change.** The CI path filter re-includes the three extension files the
  suite reads (`layout-colors.json`, `extension.ts`, `package.json`), so a change to them runs the tests
  that guard them.
- **Each check has one declared runner class** - checked both ways (rule 2) by `Test-CheckPlacement.ps1`,
  executed from `build.ps1` and `release.ps1`.

## The repository's classes against rule 8's vocabulary

The registry keeps its own finer classes, which rule 8 allows if each maps onto the contract's:

| Registry class | Contract class (rule 8) |
| --- | --- |
| `per-change-local` (`build.ps1`, run by the agent's `build` skill) | `agent-closure` |
| `ci` (`ci.yml`, skipped by `[skip ci]` on every build commit) | `per-change` only for a push without that marker - see below |
| `release-preflight` (`release.ps1`), `release-tag` (`release.yml`) | `release` |
| `operator-typed` (`tools/uitest/`, `tools/site/Measure-SiteContracts.ps1`) | `hand-run` |

## Where this repository is behind (2026-10-07, not yet fixed)

- **Rule 8:** the mapping above is written only here; the registry file does not carry it, and it does not
  declare **per-change runner: none** with its reason (a solo developer on paid CI, whose build commits
  carry `[skip ci]`). `Test-CheckPlacement.ps1` also accepts only the five repository classes.
- **Rule 2, trigger filter:** `DocumentationQualityTests` and `TrustPageTests` are in the suite, which is declared
  `ci`, but read `**.md` and `docs/**`, which the filter excludes. Only `TrustPageTests` is named in the
  `ci.yml` comment. Both run in `build.ps1` and the release preflight, which is where a docs-only change is
  checked; the declaration should say so for both.
- The dated exceptions for these are in the catalog registry, not here.
