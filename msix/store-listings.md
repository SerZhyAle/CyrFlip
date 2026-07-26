<!-- Render target. Source of truth for Store listing copy is msix/store-listing-export.csv
     (Partner Center export-then-merge). This file and store/listing-*.txt render from it -
     do not treat them as authoritative; update the CSV first. -->

# Microsoft Store listings (EN / RU / UK)

Ready-to-paste copy for the CyrFlip Store product (Partner Center → *Store listings*).
Add one listing per language: **English (en-US)**, **Russian (ru-RU)**, **Ukrainian (uk-UA)**.
The package itself ships an English UI, so its manifest declares only `en-us`; these are **listing**
translations (what shoppers read on the product page), which are independent of package resources.

Field limits: Description ≤ 10,000 chars; each Product feature ≤ 200 chars; Short description ≤ 1,000.

The Partner Center export currently carries only the **en-us** and **ru** columns, so the CSV is the
source for those two; the **Ukrainian** copy below is maintained here and pasted into Partner Center by
hand until a `uk` column appears in an export.

---

## English (en-US)

**Short description**
> Live keyboard-layout indicator right at your text caret, plus one-key conversion between any two installed layouts.

**Description**
```
CyrFlip is a tiny Windows tray app that shows your active keyboard layout right where you type, and converts text between layouts with a single key.

Its headline feature is a live layout indicator pinned next to the blinking text caret, so you always know which layout you are about to type in - even in browsers and the VS Code chat box, where most indicators can't reach. The same marker can also replace the I-beam mouse cursor and is shown on the tray icon. Each of the 13 curated languages has its own colour, any other Windows layout still gets its own two-letter code, and CapsLock adds a thin frame around the marker.

Select text typed in the wrong layout and press the hotkey: CyrFlip transliterates it in place between QWERTY and ЙЦУКЕН, in both directions and even for mixed text. A separate configurable hotkey fixes accidental CapsLock the same way.

Need other languages? Build a table of layout conversions: as many "from layout to layout" pairs as you want, each with its own global shortcut. Text is converted by physical key position, so AZERTY and QWERTZ layouts are handled correctly, and every pair works in both directions - press the same chord again while the pair's other layout is active and the text converts back.

A settings window also replaces the Windows language pane: install, reorder and remove keyboard layouts (nothing is downloaded - they ship with Windows), pick the language-cycle shortcut, and assign direct per-language shortcuts that Windows itself handles, so they keep working even when CyrFlip is closed. Both sections keep a one-time backup of your previous state.

Optional clipboard history is disabled by default. When enabled, it keeps recent Unicode text locally, searchable in its own window; entries can be pinned, deleted, paused, or cleared, and it is protected with Windows DPAPI encryption for the current Windows user.

Quick launch is a second optional module, also off until you enable it. It turns your programs, scripts and yt-dlp downloads into scenarios you can start from the tray submenu, from a global hotkey of their own, or from the taskbar Jump List - even while CyrFlip is not running, if you pin it. Scenarios from the standalone OneClickRunner tool, which this module absorbs, can be imported on the first run without touching the originals.

Every hotkey can be switched on or off independently, CyrFlip can yield them to a focused Remote Desktop client so the copy running inside the remote session handles them instead, and two keep-awake switches stop Windows sleeping or blanking the screen while you work.

The interface is available in 13 languages, right-to-left ones included. It runs in the system tray, uses little memory, and needs nothing extra installed on Windows 10/11.

A built-in translator is optional too, and off until you enable it. Select text anywhere in Windows, press its shortcut, and the translation appears in a small window next to the mouse pointer, filling in as the model writes. It runs on Ollama, a free program you install once on your own computer: CyrFlip does not bundle it, has no account and no key, and by default talks only to http://localhost:11434 - your own machine - so no text is sent to the developer or to any cloud service. Directions are an open-ended table, like the layout conversions: every row is a target language with its own global shortcut, and a row can also follow the interface language or the language of the layout active in the window you translate from. The result can be copied to the clipboard, where the optional history records it like any other copy, or pasted straight over the selection - both are off by default. The Translation tab opens the Ollama download page, starts the server, checks the connection and downloads a model: qwen2.5:3b (~2 GB) is the default, gemma2:2b (~1.6 GB) suits weaker machines, and aya-expanse:8b (~5 GB) translates noticeably better on 8 GB of RAM or more. Fair warning: the quality is the local model's, the first translation after a cold start takes a while as the model loads, Ollama and a model are a multi-gigabyte download you make once, and the memory the model uses belongs to the Ollama process, not to CyrFlip. Selections longer than 4000 characters are translated up to that point, and the window says so.

Privacy: CyrFlip uses a keyboard hook and the clipboard only to detect the active layout and perform actions you trigger. It does not log keystrokes, and it opens no network connection unless you enable the translator, which talks only to the Ollama server you point it at - your own computer by default. Open source: https://github.com/SerZhyAle/CyrFlip
```

