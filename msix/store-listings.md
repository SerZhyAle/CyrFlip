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
> A tray toolkit for your keyboard and desktop: a live layout marker at the caret, one-key fixes for wrong-layout and CapsLock text and a screen-region capture - plus an optional selection menu, local translator, quick notes, clipboard history and quick launch.

**Description**
```
CyrFlip is a tray toolkit for your keyboard and desktop. Out of the box it keeps you in the right layout and fixes text typed in the wrong one; everything else is an optional module you switch on when you need it.

Type in the right layout. A live layout marker sits next to the blinking text caret, so you always know which layout you are about to type in - even in browsers and the VS Code chat box, where most indicators can't reach. The same marker can replace the I-beam mouse cursor and is shown on the tray icon. Each of the 13 curated languages has its own colour, any other Windows layout still gets its two-letter code, and CapsLock adds a thin frame around the marker.

Select text typed in the wrong layout and press Ctrl+Shift+F12: CyrFlip converts it in place, in both directions and even for mixed text. Add as many "from layout to layout" pairs as you like, each with its own shortcut; text is converted by physical key position, so AZERTY and QWERTZ are handled correctly. A separate hotkey fixes accidental CapsLock. The Windows languages tab installs, reorders and removes keyboard layouts (nothing is downloaded - they ship with Windows) and assigns per-language shortcuts that Windows itself handles, so they keep working even when CyrFlip is closed.

Act on what you selected. Hold Ctrl and right-click a selection, and CyrFlip's own menu opens at the pointer: copy, cut and paste, your layout conversions, the case fix, translation, Quick launch and the clipboard history - without taking the focus, so the selection survives. A keyboard chord opens the same menu at the caret. Off by default.

The built-in translator is optional too. Select text anywhere in Windows, press its shortcut, and the translation appears in a small window next to the mouse pointer, filling in as the model writes. It runs on Ollama, a free program you install once on your own computer: CyrFlip does not bundle it, has no account and no key, and by default talks only to http://localhost:11434 - your own machine. Directions are an open table with a shortcut each, and the result can be copied to the clipboard or pasted over the selection. aya-expanse:8b (~4.7 GB) is the default model. Fair warning: the quality is the local model's, the first translation after a cold start takes a while, and the memory the model uses belongs to the Ollama process, not to CyrFlip.

Keep what you copy and write. The optional clipboard history keeps recent text locally, searchable in its own window; entries can be pinned, deleted, paused or cleared, and every record is encrypted with Windows DPAPI for your Windows account. Optional quick notes open with Ctrl+Shift+Alt+N and keep text exactly as given - no Markdown, no auto-formatting - so a fragment of code pastes back unchanged; search looks inside every note, and the notes are encrypted on disk too. Notes and clipboard history move to another computer or a phone as one plain-text file you export and import yourself.

Launch and capture. Ctrl+Shift+PrintScreen freezes the screen, you drag a region, and the picture lands on the clipboard - optionally also saved as a PNG to a folder. Ctrl+Shift+Alt+PrintScreen opens that folder while saving is on. Quick launch, an optional module, turns your programs, scripts and yt-dlp downloads into scenarios you start from the tray, from a hotkey of their own or from the taskbar Jump List; scenarios from the standalone OneClickRunner tool can be imported without touching the originals. Two keep-awake switches stop Windows sleeping or blanking the screen while you work.

Yours alone. Optional modules stay off until you switch them on, every hotkey can be switched off on its own, and CyrFlip can hand its hotkeys to a focused Remote Desktop window. The interface is available in 13 languages, right-to-left ones included, with a light, dark or Windows-matching theme. It runs in the system tray, uses little memory, and needs nothing extra installed on Windows 10/11.

Privacy: CyrFlip uses a keyboard hook and the clipboard only to detect the active layout and perform actions you trigger. It does not log keystrokes, and it opens no network connection unless you enable the translator, which talks only to the Ollama server you point it at - your own computer by default. If something goes wrong, the settings window collects CyrFlip's logs into one archive and opens a message to the author with it attached. You send the message yourself - CyrFlip transmits nothing over the network, and clipboard history never goes into the archive. Open source: https://github.com/SerZhyAle/CyrFlip

On your first launch, a five-step welcome tour introduces CyrFlip and offers to open Settings on General for initial setup. Skip or close it to keep the defaults. Updates do not show it again; replay it from About. Start with Windows stays off until you choose it.

On General, export CyrFlip settings to JSON, preview changes before importing, or restore settings before the last import. The three newest snapshots stay locally. The file excludes notes, clipboard history, launcher scenarios, machine paths and Windows system settings. Enabling history or notes and sending selections to another translation server require separate consent.
```

