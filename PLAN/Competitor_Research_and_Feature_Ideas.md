# Анализ аналогов и идеи для развития CyrFlip

## Краткое резюме (TL;DR)

- **CyrFlip уже выигрывает в своей нише — индикатор раскладки прямо у текстового каретки.** Четырёхуровневое отслеживание каретки (Win32 `GetGUIThreadInfo` → COM UIA `TextPattern2` → IAccessible2 → managed `TextPattern`) — самое глубокое покрытие среди всех изученных инструментов. Путь IAccessible2, находящий каретку в Chromium/Electron (чат VS Code, браузеры), **не реализован нативно ни у одного из конкурентов под Windows**. Это надо защищать и развивать, а не размывать.
- **Главная стратегическая рекомендация: оставаться «manual-first» и приватным, но закрыть один-единственный реальный эргономический разрыв — конвертацию ПОСЛЕДНЕГО СЛОВА без выделения.** Это самый частый жест всей категории Punto (Pause/двойной Shift), и единственное крупное UX-преимущество, которое можно получить, не переходя к автопереключению. Всё остальное в дорожной карте — полировка.
- **Приватность — структурное, а не декларативное преимущество CyrFlip.** Нет кейлоггера/дневника, нет телеметрии, нет сетевых вызовов, нет NuGet-зависимостей рантайма, конфиг только в HKCU, один self-contained `.exe`. Это прямой ответ на главную слабость всей категории (дневники-кейлоггеры Punto/EveryLang, недоверие к Yandex, закрытость Caramba). Это надо явно рекламировать в листинге.
- **Автопереключение раскладки — НЕ для CyrFlip.** Это «магия» всей категории Punto, но и источник жалобы №1 (ложные срабатывания ломают код/игры/пароли). Manual-hotkey-философия CyrFlip уже делает его безопасным в IDE/терминалах/играх/полях паролей без списков исключений. Автопереключение можно держать как очень дальний opt-in эксперимент, но оно требует детектора + per-app исключений + защиты паролей + подавления в играх — большой многокомпонентный проект, противоречащий духу продукта.
- **Облачный перевод, AI-rewrite, спелл-чек со словарями, дневник, менеджер буфера обмена — отклонить.** Каждый нарушает либо приватность/офлайн, либо лимит размера/без-зависимостей, либо фокус продукта.
- **Низко висящие плоды полировки:** настраиваемые цвета раскладок (всё уже в `LayoutStyle.cs`), прозрачность оверлея (`Form.Opacity`), подавление в полноэкранных приложениях/играх, явный «отменить последний flip», подсветка UK в самом flip (сейчас flip только EN↔RU, хотя индикатор уже показывает UK), и кодоподпись бинарника (реальный барьер adoption из-за SmartScreen).

---

## Обзор рынка и аналогов

Сгруппировано по релевантности для CyrFlip: сначала прямые аналоги (индикатор у каретки/курсора), затем переключатели-фиксеры раскладки, затем смежные инструменты (case/translit/PowerToys/accessibility), затем веб/мобильные.

### A. Прямые аналоги — индикатор раскладки у каретки/курсора

| Название | Платформа | Лицензия | Статус | Чем интересен |
| --- | --- | --- | --- | --- |
| LangBarXX | Windows (вкл. 11) | Open-source | Активен | **Ближайший аналог:** флаг у текстовой каретки + на I-beam + тень CapsLock на флаге; per-app правила; работает в консоли/FAR/Terminal через перезапись символов; GOST 7.79; multi-monitor+DPI |
| language-indicator (yakunins) | Windows (AHK v2) | MIT | Активен | Стилизует и каретку, и I-beam по раскладке + отдельный стиль для CapsLock; заявляет работу в консоли и UWP; папка-ассетов скининг |
| Aml Maple | Windows | Платный (~$29) | Активен | Красит саму мигающую каретку по языку + флаг/имя у мыши; 30+ раскладок; встроенный flip |
| EveryLang Pro | Windows | Freemium | Активен | Маркер у текстовой каретки + у мыши, флаг/имя, прозрачность, CapsLock-оверлей, отключение в fullscreen; но дневник-кейлоггер |
| langcursor (pavel-b-kr12) | Windows (Win32) | Не указана | Заброшен (~2020) | EN/RU + CapsLock прямо в .cur-курсоре — независимое подтверждение подхода LayoutCursor; без UK |
| Layin | Windows (AHK) | MIT | Активен | Индикатор у мыши, постоянно виден над input-полем (детект по форме I-beam), вспышка вне полей — изящная политика видимости |
| Cursor Lang Indicator (akaleeroy) | Windows (AHK) | Open-source | Активен | Минимальный SetSystemCursor-референс (бел=EN, чёрн=не-EN) |
| AL-Software Keyboard Layout Indicator | Windows x64 | Freemium | Активен | Самое широкое покрытие поверхностей: плавающее окно + tooltip мыши/каретки + рамки экрана + 3 tray-иконки + RGB-подсветка; CapsLock+NumLock |
| LangCursor | macOS | Платный | Активен | Раскладка на самом курсоре (как LayoutCursor); пресеты «буква / точка / контраст / выкл» — валидирует dot-mode |
| Keyla | macOS | Платный ($0.99) | Активен | Индикатор у курсора, любая раскладка, скрытие при простое курсора |
| InputTip (abgox) | Windows 10+ (AHK) | AGPL-3.0 | Активен | 5 схем показа + rule-движок per-window; но CJK-центричный, доки на китайском |
| Comfort Keys Pro | Windows | Платный | Активен | Флаг у каретки + режим «только при переключении» (transient) |
| Language Indicator (language-indicator.com) | Windows 10/11 | Freemium | Активен | ~270KB, флаг у мыши на переключение; есть в Microsoft Store (тот же канал) |
| macOS native (Sonoma+) | macOS | Бесплатно (ОС) | Активен | Индикатор источника ввода и CapsLock ПОД кареткой — Apple валидирует идею, но юзеры жалуются на навязчивость (урок: держать маркер мелким/смещённым) |
| Windows 11 Text Cursor Indicator | Windows | Бесплатно (ОС) | Активен | Доказывает, что ОС умеет рисовать маркер у каретки во многих приложениях — но НЕ знает про раскладку (тот самый разрыв) |
| Windows native Input Indicator / Language Bar | Windows | Бесплатно (ОС) | Активен | Индикатор в трее/углу — НИКОГДА не у каретки; это и есть разрыв, который закрывает CyrFlip |

### B. Переключатели и фиксеры раскладки (категория Punto)

