---
name: release
description: Prepare and execute a CyrFlip РЕЛИЗ - the paid, outward-facing path. Write "What's new in version XXX" and propagate it to all four surfaces, run preflight, push the tag to trigger the signed GitHub build, then publish to winget, Microsoft Store, and the VS Code Marketplace via a checklist so nothing is forgotten. Use when the user asks to release, publish, cut a version, or ship.
---

# /release - РЕЛИЗ (publish a version)

A **РЕЛИЗ** is the paid, outward-facing path: it spends GitHub minutes (signing + packaging)
and publishes to external stores. Do it deliberately, never to "just test". The free,
iterative loop is `/build`. Concept reference: [RELEASE.md](../../../RELEASE.md).

This skill is the **order of work + the don't-lose-anything checklist**. `release.ps1`
automates the safe core (preflight, tag, push); everything external is driven from the
checklist below. **Do not skip steps** - a forgotten store or stale "What's new" is the
failure mode this exists to prevent.

## Phase 0 - Decide and gather

1. Confirm the user really wants a РЕЛИЗ (paid), not a `/build`.
2. Confirm the working tree is **clean** - all сборки committed. If not, finish `/build` first.
3. Decide the **version** `YY.M.D.HHmm` (default = now; `release.ps1` computes it). The tag is
   `vYY.M.D.HHmm`.
4. **Gather "What's new in version XXX"** - collect every user-facing change since the last
   release (read `git log <last-tag>..HEAD`, the `## Unreleased` notes in
   [vscode-extension/CHANGELOG.md](../../../vscode-extension/CHANGELOG.md), and anything `/build`
   captured). Draft 2-5 short bullets in **EN / RU / UK** (the project ships all three).

## Phase 1 - Write "What's new" to ALL surfaces (before tagging)

The same notes must land in **four** places. Missing one ships a release with stale notes:

- [ ] **GitHub Release** - `release.yml` sets `generate_release_notes: true` (auto from commits).
      Make sure the squashed commit history reads sensibly; you may edit the release body after.
- [ ] **Microsoft Store** - update the **"What's new in this version"** block in
      [msix/store-listing-export.csv](../../../msix/store-listing-export.csv), the source of truth for
      the listing, in **all 13 languages**. `msix/store-listings.md` and `store/listing-*.txt` are
      render targets: run `.\msix\render-listing-mirrors.ps1` and commit what it rewrites, never
      hand-edit the copy in them. The preflight runs the same script with `-Check` and fails on drift.
- [ ] **winget** - set/refresh `ReleaseNotes` (and `ReleaseNotesUrl` → the GitHub Release) in the
      locale manifests `winget/SerZhyAle.CyrFlip.locale.{en-US,ru-RU,uk-UA}.yaml`. Add the field
      if absent (schema 1.12.0 supports it).
- [ ] **VS Code extension** (only if `vscode-extension/` changed) - move the `## Unreleased`
      bullets under a new `## <ext-version>` heading in
      [vscode-extension/CHANGELOG.md](../../../vscode-extension/CHANGELOG.md), and bump `version`
      in `vscode-extension/package.json`.

Also update README EN/RU + the site under `docs/` (the EN, RU and UK landings, the EN/RU/UK guides and
the 10 other language first pages) if user-facing behaviour changed. Commit
these doc changes as a normal `/build` commit **first** (they ride to GitHub Pages on push).

- [ ] **Release facts on the site** - pin the version and render it into the 13 landings:
      `.\tools\site\Render-ReleaseFacts.ps1 -Version <ver>` (writes `tools/site/release-facts.json`, the
      version, its date and the versioned ZIP and `.sha256` links, and the landing JSON-LD
      `softwareVersion`). Commit it with the doc changes, then run every `release.ps1` call below with
      `-Version <ver>`. The preflight checks HEAD states that version; when it does not, it renders the facts
      into the working tree and fails, so a forgotten render costs one more preflight, never a release
      without the facts.

## Phase 2 - Preflight (free, no git changes, no GitHub minutes)

```powershell
.\release.ps1
```
Runs on-main + dirty-tree reporting, the full local build (whole solution, version-pinned) and every
test tier the release owns (`dotnet test` + `Assert-TestRun`), the IconGen build, the placement, help,
positioning, site-facts and release-facts gates, the listing-mirror and CSV drift checks
(`render-listing-mirrors.ps1 -Check`, `build-store-listing-csv.ps1 -Check`), computes the version, and
prints the checklist. Between releases only `.\build.ps1 -Fast` runs (S0081); this is where everything
slower runs. If anything
is red - fix it now, before spending a minute on GitHub.

## Phase 3 - Trigger the signed GitHub build (this spends minutes)