**Product features (one per line)**
```
Live layout marker next to the text caret - works in browsers and the VS Code chat
13 curated languages, each with its own colour; any other Windows layout gets its code too
One key fixes text typed in the wrong layout, in place and in both directions
A table of layout conversions: any pairs, each with its own shortcut, by physical key position
Separate hotkey fixes accidental CapsLock (swaps UPPER/lower case)
Install, reorder and remove Windows keyboard layouts; per-language shortcuts that Windows itself handles
Optional menu over a selection at Ctrl + right click: copy, convert, fix case, translate, launch
Optional local translator: a selection is translated by hotkey in a window at the mouse pointer
Translation runs on Ollama on your own computer - installed separately, off until you enable it
Optional encrypted clipboard history with search, pins and Windows DPAPI protection
Optional quick notes that keep text exactly as given, so code pastes back unchanged; encrypted on disk
Move notes and clipboard history to another computer in one plain-text exchange file
Screen-region capture to the clipboard on Ctrl+Shift+PrintScreen, optionally saved as PNG
Optional Quick launch: programs, scripts and yt-dlp downloads as one-click scenarios
Start a scenario from the tray, its own global hotkey, or the taskbar Jump List
Two keep-awake switches: no sleep and no screen blanking while you work
Light, dark or Windows-matching theme for every window, dialog and menu
Interface in 13 languages, right-to-left included; follows your Windows language
Runs quietly in the tray, low memory, nothing extra to install
Open source - no telemetry and no data collection; the only network use is your own Ollama
```

**What's new in this version**
```
The marker's colour now names the keyboard layout, not only the language: each of the 25 layouts of the 13 curated languages has its own shade of its language's colour - Russian is always red, but Russian Typewriter is a different red - and every other Windows layout shares one neutral colour. In the dot style that colour is the whole marker.

Beside the caret and on the mouse pointer the marker is translucent, so the text under it stays readable, and its letters are fitted to the badge with one pixel of border. The I-beam and the tray icon stay solid.

No more double marker in VS Code: while the companion extension is drawing, CyrFlip hides its own. It returns in the chat panel, the terminal and the search boxes, where the extension cannot draw at all.

Mixed text is fixed in one press - the direction is now decided for each character, so "ghbdtnпривет" comes back as "приветghbdtn" instead of doubling the first half.

Punctuation is now your choice. A key that gives a symbol in both layouts says nothing about which layout you meant, so "Convert punctuation along with the text" can be switched off: "/ghbdtn" then keeps its slash instead of starting with a full stop.

Synchronizing CapsLock after the case fix sets the key instead of toggling it, so correcting the same text twice no longer leaves it backwards. And the About tab now shows the build version.
```

---

## Русский (ru-RU)

**Краткое описание**
> Набор инструментов в трее для клавиатуры и рабочего стола: живой маркер раскладки у каретки, исправление текста в неверной раскладке и с CapsLock одной клавишей и снимок области экрана - плюс необязательные меню над выделением, локальный переводчик, быстрые заметки, история буфера и быстрый запуск.