| Название | Платформа | Лицензия | Статус | Чем интересен |
| --- | --- | --- | --- | --- |
| Punto Switcher | Windows/macOS | Бесплатно (Yandex) | Активен, но стагнирует | Эталон автопереключения + translit/case/autoreplace/clipboard/diary; **дневник-кейлоггер + Yandex = главный анти-пример приватности** |
| Caramba Switcher | Windows/macOS | Freemium | Активен | Self-learning нейросеть, минимум настроек, детект паролей, авто-отключение в играх/IDE; от автора Punto; но без списка исключений, закрытый |
| Mahou | Windows (.NET 4.x) | GPL-2.0 | Активен | Open-source, портативный, без трекинга; convert слово/строка/выделение/N-слов; CapsSwitch; двойной Shift; tooltip у каретки; Scroll Lock LED |
| keyswitcher (FaineSwitcher) | Windows (.NET 4.8) | Open-source | Активен | Форк Mahou под EN/UA с ML-автопереключением, сеть вырезана; **тот же net48-таргет** |
| dotSwitcher | Windows | Open-source | Активен | Намеренно крошечный, аудируемый, анти-спайварь; «второй запуск открывает настройки» |
| EveryLang Pro | Windows | Freemium | Активен | All-in-one (см. секцию A) |
| LangOver | Windows | Бесплатно (adware) | Активен (~2018) | F10-flip + case-flip (Shift+F10) + reverse + Google search/translate; но Babylon Toolbar в инсталляторе |
| KeyboardSwitch (TolikPylypchuk) | Win/mac/Linux | Open-source | Активен | Кросс-платформа, использует системный список раскладок; service+settings split |
| KeyRay | Win/mac/Linux | Платный | Активен | Нативный код, AI-обработка выделения через OpenAI, детект паролей |
| Key Switcher (Mihov) | Windows | Бесплатно | Неизвестно | 24 языка; защита полей паролей |
| Arum Switcher | Windows | Бесплатно | Неизвестно | Manual-only (нет ложных срабатываний); case/clipboard-ремонт; тихие часы |
| Keyboard Ninja | Windows | Бесплатно | Прекращён | Мультибуфер, статистика набора |
| SimpleSwitcher (TkachenkoArtem) | Windows | GPL-3.0 | Активен | Чистый WinAPI, без рантайма (как net48-single-exe философия) |
| ccaps | Windows | Open-source | Активен | Single-exe, CapsLock-цикл раскладок, Scroll Lock LED, JSON-конфиг |
| Recaps | Windows | GPL-2.0 | Прекращён (~2015) | CapsLock-тап переключает, escape-hatch для реального CapsLock |
| Анетто Раскладка | Windows | Бесплатно | Неизвестно | Минимальные требования |

### C. macOS/Linux фиксеры (идеи для заимствования)

| Название | Платформа | Лицензия | Статус | Чем интересен |
| --- | --- | --- | --- | --- |
| reLayout | macOS (Win «скоро») | MIT | Активен | Подписан/нотаризован; Accessibility-запись без буфера обмена; per-app исключения; trigram opt-in; caret→line-start |
| KeyFlow | macOS | MIT | Активен | EN→RU→UK цикл; mid-word коррекция; детект secure-полей |
| KeySwitcher «Q*Й» (graninilya) | macOS | MIT | Активен | Ретроактивная цепочка слов; GOST 7.79 хоткей; авто-promotion правил после 3 правок |
| punto-switcher (rshagiev) | macOS | Open-source | Активен | «Лучше пропустить, чем вставить не туда» — safety-first; 3 стратегии замены |
| Easy Switcher | Linux | GPL-2.0 | Активен | Буфер-replay БЕЗ clipboard (kernel input) — обходит хрупкость clipboard-пайплайна |
| Lay | Linux (Wayland) | MIT | Активен | Двойной Shift, без clipboard, Rust |
| xneur / Easy Switcher | Linux | GPL | Прекращён/актив | In-place ремонт; гранулярность слово/строка/выделение |
| kbdd / xxkb / swaykbdd / GNOME / KDE | Linux | Open-source | Активен | Per-window память раскладки; kbdd публикует layoutChanged по D-Bus (как CyrFlip → layout.txt) |
| autokbisw | macOS | Apache-2.0 | Активен | Память раскладки per-device (внешняя vs встроенная клавиатура) |
| Smart Language Switcher | macOS | Freemium | Активен | Mac App Store автопереключатель |

### D. Смежные: case / транслитерация / PowerToys / accessibility / веб

| Название | Платформа | Лицензия | Статус | Чем интересен |
| --- | --- | --- | --- | --- |
| AnyCase App | Windows | Freemium | Активен | Системные хоткеи на UPPER/lower/Title/Sentence/aLt/toggle в 100+ приложениях |
| Word Shift+F3 / VS Code case | Windows/кросс | Платно/бесплатно | Активен | Базовый эталон смены регистра (tOGGLE cASE = фикс CapsLock+Shift) |
| PowerToys Keyboard Manager | Windows | MIT | Активен | Per-app (по имени процесса) ремап; chord-хоткеи; capture-dialog с поиском клавиш |
| PowerToys Advanced Paste | Windows | MIT | Активен | Архитектура «copy→transform→paste» с меню-chooser и per-action хоткеями; локальный AI через Ollama |
| PowerToys Quick Accent | Windows | MIT | Активен | Список исключённых приложений + «не активировать в Game Mode» |
| VibeNest Switcher (расширение) | Chrome/Chromium | Open-source | Активен | 12 раскладок, trigram-модель ~270KB, офлайн, MV3 zero host-perms; CapsLock-tiebreaker |
| KEset | Chrome/Edge | Бесплатно | Активен | Конвертация только выделения; Ctrl+B undo |
| Iuliia (библиотека) | Кросс (lib) | Open-source | Активен | 20 схем translit Cyrillic→Latin (GOST/ICAO/ISO9) офлайн — готовые данные для standards-translit |
| translit.site / Tembrica / translit.cc | Web | Бесплатно | Активен | ~13 стандартов translit для паспортов/URL (но онлайн = приватность) |
| ZoomText / Fusion / Win Magnifier / SuperNova | Windows | Платно/ОС | Активен | Accessibility-трекинг каретки/фокуса; locators; фокус-прямоугольник — референс качества трекинга (тоже плывут в Chromium) |
| Espanso / AutoHotkey | Кросс/Windows | GPL | Активен | Text-expander (отдельная категория, off-mission); явная layout-config у Espanso |
| DevUtl / qwerty.school / rapidtoolset / k1tok | Web | Бесплатно | Активен | Веб-фиксеры раскладки, до 10+ пар, client-side |
| Gboard / Translit Keyboard | Android/iOS | Бесплатно/freemium | Активен | Фонетический translit-ввод (другой transform, не позиционный flip) |

---

## Каталог функций (что есть у конкурентов)

