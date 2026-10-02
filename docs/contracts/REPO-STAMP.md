# Pointer - `REPO-STAMP` and its family

| | |
| --- | --- |
| **Ids** | `REPO-STAMP`, `REPO-LAYOUT`, `RULE-DELIVERY`, `HARNESS-PROFILE` |
| **Version** | `REPO-STAMP` 0.11, `REPO-LAYOUT` 0.10, `RULE-DELIVERY` 0.11, `HARNESS-PROFILE` not bound |
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

Note: `PLAN/` keeps its own ticket scheme (`Sxxxx_<slug>.md`), now explicitly allowed by `REPO-LAYOUT` 0.10.
