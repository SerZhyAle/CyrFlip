# Pointer - `ICON-EXTERNAL`

| | |
| --- | --- |
| **Id** | `ICON-EXTERNAL` |
| **Version** | 0.11, draft |
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
- **Any vendored third-party glyph ships with its licence notice** (`THIRD-PARTY-NOTICES.md` in the
  release ZIP and the MSIX).
