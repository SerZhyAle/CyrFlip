# Pointer - `SCENARIO-FILE`

| | |
| --- | --- |
| **Id** | `SCENARIO-FILE` |
| **Version** | 0.10, draft |
| **Home** | `launch-scenarios/README.md` in the shared contracts catalog, with nine byte-exact legacy vectors and six authored negative vectors beside it, and the two proposals it folds (`PROPOSAL-2026-09-23-owner-side-read.md`, `PROPOSAL-2026-09-26-absorbing-side-read.md`) |
| **Role here** | producer and consumer - the launcher store, the export (`LauncherScenarioStore.Export`, the settings table's context menu), the single-file import and the one-way OneClickRunner migration |

## What this repository must do to stay conformant

- **`AppItem`, its element names and their defaults are not ours to rename** (rules 2, 3). `Hotkey` is this
  product's one extension; everything else is the originating product's shape. A root in any namespace is not
  `AppItem` (item C).
- **A value that is present but does not parse makes the file unreadable** (rule 9, item C): an empty
  `<Id>`, `<Order>`, `<Type>` or `<RunAsAdmin>`, a boolean other than `true`/`false`/`1`/`0`, a non-numeric
  `Order`. An empty element is absence for string elements only; a negative `Order` reads; a repeated element
  - the first occurrence wins (`ScenarioFileNegativeVectorTests`).
- **An unknown `Type` refuses the file whole** (item B, rule 10) - skipped, counted and named, never degraded to
  `Executable` (`AnUnknownTypeValueMakesTheFileUnreadableUnderRule9`).
- **The reader honours the `schemaVersion` carrier** (item A): absent = 1, a higher MAJOR or a non-numeric value
  is refused per file, and the single-file import says "newer format" in one localized message
  (`AFileInAFormatThisBuildDoesNotReadIsRefusedWholeAndSaysSo`). **Our writer keeps omitting it**
  (`OurOwnWriterOmitsTheVersionCarrier`).
- **An `Id` assigned on read is persisted at once** (rule 3, item D), in our own store only - otherwise a Jump
  List task built from it could never find it again (`AFileWithoutAnIdGetsOneThatSurvivesTheNextLoad`). Any
  hyphenated 8-4-4-4-12 hexadecimal GUID is an identity, the version nibble is not checked.
- **The migration appends in rule 5 order** - `Order`, then the name ignoring case, then the name ordinal and
  `Id`, so the order is total (item E) - not in file-name order (`LauncherMigration.InSourceOrder`,
  `MigrationAppendsInTheSourceProductsOrder`). `0` is a valid position; only an all-zero set reads as unordered.
- **The OneClickRunner folder is read-only, always** (rule 7). The import reads and never writes, renames
  or deletes there. `TheSourceDirectoryIsNeverModified` hashes the source folder with SHA-256 before and
  after, which is the check that rule asks for. The folder `%APPDATA%\OneClickRunner\Scenarios` is in scope as a
  path (item F): the migration relies on it.
- **All fifteen vectors are pinned** (§4): `FixturesAreByteIdenticalToTheCatalogVectors` (nine) and
  `TheNegativeFixturesAreByteIdenticalToTheCatalogVectors` (six) hold the contract's SHA-256 values as
  literals, since CI has no copy of the catalog. A re-issue of the vectors updates those tests in the same
  change. Of the six, five are skipped, counted and named by both the store and the migration, and
  `unknown_element.xml` reads.
- **Identity: preserved on migration, replaced on import** (rule 8). A colliding id gets a fresh one and is
  counted, so the user is told; the same id with the same content is skipped and counted instead (not yet in
  the contract text - see the debts).
- **A corrupt file costs one scenario, never the list** (rule 9), and its name is surfaced.
- **`SPECIAL_YTDLP` keeps working forever** (rule 4), and normalizing it happens in our own store only.
- **Nothing runs at import** (rule 10); targets are validated at launch. An untrusted runtime input - the
  yt-dlp link - is never written into the file (rule 11).

## Vector this product owes the catalog (item G)

`tests/CyrFlip.Tests/Fixtures/ScenarioFile/cyrflip_current_shape.xml` is what CyrFlip's writer produces for one
fixed scenario - no encoding declaration, `xmlns:xsd` before `xmlns:xsi`, every element including `<Hotkey />`,
UTF-8 without a byte-order mark, CRLF. It is **generated, never typed**
(`ScenarioFileWriterVectorTests.TheVectorIsWhatTheWriterWrites`, rewritten with
`CYRFLIP_WRITE_SCENARIO_VECTORS=1`) and is a byte copy away from the catalog's `vectors/`. OneClickRunner's
own is owed by that product.

## Open debts

The product side of the whole 0.10 amendment is implemented and tested. What remains is not in this repository:

1. **Rule 8 on a repeated migration** (CyrFlip's proposal B5, backlog C8). The contract text was not changed:
   it still says only that a colliding id gets a fresh one and is counted. This product skips a scenario whose
   id is present **and** whose name, path and arguments match (`RunningTheMigrationTwiceImportsNothingTheSecondTime`)
   and treats the same id with other content as a collision
   (`TheSameIdWithOtherContentIsStillACollisionNotAnAlreadyMigratedScenario`). The dated exception stays open
   until the contract says so.
2. **Nobody has run OneClickRunner against the six negative vectors**, and it does not read `schemaVersion`
   yet; a file carrying a higher one is refused only here.
3. **The contract is a draft** because its declared owner - OneClickRunner, whose format it is - has not
   confirmed it. That repository was read and not touched.
