---
name: release
description: Prepare and execute a CyrFlip РЕЛИЗ — the paid, outward-facing path. Write "What's new in version XXX" and propagate it to all four surfaces, run preflight, push the tag to trigger the signed GitHub build, then publish to winget, Microsoft Store, and the VS Code Marketplace via a checklist so nothing is forgotten. Use when the user asks to release, publish, cut a version, or ship.
---

# /release — РЕЛИЗ (publish a version)

A **РЕЛИЗ** is the paid, outward-facing path: it spends GitHub minutes (signing + packaging)
and publishes to external stores. Do it deliberately, never to "just test". The free,
iterative loop is `/build`. Concept reference: [RELEASE.md](../../../RELEASE.md).

This skill is the **order of work + the don't-lose-anything checklist**. `release.ps1`
automates the safe core (preflight, tag, push); everything external is driven from the
checklist below. **Do not skip steps** — a forgotten store or stale "What's new" is the
failure mode this exists to prevent.

## Phase 0 — Decide and gather

1. Confirm the user really wants a РЕЛИЗ (paid), not a `/build`.
2. Confirm the working tree is **clean** — all сборки committed. If not, finish `/build` first.
3. Decide the **version** `YY.M.D.HHmm` (default = now; `release.ps1` computes it). The tag is
   `vYY.M.D.HHmm`.
4. **Gather "What's new in version XXX"** — collect every user-facing change since the last
   release (read `git log <last-tag>..HEAD`, the `## Unreleased` notes in
   [vscode-extension/CHANGELOG.md](../../../vscode-extension/CHANGELOG.md), and anything `/build`
   captured). Draft 2–5 short bullets in **EN / RU / UK** (the project ships all three).

## Phase 1 — Write "What's new" to ALL surfaces (before tagging)

The same notes must land in **four** places. Missing one ships a release with stale notes:

- [ ] **GitHub Release** — `release.yml` sets `generate_release_notes: true` (auto from commits).
      Make sure the squashed commit history reads sensibly; you may edit the release body after.
- [ ] **Microsoft Store** — update the **"What's new in this version"** block in
      [msix/store-listing-export.csv](../../../msix/store-listing-export.csv), the source of truth for
      the listing, in **all 13 languages**. `msix/store-listings.md` and `store/listing-*.txt` are
      render targets - regenerate them from the CSV, never hand-edit them.
- [ ] **winget** — set/refresh `ReleaseNotes` (and `ReleaseNotesUrl` → the GitHub Release) in the
      locale manifests `winget/SerZhyAle.CyrFlip.locale.{en-US,ru-RU,uk-UA}.yaml`. Add the field
      if absent (schema 1.12.0 supports it).
- [ ] **VS Code extension** (only if `vscode-extension/` changed) — move the `## Unreleased`
      bullets under a new `## <ext-version>` heading in
      [vscode-extension/CHANGELOG.md](../../../vscode-extension/CHANGELOG.md), and bump `version`
      in `vscode-extension/package.json`.

Also update README EN/RU + the site under `docs/` (the trilingual root page, the EN/RU/UK guides and
the 13 language first pages) if user-facing behaviour changed. Commit
these doc changes as a normal `/build` commit **first** (they ride to GitHub Pages on push).

## Phase 2 — Preflight (free, no git changes, no GitHub minutes)

```powershell
.\release.ps1
```
Runs clean-tree + on-main checks, a full local build + test, computes the version, and prints
the checklist. If anything is red — fix it now, before spending a minute on GitHub.

## Phase 3 — Trigger the signed GitHub build (this spends minutes)

```powershell
.\release.ps1 -Push
```
Creates the empty `release: vX` anchor commit (no `[skip ci]`, so the tag's workflow runs; the
`release:` prefix makes `ci.yml` skip the branch push → no double-bill), tags `vX`, pushes
branch + tag. The tag fires `release.yml` → **signed** `CyrFlip-<ver>-windows-x64.zip` + `.sha256`
+ GitHub Release. Watch it (`gh run watch`) and confirm it is **green**. Copy the **ZIP asset URL**
and the **SHA256** from the run log — the next steps need them.

## Phase 4 — Publish externally (manual; from the printed checklist)

- [ ] **Site/docs** — GitHub Pages auto-deployed from `/docs` on the push. Verify it's live.
- [ ] **winget** (`SerZhyAle.CyrFlip`) — **not** `wingetcreate update`: that command rebuilds from the
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
- [ ] **Fill in the winget PR body** — `wingetcreate` submits Microsoft's template *untouched*: an empty
      Description and every checklist box unticked, which reads as "nothing was verified" and stalls
      review. Write the description and tick each box **after actually doing it** (`gh pr edit <n>
      --repo microsoft/winget-pkgs --body-file <file>`), then re-read the body to confirm. Leave
      "Linked to an issue" unticked and say *not applicable*. Track the PR to merge.
- [ ] **Microsoft Store (MSIX)** — Store ID `9NB4W41NGQJ4`:
      ```powershell
      .\msix\build-msix.ps1 -IdentityName "SZA.CyrFlip" `
        -Publisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" -PublisherDisplayName "SZA"
      ```
      Upload the **unsigned** `.msix` (Microsoft re-signs). Partner Center → CyrFlip → **Create
      new submission** → replace the package → refresh all 13 listings + the **"What's new"**
      from `msix/store-listing-export.csv` → Submit. Detail: [msix/README.md](../../../msix/README.md),
      [STORE_PUBLISHING.md](../../../STORE_PUBLISHING.md). Certification takes a few business days.
- [ ] **VS Code Marketplace** (only if the extension changed) — in `vscode-extension/`:
      ```powershell
      npm install ; npm run compile ; npx @vscode/vsce publish
      ```
- [ ] **Smoke-test** once live: `winget install SerZhyAle.CyrFlip` and/or the Store install;
      confirm the tray app launches and the layout indicator works.

## Guardrails

- **Never** run `/release` to test something — that's `/build`. A release costs money and touches
  public stores.
- "What's new" goes to **all four** surfaces in the same version, or it isn't done.
- Don't tag from a commit carrying `[skip ci]` (the workflow would be skipped) — `release.ps1`
  handles this with the dedicated `release:` anchor commit. Don't hand-tag around it.

## Done means

The GitHub Release is green and signed; "What's new in vXXX" is consistent across GitHub /
Store / winget / (extension); winget PR opened, Store submission created, extension published
where applicable; and the published artefact smoke-tested.
