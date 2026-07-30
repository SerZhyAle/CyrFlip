<!-- GENERATED copy blocks. Source of truth for Store listing copy is msix/store-listing-export.csv
     (Partner Center export-then-merge). Edit the CSV, then run msix/render-listing-mirrors.ps1 -
     it rewrites the blocks below and store/listing-*.txt from it. Never hand-edit the copy here:
     the release preflight runs the same script with -Check and fails on drift.
     The prose OUTSIDE the three locale sections (the notes above, the runFullTrust justification
     and the submission notes below) is hand-written and is never touched by the renderer. -->

# Microsoft Store listings (EN / RU / UK)

Ready-to-paste copy for the CyrFlip Store product (Partner Center → *Store listings*).
Add one listing per language: **English (en-US)**, **Russian (ru-RU)**, **Ukrainian (uk-UA)**.
The package itself ships an English UI, so its manifest declares only `en-us`; these are **listing**
translations (what shoppers read on the product page), which are independent of package resources.

Field limits: Description ≤ 10,000 chars; each Product feature ≤ 200 chars; Short description ≤ 1,000.

The export carries **all 13 listing languages**, Ukrainian included, so nothing here has to be pasted
by hand any more: *Store listings → Import* takes the CSV. The three sections below are the
paste-by-hand fallback for the day the importer refuses a file, and the ten other languages live in
the CSV and in [listing/](listing/) only.

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

A built-in translator is optional too, and off until you enable it. Select text anywhere in Windows, press its shortcut, and the translation appears in a small window next to the mouse pointer, filling in as the model writes. It runs on Ollama, a free program you install once on your own computer: CyrFlip does not bundle it, has no account and no key, and by default talks only to http://localhost:11434 - your own machine - so no text is sent to the developer or to any cloud service. Directions are an open-ended table, like the layout conversions: every row is a target language with its own global shortcut, and a row can also follow the interface language or the language of the layout active in the window you translate from. The result can be copied to the clipboard, where the optional history records it like any other copy, or pasted straight over the selection - both are off by default. The Translation tab opens the Ollama download page, starts the server, checks the connection and downloads a model: aya-expanse:8b (~4.7 GB) is the default and the best of those tested, with gemma2:9b (~5 GB) as the alternative. Every model under 4 GB failed the Russian and Ukrainian check, so gemma2:2b (~1.5 GB) is there only for a machine short on space, and it will make mistakes. Fair warning: the quality is the local model's, the first translation after a cold start takes a while as the model loads, Ollama and a model are a multi-gigabyte download you make once, and the memory the model uses belongs to the Ollama process, not to CyrFlip. Selections longer than 4000 characters are translated up to that point, and the window says so.

Every hotkey can be switched on or off independently, CyrFlip can yield them to a focused Remote Desktop client so the copy running inside the remote session handles them instead, and two keep-awake switches stop Windows sleeping or blanking the screen while you work.

The interface is available in 13 languages, right-to-left ones included. It runs in the system tray, uses little memory, and needs nothing extra installed on Windows 10/11.

Privacy: CyrFlip uses a keyboard hook and the clipboard only to detect the active layout and perform actions you trigger. It does not log keystrokes, and it opens no network connection unless you enable the translator, which talks only to the Ollama server you point it at - your own computer by default. If something goes wrong, the settings window collects CyrFlip's logs into one archive and opens a message to the author with it attached. You send the message yourself - CyrFlip transmits nothing over the network, and clipboard history never goes into the archive. Open source: https://github.com/SerZhyAle/CyrFlip
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
Two keep-awake switches: no sleep and no screen blanking while you work
Interface in 13 languages, right-to-left included; follows your Windows language
Runs quietly in the tray, low memory, nothing extra to install
Open source - no telemetry and no data collection; the only network use is your own Ollama
Optional Quick launch: your programs, scripts and yt-dlp downloads as one-click scenarios
Start a scenario from the tray, its own global hotkey, or the taskbar Jump List
Optional local translator: a selection is translated by hotkey in a window at the mouse pointer
Translation runs on Ollama on your own computer - installed separately, off until you enable it
```

**What's new in this version**
```
Translation is no longer limited to the thirteen interface languages. The picker now offers every language Windows knows, and CyrFlip makes no promise about which of them your model can actually handle - it says so plainly and links to the model's own description, because coverage belongs to the model, not to us. We dispatch the text; the model translates it.

The default model changed to aya-expanse:8b. That was decided by measurement, not by size: the previous default put Chinese characters into a Russian translation and answered Ukrainian with nonsense, and every model under 4 GB failed the same way. The new one is a larger one-time download, and English, Russian and Ukrainian are now genuinely correct.

The two keep-awake switches are remembered between launches. Leave one on and it keeps the machine awake after a restart too - CyrFlip will not watch your battery for you, and the setting says so.

The VS Code companion extension now draws the layout marker in the app's own colours for every language, instead of colouring three and leaving the rest grey.

The privacy policy is now published in all 13 interface languages, not only English.

Several settings still described the marker as EN/RU/UK. It has shown the code of any installed layout for a long time; the text now matches what the app does.
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

