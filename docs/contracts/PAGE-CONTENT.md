# Pointer - `PAGE-CONTENT`

| | |
| --- | --- |
| **Id** | `PAGE-CONTENT` |
| **Version** | 1.2, active |
| **Home** | `product-web-pages/PAGE-CONTENT.md` in the shared contracts catalog |
| **Role here** | consumer - the landing page (`docs/index.html` and the 10 standalone locale pages) |

## What this repository must do to stay conformant

- **Keep the app order**: header, an outcome H1, what it is and who it is for, the demo strip, then `#get`
  with the real channels only (Microsoft Store, GitHub Releases, winget) and three steps, the safe path first.
- **3 to 6 outcome groups**, details in `<details>`; the purpose is never hidden.
- **A caveat sits beside the action it affects**: the ZIP is unsigned and is unpacked to a permanent folder
  first, with the link to the trust page (`INSTALL-TRUST`), inside `#get`.
- **Never state a channel, platform or claim the release does not have.**

Held across all 13 landing locales.