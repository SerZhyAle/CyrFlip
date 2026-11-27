---
paths:
  - "release.ps1"
  - "msix/**"
  - "winget/*"
  - "releases/*"
  - ".github/workflows/*.yml"
  - "tools/store/*"
  - "tools/release-ledger.ps1"
  - ".claude/skills/release/SKILL.md"
  - ".sza-canon.json"
  - "vscode-extension/CHANGELOG.md"
---

# Release, publishing channels and frozen anchors

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Release conventions (CyrFlip deltas)

The canon moved out of the hub repo on 2026-07-27; the old `..\Unified_Rules` local path is dead - a path
no CI job and no second machine could reach.

**Overlay facts (this repo):**
- **Version shape** - dotted `YY.M.D.HHmm` (e.g. `26.7.22.1712`), stamped at build time by the csproj
  (`src/CyrFlip/CyrFlip.csproj`). The `v*` git tag is authoritative for release-asset names; `release.yml`
  re-pins the embedded exe version to the tag (`-p:Version=<tag>`) so the in-file stamp matches the ZIP name.
  The **VS Code extension runs its own semver clock** (`vscode-extension/package.json`, `0.1.5`), decoupled
  from the app date tag.
- **Channels (4 publish ops + a site).** GitHub Release (`CyrFlip-<ver>-windows-x64.zip` + `.sha256`, body
  auto from `generate_release_notes`); **winget** `SerZhyAle.CyrFlip` (`winget/*.yaml`, `InstallerType: zip`
  / `NestedInstallerType: portable`, alias `cyrflip`); **Microsoft Store** MSIX (`msix/build-msix.ps1`,
  manual Partner Center); **VS Code Marketplace** (`npx @vscode/vsce publish`, manual). GitHub Pages from
  `/docs` (trilingual). **No installer** - portable ZIP only (no Inno `.iss` / WiX `.wxs`).
- **Frozen anchors** - winget `PackageIdentifier: SerZhyAle.CyrFlip`; MSIX Identity `Name: SZA.CyrFlip` +
  `Publisher: CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD` + Store ID `9NB4W41NGQJ4`; exe/`AssemblyName`
  `CyrFlip`; VS Code extension id `SerZhyAle.cyrflip-vscode`. No Inno `AppId` / WiX `UpgradeCode`.
- **Companion extension** - `vscode-extension/` is **not** an edition and **not** a co-shipped binary: it
  is a code-independent (TypeScript) companion coupled to the app by a **one-way file contract** (the app
  writes `layout.txt` via `LayoutPublisher.cs`; the extension polls it), published on its own clock.

**Build/release wall & CI cost levers** (every repo runs on paid Windows minutes):
- **`build.ps1`** = СБОРКА (free local build, appends `[skip ci]`, may push to `main` but **never tags**);
  **`release.ps1`** = РЕЛИЗ (the only tagger; preflight = on-main + local build/test, then
  `-Push` creates the `release: vX` anchor commit + tag). The billable boundary is that tag, not a
  script-capability wall. **A dirty tree blocks neither script** (single-developer repo): the preflight
  reports how many paths are uncommitted - they simply won't be in the release, since the tag points at
  the anchor commit - and carries on; `-RequireClean` brings the old refusal back. `build.ps1 -Commit`
  with nothing to commit likewise reports and finishes instead of failing. `build.ps1` stops the running
  tray app before building and, when the build or the tests fail, **starts the previous copy again** from
  where it ran (S0038 RP-5) - it used to leave the user without indicator and hotkeys until a green build.
- Levers wired: `[skip ci]` on build commits; **`ci.yml` runs only on a manual dispatch** (S0081 - a full
  solution build plus the whole suite is above the 5 s line, so a push or a pull request runs nothing; the
  release runs the same build and suite against the tag's tree, and the preflight builds IconGen, which
  only CI used to build); the tag triggers only `release.yml`; `concurrency cancel-in-progress` true on
  CI / false on release.
