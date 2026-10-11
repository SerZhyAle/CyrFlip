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
| [build-msix.ps1](build-msix.ps1) | Builds Release, stages payload, generates logo PNGs, fills the manifest, packs the `.msix`, then reads `<Identity>`, `PublisherDisplayName`, the version and the file list back **out of the packed archive** and compares them with the reserved Store anchors (WINDOWS-STORE rules 1-4) - a bare build can no longer produce a Store-invalid package silently. |
| [store-listing-export.csv](store-listing-export.csv) | **The source of truth for every word of the listing**, in the app's 13 listing languages (a fresh export may carry more or fewer columns - Partner Center reorders and drops them between submissions; a shipped language with no column fails the builder's run): Partner Center's bulk export/import format (Description, ReleaseNotes, Product features, Search terms). Replace it with a **fresh export before every import** (WINDOWS-STORE rule 6 - a held export goes stale the moment any import changes the submission). Edit here, then re-import via *Store listings → Import* in Partner Center - keeps the listing content under version control instead of only living in the Partner Center UI. Screenshot/logo asset rows are Partner-Center-hosted URLs, left as-is. |
| [store-listing-import-13-languages.csv](store-listing-import-13-languages.csv) | **Generated** - the export above with every empty cell filled, ready for *Store listings → Import*. Do not hand-edit; run `build-store-listing-csv.ps1`. |
| [store-listing-export.sample.csv](store-listing-export.sample.csv) | The emptied fixture (WINDOWS-STORE rule 13): the export with every copy cell emptied and every language column present, marked `-text` in `.gitattributes` so git never touches its bytes. `-FillNothing` must write it back byte for byte. Regenerate with `-WriteSample` after every fresh export. |
| [build-store-listing-csv.ps1](build-store-listing-csv.ps1) | Fills the gaps in the export from `listing/`. It **only writes empty cells** and never reorders columns, so asset URLs and anything Partner Center already holds survive untouched. `-Check` renders in memory and compares **as bytes** with the committed CSV (exit 0 on match, 1 on drift, 2 on missing inputs). `-FillNothing` proves the writer is lossless: it renders the sample fixture and fails unless the result is byte-identical. Every cell is checked against its Partner Center cap before anything is written - Feature 200, ShortDescription 1000, Description 10000, ReleaseNotes 1500 after CRLF, at most 7 search terms with no duplicate in a language's set (rule 9); a language missing its column fails the run instead of being dropped silently (rule 7). The output is UTF-8 without BOM with CRLF as the only line break - inside quoted cells as much as between records - and the writer refuses to produce anything else (rule 8). `-ScreenshotOnlyMissing` stages a screenshot only where the language has none - the flag every import after the first needs. |
| [listing/](listing/) | One `@@Field / value` text file per language, written when the export did not yet carry those 11 languages. Plain text on purpose: this is prose to be proofread, not code. Keep it in step with the CSV - the merge above only fills **empty** cells, so a change made here alone never reaches the Store. |
| [render-listing-mirrors.ps1](render-listing-mirrors.ps1) | Rewrites the two human-readable mirrors - `store-listings.md` and `store/listing-*.txt` - from the export. `-Check` renders in memory and exits 0 on match, 1 on drift, 2 on missing inputs (the release preflight runs it). Both mirrors were a release behind in three languages before this existed. |
| [merge-listing-export.ps1](merge-listing-export.ps1) | Starts an import from a **fresh export of the draft submission** (WINDOWS-STORE rule 6): overlays the product's generated import file onto it - Text rows only, by language and/or field, never blanking a cell the draft holds - and writes the result in Partner Center's own dialect (BOM, minimal quoting, CRLF, no trailing newline), so an unmodified export round-trips byte for byte. It is how a refused import is **bisected**: `-Languages`, then `-Fields`, each file built from the last accepted re-export; a re-export byte-identical to the uploaded file proves every cell landed. Measured 2026-10-11 on submission 14 (see the catalog proposal of that date). |

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
   extension by that name - so the packaged app writes the packages own per-user folder **by its real
   path**, `%LOCALAPPDATA%\Packages\SZA.CyrFlip_fdk7e19xt9z9j\LocalCache\Local\CyrFlip\`, which the extension
   computes from the frozen family name. The same folder holds the logs, the log-bundle archives and the
   quick-notes journal. Up to ticket S0016 all of that lived in the machine-wide `%ProgramData%\CyrFlip`,
   shared by every account on the PC; the packaged app now only mirrors `layout.txt`/`layout-klid.txt`
   there for older extensions (`LAYOUT-SIGNAL` 1.1 rule 1) and moves the users own old files out on its
   first start (`DataFolderMigration`).

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
.\build-msix.ps1 -ReleaseZip <the tag's release ZIP> -Version <YY.M.D.HHmm>
```

