# Ship-together surfaces

The existing surface list in `CLAUDE.md` remains authoritative. This table extends it with S0075 help.

| Surface | Source / render target | Locales | S0075 |
| --- | --- | --- | --- |
| App names, buttons and help copy | `Localization*.cs`; help copy source `tools/help/strings.json` | 13 UI languages | F1, help buttons and tray Help |
| Surface inventory | `help-map.json` | Stable IDs, UI names in the termbase | Every Form, settings page and tour step |
| Offline and site help | `tools/help/Generate-Help.ps1`, `tools/help/finalize.py` -> `docs/help/` | 13 UI languages, light and dark captures | Same files embedded in the executable and published with the site |
| Guide | `docs/guide.html`, `docs/ru/guide.html`, `docs/uk/guide.html` | EN/RU/UK | Help index links |
| Landing pages | `docs/index.html`, `docs/<locale>/index.html` | Trilingual root plus ten standalone locales | Help index links |
| README / user-facing release record | `README.md`, `README_RU.md`, `README_UK.md` | EN/RU/UK | Offline help and index links |
| Store draft sources | `msix/store-listing-export.csv`, `msix/listing/` | 13 | Offline help sentence; mirrors regenerated |
| Site discovery | `docs/sitemap.xml`, `docs/search-index.json`, `docs/DOCUMENT_REGISTRY.jsonl`, `docs/site-held-addresses.jsonl` | Per-page locales | Every new published help address |
| Support and privacy | Existing pages | Existing locale sets | Same support address; help makes no network request |
| Hub | Existing product card | EN/RU/UK | No positioning change, so no card edit |
| VS Code extension and winget descriptions | Existing sources | Existing locale sets | No feature change in the extension or package identity |

Generate after building the solution in Release:

```powershell
python tools/help/render-strings.py --check
./tools/help/Generate-Help.ps1
python tools/help/finalize.py
./tools/help/Test-ContextHelp.ps1
```

Disjoint `-Locale` groups may render concurrently. Run the finalizer after all groups finish. `-Surface`
refreshes one Form's page across the selected locales. These scripts render demo instances under a hidden
host; they do not drive the running app, copy the desktop, load real settings, or use the real clipboard.
The static gate is in `build.ps1 -Tests` and the release preflight. The release walk is recorded separately
from screenshot generation, as required by CONTEXT-HELP section 4.