### Индикация раскладки / UX индикатора

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Маркер у мигающей текстовой каретки | EveryLang, LangBarXX, Aml Maple, yakunins, InputTip, Comfort Keys, Layin, macOS Sonoma, AL-Software | **да (headline)** | Это ядро CyrFlip; 4-источниковый трекинг = лучшее покрытие. Инвестировать в консоль/терминал | must |
| Раскладка на системном I-beam (SetSystemCursor) | LangCursor, langcursor, yakunins, Aml Maple, akaleeroy, Layin, LangBarXX | **да** (opt-in, по умолч. off) | Дисциплина глобального восстановления курсора = реальный плюс над AHK-скриптами | must |
| Tray-иконка с раскладкой | Punto, Caramba, EveryLang, Mahou, LangBarXX, gxkb, Windows | **да** | Table-stakes | must |
| CapsLock у каретки (со-локация с раскладкой) | LangBarXX, EveryLang, yakunins, AL-Software, macOS Sonoma | **да** | Уже дифференциатор — редкая со-локация. Сохранить | must |
| Dot-mode (цветная точка вместо EN/RU/UK) | LangCursor, AL-Software (запрошено), yakunins | **да** | Подтверждённый спрос (топ-запрос AL-Software) | should |
| Флаги стран вместо текста EN/RU/UK | Punto, EveryLang, LangBarXX, gxkb, Aml Maple, Mahou, KDE | нет | Опционально, но флаги политически/локально щекотливы для RU/UK; нейтральный текст лучше | could |
| Пользовательские картинки маркера (папка PNG/CUR) | yakunins, gxkb, xxkb, langcursor | нет | Конфликтует с single-exe/без-ассетов; уже есть детерм. цвета | could |
| Настраиваемый размер маркера/курсора | EveryLang, LangBarXX, Aml Maple, Layin, AL-Software | **да** | Готово | should |
| Настраиваемые цвета раскладок | LangBarXX, Aml Maple, EveryLang, Layin, AL-Software, yakunins | **частично** (фикс. EN/RU/UK) | Всё в `LayoutStyle.cs`; color-picker дёшев и часто хотят | could |
| Настраиваемая прозрачность маркера | LangBarXX, Aml Maple, EveryLang, Layin, Mahou | нет | Дешёвый `Form.Opacity`; снижает навязчивость | could |
| Настраиваемое смещение маркера от каретки | EveryLang, Aml Maple, Layin, AL-Software | **частично** (фикс. вниз-вправо) | Намеренный non-obstruction выбор; low value | wont |
| Transient-режим «показ только при смене раскладки» | Comfort Keys, language-indicator.com, Layin, Input Source Pro, macOS | нет | Хорошая опциональная политика видимости (Layin: «держать над полем, мигать вне») | should |
| Дедупликация (не мигать при focus-changes внутри приложения) | Input Source Pro | **частично** | Оверлей репозиционируется только при движении каретки; проверить отсутствие мерцания | could |
| Плавающее перемещаемое окно раскладки | Punto, EveryLang, LangBarXX, AL-Software, Win Language Bar | нет | Избыточно при маркере у каретки — худшая поверхность, которую CyrFlip и побеждает | wont |
| Перекраска края экрана/таскбара/рамки окна | AL-Software, InputTip, Layout Indicator, LangBarXX | нет | Off-philosophy (CyrFlip фокальный, не периферийный) | wont |
| Индикация NumLock/ScrollLock | LangBarXX, AL-Software, Layout Indicator | нет | CapsLock — релевантный для набора; остальное шум | could |
| Раскладка через LED клавиатуры (Caps/Num/Scroll) | EveryLang, Mahou, keyswitcher, ccaps, gxkb, LangBarXX | нет | Дешёвый P/Invoke, но конфликтует с реальным ScrollLock; нишево | could |
| RGB-подсветка по раскладке (G-HUB/OpenRGB) | AL-Software | нет | Требует SDK/эксклюзивный доступ — ломает no-deps/single-exe | wont |
| Расширение VS Code: маркер у каретки Monaco | (только kbdd через D-Bus для панелей) | **да** | Уже отгружено, реальный дифференциатор | should |
| Тонировка маркера под accent цвет приложения | macOS Sonoma | нет | Юзеры Apple не любят (accent не кодирует раскладку); фикс-цвет правильнее | wont |

### Автопереключение и автокоррекция

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Авто-детект неправильной раскладки + автокоррекция при наборе | Punto, Caramba, EveryLang, Key Switcher, Mahou, keyswitcher, xneur, KeyRay, Smart LS, KeyFlow, reLayout | нет | **Крупнейшая функция категории, которой нет.** Но это жалоба №1 (ложные срабатывания). Только как явный OPT-IN (off по умолч.), и нужен детектор + per-app + защита паролей + подавление в играх/IDE = большой проект. Manual-first | could (дальний) |
| Самообучение исключений из backspace-retype | Caramba, EveryLang, KeySwitcher, langSwitcher | нет | Осмысленно только при автопереключении | wont |
| Нейро/ML-модель направления конвертации | Caramba, keyswitcher, VibeNest (trigram), reLayout (trigram) | нет | Per-char auto-detect уже чинит mixed-text; trigram (~270KB) улучшит неоднозначные выделения без срыва бюджета | could |
| Детект паролей/credentials → подавление | Caramba, Key Switcher, KeyRay, Input Source Pro, reLayout, KeyFlow | нет | Релевантно только при автопереключении; ручной flip и так инертен | wont |
| Авто-отключение в играх/IDE/терминалах | Caramba, Punto (список), EveryLang, Input Source Pro, reLayout | нет | Ручной хоткей уже безопасен; но per-app подавление индикатора/курсора полезно | could |
| Память раскладки per-window/per-app | kbdd, xxkb, swaykbdd, GNOME, KDE, Keyla, EveryLang, autokbisw | нет | Switcher-класс, вне scope индикатора+flip | wont |

