---
name: build
description: Prepare and execute a CyrFlip СБОРКА (local build) - the free, iterative path. Run the fast static gates (5 s or less), build Release when asked, verify the change, capture user-facing notes for the next release's "What's new", and commit with [skip ci] so no GitHub minutes are spent. Use when the user asks to build, do a сборка, or commit a change.
---

# /build - СБОРКА (local build)

A **СБОРКА** is the free, iterative loop: fast checks, build the exe, verify, commit. The full test suite is the release tier.
It spends **zero GitHub minutes** (commits carry `[skip ci]`). The paid, outward-facing
path is `/release`. Concept reference: [RELEASE.md](../../../RELEASE.md).

This skill is a **checklist and order of work**, not a script. The script `build.ps1`
executes steps 2 and 6; the rest is judgement you apply each time.

## Order of work

1. **Understand the change.** Confirm what was asked and which files/modules it touches.
   If nothing is staged/changed yet, do the edit first.

2. **Check, then build** - between releases nothing slower than 5 seconds runs unless the owner
   names that run (S0081, `TESTING_AND_QA` section 2):
   ```powershell
   .\build.ps1 -Fast                    # the static gates, ~4 s; -Area narrows it (placement, help,
                                        # positioning, site, listing) - run after every edit
   .\build.ps1                          # the СБОРКА the owner asked for: app-only build + stage + deploy
   ```
   `-Fast` builds nothing, tests nothing and leaves the running tray app alone. The default run
   builds the app project in Release, stages the single exe and deploys to the sync folders - it is
   the build the owner asked for, never an agent's routine check after an edit.
   `.\build.ps1 -Tests` (the whole solution, `dotnet test` and the static checks, minutes) is the
   **release tier**: run it only when the owner names it; otherwise the release preflight runs it.
   A code change is then reported as "not built, not tested until the release", with the skipped
   command named as a `RELEASE` item. The 13-language UI sweeps run nowhere automatically: nothing
   sets `CYRFLIP_SWEEP=full` but a human, for one window only. A clean build must be
   **warning-free** (csproj has `WarningsAsErrors`). If a build, a test or a gate fails - stop and
   fix before committing.

3. **Verify the actual behaviour** when the change is interop/UI (hook, clipboard flip,
   cursor/caret overlay, layout detection) - unit tests don't cover these. Run the exe and
   exercise the change in a real app. Cheap pure-logic changes (engines, parsing) are
   covered by tests; trust them.

4. **Did this change user-facing behaviour?** (new/changed hotkey, tray item, indicator,
   config default, Store/winget-visible behaviour). If **yes**, this MUST not be lost before
   the next release - capture a one-line note now:
   - Add a bullet to the **`## Unreleased`** section at the top of
     [vscode-extension/CHANGELOG.md](../../../vscode-extension/CHANGELOG.md) **only if** the
     extension changed; otherwise keep a running note for the app in your summary so `/release`
     can fold it into "What's new in vXXX" across all surfaces.
   - Update [README.md](../../../README.md) (+ `README_RU.md`, `README_UK.md`) and the config
     table if a setting/default/hotkey changed, so the docs never lag the code.
   If **no** (refactor, internal fix, test-only), skip - nothing to announce.

5. **Update CLAUDE.md** if the change alters architecture, a module's responsibility, or a
   load-bearing invariant (per the project convention). Skip for trivial changes.

6. **Commit the сборка** (free - `[skip ci]` is appended automatically):
   ```powershell
   .\build.ps1 -Commit -Message "<concise summary>"          # commit only
   .\build.ps1 -Commit -Message "<concise summary>" -Push     # also push to main
   ```
   Prefer small, targeted commits (project convention). We are on `main` - pushing is fine
   here because `[skip ci]` keeps it free; a `/release` is what spends minutes.

## Guardrails

- **Never** trigger a paid GitHub build from a сборка. If committing manually instead of via
  `build.ps1`, put `[skip ci]` in the message yourself.
- A сборка does **not** tag, does **not** publish, does **not** touch winget/Store/Marketplace.
  The moment the user wants any of that → switch to `/release`.
- Don't announce "done" with unverified interop changes - say what you ran and what you saw.

## Done means

`.\build.ps1 -Fast` green, the build green when the owner asked for one, the slower rungs named as
"not built, not tested until the release" (or run because the owner named them), behaviour verified
where it matters, any user-facing change captured for "What's new", and (if asked) committed with
`[skip ci]`.