**Описание**
```
CyrFlip - набор инструментов в трее для клавиатуры и рабочего стола. Сразу после запуска он следит, чтобы вы печатали в нужной раскладке, и исправляет текст, набранный в неверной; всё остальное - необязательные модули, которые вы включаете, когда они нужны.

Печатать в нужной раскладке. Живой маркер раскладки стоит рядом с мигающей кареткой, поэтому вы всегда знаете, в какой раскладке сейчас начнёте печатать, - даже в браузерах и в поле чата VS Code, куда большинство индикаторов не дотягивается. Тот же маркер может заменить текстовый курсор мыши (I-beam) и показывается на значке в трее. У каждого из 13 популярных языков свой цвет, любая другая раскладка Windows всё равно получает свой двухбуквенный код, а при включённом CapsLock вокруг маркера появляется тонкая рамка.

Выделите текст, набранный не в той раскладке, и нажмите Ctrl+Shift+F12: CyrFlip исправит его на месте, в обе стороны и даже в смешанном тексте. Добавьте сколько угодно пар «из раскладки в раскладку», каждую со своим сочетанием; текст преобразуется по физическому положению клавиш, поэтому AZERTY и QWERTZ обрабатываются правильно. Отдельная горячая клавиша исправляет случайно включённый CapsLock. Вкладка «Языки Windows» устанавливает, упорядочивает и удаляет раскладки (ничего не скачивается - они входят в Windows) и назначает прямые сочетания на язык, которые обрабатывает сама Windows, поэтому они работают даже при закрытом CyrFlip.

Действовать над выделенным. Удерживайте Ctrl и щёлкните правой кнопкой по выделению - у указателя откроется собственное меню CyrFlip: копировать, вырезать и вставить, ваши конвертации раскладок, исправление регистра, перевод, «Быстрый запуск» и история буфера - и всё это не забирая фокус, поэтому выделение сохраняется. Сочетание клавиш открывает то же меню у каретки. По умолчанию выключено.

Встроенный переводчик тоже необязателен. Выделите текст в любом месте Windows, нажмите его сочетание - и перевод появится в маленьком окне рядом с указателем мыши, дописываясь по мере того, как его пишет модель. Он работает на Ollama - бесплатной программе, которую вы один раз ставите на свой компьютер: CyrFlip её не включает в себя, не требует ни аккаунта, ни ключа и по умолчанию обращается только к http://localhost:11434 - то есть к вашей же машине. Направления перевода - открытая таблица со своим сочетанием у каждой строки, а результат можно скопировать в буфер обмена или вставить вместо выделения. Модель по умолчанию - aya-expanse:8b (~4,7 ГБ). Честно предупреждаем: качество перевода - это качество локальной модели, первый перевод после холодного старта занимает время, а память под модель занимает процесс Ollama, а не CyrFlip.

Сохранять скопированное и записанное. Необязательная история буфера хранит недавний текст локально, с поиском в отдельном окне; записи можно закрепить, удалить, поставить запись на паузу или очистить, и каждая запись шифруется Windows DPAPI для вашей учётной записи. Необязательные быстрые заметки открываются по Ctrl+Shift+Alt+N и хранят текст ровно таким, каким его дали, - без Markdown и автоформатирования, - поэтому фрагмент кода вставляется обратно без изменений; поиск смотрит внутрь каждой заметки, и на диске заметки тоже зашифрованы. Заметки и историю буфера можно перенести на другой компьютер или телефон одним текстовым файлом, который вы сами экспортируете и импортируете.

Запуск и снимок экрана. Ctrl+Shift+PrintScreen замораживает экран, вы выделяете область - и картинка попадает в буфер обмена, а по желанию сохраняется ещё и в PNG в папку. Пока включено сохранение, Ctrl+Shift+Alt+PrintScreen открывает эту папку. «Быстрый запуск», необязательный модуль, превращает ваши программы, скрипты и загрузки yt-dlp в сценарии, которые запускаются из трея, по собственному сочетанию или из Jump List панели задач; сценарии отдельной программы OneClickRunner можно импортировать, не трогая оригиналы. Два переключателя не дают Windows уснуть или погасить экран, пока вы работаете.

Только ваше. Необязательные модули выключены, пока вы их не включите, любую горячую клавишу можно выключить отдельно, а CyrFlip умеет отдавать свои сочетания активному окну удалённого рабочего стола. Интерфейс доступен на 13 языках, включая письмо справа налево, со светлой, тёмной или как в Windows темой. Программа работает в системном трее, расходует мало памяти и ничего не требует установить дополнительно на Windows 10/11.

Конфиденциальность: CyrFlip использует перехват клавиатуры и буфер обмена только для функций, которые запускаете вы. Не ведёт журнал нажатий и не открывает ни одного сетевого соединения, пока вы не включите перевод, — а тот обращается только к серверу Ollama, который вы указали, по умолчанию к вашему же компьютеру. Если что-то пошло не так, окно настроек собирает логи CyrFlip в один архив и открывает письмо автору с этим вложением. Письмо отправляете вы сами - CyrFlip ничего не передаёт в сеть, а история буфера обмена в архив не попадает. Открытый исходный код: https://github.com/SerZhyAle/CyrFlip

При первом запуске знакомство из пяти шагов представляет CyrFlip и предлагает открыть общие настройки для первоначальной настройки. Пропустите или закройте его, чтобы оставить значения по умолчанию. Обновления не показывают его снова; повторить можно на странице «О программе». Автозапуск остаётся выключенным, пока вы его не выберете.

На странице «Общие» можно экспортировать настройки CyrFlip в JSON, просмотреть изменения перед импортом и вернуть настройки до импорта. Три последних снимка сохраняются локально. Файл не содержит заметок, истории буфера, сценариев запуска, путей этого компьютера или настроек Windows. Включение записи истории и заметок, а также адрес другого сервера перевода требуют отдельного согласия.
```

