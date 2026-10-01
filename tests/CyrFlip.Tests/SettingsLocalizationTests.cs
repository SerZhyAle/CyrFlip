using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Regression guard for the localization layer. Three complementary checks:
    ///   1. every string registered in <see cref="Localization"/> has a non-empty translation in
    ///      <b>every</b> supported language - a missing cell would silently fall back to English;
    ///   2. every dynamically built string listed in <see cref="DynamicStrings"/> is actually
    ///      registered (the control-tree walk cannot reach those rows, so nothing else would catch it);
    ///   3. the live settings window is built in each language and walked: any surviving Cyrillic in a
    ///      non-Cyrillic language is an untranslated static label (OS-provided layout/language names
    ///      are excluded, and the "ЙЦУКЕН" layout name is allowed everywhere).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds the real window - see SharedGdiCollection
    public class SettingsLocalizationTests
    {
        private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]");

        /// <summary>Languages whose own script is Cyrillic, so surviving Cyrillic proves nothing.</summary>
        private static readonly HashSet<string> CyrillicUi = new HashSet<string> { "Русский", "Українська" };

        private static readonly string[] DynamicStrings =
        {
            "По умолчанию", "Удалить", "Добавить раскладку", "Введите язык или название раскладки для фильтра",
            "Маленькая", "Средняя", "Крупная",
            "Добавить", "Отмена", "Удалить раскладку «{0}» из Windows?",
            "Windows должна оставить хотя бы одну раскладку — эту удалить нельзя.",
            "Изменение применено к раскладкам Windows. Обычно оно вступает в силу сразу; если что-то выглядит не так, выйдите из Windows и войдите снова.",
            "Вернуть раскладки Windows в исходное состояние?", "Не назначено", "Задать...", "Очистить",
            "{0} → раскладка {1} (не установлена)", "Сочетание для переключения на язык",
            "Комбинация {0} уже занята горячей клавишей CyrFlip «{1}» — Windows её не получит.",
            "Комбинация {0} уже назначена языку «{1}».",
            "В Windows не осталось свободных слотов для языковых сочетаний.",
            "Не удалось записать сочетание в реестр Windows.",
            "Сочетание записано в настройки Windows — обрабатывать его будет система, а не CyrFlip. Если оно не сработало сразу, выйдите из Windows и войдите снова.",
            "Вернуть языковые сочетания Windows в исходное состояние?", "Исправить CapsLock",
            "Менеджер буфера", "Удалить всю сохранённую историю буфера?",
            // The conversion table (rows are rebuilt per refresh, so the walk can't see them).
            "Пара раскладок", "Комбинация", "Таблица пуста — ни одна комбинация сейчас не конвертирует текст.",
            "Изменить...", "(не установлена)", "Для конвертации установите как минимум две раскладки.",
            "Комбинация {0} уже занята действием «{1}».",
            // Tray menu and tooltip (no control tree at all).
            "Показать историю", "Скрыть историю", "История буфера", "Курсор: индикатор раскладки",
            "Каретка: метка раскладки", "Каретка: стиль точки", "Настройки...", "Выход",
            "горячие клавиши выключены", "регистр", "менеджер буфера",
            "экран не гаснет", "не даёт засыпать",
            "Задать хоткей регистра", "Задать хоткей истории буфера",
            "Эта комбинация уже занята {0}. Их нельзя объединять — выберите другую.",
            "хоткеем регистра", "конвертацией раскладок", "другим хоткеем CyrFlip",
            // Dialogs and windows built outside the settings control tree.
            "Новая комбинация (модификатор обязателен):", "Новая конвертация раскладок", "Изменить конвертацию",
            "Из раскладки:", "В раскладку:", "Комбинация:", "Сочетание конвертации",
            "Исходная и целевая раскладки должны отличаться.",
            "Сначала задайте комбинацию клавиш.", "Комбинация «{0}» не распознана.",
            "Часть настроек CyrFlip не удалось прочитать - они оставлены как есть.",
            "Введите не менее 3 символов:", "Текст", "Дата", "Источник", "Закрыть", "Вернуть в буфер",
            // The exchange file (ticket S0023): the flow's dialogs and its import report.
            "Экспорт в файл обмена", "Импорт из файла обмена", "Файл обмена CyrFlip", "Все файлы",
            "Перенос...", "Экспортировать...", "Импортировать...",
            "Экспортировано заметок: {0}, записей истории: {1}.", "Не удалось прочитать файл: {0}",
            "В файле нет ни одной заметки или записи истории, которую можно импортировать.",
            "Заметок без Id в файле: {0} - похоже, они написаны вручную. Импортировать их как новые заметки?",
            "Найдено заметок: {0}, записей истории: {1}.",
            "Быстрые заметки выключены - заметки из файла будут пропущены.",
            "История буфера выключена - записи истории будут пропущены.",
            "Будет добавлено: заметок {0}, записей истории {1}.",
            "Будет обновлено заметок (в файле более новая версия): {0}.",
            "Уже есть: заметок {0}, записей истории {1}.", "Не удалось прочитать блоков: {0}.", "Импортировать нечего.",
            "Импорт только добавляет и обновляет - ничего из текущих данных не удаляется. Импортировать?",
            "Импорт завершён: добавлено заметок {0}, обновлено {1}, добавлено записей истории {2}.",
            "Файл записан другой версией формата обмена CyrFlip. Обновите CyrFlip, чтобы его импортировать.",
            "Файл больше {0} МБ - такой файл не импортируется.", "Не найден маркер «{0}» - это не файл обмена CyrFlip.",
            "Строка {0}: {1}", "и ещё {0}", "у блока нет текста в ограждении из обратных кавычек",
            "ограждение текста не закрыто - остаток файла не прочитан", "поле «{0}» отсутствует или записано неверно",
            "текст не совпадает со своим Id - он изменён или повреждён", "текст больше допустимого размера",
            "строка метаданных длиннее {0} символов", "больше {0} объектов - остальные не импортированы",
            "Введите минимум 3 символа для поиска по части текста.", "Совпадений не найдено.", "Найдено: {0}",
            "Ничего не выделено. Я переворачиваю текст, а не воздух — сначала выделите что-нибудь.",
            "Не удалось прочитать или заменить выделение. У буфера обмена были другие планы.",
            "Выделение слишком большое для конвертации (больше миллиона символов). Выделите часть.",
            "Другая операция с буфером ещё не закончилась. Повторите через секунду.",
            "Фрагмент слишком велик для истории (>128 КБ).", "Не удалось изменить автозапуск Windows:",
            "Идёт запись ~7 секунд. Щёлкните в непокорное поле ввода и наберите что-нибудь или подвигайте каретку — покажите, где она прячется.",
            "Диагностика каретки сохранена — укрытие каретки раскрыто. Открываю:",
            "Диагностика каретки не удалась (каретка выиграла этот раунд):",
            "Спокойно — одна диагностика уже идёт.",
            // The scenario launcher (list rows, dialogs, tray submenu, Jump List tasks, balloons -
            // all built outside the walked static control tree).
            "Быстрый запуск", "Имя", "Путь", "Аргументы", "Рабочая папка", "Админ",
            "Не прочитано файлов: {0}", "{0} (копия)", "Удалить сценарий «{0}»?",
            "Запустить", "Клонировать", "Экспорт...", "Импорт...", "Добавить...",
            "Импорт из OneClickRunner...", "Сценарии OneClickRunner не найдены.",
            "Экспортировать сценарий в XML", "Сценарий «{0}» экспортирован.", "Не удалось экспортировать: {0}",
            "Выберите XML-файл сценария", "Не удалось импортировать: {0}", "Импортирован сценарий «{0}».",
            "Его комбинация уже занята, поэтому не перенесена.",
            "Найдены сценарии OneClickRunner ({0} шт.). Перенести их в CyrFlip? Исходные файлы останутся без изменений.",
            "Перенесено сценариев: {0}.", "Пропущено повреждённых файлов: {0}.",
            "Из-за совпадения идентификаторов назначены новые: {0}.", "Калькулятор",
            "Управление сценариями...", "Выход из CyrFlip", "сценарием быстрого запуска",
            "Не удалось запустить «{0}»: {1}", "У сценария не задан путь.", "Файл не найден: {0}",
            "«{0}» не найден ни как файл, ни в PATH.",
            "Сценарию yt-dlp нужен запрос ссылки, недоступный в этом контексте.",
            "Ссылка содержит недопустимые символы (кавычки или управляющие).",
            "Ссылка должна быть адресом http:// или https://.",
            "Эти параметры не будут переданы yt-dlp: допустимы только латинские буквы, цифры, пробел и символы + / [ ] < > = * . _ , -",
            "yt-dlp не найден в PATH. Установите его, чтобы команда yt-dlp работала в терминале.",
            "Не удалось использовать папку загрузки «{0}»: {1}",
            "Сценарий не найден — обновите Jump List, открыв CyrFlip.",
            "Загрузка yt-dlp", "Ссылка для скачивания:", "Начать",
            "Новый сценарий", "Изменить сценарий", "Тип:", "Имя:", "Программа или скрипт",
            "yt-dlp: скачать по ссылке", "Путь:", "Аргументы:", "Рабочая папка:", "Обзор...",
            "Запускать от имени администратора", "Папка загрузки (пусто = Загрузки):",
            "Доп. параметры yt-dlp:",
            "Ссылка запрашивается при каждом запуске. Программа yt-dlp должна быть доступна в PATH.",
            "Комбинация запуска сценария", "Выберите программу или скрипт", "Выберите рабочую папку",
            "Выберите папку загрузки", "Укажите имя сценария.", "Укажите путь к программе или скрипту.",
            // The translator (table rows, the Ollama status line, the result window, the tray entry -
            // none of which the walk over the settings control tree can reach).
            "Перевод", "На язык", "Направлений пока нет — добавьте первое, чтобы назначить комбинацию.",
            "Ollama уже установлен — нажмите «Запустить Ollama».",
            "Открываю сайт Ollama — установите его оттуда.",
            "Скачать и установить Ollama? Это несколько сотен мегабайт, загрузка может занять время.",
            "Скачиваю Ollama...", "Скачиваю Ollama:", "Запускаю установщик Ollama...",
            "Не удалось скачать. Открываю сайт Ollama...",
            "Ollama не установлен — нажмите «Установить Ollama».", "Запускаю Ollama...",
            "Не удалось запустить Ollama.", "Ollama не запущен — нажмите «Запустить Ollama».",
            "Ollama не найден — нажмите «Установить Ollama».", "Укажите имя модели, например aya-expanse:8b.",
            "Загружаю модель:", "Модель установлена:", "Не удалось загрузить модель. Ollama запущен?",
            "Ollama работает. Установленные модели:",
            "Ollama работает, но ни одна модель не установлена — нажмите «Загрузить модель».",
            "Новое направление перевода", "Изменить направление перевода", "Переводить на:",
            "Комбинация для перевода", "Копировать", "Вставить", "Ещё раз", "Настройки",
            "Язык интерфейса", "Язык активной раскладки",
            "Перевести буфер обмена", "В буфере обмена нет текста для перевода.",
            "Перевожу… (Esc — отмена)", "переведены первые {0} из {1} символов",
            "Фокус сменился — вставьте перевод вручную.", "Нет текста для перевода.",
            "Не найден Ollama — локальный переводчик. Его нужно установить один раз.",
            "Ollama не запущен.",
            "Не установлена ни одна модель. Рекомендуем aya-expanse:8b (~4,7 ГБ) — загрузите её в настройках.",
            "Ollama ответил ошибкой:", "Ollama не принял запрос. Проверьте адрес сервера и модель в настройках.",
            "Модель не ответила вовремя. Возможно, она слишком велика для этого компьютера.",
            "Модель не справилась с этим текстом.", "переводом текста",
            // S0010: the partial answer's note, the remote server that does not answer, the refused installer.
            "модель перестала отвечать - перевод может быть неполным",
            "Сервер {0} не отвечает. Проверьте адрес в настройках и что Ollama там запущен.",
            "Скачанный установщик не подписан Ollama - он удалён. Открываю сайт Ollama...",
            // "Send logs to the author": the pre-send dialog and the two answers for a machine whose
            // mail client cannot take an attachment from a link.
            "Логи для автора", "Архив с логами собран:", "Файл", "Размер", "Что внутри",
            "обрезан — сохранён только конец файла", "не вошёл — превышен общий размер",
            "Письмо отправляете вы сами - CyrFlip ничего не передаёт в сеть. История буфера обмена и быстрые заметки в архив не включены; пути и аргументы сценариев, заголовки окон и выделенный текст в логи не пишутся. Внутри логов встречаются пути к файлам - привычные уже без имени вашей учётной записи Windows, но непривычный путь его может сохранить.",
            // What each collected file holds (S0010 TD-1) - SupportBundle.Contents.
            "версия, Windows, раскладки, включённые функции и настройки CyrFlip",
            "запуски сценариев: имя, тип, результат - без путей и аргументов",
            "контекстное меню: команды, классы окон и числа - без текста",
            "переводы: языки, модель, длины и исход - без текста",
            "быстрые заметки: только счётчики", "история буфера: только счётчики",
            "конвертации выделения: длины и имена процессов - без текста",
            "диагностика каретки: классы окон и процессы - заголовки только длиной",
            "код текущей раскладки",
            "Создать письмо", "Открыть папку с архивом", "Не удалось собрать архив с логами:",
            "Ваша почтовая программа не принимает вложение из ссылки. Письмо открыто, а архив выделен в проводнике — перетащите его в письмо перед отправкой.",
            "Не удалось открыть почтовую программу. Отправьте архив вручную на адрес:",
            // The quick notes: the window is built on demand and lives outside the settings control
            // tree entirely, so every one of its captions has to be listed here.
            "Быстрые заметки", "Заметка", "Список", "Пункт", "Создана", "Имя (необязательно)",
            "Добавить пункт", "Удалить пункт", "Копировать для Google Keep",
            "Заметок: {0}", "(пусто)", "Новая заметка", "Создана {0}, изменена {1}",
            "Фрагмент больше {0} КБ и в заметку не помещается.",
            "Отметки выполнения при переходе к тексту не сохранятся. Продолжить?",
            "Пункты списка не хранят переносы строк: текст вернётся с обычными переводами строк. Продолжить?",
            "Текст скопирован. Создайте заметку в Google Keep и вставьте его.",
            "Отметки выполнения в Google Keep не переносятся.", "Открыть Google Keep в браузере?",
            "Экспорт заметки", "Экспорт всех заметок", "Текстовый файл",
            "Заметка сохранена: {0}", "Заметки сохранены: {0}",
            "Этот файл не защищён DPAPI — его прочитает любой, у кого есть доступ к папке.",
            "Не удалось сохранить файл: {0}", "Удалить эту заметку? Отменить удаление нельзя.",
            "Удалить все быстрые заметки, журнал и резервную копию? Отменить это нельзя.",
            "Заметки хранятся только на этом компьютере и шифруются DPAPI. Не храните здесь пароли, боевые токены и приватные ключи.",
            "Сохранить выделение в быстрые заметки", "Задать хоткей быстрых заметок",
            "быстрые заметки", "быстрыми заметками",
            "Не прочитано записей быстрых заметок: {0}. Остальные заметки на месте.",
            "Заметки хранятся только на этом компьютере, в зашифрованном DPAPI файле вашей учётной записи Windows. CyrFlip не отправляет их в сеть и ничем их не индексирует.",
            "Это не хранилище секретов: не сохраняйте здесь пароли, боевые токены и приватные ключи.",
            // The About tab's version line: filled in by ApplyLanguage rather than built as a
            // static caption, so the control-tree walk sees it already translated.
            "Версия {0}",
        };

        [Fact]
        public void EveryRegisteredStringIsTranslatedIntoEverySupportedLanguage()
        {
            var problems = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                // Values are parallel to Names[1..], i.e. every language except the Russian key itself.
                Assert.Equal(Localization.Names.Length - 1, entry.Value.Length);
                for (int i = 0; i < entry.Value.Length; i++)
                    if (string.IsNullOrWhiteSpace(entry.Value[i]))
                        problems.Add($"[{Localization.Names[i + 1]}] missing: \"{Trim(entry.Key)}\"");
            }
            Assert.True(problems.Count == 0, "Untranslated strings:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void EveryDynamicStringIsRegisteredInTheLocalizationTable()
        {
            var known = new HashSet<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All) known.Add(entry.Key);

            var missing = new List<string>();
            foreach (string source in DynamicStrings)
                if (!known.Contains(source)) missing.Add(Trim(source));

            Assert.True(missing.Count == 0,
                "These strings are shown to users but have no entry in Localization:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void EveryUserFacingStringTranslatesOutOfRussian()
        {
            var problems = new List<string>();
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { foreach (string language in Localization.Names) Run(language, problems); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            // A window that could not be built is not an untranslated one (S0029 RB-1): say so under
            // its own heading, or the report sends the reader hunting for a missing translation.
            Assert.True(failure == null, "The settings window could not be built or walked:\n" + failure);
            Assert.True(problems.Count == 0, "Untranslated user-facing strings:\n" + string.Join("\n", problems));
        }

        private static void Run(string language, List<string> problems)
        {
            bool cyrillicScript = CyrillicUi.Contains(language);
            Type type = typeof(SettingsForm);
            // With the exchange rows, so the walk sees their captions (ticket S0023).
            object form = TestForms.NewSettings(new AppConfig { UiLanguage = language }, withExchange: true);
            try
            {
                MethodInfo translate = type.GetMethod("Translate", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (string src in DynamicStrings)
                {
                    string outp = (string)translate.Invoke(form, new object[] { src })!;
                    // A translation that legitimately equals its Russian source (proper nouns, "EN ⇄ RU")
                    // is a hit, not a miss; the real failure is Cyrillic surviving in a Latin-script build.
                    if (!cyrillicScript && Cyrillic.IsMatch(StripAllowed(outp)))
                        problems.Add($"[{language}] dynamic: \"{Trim(src)}\" -> \"{Trim(outp)}\"");
                }

                if (!cyrillicScript)
                {
                    var skip = new HashSet<object>();
                    // The screenshot folder box shows a path - the user's data, whatever script it is in (S0026).
                    foreach (string field in new[] { "_layoutRows", "_languageRows", "_uiLanguage", "_screenshotFolderBox" })
                        if (type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(form) is object c)
                            skip.Add(c);
                    Walk(language, form, skip, problems);
                }
                MnemonicWalk(language, (System.Windows.Forms.Control)form, problems);
            }
            finally { (form as IDisposable)?.Dispose(); }
        }

        /// <summary>
        /// S0036 UI-9: a label that reads mnemonics turns a lone "&amp;" into an underline - English
        /// "Language &amp; region" read "Language region". Every label in every language either shows
        /// its text as written or has no lone ampersand in it.
        /// </summary>
        private static void MnemonicWalk(string language, System.Windows.Forms.Control control, List<string> problems)
        {
            if (control is System.Windows.Forms.Label label && label.UseMnemonic
                && label.Text.Replace("&&", "").IndexOf('&') >= 0)
                problems.Add($"[{language}] a label eats its ampersand: \"{Trim(label.Text)}\"");
            foreach (System.Windows.Forms.Control child in control.Controls) MnemonicWalk(language, child, problems);
        }

        private static void Walk(string language, object control, HashSet<object> skip, List<string> problems)
        {
            if (skip.Contains(control)) return;
            Type ct = control.GetType();
            string? text = ct.GetProperty("Text")?.GetValue(control) as string;
            if (!string.IsNullOrEmpty(text) && Cyrillic.IsMatch(StripAllowed(text!)))
                problems.Add($"[{language}] static {ct.Name}: \"{Trim(text!)}\"");

            if (ct.GetProperty("Controls")?.GetValue(control) is IEnumerable children)
                foreach (object child in children) Walk(language, child, skip, problems);
        }

        // ЙЦУКЕН is the Cyrillic layout name and legitimately appears verbatim in any language.
        private static string StripAllowed(string s) => s.Replace("ЙЦУКЕН", "");

        private static string Trim(string s) => s.Length <= 60 ? s : s.Substring(0, 57) + "...";
    }
}
