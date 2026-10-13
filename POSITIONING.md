# CyrFlip positioning

The one source of what CyrFlip is called and how it is introduced, on every public surface: the landing pages,
the guide, the READMEs, the Microsoft Store listing, the winget manifests, the VS Code extension, the About tab
and the first-run tour. Every other text derives from this file; none of them is a second source
(`SITE-REPRESENTATION` rules 1 and 2). Decided by the owner on 2026-10-07 (ticket S0058, decisions O1 to O3).

The facts block at the end of this file is read by `tools/site/Test-Positioning.ps1`, which fails a surface
that lost the descriptor, kept a retired phrase or moved a frozen anchor.

## The name

| | Text | Where |
| --- | --- | --- |
| Product name | **CyrFlip** | everywhere a name identifies: the store titles, winget `PackageName`, the exe, window titles, the tray tooltip, the registry, file and folder names |
| Descriptor | **a tray toolkit for your keyboard and desktop** | after the product name wherever prose reads it: About, the first sentence of a description, a page title, a README lead |
| Full name | **CyrFlip - a tray toolkit for your keyboard and desktop** | page titles, the README heading line, the social preview |

The descriptor never replaces the product name and never enters an identifier, a window title or a store
`Title` field. Its 13 translations are in the facts block; a surface in another language uses its own.

## The pillars, in this order

The order is the order of every feature list, card grid and listing paragraph. The first pillar is the part that
works from the first start and the one people search for; it is one group among five, not "the main feature".

1. **Type in the right layout** - *on by default.* The live layout marker at the caret, on the I-beam and in the
   tray; one-key conversion of text typed in the wrong layout, between any two installed layouts and in both
   directions; the CapsLock fix; the Windows keyboard layouts and per-language shortcuts in one window.
2. **Act on what you selected** - *opt-in.* CyrFlip's own menu over a selection (Ctrl + right click, or a chord at
   the caret); a translator that runs on a local Ollama model the user installs; a symbols window that finds the
   character your keyboard lacks by name, keyword or Unicode code and copies it with a click.
3. **Keep what you copy and write** - *opt-in.* The encrypted clipboard history; local quick notes that keep text
   exactly as given; the plain-text exchange file that moves both to another computer.
4. **Launch and capture** - *partly on.* The screen-region capture (its chord is on by default); Quick launch for
   programs, scripts and yt-dlp downloads (opt-in); two keep-awake switches (off by default).
5. **Yours alone** - *the promise under all of them, not a feature group.* No account, no telemetry, nothing sent to
   the developer; optional modules stay off until switched on; every hotkey with its state in one list that resets
   to the defaults; 13 interface languages, right-to-left included; a light, dark or Windows-matching theme; one
   small program with nothing extra to install.

## Headline

| | Text |
| --- | --- |
| EN | Type in the right layout, and keep your text tools one chord away. |
| RU | Печатайте в нужной раскладке, а инструменты для текста держите под рукой. |
| UK | Друкуйте в потрібній розкладці, а інструменти для тексту тримайте під рукою. |

## Rules for every text that derives from this file

- **The default is stated.** A text that names an opt-in module says it is optional or off by default; nothing is
  presented as running from the first start except pillar 1 and the region capture.
- **Only what the latest release contains.** A feature of an open ticket is announced in the release that ships it.
- **Search words are kept.** No existing tag, topic or store search term is removed by a rewrite
  (`keyboard-layout`, `transliteration`, `cyrillic` and the layout wording of the RU and UK pages are entry
  points); new ones are added beside them. The one exception: a term that carries a competitor's product or
  brand name is never a search word and is removed wherever it is found (canon PROMOTION.md section 1 rule 4;
  owner, 2026-10-09: "Убрать названия конкурентов" - "remove the competitors' names").
- **The privacy statement is not rewritten with the positioning.** Its meaning stays as it is; a change there is a
  separate, deliberate edit on the privacy page, the trust page and the guide together.
- **Every locale in one edit.** 13 landing pages, 13 Store columns, 13 About strings, 3 READMEs, 3 winget locales.

## Facts