**Функции продукта (по одной в строке)**
```
Живой маркер раскладки рядом с текстовой кареткой - работает в браузерах и в чате VS Code
13 популярных языков со своим цветом; любая другая раскладка Windows тоже получает свой код
Одна клавиша исправляет текст в неверной раскладке, на месте и в обе стороны
Таблица конвертаций раскладок: любые пары со своим сочетанием, по физическому положению клавиш
Отдельная горячая клавиша исправляет случайный CapsLock (меняет ПРОПИСНЫЕ/строчные)
Установка, порядок и удаление раскладок Windows; прямые сочетания на язык, которые обрабатывает сама Windows
Необязательное меню над выделением по Ctrl и правому клику: копировать, конвертировать, регистр, перевод, запуск
Необязательный локальный переводчик: выделение переводится по сочетанию в окне у указателя мыши
Перевод работает на Ollama на вашем компьютере - ставится отдельно, выключен, пока вы его не включите
Необязательная зашифрованная история буфера с поиском, закреплением и защитой Windows DPAPI
Необязательные быстрые заметки хранят текст ровно таким, каким его дали, - код вставляется без правок; шифруются на диске
Перенос заметок и истории буфера на другой компьютер одним открытым текстовым файлом
Снимок области экрана в буфер обмена по Ctrl+Shift+PrintScreen, по желанию с сохранением в PNG
Необязательный «Быстрый запуск»: программы, скрипты и загрузки yt-dlp как сценарии в один клик
Запуск сценария из трея, по его собственному глобальному сочетанию или из Jump List панели задач
Два переключателя «не спать»: без перехода в сон и без гашения экрана, пока вы работаете
Светлая, тёмная или как в Windows тема для всех окон, диалогов и меню
Интерфейс на 13 языках, включая письмо справа налево; следует языку Windows
Тихо работает в трее, мало памяти, ничего не нужно устанавливать дополнительно
Открытый исходный код - без телеметрии и сбора данных; сеть использует только ваш собственный Ollama
```