**Product features (one per line)**
```
Live layout marker next to the text caret - works in browsers and the VS Code chat
13 curated languages, each with its own colour; any other Windows layout gets its code too
One-key transliteration between QWERTY and ЙЦУКЕН, in place, both directions
A table of layout conversions: any number of pairs, each with its own shortcut, both ways
Converts by physical key position, so AZERTY and QWERTZ layouts are handled correctly
Separate hotkey fixes accidental CapsLock (swaps UPPER/lower case)
Install, reorder and remove Windows keyboard layouts without the Windows language pane
Per-language switch shortcuts that Windows handles itself, so they work when CyrFlip is closed
Optional encrypted clipboard history with search, pin, and Windows DPAPI protection
Optional Quick launch: your programs, scripts and yt-dlp downloads as one-click scenarios
Start a scenario from the tray, its own global hotkey, or the taskbar Jump List
Two keep-awake switches: no sleep and no screen blanking while you work
Interface in 13 languages, right-to-left included; follows your Windows language
Runs quietly in the tray, low memory, nothing extra to install
Optional local translator: a selection is translated by hotkey in a window at the mouse pointer
Translation runs on Ollama on your own computer - installed separately, off until you enable it
Open source - no telemetry and no data collection; the only network use is your own Ollama
```

**What's new in this version**
```
The biggest release so far: five new modules, each optional and off until you switch it on.

Layout conversion is now an open table - any number of "from layout to layout" pairs, each with its own shortcut. Text is converted by physical key position, so AZERTY and QWERTZ are handled correctly, and every pair works in both directions. The original EN/RU flip is simply its first row.

A settings page replaces the Windows language pane: install, reorder and remove keyboard layouts, choose the language-cycle shortcut, and assign direct per-language shortcuts that Windows itself handles - they keep working even when CyrFlip is closed.

A built-in translator: select text, press your shortcut, and the translation appears in a small window beside the mouse pointer, filling in as the model writes it. It runs on Ollama, a free program you install once on your own computer, so the text goes to your own machine - never to a cloud service and never to the developer.

Quick launch turns your programs, scripts and yt-dlp downloads into scenarios you can start from the tray, from a global hotkey of their own, or from the taskbar Jump List.

Two keep-awake switches stop Windows sleeping or blanking the screen while you work.

The interface now speaks 13 languages, right-to-left included, and follows your Windows language by default.
```

---

## Русский (ru-RU)

**Краткое описание**
> Живой индикатор раскладки прямо у текстового курсора и конвертация текста между любыми двумя установленными раскладками одной клавишей.