```json
{
  "schema": 1,
  "product": "CyrFlip",
  "descriptor": {
    "en": "a tray toolkit for your keyboard and desktop",
    "ru": "набор инструментов в трее для клавиатуры и рабочего стола",
    "uk": "набір інструментів у треї для клавіатури та робочого столу",
    "de": "ein Werkzeugkasten im Infobereich für Tastatur und Desktop",
    "it": "un set di strumenti nell'area di notifica per tastiera e desktop",
    "es": "una caja de herramientas en la bandeja para el teclado y el escritorio",
    "fr": "une boîte à outils dans la zone de notification pour le clavier et le bureau",
    "pt": "uma caixa de ferramentas na bandeja para o teclado e a área de trabalho",
    "ar": "مجموعة أدوات في علبة النظام للوحة المفاتيح وسطح المكتب",
    "hi": "कीबोर्ड और डेस्कटॉप के लिए ट्रे टूलकिट",
    "bn": "কীবোর্ড ও ডেস্কটপের জন্য ট্রে টুলকিট",
    "ur": "کی بورڈ اور ڈیسک ٹاپ کے لیے ٹرے ٹول کٹ",
    "zh": "托盘里的键盘与桌面工具箱"
  },
  "mark": {
    "en": "tray toolkit",
    "ru": "набор инструментов",
    "uk": "набір інструментів",
    "de": "Werkzeugkasten",
    "it": "set di strumenti",
    "es": "caja de herramientas",
    "fr": "boîte à outils",
    "pt": "caixa de ferramentas",
    "ar": "مجموعة أدوات",
    "hi": "टूलकिट",
    "bn": "টুলকিট",
    "ur": "ٹول کٹ",
    "zh": "工具箱"
  },
  "pillars": [
    { "id": "layout",  "default": "on",      "en": "Type in the right layout",      "ru": "Печатать в нужной раскладке",      "uk": "Друкувати в потрібній розкладці" },
    { "id": "select",  "default": "opt-in",  "en": "Act on what you selected",      "ru": "Действовать над выделенным",       "uk": "Діяти над виділеним" },
    { "id": "keep",    "default": "opt-in",  "en": "Keep what you copy and write",  "ru": "Сохранять скопированное и записанное", "uk": "Зберігати скопійоване й записане" },
    { "id": "launch",  "default": "partly",  "en": "Launch and capture",            "ru": "Запуск и снимок экрана",           "uk": "Запуск і знімок екрана" },
    { "id": "yours",   "default": "promise", "en": "Yours alone",                   "ru": "Только ваше",                      "uk": "Лише ваше" }
  ],
  "retired": [
    "fix wrong-layout text on Windows",
    "(the main feature)",
    "Its headline feature is",
    "plus one-key conversion between any two installed layouts",
    "исправление текста в неверной раскладке (Windows)",
    "виправлення тексту в хибній розкладці (Windows)"
  ],
  "frozen": [
    { "file": "src/CyrFlip/CyrFlip.csproj", "regex": "<AssemblyName>([^<]+)</AssemblyName>", "value": "CyrFlip" },
    { "file": "src/CyrFlip/CyrFlip.csproj", "regex": "<Product>([^<]+)</Product>", "value": "CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.yaml", "regex": "(?m)^PackageIdentifier: (.+)$", "value": "SerZhyAle.CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.installer.yaml", "regex": "(?m)^PackageIdentifier: (.+)$", "value": "SerZhyAle.CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.en-US.yaml", "regex": "(?m)^PackageIdentifier: (.+)$", "value": "SerZhyAle.CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.en-US.yaml", "regex": "(?m)^PackageName: (.+)$", "value": "CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.en-US.yaml", "regex": "(?m)^Moniker: (.+)$", "value": "cyrflip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.ru-RU.yaml", "regex": "(?m)^PackageIdentifier: (.+)$", "value": "SerZhyAle.CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.ru-RU.yaml", "regex": "(?m)^PackageName: (.+)$", "value": "CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.uk-UA.yaml", "regex": "(?m)^PackageIdentifier: (.+)$", "value": "SerZhyAle.CyrFlip" },
    { "file": "winget/SerZhyAle.CyrFlip.locale.uk-UA.yaml", "regex": "(?m)^PackageName: (.+)$", "value": "CyrFlip" },
    { "file": "msix/build-msix.ps1", "regex": "(?m)^\\$ReservedIdentityName\\s*=\\s*'([^']+)'", "value": "SZA.CyrFlip" },
    { "file": "msix/build-msix.ps1", "regex": "(?m)^\\$ReservedPublisher\\s*=\\s*'([^']+)'", "value": "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" },
    { "file": "msix/AppxManifest.xml", "regex": "<DisplayName>([^<]+)</DisplayName>", "value": "CyrFlip" },
    { "file": "msix/AppxManifest.xml", "regex": "uap:VisualElements[^>]*?DisplayName=\"([^\"]+)\"", "value": "CyrFlip" },
    { "file": "msix/AppxManifest.xml", "regex": "StartupTask TaskId=\"([^\"]+)\"", "value": "CyrFlipStartup" },
    { "file": "vscode-extension/package.json", "regex": "\"name\":\\s*\"([^\"]+)\"", "value": "cyrflip-vscode" },
    { "file": "vscode-extension/package.json", "regex": "\"publisher\":\\s*\"([^\"]+)\"", "value": "SerZhyAle" }
  ]
}
```
