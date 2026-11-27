---
paths:
  - "help-map.json"
  - "tools/help/*"
  - "DOCS_SURFACES.md"
  - "src/CyrFlip/ContextHelp*.cs"
  - "src/CyrFlip/HelpElements.cs"
---

# In-app help

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## In-app help (S0075)

`help-map.json` is the surface inventory and ships inside the exe. `ContextHelp` resolves both F1 and the
help buttons; bare F1 remains a window command. Add a map row whenever adding a Form, settings page or tour
step. `tools/help/Generate-Help.ps1` captures demo fixtures off screen in all UI languages and both themes;
`docs/help/` is the generated site set and the embedded offline set. `tools/help/Test-ContextHelp.ps1` checks
coverage, locale parity, page anatomy and local links, and holds every PNG to its digest in `docs/help/freshness.json`
(written by `tools/help/finalize.py`); with `-Release` - the preflight's call - it also fails captures older than any
input, i.e. any `src/CyrFlip/*.cs` changed since the last generator run (`CONTEXT-HELP` rung 2, static form). Between
releases that count is printed, not judged. Regenerate help with UI changes; do not hand-edit PNGs.
The 128 MiB uncompressed budget covers all help assets; the current implementation record is
`PLAN/done/CYRFLP-0075_in-app-help-f1-and-help-button/help-implementation.md`. The release-only walk is deliberately distinct from the static gate.
