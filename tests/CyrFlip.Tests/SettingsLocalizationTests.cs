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
    public class SettingsLocalizationTests
    {
        private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]");

        /// <summary>Languages whose own script is Cyrillic, so surviving Cyrillic proves nothing.</summary>
        private static readonly HashSet<string> CyrillicUi = new HashSet<string> { "Русский", "Українська" };

        private static readonly string[] DynamicStrings =
        {
            "По умолчанию", "Удалить", "Добавить раскладку", "Введите язык или название раскладки для фильтра",
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
            "Введите не менее 3 символов:", "Текст", "Дата", "Источник", "Закрыть", "Вернуть в буфер",
            "Введите минимум 3 символа для поиска по части текста.", "Совпадений не найдено.", "Найдено: {0}",
            "Ничего не выделено. Я переворачиваю текст, а не воздух — сначала выделите что-нибудь.",
            "Не удалось прочитать или заменить выделение. У буфера обмена были другие планы.",
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
            var t = new Thread(() =>
            {
                try { foreach (string language in Localization.Names) Run(language, problems); }
                catch (Exception ex) { problems.Add("EXCEPTION: " + ex); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            Assert.True(problems.Count == 0, "Untranslated user-facing strings:\n" + string.Join("\n", problems));
        }

        private static void Run(string language, List<string> problems)
        {
            bool cyrillicScript = CyrillicUi.Contains(language);
            Type type = typeof(AppConfig).Assembly.GetType("CyrFlip.SettingsForm", true)!;
            var config = new AppConfig { UiLanguage = language };
            Action<bool> b = _ => { };
            Action noop = () => { };
            Action<int> i = _ => { };
            Action<string> s = _ => { };
            // A store over a per-run temp folder: never the developer's real scenario data, and the
            // folder is only created if something writes (nothing does here).
            var launcherStore = new LauncherScenarioStore(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N")));
            object form = Activator.CreateInstance(type, new object[]
            {
                config, b, b, b, b, b, b, b, b, b, i, s, noop, noop, noop, noop, noop, b, b, b, b, b, b,
                launcherStore, b,
            })!;
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
                    foreach (string field in new[] { "_layoutRows", "_languageRows", "_uiLanguage" })
                        if (type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(form) is object c)
                            skip.Add(c);
                    Walk(language, form, skip, problems);
                }
            }
            finally { (form as IDisposable)?.Dispose(); }
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
