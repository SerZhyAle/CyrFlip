<!-- Render target. Source of truth for Store listing copy is msix/store-listing-export.csv
     (Partner Center export-then-merge). This file and store/listing-*.txt render from it -
     do not treat them as authoritative; update the CSV first. -->

# Microsoft Store listings (EN / RU / UK)

Ready-to-paste copy for the CyrFlip Store product (Partner Center → *Store listings*).
Add one listing per language: **English (en-US)**, **Russian (ru-RU)**, **Ukrainian (uk-UA)**.
The package itself ships an English UI, so its manifest declares only `en-us`; these are **listing**
translations (what shoppers read on the product page), which are independent of package resources.

Field limits: Description ≤ 10,000 chars; each Product feature ≤ 200 chars; Short description ≤ 1,000.

---

## English (en-US)

**Short description**
> Live EN/RU/UK keyboard-layout indicator right at your text caret, plus one-key transliteration between QWERTY and ЙЦУКЕН.

**Description**
```
CyrFlip is a tiny Windows tray app that shows your active keyboard layout (EN/RU/UK) right where you type, and flips text between layouts with a single key.

Its headline feature is a live layout indicator pinned next to the blinking text caret, so you always know whether you are about to type Latin or Cyrillic - even in browsers and the VS Code chat box, where most indicators can't reach. The same EN/RU/UK marker can also replace the I-beam mouse cursor and is shown on the tray icon.

Its second feature fixes text typed in the wrong layout: select it, press the hotkey, and CyrFlip transliterates it in place between QWERTY and ЙЦУКЕН - in both directions, and even for mixed text. The hotkey is configurable from the tray without restarting.

It runs in the system tray, uses little memory, and needs nothing extra installed on Windows 10/11.

Privacy: CyrFlip uses a keyboard hook and the clipboard only to detect the active layout and perform the flip you trigger. Optional clipboard history is off by default; if enabled, it is stored locally with Windows DPAPI encryption and can be paused or cleared at any time. CyrFlip does not log keystrokes or use the network. Open source: https://github.com/SerZhyAle/CyrFlip
```

**Product features** (one per line)
```
Live EN/RU/UK layout marker next to the text caret - works in browsers and the VS Code chat
Optional layout-coloured mouse cursor and tray icon
One-key transliteration between QWERTY and ЙЦУКЕН, in place, both directions
Auto-detects direction per character - also fixes mixed-layout text
Configurable global hotkey, changeable from the tray without restarting
Runs quietly in the tray, low memory, nothing extra to install
Open source - no telemetry, no network, no data collection
```

**What's new in this version**
```
Added a full-history search window; independent on/off switches for each hotkey plus a master switch; an option to yield the hotkeys to a focused Remote Desktop client (mstsc/msrdc) so a CyrFlip inside the remote session handles them; the clipboard manager window now remembers whether it was open and stays that way on the next launch; and the interface language now follows your Windows language by default.
```

---

## Русский (ru-RU)

**Краткое описание**
> Живой индикатор раскладки EN/RU/UK прямо у текстового курсора и транслитерация QWERTY⇄ЙЦУКЕН одной клавишей.

**Описание**
```
CyrFlip — это крошечная утилита в системном трее Windows: она показывает активную раскладку клавиатуры (EN/RU/UK) прямо там, где вы печатаете, и переключает текст между раскладками одной клавишей.

Главная функция — живой индикатор раскладки рядом с мигающим текстовым курсором, поэтому вы всегда видите, латиницей или кириллицей набираете сейчас, — даже в браузерах и в окне чата VS Code, куда большинство индикаторов не дотягивается. Тот же значок EN/RU/UK может заменять курсор-«балку» мыши и отображается на иконке в трее.

Вторая функция исправляет текст, набранный не в той раскладке: выделите его, нажмите горячую клавишу — и CyrFlip транслитерирует его на месте между QWERTY и ЙЦУКЕН, в обе стороны и даже для смешанного текста. Горячая клавиша настраивается прямо из трея, без перезапуска.

Работает в системном трее, потребляет мало памяти и не требует ничего доустанавливать на Windows 10/11.

Конфиденциальность: CyrFlip использует перехват клавиатуры и буфер обмена только для того, чтобы определить активную раскладку и выполнить запрошенную вами замену. Необязательная история буфера по умолчанию выключена; при включении она хранится только локально и шифруется Windows DPAPI, её можно поставить на паузу или очистить. CyrFlip не ведёт журнал нажатий и не использует сеть. Открытый исходный код: https://github.com/SerZhyAle/CyrFlip
```

**Функции продукта** (по одной в строке)
```
Живой маркер раскладки EN/RU/UK у текстового курсора — работает в браузерах и чате VS Code
Необязательная замена курсора мыши и иконка в трее в цвете раскладки
Транслитерация одной клавишей между QWERTY и ЙЦУКЕН, на месте, в обе стороны
Определяет направление по каждому символу — исправляет и смешанный текст
Настраиваемая глобальная горячая клавиша, меняется из трея без перезапуска
Тихо работает в трее, мало памяти, ничего доустанавливать не нужно
Открытый код — без телеметрии, без сети, без сбора данных
```

