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
| [store-listing-import-13-languages.csv](store-listing-import-13-languages.csv) | **Generated** - the export above with every empty cell filled, ready for *Store listings → Import*. Do not hand-edit; run `build-store-listing-csv.ps1`. |
| [build-store-listing-csv.ps1](build-store-listing-csv.ps1) | Fills the gaps in the export from `listing/`. It **only writes empty cells** and never reorders columns, so asset URLs and anything Partner Center already holds survive untouched. `-FillNothing` proves the writer is lossless: the output must come back byte-identical to the export. |
| [listing/](listing/) | One `@@Field / value` text file per language for the 11 languages the export does not carry. Plain text on purpose: this is prose to be proofread, not code. |

Diagnostics are generated on demand rather than kept around: `-SkipFields <Field...> -OutFile <name>`
narrows an import down to the field being blamed, and `-LogoFlagOnly` writes the logo-flag repair
alone. Both diff cleanly against the export, which is how a silent rejection gets located.

The `stage/` and `dist/` folders are produced by the script (git-ignore them).

## How the app adapts under MSIX

The **same `CyrFlip.exe`** ships packaged and unpackaged; it detects which at runtime
(`PackageInfo.IsPackaged` → `GetCurrentPackageFullName`) and adjusts two things:

1. **Autostart.** Unpackaged uses `HKCU\..\Run`. Under MSIX that write is virtualized and ignored at
   sign-in, so autostart is declared in the manifest as a **`windows.startupTask`** (off by default).
   The settings checkbox therefore can't flip it: clicking opens *Settings ▸ Apps ▸ Startup*, where the
   user does. It still shows the real state, read from
   `HKCU\Software\Classes\Local Settings\..\AppModel\SystemAppData\<PackageFamilyName>\CyrFlipStartup\State`
   (`StartupTaskState`: 2 = Enabled, 4 = EnabledByPolicy), and re-read whenever the window is activated.
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
4. **Store listings** - the listing copy is imported, not typed:
   1. **Manage additional languages** → add every language you intend to ship (the app's own 13:
      English, Russian, Ukrainian, German, Italian, Spanish, French, Portuguese (Brazil), Chinese
      Simplified, Hindi, Bengali, Arabic, Urdu → `en-us ru uk de it es fr pt-br zh-hans hi bn ar ur`).
   2. **Export**, and drop the downloaded file over `msix/store-listing-export.csv`. Do this
      **every time**: the export carries the current submission's listing ids in its asset URLs, and
      the languages present on it are exactly the columns an import will accept.
   3. `.\msix\build-store-listing-csv.ps1 -ImportFolder` - fills the empty cells from
      `msix/listing/` and stages `msix/store-listing-import/`: the CSV beside the screenshots, each
      language's `DesktopScreenshot` pointing at its own file by relative path.
   4. **Import listings → Import folder** and pick `msix/store-listing-import`.

   > **An import is all-or-nothing.** If any cell is rejected, Partner Center saves *none* of the
   > file - "even for fields without errors". So a single bad image path throws away the text too;
   > read *View errors for last import* before assuming anything landed.

   > **Only the folder upload understands a relative image path.** `store-listing-import/x.png` in a
   > file imported through *Import .csv* fails with *"The value you provided is not valid"* on every
   > image cell, because there is no folder to resolve it against. That is why the plain
   > `store-listing-import-13-languages.csv` carries **no** image paths at all - blank image cells
   > are safe, they leave whatever Partner Center already has in place.

   > **Never copy `OverrideLogosForWin10` between languages.** The flag promises Windows-10 override
   > logos for that listing, and Partner Center then holds the listing *Incomplete* until the logo
   > files exist - silently, with no error and nothing marked on the page. An early version of this
   > script copied it from `en-us` along with `Title` and the copyright, and ten listings sat at
   > Incomplete with a full description and a screenshot while Urdu, typed in by hand and left at
   > `False`, read Complete. `-LogoFlagOnly` writes the one-field repair.

   > **A listing is complete only with a description AND at least one screenshot.** Text alone leaves
   > the language sitting at *Incomplete*, which is what happened to the ten languages of the first
   > import. Images cannot travel in a bare `.csv` - Partner Center only accepts new files through
   > *Import folder*, where an image cell holds `<root folder name>/<file>`. Screenshots come from
   > `.\tools\uitest\Save-StoreScreenshots.ps1` (the settings window per language, on the 1366x768
   > canvas). A language that already has a screenshot keeps it and ours lands in the next free slot.

   > **A language that is not on the submission is dropped silently.** That is how the Urdu copy went
   > missing on the first attempt: `ur` had not been added yet, so Partner Center took the other
   > twelve columns and said nothing about the thirteenth. Always read the per-language report the
   > script prints, then check the language count in Partner Center after the import.

   > **The importer and the exporter disagree about the byte-order mark.** Partner Center exports
   > UTF-8 **with** BOM and then refuses that same file - *"We couldn't process this .csv file"* -
   > until the BOM is stripped, a long-standing report on
   > [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/960287/partner-center-import-csv-listings-stop-working-wi).
   > So the script writes **UTF-8 without BOM** by default; `-KeepBom` restores it. It also quotes
   > every field, which is what the one upload Partner Center did accept looked like.

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
