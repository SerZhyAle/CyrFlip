# Pointer - `INSTALL-TRUST`

| | |
| --- | --- |
| **Id** | `INSTALL-TRUST` |
| **Version** | 1.2, active |
| **Home** | `install-trust/README.md` in the shared contracts catalog, with the reference rendering beside it |
| **Role here** | producer - **adopted** |

## Where this product stands

Releases ship **unsigned**: the CI Authenticode step exists but no certificate is configured, so the ZIP
and the winget package (the same ZIP) are unsigned, and only the Store build is signed - by the Store.
Users of the ZIP therefore meet SmartScreen and, on some machines, an antivirus behaviour heuristic.

## What this repository must do to stay conformant

- **Keep `docs/trust.html` and its twelve translations** (`docs/<lang>/trust.html`) in the four sections of
  rule 1, in order, with the ids `what`, `why`, `click`, `never`. English is authoritative; each
  translation links back to it.
- **Quote the dialog** (rule 2): "Windows protected your PC", "More info", "Run anyway". A translation
  quotes Windows' own wording only where it is confirmed, otherwise the English string with a translation.
- **Say the real reason and its cost** (rule 3): a certificate costs money every year and not buying one
  was a choice; behaviour heuristics are a second reason that signing would not remove.
- **Only per-file, per-folder, reversible instructions** (rule 4) - never "turn off SmartScreen" or the
  antivirus, never "run as administrator" as a fix.
- **Itemize every elevation** (rule 5): CyrFlip never elevates itself; the two prompts it can cause on the
  user's behalf (a scenario marked "run as administrator", the Ollama installer) are named with whose they are.
- **The never-does list says what the privacy page says** (rule 6), locale by locale. When a behaviour
  default changes (cursor change, context menu, autostart), both pages change in the same edit.
- **Reachable from where the download is** (rule 7): the 11 landing pages, the 3 guides, the 13 privacy
  pages, the 3 READMEs and the winget `InstallationNotes`. The Store listing carries no SmartScreen text:
  that channel is signed.
- **On signing, update - never delete** (rule 8): the page stays and says from which version.
- **Never tell a user to "Unblock" a file** (rule 4, 1.2 item K): the Properties checkbox removes the mark of
  the web that SmartScreen keys on, and rule 4's list is closed. No page, README, winget note or listing says
  it, in any of the 13 languages.

Guard: `TrustPageTests` (every locale has the page, the four sections in order, the English quotes, no
forbidden instruction, every download surface links it).

## Conformance state

Read at 1.2 on 2026-10-07 (ticket S0050). **Held:** the 13 trust pages carry the four sections of rule 1 in
order (item L), ids `what`, `why`, `click`, `never`; the English page quotes the dialog (rule 2); no page, the
three READMEs, the three winget `InstallationNotes` or the Store listing sources instruct an "Unblock"
(item K). **Not read:** the localized Windows strings of the dialog strings of the twelve translations against a live dialog in
each display language (item A's fallback applies until they are). **Guard gap:** `TrustPageTests` forbids
"turn off SmartScreen" and the antivirus phrases but has no "Unblock" entry in its forbidden list, so item K
is held by reading, not by a test.

S0055: `WelcomeDialog` page 4 links the trust and privacy pages on explicit clicks (rule 7). Startup remains opt-in; the packaged build hides the startup control. The last page warns about a transient executable folder. `TrustPageTests` includes this first-run surface.