### Ручная конвертация (flip)

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Хоткей-flip выделения (EN↔RU, QWERTY↔ЙЦУКЕН) | Punto, Caramba, Mahou, dotSwitcher, LangOver, KeyboardSwitch, EveryLang, xneur, Easy Switcher | **да** | Ядро, готово | must |
| Авто-детект направления (без выбора руками) | LangOver, VibeNest, kbd-layout-fix, qwerty.school, DevUtl | **да** | Готово, надёжно; per-char | must |
| Смена языка ввода после flip | Caramba, KeyboardSwitch, AHK Translator, Mahou | **да** (opt-in) | Готово (`LayoutSwitcher.cs`) | should |
| **Конвертация ПОСЛЕДНЕГО СЛОВА (без выделения)** | Punto, Caramba, Mahou, dotSwitcher, xneur, Easy Switcher, Lay | **нет** | **Самый ценный эргономический разрыв.** Захват текста от каретки до границы слова (Shift+Ctrl+Left или accessibility-range) | should |
| Конвертация последней строки / от начала строки | Mahou, xneur, Easy Switcher, EveryLang, reLayout | нет | Естественный спутник last-word (Shift+Home) | could |
| Конвертация N предыдущих слов (цифра 1-9) | Mahou, keyswitcher | нет | Нишевый power-жест; зависит от last-word | could |
| Undo/revert flip | Punto, Caramba, dotSwitcher, KEset (Ctrl+B), VibeNest, reLayout | **частично** (Ctrl+Z хоста) | Translit — свой инверс; «flip again to undo» уже работает, явный re-flip-last дёшев | should |
| Single bare-key / tap / double-tap триггер | LangOver (F10), Caramba (2×Shift), Boomkey, Lay, Mahou, Input Source Pro | **нет** (только chord) | Двойной Shift — де-факто жест жанра, очень низкое трение. Детект в KeyboardHook | should |
| CapsLock как триггер | Mahou, ccaps, Recaps, KeySwitcher, AHK | **частично** (CapsLock-поддержка добавлена) | Полный CapsLock-as-trigger с escape-hatch | could |
| Pass-through неотображённых символов | Mahou, KeyboardSwitch, VibeNest, большинство | **да** | Готово; есть static-ctor assert | must |
| Мульти-раскладки (>2 пар: DE/FR/EL/HE/PL) | VibeNest (12), DevUtl (10), Key Switcher (24), EveryLang (21) | нет | CyrFlip — EN/RU/UK by design | wont |
| **UK-транслитерация в самом flip** | (индикатор уже UK; flip — EN↔RU) | **нет** | **Самое on-mission расширение:** индикатор показывает UK, а flip только EN↔RU. Добавить UK-строку карты | should |
| Конвертация только выделенной подстроки | KEset, translit.cc, Punto | **да** | Готово | must |
| Бэкап/восстановление буфера обмена | Mahou, KeyboardSwitch, AHK, Punto | **да** | Готово (retry 3×) | must |
| Clipboard-free (buffer-replay / Accessibility-write) | Easy Switcher, Lay, reLayout, VibeNest, IA2-инструменты | нет | Clipboard-пайплайн — самая хрупкая часть; запись через IA2/UIA TextPattern обошла бы её, но non-trivial | could |

### Трансформация текста (регистр / translit / прочее)

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Инверсия регистра (фикс CapsLock) | Punto, LangOver, EveryLang, AnyCase, Word Shift+F3, KeySwitcher | **да** | Готово (self-inverse, Latin+Cyrillic) | must |
| Переключить физический CapsLock после case-flip | (никого) | **да** | **Уникально среди всех изученных** | should |
| Режимы регистра: UPPER/lower/Title/Sentence/aLt | AnyCase, EveryLang, PowerRename, Word, VS Code, онлайн | нет | Расширит case-инструмент дёшево (тот же пайплайн); Title/Sentence эвристики несовершенны | could |
| Программистские регистры (camel/snake/kebab/Pascal) | EveryLang, PowerRename, VS Code | нет | Off-mission; разработчики используют редактор | wont |
| Стандартная транслитерация (GOST 7.79/ICAO/ISO9) | LangBarXX, Punto, KeySwitcher, translit.site, Iuliia, EveryLang | нет | Другой transform; реально полезен RU/UK (паспорт/URL), офлайн-реализуем из Iuliia; приватнее веб-инструментов | could |
| Реверс текста (предложение/слова) | LangOver | нет | Дёшево, но нишево и off-theme | wont |
| Вставка как plain text | Punto, EveryLang, Arum, PowerToys | нет | Функция clipboard-менеджера, не раскладки | wont |
| Числа-в-слова / inline-math | Punto, Key Switcher, Keyboard Ninja, EveryLang | нет | Любимо, но вне scope | wont |
| CapsLock-aware tiebreaker в flip (hELLO→Hello) | VibeNest, reLayout | нет | Изящное слияние flip+case-fix; малая эвристика | could |

### Перевод / словари / поиск

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Перевод выделения (облако Google/Bing/Yandex) | EveryLang, LangOver, KeyRay, Mahou, Gboard | нет | Нарушает приватность/офлайн + сетевые deps. Отклонить | wont |
| AI/LLM-rewrite выделения | KeyRay, KeySwitcher, PowerToys | нет | Облако ломает приватность+размер; локальный LLM — внешний рантайм. Отклонить | wont |
| Спелл-чек выделения / при наборе | Punto, EveryLang, xneur, LangBarXX (Hunspell) | нет | Нужны словари (размер), off-mission. Отклонить | wont |
| Веб-поиск выделения по хоткею | Punto, LangOver, EveryLang | нет | Тривиально (ShellExecute), но scope-creep | wont |

### Продуктивность / экспандеры / буфер обмена

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Text-expander / сниппеты / autoreplace | Punto, EveryLang, Mahou, Espanso, AHK, Comfort Keys | нет | Отдельная категория (Espanso/AHK). Off-mission. Отклонить | wont |
| Менеджер/история буфера обмена | Punto, EveryLang, Keyboard Ninja, Comfort Keys | нет | Off-mission; конфликт с save/restore-only дисциплиной. Отклонить | wont |
| OCR / текст с экрана | EveryLang, PowerToys, Snipping Tool | нет | Уже commoditized ОС; вне scope. Отклонить | wont |
| Окно-выбор действия + per-action хоткеи | PowerToys Advanced Paste, SmartClick, PowerToys Run | нет | Если действий станет 3+, chooser избегает hotkey-sprawl | could |

### Per-app / контекстные правила

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Список исключений приложений (по имени процесса) | Punto, EveryLang, Mahou, LangBarXX, Input Source Pro, PowerToys, reLayout | нет | Самая хвалимая функция Punto. «Не показывать индикатор / не ставить курсор / не глотать хоткей здесь» по имени процесса | should |
| Подавление индикатора/курсора в fullscreen/играх | EveryLang, PowerToys (Game Mode), Caramba, Input Source Pro | нет | **Важно:** SetSystemCursor + topmost-оверлей — ровно то, что ненавидят геймеры. Детект fullscreen (QUERY_USER_NOTIFICATION_STATE) | should |
| Раскладка по умолчанию per-app | EveryLang, xxkb, Arum, Input Source Pro | нет | Switcher-класс, вне scope | wont |
| Per-website правила (браузер) | Input Source Pro, FlicKey | нет | Требует интеграции с браузером; вне scope | wont |

