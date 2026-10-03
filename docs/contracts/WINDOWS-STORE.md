# Pointer - `WINDOWS-STORE`

| | |
| --- | --- |
| **Id** | `WINDOWS-STORE` |
| **Version** | 0.1, draft |
| **Home** | `windows-store/README.md` in the shared contracts catalog |
| **Role here** | Producer - the packed MSIX (`msix/build-msix.ps1`) and the Partner Center listing import (`msix/build-store-listing-csv.ps1`, copy decks in `msix/listing/`, source of truth `msix/store-listing-export.csv`) |

## What this repository does to stay conformant

Held today: the identity defaults are the reserved anchors and `build-msix.ps1` reads `<Identity>`,
`PublisherDisplayName`, the version and the file list back out of the packed archive, comparing them
with the reserved values (rules 1-3, conformance rung 3); the build asserts no `AppxSignature.p7x`
on the upload candidate and the self-signed build writes `...-selfsigned.msix`, never the same name
(rule 2); `THIRD-PARTY-NOTICES.txt` is staged and asserted inside the packed archive (rule 4); the
version is the mechanical remap of the tag (rule 3); startup, data folder and file access do not
rely on what the package container hides - `windows.startupTask`, the per-user package folder, no
`Square310x310Logo` (rule 5); the import file is UTF-8 without BOM with CRLF as its only line break -
inside quoted cells as much as between records - and the writer refuses anything else (rule 8);
every cell is checked against its cap before the file is written, and a language with no column
fails the run naming it (rules 7 and 9); the emptied fixture `store-listing-export.sample.csv` is
committed `-text` and `-FillNothing` writes it back byte for byte (rule 13, conformance rung 1);
`-Check` compares the built import CSV as bytes (rung 1's sibling); "What's new" is authored in
en-us, ru and uk and the machine-translated columns carry the English text folded to 1500 (rule 16);
`Title` is one string in every column (rule 14).

The exceptions of 2026-10-03 were closed the same day (ticket `S0047`) - see the catalog registry
section 3 rows for the commands and outputs. Rules 15 (the forbidden-terms list) and 18 (the IARC
questionnaire) are still unevidenced here: neither artifact is in this repository's tree. Rung 4
(the real import report with no "couldn't import" block) belongs to the next Store submission.
