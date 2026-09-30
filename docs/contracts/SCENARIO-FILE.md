# Pointer - `SCENARIO-FILE`

| | |
| --- | --- |
| **Id** | `SCENARIO-FILE` |
| **Version** | 0.9, draft |
| **Home** | `launch-scenarios/README.md` in the shared contracts catalog, with nine byte-exact vectors beside it and the owner-side proposal `PROPOSAL-2026-09-23-owner-side-read.md` (not folded in) |
| **Role here** | producer and consumer - the launcher store, the export (`LauncherScenarioStore.Export`, the settings table's context menu), the single-file import and the one-way OneClickRunner migration |

## What this repository must do to stay conformant

- **`AppItem`, its element names and their defaults are not ours to rename** (rules 2, 3). `Hotkey` is this
  product's one extension; everything else is the originating product's shape.
- **An `Id` assigned on read is persisted at once** (rule 3), in our own store only - otherwise a Jump List
  task built from it could never find it again (`AFileWithoutAnIdGetsOneThatSurvivesTheNextLoad`).
- **The migration appends in rule 5 order** - `Order`, then the name ignoring case (then the name ordinal
  and `Id`, so the order is total) - not in file-name order (`LauncherMigration.InSourceOrder`,
  `MigrationAppendsInTheSourceProductsOrder`).
- **The OneClickRunner folder is read-only, always** (rule 7). The import reads and never writes, renames
  or deletes there. `TheSourceDirectoryIsNeverModified` hashes the source folder with SHA-256 before and
  after, which is the check that rule asks for.
- **The nine vectors are pinned** (§4): `FixturesAreByteIdenticalToTheCatalogVectors` holds the contract's
  SHA-256 values as literals, since CI has no copy of the catalog. A re-issue of the vectors updates that
  test in the same change.
- **Identity: preserved on migration, replaced on import** (rule 8). A colliding id gets a fresh one and is
  counted, so the user is told; the same id with the same content is skipped and counted instead.
- **A corrupt file costs one scenario, never the list** (rule 9), and its name is surfaced.
- **`SPECIAL_YTDLP` keeps working forever** (rule 4), and normalizing it happens in our own store only.
- **Nothing runs at import** (rule 10); targets are validated at launch. An untrusted runtime input - the
  yt-dlp link - is never written into the file (rule 11).

## Open debts

The product side of the three decisions the owner took on 2026-09-30 (proposals B1, B2, B5 of S0014) is
implemented; what remains is the catalog - the contract text and the registry rows still describe them as open.
Each is an exception row in the registry and a ticket in `PLAN/Contract_Conformance_Backlog_v0.1.md` (local,
not published):

1. **C1** - a `schemaVersion` attribute on `AppItem` (absent = 1). This product reads it and refuses a higher
   MAJOR, a renamed or namespaced root and a non-numeric value with one localized "newer format" message
   (`AFileInAFormatThisBuildDoesNotReadIsRefusedWholeAndSaysSo`); its writer still omits the attribute. The
   contract has to name the carrier before OneClickRunner can honour it;
2. **C2** - an unknown `Type` refuses the file under rule 9, because degrading it to `Executable` would run
   its `Path` as a program (rule 10). Pinned by `AnUnknownTypeValueMakesTheFileUnreadableUnderRule9`; the
   contract has to write that down as the documented default (B2);
3. **C8** - a repeated OneClickRunner migration skips what is already there (same id, same name, path and
   arguments), counts it and says so; the same id with other content is a collision and gets a fresh id
   (`RunningTheMigrationTwiceImportsNothingTheSecondTime`; proposal B5 for the contract).

The contract is a draft because its declared owner - OneClickRunner, whose format it is - has not confirmed
it. That repository was read and not touched.