### Приватность / безопасность / доверие

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Нет кейлоггера / нет дневника | Caramba, Mahou, dotSwitcher, KeyboardSwitch, VibeNest, reLayout | **да** | Сильный дифференциатор доверия vs Punto/EveryLang. Рекламировать | must |
| Нет телеметрии / нет сетевых вызовов | Caramba (corp), dotSwitcher, VibeNest, Espanso | **да** | Заголовочный пункт доверия vs Yandex | must |
| Open-source / аудируемый | Mahou, dotSwitcher, KeyboardSwitch, VibeNest | **частично** | На GitHub с CI; усилить позиционирование «auditable, no-spyware» | should |
| Код-подпись / нотаризация (обход SmartScreen) | reLayout, ZoomText | **нет** | Реальный барьер adoption для unsigned single-exe. Стоит сертификата + шага CI | should |
| Single-instance (path-independent mutex) | ccaps, dotSwitcher | **да** | Готово | must |
| Пауза при вводе платёжных/PII данных | Caramba | нет | Релевантно только при автопереключении | wont |

### Платформа / упаковка / дистрибуция

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Один self-contained EXE, без рантайма | SimpleSwitcher, ccaps, Mahou, dotSwitcher | **да** | Ядро ограничения. Сильная история vs инсталляторы (adware LangOver) | must |
| <50MB / низкая память | Mahou, ccaps, SimpleSwitcher, Language Indicator (270KB) | **да** | Выполнено | must |
| Портативность (конфиг рядом с exe) | Mahou, dotSwitcher, SimpleSwitcher, ccaps | **частично** (HKCU) | True-portable (конфиг рядом, без реестра) для USB/locked-down | could |
| Автостарт с Windows | Punto, EveryLang, Mahou, ccaps | **да** | Готово | must |
| Microsoft Store / MSIX | Language Indicator, Smart LS | **да** | MSIX-aware пути уже есть | should |
| winget / Chocolatey | Mahou (Choco), KeyboardSwitch | **частично** | Шаблоны манифестов есть; заполнять per-release | should |
| EXE-инсталлятор (Inno) рядом с ZIP | LangOver, EveryLang | нет | Конфликтует со спецификацией «без инсталлятора»; согласовать | could |
| Авто-обновление | Caramba, Mahou, ccaps | нет | Сеть+сложность; Store/winget обновляют. Пропустить | wont |
| DPI / multi-monitor awareness | LangBarXX, ZoomText | **да** | Готово (PerMonitorV2); проверить mixed-DPI | must |
| Трёхъязычный EN/RU/UK UI+docs | Punto, Mahou, EveryLang | **да** | Готово | must |

### Статистика / accessibility-трекинг каретки / прочее

| Функция | У кого есть | Есть в CyrFlip | Оценка для CyrFlip | Приоритет |
| --- | --- | --- | --- | --- |
| Счётчики использования (flips/case-flips) | Punto, Keyboard Ninja, langSwitcher | **да** | Готово, локально | could |
| Статистика скорости набора | Punto, Keyboard Ninja | нет | Требует слежки за всеми клавишами — против no-keylog. Отклонить | wont |
| Каретка через GetGUIThreadInfo | EveryLang, Aml Maple, yakunins, Magnifier | **да** | Источник 1/4 | must |
| Каретка через UIA TextPattern2.GetCaretRange | (никого) | **да** | Дифференциатор (hand-rolled COM) | must |
| Каретка через IAccessible2 (Chromium/Electron) | (никого) | **да** | **Standout — никто из изученных нативно не повторяет** | must |
| Каретка в консоли/терминале | LangBarXX, yakunins, InputTip | **нет** | Известный разрыв; сложно (нет API); LangBarXX — перезапись символов. Low ROI | could |
| Диагностика позиции каретки | Input Source Pro, Mahou (лог) | **да** | Готово (`CaretDiagnostics`) | should |
| I-beam-shape эвристика для детекта input | Layin | нет | Дешёвый last-ditch fallback, когда все 4 источника отказали | could |
| Скрытие маркера при простое каретки | Keyla, Input Source Pro, Aml Maple | **частично** | Idle-timeout hide — доп. полировка | could |
| Звук при flip/switch | Punto, Mahou, xneur, Key Switcher, EveryLang | нет | CLAUDE.md: «no sound» намеренно; opt-in максимум | wont |
| Тост при смене раскладки | xneur, Windows OSD | нет | Избыточно с маркером у каретки | wont |
| Конфликт-реджект хоткеев (SameChord) | (немногие) | **да** | Готово, редкая надёжность | should |
| Отмена при смене foreground-окна | rshagiev | **да** | Готово (safety-first) | must |
| Публикация раскладки по IPC для внешних | kbdd (D-Bus), IBus, fcitx5 | **частично** (layout.txt) | Богатый event/named-pipe канал — low value сверх файла | could |
| Event-driven детект вместо polling | kbdd, swaykbdd, Input Source Pro | нет | Win32 без чистого global-события; poll прагматичен | wont |

---

## Чего не хватает CyrFlip — кандидаты на реализацию

### MUST (защита ядра и базовая гигиена доверия)

Строго говоря, must-функции у CyrFlip уже реализованы (индикатор у каретки, I-beam, tray, CapsLock со-локация, flip, case-flip, приватность, single-exe). «Must» здесь = **не регрессировать и явно рекламировать**:

- **Защитить и расширять 4-источниковый трекинг каретки.** Это незаменимое преимущество. Проверять mixed-DPI позиционирование оверлея. Любой рефакторинг не должен ронять путь IAccessible2.
- **Явно рекламировать приватность в листинге Store/README.** «Нет кейлоггера, нет телеметрии, нет сети, нет зависимостей, один EXE» — прямой ответ на дневники Punto/EveryLang и недоверие к Yandex. Это бесплатное маркетинговое преимущество.

### SHOULD (высокая ценность, вписывается в философию)

**1. Конвертация последнего слова без выделения**
- *Что:* хоткей чинит только что набранное слово, захватывая текст от каретки до границы слова (синтез `Shift+Ctrl+Left`, либо accessibility text-range через уже используемые IA2/UIA).
- *Зачем:* самый частый жест всей категории Punto; единственный крупный UX-выигрыш без перехода к автопереключению.
- *Вписывается:* остаётся ручным (хоткей), приватным, без зависимостей; переиспользует существующий clipboard-пайплайн и IA2-инфраструктуру.
- *Трудоёмкость:* средняя (краевые случаи буфера/выделения, границы слов в разных приложениях).
- *Риски:* в приложениях без надёжного Shift+Ctrl+Left или text-range — деградация; нужен fallback и отмена при смене окна (уже есть).

**2. UK-транслитерация в самом flip**
- *Что:* добавить раскладку UK (Ї/Є/І/Ґ и пунктуацию) в `TransliterationEngine`, чтобы flip покрывал EN↔UK, а не только EN↔RU.
- *Зачем:* индикатор уже показывает UK, но flip её не чинит — несоответствие обещанию EN/RU/UK.
- *Вписывается:* on-brand, чистая логика, покрывается unit-тестами; static-ctor assert защищает выравнивание карты.
- *Трудоёмкость:* средняя (нужно решить направление при неоднозначности RU vs UK — per-char или эвристика).
- *Риски:* неоднозначность RU/UK для общих кириллических символов; возможен trigram-tiebreaker (см. Could).