**Описание**
```
CyrFlip — крошечная утилита Windows в системном трее: она показывает активную раскладку клавиатуры прямо там, где вы печатаете, и конвертирует текст между раскладками одной клавишей.

Главная функция — живой индикатор раскладки рядом с мигающей текстовой кареткой, поэтому вы заранее видите, какой раскладкой будете печатать, — даже в браузерах и в окне чата VS Code, куда большинство индикаторов не дотягивается. Тот же маркер может заменять текстовый курсор мыши и отображается на значке в трее. У каждого из 13 популярных языков свой цвет, любая другая раскладка Windows тоже получает свой двухбуквенный код, а при включённом CapsLock вокруг маркера появляется тонкая рамка.

Выделите текст, набранный не в той раскладке, и нажмите горячую клавишу: CyrFlip транслитерирует его на месте между QWERTY и ЙЦУКЕН, в обе стороны и даже для смешанного текста. Отдельная настраиваемая горячая клавиша так же исправляет случайно включённый CapsLock.

Нужны другие языки? Соберите таблицу конвертаций: сколько угодно пар «из раскладки в раскладку», у каждой своя глобальная комбинация. Текст преобразуется по физическим клавишам, поэтому AZERTY и QWERTZ обрабатываются корректно, а каждая пара работает в обе стороны — нажмите ту же комбинацию, когда активна вторая раскладка пары, и текст конвертируется обратно.

Окно настроек заменяет и раздел языков Windows: установка, порядок и удаление раскладок (ничего не скачивается — они уже входят в состав Windows), сочетание перебора языков и прямые сочетания на каждый язык, которые обрабатывает сама Windows, поэтому они работают даже при закрытом CyrFlip. Оба раздела один раз сохраняют предыдущее состояние для отката.

Необязательная история буфера обмена по умолчанию выключена. При включении она хранит недавние Unicode-тексты локально, с поиском по всей истории в отдельном окне; записи можно закреплять, удалять, приостанавливать захват или полностью очищать, а сама история защищена шифрованием Windows DPAPI для текущей учётной записи.

«Быстрый запуск» — второй необязательный модуль, тоже выключенный, пока вы его не включите. Он превращает ваши программы, скрипты и загрузки yt-dlp в сценарии, которые запускаются из подменю в трее, по собственной глобальной комбинации или из Jump List панели задач — даже когда CyrFlip не запущен, если закрепить его на панели. Сценарии отдельной программы OneClickRunner, которую этот модуль вобрал в себя, можно перенести при первом включении, не трогая оригиналы.

Каждую горячую клавишу можно включать и выключать независимо, можно уступать их активному клиенту удалённого рабочего стола, чтобы их обрабатывала копия CyrFlip внутри удалённого сеанса, а два переключателя не дают Windows заснуть или погасить экран, пока вы работаете.

Интерфейс доступен на 13 языках, включая языки с письмом справа налево. Работает в системном трее, потребляет мало памяти и не требует ничего доустанавливать на Windows 10/11.

Встроенный переводчик — тоже необязательный модуль, выключенный, пока вы его не включите. Выделите текст в любом приложении Windows, нажмите свою комбинацию — и перевод появится в маленьком окне у курсора мыши, дописываясь по мере того, как его пишет модель. Работает он на Ollama — бесплатной программе, которую вы один раз ставите на свой компьютер: CyrFlip её в себе не несёт, не требует ни учётной записи, ни ключа и по умолчанию обращается только к http://localhost:11434 — то есть к вашей же машине, — поэтому текст не уходит ни разработчику, ни в облако. Направления перевода — открытая таблица, как и конвертации раскладок: в каждой строке язык перевода и своя глобальная комбинация, а строка может следовать за языком интерфейса или за языком раскладки, активной в том окне, откуда вы переводите. Результат можно класть в буфер обмена, где необязательная история запишет его как обычную копию, или сразу вставлять вместо выделения — по умолчанию выключено и то и другое. Вкладка «Перевод» открывает страницу загрузки Ollama, запускает сервер, проверяет связь и загружает модель: по умолчанию qwen2.5:3b (~2 ГБ), для слабых машин gemma2:2b (~1,6 ГБ), а aya-expanse:8b (~5 ГБ) переводит заметно лучше при 8 ГБ ОЗУ и больше. Честные оговорки: качество — это качество локальной модели, первый перевод после холодного старта идёт дольше, пока модель загружается, Ollama вместе с моделью — это многогигабайтная загрузка, которую вы делаете один раз, а память под модель занимает процесс Ollama, а не CyrFlip. Выделение длиннее 4000 знаков переводится до этой границы, и окно об этом сообщает.

Конфиденциальность: CyrFlip использует перехват клавиатуры и буфер обмена только для функций, которые запускаете вы. Не ведёт журнал нажатий и не открывает ни одного сетевого соединения, пока вы не включите перевод, — а тот обращается только к серверу Ollama, который вы указали, по умолчанию к вашему же компьютеру. Открытый исходный код: https://github.com/SerZhyAle/CyrFlip
```

