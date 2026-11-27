---
paths:
  - "PLAN/*.md"
  - ".sza-profile.json"
---

# Ticket store and the original specification

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Project status: `PLAN/`

- `PLAN/` - the ticket store (local-only, gitignored). One ticket = one spec file `PLAN/CYRFLP-####_<slug>.md` with
  `**Ticket:**`, `**Status:**` (canon vocabulary: Draft, Approved, Tactical, In Progress, Implemented, Verified,
  Partial, Block*) and a literal `**Blocked by:**` line; an optional `PLAN/CYRFLP-####_<slug>/` folder holds its
  `research/` and tactical `INDEX.md` + `PHASE_NN__*.md`. Order and package live in `PLAN/RELEASE_QUEUE.md`,
  finished work in `PLAN/RELEASE_READY.md`, closed ticket files in `PLAN/done/`. **Ids (2026-11-19, S0137, canon
  TASK-FLOW 0.37):** new tickets are `CYRFLP-0001`..`CYRFLP-9999` (`.sza-profile.json` `grammar.ticketIdPattern`); every
  ticket that was not Verified or Archived on that day was renamed `S####` -> `CYRFLP-####` with its number kept, the
  closed ones keep their `S####` ids (`grammar.legacyTicketIdPatterns`), and an old id stays a name of its ticket through
  the ledger field `formerIds`, so `S0105` in this file or in an older spec still finds `CYRFLP-0105`.

The spec is in Russian and lives in [PLAN/done/KeyboardTransliterator_Specification_v1.0.md](../../PLAN/done/KeyboardTransliterator_Specification_v1.0.md) (moved to `PLAN/done/` now that it's implemented). The Markdown file is the single source of truth; the original Word `.docx` was dropped (recoverable from git history if ever needed).
