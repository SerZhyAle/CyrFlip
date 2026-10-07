# Pointer - `REPO-STAMP` and its family

| | |
| --- | --- |
| **Ids** | `REPO-STAMP`, `REPO-LAYOUT`, `RULE-DELIVERY`, `HARNESS-PROFILE` |
| **Version** | `REPO-STAMP` 0.12, `REPO-LAYOUT` 0.11, `RULE-DELIVERY` 0.12, `HARNESS-PROFILE` not bound (0.11 in the catalog) |
| **Home** | `rule-adoption/README.md` in the shared contracts catalog |
| **Owner** | sza-unified-rules |
| **Role here** | producer of the stamp and of the names tools address; consumer of the rule delivery; not bound by the harness profile |

## What this repository must do to stay conformant

- **`.sza-canon.json` is written only by the `sza:adopt-canon` run** (`REPO-STAMP` rule 8). When the canon
  moves and the digest does not, nothing is owed; the version is never bumped by hand.
- **A stale-digest warning is work owed** (`RULE-DELIVERY` rule 5): reconcile the changed rule documents,
  then let the run rewrite the version and digest. Never silence it.
- **The stamp must be true of the tree**: every value it declares (channels, ledger shape, tag prefixes)
  describes something that exists here.
- **Names tools address stay where they are** (`REPO-LAYOUT`): `CLAUDE.md`, `README.md` and `LICENSE` at
  the root, one pointer per contract in this folder with its index, `PLAN/done/` frozen. A spec-id scheme
  named in the rules file is allowed.
- **No `.sza-profile.json`** while no harness tool runs here (`HARNESS-PROFILE` does not bind). If one is
  ever adopted, the profile comes first - the harness defaults (`dev/CHANGELOG.md`, `PLAN/archive`) are
  wrong for this repository.
- **`.sza-canon.json` supports optional `canon.reconciledOn`** (`REPO-STAMP` 0.11); age counts from
  `reconciledOn` when present.
- **An unknown age is read as past the 180-day window** (`REPO-STAMP` rule 4, `RULE-DELIVERY` rule 5, 0.12).
  This binds readers only; CyrFlip is a stamp producer and implements no stamp reader or age logic. The stamp
  carries a parseable `adoptedOn` (first adoption, never moved by a re-sync) and `reconciledOn`.
- **The release package files are exempt from the type-prefix rule** (`REPO-LAYOUT` rule 3, third exception,
  0.11): `PLAN/RELEASE_QUEUE.md` and `PLAN/RELEASE_READY.md` are named in `CLAUDE.md`. No
  `RELEASE_QUEUE_DONE.md` is kept; shipped tickets move to `PLAN/done/`.

Note: `PLAN/` keeps its own ticket scheme (`Sxxxx_<slug>.md`), allowed by `REPO-LAYOUT` rule 3 (declared
spec-id scheme, 0.10). One file stays an ordinary rule 3 case that no exception names:
`PLAN/Contract_Conformance_Backlog_v0.1.md` (free-form name; dated exception in the registry).

Open: the stamp declares canon `2026.10.02.1` and its digest; the canon has moved since, so a reconciliation
by the adoption run is owed (`RULE-DELIVERY` rule 5, warning rung). Never advance the version by hand.
