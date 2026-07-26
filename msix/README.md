# MSIX package (Microsoft Store)

Packages the unpackaged `CyrFlip.exe` as a **full-trust MSIX** for the Microsoft Store (Path A).

**Why the Store path is attractive:** the developer account is now **free** (individuals since late
2025, companies since May 2026), and **Microsoft re-signs the package** during certification - so you
get a trusted signature and reputation **without buying a code-signing certificate**. A Store-signed,
Store-distributed build is also the most effective answer to the Avast/AVG `IDP.Generic` heuristic.

## Files

| File | Purpose |
| --- | --- |
| [AppxManifest.xml](AppxManifest.xml) | Package manifest (full-trust app, `runFullTrust`, `startupTask`). Has `__PLACEHOLDERS__`. |
| [build-msix.ps1](build-msix.ps1) | Builds Release, stages payload, generates logo PNGs, fills the manifest, packs the `.msix`. |
| [store-listing-export.csv](store-listing-export.csv) | Partner Center bulk listing export/import format (Description, ReleaseNotes, Product features, Search terms for en-us/ru). Edit here, then re-import via *Store listings → Import* in Partner Center - keeps the listing content under version control instead of only living in the Partner Center UI. Screenshot/logo asset rows are Partner-Center-hosted URLs, left as-is. |
| [store-listing-import-13-languages.csv](store-listing-import-13-languages.csv) | **Generated** - the same listing in all 13 languages the app's interface speaks, as one import file. Do not hand-edit; run `build-store-listing-csv.ps1`. |
| [build-store-listing-csv.ps1](build-store-listing-csv.ps1) | Builds the 13-language import CSV: `en-us`/`ru` copied verbatim from the export above, the other 11 read from `listing/`. Prints a per-language completeness report. `-Codes` overrides the column headers if Partner Center spells a language code differently. |
| [listing/](listing/) | One `@@Field / value` text file per language for the 11 languages the export does not carry. Plain text on purpose: this is prose to be proofread, not code. |

The `stage/` and `dist/` folders are produced by the script (git-ignore them).

## How the app adapts under MSIX

The **same `CyrFlip.exe`** ships packaged and unpackaged; it detects which at runtime
(`PackageInfo.IsPackaged` → `GetCurrentPackageFullName`) and adjusts two things:

1. **Autostart.** Unpackaged uses `HKCU\..\Run`. Under MSIX that write is virtualized and ignored at
   sign-in, so autostart is declared in the manifest as a **`windows.startupTask`** (off by default).
   The tray menu item becomes **"Start with Windows.."** and opens *Settings ▸ Apps ▸ Startup*, where
   the user toggles it.
2. **`layout.txt` for the VS Code extension.** Unpackaged writes `%LOCALAPPDATA%\CyrFlip\layout.txt`.
   Under MSIX `%LOCALAPPDATA%` is virtualized into the package container - invisible to the (unpackaged)
   extension - so the packaged app writes **`%ProgramData%\CyrFlip\layout.txt`** instead. The extension
   checks both locations.

The global keyboard hook, `SendInput`, `SetSystemCursor` and clipboard all keep working because the
package declares the `runFullTrust` restricted capability.

## One-time setup in Partner Center