**Что нового в этой версии**
```
Цвет маркера теперь называет раскладку, а не только язык: у каждой из 25 раскладок 13 отобранных языков свой оттенок цвета её языка - русский всегда красный, но «Русская (машинопись)» уже другой красный, - а все остальные раскладки Windows получили один нейтральный цвет. В стиле «точка» этот цвет и есть весь маркер.

Рядом с кареткой и на указателе мыши маркер стал полупрозрачным, так что текст под ним остаётся читаемым, а его буквы вписаны в значок с одним пикселем поля. Текстовый курсор и значок в трее остаются непрозрачными.

Двойного маркера в VS Code больше нет: пока рисует расширение, CyrFlip прячет свой. В панели чата, в терминале и в полях поиска, где расширение рисовать не может, маркер возвращается.

Смешанный текст исправляется одним нажатием: направление выбирается теперь для каждого символа, поэтому «ghbdtnпривет» превращается в «приветghbdtn», а не удваивает первую половину.

Знаки препинания теперь ваш выбор. Клавиша, дающая знак в обеих раскладках, не говорит о том, в какой из них её нажали, поэтому «Конвертировать знаки препинания вместе с текстом» можно выключить: тогда «/ghbdtn» сохранит свой слеш вместо того, чтобы начаться с точки.

Синхронизация CapsLock после исправления регистра задаёт состояние клавиши, а не переключает его, поэтому повторное исправление того же текста больше не оставляет её в обратном положении. А на вкладке «О программе» показана версия сборки.
```

---

## Українська (uk-UA)

**Короткий опис**
> Набір інструментів у треї для клавіатури та робочого столу: живий маркер розкладки біля каретки, виправлення тексту в хибній розкладці й із CapsLock однією клавішею та знімок області екрана - плюс необов'язкові меню над виділенням, локальний перекладач, швидкі нотатки, історія буфера й швидкий запуск.

