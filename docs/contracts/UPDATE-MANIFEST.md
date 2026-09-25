# Pointer - `UPDATE-MANIFEST`

| | |
| --- | --- |
| **Id** | `UPDATE-MANIFEST` |
| **Version** | 0.9, draft |
| **Home** | `app-update-feed/README.md` in the shared contracts catalog |
| **Role here** | Consumer / Client - application update discovery and integrity verification |

## What this repository implements and guarantees

- **Non-Store release checks**: Client check for update feed manifests (`https://sza.od.ua/updates/cyrflip.json`).
- **Integrity verification**: Verification of package SHA-256 before invoking external installers.
- **Silent degradation on network failure**: Update check failures never interrupt app startup or throw blocking message boxes.
- **Store neutrality**: In packaged MSIX / Store builds, update checking is bypassed in favor of native Microsoft Store management.
