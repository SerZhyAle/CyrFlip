# RELEASE-LEDGER

| | |
| --- | --- |
| **Id** | `RELEASE-LEDGER` |
| **Version** | 0.1 draft, read and adopted 2026-11-17 (`S0122`); the ledger stands with its history imported, the build and release hooks are written but not yet run by a real release - the next сборка and the next РЕЛИЗ prove them |
| **Role** | producer of the ledger files in `releases/`, written only through the canon's one writer |
| **Home** | shared contracts catalog, folder `release-ledger/`, document `README.md` |
| **Owner** | sza-unified-rules |

## What this repository must do to stay conformant

- **The ledger is `releases/` at the repository root**, named by `.sza-profile.json`
  `paths.releasesDir`: `docs/` is the published product site - GitHub Pages serves the whole folder - so the
  `docs/releases` default would publish the ledger to the web. In it: `channels.json` (declared by hand),
  `builds.json` and `history.jsonl` (machine-written, the history append-only) and `RELEASES.md` (rendered
  after every write, never edited). The folder is tracked in git.
- **Two packages, four channels.** `cyrflip` (the tray app - the release ZIP, winget, the Store MSIX - one
  `YY.M.D.HHmm` clock) ships to `github` (`SerZhyAle/CyrFlip`), `winget` (`SerZhyAle.CyrFlip`) and `msstore`
  (`9NB4W41NGQJ4`); `cyrflip-vscode` (the extension, its own three-part version clock, `vscode-extension/`)
  ships to `vscode` (`SerZhyAle.cyrflip-vscode`). `channels.json` declares every channel the canon stamp lists.
- **One door to the writer.** `tools/release-ledger.ps1` finds the shipped writer (`SZA_HARNESS_ROOT`, then
  the installed sza plugin) and adds `-RepoRoot`; this repository has no `tools/harness.ps1` forwarder.
- **Every successful build records itself as its last step** (rule 4): `build.ps1` (a сборка, with or without
  `-Tests`) records `cyrflip` **kind `test`** - nothing a local сборка makes is published, `release.yml` builds
  what ships; `msix\build-msix.ps1` records `cyrflip` **kind `release`, flavor `msix`** (a `-SelfSign`
  sideload is the same row with a note); `release.ps1 -Push` records **kind `release`** (the ZIP `release.yml`
  is building from the tag - an Actions build cannot write this ledger) and then the GitHub channel step. A
  ledger failure is one yellow line and never fails a build or a release.
- **Every channel step of a release records itself**: `release.ps1 -Push` writes the GitHub `submitted` event;
  the checklist it prints carries a ledger line per step - GitHub `live` when the run is green, winget
  `submitted` at the pull request and `probe` after the merge, msstore `submitted` at the Partner Center
  submission and `confirm ... live` after certification, `vscode` at the `vsce publish`. The written events
  stay in the working tree until the next сборка carries them (the release anchor is created from HEAD's
  tree, so a ledger write never rides into it).
- **The VS Code extension compile in the release preflight is not a build record**: the preflight builds in a
  temporary worktree that is removed, and the extension reaches its channel (`publish` on the Marketplace) by
  hand - the record is the checklist's `vscode` line.
- **No secret and no machine fact goes into the files** (rule 9): the Store id, the package identifier and the
  repository are public, artifacts are repository-relative paths.
- **One edge of the writer, carried here.** The msstore probe mis-reads the `msstore` CLI's answer when no
  pending submission exists: the CLI prints "found the **Last Published Submission**" with an ANSI colour
  sequence between "the" and "Last", the probe's no-live-submission regex fires (its rescue pattern does not
  match through the escape codes) and it reports "nothing published" although the last published submission
  is live. `releases/` was confirmed by hand for the Store from the same read-only
  `msstore submission get` (`26.1112.1652.0`, status Published); a canon fix is owed (canon ticket in the
  `S0092` family).

## Out of scope here

- The site's release facts (`tools/site/release-facts.json` and the 13 landings) keep rendering their own way
  (`PAGE-STYLE` 3.10, ticket `S0024` F16); whether a page draws its release link from this ledger is a later
  decision of the product-web-pages owner (contract section 5).
- This repository had no hand-run release-state file to retire onto the ledger; the preflight's
  Store-channel evidence (`artifacts\store-channel-evidence.jsonl`, `S0100`) stays as it is - it records
  preflight refusals, not publications.
