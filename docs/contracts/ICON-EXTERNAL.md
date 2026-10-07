# Pointer - `ICON-EXTERNAL`

| | |
| --- | --- |
| **Id** | `ICON-EXTERNAL` |
| **Version** | 0.12, draft (read 2026-10-07; 0.11 added rule 6 on languages, 0.12 the images inside a document - which does not arise here) |
| **Home** | `iconography/README.md` in the shared contracts catalog, section 4 |
| **Role here** | consumer - launcher scenario icons and their fallbacks |

## What this repository must do to stay conformant

- **Third parties appear in text or as their own mark only** - no redrawn logos.
- **Scenario icons are shown as the OS reports them** (`LauncherIconResolver`): the program's own icon and a
  script's interpreter icon, untinted.
- **Fallbacks come from the vocabulary**: `content.apps` when a program's icon cannot be read,
  `action.download` for a yt-dlp scenario - never CyrFlip's own mark and never a blank task.
- **A language is marked by its endonym, never by a flag** (rule 6): the interface-language selector and the
  translation target list show "Русский", "Deutsch", "中文", never a flag.
- **Any vendored third-party glyph ships with its licence notice** (`THIRD-PARTY-NOTICES.txt` in the
  release ZIP and the MSIX).
- **Not an `ICON-EXTERNAL` matter, noted here because it is the same icons:** the vocabulary fallbacks are
  drawn in the decorated look, where `ICON-RENDER` rule 9 asks a Jump List task for the one-tone mono look - see
  that pointer's open deviation.