Встроенный переводчик — тоже необязательный модуль, выключенный, пока вы его не включите. Выделите текст в любом приложении Windows, нажмите свою комбинацию — и перевод появится в маленьком окне у курсора мыши, дописываясь по мере того, как его пишет модель. Работает он на Ollama — бесплатной программе, которую вы один раз ставите на свой компьютер: CyrFlip её в себе не несёт, не требует ни учётной записи, ни ключа и по умолчанию обращается только к http://localhost:11434 — то есть к вашей же машине, — поэтому текст не уходит ни разработчику, ни в облако. Направления перевода — открытая таблица, как и конвертации раскладок: в каждой строке язык перевода и своя глобальная комбинация, а строка может следовать за языком интерфейса или за языком раскладки, активной в том окне, откуда вы переводите. Результат можно класть в буфер обмена, где необязательная история запишет его как обычную копию, или сразу вставлять вместо выделения — по умолчанию выключено и то и другое. Вкладка «Перевод» открывает страницу загрузки Ollama, запускает сервер, проверяет связь и загружает модель: по умолчанию aya-expanse:8b (~4,7 ГБ) - лучший перевод из проверенных, рядом gemma2:9b (~5 ГБ). Все модели меньше 4 ГБ проверку на русском и украинском не прошли, поэтому gemma2:2b (~1,5 ГБ) остаётся только для машин, где мало места, и будет ошибаться. Честные оговорки: качество — это качество локальной модели, первый перевод после холодного старта идёт дольше, пока модель загружается, Ollama вместе с моделью — это многогигабайтная загрузка, которую вы делаете один раз, а память под модель занимает процесс Ollama, а не CyrFlip. Выделение длиннее 4000 знаков переводится до этой границы, и окно об этом сообщает.

Каждую горячую клавишу можно включать и выключать независимо, можно уступать их активному клиенту удалённого рабочего стола, чтобы их обрабатывала копия CyrFlip внутри удалённого сеанса, а два переключателя не дают Windows заснуть или погасить экран, пока вы работаете.

Интерфейс доступен на 13 языках, включая языки с письмом справа налево. Работает в системном трее, потребляет мало памяти и не требует ничего доустанавливать на Windows 10/11.

Конфиденциальность: CyrFlip использует перехват клавиатуры и буфер обмена только для функций, которые запускаете вы. Не ведёт журнал нажатий и не открывает ни одного сетевого соединения, пока вы не включите перевод, — а тот обращается только к серверу Ollama, который вы указали, по умолчанию к вашему же компьютеру. Если что-то пошло не так, окно настроек собирает логи CyrFlip в один архив и открывает письмо автору с этим вложением. Письмо отправляете вы сами - CyrFlip ничего не передаёт в сеть, а история буфера обмена в архив не попадает. Открытый исходный код: https://github.com/SerZhyAle/CyrFlip
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
Два переключателя: не давать компьютеру заснуть и не гасить экран, пока вы работаете
Интерфейс на 13 языках, включая письмо справа налево; следует за языком Windows
Тихо работает в трее, мало памяти, ничего доустанавливать не нужно
Открытый код — без телеметрии и без сбора данных; в сеть ходит только перевод через ваш Ollama
Необязательный «Быстрый запуск»: программы, скрипты и загрузки yt-dlp как сценарии в один клик
Запуск сценария из трея, по своей глобальной комбинации или из Jump List панели задач
Необязательный локальный перевод: выделенный текст переводится по горячей клавише в окне у курсора мыши
Перевод работает на Ollama на вашем компьютере — ставится отдельно, по умолчанию выключен
```

**Что нового в этой версии**
```
Перевод больше не ограничен тринадцатью языками интерфейса. В списке теперь все языки, которые знает Windows, и CyrFlip не обещает, какие из них потянет ваша модель: он честно об этом пишет и даёт ссылку на её описание, потому что охват языков - свойство модели, а не наше. Мы отправляем текст, переводит модель.

Модель по умолчанию заменена на aya-expanse:8b. Решено замером, а не размером: прежняя вставляла китайские иероглифы в русский перевод и отвечала бессмыслицей на украинском, и точно так же провалились все модели меньше 4 ГБ. Новая - более крупная разовая загрузка, зато английский, русский и украинский теперь действительно корректны.

Два переключателя - «не давать засыпать» и «не блокировать экран» - запоминаются между запусками. Забытый включённым не даст компьютеру уснуть и после перезапуска: следить за вашей батареей CyrFlip не станет, и подсказка так и говорит.

Расширение для VS Code рисует маркер раскладки цветами самого приложения для всех языков, а не для трёх, оставляя остальные серыми.

Политика приватности теперь опубликована на всех 13 языках интерфейса, а не только на английском.

Несколько настроек всё ещё описывали маркер как EN/RU/UK. Он давно показывает код любой установленной раскладки - текст приведён в соответствие с тем, что программа делает на самом деле.
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

