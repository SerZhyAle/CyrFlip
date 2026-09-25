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
  counted, so the user is told.
- **A corrupt file costs one scenario, never the list** (rule 9), and its name is surfaced.
- **`SPECIAL_YTDLP` keeps working forever** (rule 4), and normalizing it happens in our own store only.
- **Nothing runs at import** (rule 10); targets are validated at launch. An untrusted runtime input - the
  yt-dlp link - is never written into the file (rule 11).

## Open debts

Each is an exception row in the registry and a ticket in
`PLAN/Contract_Conformance_Backlog_v0.1.md` (local, not published); the product's proposals on them are
S0014 section 4 (B1-B7):

1. **C1** - the format carries **no version at all**, so no reader can refuse a future MAJOR cleanly
   (proposal B1: a `schemaVersion` attribute on `AppItem`, which today's reader already ignores);
2. **C2** - an unknown `Type` value fails the whole file instead of degrading to `Executable`. Pinned by
   `LauncherScenarioStoreTests`; this product argues the contract should make refusal the documented
   default (B2);
3. **C8** - a repeated OneClickRunner migration imports every scenario again under fresh ids: counted,
   not silent, but doubled (rule 8; proposal B5).

The contract is a draft because its declared owner - OneClickRunner, whose format it is - has not confirmed
it. That repository was read and not touched.