**Функции продукта (по одной в строке)**
```
Живой маркер раскладки у текстового курсора — работает в браузерах и чате VS Code
13 популярных языков, у каждого свой цвет; любая другая раскладка Windows тоже получает код
Транслитерация одной клавишей между QWERTY и ЙЦУКЕН, на месте, в обе стороны
Таблица конвертаций раскладок: любое число пар, у каждой своя комбинация, в обе стороны
Преобразование по физическим клавишам — AZERTY и QWERTZ обрабатываются корректно
Отдельная горячая клавиша исправляет случайный CapsLock (меняет ВЕРХНИЙ/нижний регистр)
Установка, порядок и удаление раскладок Windows без раздела языков Windows
Прямые сочетания на язык обрабатывает сама Windows — работают и при закрытом CyrFlip
Необязательная зашифрованная история буфера: поиск, закрепление, защита Windows DPAPI
Необязательный «Быстрый запуск»: программы, скрипты и загрузки yt-dlp как сценарии в один клик
Запуск сценария из трея, по своей глобальной комбинации или из Jump List панели задач
Два переключателя: не давать компьютеру заснуть и не гасить экран, пока вы работаете
Интерфейс на 13 языках, включая письмо справа налево; следует за языком Windows
Тихо работает в трее, мало памяти, ничего доустанавливать не нужно
Открытый код — без телеметрии и без сбора данных; в сеть ходит только перевод через ваш Ollama
```

**Что нового в этой версии**
```
Самый большой выпуск за всё время: пять новых модулей, каждый необязательный и выключенный, пока вы его не включите.

Конвертация раскладок теперь открытая таблица - сколько угодно пар «из раскладки в раскладку», у каждой своя комбинация клавиш. Текст переводится по физическому положению клавиш, поэтому AZERTY и QWERTZ обрабатываются правильно, и каждая пара работает в обе стороны. Прежний переворот EN/RU - просто её первая строка.

Отдельная страница настроек заменяет языковую панель Windows: устанавливайте, переставляйте и удаляйте раскладки, выбирайте комбинацию для перебора языков и назначайте прямые сочетания на конкретный язык, которые обрабатывает сама Windows - они работают, даже когда CyrFlip закрыт.

Встроенный переводчик: выделите текст, нажмите свою комбинацию - и перевод появится в небольшом окне рядом с указателем мыши, заполняясь по мере того, как его пишет модель. Работает на Ollama, бесплатной программе, которую вы один раз ставите себе сами, поэтому текст уходит на вашу же машину, а не в облако и не разработчику.

«Быстрый запуск» превращает ваши программы, скрипты и загрузки yt-dlp в сценарии, которые запускаются из трея, по собственной горячей клавише или из списка переходов на панели задач.

Два переключателя не дают Windows уснуть и погасить экран, пока вы работаете.

Интерфейс теперь говорит на 13 языках, включая языки справа налево, и по умолчанию следует за языком Windows.
```

---

## Українська (uk-UA)

**Короткий опис**
> Живий індикатор розкладки просто біля текстового курсора та перетворення тексту між будь-якими двома встановленими розкладками однією клавішею.

