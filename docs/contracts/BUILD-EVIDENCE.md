# Pointer - `BUILD-EVIDENCE`

| | |
| --- | --- |
| **Id** | `BUILD-EVIDENCE` |
| **Version** | 0.10, draft (read 2026-10-07; 0.10 names edition as an axis, prefers the embedded source revision, adds the manual-dispatch class and rule 8) |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 5 |
| **Role here** | producer - the release ZIP (and the MSIX) carry the version of the build that produced them |

## What this repository must do to stay conformant

- **The exe's version is the tag and its revision is the tag's commit** (rule 2). `release.yml` checks
  out the tag it releases, also on a manual dispatch (`ref: ${{ env.RELEASE_TAG }}`), and fails unless
  `FileVersion` equals the tag version (compared as parsed versions, not strings) and `ProductVersion` equals
  `<version>+<commit>` - on the built exe and again on the exe inside the ZIP.
- **No retry to green** (rule 3): `NoTestRetryPackageIsReferenced` fails on any retry package.
- **Each generator ships with its compare check** (rule 7): the Store listing mirrors and the import CSV
  have one (`render-listing-mirrors.ps1 -Check`, `build-store-listing-csv.ps1 -Check`, both in the preflight).
- **A release artifact rebuilt outside the tested build states its binding** (rule 8): the tag workflow rebuilds
  the exe, so its binding is the first one - the suite runs in the publishing job on the rebuilt tree
  (`release.yml`, step Test, followed by `Assert-TestRun.ps1`) before the release is created, and the version
  is read back from the exe and from the one inside the ZIP. The residual risk is the usual one: the CI toolchain
  is not the local one.
- **Run times in prose** (rules 4-6): there is no execution telemetry, and no document states a run time a
  decision rests on.

The MSIX build (`msix/build-msix.ps1`) is pinned to the tag too (S0038 RP-3): on both of its paths the
exe's `ProductVersion` must be `<version>+<tag commit>`. IconGen's committed assets are compared with nothing;
that is declared in the placement registry with its reason. The subject banner names the version
(`build <version>: PASS`, `release-preflight v<version>`); the product ships one edition of each artifact, so
rule 1's edition axis does not arise.
