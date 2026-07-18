using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Persistent settings surface; closing it only hides it so CyrFlip remains tray-first.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly Action<bool> _setAutostart, _setCursor, _setCaret, _setDot, _setLanguage, _setCaps, _setHistory, _setPause, _setHistoryStartup;
        private readonly Action<int> _setOpacity;
        private readonly Action<string> _setUiLanguage;
        private readonly Action _setFlipHotkey, _setCaseHotkey, _setHistoryHotkey, _clearHistory, _diagnoseCaret;
        private readonly CheckBox _cursor = Check("Показывать раскладку на текстовом курсоре мыши");
        private readonly CheckBox _autostart = Check("Запускать CyrFlip вместе с Windows");
        private readonly ComboBox _uiLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly CheckBox _caret = Check("Показывать метку раскладки рядом с кареткой");
        private readonly CheckBox _dot = Check("Компактная точка вместо букв EN/RU/UK");
        private readonly CheckBox _language = Check("Менять раскладку после переворота текста");
        private readonly CheckBox _caps = Check("Синхронизировать CapsLock после исправления регистра");
        private readonly CheckBox _history = Check("Включить историю буфера");
        private readonly CheckBox _pause = Check("Приостановить захват истории");
        private readonly CheckBox _historyStartup = Check("Показывать окно менеджера буфера при запуске");
        private readonly TrackBar _opacity = new TrackBar { Minimum = 30, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 245 };
        private readonly Label _opacityValue = new Label { AutoSize = true };
        private readonly Label _flipHotkeyValue = new Label { AutoSize = true };
        private readonly Label _caseHotkeyValue = new Label { AutoSize = true };
        private readonly Label _historyHotkeyValue = new Label { AutoSize = true };
        private bool _loading;
        private readonly Dictionary<Control, string> _russianTexts = new Dictionary<Control, string>();

        public SettingsForm(AppConfig config,
            Action<bool> setAutostart, Action<bool> setCursor, Action<bool> setCaret, Action<bool> setDot, Action<bool> setLanguage, Action<bool> setCaps,
            Action<bool> setHistory, Action<bool> setPause, Action<bool> setHistoryStartup, Action<int> setOpacity, Action<string> setUiLanguage,
            Action setFlipHotkey, Action setCaseHotkey, Action setHistoryHotkey, Action clearHistory, Action diagnoseCaret)
        {
            _config = config;
            _setAutostart = setAutostart; _setCursor = setCursor; _setCaret = setCaret; _setDot = setDot; _setLanguage = setLanguage; _setCaps = setCaps;
            _setHistory = setHistory; _setPause = setPause; _setHistoryStartup = setHistoryStartup; _setOpacity = setOpacity;
            _setUiLanguage = setUiLanguage;
            _setFlipHotkey = setFlipHotkey; _setCaseHotkey = setCaseHotkey; _setHistoryHotkey = setHistoryHotkey; _clearHistory = clearHistory; _diagnoseCaret = diagnoseCaret;

            Text = "Настройки CyrFlip"; StartPosition = FormStartPosition.CenterScreen; Size = new Size(1060, 680);
            MinimumSize = new Size(820, 540); ShowInTaskbar = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormClosing += (_, e) => { e.Cancel = true; Hide(); };

            var tabs = new TabControl { Dock = DockStyle.Fill, ItemSize = new Size(120, 30), SizeMode = TabSizeMode.Normal };
            var tabIcons = CreateTabIcons();
            tabs.ImageList = tabIcons;
            _uiLanguage.Items.AddRange(new object[] { "Русский", "English", "Українська" });
            tabs.TabPages.Add(WithIcon(Page("Общие", "Основные параметры приложения. Все изменения применяются сразу.",
                Setting(_autostart, "Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."),
                Setting(LanguageRow(), "Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее.")), 0));
            tabs.TabPages.Add(WithIcon(Page("Индикаторы", "Параметры, которые помогают увидеть активную раскладку до ввода текста.",
                Setting(_cursor, "Заменяет стандартный текстовый курсор I-beam на курсор с маленькой меткой EN, RU или UK. Обычная стрелка мыши не меняется."),
                Setting(_caret, "Рисует небольшую метку рядом с мигающей кареткой в поле ввода. Работает в приложениях, которые передают Windows положение каретки."),
                Setting(_dot, "Вместо букв EN/RU/UK рядом с кареткой показывает компактную цветную точку — удобно, если буквы отвлекают."),
                Setting(_language, "После переворота текста переключает раскладку активного окна, чтобы можно было сразу продолжить печатать на правильном языке."),
                Setting(_caps, "После исправления регистра меняет физическое состояние CapsLock, чтобы следующие нажатия соответствовали исправленному тексту.")), 1));
            tabs.TabPages.Add(WithIcon(Page("Горячие клавиши", "Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows.",
                HotkeyRow("Переворот EN ⇄ RU", _flipHotkeyValue, _setFlipHotkey, "Копирует выделение, переводит раскладку QWERTY ↔ ЙЦУКЕН и вставляет исправленный текст обратно."),
                HotkeyRow("Исправить CapsLock", _caseHotkeyValue, _setCaseHotkey, "Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock."),
                HotkeyRow("Менеджер буфера", _historyHotkeyValue, _setHistoryHotkey, "Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию.")), 2));
            tabs.TabPages.Add(WithIcon(ClipboardPage(), 3));
            tabs.TabPages.Add(WithIcon(AboutPage(), 5));
            Controls.Add(tabs);

            _cursor.CheckedChanged += (_, _) => Changed(_setCursor, _cursor.Checked);
            _autostart.CheckedChanged += (_, _) => Changed(_setAutostart, _autostart.Checked);
            _caret.CheckedChanged += (_, _) => Changed(_setCaret, _caret.Checked);
            _dot.CheckedChanged += (_, _) => Changed(_setDot, _dot.Checked);
            _language.CheckedChanged += (_, _) => Changed(_setLanguage, _language.Checked);
            _caps.CheckedChanged += (_, _) => Changed(_setCaps, _caps.Checked);
            _history.CheckedChanged += (_, _) => Changed(_setHistory, _history.Checked);
            _pause.CheckedChanged += (_, _) => Changed(_setPause, _pause.Checked);
            _historyStartup.CheckedChanged += (_, _) => Changed(_setHistoryStartup, _historyStartup.Checked);
            _uiLanguage.SelectedIndexChanged += (_, _) => { if (!_loading && _uiLanguage.SelectedItem is string language) { _setUiLanguage(language); ApplyLanguage(); } };
            _opacity.ValueChanged += (_, _) => { if (!_loading) { _opacityValue.Text = _opacity.Value + "%"; _setOpacity(_opacity.Value); } };
            Reload();
            RememberRussianTexts(this);
            ApplyLanguage();
        }

        public void Reload()
        {
            _loading = true;
            _autostart.Checked = Autostart.IsEnabled;
            _uiLanguage.SelectedItem = _config.UiLanguage;
            if (_uiLanguage.SelectedIndex < 0) _uiLanguage.SelectedIndex = 0;
            _cursor.Checked = _config.EnableCursorChange; _caret.Checked = _config.EnableCaretOverlay; _dot.Checked = _config.CaretDotMode;
            _language.Checked = _config.EnableLanguageSwitch; _caps.Checked = _config.FlipCapsLockAfter;
            _history.Checked = _config.EnableClipboardHistory; _pause.Checked = _config.PauseClipboardHistory;
            _historyStartup.Checked = _config.ShowClipboardHistoryOnStartup;
            _opacity.Value = Math.Max(_opacity.Minimum, Math.Min(_opacity.Maximum, _config.ClipboardHistoryOpacity));
            _opacityValue.Text = _opacity.Value + "%";
            _flipHotkeyValue.Text = _config.Hotkey; _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
            _pause.Enabled = _history.Checked; _historyStartup.Enabled = _history.Checked; _opacity.Enabled = _history.Checked;
            _loading = false;
            ApplyLanguage();
        }

        private void RememberRussianTexts(Control root)
        {
            // ComboBox.Text is its selected value, not a UI caption. Translating it would reset
            // the language selector back to the first Russian value on every UI refresh.
            if (!(root is ComboBox) && !_russianTexts.ContainsKey(root) && !string.IsNullOrEmpty(root.Text))
                _russianTexts.Add(root, root.Text);
            foreach (Control child in root.Controls) RememberRussianTexts(child);
        }

        private void ApplyLanguage()
        {
            foreach (KeyValuePair<Control, string> pair in _russianTexts)
                pair.Key.Text = Translate(pair.Value);
            _flipHotkeyValue.Text = _config.Hotkey; _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
        }

        private string Translate(string ru)
        {
            if (_config.UiLanguage == "Русский") return ru;
            bool uk = _config.UiLanguage == "Українська";
            var en = new Dictionary<string, string>
            {
                ["Настройки CyrFlip"] = "CyrFlip Settings", ["Общие"] = "General", ["Индикаторы"] = "Indicators", ["Горячие клавиши"] = "Hotkeys", ["Буфер обмена"] = "Clipboard", ["О программе и дополнительно"] = "About & Advanced",
                ["Запускать CyrFlip вместе с Windows"] = "Start CyrFlip with Windows", ["Язык интерфейса:"] = "Interface language:", ["Показывать раскладку на текстовом курсоре мыши"] = "Show layout on the text mouse cursor", ["Показывать метку раскладки рядом с кареткой"] = "Show layout label near the caret", ["Компактная точка вместо букв EN/RU/UK"] = "Compact dot instead of EN/RU/UK", ["Менять раскладку после переворота текста"] = "Change layout after flipping text", ["Синхронизировать CapsLock после исправления регистра"] = "Synchronize CapsLock after case correction",
                ["Включить историю буфера"] = "Enable clipboard history", ["Приостановить захват истории"] = "Pause history capture", ["Показывать окно менеджера буфера при запуске"] = "Show clipboard manager on startup", ["Прозрачность окна истории:"] = "History window transparency:", ["Очистить всю историю"] = "Clear all history", ["Диагностика положения каретки..."] = "Diagnose caret position...", ["Изменить..."] = "Change...",
                ["Разработчик: SerZhyAle"] = "Developer: SerZhyAle", ["Сайт программы: serzhyale.github.io/CyrFlip"] = "App website: serzhyale.github.io/CyrFlip", ["Сайт разработчика: sza.od.ua"] = "Developer website: sza.od.ua"
            };
            var ua = new Dictionary<string, string>
            {
                ["Настройки CyrFlip"] = "Налаштування CyrFlip", ["Общие"] = "Загальні", ["Индикаторы"] = "Індикатори", ["Горячие клавиши"] = "Гарячі клавіші", ["Буфер обмена"] = "Буфер обміну", ["О программе и дополнительно"] = "Про програму й додатково",
                ["Запускать CyrFlip вместе с Windows"] = "Запускати CyrFlip разом із Windows", ["Язык интерфейса:"] = "Мова інтерфейсу:", ["Показывать раскладку на текстовом курсоре мыши"] = "Показувати розкладку на текстовому курсорі миші", ["Показывать метку раскладки рядом с кареткой"] = "Показувати мітку розкладки біля каретки", ["Компактная точка вместо букв EN/RU/UK"] = "Компактна крапка замість EN/RU/UK", ["Менять раскладку после переворота текста"] = "Змінювати розкладку після перевороту тексту", ["Синхронизировать CapsLock после исправления регистра"] = "Синхронізувати CapsLock після виправлення регістру",
                ["Включить историю буфера"] = "Увімкнути історію буфера", ["Приостановить захват истории"] = "Призупинити захоплення історії", ["Показывать окно менеджера буфера при запуске"] = "Показувати менеджер буфера під час запуску", ["Прозрачность окна истории:"] = "Прозорість вікна історії:", ["Очистить всю историю"] = "Очистити всю історію", ["Диагностика положения каретки..."] = "Діагностика положення каретки...", ["Изменить..."] = "Змінити...",
                ["Разработчик: SerZhyAle"] = "Розробник: SerZhyAle", ["Сайт программы: serzhyale.github.io/CyrFlip"] = "Сайт програми: serzhyale.github.io/CyrFlip", ["Сайт разработчика: sza.od.ua"] = "Сайт розробника: sza.od.ua"
            };
            AddDetailedTranslations(en, ua);
            Dictionary<string, string> map = uk ? ua : en;
            return map.TryGetValue(ru, out string? translated) ? translated : ru;
        }

        private static void AddDetailedTranslations(Dictionary<string, string> en, Dictionary<string, string> ua)
        {
            en["Основные параметры приложения. Все изменения применяются сразу."] = "Core app settings. Changes take effect immediately.";
            ua["Основные параметры приложения. Все изменения применяются сразу."] = "Основні параметри застосунку. Зміни застосовуються одразу.";
            en["Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."] = "Adds CyrFlip to startup for the current Windows user only. It starts in the background and appears in the notification area at the next sign-in.";
            ua["Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."] = "Додає CyrFlip до автозавантаження лише поточного користувача Windows. Під час наступного входу застосунок запуститься у фоні та з'явиться в треї.";
            en["Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее."] = "Choose the CyrFlip interface language. The choice is saved and also applies to the tray menu.";
            ua["Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее."] = "Оберіть мову інтерфейсу CyrFlip. Вибір зберігається та застосовується також до меню в треї.";
            en["Параметры, которые помогают увидеть активную раскладку до ввода текста."] = "Options that help you see the active layout before typing.";
            ua["Параметры, которые помогают увидеть активную раскладку до ввода текста."] = "Параметри, що допомагають побачити активну розкладку до введення тексту.";
            en["Заменяет стандартный текстовый курсор I-beam на курсор с маленькой меткой EN, RU или UK. Обычная стрелка мыши не меняется."] = "Replaces the standard I-beam with a small EN, RU or UK marker. The normal mouse arrow is unchanged.";
            ua["Заменяет стандартный текстовый курсор I-beam на курсор с маленькой меткой EN, RU или UK. Обычная стрелка мыши не меняется."] = "Замінює стандартний I-beam маленькою міткою EN, RU або UK. Звичайна стрілка миші не змінюється.";
            en["Рисует небольшую метку рядом с мигающей кареткой в поле ввода. Работает в приложениях, которые передают Windows положение каретки."] = "Draws a small marker beside the blinking caret. It works in apps that expose the caret position to Windows.";
            ua["Рисует небольшую метку рядом с мигающей кареткой в поле ввода. Работает в приложениях, которые передают Windows положение каретки."] = "Малює невелику мітку біля миготливої каретки. Працює в застосунках, що передають Windows положення каретки.";
            en["Вместо букв EN/RU/UK рядом с кареткой показывает компактную цветную точку — удобно, если буквы отвлекают."] = "Shows a compact colour dot beside the caret instead of EN/RU/UK letters.";
            ua["Вместо букв EN/RU/UK рядом с кареткой показывает компактную цветную точку — удобно, если буквы отвлекают."] = "Показує компактну кольорову крапку біля каретки замість літер EN/RU/UK.";
            en["После переворота текста переключает раскладку активного окна, чтобы можно было сразу продолжить печатать на правильном языке."] = "Switches the active window's layout after a flip, so you can continue typing in the correct language.";
            ua["После переворота текста переключает раскладку активного окна, чтобы можно было сразу продолжить печатать на правильном языке."] = "Перемикає розкладку активного вікна після перевороту, щоб можна було одразу продовжити друкувати правильною мовою.";
            en["После исправления регистра меняет физическое состояние CapsLock, чтобы следующие нажатия соответствовали исправленному тексту."] = "Changes the physical CapsLock state after case correction, so subsequent keystrokes match the corrected text.";
            ua["После исправления регистра меняет физическое состояние CapsLock, чтобы следующие нажатия соответствовали исправленному тексту."] = "Змінює фізичний стан CapsLock після виправлення регістру, щоб наступні натискання відповідали виправленому тексту.";
            en["Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows."] = "These shortcuts work globally while CyrFlip runs in your Windows session.";
            ua["Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows."] = "Ці комбінації працюють глобально, поки CyrFlip запущений у вашому сеансі Windows.";
            en["Копирует выделение, переводит раскладку QWERTY ↔ ЙЦУКЕН и вставляет исправленный текст обратно."] = "Copies the selection, converts QWERTY ↔ ЙЦУКЕН, and pastes the corrected text back.";
            ua["Копирует выделение, переводит раскладку QWERTY ↔ ЙЦУКЕН и вставляет исправленный текст обратно."] = "Копіює виділення, перетворює QWERTY ↔ ЙЦУКЕН і вставляє виправлений текст назад.";
            en["Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock."] = "Swaps upper and lower case in the selection; useful after accidentally enabling CapsLock.";
            ua["Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock."] = "Змінює верхній і нижній регістр виділеного тексту; зручно після випадково ввімкненого CapsLock.";
            en["Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию."] = "Shows or hides the text-history window. Two CyrFlip actions cannot share one shortcut.";
            ua["Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию."] = "Показує або ховає вікно текстової історії. Двом діям CyrFlip не можна призначити одну комбінацію.";
            en["История хранится только локально и шифруется Windows DPAPI для вашей учётной записи."] = "History is stored locally only and encrypted with Windows DPAPI for your account.";
            ua["История хранится только локально и шифруется Windows DPAPI для вашей учётной записи."] = "Історія зберігається лише локально та шифрується Windows DPAPI для вашого облікового запису.";
            en["CyrFlip сохраняет только Unicode-текст, скопированный после включения функции. Изображения, файлы и другие форматы не записываются."] = "CyrFlip saves only Unicode text copied after this feature is enabled. Images, files and other formats are not stored.";
            ua["CyrFlip сохраняет только Unicode-текст, скопированный после включения функции. Изображения, файлы и другие форматы не записываются."] = "CyrFlip зберігає лише Unicode-текст, скопійований після ввімкнення функції. Зображення, файли та інші формати не записуються.";
            en["Временно прекращает захват новых копирований, не удаляя уже сохранённую историю. Полезно при работе с паролями и личными данными."] = "Temporarily stops capturing new copies without deleting saved history. Useful while handling passwords or private data.";
            ua["Временно прекращает захват новых копирований, не удаляя уже сохранённую историю. Полезно при работе с паролями и личными данными."] = "Тимчасово зупиняє захоплення нових копій, не видаляючи збережену історію. Корисно під час роботи з паролями чи приватними даними.";
            en["Позволяет вести историю в фоне, но не показывать её окно после каждого запуска CyrFlip."] = "Keeps capturing history in the background without showing its window after every CyrFlip launch.";
            ua["Позволяет вести историю в фоне, но не показывать её окно после каждого запуска CyrFlip."] = "Дозволяє вести історію у фоні, але не показувати її вікно після кожного запуску CyrFlip.";
            en["Задаёт прозрачность плавающего окна истории от 30% до 100%. Значение применяется сразу."] = "Sets the floating history window opacity from 30% to 100%. Applied immediately.";
            ua["Задаёт прозрачность плавающего окна истории от 30% до 100%. Значение применяется сразу."] = "Задає прозорість плаваючого вікна історії від 30% до 100%. Застосовується одразу.";
            en["Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить."] = "Deletes every entry from memory and the encrypted local file. This cannot be undone.";
            ua["Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить."] = "Видаляє всі записи з пам'яті та зашифрованого локального файлу. Цю дію не можна скасувати.";
            en["Редкие действия и техническая диагностика."] = "Occasional actions and technical diagnostics.";
            ua["Редкие действия и техническая диагностика."] = "Рідкісні дії та технічна діагностика.";
            en["Создаёт локальный отчёт о том, как Windows и UI Automation видят каретку. Нужен только если метка не появляется или рисуется не там в конкретной программе."] = "Creates a local report of how Windows and UI Automation see the caret. Use it only when the marker is missing or misplaced in a particular app.";
            ua["Создаёт локальный отчёт о том, как Windows и UI Automation видят каретку. Нужен только если метка не появляется или рисуется не там в конкретной программе."] = "Створює локальний звіт про те, як Windows та UI Automation бачать каретку. Потрібен лише якщо мітка не з'являється або малюється не там у певній програмі.";
        }

        private TabPage ClipboardPage()
        {
            var page = Page("Буфер обмена", "История хранится только локально и шифруется Windows DPAPI для вашей учётной записи.",
                Setting(_history, "CyrFlip сохраняет только Unicode-текст, скопированный после включения функции. Изображения, файлы и другие форматы не записываются."),
                Setting(_pause, "Временно прекращает захват новых копирований, не удаляя уже сохранённую историю. Полезно при работе с паролями и личными данными."),
                Setting(_historyStartup, "Позволяет вести историю в фоне, но не показывать её окно после каждого запуска CyrFlip."));
            var panel = ContentPanel(page);
            var opacityLine = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 7, 0, 0) };
            opacityLine.Controls.Add(new Label { Text = "Прозрачность окна истории:", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
            opacityLine.Controls.Add(_opacity); opacityLine.Controls.Add(_opacityValue);
            panel.Controls.Add(Setting(opacityLine, "Задаёт прозрачность плавающего окна истории от 30% до 100%. Значение применяется сразу."));
            panel.Controls.Add(Setting(Button("Очистить всю историю", () => { if (MessageBox.Show(Translate("Удалить всю сохранённую историю буфера?"), "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) _clearHistory(); }), "Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить."));
            return page;
        }

        private static TabPage Page(string title, string description, params Control[] controls)
        {
            var page = new TabPage(title);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = SystemColors.Window };
            panel.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(930, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(2, 0, 2, 10) });
            foreach (Control control in controls) panel.Controls.Add(control);
            page.Controls.Add(panel); return page;
        }
        private static FlowLayoutPanel ContentPanel(TabPage page) => (FlowLayoutPanel)page.Controls[0];
        private static TabPage WithIcon(TabPage page, int imageIndex) { page.ImageIndex = imageIndex; return page; }
        private static ImageList CreateTabIcons()
        {
            var images = new ImageList { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit };
            images.Images.Add("general", TabIcon(0)); images.Images.Add("indicators", TabIcon(1)); images.Images.Add("hotkeys", TabIcon(2));
            images.Images.Add("clipboard", TabIcon(3)); images.Images.Add("advanced", TabIcon(4)); images.Images.Add("about", TabIcon(5));
            return images;
        }
        private static Bitmap TabIcon(int kind)
        {
            var image = new Bitmap(18, 18); using var g = Graphics.FromImage(image); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(45, 105, 175), 1.8f); using var brush = new SolidBrush(Color.FromArgb(45, 105, 175));
            switch (kind)
            {
                case 0: g.DrawEllipse(pen, 3, 3, 12, 12); g.FillEllipse(brush, 7, 7, 4, 4); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; float x = 9 + (float)Math.Cos(a) * 7; float y = 9 + (float)Math.Sin(a) * 7; g.DrawLine(pen, 9 + (float)Math.Cos(a) * 5, 9 + (float)Math.Sin(a) * 5, x, y); } break;
                case 1: g.DrawEllipse(pen, 2, 5, 14, 8); g.FillEllipse(brush, 7, 7, 4, 4); break;
                case 2: g.DrawRectangle(pen, 2, 4, 14, 10); for (int x = 4; x <= 12; x += 4) for (int y = 6; y <= 10; y += 4) g.FillRectangle(brush, x, y, 2, 2); break;
                case 3: g.DrawRectangle(pen, 4, 3, 10, 13); g.DrawLine(pen, 6, 6, 12, 6); g.DrawLine(pen, 6, 9, 12, 9); g.DrawLine(pen, 6, 12, 10, 12); break;
                case 4: g.DrawLine(pen, 4, 14, 13, 5); g.DrawEllipse(pen, 10, 2, 5, 5); break;
                default: g.DrawEllipse(pen, 2, 2, 14, 14); using (var f = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Bold)) g.DrawString("i", f, brush, 7, 2); break;
            }
            return image;
        }
        private Control LanguageRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Язык интерфейса:", AutoSize = true, Width = 155, Padding = new Padding(0, 7, 0, 0) });
            row.Controls.Add(_uiLanguage);
            return row;
        }
        private TabPage AboutPage()
        {
            var page = Page("О программе и дополнительно", "CyrFlip — лёгкая утилита для Windows: показывает EN/RU/UK у текстового курсора и каретки, исправляет текст, набранный в неверной раскладке, и хранит необязательную локальную историю буфера.\n\nПриложение не передаёт историю буфера в сеть. Если история включена, записи защищены Windows DPAPI и читаются только той же учётной записью Windows.");
            var panel = ContentPanel(page);
            panel.Controls.Add(new Label { Text = "Разработчик: SerZhyAle", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) });
            panel.Controls.Add(Link("Сайт программы: serzhyale.github.io/CyrFlip", "https://serzhyale.github.io/CyrFlip/"));
            panel.Controls.Add(Link("GitHub: github.com/SerZhyAle/CyrFlip", "https://github.com/SerZhyAle/CyrFlip"));
            panel.Controls.Add(Link("Сайт разработчика: sza.od.ua", "https://sza.od.ua/"));
            panel.Controls.Add(new Label { Text = "CyrFlip работает локально, без телеметрии и сетевой синхронизации истории буфера.", AutoSize = true, MaximumSize = new Size(690, 0), Padding = new Padding(0, 14, 0, 0), ForeColor = SystemColors.GrayText });
            panel.Controls.Add(Setting(Button("Диагностика положения каретки...", _diagnoseCaret), "Создаёт локальный отчёт о том, как Windows и UI Automation видят каретку. Нужен только если метка не появляется или рисуется не там в конкретной программе."));
            return page;
        }
        private static LinkLabel Link(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            return link;
        }
        private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
        private static Button Button(string text, Action action) { var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 5, 3, 5) }; button.Click += (_, _) => action(); return button; }
        private static Control Setting(Control control, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            block.Controls.Add(control);
            block.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }
        private static Control HotkeyRow(string label, Label value, Action change, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            row.Controls.Add(new Label { Text = label + ":", AutoSize = true, Width = 150, Padding = new Padding(0, 7, 0, 0) });
            value.Padding = new Padding(0, 7, 5, 0); row.Controls.Add(value); row.Controls.Add(Button("Изменить...", change));
            block.Controls.Add(row);
            block.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }
        private void Changed(Action<bool> action, bool value) { if (_loading) return; action(value); Reload(); }
    }
}