**Опис**
```
CyrFlip — це крихітна утиліта в системному треї Windows: вона показує активну розкладку клавіатури прямо там, де ви друкуєте, і перетворює текст між розкладками однією клавішею.

Головна функція — живий індикатор розкладки поруч із миготливим текстовим курсором, тож ви завжди бачите, якою розкладкою друкуєте, — навіть у браузерах і у вікні чату VS Code, куди більшість індикаторів не дотягується. Той самий маркер може замінювати курсор-«балку» миші та відображається на піктограмі в треї. Кожна з 13 популярних мов має власний колір, будь-яка інша розкладка Windows теж отримує свій дволітерний код, а за увімкненого CapsLock навколо маркера з'являється тонка рамка.

Виділіть текст, набраний не в тій розкладці, і натисніть гарячу клавішу: CyrFlip транслітерує його на місці між QWERTY та ЙЦУКЕН, в обидва боки й навіть для змішаного тексту. Окрема налаштовувана гаряча клавіша так само виправляє випадково увімкнений CapsLock.

Потрібні інші мови? Складіть таблицю перетворень: скільки завгодно пар «з розкладки в розкладку», кожна зі своїм глобальним сполученням. Текст перетворюється за фізичними клавішами, тому AZERTY та QWERTZ обробляються коректно, а кожна пара працює в обидва боки — натисніть те саме сполучення, коли активна друга розкладка пари, і текст перетвориться назад.

Вікно налаштувань замінює й розділ мов Windows: встановлення, порядок і видалення розкладок (нічого не завантажується — вони вже входять до складу Windows), сполучення перебору мов і прямі сполучення на кожну мову, які обробляє сама Windows, тому вони працюють навіть за закритого CyrFlip. Обидва розділи один раз зберігають попередній стан для відкату.

Необов'язкова історія буфера обміну типово вимкнена. Після ввімкнення вона зберігає недавні Unicode-тексти лише локально, з пошуком в окремому вікні; записи можна закріплювати, видаляти, призупиняти захоплення або повністю очищати, а сама історія захищена шифруванням Windows DPAPI для поточного облікового запису.

«Швидкий запуск» — другий необов'язковий модуль, теж вимкнений, доки ви його не ввімкнете. Він перетворює ваші програми, скрипти та завантаження yt-dlp на сценарії, які запускаються з підменю в треї, за власним глобальним сполученням або з Jump List панелі завдань — навіть коли CyrFlip не запущено, якщо закріпити його на панелі. Сценарії окремої програми OneClickRunner, яку цей модуль увібрав у себе, можна перенести під час першого ввімкнення, не чіпаючи оригінали.

Кожну гарячу клавішу можна вмикати й вимикати окремо, можна поступатися ними активному клієнту віддаленого робочого столу, щоб їх обробляла копія CyrFlip у віддаленому сеансі, а два перемикачі не дають Windows заснути чи погасити екран, поки ви працюєте.

Інтерфейс доступний 13 мовами, зокрема з письмом справа наліво. Працює в системному треї, споживає мало пам'яті й не потребує нічого додатково встановлювати на Windows 10/11.

Вбудований перекладач — теж необов'язковий модуль, вимкнений, доки ви його не ввімкнете. Виділіть текст у будь-якій програмі Windows, натисніть своє сполучення — і переклад з'явиться в невеликому вікні біля вказівника миші, заповнюючись у міру того, як його пише модель. Працює це на Ollama, безкоштовній програмі, яку ви один раз встановлюєте на власний комп'ютер, тому виділений текст іде на вашу ж машину, а не в хмару. Напрямки перекладу — відкрита таблиця, кожен рядок зі своїм сполученням; результат за бажанням потрапляє в буфер обміну або одразу замінює виділення. Типово вимкнено.

Конфіденційність: CyrFlip використовує перехоплення клавіатури та буфер обміну лише для функцій, які запускаєте ви. Не веде журнал натискань і не відкриває жодного мережевого з'єднання, доки ви не ввімкнете переклад, — а той звертається лише до сервера Ollama, який ви вказали, типово до вашого ж комп'ютера. Відкритий вихідний код: https://github.com/SerZhyAle/CyrFlip
```

