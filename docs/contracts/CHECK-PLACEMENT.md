# Pointer - `CHECK-PLACEMENT`

| | |
| --- | --- |
| **Id** | `CHECK-PLACEMENT` |
| **Version** | 0.11, draft (read 2026-10-07; 0.11 names the runner classes in rule 8 and makes the CI path filter part of the runner in rule 2) |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 4 |
| **Role here** | producer - placement declared in `tools/checks/check-placement.jsonl` (35 records), checked backward by `tools/checks/Test-CheckPlacement.ps1`; **partial against 0.11 (see below)** |

## What this repository must do to stay conformant

- **A check runs where its inputs change - within the 5 s line (S0081).** Between releases only the
  static gates of 5 seconds or less run (`build.ps1 -Fast`); the unit suite runs at the release
  (`release.ps1`, `release.yml`) and when the owner dispatches `ci.yml` by hand, so there is no CI path
  filter any more.
- **Each check has one declared runner class** - checked both ways (rule 2) by `Test-CheckPlacement.ps1`,
  executed from `build.ps1 -Fast` / `-Tests` and `release.ps1`.

## The repository's classes against rule 8's vocabulary

The registry keeps its own finer classes, which rule 8 allows if each maps onto the contract's:

| Registry class | Contract class (rule 8) |
| --- | --- |
| `per-change-local` (`build.ps1 -Fast`, run by the agent's `build` skill; `-Tests` is owner-named) | `agent-closure` |
| `ci` (`ci.yml`, a manual dispatch only since S0081) | `hand-run` |
| `release-preflight` (`release.ps1`), `release-tag` (`release.yml`) | `release` |
| `operator-typed` (`tools/uitest/`, `tools/site/Measure-SiteContracts.ps1`) | `hand-run` |

## Where this repository is behind (2026-10-07, not yet fixed)

- **Rule 8:** the mapping above is written only here; the registry file does not carry it, and it does not
  declare **per-change runner: none** with its reason (a solo developer on paid CI, whose build commits
  carry `[skip ci]`). `Test-CheckPlacement.ps1` also accepts only the five repository classes.
- **Rule 2, trigger filter:** resolved by S0081 (2026-10-29) - `ci.yml` has no push trigger and so no path
  filter; `DocumentationQualityTests` and `TrustPageTests` run with the whole suite at the release.
- **Rule 8, per-change runner:** since S0081 the repository's per-change runner is `build.ps1 -Fast` (the
  static gates, 5 s or less) and nothing runs the unit suite per change; the registry file still does not
  carry that declaration.
- The dated exceptions for these are in the catalog registry, not here.
