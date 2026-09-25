# Pointer - `SCENARIO-FILE`

| | |
| --- | --- |
| **Id** | `SCENARIO-FILE` |
| **Version** | 0.9, draft |
| **Home** | `launch-scenarios/README.md` in the shared contracts catalog, with nine byte-exact vectors beside it |
| **Role here** | producer and consumer - the launcher store, the export, and the one-way OneClickRunner import |

## What this repository must do to stay conformant

- **`AppItem`, its element names and their defaults are not ours to rename** (rules 2, 3). `Hotkey` is this
  product's one extension; everything else is the originating product's shape.
- **The OneClickRunner folder is read-only, always** (rule 7). The import reads and never writes, renames
  or deletes there. `LauncherMigrationTests` hashes the source folder before and after, which is the check
  that rule asks for.
- **Identity: preserved on migration, replaced on import** (rule 8). A colliding id gets a fresh one and is
  counted, so the user is told.
- **A corrupt file costs one scenario, never the list** (rule 9), and its name is surfaced.
- **`SPECIAL_YTDLP` keeps working forever** (rule 4), and normalizing it happens in our own store only.
- **Nothing runs at import** (rule 10); targets are validated at launch. An untrusted runtime input - the
  yt-dlp link - is never written into the file (rule 11).

## Open debts

Both are exception rows in the registry and tickets in
`PLAN/Contract_Conformance_Backlog_v0.1.md` (local, not published):

1. the format carries **no version at all**, so no reader can refuse a future MAJOR cleanly;
2. an unknown `Type` value fails the whole file instead of degrading to `Executable`, which is the
   documented default. Pinned by `LauncherScenarioStoreTests`; the contract decides the answer first.

The contract is a draft because its declared owner - OneClickRunner, whose format it is - has not confirmed
it. That repository was read and not touched.