1. Create a **free** developer account and sign in to [Partner Center](https://partner.microsoft.com/dashboard).
2. **Reserve the app name** "CyrFlip" (Apps and games ▸ New product ▸ MSIX or PWA app).
3. Open **Product ▸ Product identity** and copy the three values Microsoft assigned you:
   - **Package/Identity/Name** → pass as `-IdentityName`
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-..`) → pass as `-Publisher`
   - **Package/Properties/PublisherDisplayName** → pass as `-PublisherDisplayName`

These **must match exactly**, or the Store rejects the upload.

**CyrFlip is already reserved** (Store ID `9NB4W41NGQJ4`). Reuse these exact identity values for
every build/update (they're public - they live inside every published `.msix`):

| Parameter | Value |
| --- | --- |
| `-IdentityName` | `SZA.CyrFlip` |
| `-Publisher` | `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD` |
| `-PublisherDisplayName` | `SZA` |

## Build a Store-ready package

Requires the Windows SDK (`makeappx`): `winget install Microsoft.WindowsSDK`.

```powershell
.\build-msix.ps1 `
  -IdentityName        "SZA.CyrFlip" `
  -Publisher           "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" `
  -PublisherDisplayName "SZA"
```

Output: `msix/dist/CyrFlip-<version>-x64.msix`, **unsigned** - that's correct, upload it as-is.
The internal package version is derived from the exe's `YY.M.D.HHmm` stamp and remapped to a
Store-legal `Major.Minor.Build.0` (the revision must be 0; the build script handles this), so each
build is monotonically newer than the last - no manual version bump needed.

## Publishing an update

For an existing app you only create a new submission - the identity above is unchanged:

1. Build the package (command above). Confirm the new `Major.Minor.Build.0` is **higher** than the
   currently published one (check the Store/Partner Center).
2. [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games ▸ CyrFlip ▸ Create
   new submission**.
3. **Packages** → remove the old `.msix`, upload the new one from `msix/dist/`.
4. **Store listings ▸ Manage additional languages** → add every language you intend to ship
   (the app's own 13: English, Russian, Ukrainian, German, Italian, Spanish, French, Portuguese,
   Chinese Simplified, Hindi, Bengali, Arabic, Urdu), then **Store listings ▸ Import** the generated
   [store-listing-import-13-languages.csv](store-listing-import-13-languages.csv). A language must
   exist on the submission before its column is accepted, and the column header has to be the code
   Partner Center itself uses - **export once after adding the languages** and, if a header differs
   from the script's default, re-run `build-store-listing-csv.ps1 -Codes <the exported list>`.
   Screenshots and logos are **not** in the import (their rows are asset URLs tied to an existing
   listing id): upload them per language, or leave the default listing's images to stand.
   "What's new" is filled for **en-us/ru/uk only** - by decision, release notes are not translated
   into all 13.
5. **Submit**. Certification typically takes a few business days; a keyboard-hook app can draw extra
   review - the runFullTrust justification in `store-listings.md` pre-empts most questions.

> **Automating with the `msstore` CLI?** It works only with an Azure AD service principal
> (`msstore reconfigure --tenantId .. --sellerId .. --clientId .. --clientSecret ..`). The
> interactive `msstore reconfigure` **fails on an individual developer account** ("Error while
> retrieving Organization" - no Azure AD org), so for an individual account the **Partner Center
> web flow above is the practical path**.

> **Heads-up on certification:** a global keyboard hook + clipboard access can draw extra review (it
> looks like a keylogger to automated checks). Describe the layout-indicator/transliterator purpose
> plainly in the listing; be ready to justify it if asked.

> **Search terms (policy 10.1.3) - never name a competitor.** Keywords must be **≤ 7 unique terms**,
> relevant to the product, and **must not contain product titles we don't publish** - in *any*
> language or spelling (a transliteration like `пунто свитчер` counts too). Submission `26.7.22.1712`
> was rejected on 2026-07-23 for `punto switcher alternative` in the English listing; the
> `SearchTerm5` row in [store-listing-export.csv](store-listing-export.csv) now reads
> `cyrillic keyboard` / `кириллица латиница`. Keep competitor comparisons out of keywords entirely -
> the Partner Center keyword hints are UI rules, not the policy.

## Test locally before submitting (self-signed)

To sideload and run the package on your own machine, sign it with a throwaway cert (its subject must
equal `-Publisher`, so keep the default or pass a matching `CN=`):

```powershell
.\build-msix.ps1 -SelfSign
```

The script signs the package and prints the two commands to (1) trust the test cert in
`LocalMachine\TrustedPeople` (run as admin) and (2) `Add-AppxPackage` the `.msix`. Test that the tray
app launches, the layout indicator works, and *Settings ▸ Apps ▸ Startup* lists **CyrFlip**.

> Self-signed packages are for local testing only. **Do not** sign the package you upload to the
> Store - Microsoft signs that one.