**Опис**
```
CyrFlip - набір інструментів у треї для клавіатури та робочого столу. Одразу після запуску він стежить, щоб ви друкували в потрібній розкладці, і виправляє текст, набраний у хибній; усе інше - необов'язкові модулі, які ви вмикаєте, коли вони потрібні.

Друкувати в потрібній розкладці. Живий маркер розкладки стоїть поруч із миготливою кареткою, тож ви завжди знаєте, якою розкладкою зараз почнете друкувати, - навіть у браузерах і в полі чату VS Code, куди більшість індикаторів не дотягується. Той самий маркер може замінити текстовий курсор миші (I-beam) і показується на значку в треї. Кожна з 13 популярних мов має власний колір, будь-яка інша розкладка Windows однаково отримує свій дволітерний код, а за увімкненого CapsLock навколо маркера з'являється тонка рамка.

Виділіть текст, набраний не в тій розкладці, і натисніть Ctrl+Shift+F12: CyrFlip виправить його на місці, в обидва боки й навіть у змішаному тексті. Додайте скільки завгодно пар «з розкладки в розкладку», кожну з власним сполученням; текст перетворюється за фізичним положенням клавіш, тому AZERTY та QWERTZ обробляються правильно. Окрема гаряча клавіша виправляє випадково увімкнений CapsLock. Вкладка «Мови Windows» встановлює, упорядковує й видаляє розкладки (нічого не завантажується - вони входять до Windows) і призначає прямі сполучення на мову, які обробляє сама Windows, тож вони працюють навіть за закритого CyrFlip.

Діяти над виділеним. Утримуйте Ctrl і клацніть правою кнопкою по виділенню - біля вказівника відкриється власне меню CyrFlip: копіювати, вирізати й вставити, ваші перетворення розкладок, виправлення регістру, переклад, «Швидкий запуск» та історія буфера - і все це не забираючи фокус, тож виділення зберігається. Сполучення клавіш відкриває те саме меню біля каретки. Типово вимкнено.

Вбудований перекладач теж необов'язковий. Виділіть текст у будь-якому місці Windows, натисніть його сполучення - і переклад з'явиться в маленькому вікні поруч із вказівником миші, дописуючись у міру того, як його пише модель. Він працює на Ollama - безкоштовній програмі, яку ви один раз встановлюєте на свій комп'ютер: CyrFlip її не містить, не потребує ні облікового запису, ні ключа й типово звертається лише до http://localhost:11434 - тобто до вашої ж машини. Напрямки перекладу - відкрита таблиця з власним сполученням у кожного рядка, а результат можна скопіювати в буфер обміну або вставити замість виділення. Модель за замовчуванням - aya-expanse:8b (~4,7 ГБ). Чесно попереджаємо: якість перекладу - це якість локальної моделі, перший переклад після холодного старту займає час, а пам'ять під модель займає процес Ollama, а не CyrFlip.

Зберігати скопійоване й записане. Необов'язкова історія буфера зберігає нещодавній текст локально, з пошуком в окремому вікні; записи можна закріпити, видалити, поставити запис на паузу або очистити, і кожен запис шифрується Windows DPAPI для вашого облікового запису. Необов'язкові швидкі нотатки відкриваються за Ctrl+Shift+Alt+N і зберігають текст рівно таким, яким його дали, - без Markdown і автоформатування, - тож фрагмент коду вставляється назад без змін; пошук дивиться всередину кожної нотатки, і на диску нотатки теж зашифровані. Нотатки та історію буфера можна перенести на інший комп'ютер або телефон одним текстовим файлом, який ви самі експортуєте та імпортуєте.

Запуск і знімок екрана. Ctrl+Shift+PrintScreen заморожує екран, ви виділяєте область - і зображення потрапляє в буфер обміну, а за бажанням зберігається ще й у PNG у теку. Поки ввімкнено збереження, Ctrl+Shift+Alt+PrintScreen відкриває цю теку. «Швидкий запуск», необов'язковий модуль, перетворює ваші програми, скрипти та завантаження yt-dlp на сценарії, які запускаються з трея, за власним сполученням або з Jump List панелі завдань; сценарії окремої програми OneClickRunner можна імпортувати, не чіпаючи оригіналів. Два перемикачі не дають Windows заснути чи погасити екран, поки ви працюєте.

Лише ваше. Необов'язкові модулі вимкнені, доки ви їх не ввімкнете, будь-яку гарячу клавішу можна вимкнути окремо, а CyrFlip уміє віддавати свої сполучення активному вікну віддаленого робочого столу. Інтерфейс доступний 13 мовами, зокрема з письмом справа наліво, зі світлою, темною або як у Windows темою. Програма працює в системному треї, витрачає мало пам'яті й нічого не потребує встановлювати додатково на Windows 10/11.

Конфіденційність: CyrFlip використовує перехоплення клавіатури та буфер обміну лише для функцій, які запускаєте ви. Не веде журнал натискань і не відкриває жодного мережевого з'єднання, доки ви не ввімкнете переклад, — а той звертається лише до сервера Ollama, який ви вказали, типово до вашого ж комп'ютера. Якщо щось пішло не так, вікно налаштувань збирає логи CyrFlip в один архів і відкриває лист до автора з цим вкладенням. Лист надсилаєте ви самі - CyrFlip нічого не передає в мережу, а історія буфера обміну до архіву не потрапляє. Відкритий вихідний код: https://github.com/SerZhyAle/CyrFlip

На сторінці «Загальні» можна експортувати налаштування CyrFlip у JSON, переглянути зміни перед імпортом і відновити налаштування до імпорту. Три останні знімки зберігаються локально. Файл не містить нотаток, історії буфера, сценаріїв запуску, шляхів цього комп’ютера чи налаштувань Windows. Увімкнення запису історії й нотаток та адреса іншого сервера перекладу потребують окремої згоди.
```