- **Preflight verdict contract (`CHECK-VERDICT` 0.9)**: gates exit **0** (pass), **1** (defect/drift), **2** (could-not-verify / unverified / tool missing / unreachable origin without `-Offline`), or **3** (advisories). A green tail line is never printed over a failed check. The preflight ends with a machine-readable verdict line `<subject>: PASS|FAIL (n)|NOT VERIFIED (n)` as the last stdout line. Mirror and CSV checks run even after a failed test (rule 9 collect-all).
- **Check placement (`CHECK-PLACEMENT` 0.9)**: declared in `tools/checks/check-placement.jsonl` and verified both ways by `tools/checks/Test-CheckPlacement.ps1` in `build.ps1 -Fast` / `-Tests` and `release.ps1`.
- **Zero accepted debt (`CHECK-BASELINE` 0.9)**: zero-tolerance build (`WarningsAsErrors`, `Skipped: 0`), no accepted-debt baseline file exists.
- **Artifact version gate** - `release.yml` checks out the tag it releases (also on a manual dispatch),
  re-applies the tag-shape gate, and fails unless the built exe - and again the exe inside the ZIP - has
  `FileVersion` = the tag version and `ProductVersion` = `<version>+<commit sha>` (`BUILD-EVIDENCE` rule 2).
  `msix/build-msix.ps1` applies the same `ProductVersion` check on both its paths (S0038 RP-3), and its
  identity comes from `msix/identity.json` - **it holds** the frozen Store anchors (`SZA.CyrFlip` / `CN=F98ACEDB-...` / `SZA`,
  plus the Store ID and the package family name; no file, no build; S0100; a local
  test identity only behind `-SelfSign -TestIdentity`, RP-2).
- **Store channel preflight (S0100, `WINDOWS-STORE` 0.4 section D)** - the release preflight reads the live Store
  read-only **before the tag**: `msix/Invoke-StoreChannelPreflight.ps1` finds the canon's shared
  `Store-Preflight.ps1` (the `sza` plugin), passes it `msix/identity.json` and the MSIX version about to be built, and the
  shared script issues one `msstore submission get <Store ID>` and no other verb. Exit 1 (a pending submission,
  a version not newer, a price not `Free`, no live submission, an unreadable state) fails the preflight with the
  reason - a console-made draft such as Submission 16 does exactly that until the owner commits or deletes it in
  Partner Center; exit 2 (CLI missing or unconfigured, token refused, recorded credential expired, plugin absent)
  prints one line, the Store step is manual, and changes nothing, so the channel never fails a release after the
  tag. `release.ps1 -SkipStoreChannel` (and `-Offline`) leaves it out; one evidence line per run goes to
  `artifacts/store-channel-evidence.jsonl` (values the script sets itself, no account identifier). The upload stays
  the Partner Center web flow; no write has ever been made through the channel. `tools/checks/Test-StoreChannelPreflight.ps1`
  (in `build.ps1 -Fast -Area store`) runs it against a stub `msstore`. Detail: `msix/README.md`.
- **A published tag is never rebuilt** (S0038 RP-1): the resolve step asks `gh release view` and refuses
  when the release already carries its `CyrFlip-*.zip`, and the publish step has `overwrite_files: false`
  - a re-run makes a ZIP with other bytes (`Compress-Archive` stamps fresh times), and replacing it and its
  `.sha256` breaks the hash in every winget manifest already merged.
- **Tag-format gate** - `release.ps1` validates `^\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4}$` (no zero-padded month/day) + `ParseExact` before
  tagging, so a mistyped `-Version` fails before any push; `release.yml` applies the same shape,
  `ParseExact` and "newer than every other `v*` tag" gates to what it is asked to release (RP-6).
- **About and the log report show the tag's spelling** (`26.9.26.0930`, from the informational version -
  `SupportBundle.AppVersion`, S0038 RP-4), not the SDK-normalized assembly version (`26.9.26.930`).
- Releases ship **unsigned** (CI Authenticode step is opt-in via `SIGNING_CERT_*` secrets, which are unset;
  the Store re-signs the MSIX). Don't claim releases are signed.

**Repo-specific decisions (resolved 2026-07-23):**
- **Store-listing source of truth = `msix/store-listing-export.csv`** (Partner Center export-then-merge),
  all 13 languages. `msix/store-listings.md` and `store/listing-*.txt` are **generated** from it by
  **`msix/render-listing-mirrors.ps1`** - never hand-edit the copy in them; the prose *outside* the three
  locale sections of the md (the runFullTrust justification, the submission notes) is hand-written and the
  renderer leaves it alone. `release.ps1` runs the same script with **`-Check`** in the preflight, because
  the alternative is what actually happened: both mirrors sat a release behind on the translator paragraph
  and on four feature lines, in three languages, and nothing could see it - the mirror is exactly what
  gets pasted into the live listing on the day the CSV importer refuses a file. `msix/listing/<lang>.txt`
  is upstream of the CSV, not downstream: the merge fills **empty** cells only, so a change made there
  alone never reaches the Store - edit both.
- **VS Code extension is published by hand** (`vsce publish`, its own semver), only when `vscode-extension/`
  changed - deliberately no `ext-vscode-v*` tag/workflow.