**Что нового в этой версии**
```
Добавлено окно поиска по всей истории буфера; отдельные выключатели для каждой горячей клавиши и общий выключатель; возможность уступать хоткеи активному клиенту удалённого рабочего стола (mstsc/msrdc), чтобы их обрабатывал CyrFlip внутри удалённого сеанса; окно менеджера буфера теперь запоминает, было ли оно открыто, и сохраняет это состояние при следующем запуске; а язык интерфейса теперь по умолчанию следует за языком Windows.
```

---

## Українська (uk-UA)

**Короткий опис**
> Живий індикатор розкладки EN/RU/UK просто біля текстового курсора та транслітерація QWERTY⇄ЙЦУКЕН однією клавішею.

**Опис**
```
CyrFlip — це крихітна утиліта в системному треї Windows: вона показує активну розкладку клавіатури (EN/RU/UK) прямо там, де ви друкуєте, і перемикає текст між розкладками однією клавішею.

Головна функція — живий індикатор розкладки поруч із миготливим текстовим курсором, тож ви завжди бачите, латиницею чи кирилицею друкуєте зараз, — навіть у браузерах і у вікні чату VS Code, куди більшість індикаторів не дотягується. Той самий значок EN/RU/UK може замінювати курсор-«балку» миші та відображається на піктограмі в треї.

Друга функція виправляє текст, набраний не в тій розкладці: виділіть його, натисніть гарячу клавішу — і CyrFlip транслітерує його на місці між QWERTY та ЙЦУКЕН, в обидва боки й навіть для змішаного тексту. Гарячу клавішу можна налаштувати прямо з трея, без перезапуску.

Працює в системному треї, споживає мало пам'яті й не потребує нічого додатково встановлювати на Windows 10/11.

Конфіденційність: CyrFlip використовує перехоплення клавіатури та буфер обміну лише для того, щоб визначити активну розкладку й виконати запитану вами заміну. Необов'язкова історія буфера типово вимкнена; після ввімкнення вона зберігається лише локально й шифрується Windows DPAPI, її можна призупинити або очистити. CyrFlip не веде журнал натискань і не використовує мережу. Відкритий вихідний код: https://github.com/SerZhyAle/CyrFlip
```

**Функції продукту** (по одній у рядку)
```
Живий маркер розкладки EN/RU/UK біля текстового курсора — працює в браузерах і чаті VS Code
Необов'язкова заміна курсора миші та піктограма в треї в кольорі розкладки
Транслітерація однією клавішею між QWERTY та ЙЦУКЕН, на місці, в обидва боки
Визначає напрямок за кожним символом — виправляє й змішаний текст
Налаштовувана глобальна гаряча клавіша, змінюється з трея без перезапуску
Тихо працює в треї, мало пам'яті, нічого додатково встановлювати не потрібно
Відкритий код — без телеметрії, без мережі, без збору даних
```

**Що нового в цій версії**
```
Додано вікно пошуку по всій історії буфера; окремі вимикачі для кожної гарячої клавіші та загальний вимикач; можливість поступатися хоткеями активному клієнту віддаленого робочого столу (mstsc/msrdc), щоб їх обробляв CyrFlip у віддаленому сеансі; вікно менеджера буфера тепер запам'ятовує, чи було воно відкрите, і зберігає цей стан під час наступного запуску; а мова інтерфейсу тепер за замовчуванням відповідає мові Windows.
```

---

## runFullTrust justification (submission field, English; reuse as-is)

```
CyrFlip is a full-trust Win32 desktop app (.NET Framework / WinForms), not a UWP app, so runFullTrust is required to run as a normal desktop process and to call the Win32 APIs its core features depend on:
- A low-level keyboard hook (WH_KEYBOARD_LL) detects the configurable hotkey that triggers the in-place transliteration. It only matches the chord; it does not log or store keystrokes.
- The clipboard (with SendInput copy/paste) reads the current selection and writes back the transliterated text for the flip the user explicitly triggers.
- SetSystemCursor / UI Automation / IAccessible2 read the caret position to draw the layout indicator next to it.
These APIs are available only to full-trust desktop apps. The app runs entirely locally, makes no network connections, and collects no user data. Open source: https://github.com/SerZhyAle/CyrFlip
```

---

## Submitting the update (Partner Center)

The package is built and unsigned (Microsoft re-signs on certification):
`msix/dist/CyrFlip-26.722.1718.0-x64.msix` (version 26.722.1718.0).

1. [partner.microsoft.com/dashboard](https://partner.microsoft.com/dashboard) → **Apps and games → CyrFlip → Create new submission** (Update).
2. **Packages** → remove the old `.msix` / upload `CyrFlip-26.722.1718.0-x64.msix`.
3. **Store listings → Manage additional languages** → add **Russian (ru-RU)** and **Ukrainian (uk-UA)** (if not present). For each language listing (and refresh English), paste the Short description, Description, and Product features above. Reuse the existing screenshots, or add localized ones.
4. **Properties / Age ratings / Pricing** — unchanged.
5. If asked again, paste the **runFullTrust justification** above.
6. **Submit to the Store.** Certification usually takes a few business days; a keyboard-hook app can draw extra review — the justification + clear description pre-empt most questions.