**3. Двойной Shift / single-tap триггер**
- *Что:* детект двойного нажатия Shift (или одиночной F-клавиши) как триггера flip, в дополнение к chord.
- *Зачем:* двойной Shift — де-факто жест жанра (Caramba/Mahou/Boomkey/Lay), очень низкое трение.
- *Вписывается:* расширяет `KeyboardHook`; остаётся ручным; без новых зависимостей.
- *Трудоёмкость:* средняя (тайминг double-tap, отсев ложных при обычном Shift).
- *Риски:* ложные срабатывания при быстром наборе с Shift; сделать opt-in и настраиваемым.

**4. Подавление в полноэкранных приложениях / играх**
- *Что:* детект fullscreen-окна (`SHQueryUserNotificationState` / fullscreen HWND) и авто-скрытие оверлея + отказ от SetSystemCursor там.
- *Зачем:* topmost-оверлей и глобальная подмена курсора — именно то, что ломает игровой опыт.
- *Вписывается:* чистый P/Invoke, без зависимостей; усиливает «безопасен везде».
- *Трудоёмкость:* средняя.
- *Риски:* ложный детект fullscreen в некоторых приложениях; держать как настройку.

**5. Per-app список исключений**
- *Что:* по имени процесса foreground-окна — «не показывать индикатор / не ставить курсор / не глотать хоткей».
- *Зачем:* самая хвалимая функция Punto и самый ощутимый пробел Caramba; полезно для игр/IDE/fullscreen.
- *Вписывается:* конфиг в HKCU; модель — как PowerToys (имя процесса).
- *Трудоёмкость:* средняя.
- *Риски:* UX списка в tray-меню; держать простым.

**6. Явный «отменить последний flip»**
- *Что:* хоткей re-flip-last выделения (translit — свой инверс, так что повторный flip уже откатывает, если выделение сохранилось).
- *Зачем:* успокаивающая страховка; почти бесплатно.
- *Вписывается:* тривиально, переиспользует пайплайн.
- *Трудоёмкость:* низкая.
- *Риски:* минимальны (зависит от сохранения выделения).

**7. Код-подпись бинарника**
- *Что:* Authenticode-сертификат + шаг подписи в CI.
- *Зачем:* реальный барьер adoption — unsigned single-exe ловит SmartScreen; доверие для Store/winget.
- *Вписывается:* процесс/стоимость, не код; не трогает приватность/размер.
- *Трудоёмкость:* средняя (организационная, не техническая).
- *Риски:* стоимость сертификата; OV/EV выбор.

**8. winget-манифест per release; усилить open-source позиционирование**
- *Что:* заполнять `winget/` на каждый релиз; в README явно «auditable, no spyware» как dotSwitcher.
- *Трудоёмкость:* низкая.

### COULD (приятно, если ресурсы есть)

- **Настраиваемые цвета раскладок (color-picker).** Всё в `LayoutStyle.cs`; дёшево, часто хотят. Трудоёмкость низкая.
- **Прозрачность оверлея.** `Form.Opacity`-слайдер; снижает навязчивость. Низкая.
- **Transient-режим видимости** (мигать при смене, держать над input-полем — модель Layin). Средняя.
- **Конвертация последней строки / N слов.** Спутник last-word (Shift+Home / цифра 1-9). Средняя; зависит от #1.
- **CapsLock-aware tiebreaker в flip** (hELLO→Hello при равенстве очков). Малая эвристика. Низкая.
- **Дополнительные режимы регистра** (UPPER/lower/Title/Sentence) за тем же пайплайном. Средняя; держать продукт сфокусированным.
- **Standards-транслитерация (GOST 7.79 / ICAO) Cyrillic→Latin** офлайн из открытых данных Iuliia — приватный аналог веб-инструментов для паспортов/URL. Средняя.
- **Trigram-модель направления (~270KB)** для неоднозначных выделений и RU/UK tiebreaker (как VibeNest). Средняя; только если появятся жалобы на точность.
- **I-beam-shape эвристика** как last-ditch fallback, когда все 4 источника каретки отказали. Низкая.
- **Idle-timeout скрытие маркера.** Низкая.
- **True-portable режим** (конфиг рядом с exe, без реестра) для USB/locked-down. Низкая-средняя.
- **Каретка в консоли/терминале** (перезапись символов как LangBarXX). Высокая трудоёмкость, low ROI.
- **Богатый IPC-канал** (named pipe / event) для внешних виджетов сверх layout.txt. Средняя; low value.
- **Окно-выбор действия** — если действий станет 3+ (translit-to-Latin, case-modes), chooser избегает hotkey-sprawl. Средняя.

### WONT (конфликтует с философией — отклонить осознанно)

| Функция | Почему отклонить |
| --- | --- |
| Автопереключение раскладки при наборе (в продакшн) | Жалоба №1 категории; ломает код/игры/пароли; требует детектора + per-app + защиты паролей + подавления = большой проект, противоречащий manual-first. Только дальний opt-in эксперимент |
| Облачный перевод (Google/Bing/Yandex) | Отправляет текст с устройства — против privacy-first/no-telemetry; сетевые deps |
| AI/LLM-rewrite (облако или локальный рантайм) | Облако ломает приватность; локальный LLM = внешний рантайм, ломает single-exe/<50MB/no-deps |
| Спелл-чек со словарями | Данные словарей (размер), off-mission |
| Дневник / кейлоггер / статистика набора | Против no-keylog — это сама причина существования CyrFlip как доверенной альтернативы |
| Менеджер истории буфера обмена | Off-mission; конфликт с save/restore-only дисциплиной clipboard |
| Text-expander / сниппеты | Отдельная категория (Espanso/AHK) |
| OCR | Commoditized ОС (Snipping Tool) |
| Флаги стран | Политически/локально щекотливо для RU/UK; нейтральный текст/точка лучше |
| RGB-подсветка / LED-приоритет | SDK/эксклюзивный доступ к устройству — ломает no-deps; конфликт с реальным ScrollLock |
| Плавающее окно / перекраска края экрана / таскбара | Off-philosophy — CyrFlip фокальный (у каретки), не периферийный; это худшие поверхности, которые он и побеждает |
| Память раскладки per-window/per-app, раскладка по умолчанию per-app | Switcher-класс с собственным UX, вне scope индикатора+flip |
| Авто-обновление в приложении | Сеть+сложность; Store/winget обновляют |
| Тонировка под accent цвет приложения | Не кодирует раскладку (юзеры Apple не любят) |
| Звук / тост при смене раскладки | «No sound» намеренно; маркер у каретки уже сообщает состояние |

---

## Уникальные преимущества CyrFlip

Эти дифференциаторы надо защищать и на них опираться в позиционировании:

1. **Четырёхуровневый трекинг каретки** (Win32 `GetGUIThreadInfo` → COM UIA `TextPattern2.GetCaretRange` → IAccessible2 → managed `TextPattern`) — самое глубокое at-caret покрытие из всех изученных инструментов. Путь **IAccessible2, находящий каретку в Chromium/Electron** (чат VS Code, браузеры), эмпирически подтверждён и **не повторяется нативно ни одним конкурентом под Windows** — большинство останавливаются на Win32-каретке + UIA и молча отказывают в Electron.

2. **CapsLock со-локирован С маркером раскладки у каретки** (1px рамка цвета раскладки / тёмное кольцо в dot-mode) на всех трёх поверхностях одновременно. Конкуренты либо опускают CapsLock у каретки, либо показывают отдельным центрированным тостом, либо разносят раскладку и CapsLock по несвязанным механизмам.

3. **Три живо-управляемые поверхности из одного события** `LayoutChanged(code, capsOn)` — системный I-beam, оверлей у каретки И tray-иконка, синхронно. Большинство конкурентов делают одну поверхность (только курсор как LangCursor, или только трей как ОС).

4. **Документированная дисциплина глобального восстановления `SetSystemCursor`** — дефолтные курсоры перезагружаются на Dispose, ApplicationExit, ProcessExit и UnhandledException; единственный gap — жёсткий TerminateProcess. AHK/gist-инструменты подмены курсора печально известны тем, что оставляют курсор сломанным при падении скрипта.

5. **Расширение-компаньон для VS Code** рисует маркер ТОЧНО у каретки Monaco через абсолютно позиционированную after-декорацию — точный in-editor ответ на неспособность UIA найти каретку Monaco. Ни один нативный конкурент под Windows не соединяет приложение с расширением редактора подобным образом.

6. **«Переключить физический CapsLock после case-flip»** (тоггл реальной клавиши после коррекции, чтобы дальнейший набор совпал) — **уникально среди всех изученных инструментов**.

7. **Структурно чистая приватность, а не декларируемая:** нет кейлоггера/дневника, нет телеметрии, нет сетевых вызовов, нет NuGet-зависимостей рантайма, конфиг только в HKCU, один self-contained net48 EXE. Прямой ответ на определяющую слабость всей категории (дневники-кейлоггеры Punto/EveryLang; недоверие к Yandex/российским вендорам; закрытость Caramba «доверяй на слово»).

8. **Per-char двунаправленный авто-детект транслитерации** — чинит mixed/любое направление за один проход, карта 26↔26 защищена static-ctor assert (ловушка спецификации с лишними 28 кириллическими буквами явно обработана) — надёжнее множества веб-конвертеров, требующих выбирать направление вручную.

9. **Реджект конфликтующих хоткеев** (`Hotkey.SameChord`) и **отмена при смене foreground-окна** mid-operation — детали надёжности, которых нет у большинства конкурентов.

10. **Встроенная «Диагностика позиции каретки»** (14 снимков за ~7с каждого источника каретки) для триажа «здесь нет маркера» — отладочный аффорданс, которого нет у конкурентов.

11. **Manual-hotkey-first философия** намеренно обходит жалобу №1 всей категории Punto/auto-switch (ложные автокоррекции ломают код/игры/пароли), делая CyrFlip безопасным в IDE/терминалах/играх/полях паролей **без списков исключений и эвристик детекта паролей**.

12. **Детерминированный яркий HSL-цвет** для любого кода раскладки сверх EN/RU/UK — произвольные раскладки получают отдельный читаемый маркер без ручных ассетов.

---

## Предлагаемая дорожная карта

### Фаза 1 — Ближайшее (закрыть UX-разрыв + гигиена доверия)
Цель: устранить единственный реальный эргономический пробел и снять барьеры adoption, не трогая философию.