Вбудований перекладач теж необов'язковий і вимкнений, доки ви його не ввімкнете. Виділіть текст будь-де у Windows, натисніть його сполучення — і переклад з'явиться в невеликому вікні біля вказівника миші, заповнюючись у міру того, як його пише модель. Працює це на Ollama, безкоштовній програмі, яку ви один раз встановлюєте на власний комп'ютер: CyrFlip її не містить, не має ні облікового запису, ні ключа й типово звертається лише до http://localhost:11434 — вашої ж машини, — тому жоден текст не йде ні розробникові, ні до будь-якої хмарної служби. Напрямки — відкрита таблиця, як і перетворення розкладок: кожен рядок це мова перекладу з власним глобальним сполученням, а рядок може ще й стежити за мовою інтерфейсу або за мовою розкладки, активної у вікні, з якого ви перекладаєте. Результат можна класти в буфер обміну, де його записує необов'язкова історія, як і будь-яку іншу копію, або одразу вставляти замість виділення — обидві опції типово вимкнені. Вкладка «Переклад» відкриває сторінку завантаження Ollama, запускає сервер, перевіряє з'єднання й отримує модель: aya-expanse:8b (~4,7 ГБ) типова - найкращий переклад із перевірених, поруч gemma2:9b (~5 ГБ). Усі моделі менші за 4 ГБ перевірки українською та російською не пройшли, тож gemma2:2b (~1,5 ГБ) лишається тільки для машин, де мало місця, і помилятиметься. Скажемо чесно: якість буде така, як у локальної моделі; перший переклад після холодного старту триває довше, бо модель завантажується; Ollama з моделлю — це кількагігабайтне завантаження, яке ви робите один раз; а пам'ять, яку займає модель, належить процесу Ollama, а не CyrFlip. Виділення, довші за 4000 символів, перекладаються до цієї межі, і вікно про це повідомляє.

Кожну гарячу клавішу можна вмикати й вимикати окремо, можна поступатися ними активному клієнту віддаленого робочого столу, щоб їх обробляла копія CyrFlip у віддаленому сеансі, а два перемикачі не дають Windows заснути чи погасити екран, поки ви працюєте.

Інтерфейс доступний 13 мовами, зокрема з письмом справа наліво. Працює в системному треї, споживає мало пам'яті й не потребує нічого додатково встановлювати на Windows 10/11.

Конфіденційність: CyrFlip використовує перехоплення клавіатури та буфер обміну лише для функцій, які запускаєте ви. Не веде журнал натискань і не відкриває жодного мережевого з'єднання, доки ви не ввімкнете переклад, — а той звертається лише до сервера Ollama, який ви вказали, типово до вашого ж комп'ютера. Якщо щось пішло не так, вікно налаштувань збирає логи CyrFlip в один архів і відкриває лист до автора з цим вкладенням. Лист надсилаєте ви самі - CyrFlip нічого не передає в мережу, а історія буфера обміну до архіву не потрапляє. Відкритий вихідний код: https://github.com/SerZhyAle/CyrFlip
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
Два перемикачі: не давати комп'ютеру заснути й не гасити екран, поки ви працюєте
Інтерфейс 13 мовами, зокрема з письмом справа наліво; слідує за мовою Windows
Тихо працює в треї, мало пам'яті, нічого додатково встановлювати не потрібно
Відкритий код — без телеметрії та збору даних; у мережу ходить лише ваш власний Ollama
Необов'язковий «Швидкий запуск»: програми, скрипти та завантаження yt-dlp як сценарії в один клік
Запуск сценарію з трея, за своїм глобальним сполученням або з Jump List панелі завдань
Необов'язковий локальний перекладач: виділення перекладається у вікні біля вказівника миші
Переклад працює на Ollama на вашому комп'ютері — встановлюється окремо, типово вимкнений
```

**Що нового в цій версії**
```
Переклад більше не обмежений тринадцятьма мовами інтерфейсу. У списку тепер усі мови, які знає Windows, і CyrFlip не обіцяє, які з них потягне ваша модель: він чесно про це пише й дає посилання на її опис, бо охоплення мов - властивість моделі, а не наша. Ми надсилаємо текст, перекладає модель.

Модель за замовчуванням змінено на aya-expanse:8b. Вирішено виміром, а не розміром: попередня вставляла китайські ієрогліфи в російський переклад і відповідала нісенітницею українською, і так само провалилися всі моделі, менші за 4 ГБ. Нова - більше одноразове завантаження, зате англійська, російська та українська тепер справді коректні.

Два перемикачі - «не давати засинати» та «не блокувати екран» - запам'ятовуються між запусками. Забутий увімкненим не дасть комп'ютеру заснути й після перезапуску: стежити за вашою батареєю CyrFlip не буде, і підказка так і каже.

Розширення для VS Code малює маркер розкладки кольорами самого застосунку для всіх мов, а не для трьох, лишаючи решту сірими.

Політику приватності тепер опубліковано всіма 13 мовами інтерфейсу, а не лише англійською.

Кілька налаштувань усе ще описували маркер як EN/RU/UK. Він давно показує код будь-якої встановленої розкладки - текст приведено у відповідність до того, що програма робить насправді.
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
