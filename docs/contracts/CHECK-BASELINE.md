# Pointer - `CHECK-BASELINE`

| | |
| --- | --- |
| **Id** | `CHECK-BASELINE` |
| **Version** | 0.9, draft |
| **Home** | `automated-checks/README.md` in the shared contracts catalog, section 3 |
| **Role here** | none - **no accepted-debt file, by design** |

## Where this product stands

The build and the suite are zero-tolerance: a fixed `WarningsAsErrors` list in the csproj and `Skipped: 0`
in the test run. No finding class carries accepted debt, so there is no baseline file. Any future one
follows the contract's set and count rules and only ever falls.