The identity above is the script's default - it never defaults to anything else (S0038 RP-2) - and
it refuses an exe whose `ProductVersion` is not `<version>+<tag commit>`, i.e. one not built from the
tag (RP-3).

Output: `msix/dist/CyrFlip-<version>-x64.msix`, **unsigned** - that's correct, upload it as-is. A
`-SelfSign` build writes `CyrFlip-<version>-x64-selfsigned.msix` instead: the two never share one
name, so a locally signed package cannot be mistaken for (or overwrite) the Store upload
(WINDOWS-STORE rule 2). The read-back then also asserts the signed package *has* its
`AppxSignature.p7x`, and the unsigned one has none.
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
      Simplified, Hindi, Bengali, Arabic, Urdu → `en-us ru uk de it es fr pt-br zh-hans hi bn ar ur`),
      plus **`pt`** - Portuguese (Portugal), added on 2026-08-19. It is served by the same
      `msix/listing/pt.txt` as `pt-br`, i.e. by Brazilian copy, which reads fine in Portugal but is
      not adapted to it; a separate European file would need its own column mapping in
      `build-store-listing-csv.ps1`.
   2. **Export**, and drop the downloaded file over `msix/store-listing-export.csv`. Do this
      **every time**: the export carries the current submission's listing ids in its asset URLs, and
      the languages present on it are exactly the columns an import will accept.

      > **The export is the *published* copy, which is not always the newest one.** Text written
      > here after the last successful import - a rewritten description, the next release's "What's
      > new" - is *older* in the file you just downloaded, and the merge below only fills **empty**
      > cells, so it will not put it back. Diff the incoming export against the one in git and carry
      > those cells over by hand before running anything.
   3. `.\msix\build-store-listing-csv.ps1 -ImportFolder -ScreenshotOnlyMissing` - fills the empty
      cells from `msix/listing/` and stages `msix/store-listing-import/`: the CSV beside the
      screenshots, each language's `DesktopScreenshot` pointing at its own file by relative path.
      **`-ScreenshotOnlyMissing` is right for every import after the first**: without it each run
      stages our settings shot again, in the next free slot, beside the copy the previous import
      already uploaded - and the live listing then shows the same picture twice.
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
   > So the script writes **UTF-8 without BOM** - there is no switch to put one back, the writer
   > refuses it (WINDOWS-STORE rule 8). Every line break is CRLF, inside quoted cells as much as
   > between records, and there is no trailing newline. It also quotes
   > every field, which is what the one upload Partner Center did accept looked like.

   Screenshots and logos are **not** in the import (their rows are asset URLs tied to an existing
   listing id): upload them per language, or leave the default listing's images to stand.
   "What's new" is authored for **en-us/ru/uk only** - the languages CyrFlip proofreads - and every
   machine-translated column carries the English text (WINDOWS-STORE rule 16), folded to the
   1500-character cap.
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
> language or spelling (a transliteration of a competitor's name counts too). Submission `26.7.22.1712`
> was rejected on 2026-07-23 for a competitor's name used as an "X alternative" search term in the
> English listing; the
> `SearchTerm5` row in [store-listing-export.csv](store-listing-export.csv) now reads
> `cyrillic keyboard` / `кириллица латиница`. Keep competitor comparisons out of keywords entirely -
> the Partner Center keyword hints are UI rules, not the policy.

## Test locally before submitting (self-signed)

To sideload and run the package on your own machine, sign it with a throwaway cert (its subject must
equal `-Publisher`). `-TestIdentity` switches to a local test identity, so the package installs beside
a Store build as a family of its own (with the Store identity a self-signed package cannot install
over the Store-signed one):

```powershell
.\build-msix.ps1 -NoBuild -Version <YY.M.D.HHmm> -SelfSign -TestIdentity
```

The script signs the package and prints the two commands to (1) trust the test cert in
`LocalMachine\TrustedPeople` (run as admin) and (2) `Add-AppxPackage` the `.msix`. Test that the tray
app launches, the layout indicator works, and *Settings ▸ Apps ▸ Startup* lists **CyrFlip**.

> Self-signed packages are for local testing only. **Do not** sign the package you upload to the
> Store - Microsoft signs that one.