**Функції продукту (по одній у рядку)**
```
Живий маркер розкладки біля текстового курсора — працює в браузерах і чаті VS Code
13 популярних мов, кожна зі своїм кольором; будь-яка інша розкладка Windows теж отримує код
Транслітерація однією клавішею між QWERTY та ЙЦУКЕН, на місці, в обидва боки
Таблиця перетворень розкладок: будь-яка кількість пар, кожна зі своїм сполученням, в обидва боки
Перетворення за фізичними клавішами — AZERTY та QWERTZ обробляються коректно
Окрема гаряча клавіша виправляє випадковий CapsLock (міняє ВЕРХНІЙ/нижній регістр)
Встановлення, порядок і видалення розкладок Windows без розділу мов Windows
Прямі сполучення на мову обробляє сама Windows — працюють і за закритого CyrFlip
Необов'язкова зашифрована історія буфера: пошук, закріплення, захист Windows DPAPI
Необов'язковий «Швидкий запуск»: програми, скрипти та завантаження yt-dlp як сценарії в один клік
Запуск сценарію з трея, за своїм глобальним сполученням або з Jump List панелі завдань
Два перемикачі: не давати комп'ютеру заснути й не гасити екран, поки ви працюєте
Інтерфейс 13 мовами, зокрема з письмом справа наліво; слідує за мовою Windows
Тихо працює в треї, мало пам'яті, нічого додатково встановлювати не потрібно
Відкритий код — без телеметрії та збору даних; у мережу ходить лише переклад через ваш Ollama
```

**Що нового в цій версії**
```
Найбільший випуск за весь час: п'ять нових модулів, кожен необов'язковий і вимкнений, доки ви його не ввімкнете.

Конвертація розкладок тепер відкрита таблиця - скільки завгодно пар «з розкладки в розкладку», у кожної власне сполучення клавіш. Текст перетворюється за фізичним положенням клавіш, тож AZERTY і QWERTZ обробляються правильно, і кожна пара працює в обидва боки. Колишнє перевертання EN/RU - просто її перший рядок.

Окрема сторінка налаштувань замінює мовну панель Windows: встановлюйте, переставляйте та вилучайте розкладки, обирайте сполучення для перебору мов і призначайте прямі сполучення на конкретну мову, які обробляє сама Windows - вони працюють, навіть коли CyrFlip закрито.

Вбудований перекладач: виділіть текст, натисніть своє сполучення - і переклад з'явиться в невеликому вікні біля вказівника миші, заповнюючись у міру того, як його пише модель. Працює на Ollama, безкоштовній програмі, яку ви один раз встановлюєте собі самі, тому текст іде на вашу ж машину, а не в хмару й не розробникові.

«Швидкий запуск» перетворює ваші програми, скрипти та завантаження yt-dlp на сценарії, які запускаються з трея, за власною гарячою клавішею або зі списку переходів на панелі задач.

Два перемикачі не дають Windows заснути та згасити екран, доки ви працюєте.

Інтерфейс тепер говорить 13 мовами, зокрема справа наліво, і типово стежить за мовою Windows.
```

---

## runFullTrust justification (submission field, English; reuse as-is)

```
CyrFlip is a full-trust Win32 desktop app (.NET Framework / WinForms), not a UWP app, so runFullTrust is required to run as a normal desktop process and to call the Win32 APIs its core features depend on:
- A low-level keyboard hook (WH_KEYBOARD_LL) detects the configurable hotkey that triggers the in-place transliteration. It only matches the chord; it does not log or store keystrokes.
- The clipboard (with SendInput copy/paste) reads the current selection and writes back the transliterated text for the flip the user explicitly triggers.
- SetSystemCursor / UI Automation / IAccessible2 read the caret position to draw the layout indicator next to it.
These APIs are available only to full-trust desktop apps. The app runs locally and collects no user data; it opens a network connection only when the user enables the optional translator, and then only to the Ollama address they configured (their own computer by default). Open source: https://github.com/SerZhyAle/CyrFlip
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