- **Конвертация последнего слова без выделения** (should #1) — флагманская UX-доработка.
- **UK-транслитерация в самом flip** (should #2) — выполнить обещание EN/RU/UK.
- **Явный «отменить последний flip»** (should #6) — дёшево, успокаивает.
- **Код-подпись бинарника + winget-манифест per release** (should #7, #8) — снять SmartScreen, усилить доверие.
- Рекламная правка README/Store: явный privacy-блок.

### Фаза 2 — Среднесрочное (полировка индикатора + контекст)
Цель: сделать индикатор тише/настраиваемее и безопаснее в играх; снизить трение триггера.

- **Подавление в fullscreen/играх** (should #4) — критично для геймеров.
- **Per-app список исключений** (should #5).
- **Двойной Shift / single-tap триггер** (should #3) — opt-in.
- **Настраиваемые цвета раскладок + прозрачность оверлея** (could) — низкая трудоёмкость, частый запрос.
- **Transient-режим видимости** (could) — модель Layin.
- **Конвертация последней строки** (could) — спутник last-word.

### Фаза 3 — Дальнее / Эксперименты (осторожно, за флагами)
Цель: точечные улучшения и исследования, не размывающие ядро.

- **Standards-транслитерация (GOST 7.79 / ICAO) офлайн из Iuliia** (could) — отдельный хоткей, on-mission для RU/UK.
- **Trigram-модель направления (~270KB)** (could) — RU/UK tiebreaker и неоднозначные выделения; только при жалобах на точность.
- **CapsLock-aware tiebreaker + доп. режимы регистра** (could) — слияние flip+case.
- **I-beam-shape fallback + idle-timeout hide + true-portable режим** (could).
- **Каретка в консоли/терминале** (could, high effort) — только если будет спрос.
- **Clipboard-free запись через IA2/UIA TextPattern** (could, high effort) — устранение самой хрупкой части пайплайна.
- **Автопереключение раскладки** — держать как дальний, чётко-помеченный OPT-IN эксперимент (off по умолчанию), ТОЛЬКО с детектором + per-app + защитой паролей + подавлением в играх/IDE. Не отгружать, пока эти защиты не готовы; иначе CyrFlip унаследует жалобу №1 категории и потеряет своё manual-first преимущество.

**Не делать никогда:** облачный перевод, AI-rewrite, спелл-чек со словарями, дневник/кейлоггер, менеджер буфера обмена, text-expander, OCR, RGB/LED, флаги стран, периферийные индикаторы (край экрана/таскбар/плавающее окно), память раскладки per-window, авто-обновление в приложении, звук/тосты.

---

## Источники

- https://yandex.ru/soft/punto/win/
- https://ru.wikipedia.org/wiki/Punto_Switcher
- https://spy-soft.net/punto-switcher-shpion/
- https://elims.org.ua/blog/punto-switcher-kak-kejloger-ili-pochemu-ya-ego-nedolyublivayu/
- https://www.file.net/process/punto.exe.html
- https://caramba-switcher.com/
- https://grokipedia.com/page/Punto_Switcher
- https://blog.avast.com/yandex-and-data-privacy
- https://caramba-switcher.com/mac
- https://ru.wikipedia.org/wiki/Caramba_Switcher
- https://www.iguides.ru/main/os/caramba_switcher/
- https://alternativeto.net/software/caramba-switcher/
- https://apps.apple.com/us/app/caramba-switcher-autocorrect/id1565826179
- https://everylang.net/
- https://everylang.net/help
- https://everylang.net/price
- https://alternativeto.net/software/everylang/about/
- https://habr.com/ru/post/424313/
- https://github.com/iamkarlson/Mahou
- https://gitea.com/BladeMight/Mahou/wiki/Functions-list
- https://github.com/BladeMight/Mahou
- https://community.chocolatey.org/packages/Mahou
- https://langover.com/
- https://www.softpedia.com/get/Office-tools/Other-Office-Tools/LangOver.shtml
- https://github.com/Krot66/LangBarXX
- https://www.comfortsoftware.com/comfort-keys/
- https://www.keyray.ru/
- https://github.com/yakunins/language-indicator
- http://www.amlpages.com/amlmaple.shtml
- https://al-soft.com/keyboard-layout-indicator/
- https://keyboard-layout-indicator.en.uptodown.com/windows
- https://language-indicator.com/
- https://www.softpedia.com/get/Desktop-Enhancements/Other-Desktop-Enhancements/Layout-Indicator.shtml
- https://gist.github.com/akaleeroy/23a6d0323f3ae0ff4e2bc7962534cc0c
- https://github.com/abgox/InputTip
- https://learn.microsoft.com/en-us/answers/questions/3743227/the-text-cursor-indicator
- https://inputsource.pro/
- https://github.com/runjuu/InputSourcePro
- https://langcursor.com/en/
- https://apps.apple.com/us/app/keyla-keyboard-indicator/id6479205015
- https://macreports.com/how-to-disable-the-popup-for-switching-input-sources-on-mac/
- https://github.com/ohueter/autokbisw
- https://github.com/qnikst/kbdd
- https://sourceforge.net/projects/xxkb/
- https://github.com/zen-tools/gxkb
- https://github.com/artemsen/swaykbdd
- https://help.gnome.org/users/gnome-help/stable/keyboard-layouts.html.en
- https://extensions.gnome.org/extension/596/per-window-keyboard-layout/
- https://docs.kde.org/stable_kf6/en/plasma-desktop/kcontrol/keyboard/layouts.html
- https://fcitx-im.org/wiki/Fcitx5
- https://docs.oracle.com/cd/E53394_01/html/E54757/glmks.html
- https://xneur.ru/settings/
- https://github.com/freemind001/easy-switcher
- https://abit.ee/en/soft/lay-keyboard-layout-gnome-wayland-rust-linux-autocorrect-open-source-en
- https://github.com/unconditional/layin
- https://github.com/pavel-b-kr12/langcursor
- https://www.autohotkey.com/boards/viewtopic.php?t=5088
- https://github.com/dspinellis/kbd-layout-fix
- https://github.com/alexantoshuk/kbdf
- http://www.script-coding.com/AutoHotkey/AhkRussianEng.html
- https://dev.to/koddr/productivity-for-coders-caps-lock-as-a-keyboard-layout-switcher-2lme
- https://github.com/FaineSwitcher/keyswitcher
- https://github.com/TolikPylypchuk/KeyboardSwitch
- https://github.com/TkachenkoArtem/SimpleSwitcher
- https://github.com/reg2005/langSwitcher
- https://github.com/victor-homyakov/recaps
- https://github.com/holgertkey/ccaps
- https://github.com/kurumpa/dotSwitcher
- https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager
- https://learn.microsoft.com/en-us/windows/powertoys/text-extractor
- https://learn.microsoft.com/en-us/windows/powertoys/quick-accent
- https://learn.microsoft.com/en-us/windows/powertoys/advanced-paste
- https://learn.microsoft.com/en-us/windows/powertoys/powerrename
- https://learn.microsoft.com/en-us/windows/powertoys/run
- https://support.microsoft.com/en-us/windows/manage-the-language-and-keyboard-input-layout-settings-in-windows-12a10cb4-8626-9b77-0ccb-5013e0c7c7a2
- https://www.bleepingcomputer.com/news/microsoft/windows-11-snipping-tool-gets-ocr-support-to-copy-text-from-images/
- https://sugarsweetapps.com/blog/shortcut-for-all-caps-works-in-any-windows-program/
- https://www.softpedia.com/get/Office-tools/Other-Office-Tools/Heinrich-Case-Changer.shtml
- https://www.autohotkey.com/docs/commands/StringLower.htm
- https://github.com/microsoft/PowerToys/issues/45127
- https://learn.microsoft.com/en-us/visualstudio/ide/how-to-change-text-case-in-the-editor?view=vs-2022
- https://www.translit.site/en/type/icao
- https://tembrica.com/en/russian-transliteration
- https://github.com/nalgeon/iuliia
- https://caseconverter.cc/
- https://chromewebstore.google.com/detail/keyboard-layout-switcher/nfnbpbkloajooggeceohpajmbolflifp
- https://github.com/NikitaBabenko/Switcher
- https://chromewebstore.google.com/detail/keset-fix-gibberish-from/dagpfdeohfadedgdedeclamngoodefio
- https://chromewebstore.google.com/detail/keyboard-converter/ldmiahkgcjadamdkodehdfldddepnbko
- https://github.com/graninilya/keyswitcher
- https://relayout.forfutdinov.com/
- https://github.com/rshagiev/punto-switcher
- https://apps.apple.com/us/app/smart-language-switcher/id1597566195
- https://github.com/weird-mirror/keyflow
- https://support.google.com/gboard/answer/7068494
- https://apps.apple.com/ca/app/translit-keyboard/id928742619
- https://github.com/k1tok/layout-switch-translator
- https://translit.cc/
- https://espanso.org/
- https://www.autohotkey.com/docs/v1/Hotstrings.htm
- https://vispero.com/zoomtext-screen-magnifier-software/
- https://vispero.com/fusion-accessibility-software/
- https://support.microsoft.com/en-us/accessibility/windows/magnifier/use-magnifier-to-make-things-on-the-screen-easier-to-see
- https://github.com/microsoft/vscode/issues/105558
- https://yourdolphin.com/SuperNova
- https://support.apple.com/guide/mac-help/change-zoom-advanced-options-accessibility-mh35715/mac
- https://magnifier.sourceforge.net/
- https://qwerty.school/tools/switcher
- https://en.web-tool.org/change-keyboard-layout/
- https://devutl.com/keyboard-layout-switcher/
- https://keyboardguides.com/tool/keyboard-layout-converter/
- https://rapidtoolset.com/en/tool/keyboard-layout-fixer
- https://awsm-tools.com/keyboard-layout