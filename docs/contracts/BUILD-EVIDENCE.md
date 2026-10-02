# Pointer - `BUILD-EVIDENCE`

| | |
| --- | --- |
| **Id** | `BUILD-EVIDENCE` |
| **Version** | 0.10, draft |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 5 |
| **Role here** | producer - the release ZIP (and the MSIX) carry the version of the build that produced them |

## What this repository must do to stay conformant

- **The exe's version is the tag and its revision is the tag's commit** (rule 2). `release.yml` checks
  out the tag it releases, also on a manual dispatch, and fails unless `FileVersion` equals the tag
  version and `ProductVersion` equals `<version>+<commit>` - on the built exe and again on the exe inside
  the ZIP.
- **No retry to green** (rule 3): `NoTestRetryPackageIsReferenced` fails on any retry package.
- **Each generator ships with its compare check** (rule 7): the Store listing mirrors have one
  (`render-listing-mirrors.ps1 -Check`).

The MSIX build (`msix/build-msix.ps1`) is pinned to the tag too (S0038 RP-3): on both of its paths the
exe's `ProductVersion` must be `<version>+<tag commit>`. Generators have compare checks: `render-listing-mirrors.ps1 -Check` and `build-store-listing-csv.ps1 -Check`. IconGen is checked via CI build and declared in placement registry.