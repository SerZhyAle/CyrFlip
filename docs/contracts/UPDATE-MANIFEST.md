# Pointer - `UPDATE-MANIFEST`

| | |
| --- | --- |
| **Id** | `UPDATE-MANIFEST` |
| **Version** | 0.10, draft |
| **Home** | `app-update-feed/README.md` in the shared contracts catalog |
| **Role here** | **Not bound** - CyrFlip has no update check |

## What this repository implements

Nothing. CyrFlip never asks a server whether a newer version exists: there is no update-feed client, no
manifest download and no installer verification of its own in `src/CyrFlip/` (checked 2026-09-25 - no
feed URL, no manifest type, no update code). The app opens no socket at all unless the user switches the
translator on, and then only to the Ollama endpoint. Updates reach the user through the channel they
installed from: winget, the Microsoft Store, or a new ZIP from the GitHub release page.

The one installer CyrFlip ever downloads is Ollama's, on the user's request, verified by its Authenticode
signature (`OllamaManager`, ticket S0010 TD-5) - that is not an update of CyrFlip and not this contract.

## Open

An earlier version of this pointer, and the catalog's `UPDATE-MANIFEST | CyrFlip` registry row (dated
2026-09-24), described a feed client at an `sza.od.ua/updates/` address that was never written. The
pointer is corrected here; the registry row is a catalog edit and waits for the owner's review with the
rest of the S0012 packet (`PLAN/`, local only).

If an update check is ever added it is a network-surface change: off by default, opt-in, and declared on
the privacy page in all 13 languages before it ships.
