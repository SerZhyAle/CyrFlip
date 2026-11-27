---
paths:
  - "build.ps1"
  - "release.ps1"
  - "tools/Assert-TestRun.ps1"
  - "tools/checks/*"
  - "tests/CyrFlip.Tests/UiSweepGate.cs"
  - ".claude/skills/build/SKILL.md"
---

# Build, run and the check tiers

Moved verbatim from `CLAUDE.md` (2026-11-25). Feature numbers refer to the numbered list in `CLAUDE.md` "What CyrFlip is"; "above", "below" and "this file" refer to the former single file.

## Build, run, test

**Between releases only checks of 5 seconds or less run** (S0081, `TESTING_AND_QA` section 2): the full
build and every slower test run only at the release, or when the owner names that run. A slow rung a
ticket skips goes into its audit as a `RELEASE` item naming the command the release will run, and the
report says "not built, not tested until the release".

```powershell
.\build.ps1 -Fast                             # FAST tier, after every edit: the static gates, no build, no test (~4 s)
.\build.ps1 -Fast -Area help                  # one area: placement | help | positioning | site | listing | cli
.\build.ps1                                   # the owner's СБОРКА: app-only build + stage single exe + deploy to C:\GD\..
.\build.ps1 -Tests                            # RELEASE tier, owner-named only: whole solution + dotnet test + static gates
.\release.ps1                                 # the release preflight: full build, every tier, IconGen, listing and help gates
.\src\CyrFlip\bin\Release\net48\CyrFlip.exe   # run the app (tray icon → right-click → Exit)
dotnet run --project tools/IconGen            # regenerate icon/banner assets
```

The measured list (S0081; "direct" = one timed call on the dev machine, 2026-10-29):

| Check | Tier | Seconds | Source |
| --- | --- | --- | --- |
| `tools/checks/Test-CheckPlacement.ps1` | fast | 0.39 | direct, 2026-10-29 |
| `tools/site/Test-Positioning.ps1` | fast | 0.45 | direct, 2026-10-29 |
| `tools/site/Test-SiteFacts.ps1` | fast | 0.44 | direct, 2026-10-29 |
| `tools/help/Test-ContextHelp.ps1` | fast | 2.57 | direct, 2026-10-29 (with the S0075 capture digests) |
| `msix/render-listing-mirrors.ps1 -Check` | fast | 0.71 | direct, 2026-10-29 |
| `msix/build-store-listing-csv.ps1 -Check` | fast | 0.75 | direct, 2026-10-29 |
| `tools/cli/Render-CliDoc.ps1 -Check` | fast | 0.2 | direct, 2026-11-15 (with the placement gate) |
| `build.ps1 -Fast` (all six, and `cli` since S0107) | fast | 4.04 | direct, 2026-10-29 (one area: 0.4-1.1) |
| `build.ps1 -Fast -Area store` (`tools/checks/Test-StoreChannelPreflight.ps1`, stub `msstore`, 44 cases) | fast, outside the default set | 1.0-1.3 | direct, 2026-11-12 |
| `dotnet test CyrFlip.sln` (sweeps gated) | release | ~45 | dev machine, 2026-10-24 (UiSweepGate decision) |
| `dotnet build CyrFlip.sln` / app-only `build.ps1` / IconGen build | release | not recorded | measured at the next release run |
| `CYRFLIP_SWEEP=full` window sweeps | owner-named | ~36 min (all) | 2026-10-24 |
| `tools/uitest/*`, `tools/site/Measure-SiteContracts.ps1` | owner-named | minutes, desktop-bound | `operator-typed` in `check-placement.jsonl` |

Branch CI (`ci.yml`) runs only on a manual dispatch; the push and the pull request run nothing.

**The 13-language UI sweeps run nowhere automatically** (`tests/CyrFlip.Tests/UiSweepGate.cs`, owner decision 2026-10-24, the fourth version of the rule: 2026-10-15 skipped a sweep only while `Localization*.cs` + `SettingsForm*.cs` were unchanged, 2026-10-16 keyed it on the build configuration, then "a release is the only thing that sweeps" - which put a ~36 minute walk of every window in every language into every `release.ps1` preflight, `release.yml` and `ci.yml`. The owner's verdict: it passed once, that is enough, and a release test run is a couple of minutes, not a space rocket). The `[UiSweepFact]` / `[UiSweepTheory]` methods (the ones that build real windows in every UI language - the full settings window, the dialogs, the module windows, the traffic-light board - **and every other test that builds a `SettingsForm` and costs 2 s or more**: a form builds in about 5 s on the dev machine, so ninety of them were 10 of the 11 minutes the rest of the suite took, measured 2026-10-24; with them gated the whole suite runs in **about 45 s**) keep no state and ask for one thing: **they run only when `CYRFLIP_SWEEP=full` is set by hand, in any configuration**, and **no script and no workflow sets it any more** - `release.ps1` clears it for its own test run and judges the run with `Assert-TestRun.ps1 -AllowSkipped`, `release.yml` and the hand-dispatched `ci.yml` do the same (`release.ps1 -SkipSweeps` and the `skip_sweeps` dispatch input are gone / a no-op, there is nothing left to skip). `dotnet test` in Debug or Release, `build.ps1 -Tests`, the preflight, CI: every sweep reports skipped. **A change to the interface sweeps the window it touched, not the interface**: `CYRFLIP_SWEEP=full dotnet test --filter SymbolsWindowTests` while working on one window, and only then - and, being above the 5 s line (S0081), only when the owner names that run. An agent never sets `CYRFLIP_SWEEP` for a change that touches no window or string table, never for the whole suite, and never to "see a green". **The default run stays at a couple of minutes** (owner, 2026-10-24): a new test that builds a `SettingsForm` or loops a window over languages or pages is `[UiSweepFact]` (`[UiSweepTheory]` for a theory) from the start, not `[Fact]`. Marking a new 13-language window loop `[UiSweepFact]` instead of `[Fact]` is the whole registration; pure-string loops over `Localization.Names` stay `[Fact]` (milliseconds, always on). Under `dotnet test` the assembly-level `[assembly: TestFramework]` hook is dead - `xunit.runner.visualstudio` 3.x ignores it - hence the attribute+discoverer mechanism.

The `net48` build output *is* the distributable: a single `CyrFlip.exe` plus `config.json`, no runtime to install. (The spec's `dotnet publish .. --self-contained false` line assumed a .NET 6+ target and does not apply to the net48 choice.) Releases are cut by pushing a `vYY.M.D.HHmm` tag (`.github/workflows/release.yml` builds the ZIP + sha256, carrying `CyrFlip.exe`, `CyrFlip.exe.config`, `README.md`, `LICENSE`, and `THIRD-PARTY-NOTICES.txt`). winget manifests in `winget/` are filled per release (see `winget/README.md`).

Version is stamped at build time as `YY.M.D.HHmm` via a `<Version>` property in the csproj (verified: e.g. `26.6.11.1649`).