```powershell
.\release.ps1 -Push
```
Creates the empty `release: vX` anchor commit (no `[skip ci]`, so the tag's workflow runs; `ci.yml`
runs only on a manual dispatch, so the branch push bills nothing), tags `vX`, pushes
branch + tag. The tag fires `release.yml` → **signed** `CyrFlip-<ver>-windows-x64.zip` + `.sha256`
+ GitHub Release. Watch it (`gh run watch`) and confirm it is **green**. Copy the **ZIP asset URL**
and the **SHA256** from the run log - the next steps need them.

## Phase 4 - Publish externally (manual; from the printed checklist)

- [ ] **Site/docs** - GitHub Pages auto-deployed from `/docs` on the push. Verify it's live.
- [ ] **winget** (`SerZhyAle.CyrFlip`) - **not** `wingetcreate update`: that command rebuilds from the
      manifest already published in winget-pkgs and only bumps version/URL/hash, so this repo's
      `Description` / `ShortDescription` / `Tags` / `ReleaseNotes` never reach the store. Build from the
      templates instead: copy `winget/*.yaml` to a scratch dir, replace `__VERSION__` / `__URL__` /
      `__SHA256__`, point `ReleaseNotesUrl` at `/releases/tag/v<ver>`, then
      ```powershell
      winget validate --manifest <dir>     # yaml-only copy: validating winget/ itself
                                           # trips over README.md being parsed as YAML
      winget install  --manifest <dir>     # required by the PR checklist below
      wingetcreate submit --prtitle "SerZhyAle.CyrFlip version <ver>" --no-open `
        --token (gh auth token) <dir>
      ```
- [ ] **Fill in the winget PR body** - `wingetcreate` submits Microsoft's template *untouched*: an empty
      Description and every checklist box unticked, which reads as "nothing was verified" and stalls
      review. Write the description and tick each box **after actually doing it** (`gh pr edit <n>
      --repo microsoft/winget-pkgs --body-file <file>`), then re-read the body to confirm. Leave
      "Linked to an issue" unticked and say *not applicable*. Track the PR to merge.
- [ ] **Microsoft Store (MSIX)** - Store ID `9NB4W41NGQJ4`:
      ```powershell
      .\msix\build-msix.ps1 -ReleaseZip <downloaded-release-ZIP> -Version <ver>
      ```
      The Store identity (`SZA.CyrFlip` / `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD` / `SZA`) is read
      from `msix/identity.json` (the script stops without it), and the script refuses an exe whose
      `ProductVersion` is not `<ver>+<tag commit>`. The upload stays the Partner Center flow below. The
      Store machine channel (`msstore`, a service principal the owner configured) is read-only here:
      the preflight of `release.ps1` already read the live state before the tag (the canon's shared
      `Store-Preflight.ps1`, one `msstore submission get`; exit 1 stops the release with the reason, exit 2
      prints that the Store step is manual and changes nothing; one evidence line per run in
      `artifacts\store-channel-evidence.jsonl`; `-SkipStoreChannel` leaves it out). Never run a write verb,
      and never touch a submission made in the console - a pending console draft is why the preflight can
      say exit 1 (detail in `msix/README.md`).
      Upload the **unsigned** `.msix` (Microsoft re-signs). Partner Center → CyrFlip → **Create
      new submission** → replace the package → refresh all 13 listings + the **"What's new"**
      from `msix/store-listing-export.csv` → Submit. Detail: [msix/README.md](../../../msix/README.md),
      [STORE_PUBLISHING.md](../../../STORE_PUBLISHING.md). Certification takes a few business days.
- [ ] **VS Code Marketplace** (only if the extension changed) - in `vscode-extension/`:
      ```powershell
      npm install ; npm run compile ; npx @vscode/vsce publish
      ```
- [ ] **Smoke-test** once live: `winget install SerZhyAle.CyrFlip` and/or the Store install;
      confirm the tray app launches and the layout indicator works.

## Guardrails

- **Never** run `/release` to test something - that's `/build`. A release costs money and touches
  public stores.
- "What's new" goes to **all four** surfaces in the same version, or it isn't done.
- Don't tag from a commit carrying `[skip ci]` (the workflow would be skipped) - `release.ps1`
  handles this with the dedicated `release:` anchor commit. Don't hand-tag around it.

## Done means

The GitHub Release is green and signed; "What's new in vXXX" is consistent across GitHub /
Store / winget / (extension); winget PR opened, Store submission created, extension published
where applicable; and the published artefact smoke-tested.

The first-run welcome tour is also a ship-together surface: `WelcomeDialog` page 4 reads the live optional-module list. Keep its strings, README EN/RU/UK, guide EN/RU/UK, trilingual landing and ten standalone locale first pages, and all 13 Store listing sources in sync when that list or first-launch behavior changes (S0055).
