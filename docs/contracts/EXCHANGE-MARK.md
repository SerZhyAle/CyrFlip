# Pointer - `EXCHANGE-MARK`

| | |
| --- | --- |
| **Id** | `EXCHANGE-MARK` |
| **Version** | 0.3, draft (read 2026-10-21; the picture and the names, ticket S0079) |
| **Home** | `exchange-mark/README.md` in the shared contracts catalog, sections 2 and 3 |
| **Role here** | Consumer, `partial 0.3` - CyrFlip only signs in to an exchange account (ticket S0074), so the server program's own rules (9, 13) do not apply and the launcher icon is never touched (rule 12, section 6) |

## What this repository must do to stay conformant

- **Rules 1 and 6 - the artwork is copied, never drawn.** Byte copies, with the catalog's names: the PNG steps
  `mark-16..256.png` and the glyph master `exchange-mark-mono.svg` in `assets/exchange-mark/` (the steps are embedded
  in the exe, `ExchangeMark`), the four SVG masters in `docs/assets/exchange-mark/` (the site) and in
  `docs/help/exchange-mark/` (the help set is unpacked to a per-user folder and cannot reach `docs/assets/`).
  `.gitattributes` marks the three folders `-text`. `ExchangeMarkTests` pins the SHA-256 of every file and of every
  embedded step, and fails on a second drawing of the star in `src/` (the five ray colours, the glyph's path outside
  `Glyphs.g.cs`). Rung 2: `Find-ExchangeMark.ps1 -Require svg/exchange-mark.svg,svg/exchange-mark-small.svg,svg/exchange-mark-mono.svg,svg/exchange-mark-full.svg`.
- **Rule 5 - sizes.** A .NET Framework window has no SVG renderer, so the program takes the held PNG step of the exact
  device size (the 100 to 200 per cent steps of 16, 24 and 32 logical px are all held) and, between two steps, scales
  the next larger one **down**, never up. The flat drawing is the set's own up to 32 px, the faceted one from 36 px.
- **Rules 2 and 3.** `ExchangeMarkPicture` never mirrors, tints or outlines the star (the layout marker's out-of-theme
  precedent, `APP-STYLE` rule 5); in Windows high contrast it draws the one-tone glyph in the theme's text colour. The glyph
  is the vocabulary record `source.exchange-server`, whose path data is `exchange-mark-mono.svg` unchanged: `tools/IconGen/glyph-overrides.txt`
  puts it over the catalog's two server bays and says when `ICON-SET` has caught up (section 6; the line is then deleted).
- **Rule 8 - where the program shows it.** (a) the exchange server settings page, in the header band beside the title, 24
  logical px, and the sign-in dialog, 32; (b) each account entry, before its label, 24; (e) the About page's resource list,
  16, beside the credit **"Media Exchange Server"** (rule 14: named as itself). In the page strip the one-tone glyph.
  An image-only mark is named by item CW's term in the interface language. **Name alone, with the reason:** the state line
  and the failure text (running text), the balloons (they cannot hold a picture), the traffic light's `exchange` tile (its
  one picture is the state glyph) and its "Type" picker (a list of text only, nothing else has a picture), the tray menu, Jump
  List and desktop shortcuts (commands, they carry the verbs' glyphs), the hotkeys and settings-file rows (text).
- **Rules 10 to 12 - the documents.** The help pages of the settings page and of the sign-in dialog open with the full
  form (128 px, once, `alt` "Media Exchange Server"); the About help page carries the flat mark inline beside its title
  (24 px, the term as `alt`); the guide chapter and the three README headings carry it inline at 32 px (flat: rule 5 puts the faceted drawing at 33 px and up); the
  guide's page-strip row draws the glyph inline. `ExchangeMarkTests` and `tools/help/Test-ContextHelp.ps1` scan them in every language.
- **Never marked:** the open exchange file (ticket S0023), the traffic light's `sftp` kind (direct access), the product's
  own icon (`assets/cyrflip.ico` is byte-identical), a page that merely mentions the server (privacy, trust, store text).
- **Open (`partial`):** the help captures are not regenerated yet, so the strip's new glyph and the band's mark are not in
  the screenshots (rule 11, batched with the next help regeneration); the on-screen walk of rung 3 - the light and dark
  themes at 100 and 200 per cent, `ar` and `ur`, high contrast - is owed: the unit tests render the controls and prove the
  sizes, the mirroring and the one-tone glyph, but a per-monitor scale change needs a live session.