**Функції продукту (по одній у рядку)**
```
Живий маркер розкладки поруч із текстовою кареткою - працює в браузерах і в чаті VS Code
13 популярних мов із власним кольором; будь-яка інша розкладка Windows теж отримує свій код
Одна клавіша виправляє текст у хибній розкладці, на місці й в обидва боки
Таблиця перетворень розкладок: будь-які пари з власним сполученням, за фізичним положенням клавіш
Окрема гаряча клавіша виправляє випадковий CapsLock (міняє ВЕЛИКІ/малі літери)
Встановлення, порядок і видалення розкладок Windows; прямі сполучення на мову, які обробляє сама Windows
Необов'язкове меню над виділенням за Ctrl і правим кліком: копіювати, перетворити, регістр, переклад, запуск
Необов'язковий локальний перекладач: виділення перекладається за сполученням у вікні біля вказівника миші
Переклад працює на Ollama на вашому комп'ютері - встановлюється окремо, вимкнений, доки ви його не ввімкнете
Необов'язкова зашифрована історія буфера з пошуком, закріпленням і захистом Windows DPAPI
Необов'язкові швидкі нотатки зберігають текст рівно таким, яким його дали, - код вставляється без правок; шифруються на диску
Перенесення нотаток та історії буфера на інший комп'ютер одним відкритим текстовим файлом
Знімок області екрана в буфер обміну за Ctrl+Shift+PrintScreen, за бажанням зі збереженням у PNG
Необов'язковий «Швидкий запуск»: програми, скрипти та завантаження yt-dlp як сценарії в один клік
Запуск сценарію з трея, за його власним глобальним сполученням або з Jump List панелі завдань
Два перемикачі «не спати»: без переходу в сон і без гасіння екрана, поки ви працюєте
Світла, темна або як у Windows тема для всіх вікон, діалогів і меню
Інтерфейс 13 мовами, зокрема з письмом справа наліво; стежить за мовою Windows
Тихо працює в треї, мало пам'яті, нічого не треба встановлювати додатково
Відкритий вихідний код - без телеметрії та збору даних; мережу використовує лише ваш власний Ollama
```

**Що нового в цій версії**
```
Колір маркера тепер називає розкладку, а не лише мову: кожна з 25 розкладок 13 відібраних мов має власний відтінок кольору своєї мови - російська завжди червона, але «Російська (друкарська машинка)» вже інший червоний, - а всі інші розкладки Windows дістали один нейтральний колір. У стилі «крапка» цей колір і є весь маркер.

Поруч із кареткою та на вказівнику миші маркер став напівпрозорим, тож текст під ним лишається читабельним, а його літери вписані у значок з одним пікселем поля. Текстовий курсор і піктограма в треї лишаються непрозорими.

Подвійного маркера у VS Code більше немає: поки малює розширення, CyrFlip ховає свій. У панелі чату, в терміналі та в полях пошуку, де розширення малювати не може, маркер повертається.

Змішаний текст виправляється одним натисканням: напрям обирається тепер для кожного символу, тому «ghbdtnпривет» перетворюється на «приветghbdtn», а не подвоює першу половину.

Розділові знаки тепер ваш вибір. Клавіша, що дає знак в обох розкладках, не каже, в якій із них її натиснули, тому «Перетворювати розділові знаки разом із текстом» можна вимкнути: тоді «/ghbdtn» збереже свій слеш замість того, щоб початися з крапки.

Синхронізація CapsLock після виправлення регістру задає стан клавіші, а не перемикає його, тож повторне виправлення того самого тексту більше не лишає її у зворотному положенні. А на вкладці «Про програму» показано версію збірки.
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
4. **Properties / Age ratings / Pricing** - unchanged.
5. If asked again, paste the **runFullTrust justification** above.
6. **Submit to the Store.** Certification usually takes a few business days; a keyboard-hook app can draw extra review - the justification + clear description pre-empt most questions.
