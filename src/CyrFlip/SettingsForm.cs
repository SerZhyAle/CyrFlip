using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Persistent settings surface; closing it only hides it so CyrFlip remains tray-first.</summary>
    internal sealed class SettingsForm : ThemedForm
    {
        private readonly AppConfig _config;
        private readonly Action<bool> _setAutostart, _setCursor, _setCaret, _setDot, _setLanguage, _setCaps, _setHistory, _setPause, _setHistoryStartup;
        private readonly Action<int> _setOpacity;
        private readonly Action<string> _setUiLanguage;
        private readonly Action _setCaseHotkey, _setHistoryHotkey, _openHistorySearch, _clearHistory, _diagnoseCaret;
        private readonly Func<int>? _getHistoryCount, _getQuickNotesCount;
        private readonly Action<bool> _setHotkeysEnabled, _setCaseEnabled, _setHistoryEnabled, _setDeferRdp;
        private readonly Action<bool> _setKeepAwake, _setKeepScreen;
        // Disabled while a bundle is being packed or a compose window is open, so a second click
        // cannot start a second archive on top of the first.
        private Button? _sendLogs;
        private readonly CheckBox _enableHotkeys = Check("Слушать глобальные горячие клавиши");
        private readonly CheckBox _caseEnabled = Check("Исправить CapsLock");
        private readonly CheckBox _historyEnabled = Check("Менеджер буфера");
        private readonly CheckBox _deferRdp = Check("Уступать хоткеи удалённому рабочему столу (mstsc/msrdc)");
        private readonly CheckBox _convertSymbols = Check("Конвертировать знаки препинания вместе с текстом");
        // ---- Text context menu (CyrFlip's own menu over the selection) ----
        private readonly CheckBox _contextMenuEnabled = Check("Своё контекстное меню над выделенным текстом");
        private readonly ComboBox _contextMenuChord = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly CheckBox _cursor = Check("Показывать раскладку на текстовом курсоре мыши");
        private readonly CheckBox _autostart = Check("Запускать CyrFlip вместе с Windows");
        private readonly CheckBox _startMenuShortcut = Check("Ярлык CyrFlip в меню «Пуск»");
        private readonly CheckBox _desktopShortcut = Check("Ярлык CyrFlip на рабочем столе");
        private readonly CheckBox _keepAwake = Check("Не давать компьютеру засыпать");
        private readonly CheckBox _keepScreen = Check("Не блокировать экран (как при видео)");
        private readonly ComboBox _uiLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        // The theme (ticket S0020): three radio buttons rather than a drop-down - three exclusive
        // choices, all visible at once. One container, so Windows keeps exactly one of them checked.
        private readonly RadioButton _themeSystem = Radio("Как в Windows");
        private readonly RadioButton _themeLight = Radio("Светлая");
        private readonly RadioButton _themeDark = Radio("Тёмная");
        private readonly CheckBox _caret = Check("Показывать метку раскладки рядом с кареткой");
        private readonly CheckBox _dot = Check("Компактная точка вместо букв раскладки");
        // The marker size (S0011 LI-3): three steps, see MarkerSize.Presets. The caption beside it is a
        // translated word, so it is rebuilt on a language change rather than remembered as Russian.
        private readonly TrackBar _markerSize = new TrackBar { Minimum = 0, Maximum = 2, TickFrequency = 1, SmallChange = 1, LargeChange = 1, Width = 150 };
        private readonly Label _markerSizeValue = new Label { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        private readonly CheckBox _language = Check("Менять раскладку после конвертации текста");
        private readonly CheckBox _caps = Check("Синхронизировать CapsLock после исправления регистра");
        private readonly CheckBox _history = Check("Включить историю буфера");
        private readonly CheckBox _pause = Check("Приостановить захват истории");
        private readonly CheckBox _historyStartup = Check("Показывать окно менеджера буфера при запуске");
        private readonly TrackBar _opacity = new TrackBar { Minimum = 30, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 245 };
        private readonly Label _opacityValue = new Label { AutoSize = true };
        private readonly Label _caseHotkeyValue = new Label { AutoSize = true };
        private readonly Label _historyHotkeyValue = new Label { AutoSize = true };
        /// <summary>The About tab's version line - see <see cref="VersionLine"/> for what it says.</summary>
        private readonly Label _version = new Label { AutoSize = true, Margin = new Padding(3, 14, 3, 0) };
        // The Windows-languages tab. Every list here is rebuilt from the registry on each Reload, so it
        // stays truthful even when the user edits layouts or hotkeys in the Windows dialog meanwhile.
        private readonly FlowLayoutPanel _layoutRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 2, 3, 2) };
        private readonly FlowLayoutPanel _languageRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly FlowLayoutPanel _conversionRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly ComboBox _toggleCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly Button _restoreLanguageHotkeys = new Button { Text = "Вернуть хоткеи как было", AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
        private readonly Button _restoreLayouts = new Button { Text = "Вернуть раскладки как было", AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
        private bool _languageHotkeyNoticeShown;
        private bool _layoutNoticeShown;
        private bool _loading;
        // ---- Scenario launcher tab ----
        private readonly LauncherScenarioStore _launcherStore;
        private readonly Action<bool> _setLauncherEnabled;
        private readonly CheckBox _launcherEnabled = Check("Включить быстрый запуск");
        private readonly TextBox _launcherSearch = new TextBox { Width = 240, Margin = new Padding(3, 3, 3, 3) };
        private readonly ListView _launcherList = new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            Width = 900, Height = 320, Margin = new Padding(3, 4, 3, 4), ShowItemToolTips = true,
        };
        private readonly Label _launcherLoadErrors = new Label { AutoSize = true, ForeColor = ThemePalette.Light.Warning, Margin = new Padding(3, 2, 3, 2), Visible = false };
        private readonly LauncherIconCache _launcherIcons = new LauncherIconCache();
        /// <summary>The <see cref="LauncherRowsStamp"/> the table was last built for; null = never built.</summary>
        private string? _launcherRowsStamp;
        private int _launcherIconsVersion = -1;
        private readonly ImageList _launcherImages = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        private readonly List<Bitmap> _launcherBitmaps = new List<Bitmap>();
        /// <summary>
        /// <see cref="SystemFonts.MessageBoxFont"/> builds a new <see cref="Font"/> on every read, and
        /// <see cref="ApplyScript"/> runs on every refresh - one font per process instead (ST-5).
        /// </summary>
        private static readonly Font MessageBoxFont = SystemFonts.MessageBoxFont;
        private readonly List<Button> _launcherButtons = new List<Button>();
        private Button? _launcherImportOcr;
        // ---- Translator tab (local Ollama) ----
        private readonly CheckBox _translateEnabled = Check("Включить перевод выделенного текста");
        private readonly CheckBox _translateAutoStart = Check("Запускать Ollama автоматически при переводе");
        private readonly CheckBox _translateCopy = Check("Класть перевод в буфер обмена");
        private readonly CheckBox _translatePaste = Check("Сразу вставлять перевод вместо выделения");
        private readonly CheckBox _translateShowSource = Check("Показывать исходный текст рядом с переводом");
        // Value-carrying controls are created with no Text on purpose: RememberRussianTexts only
        // registers non-empty captions, so a user-entered address is never treated as a translatable
        // string and reset on the next language change.
        private readonly TextBox _translateEndpoint = new TextBox { Width = 260, Margin = new Padding(3, 4, 3, 4) };
        private readonly ComboBox _translateModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 220, Margin = new Padding(3, 4, 3, 4) };
        // -1 is a real value here, not a guard: Ollama reads it as "keep the model loaded forever".
        private readonly NumericUpDown _translateKeepAlive = new NumericUpDown { Minimum = -1, Maximum = 120, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly NumericUpDown _translateTimeout = new NumericUpDown { Minimum = 5, Maximum = 600, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly NumericUpDown _translateWindowTimeout = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly Label _translateStatus = new Label { AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 6) };
        private readonly FlowLayoutPanel _translationRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly List<Button> _translateButtons = new List<Button>();
        private Button? _translateCancel;
        // A model pull is minutes of streaming with no timeout by design; this is how the window
        // closing (or the app exiting) stops it instead of leaving the tab wedged on "busy".
        private CancellationTokenSource? _translateWork;
        private bool _translateBusy;
        // ---- Quick notes tab ----
        private readonly Action _setQuickNotesHotkey, _openQuickNotes, _clearQuickNotes;
        private readonly Func<string, bool, string> _exportQuickNotes;
        // Export / import of the open exchange file (ticket S0023); null leaves the rows out.
        private readonly Action<IWin32Window, bool>? _exchange;
        private readonly CheckBox _quickNotesEnabled = Check("Включить быстрые заметки");
        private readonly CheckBox _quickNotesHotkeyEnabled = Check("Быстрые заметки");
        private readonly CheckBox _quickNotesWrap = Check("Переносить длинные строки в редакторе");
        private readonly CheckBox _quickNotesExportMeta = Check("Добавлять даты при экспорте");
        private readonly Label _quickNotesHotkeyValue = new Label { AutoSize = true };
        private readonly List<Control> _quickNotesControls = new List<Control>();
        // ---- The text context menu's keyboard chord (S0045 K2) ----
        private readonly CheckBox _textMenuHotkeyEnabled = Check("Открыть меню с клавиатуры");
        private readonly Label _textMenuHotkeyValue = new Label { AutoSize = true };
        // ---- Graphics (S0026) ----
        private readonly CheckBox _screenshotHotkeyEnabled = Check("Снимок области экрана");
        private readonly Label _screenshotHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _screenshotSave = Check("Также сохранять снимки в папку");
        private readonly TextBox _screenshotFolderBox = new TextBox { ReadOnly = true, Width = 520, Margin = new Padding(3, 6, 3, 3) };
        private readonly List<Control> _screenshotFolderControls = new List<Control>();
        private readonly Dictionary<Control, string> _russianTexts = new Dictionary<Control, string>();
        // Everything below is a GDI resource this window owns and therefore has to free: WinForms
        // disposes neither a font handed to a control, nor an ImageList merely assigned to one, nor the
        // Icon a form was given (only the small copy it derives). One shared bold font serves every
        // row and header, so a table rebuild - which happens on every settings change - allocates none.
        private Font? _ownFont;
        private Font? _boldFont;
        /// <summary>
        /// The one bold font every header and every table row shares, derived from the form's current
        /// font. Cleared whenever <see cref="ApplyScript"/> swaps the family, so the next use re-derives
        /// it in the script the language needs. Shared rather than per-row on purpose: the tables are
        /// rebuilt on every settings change, and a font handed to a control is never freed by WinForms.
        /// </summary>
        private Font BoldFont => _boldFont ?? (_boldFont = new Font(Font, FontStyle.Bold));
        private ImageList? _tabIcons;
        private Color _tabIconsInk = ThemePalette.Light.TextPrimary;
        private System.Drawing.Icon? _ownIcon;
        private TabControl? _tabs;
        private FlowLayoutPanel? _launcherPanel;
        private bool _layingOutLauncher;
        // KLID → installed layout name, so the conversion table doesn't re-read the registry per row.
        private readonly Dictionary<string, string> _layoutNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised after the profile list changes, so the global hook can replace its bindings.</summary>
        public event EventHandler? ConversionProfilesChanged;

        /// <summary>Raised after the launcher scenarios change, so the tray submenu / Jump List / hook refresh.</summary>
        public event EventHandler? LauncherScenariosChanged;

        /// <summary>
        /// Raised after anything on the translator tab changes - the switch, a setting or a row. One
        /// event for the whole tab rather than a constructor callback per value: the context saves the
        /// config, rebinds the chords and refreshes the tray entry in one place.
        /// </summary>
        public event EventHandler? TranslationChanged;

        /// <summary>
        /// Raised after the text context menu's switch or chord changes, so the context can save and
        /// install (or drop) the mouse hook. Named TextMenuChanged, not ContextMenuChanged: the latter
        /// is an inherited <see cref="Control"/> event, and hiding it is a build error here (CS0108).
        /// </summary>
        public event EventHandler? TextMenuChanged;

        /// <summary>
        /// Raised after anything on the quick-notes tab changes. One event for the whole tab, like
        /// the translator's: the context saves the config, rebinds the chord, shows or hides the
        /// tray entry and - on the very first enable - says where the notes are kept.
        /// </summary>
        public event EventHandler? QuickNotesChanged;
        /// <summary>Anything on the Graphics tab changed (S0026): the context saves and rebinds.</summary>
        public event EventHandler? GraphicsChanged;
        /// <summary>"Изменить..." beside the capture chord.</summary>
        public event EventHandler? ScreenshotHotkeyChangeRequested;
        /// <summary>"Изменить..." beside the text context menu's keyboard chord (S0045 K2).</summary>
        public event EventHandler? TextMenuHotkeyChangeRequested;
        /// <summary>"Сделать снимок области" - the same action as the tray item.</summary>
        public event EventHandler? ScreenshotRequested;

        /// <summary>
        /// Raised after the marker size changes (the value is already in the config), so the context can
        /// save it and resize the caret overlay and the I-beam.
        /// </summary>
        public event EventHandler? MarkerSizeChanged;

        /// <param name="exchange">Export (false) / import (true) of the open exchange file (ticket S0023); null leaves the rows out.</param>
        public SettingsForm(AppConfig config,
            Action<bool> setAutostart, Action<bool> setCursor, Action<bool> setCaret, Action<bool> setDot, Action<bool> setLanguage, Action<bool> setCaps,
            Action<bool> setHistory, Action<bool> setPause, Action<bool> setHistoryStartup, Action<int> setOpacity, Action<string> setUiLanguage,
            Action setCaseHotkey, Action setHistoryHotkey, Action openHistorySearch, Action clearHistory, Action diagnoseCaret,
            Action<bool> setHotkeysEnabled, Action<bool> setCaseEnabled, Action<bool> setHistoryEnabled, Action<bool> setDeferRdp,
            Action<bool> setKeepAwake, Action<bool> setKeepScreen,
            LauncherScenarioStore launcherStore, Action<bool> setLauncherEnabled,
            Action setQuickNotesHotkey, Action openQuickNotes, Action clearQuickNotes,
            Func<string, bool, string> exportQuickNotes, Action<IWin32Window, bool>? exchange,
            Func<int>? getHistoryCount = null, Func<int>? getQuickNotesCount = null)
        {
            _config = config;
            _launcherStore = launcherStore;
            _setLauncherEnabled = setLauncherEnabled;
            _setQuickNotesHotkey = setQuickNotesHotkey; _openQuickNotes = openQuickNotes;
            _clearQuickNotes = clearQuickNotes; _exportQuickNotes = exportQuickNotes;
            _exchange = exchange;
            _getHistoryCount = getHistoryCount;
            _getQuickNotesCount = getQuickNotesCount;
            _setAutostart = setAutostart; _setCursor = setCursor; _setCaret = setCaret; _setDot = setDot; _setLanguage = setLanguage; _setCaps = setCaps;
            _setHistory = setHistory; _setPause = setPause; _setHistoryStartup = setHistoryStartup; _setOpacity = setOpacity;
            _setUiLanguage = setUiLanguage;
            _setCaseHotkey = setCaseHotkey; _setHistoryHotkey = setHistoryHotkey; _openHistorySearch = openHistorySearch; _clearHistory = clearHistory; _diagnoseCaret = diagnoseCaret;
            _setHotkeysEnabled = setHotkeysEnabled; _setCaseEnabled = setCaseEnabled; _setHistoryEnabled = setHistoryEnabled; _setDeferRdp = setDeferRdp;
            _setKeepAwake = setKeepAwake; _setKeepScreen = setKeepScreen;

            Text = "Настройки CyrFlip"; StartPosition = FormStartPosition.CenterScreen; Size = DefaultWindowSize();
            MinimumSize = new Size(900, 620); ShowInTaskbar = true;
            try { Icon = _ownIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormClosing += (_, e) => { if (CloseToHide.Intercept(e)) Hide(); };

            // Vertical tab strip on the left: the captions are long in most of the 13 languages and a
            // horizontal strip either clipped them or wrapped into a second row that moved on click.
            // Left alignment draws the caption rotated unless we own-draw it - hence OwnerDrawFixed.
            var tabs = new ThemeTabControl
            {
                Dock = DockStyle.Fill, Alignment = TabAlignment.Left, Multiline = true,
                SizeMode = TabSizeMode.Fixed, DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(34, 200),
            };
            _tabs = tabs;
            _tabIcons = CreateTabIcons(ThemePalette.Light.TextPrimary);
            tabs.ImageList = _tabIcons;
            tabs.DrawItem += DrawTab;
            // INPUT-PARITY rule 2: the focus frame follows the strip's focus (DrawTab).
            tabs.GotFocus += (_, _) => tabs.Invalidate();
            tabs.LostFocus += (_, _) => tabs.Invalidate();
            _uiLanguage.Items.AddRange(Localization.Names);
            tabs.TabPages.Add(WithIcon(Page("Общие", "Основные параметры приложения. Все изменения применяются сразу.",
                Setting(_autostart, Autostart.ManagedByWindows
                    ? "Автозапуском версии из Microsoft Store управляет сама Windows. Флажок показывает текущее состояние, а нажатие открывает «Параметры ▸ Приложения ▸ Автозагрузка», где автозапуск включается и выключается."
                    : "Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."),
                ShortcutSettings(),
                Setting(_keepAwake, "Пока включено, Windows не уходит в сон или гибернацию по простою — удобно для долгих загрузок, копирования или рендера. Состояние сохраняется: забытый включённым переключатель не даст компьютеру уснуть и после перезапуска - следить за вашей батареей CyrFlip не станет."),
                Setting(_keepScreen, "Экран не гаснет и не блокируется по бездействию, как во время просмотра видео. Блокировку по паролю (Win+L или политику безопасности) это не отменяет. Состояние сохраняется: забытый включённым переключатель не даст экрану погаснуть и после перезапуска."),
                Setting(LanguageRow(), "Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее."),
                Setting(ThemeRow(), "«Как в Windows» - CyrFlip светлый или тёмный вместе с Windows и переключается вслед за ней без перезапуска. Контрастные темы Windows всегда важнее этой настройки. Метка раскладки у курсора и каретки от темы не зависит: её цвет обозначает раскладку.")), 0));
            tabs.TabPages.Add(WithIcon(Page("Индикаторы", "Параметры, которые помогают увидеть активную раскладку до ввода текста.",
                Setting(_cursor, "Заменяет стандартный текстовый курсор I-beam на курсор с маленькой меткой текущей раскладки. Обычная стрелка мыши не меняется."),
                Setting(_caret, "Рисует небольшую метку рядом с мигающей кареткой в поле ввода. Работает в приложениях, которые передают Windows положение каретки."),
                Setting(_dot, "Вместо букв раскладки рядом с кареткой показывает компактную цветную точку — удобно, если буквы отвлекают."),
                Setting(MarkerSizeRow(), "Размер метки у каретки и на текстовом курсоре мыши. Он дополнительно подстраивается под масштаб экрана, а курсор мыши - ещё и под размер указателя из специальных возможностей Windows. Применяется сразу."),
                Setting(_language, "После конвертации текста переключает раскладку активного окна на ту, в которой текст теперь набран, чтобы можно было сразу продолжить печатать."),
                Setting(_caps, "После исправления регистра меняет физическое состояние CapsLock, чтобы следующие нажатия соответствовали исправленному тексту.")), 1));
            tabs.TabPages.Add(WithIcon(Page("Горячие клавиши", "Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows. Каждый хоткей можно включить или отключить отдельно.",
                Setting(_enableHotkeys, "Общий выключатель всех горячих клавиш. Когда снят, CyrFlip не перехватывает ни одной комбинации — клавиши проходят в приложение как обычно."),
                HotkeyRow(_caseEnabled, _caseHotkeyValue, _setCaseHotkey, "Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock."),
                HotkeyRow(_historyEnabled, _historyHotkeyValue, _setHistoryHotkey, "Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию."),
                Setting(_deferRdp, "Когда в фокусе окно клиента удалённого рабочего стола (mstsc/msrdc), CyrFlip не перехватывает хоткеи — клавиша уходит в удалённый сеанс, где её обработает CyrFlip на той машине. Включите, если утилита запущена на обеих сторонах RDP."),
                new Label { Text = "Здесь только эти два хоткея. Все комбинации, которые конвертируют текст из одной раскладки в другую — включая EN ⇄ RU на Ctrl+Shift+F12 — живут одной таблицей на вкладке «Конвертация раскладок».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 4) },
                Setting(_contextMenuEnabled, "Аккорд мыши открывает меню CyrFlip рядом с указателем: копировать, вырезать, вставить, конвертация раскладок, регистр, перевод, быстрый запуск. Родное меню приложения при этом не появляется. Пока флажок снят, CyrFlip вообще не следит за мышью."),
                Setting(ContextMenuChordRow(), "Ctrl и правая кнопка ничем в Windows не заняты. Shift и правая кнопка отберут у Проводника расширенное меню, а средняя кнопка отберёт автоскролл и открытие ссылки в новой вкладке."),
                HotkeyRow(_textMenuHotkeyEnabled, _textMenuHotkeyValue, () => TextMenuHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "То же меню с клавиатуры: открывается у текстового курсора, а где его не видно - у указателя мыши. Стрелки выбирают пункт, Enter выполняет, Esc закрывает.")), 2));
            tabs.TabPages.Add(WithIcon(ConversionsPage(), 7));
            tabs.TabPages.Add(WithIcon(LanguagesPage(), 6));
            tabs.TabPages.Add(WithIcon(ClipboardPage(), 3));
            tabs.TabPages.Add(WithIcon(QuickNotesPage(), 10));
            tabs.TabPages.Add(WithIcon(GraphicsPage(), 11));
            TabPage translatePage = WithIcon(TranslatePage(), 9);
            tabs.TabPages.Add(translatePage);
            tabs.TabPages.Add(WithIcon(LauncherPage(), 8));
            tabs.TabPages.Add(WithIcon(AboutPage(), 5));
            Controls.Add(tabs);

            // Come back to the page the user was last on. Restored before subscribing so the restore
            // itself doesn't count as a change, and clamped: the tab strip grows between versions.
            if (_config.SettingsTab > 0 && _config.SettingsTab < tabs.TabPages.Count)
                tabs.SelectedIndex = _config.SettingsTab;
            tabs.SelectedIndexChanged += (_, _) =>
            {
                _config.SaveSettingsTab(tabs.SelectedIndex);
                // Old installer downloads go when the translator page is looked at (S0037 TR-5).
                if (ReferenceEquals(tabs.SelectedTab, translatePage))
                    OllamaManager.PruneDownloadsInBackground();
            };

            _cursor.CheckedChanged += (_, _) => Changed(_setCursor, _cursor.Checked);
            if (Autostart.ManagedByWindows)
            {
                // Packaged build: the startupTask is Windows' to flip, not ours. The checkbox reports
                // the state (AutoCheck off, so a click never fakes a change it cannot make) and the
                // click opens the Startup-apps page; the state is re-read when the window comes back.
                _autostart.AutoCheck = false;
                _autostart.Click += (_, _) => { if (!_loading) _setAutostart(!_autostart.Checked); };
                Activated += (_, _) => RefreshAutostart();
            }
            else
            {
                _autostart.CheckedChanged += (_, _) => Changed(_setAutostart, _autostart.Checked);
            }
            if (!AppShortcuts.ManagedByWindows)
            {
                _startMenuShortcut.CheckedChanged += (_, _) => SetShortcut(ShortcutPlace.StartMenu, _startMenuShortcut);
                _desktopShortcut.CheckedChanged += (_, _) => SetShortcut(ShortcutPlace.Desktop, _desktopShortcut);
                // The user may delete or restore a shortcut outside CyrFlip while the window is open.
                Activated += (_, _) => RefreshShortcuts();
            }
            _keepAwake.CheckedChanged += (_, _) => Changed(_setKeepAwake, _keepAwake.Checked);
            _keepScreen.CheckedChanged += (_, _) => Changed(_setKeepScreen, _keepScreen.Checked);
            _caret.CheckedChanged += (_, _) => Changed(_setCaret, _caret.Checked);
            _dot.CheckedChanged += (_, _) => Changed(_setDot, _dot.Checked);
            _markerSize.ValueChanged += (_, _) =>
            {
                _markerSizeValue.Text = MarkerSizeName(_markerSize.Value);
                if (_loading) return;
                _config.CursorSize = MarkerSize.Presets[_markerSize.Value];
                MarkerSizeChanged?.Invoke(this, EventArgs.Empty);
            };
            _language.CheckedChanged += (_, _) => Changed(_setLanguage, _language.Checked);
            _caps.CheckedChanged += (_, _) => Changed(_setCaps, _caps.Checked);
            _history.CheckedChanged += (_, _) => Changed(_setHistory, _history.Checked);
            _pause.CheckedChanged += (_, _) => Changed(_setPause, _pause.Checked);
            _historyStartup.CheckedChanged += (_, _) => Changed(_setHistoryStartup, _historyStartup.Checked);
            _enableHotkeys.CheckedChanged += (_, _) => Changed(_setHotkeysEnabled, _enableHotkeys.Checked);
            _caseEnabled.CheckedChanged += (_, _) => Changed(_setCaseEnabled, _caseEnabled.Checked);
            _historyEnabled.CheckedChanged += (_, _) => Changed(_setHistoryEnabled, _historyEnabled.Checked);
            _deferRdp.CheckedChanged += (_, _) => Changed(_setDeferRdp, _deferRdp.Checked);
            // The UIA AutomationId tools/uitest/Test-LongRun.ps1 -SettingsToggles finds it by: a toggle
            // runs the whole Reload(), and an even count leaves the setting as it was (S0007 ST-5).
            _deferRdp.Name = "deferRdp";
            // Nothing to rebind or reinstall - the conversion reads the value when a chord fires, so
            // writing it and saving is the whole of it.
            _convertSymbols.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                _config.ConvertSymbols = _convertSymbols.Checked;
                _config.Save();
            };
            // Same shape as the translator tab: write the value, say "something changed", let the
            // context save the config and install or drop the mouse hook.
            _contextMenuEnabled.CheckedChanged += (_, _) =>
                TextMenuSettingChanged(() => _config.EnableContextMenu = _contextMenuEnabled.Checked, reload: true);
            _contextMenuChord.SelectedIndexChanged += (_, _) =>
            {
                if (_contextMenuChord.SelectedItem is ChordItem item)
                    TextMenuSettingChanged(() => _config.ContextMenuChord = item.Token);
            };
            _textMenuHotkeyEnabled.CheckedChanged += (_, _) =>
                TextMenuSettingChanged(() => _config.EnableTextMenuHotkey = _textMenuHotkeyEnabled.Checked);
            _restoreLanguageHotkeys.Click += (_, _) => RestoreLanguageHotkeys();
            _restoreLayouts.Click += (_, _) => RestoreLayouts();
            _toggleCombo.SelectedIndexChanged += (_, _) => OnToggleChanged();
            _uiLanguage.SelectedIndexChanged += (_, _) => { if (!_loading && _uiLanguage.SelectedItem is string language) { _setUiLanguage(language); ApplyLanguage(); } };
            _themeSystem.CheckedChanged += (_, _) => ThemeChosen(_themeSystem, ThemeMode.System);
            _themeLight.CheckedChanged += (_, _) => ThemeChosen(_themeLight, ThemeMode.Light);
            _themeDark.CheckedChanged += (_, _) => ThemeChosen(_themeDark, ThemeMode.Dark);
            _opacity.ValueChanged += (_, _) => { if (!_loading) { _opacityValue.Text = _opacity.Value + "%"; _setOpacity(_opacity.Value); } };

            // The quick-notes tab, same shape as the translator's: write the value, say "something
            // changed", let the context save and rebind.
            _quickNotesEnabled.CheckedChanged += (_, _) =>
                QuickNotesSettingChanged(() => _config.EnableQuickNotes = _quickNotesEnabled.Checked, reload: true);
            _quickNotesHotkeyEnabled.CheckedChanged += (_, _) =>
                QuickNotesSettingChanged(() => _config.EnableQuickNotesHotkey = _quickNotesHotkeyEnabled.Checked);
            _quickNotesWrap.CheckedChanged += (_, _) =>
                QuickNotesSettingChanged(() => _config.QuickNotesWordWrap = _quickNotesWrap.Checked);

            // The Graphics tab (S0026), same shape again.
            _screenshotHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotHotkey = _screenshotHotkeyEnabled.Checked);
            _screenshotSave.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.ScreenshotSaveEnabled = _screenshotSave.Checked, reload: true);

            // The translator tab talks to the context through one event, so every handler here just
            // writes the value and says "something changed"; the context saves and rebinds.
            _translateEnabled.CheckedChanged += (_, _) => TranslateChanged(() => _config.EnableTranslate = _translateEnabled.Checked, reload: true);
            _translateAutoStart.CheckedChanged += (_, _) => TranslateChanged(() => _config.TranslateAutoStartServer = _translateAutoStart.Checked);
            _translateCopy.CheckedChanged += (_, _) => TranslateChanged(() => _config.TranslateCopyResult = _translateCopy.Checked);
            _translatePaste.CheckedChanged += (_, _) => TranslateChanged(() => _config.TranslatePasteResult = _translatePaste.Checked);
            _translateShowSource.CheckedChanged += (_, _) => TranslateChanged(() => _config.TranslateShowSource = _translateShowSource.Checked);
            _translateKeepAlive.ValueChanged += (_, _) => TranslateChanged(() => _config.TranslateKeepAliveMinutes = (int)_translateKeepAlive.Value);
            _translateTimeout.ValueChanged += (_, _) => TranslateChanged(() => _config.TranslateTimeoutSeconds = (int)_translateTimeout.Value);
            _translateWindowTimeout.ValueChanged += (_, _) => TranslateChanged(() => _config.TranslateWindowTimeout = (int)_translateWindowTimeout.Value);
            // Text fields commit when the user leaves them (and when the window closes), not on every
            // keystroke - the context writes the registry on each change.
            _translateEndpoint.Leave += (_, _) => CommitTranslateText();
            _translateModel.Leave += (_, _) => CommitTranslateText();
            VisibleChanged += (_, _) => { if (!Visible) { CommitTranslateText(); CancelTranslateWork(); } };
            // The captions are remembered before the first Reload fills any value in (S0036 UI-10): a
            // value label filled there - a chord, the version, a size - was registered as a "Russian
            // original" and put back over the live value by every re-translation, the S0007 ST-4 bug
            // class. It only held because ApplyLanguage happened to set each of them again.
            RememberRussianTexts(this);
            Reload();
            ApplyLanguage();
        }

        public void Reload()
        {
            _loading = true;
            _startMenuShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.StartMenu);
            _desktopShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.Desktop);
            _autostart.Checked = Autostart.IsEnabled;
            // The live request is the truth here, not the config: the tray items flip it too, and the
            // config is only what it was restored from (and is written back to) on the next toggle.
            _keepAwake.Checked = KeepAwake.KeepSystemAwake;
            _keepScreen.Checked = KeepAwake.KeepScreenOn;
            // The name the UI actually falls back to, not the raw value: a stored language outside the
            // 13 used to leave the picker on its first entry (Russian) while the UI spoke English
            // (ticket S0007, CF-3).
            _uiLanguage.SelectedItem = Localization.Names[Localization.IndexOf(_config.UiLanguage)];
            ThemeMode theme = ThemeModes.Parse(_config.Theme);
            _themeSystem.Checked = theme == ThemeMode.System;
            _themeLight.Checked = theme == ThemeMode.Light;
            _themeDark.Checked = theme == ThemeMode.Dark;
            _cursor.Checked = _config.EnableCursorChange; _caret.Checked = _config.EnableCaretOverlay; _dot.Checked = _config.CaretDotMode;
            _markerSize.Value = MarkerSize.NearestPreset(_config.CursorSize);
            _markerSizeValue.Text = MarkerSizeName(_markerSize.Value);
            _language.Checked = _config.EnableLanguageSwitch; _caps.Checked = _config.FlipCapsLockAfter;
            _history.Checked = _config.EnableClipboardHistory; _pause.Checked = _config.PauseClipboardHistory;
            _historyStartup.Checked = _config.ShowClipboardHistoryOnStartup;
            _opacity.Value = Math.Max(_opacity.Minimum, Math.Min(_opacity.Maximum, _config.ClipboardHistoryOpacity));
            _opacityValue.Text = _opacity.Value + "%";
            _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
            _version.Text = VersionLine();
            _pause.Enabled = _history.Checked; _historyStartup.Enabled = _history.Checked; _opacity.Enabled = _history.Checked;
            _enableHotkeys.Checked = _config.EnableHotkeys;
            _caseEnabled.Checked = _config.EnableCaseHotkey; _historyEnabled.Checked = _config.EnableHistoryHotkey;
            _deferRdp.Checked = _config.DeferToRemoteDesktop;
            _convertSymbols.Checked = _config.ConvertSymbols;
            // The per-hotkey switches only matter while the master switch is on.
            _caseEnabled.Enabled = _historyEnabled.Enabled = _enableHotkeys.Checked;
            // The context menu is a mouse chord, so it is deliberately NOT gated by the keyboard
            // master switch: it still works when the hotkeys are off (it just shows no chords).
            _contextMenuEnabled.Checked = _config.EnableContextMenu;
            _contextMenuChord.Enabled = _config.EnableContextMenu;
            _textMenuHotkeyEnabled.Checked = _config.EnableTextMenuHotkey;
            _textMenuHotkeyEnabled.Enabled = _config.EnableContextMenu;
            _textMenuHotkeyValue.Text = _config.TextMenuHotkey;
            _launcherEnabled.Checked = _config.EnableScenarioLauncher;
            ReloadQuickNotesState();
            ReloadGraphicsState();
            ReloadTranslateState();
            _loading = false;
            ApplyLanguage();
        }

        /// <summary>
        /// Re-reads the Windows-owned autostart state (packaged build) without letting the refresh
        /// count as a user change - the user flips it in Windows' own Startup-apps page, so the
        /// checkbox has to catch up when the settings window is activated again.
        /// </summary>
        private void RefreshAutostart()
        {
            bool loading = _loading;
            _loading = true;
            _autostart.Checked = Autostart.IsEnabled;
            _loading = loading;
        }

        /// <summary>
        /// The two shortcut checkboxes of the General page. The packaged build gets none: Windows puts
        /// the package in the Start menu itself, and a desktop link written by the app would outlive it.
        /// </summary>
        private Control ShortcutSettings()
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            if (AppShortcuts.ManagedByWindows) return panel;
            panel.Controls.Add(Setting(_startMenuShortcut, "CyrFlip ставится без установщика, поэтому ярлыки в меню «Пуск» и на рабочем столе создаёт он сам при первом запуске. Если перенести папку с программой, ярлыки при следующем запуске сами укажут на новое место."));
            panel.Controls.Add(Setting(_desktopShortcut, "Удалённый ярлык CyrFlip сам больше не создаёт - вернуть его можно только этим флажком."));
            return panel;
        }

        /// <summary>A shortcut checkbox changed: create or delete the link, and show the truth if that failed.</summary>
        private void SetShortcut(ShortcutPlace place, CheckBox box)
        {
            if (_loading) return;
            try { AppShortcuts.Set(place, box.Checked); }
            catch { }
            RefreshShortcuts();
        }

        private void RefreshShortcuts()
        {
            bool loading = _loading;
            _loading = true;
            _startMenuShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.StartMenu);
            _desktopShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.Desktop);
            _loading = loading;
        }

        private void RememberRussianTexts(Control root)
        {
            // The language-hotkey rows are thrown away and rebuilt on every refresh, already in the
            // current language. Registering them would both leak dead Control keys into the map and
            // capture translated text as if it were the Russian original.
            if (ReferenceEquals(root, _languageRows) || ReferenceEquals(root, _layoutRows)
                || ReferenceEquals(root, _conversionRows) || ReferenceEquals(root, _translationRows)) return;
            // A value label, not a caption: its "100%" registered here was put back on top of the
            // slider's real value by every re-translation (ticket S0007, ST-4).
            if (ReferenceEquals(root, _opacityValue)) return;

            // ComboBox.Text is its selected value, not a UI caption. Translating it would reset
            // the language selector back to the first Russian value on every UI refresh. Same for
            // NumericUpDown, whose Text is the number the user just typed, and for every text box:
            // a box filled from the config (the translator's server address) would be captured as if
            // it were a Russian caption and restored on top of the user's edit at the next language
            // change. The walk runs before the first Reload for the same reason (S0036 UI-10).
            if (!(root is ComboBox) && !(root is NumericUpDown) && !(root is TextBoxBase)
                && !_russianTexts.ContainsKey(root) && !string.IsNullOrEmpty(root.Text))
                _russianTexts.Add(root, root.Text);
            foreach (Control child in root.Controls) RememberRussianTexts(child);
        }

        private void ApplyLanguage()
        {
            ApplyScript();
            foreach (KeyValuePair<Control, string> pair in _russianTexts)
                pair.Key.Text = Translate(pair.Value);
            _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
            _quickNotesHotkeyValue.Text = _config.QuickNotesHotkey;
            _textMenuHotkeyValue.Text = _config.TextMenuHotkey;
            _screenshotHotkeyValue.Text = _config.ScreenshotHotkey;
            _screenshotFolderBox.Text = ScreenshotFolderShown();
            _opacityValue.Text = _opacity.Value + "%";
            _markerSizeValue.Text = MarkerSizeName(_markerSize.Value);
            _version.Text = VersionLine();
            AlignHotkeyCaptions();
            AdjustTabStrip();
            // Built in the current language, so these must run after the translation pass above.
            ReloadLayoutRows();
            ReloadLanguageRows();
            ReloadConversionRows();
            ReloadTranslationRows();
            ReloadToggleCombo();
            ReloadContextMenuChords();
            // Every Reload() ends here - i.e. every checkbox on every page - so the scenario table is
            // rebuilt only when something it shows changed (S0030 HT-1).
            string launcherStamp = LauncherRowsStamp();
            if (launcherStamp != _launcherRowsStamp)
                ReloadLauncherRows();
            else
                ApplyLauncherEnabledState();
        }

        /// <summary>What the scenario table depends on: the language, the store's content and the switch.</summary>
        private string LauncherRowsStamp()
            => _config.UiLanguage + "|" + _launcherStore.Version + "|" + _config.EnableScenarioLauncher;

        /// <summary>
        /// Refill the chord dropdown in the current language. The stored value is an invariant token,
        /// so switching the UI language re-labels the list without touching what is saved.
        /// </summary>
        private void ReloadContextMenuChords()
        {
            bool loading = _loading;
            _loading = true;
            _contextMenuChord.Items.Clear();
            string current = MouseChord.Parse(_config.ContextMenuChord).Token;
            int select = 0;
            foreach (string token in MouseChord.Choices)
            {
                int i = _contextMenuChord.Items.Add(new ChordItem
                {
                    Token = token,
                    Label = MouseChord.Parse(token).Display(Translate),
                });
                if (token == current) select = i;
            }
            _contextMenuChord.SelectedIndex = select;
            int max = 260;
            foreach (ChordItem item in _contextMenuChord.Items)
            {
                int w = TextRenderer.MeasureText(item.Label, Font).Width + 36;
                if (w > max) max = w;
            }
            _contextMenuChord.Width = max;
            _contextMenuChord.DropDownWidth = max;
            _loading = loading;
        }

        private sealed class ChordItem
        {
            public string Token = "";
            public string Label = "";
            public override string ToString() => Label;
        }

        private Control ContextMenuChordRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Аккорд мыши:", AutoSize = true, Width = 155, Padding = new Padding(0, 7, 0, 0) });
            row.Controls.Add(_contextMenuChord);
            return row;
        }

        /// <summary>Apply one context-menu setting and tell the context; mirrors <see cref="TranslateChanged"/>.</summary>
        private void TextMenuSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            TextMenuChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        /// <summary>
        /// Mirror the translator settings into the tab and gate the whole group behind its switch -
        /// done here rather than in <see cref="TranslatePage"/>, like every other master switch.
        /// </summary>
        private void ReloadTranslateState()
        {
            _translateEnabled.Checked = _config.EnableTranslate;
            _translateAutoStart.Checked = _config.TranslateAutoStartServer;
            _translateCopy.Checked = _config.TranslateCopyResult;
            _translatePaste.Checked = _config.TranslatePasteResult;
            _translateShowSource.Checked = _config.TranslateShowSource;
            _translateEndpoint.Text = _config.TranslateEndpoint;
            if (_translateModel.Items.Count == 0)
                foreach (string model in TranslationService.RecommendedModels) _translateModel.Items.Add(model);
            _translateModel.Text = _config.TranslateModel;
            _translateKeepAlive.Value = Clamp(_translateKeepAlive, _config.TranslateKeepAliveMinutes);
            _translateTimeout.Value = Clamp(_translateTimeout, _config.TranslateTimeoutSeconds);
            _translateWindowTimeout.Value = Clamp(_translateWindowTimeout, _config.TranslateWindowTimeout);

            bool on = _config.EnableTranslate;
            _translateAutoStart.Enabled = _translateCopy.Enabled = _translatePaste.Enabled = _translateShowSource.Enabled = on;
            _translateEndpoint.Enabled = _translateModel.Enabled = on;
            _translateKeepAlive.Enabled = _translateTimeout.Enabled = _translateWindowTimeout.Enabled = on;
            foreach (Button button in _translateButtons) button.Enabled = on && !_translateBusy;
        }

        private static decimal Clamp(NumericUpDown box, int value)
            => Math.Max(box.Minimum, Math.Min(box.Maximum, value));

        /// <summary>
        /// Keeps the chord column of the hotkey rows aligned without a fixed pixel width:
        /// each caption auto-sizes (so nothing is ever clipped), then they all get a minimum
        /// width equal to the widest one. Re-run whenever the captions change language.
        /// </summary>
        private void AlignHotkeyCaptions()
        {
            CheckBox[] boxes = { _caseEnabled, _historyEnabled, _textMenuHotkeyEnabled };
            int widest = 0;
            foreach (CheckBox box in boxes)
            {
                box.MinimumSize = Size.Empty;
                widest = Math.Max(widest, box.GetPreferredSize(Size.Empty).Width);
            }
            widest += 16; // breathing room before the chord
            foreach (CheckBox box in boxes) box.MinimumSize = new Size(widest, 0);
        }

        /// <summary>Roomy default, but never larger than the screen it opens on.</summary>
        private static Size DefaultWindowSize()
        {
            try
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                return new Size(Math.Min(1340, Math.Max(900, work.Width - 120)), Math.Min(940, Math.Max(620, work.Height - 100)));
            }
            catch { return new Size(1340, 940); }
        }

        /// <summary>
        /// Sizes the left tab strip to the widest caption in the current language. With
        /// <see cref="TabAlignment.Left"/> the two <c>ItemSize</c> components swap meaning:
        /// <b>Width</b> is the height of one tab, <b>Height</b> is how far the strip reaches sideways.
        /// </summary>
        private void AdjustTabStrip()
        {
            if (_tabs == null) return;
            int widest = 0;
            using (var bold = new Font(Font, FontStyle.Bold))
                foreach (TabPage page in _tabs.TabPages)
                    widest = Math.Max(widest, TextRenderer.MeasureText(page.Text, bold).Width);
            int rowHeight = Math.Max(32, Font.Height + 16);
            _tabs.ItemSize = new Size(rowHeight, Math.Max(170, widest + 60));
        }

        /// <summary>Owner-draws one left-aligned tab: icon + horizontal caption, the selected one bold
        /// with an accent bar, so the strip reads as a navigation list rather than a hairline.</summary>
        private void DrawTab(object? sender, DrawItemEventArgs e)
        {
            if (_tabs == null || e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            TabPage page = _tabs.TabPages[e.Index];
            bool selected = _tabs.SelectedIndex == e.Index;
            Rectangle bounds = e.Bounds;
            // The selected page shares the page's own surface; in light that is exactly the window
            // and control colours this list was always drawn with.
            ThemePalette palette = ThemeApply.PaletteOf(_tabs) ?? ThemePalette.Light;
            using (var back = new SolidBrush(selected ? palette.SurfaceRaised : palette.SurfaceWindow))
                e.Graphics.FillRectangle(back, bounds);
            if (selected)
                using (var accent = new SolidBrush(palette.Accent))
                    e.Graphics.FillRectangle(accent, bounds.Left, bounds.Top, 4, bounds.Height);

            int x = bounds.Left + 12;
            if (_tabs.ImageList != null && page.ImageIndex >= 0 && page.ImageIndex < _tabs.ImageList.Images.Count)
            {
                Image icon = _tabs.ImageList.Images[page.ImageIndex];
                e.Graphics.DrawImage(icon, x, bounds.Top + (bounds.Height - icon.Height) / 2, icon.Width, icon.Height);
                x += icon.Width + 9;
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            using (var font = new Font(Font, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, page.Text, font, new Rectangle(x, bounds.Top, Math.Max(10, bounds.Right - x - 8), bounds.Height),
                    palette.TextPrimary, flags);
            // The native strip's own focus rectangle is gone with the owner draw; a keyboard user
            // still has to see that the page list holds the focus (INPUT-PARITY rule 2).
            if (selected && _tabs.Focused)
                using (var focus = new Pen(palette.TextPrimary) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    e.Graphics.DrawRectangle(focus, bounds.Left + 7, bounds.Top + 3, bounds.Width - 12, bounds.Height - 7);
        }

        /// <summary>
        /// The page icons are the mono look (ICON-RENDER section 10 C: tabs), in the theme's text colour -
        /// which is what the dark theme changes - so they are drawn again when it changes (S0021 A10 item 3,
        /// S0022 A2). The selected page keeps its accent bar; that is not a glyph.
        /// </summary>
        protected override void OnThemeApplied(ThemePalette palette)
        {
            if (_tabs == null || _tabIcons == null || _tabIconsInk == palette.TextPrimary) return;
            ImageList previous = _tabIcons;
            _tabIcons = CreateTabIcons(palette.TextPrimary);
            _tabIconsInk = palette.TextPrimary;
            _tabs.ImageList = _tabIcons;
            previous.Dispose();
            _tabs.Invalidate();
        }

        /// <summary>The settings window closes (hides) on Escape per APP-BEHAVIOUR rule 1 and APP-SETTINGS rule 9.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Every user-facing string here is keyed by its Russian original - see <see cref="Localization"/>.</summary>
        private string Translate(string ru) => Localization.Translate(_config.UiLanguage, ru);

        /// <summary>Column width for the current language: Devanagari, Bengali and the longer Latin
        /// languages need more room than the Russian original the pixel numbers were measured with.</summary>
        private int Column(int width) => Localization.Scaled(_config.UiLanguage, width);

        /// <summary>
        /// Mirrors the window for Arabic and Urdu and swaps in a font that covers the script when the
        /// default UI font does not (Devanagari, Bengali, Chinese). Both have to be applied to the
        /// whole form: WinForms propagates <c>RightToLeft</c> and <c>Font</c> to child controls.
        /// </summary>
        private void ApplyScript()
        {
            bool rtl = Localization.IsRightToLeft(_config.UiLanguage);
            RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = rtl;
            string? family = Localization.FontFamily(_config.UiLanguage);
            string wanted = family ?? MessageBoxFont.FontFamily.Name;
            if (Font.FontFamily.Name == wanted) return;
            try
            {
                // Both fonts are ours: WinForms disposes neither the font assigned to a control nor the
                // one it replaces. Release the outgoing pair only after every control has been moved to
                // the incoming one - a control drawing with a disposed font throws on the next paint.
                Font? previousOwn = _ownFont;
                Font? previousBold = _boldFont;
                Font = _ownFont = new Font(wanted, MessageBoxFont.SizeInPoints);
                _boldFont = null;       // BoldFont re-derives itself from the new font on first use
                RefreshBoldFonts(this);
                previousBold?.Dispose();
                previousOwn?.Dispose();
            }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        /// <summary>
        /// Section headers and table headings carry an explicit <b>bold</b> font, and an explicit font is
        /// not inherited - so the form switching to Nirmala UI or Microsoft YaHei UI would leave exactly
        /// those captions behind in a family that cannot draw the script. Re-point each of them at the
        /// current <see cref="BoldFont"/>.
        /// </summary>
        private void RefreshBoldFonts(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child.Font.Bold) child.Font = BoldFont;
                RefreshBoldFonts(child);
            }
        }

        /// <summary>
        /// The one-stop "you never have to open the Windows language pane" tab: install / remove /
        /// reorder keyboard layouts, pick the whole-cycle switch chord, and assign the per-language
        /// direct-switch hotkeys. Every control here writes a <b>system</b> setting, not a CyrFlip one -
        /// see <see cref="InputLayouts"/> and <see cref="LanguageHotkeys"/>.
        /// </summary>
        private TabPage LanguagesPage()
        {
            var page = Page("Языки Windows", "Всё, что раньше требовало раздела «Язык и регион» в параметрах Windows: раскладки клавиатуры, порядок между ними и сочетания переключения. Раскладки уже входят в состав Windows — ничего не скачивается. Установка языка интерфейса (перевод самой Windows) остаётся за кнопкой «Открыть настройки Windows».");
            var panel = ContentPanel(page);

            if (PackageInfo.IsPackaged)
                panel.Controls.Add(new Label
                {
                    Text = "Версия из Microsoft Store работает в контейнере, поэтому Windows может перенаправить запись в реестр внутрь пакета. Если раскладка или сочетание не подхватились даже после повторного входа в систему, задайте их в настройках Windows кнопкой внизу.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.Warning, Margin = new Padding(3, 0, 3, 10),
                });

            panel.Controls.Add(SectionHeader("Раскладки клавиатуры"));
            panel.Controls.Add(_layoutRows);
            panel.Controls.Add(new Label { Text = "Стрелки меняют порядок раскладок внутри одного языка. Порядок самих языков задаёт Windows и восстанавливает его при входе в систему — меняйте его в параметрах Windows.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 2, 3, 2) });
            var layoutButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 4, 3, 2) };
            layoutButtons.Controls.Add(Button("Добавить раскладку...", OnAddLayout));
            layoutButtons.Controls.Add(Button("Добавить популярные языки", AddPopularLayouts));
            layoutButtons.Controls.Add(_restoreLayouts);
            panel.Controls.Add(layoutButtons);
            panel.Controls.Add(new Label { Text = "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian и Ukrainian. Для языков с популярными вариантами (например, US International, Spanish Latin American) используйте «Добавить раскладку...».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 5) });

            panel.Controls.Add(SectionHeader("Переключение по кругу"));
            var toggleRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            toggleRow.Controls.Add(new Label { Text = "Сочетание для перебора языков:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            toggleRow.Controls.Add(_toggleCombo);
            panel.Controls.Add(Setting(toggleRow, "Одно сочетание перебирает установленные языки по кругу. Это штатная настройка Windows (Alt+Shift, Ctrl+Shift или «`»); «—» отключает перебор."));

            panel.Controls.Add(SectionHeader("Прямые сочетания на язык"));
            panel.Controls.Add(new Label { Text = "Эти сочетания обрабатывает сама Windows: они работают, даже когда CyrFlip закрыт. Комбинацию вы выбираете сами — в отличие от штатного окна, здесь не только Ctrl+Shift+цифра.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 4) });
            panel.Controls.Add(_languageRows);
            panel.Controls.Add(Setting(_restoreLanguageHotkeys, "Возвращает языковые сочетания Windows в то состояние, в котором они были до первого изменения из CyrFlip."));

            panel.Controls.Add(Button("Открыть настройки Windows", () => Open("ms-settings:keyboard")));
            return page;
        }

        /// <summary>
        /// The conversion table: as many "from layout → to layout" pairs as the user wants, each with
        /// its own global chord (Ctrl+Shift+F12 = EN ⇄ RU, Alt+Shift+F12 = EN ⇄ UK, Ctrl+Alt+F12 =
        /// RU ⇄ UK, ...). It is the <b>only</b> home of these chords: the EN ⇄ RU flip CyrFlip started
        /// life with is an ordinary seeded row here, editable and deletable like every other.
        /// </summary>
        private TabPage ConversionsPage()
        {
            var page = Page("Конвертация раскладок", "Таблица преобразований: сколько угодно пар раскладок, у каждой своя горячая клавиша. Строка EN ⇄ RU на Ctrl+Shift+F12 создаётся при первом запуске — её можно изменить или удалить, как любую другую. Каждая пара работает в обе стороны: если активна вторая раскладка пары, та же комбинация конвертирует обратно. Текст переводится по физическим клавишам выбранных раскладок.");
            var panel = ContentPanel(page);
            panel.Controls.Add(_conversionRows);
            panel.Controls.Add(Button("Добавить конвертацию...", AddConversionProfile));
            panel.Controls.Add(Setting(_convertSymbols, "Клавиша, которая в обеих раскладках даёт знак, а не букву, не говорит о том, в какой раскладке её нажали: за «/» на русской клавише стоит точка, а «/» с цифрового блока вообще одинаков везде и в скопированном тексте неотличим от обычного. Пока флажок стоит, такие знаки конвертируются вместе с текстом - так CyrFlip вёл себя всегда. Снимите его, если чаще набираете их намеренно: тогда «/ghbdtn» сохранит свой слеш вместо того, чтобы начаться с точки. Знаки, на клавише которых в другой раскладке стоит буква (запятая, скобки), конвертируются в любом случае."));
            panel.Controls.Add(new Label { Text = "Работают только установленные в Windows раскладки — добавьте нужные на вкладке «Языки Windows». Одну комбинацию нельзя отдать двум действиям CyrFlip.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 8, 3, 4) });
            return page;
        }

        // Russian text, not pre-translated: the header is created once and lives in the translatable
        // control tree, so the normal ApplyLanguage pass (which keys off the Russian original) handles it.
        // The explicit bold font stops it inheriting the form's - RefreshBoldFonts re-derives it whenever
        // the language brings a different family.
        private Label SectionHeader(string russianText)
            => new Label { Text = russianText, AutoSize = true, Font = BoldFont, Margin = new Padding(3, 16, 3, 4) };

        // Row height for the layout / hotkey tables. Fixed width but a height derived from the *current*
        // font plus MiddleLeft alignment: the text is vertically centred and never clipped, whether the
        // font grew because of display scaling or because the language needs a taller family (Devanagari,
        // Bengali, Chinese). A constant 26 px was chopping the header line in half on a scaled display.
        private int RowHeight => Math.Max(26, Font.Height + 9);
        private Label ColumnLabel(string text, int width, bool ellipsis = true)
            => new Label
            {
                Text = text, AutoSize = false, Width = width, Height = RowHeight,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = ellipsis,
                Margin = new Padding(3, 4, 3, 4),
            };

        /// <summary>Rebuilds the installed-layout rows from the registry.</summary>
        private void ReloadLayoutRows()
        {
            RowFocus focus = RowFocus.Capture(_layoutRows);
            _layoutRows.SuspendLayout();
            var old = new Control[_layoutRows.Controls.Count];
            _layoutRows.Controls.CopyTo(old, 0);
            _layoutRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();

            List<InputLayouts.Installed> installed = InputLayouts.ListInstalled();
            RefreshLayoutNames(installed);
            for (int i = 0; i < installed.Count; i++)
                _layoutRows.Controls.Add(LayoutRow(installed, i));

            _layoutRows.ResumeLayout();
            focus.Restore(_layoutRows, TakeRowFocusDelta());
            _restoreLayouts.Enabled = _config.InputLayoutsBackup.Length > 0;
        }

        // Column widths for the conversion table, measured on every refresh (see MeasureConversionColumns).
        private int _pairColumn, _chordColumn;

        /// <summary>
        /// The nominal pixel widths were measured against Russian at 100% scaling, so on a scaled display
        /// or in a longer language they cut the text - and the chord column has no ellipsis, which turned
        /// "Ctrl+Shift+F12" into a plausible-looking "Ctrl+Shift+F1". Measure what is actually going to be
        /// drawn: never narrower than the nominal width, and the pair column capped so the row buttons stay
        /// on screen (that column has an ellipsis and degrades gracefully).
        /// </summary>
        private void MeasureConversionColumns()
        {
            using var bold = new Font(Font, FontStyle.Bold);
            _pairColumn = Math.Max(Column(390), TextWidth(Translate("Пара раскладок"), bold));
            _chordColumn = Math.Max(Column(145), TextWidth(Translate("Комбинация"), bold));
            foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
            {
                _chordColumn = Math.Max(_chordColumn, TextWidth(profile.Hotkey, Font));
                _pairColumn = Math.Max(_pairColumn,
                    TextWidth(LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid), Font));
            }
            _pairColumn = Math.Min(_pairColumn, Column(560));
        }

        private static int TextWidth(string text, Font font) => TextRenderer.MeasureText(text, font).Width + 12;

        private void ReloadConversionRows()
        {
            RowFocus focus = RowFocus.Capture(_conversionRows);
            _conversionRows.SuspendLayout();
            var old = new Control[_conversionRows.Controls.Count]; _conversionRows.Controls.CopyTo(old, 0);
            _conversionRows.Controls.Clear(); foreach (Control control in old) control.Dispose();

            MeasureConversionColumns();
            _conversionRows.Controls.Add(ConversionHeaderRow());
            foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
                _conversionRows.Controls.Add(ConversionRow(profile));
            if (_config.LayoutConversionProfiles.Count == 0)
                _conversionRows.Controls.Add(new Label
                {
                    Text = Translate("Таблица пуста — ни одна комбинация сейчас не конвертирует текст."),
                    AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(25, 6, 3, 4),
                });
            _conversionRows.ResumeLayout();
            focus.Restore(_conversionRows, TakeRowFocusDelta());
        }

        private Control ConversionHeaderRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row.Controls.Add(new Label { Text = "", AutoSize = false, Width = 22, Height = 18 });
            Label pair = ColumnLabel(Translate("Пара раскладок"), _pairColumn);
            Label chord = ColumnLabel(Translate("Комбинация"), _chordColumn, ellipsis: false);
            // Derived from the form's font, not from MessageBoxFont: the header has to follow the family
            // the current language needs, and keep the row height the labels were built with.
            pair.Font = chord.Font = BoldFont;
            row.Controls.Add(pair); row.Controls.Add(chord);
            return row;
        }

        private Control ConversionRow(LayoutConversionProfile profile)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            var enabled = new RowCheckBox { Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 6, 2, 2) };
            enabled.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                // Switching a row back on is checked like assigning it (ticket S0004, KC-6).
                if (enabled.Checked && Hotkey.TryParse(profile.Hotkey, out Hotkey chord)
                    && !ChordIsFree(chord, ChordKind.Conversion, profile.Id, askAboutWindows: false))
                {
                    _loading = true;
                    enabled.Checked = false;
                    _loading = false;
                    return;
                }
                profile.Enabled = enabled.Checked;
                ConversionProfilesChanged?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(enabled);
            // "⇄", not "→": the pair converts in whichever direction the active layout implies.
            Label pair = ColumnLabel(LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid), _pairColumn);
            // A row is only live while the master switch is on, so say so instead of promising a chord.
            if (!_config.EnableHotkeys) pair.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(pair);
            row.Controls.Add(ColumnLabel(profile.Hotkey, _chordColumn, ellipsis: false));
            row.Controls.Add(Button(Translate("Изменить..."), () => EditConversionProfile(profile)));
            row.Controls.Add(Button(Translate("Удалить"), () => { _config.LayoutConversionProfiles.Remove(profile); ConversionProfilesChanged?.Invoke(this, EventArgs.Empty); ReloadConversionRows(); }));
            return row;
        }

        /// <summary>
        /// Display name for a KLID, from the cache filled while the layout rows are rebuilt. A layout
        /// the user has since removed keeps its two-letter code plus a note, so a stale row reads as
        /// "PL (не установлена)" rather than as a bare hex KLID.
        /// </summary>
        private string LayoutName(string klid)
        {
            if (_layoutNames.TryGetValue(klid, out string? name)) return name;
            string code = WorldLayouts.CodeForKlid(klid);
            return (code.Length > 0 ? code : klid) + " " + Translate("(не установлена)");
        }

        /// <summary>KLID → installed layout name, refreshed together with the layout table.</summary>
        private void RefreshLayoutNames(List<InputLayouts.Installed> installed)
        {
            _layoutNames.Clear();
            foreach (InputLayouts.Installed layout in installed)
                _layoutNames[layout.Klid] = layout.LanguageName + (layout.DisplayName.Length > 0 ? " — " + layout.DisplayName : "");
        }

        private void AddConversionProfile() => EditConversionProfile(null);

        private void EditConversionProfile(LayoutConversionProfile? existing)
        {
            List<InputLayouts.Installed> layouts = InputLayouts.ListInstalled();
            if (layouts.Count < 2) { Warn(Translate("Для конвертации установите как минимум две раскладки.")); return; }
            using var dialog = new LayoutConversionDialog(layouts, existing, _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile == null) return;
            LayoutConversionProfile profile = dialog.Profile;
            if (!Hotkey.TryParse(profile.Hotkey, out Hotkey chord)
                || !ChordIsFree(chord, ChordKind.Conversion, existing?.Id ?? profile.Id)) return;
            if (existing == null) _config.LayoutConversionProfiles.Add(profile);
            else
            {
                int index = _config.LayoutConversionProfiles.IndexOf(existing);
                if (index >= 0) _config.LayoutConversionProfiles[index] = profile;
            }
            ConversionProfilesChanged?.Invoke(this, EventArgs.Empty); ReloadConversionRows();
        }

        private Control LayoutRow(List<InputLayouts.Installed> all, int index)
        {
            InputLayouts.Installed layout = all[index];
            int total = all.Count;
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            string name = layout.LanguageName + (layout.DisplayName.Length > 0 ? " — " + layout.DisplayName : "");
            if (layout.IsDefault) name = "★ " + name;
            Label nameLabel = ColumnLabel(name, Column(330));
            nameLabel.Font = layout.IsDefault ? BoldFont : Font;
            row.Controls.Add(nameLabel);

            var up = Button("↑", () => MoveLayout(layout.Klid, -1));
            up.AccessibleName = Translate("Вверх");
            // Only within one language: the order of the languages is Windows' own and is put back at
            // sign-in (ticket S0007, WL-8) - the hint under the table says so.
            up.Width = 32; up.Height = RowHeight; up.AutoSize = false; up.Margin = new Padding(3, 4, 3, 4); up.Enabled = InputLayouts.CanMove(all, index, -1);
            var down = Button("↓", () => MoveLayout(layout.Klid, +1));
            down.AccessibleName = Translate("Вниз");
            down.Width = 32; down.Height = RowHeight; down.AutoSize = false; down.Margin = new Padding(3, 4, 3, 4); down.Enabled = InputLayouts.CanMove(all, index, +1);
            row.Controls.Add(up);
            row.Controls.Add(down);

            var makeDefault = Button(Translate("По умолчанию"), () => MakeDefaultLayout(layout.Klid));
            makeDefault.Enabled = !layout.IsDefault;
            row.Controls.Add(makeDefault);

            var remove = Button(Translate("Удалить"), () => OnRemoveLayout(layout));
            remove.Enabled = total > 1; // Windows always keeps at least one layout
            row.Controls.Add(remove);

            row.Controls.Add(new Label { Text = layout.Klid, AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(8, 5, 0, 0), Padding = new Padding(0, 1, 0, 0) });
            return row;
        }

        // Rows the focus shifts by on the next rebuild - set by a move, consumed by RowFocus.Restore.
        private int _rowFocusDelta;

        private int TakeRowFocusDelta()
        {
            int delta = _rowFocusDelta;
            _rowFocusDelta = 0;
            return delta;
        }

        private void MoveLayout(string klid, int delta)
        {
            if (!EnsureSystemBackups()) return;
            if (ReportLayoutEdit(InputLayouts.Move(klid, delta))) _rowFocusDelta = delta; // the focus follows the moved layout
            LanguageHotkeys.ApplyToWindows();
            ReloadLayoutRows();
        }

        private void MakeDefaultLayout(string klid)
        {
            if (!EnsureSystemBackups()) return;
            if (ReportLayoutEdit(InputLayouts.MakeDefault(klid, out string? writtenOverride)) && writtenOverride != null)
            {
                _config.InputMethodOverrideWritten = writtenOverride;
                _config.Save();
            }
            LanguageHotkeys.ApplyToWindows();
            ReloadLayoutRows();
        }

        /// <summary>Says why a layout edit wrote nothing; true when it went through.</summary>
        private bool ReportLayoutEdit(InputLayouts.EditResult result)
        {
            switch (result)
            {
                case InputLayouts.EditResult.Ok:
                    return true;
                case InputLayouts.EditResult.LastLayout:
                    Warn(Translate("Windows должна оставить хотя бы одну раскладку — эту удалить нельзя."));
                    return false;
                case InputLayouts.EditResult.ManagedByWindows:
                    Warn(Translate("Это метод ввода (IME), которым управляет Windows: удалённый здесь, он вернётся при следующем входе. Удалите его в параметрах Windows «Язык и регион»."));
                    return false;
                case InputLayouts.EditResult.CrossLanguage:
                    // The arrows are greyed for this; reaching it means the stores changed under the window.
                    return false;
                default:
                    Warn(Translate("Список раскладок Windows не удалось прочитать полностью — CyrFlip ничего не изменил."));
                    return false;
            }
        }

        private void OnAddLayout()
        {
            var installedKlids = new List<string>();
            foreach (InputLayouts.Installed i in InputLayouts.ListInstalled()) installedKlids.Add(i.Klid);

            using var picker = new LayoutPickerDialog(installedKlids, _config.UiLanguage);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKlid == null) return;

            if (!EnsureSystemBackups()) return;
            if (!ReportLayoutEdit(InputLayouts.Add(picker.SelectedKlid))) return;
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        private void AddPopularLayouts()
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputLayouts.Installed layout in InputLayouts.ListInstalled()) installed.Add(layout.Klid);
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputLayouts.Available layout in InputLayouts.ListAvailable()) available.Add(layout.Klid);
            int added = 0;
            if (!EnsureSystemBackups()) return;
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
            {
                string klid = language.Klids[0];
                if (installed.Contains(klid) || !available.Contains(klid)) continue;
                if (!ReportLayoutEdit(InputLayouts.Add(klid))) break;
                installed.Add(klid);
                added++;
            }
            LanguageHotkeys.ApplyToWindows(); Reload();
            if (added > 0) NotifyLayoutChangeOnce();
        }

        private void OnRemoveLayout(InputLayouts.Installed layout)
        {
            // Said before the question, not after it (ticket S0007, WL-9).
            if (layout.ManagedByWindows) { ReportLayoutEdit(InputLayouts.EditResult.ManagedByWindows); return; }
            if (ConfirmDialog.Show(this, _config.UiLanguage, string.Format(Translate("Удалить раскладку «{0}» из Windows?"), layout.LanguageName + " — " + layout.DisplayName), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            if (!EnsureSystemBackups()) return;
            if (!ReportLayoutEdit(InputLayouts.Remove(layout.Klid))) return;
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        private void OnToggleChanged()
        {
            if (_loading || !(_toggleCombo.SelectedItem is ToggleItem item)) return;
            if (!EnsureSystemBackups()) { ReloadToggleCombo(); return; }
            LanguageHotkeys.SetToggle(item.Code);
            LanguageHotkeys.ApplyToWindows();
        }

        private void ReloadToggleCombo()
        {
            // Restored, not cleared (S0036 UI-10): called from inside a _loading section, clearing it
            // made the rest of that section's assignments fire their handlers as user changes.
            bool loading = _loading;
            _loading = true;
            _toggleCombo.Items.Clear();
            string current = LanguageHotkeys.ToggleCode();
            int select = 0;
            foreach (string code in LanguageHotkeys.ToggleCodes)
            {
                int i = _toggleCombo.Items.Add(new ToggleItem { Code = code, Label = LanguageHotkeys.ToggleLabel(code) });
                if (code == current) select = i;
            }
            _toggleCombo.SelectedIndex = select;
            int max = 200;
            foreach (ToggleItem item in _toggleCombo.Items)
            {
                int w = TextRenderer.MeasureText(item.Label, Font).Width + 36;
                if (w > max) max = w;
            }
            _toggleCombo.Width = max;
            _toggleCombo.DropDownWidth = max;
            _loading = loading;
        }

        private sealed class ToggleItem
        {
            public string Code = "";
            public string Label = "";
            public override string ToString() => Label;
        }

        /// <summary>
        /// Capture the pre-CyrFlip state of both Windows stores this tab writes - the keyboard layouts
        /// and the language hotkeys with the switch chords - once, before the first write of either
        /// lands. Called at the top of <b>every</b> handler that writes Windows state (ticket S0007,
        /// WL-3; <c>SettingsBackupInventoryTests</c> holds every handler to it). False - and a warning -
        /// when a snapshot could not be taken: the handler then writes nothing, since a first edit
        /// without its backup could never be undone. A stored backup that did not read is still the
        /// user's one backup: it is kept on disk and never retaken over (ticket S0007, CF-4).
        /// </summary>
        private bool EnsureSystemBackups()
        {
            bool changed = false, ok = true;
            if (NeedsBackup(_config.InputLayoutsBackup, "InputLayoutsBackup"))
            {
                _config.InputLayoutsBackup = InputLayouts.BackupAll();
                changed |= _config.InputLayoutsBackup.Length > 0;
                ok &= _config.InputLayoutsBackup.Length > 0;
            }
            if (NeedsBackup(_config.LanguageHotkeysBackup, "LanguageHotkeysBackup"))
            {
                _config.LanguageHotkeysBackup = LanguageHotkeys.BackupAll();
                changed |= _config.LanguageHotkeysBackup.Length > 0;
                ok &= _config.LanguageHotkeysBackup.Length > 0;
            }
            if (changed) _config.Save();
            if (!ok) Warn(Translate("Не удалось сохранить исходное состояние настроек Windows — CyrFlip ничего не изменил."));
            return ok;
        }

        private bool NeedsBackup(string backup, string valueName)
            => backup.Length == 0 && !_config.IsUnreadable(valueName);

        private void NotifyLayoutChangeOnce()
        {
            if (_layoutNoticeShown) return;
            _layoutNoticeShown = true;
            ConfirmDialog.Show(this, _config.UiLanguage, Translate("Изменение применено к раскладкам Windows. Обычно оно вступает в силу сразу; если что-то выглядит не так, выйдите из Windows и войдите снова."),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestoreLayouts()
        {
            if (_config.InputLayoutsBackup.Length == 0) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Вернуть раскладки Windows в исходное состояние?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            if (!InputLayouts.RestoreAll(_config.InputLayoutsBackup, _config.InputMethodOverrideWritten))
            {
                Warn(Translate("Резервную копию не удалось прочитать — ничего не изменено."));
                return;
            }
            _config.InputMethodOverrideWritten = "";
            _config.Save();
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        /// <summary>Rebuilds the per-layout rows from the registry. Cheap enough to run on every refresh.</summary>
        private void ReloadLanguageRows()
        {
            RowFocus focus = RowFocus.Capture(_languageRows);
            _languageRows.SuspendLayout();

            var old = new Control[_languageRows.Controls.Count];
            _languageRows.Controls.CopyTo(old, 0);
            _languageRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();

            List<LanguageHotkeys.Entry> entries = LanguageHotkeys.ReadAll();
            var installed = new List<IntPtr>(LanguageHotkeys.InstalledLayouts());

            MeasureLanguageHotkeyColumn(entries);
            foreach (IntPtr hkl in installed)
                _languageRows.Controls.Add(LanguageHotkeyRow(hkl, entries.Find(e => LanguageHotkeys.HklEquals(e.TargetHkl, hkl))));

            // Assignments pointing at a layout that is no longer installed. Windows ships some of
            // these out of the box, so they are surfaced for removal rather than deleted for the user.
            foreach (LanguageHotkeys.Entry entry in entries)
                if (!installed.Exists(hkl => LanguageHotkeys.HklEquals(hkl, entry.TargetHkl)))
                    _languageRows.Controls.Add(OrphanHotkeyRow(entry));

            _languageRows.ResumeLayout();
            focus.Restore(_languageRows, TakeRowFocusDelta());
            _restoreLanguageHotkeys.Enabled = _config.LanguageHotkeysBackup.Length > 0;
        }

        private Control LanguageHotkeyRow(IntPtr hkl, LanguageHotkeys.Entry? entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            string layout = LanguageHotkeys.LayoutName(hkl);
            row.Controls.Add(ColumnLabel(LanguageHotkeys.LanguageName(hkl) + (layout.Length > 0 ? " — " + layout : ""), Column(290)));

            Label valueLabel = ColumnLabel(entry != null ? entry.Display : Translate("Не назначено"), _languageChordColumn, ellipsis: false);
            valueLabel.Font = entry != null ? BoldFont : Font;
            if (entry == null) valueLabel.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(valueLabel);
            row.Controls.Add(Button(Translate("Задать..."), () => AssignLanguageHotkey(hkl)));

            Button clear = Button(Translate("Очистить"), () => ClearLanguageHotkey(hkl));
            clear.Enabled = entry != null;
            row.Controls.Add(clear);

            // The raw HKL is the only always-correct way to tell two layouts of one language apart.
            row.Controls.Add(new Label { Text = LanguageHotkeys.HklText(hkl), AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(8, 5, 0, 0), Padding = new Padding(0, 1, 0, 0) });
            return row;
        }

        private Control OrphanHotkeyRow(LanguageHotkeys.Entry entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            Label orphanLabel = ColumnLabel(string.Format(Translate("{0} → раскладка {1} (не установлена)"), entry.Display, LanguageHotkeys.HklText(entry.TargetHkl)), Column(455));
            orphanLabel.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(orphanLabel);
            row.Controls.Add(Button(Translate("Удалить"), () => RemoveOrphanHotkey(entry.Id)));
            return row;
        }

        // The chord column of the language-hotkey rows, measured on every refresh like the conversion
        // table's (see MeasureConversionColumns): a fixed 165 px clipped "Ctrl+Shift+Alt+F12" at 125 %.
        private int _languageChordColumn;

        private void MeasureLanguageHotkeyColumn(List<LanguageHotkeys.Entry> entries)
        {
            _languageChordColumn = Math.Max(Column(165), TextWidth(Translate("Не назначено"), Font));
            foreach (LanguageHotkeys.Entry entry in entries)
                _languageChordColumn = Math.Max(_languageChordColumn, TextWidth(entry.Display, BoldFont));
        }

        private void ClearLanguageHotkey(IntPtr hkl)
        {
            if (!EnsureSystemBackups()) return;
            LanguageHotkeys.Clear(hkl);
            LanguageHotkeys.ApplyToWindows();
            ReloadLanguageRows();
        }

        private void RemoveOrphanHotkey(int id)
        {
            if (!EnsureSystemBackups()) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Translate("Удалить сохранённое сочетание для неустановленной раскладки?"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            LanguageHotkeys.Remove(id);
            LanguageHotkeys.ApplyToWindows();
            ReloadLanguageRows();
        }

        private void AssignLanguageHotkey(IntPtr hkl)
        {
            LanguageHotkeys.Entry? current = LanguageHotkeys.FindFor(hkl);
            using var dialog = new HotkeyDialog(current?.Display ?? "", Translate("Сочетание для переключения на язык"), _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedHotkey == null) return;

            // TryParse, never Parse: a chord it cannot read must not become Ctrl+Shift+F12 in
            // Windows' own hotkey store (ticket S0004, KC-2).
            if (!Hotkey.TryParse(dialog.CapturedHotkey, out Hotkey chord)) return;

            // CyrFlip's own hook swallows its chords, so Windows would never see one assigned here -
            // including a chord whose action is switched off right now: switching it on must not
            // silently take the chord away from Windows (KC-6).
            ChordOwner? owner = Chords().CyrFlipOwnerOf(chord, ChordKind.WindowsLanguage);
            string? taken = owner == null ? null : OwnerLabel(owner);
            if (taken != null)
            {
                Warn(string.Format(Translate("Комбинация {0} уже занята горячей клавишей CyrFlip «{1}» — Windows её не получит."), chord.Display, taken));
                return;
            }

            // Capture the pre-CyrFlip state once, before the first write of this feature ever lands.
            if (!EnsureSystemBackups()) return;

            LanguageHotkeys.AssignStatus status = LanguageHotkeys.Assign(hkl, chord, out string conflict);
            switch (status)
            {
                case LanguageHotkeys.AssignStatus.ChordTaken:
                    Warn(string.Format(Translate("Комбинация {0} уже назначена языку «{1}»."), chord.Display, conflict));
                    return;
                case LanguageHotkeys.AssignStatus.NoFreeSlot:
                    Warn(Translate("В Windows не осталось свободных слотов для языковых сочетаний."));
                    return;
                case LanguageHotkeys.AssignStatus.Failed:
                    Warn(Translate("Не удалось записать сочетание в реестр Windows."));
                    return;
            }

            LanguageHotkeys.ApplyToWindows();
            ReloadLanguageRows();

            if (!_languageHotkeyNoticeShown)
            {
                _languageHotkeyNoticeShown = true;
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("Сочетание записано в настройки Windows — обрабатывать его будет система, а не CyrFlip. Если оно не сработало сразу, выйдите из Windows и войдите снова."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RestoreLanguageHotkeys()
        {
            if (_config.LanguageHotkeysBackup.Length == 0) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Вернуть языковые сочетания Windows в исходное состояние?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            if (!LanguageHotkeys.RestoreAll(_config.LanguageHotkeysBackup))
            {
                Warn(Translate("Резервную копию не удалось прочитать — ничего не изменено."));
                return;
            }
            ReloadLanguageRows();
            ReloadToggleCombo();
        }

        /// <summary>
        /// Every chord and its owner, read fresh - the settings window edits the config, the launcher
        /// store and Windows' hotkey records, so a cached copy would go stale (ticket S0004, KC-6).
        /// </summary>
        private ChordRegistry Chords() => ChordRegistry.Build(_config, _launcherStore.All, LanguageHotkeys.ReadAll());

        private string OwnerLabel(ChordOwner owner) => ChordRegistry.Label(owner, _config.UiLanguage, LayoutName);

        /// <summary>
        /// May <paramref name="chord"/> go to this action? Refuses when another CyrFlip action owns
        /// it - switched on or not, and whatever the master switch says - and asks when a Windows
        /// language hotkey does.
        /// </summary>
        private bool ChordIsFree(Hotkey chord, ChordKind kind, string? id, bool askAboutWindows = true)
            => ChordGuard.IsFree(this, Chords(), chord, kind, id, _config.UiLanguage, LayoutName, askAboutWindows);

        private void Warn(string message)
            => ConfirmDialog.Show(this, _config.UiLanguage, message, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private static void Open(string target)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        private TabPage ClipboardPage()
        {
            var page = Page("Буфер обмена", "История хранится только локально и шифруется Windows DPAPI для вашей учётной записи.",
                Setting(_history, "CyrFlip сохраняет только Unicode-текст, скопированный после включения функции. Изображения, файлы и другие форматы не записываются."),
                Setting(_pause, "Временно прекращает захват новых копирований, не удаляя уже сохранённую историю. Полезно при работе с паролями и личными данными."),
                Setting(_historyStartup, "Запоминает, открыто ли окно менеджера буфера: если вы его закрыли, при следующем запуске оно останется закрытым (история всё равно ведётся в фоне)."));
            var panel = ContentPanel(page);
            var opacityLine = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 7, 0, 0) };
            opacityLine.Controls.Add(new Label { Text = "Прозрачность окна истории:", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
            opacityLine.Controls.Add(_opacity); opacityLine.Controls.Add(_opacityValue);
            panel.Controls.Add(Setting(opacityLine, "Задаёт прозрачность плавающего окна истории от 30% до 100%. Значение применяется сразу."));
            panel.Controls.Add(Setting(Button("Поиск по истории", _openHistorySearch), "Открывает отдельное окно поиска по фрагменту текста. Для поиска нужно ввести не менее трёх символов."));
            panel.Controls.Add(Setting(Button("Очистить всю историю", () =>
            {
                if (_getHistoryCount != null && _getHistoryCount() == 0)
                {
                    ConfirmDialog.Show(this, _config.UiLanguage, Translate("История пуста."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Удалить всю сохранённую историю буфера?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) == DialogResult.Yes)
                    _clearHistory();
            }), "Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить."));
            Control? transfer = ExchangeRow();
            if (transfer != null)
            {
                panel.Controls.Add(SectionHeader("Перенос на другой компьютер"));
                panel.Controls.Add(transfer);
            }
            return page;
        }

        /// <summary>
        /// Export / import of the open exchange file (ticket S0023) - the same pair on the clipboard page
        /// and the quick-notes page, since the file carries both. Not greyed by either module's switch:
        /// the export dialog and the import preview say themselves what a switched-off module means.
        /// </summary>
        private Control? ExchangeRow()
        {
            if (_exchange == null) return null;
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            row.Controls.Add(Button("Экспортировать...", () => _exchange(this, false)));
            row.Controls.Add(Button("Импортировать...", () => _exchange(this, true)));
            return Setting(row, "Один открытый текстовый файл с заметками и выбранной историей буфера: его можно прочитать в любом редакторе или на телефоне и импортировать в CyrFlip на другом компьютере. Файл не шифруется. Импорт показывает, что будет добавлено, и ничего не удаляет.");
        }

        // ---- Quick notes tab ----

        /// <summary>
        /// The quick-notes tab (spec §8.1): the opt-in switch, the chord, how the editor behaves,
        /// and the two actions that leave the encrypted journal - the export and the wipe. Like
        /// every other page here it builds no I/O of its own: the localization tests construct this
        /// window thirteen times, and a page that read a journal to draw itself would read it
        /// thirteen times too.
        /// </summary>
        private TabPage QuickNotesPage()
        {
            var page = Page("Быстрые заметки",
                "Маленький локальный блокнот: по хоткею открыть, бросить фрагмент кода или мысль, закрыть. Текст хранится как есть — без разметки, подсветки и автоформатирования, — поэтому его можно вставить обратно в код без единого изменения.",
                Setting(_quickNotesEnabled, "Пока выключено, CyrFlip не читает и не создаёт файл заметок, в трее нет пункта заметок и комбинация не назначена. Уже сохранённые заметки остаются на диске."));
            var panel = ContentPanel(page);

            panel.Controls.Add(SectionHeader("Как открывать"));
            panel.Controls.Add(Track(HotkeyRow(_quickNotesHotkeyEnabled, _quickNotesHotkeyValue, _setQuickNotesHotkey,
                "Сразу создаёт новую заметку и ставит курсор в поле текста — записать мысль можно, ничего больше не нажимая. Одну комбинацию нельзя отдать двум действиям CyrFlip.")));
            panel.Controls.Add(Track(Setting(Button("Открыть заметки", _openQuickNotes),
                "То же, что пункт «Быстрые заметки» в меню трея: открывает список без создания новой заметки.")));

            panel.Controls.Add(SectionHeader("Редактор"));
            panel.Controls.Add(Track(Setting(_quickNotesWrap,
                "По умолчанию выключено: тело заметки чаще код, чем проза, а перенесённую строку кода приходится мысленно собирать обратно. Включите, если пишете здесь текст.")));

            panel.Controls.Add(SectionHeader("Экспорт и хранение"));
            panel.Controls.Add(Track(Setting(_quickNotesExportMeta,
                "По умолчанию в экспорт идут только имя и текст: так фрагмент кода вставляется обратно без правок. С флажком к каждой заметке добавляется дата создания.")));
            panel.Controls.Add(Track(Setting(Button("Экспортировать все заметки в Markdown...", ExportQuickNotes),
                "Один файл со всеми заметками по порядку. Экспортированный файл уже не защищён DPAPI — его прочитает любой, у кого есть доступ к папке.")));
            panel.Controls.Add(Track(Setting(Button("Удалить все быстрые заметки", ClearQuickNotes),
                "Удаляет все заметки, журнал и его резервную копию. Отменить это нельзя.")));
            Control? transfer = ExchangeRow();
            if (transfer != null)
            {
                panel.Controls.Add(SectionHeader("Перенос на другой компьютер"));
                panel.Controls.Add(transfer);
            }
            panel.Controls.Add(new Label
            {
                Text = "Заметки лежат только на этом компьютере и шифруются Windows DPAPI для вашей учётной записи. CyrFlip не отправляет их в сеть, не индексирует их поиском Windows и не превращает записи истории буфера в заметки — для этого есть отдельная команда. Это не хранилище секретов: не сохраняйте здесь пароли, боевые токены и приватные ключи.",
                AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                Margin = new Padding(3, 10, 3, 4),
            });
            return page;
        }

        /// <summary>Remember a control so the master switch can grey the whole group (see Reload).</summary>
        private Control Track(Control control) { _quickNotesControls.Add(control); return control; }

        private void ExportQuickNotes()
        {
            using var dialog = new SaveFileDialog
            {
                Title = Translate("Экспорт всех заметок"),
                Filter = "Markdown (*.md)|*.md",
                FileName = "cyrflip-notes.md",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string path = _exportQuickNotes(dialog.FileName, _quickNotesExportMeta.Checked);
                if (path.Length == 0) return;
                ConfirmDialog.Show(this, _config.UiLanguage,
                    string.Format(Translate("Заметки сохранены: {0}"), path) + "\n\n"
                    + Translate("Этот файл не защищён DPAPI — его прочитает любой, у кого есть доступ к папке."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn(string.Format(Translate("Не удалось сохранить файл: {0}"), FailureCause.Describe(ex, _config.UiLanguage)));
            }
        }

        private void ClearQuickNotes()
        {
            if (_getQuickNotesCount != null && _getQuickNotesCount() == 0)
            {
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("Заметок нет."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Translate("Удалить все быстрые заметки, журнал и резервную копию? Отменить это нельзя."),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            _clearQuickNotes();
        }

        /// <summary>Apply one quick-notes setting and tell the context; mirrors <see cref="TranslateChanged"/>.</summary>
        private void QuickNotesSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            QuickNotesChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        /// <summary>Mirror the quick-notes settings into the tab and gate the group behind its switch.</summary>
        private void ReloadQuickNotesState()
        {
            _quickNotesEnabled.Checked = _config.EnableQuickNotes;
            _quickNotesHotkeyEnabled.Checked = _config.EnableQuickNotesHotkey;
            _quickNotesWrap.Checked = _config.QuickNotesWordWrap;
            _quickNotesHotkeyValue.Text = _config.QuickNotesHotkey;
            foreach (Control control in _quickNotesControls) control.Enabled = _config.EnableQuickNotes;
        }

        // ---- Graphics tab (S0026) ----

        /// <summary>
        /// The Graphics tab: for now one tool - the screen region capture with its chord, a button to
        /// try it, and the opt-in "also save to a folder". No module switch: the tray item is always
        /// there and the chord has its own switch (owner, 2026-09-26). Like every page here it does no
        /// I/O to draw itself: the folder is shown, never created.
        /// </summary>
        private TabPage GraphicsPage()
        {
            var page = Page("Графика",
                "Инструменты, которые работают с тем, что сейчас на экране. Всё остаётся на этом компьютере.");
            var panel = ContentPanel(page);

            panel.Controls.Add(SectionHeader("Снимок области экрана"));
            panel.Controls.Add(HotkeyRow(_screenshotHotkeyEnabled, _screenshotHotkeyValue,
                () => ScreenshotHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                "Экран замирает, вы выделяете прямоугольник мышью, и картинка сразу в буфере обмена. Esc или правая кнопка - отмена. Одну комбинацию нельзя отдать двум действиям CyrFlip."));
            panel.Controls.Add(Setting(Button("Сделать снимок области", () => ScreenshotRequested?.Invoke(this, EventArgs.Empty)),
                "То же, что пункт в меню трея. Всплывающую подсказку или открытое меню поймает только комбинация: щелчок по трею их закрывает."));
            panel.Controls.Add(Setting(_screenshotSave,
                "По умолчанию выключено: снимок попадает только в буфер обмена. С флажком каждый выделенный фрагмент также сохраняется как PNG в папку ниже."));

            var folderRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(28, 0, 3, 3) };
            folderRow.Controls.Add(_screenshotFolderBox);
            Button choose = Button("Выбрать...", ChooseScreenshotFolder);
            Button open = Button("Открыть папку", OpenScreenshotFolder);
            folderRow.Controls.Add(choose);
            folderRow.Controls.Add(open);
            _screenshotFolderControls.Add(_screenshotFolderBox);
            _screenshotFolderControls.Add(choose);
            _screenshotFolderControls.Add(open);
            panel.Controls.Add(folderRow);

            panel.Controls.Add(new Label
            {
                Text = "Снимок всегда попадает в буфер обмена и не записывается в историю буфера. На диск он пишется, только если включено сохранение, и только выделенный фрагмент - в показанную папку (а если она недоступна - в «Изображения\\Снимки экрана» или «Загрузки», и CyrFlip сразу скажет, куда). Файлы не шифруются, CyrFlip их не удаляет и никуда не отправляет. Содержимое, которое защищает Windows (видео с DRM, окно UAC), получается чёрным.",
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                Margin = new Padding(3, 10, 3, 4),
            });
            return page;
        }

        /// <summary>The folder the settings show: the user's choice, else the Screenshots known folder.</summary>
        private string ScreenshotFolderShown()
        {
            try { return new ScreenshotSaver().DisplayFolder(_config.ScreenshotFolder) ?? ""; }
            catch { return _config.ScreenshotFolder; }
        }

        private void ChooseScreenshotFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Translate("Папка для снимков области экрана"),
                SelectedPath = ScreenshotFolderShown(),
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
            // The default folder chosen explicitly is stored as "the default", so it keeps following a
            // Pictures folder the user later moves to OneDrive (CAPTURE-OUTPUT rule 9).
            string? standard = new ScreenshotSaver().DisplayFolder("");
            string chosen = dialog.SelectedPath;
            GraphicsSettingChanged(() => _config.ScreenshotFolder =
                standard != null && string.Equals(chosen.TrimEnd('\\'), standard.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    ? "" : chosen, reload: true);
        }

        private void OpenScreenshotFolder()
        {
            string folder = ScreenshotFolderShown();
            // The folder is created on the first save, never earlier - so there may be nothing to open yet.
            if (folder.Length == 0 || !System.IO.Directory.Exists(folder))
            {
                ConfirmDialog.Show(this, _config.UiLanguage,
                    Translate("Этой папки пока нет - она появится при первом сохранённом снимке."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Open(folder);
        }

        /// <summary>Apply one Graphics setting and tell the context; mirrors <see cref="QuickNotesSettingChanged"/>.</summary>
        private void GraphicsSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            GraphicsChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        /// <summary>Mirror the Graphics settings into the tab and gate the group behind its switches.</summary>
        private void ReloadGraphicsState()
        {
            _screenshotHotkeyEnabled.Checked = _config.EnableScreenshotHotkey;
            _screenshotHotkeyValue.Text = _config.ScreenshotHotkey;
            _screenshotSave.Checked = _config.ScreenshotSaveEnabled;
            _screenshotFolderBox.Text = ScreenshotFolderShown();
            foreach (Control control in _screenshotFolderControls)
                control.Enabled = _config.ScreenshotSaveEnabled;
        }

        // ---- Translator tab (local Ollama) ----

        /// <summary>
        /// The translator tab: the opt-in switch, the Ollama helpers (install / start / check / pull),
        /// the table of "translate into this language on this chord" rows, and what happens to the
        /// result. Nothing here touches the network until the user presses one of the buttons - the
        /// page is built 13 times by the localization tests and must stay free of I/O.
        /// </summary>
        private TabPage TranslatePage()
        {
            var page = Page("Перевод", "Перевод выделенного текста локальной моделью Ollama: текст никуда не отправляется, кроме сервера, указанного ниже (по умолчанию это ваш же компьютер). Сам Ollama и языковую модель нужно установить один раз — кнопками ниже.",
                Setting(_translateEnabled, "Пока выключено, комбинации перевода не назначены, в трее нет пункта перевода и CyrFlip не открывает ни одного сетевого соединения."));
            var panel = ContentPanel(page);

            panel.Controls.Add(SectionHeader("Ollama"));
            panel.Controls.Add(Setting(LabeledRow("Адрес сервера:", _translateEndpoint),
                "Пусто — http://localhost:11434, то есть Ollama на этом компьютере. Если указать другой адрес, выделенный текст будет отправлен на ту машину."));

            var modelRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            modelRow.Controls.Add(new Label { Text = "Модель:", AutoSize = true, Padding = new Padding(0, 8, 6, 0) });
            modelRow.Controls.Add(_translateModel);
            modelRow.Controls.Add(TranslateButton("Загрузить модель", PullTranslateModel));
            panel.Controls.Add(Setting(modelRow,
                "Рекомендуемые: aya-expanse:8b (~4,7 ГБ, по умолчанию - лучший перевод из проверенных), gemma2:9b (~5 ГБ). Модели меньше 4 ГБ проверку на русском и украинском не прошли: gemma2:2b (~1,5 ГБ) берите, только если места мало, и ждите ошибок. Модель занимает память процесса Ollama, а не CyrFlip."));

            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            actions.Controls.Add(TranslateButton("Установить Ollama", InstallOllama));
            actions.Controls.Add(TranslateButton("Запустить Ollama", StartOllama));
            actions.Controls.Add(TranslateButton("Проверить связь", CheckOllama));
            // Live only while a download, a model pull or a start runs (S0037 TR-1): a stalled
            // download used to leave every button here disabled until CyrFlip was restarted.
            _translateCancel = Button("Отмена", CancelTranslateWork);
            _translateCancel.Enabled = false;
            actions.Controls.Add(_translateCancel);
            panel.Controls.Add(actions);
            panel.Controls.Add(_translateStatus);

            panel.Controls.Add(Setting(_translateAutoStart,
                "Если сервер не отвечает в момент перевода, CyrFlip один раз попробует запустить его сам и подождёт до восьми секунд."));
            panel.Controls.Add(Setting(LabeledRow("Держать модель в памяти (мин):", _translateKeepAlive),
                "Сколько Ollama держит модель загруженной после перевода. -1 держит её всегда, и это значение по умолчанию: первая загрузка на машине без подходящей видеокарты занимает минуты, и платить за неё заново каждые несколько минут хуже, чем занятые гигабайты. 0 выгружает сразу."));
            panel.Controls.Add(Setting(LabeledRow("Ждать загрузки модели не дольше (с):", _translateTimeout),
                "Это время на первый ответ модели, то есть почти целиком на её загрузку в память. Дальше перевод идёт, пока модель пишет: он прерывается, только если она замолчит на 30 секунд, и тогда уже написанное остаётся на экране."));

            panel.Controls.Add(SectionHeader("Направления перевода"));
            panel.Controls.Add(_translationRows);
            panel.Controls.Add(TranslateButton("Добавить направление...", AddTranslationProfile));
            panel.Controls.Add(new Label
            {
                Text = "«Язык интерфейса» и «Язык активной раскладки» вычисляются в момент нажатия. Одну комбинацию нельзя отдать двум действиям CyrFlip.",
                AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 8, 3, 4),
            });

            panel.Controls.Add(SectionHeader("Результат"));
            panel.Controls.Add(Setting(_translateCopy,
                "Перевод попадает в буфер обмена обычной записью — и, если история включена, сохраняется в ней, как любое другое копирование."));
            panel.Controls.Add(Setting(_translatePaste,
                "Сразу заменяет выделение переводом. Если к моменту ответа фокус ушёл в другое окно, вставки не будет — окно предложит вставить вручную."));
            panel.Controls.Add(Setting(_translateShowSource,
                "Показывает исходный текст в верхней части окна результата."));
            panel.Controls.Add(Setting(LabeledRow("Закрывать окно через (с):", _translateWindowTimeout),
                "Отсчёт идёт с момента, когда перевод получен, и замирает, пока указатель мыши над окном. 0 — не закрывать по таймеру: окно всё равно закроется по Esc, по кнопке закрытия или когда вы перейдёте в другое окно."));
            return page;
        }

        /// <summary>A label and its control on one line; the label auto-sizes so no language clips it.</summary>
        private static Control LabeledRow(string label, Control control)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 6, 0) });
            row.Controls.Add(control);
            return row;
        }

        /// <summary>A button that belongs to the translator group: gated by the switch and by the busy flag.</summary>
        private Button TranslateButton(string russianText, Action action)
        {
            Button button = Button(russianText, action);
            _translateButtons.Add(button);
            return button;
        }

        private void SetTranslateBusy(bool value)
        {
            _translateBusy = value;
            foreach (Button button in _translateButtons) button.Enabled = !value && _translateEnabled.Checked;
            if (_translateCancel != null) _translateCancel.Enabled = value;
            if (value) return;
            _translateWork?.Dispose();
            _translateWork = null;
        }

        /// <summary>The token for the long Ollama operations; cancelled when the window hides or closes.</summary>
        private CancellationToken TranslateWorkToken()
        {
            _translateWork?.Cancel();
            _translateWork?.Dispose();
            _translateWork = new CancellationTokenSource();
            return _translateWork.Token;
        }

        private void CancelTranslateWork()
        {
            try { _translateWork?.Cancel(); }
            catch { /* already disposed - nothing to stop */ }
        }

        private void NotifyTranslationChanged() => TranslationChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Apply one translator setting, tell the context, and optionally re-derive the gating.</summary>
        private void TranslateChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            NotifyTranslationChanged();
            if (reload) Reload();
        }

        /// <summary>Commit the two free-text translator fields; called on Leave and when the window hides.</summary>
        private void CommitTranslateText()
        {
            if (_loading) return;
            string endpoint = _translateEndpoint.Text.Trim();
            string model = _translateModel.Text.Trim();
            if (endpoint == _config.TranslateEndpoint && model == _config.TranslateModel) return;
            _config.TranslateEndpoint = endpoint;
            _config.TranslateModel = model;
            NotifyTranslationChanged();
        }

        // Column widths for the translation table, measured like the conversion table's.
        private int _targetColumn, _translateChordColumn;

        private void MeasureTranslationColumns()
        {
            using var bold = new Font(Font, FontStyle.Bold);
            _targetColumn = Math.Max(Column(300), TextWidth(Translate("На язык"), bold));
            _translateChordColumn = Math.Max(Column(145), TextWidth(Translate("Комбинация"), bold));
            foreach (TranslationProfile profile in _config.TranslateProfiles)
            {
                _translateChordColumn = Math.Max(_translateChordColumn, TextWidth(profile.Hotkey, Font));
                _targetColumn = Math.Max(_targetColumn,
                    TextWidth(TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage), Font));
            }
            _targetColumn = Math.Min(_targetColumn, Column(460));
        }

        private void ReloadTranslationRows()
        {
            RowFocus focus = RowFocus.Capture(_translationRows);
            _translationRows.SuspendLayout();
            var old = new Control[_translationRows.Controls.Count];
            _translationRows.Controls.CopyTo(old, 0);
            _translationRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();

            MeasureTranslationColumns();
            _translationRows.Controls.Add(TranslationHeaderRow());
            foreach (TranslationProfile profile in _config.TranslateProfiles)
                _translationRows.Controls.Add(TranslationRow(profile));
            if (_config.TranslateProfiles.Count == 0)
                _translationRows.Controls.Add(new Label
                {
                    Text = Translate("Направлений пока нет — добавьте первое, чтобы назначить комбинацию."),
                    AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(25, 6, 3, 4),
                });
            // Gated here, not in ReloadTranslateState: ApplyLanguage rebuilds these rows after the
            // gating pass, so a switch set there would be lost on every language change.
            _translationRows.Enabled = _config.EnableTranslate;
            _translationRows.ResumeLayout();
            focus.Restore(_translationRows, TakeRowFocusDelta());
        }

        private Control TranslationHeaderRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row.Controls.Add(new Label { Text = "", AutoSize = false, Width = 22, Height = 18 });
            Label target = ColumnLabel(Translate("На язык"), _targetColumn);
            Label chord = ColumnLabel(Translate("Комбинация"), _translateChordColumn, ellipsis: false);
            target.Font = chord.Font = BoldFont;
            row.Controls.Add(target);
            row.Controls.Add(chord);
            return row;
        }

        private Control TranslationRow(TranslationProfile profile)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            var enabled = new RowCheckBox { Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 6, 2, 2) };
            enabled.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                // Switching a row back on can collide with a chord that was assigned while this row
                // slept - the editing paths check, so the enabling path has to check too.
                if (enabled.Checked && profile.IsUsable && Hotkey.TryParse(profile.Hotkey, out Hotkey chord))
                {
                    if (!ChordIsFree(chord, ChordKind.Translation, profile.Id, askAboutWindows: false))
                    {
                        _loading = true;
                        enabled.Checked = false;
                        _loading = false;
                        return;
                    }
                }
                profile.Enabled = enabled.Checked;
                NotifyTranslationChanged();
            };
            row.Controls.Add(enabled);
            Label target = ColumnLabel(TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage), _targetColumn);
            if (!_config.EnableHotkeys || !_config.EnableTranslate) target.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(target);
            row.Controls.Add(ColumnLabel(profile.Hotkey.Length > 0 ? profile.Hotkey : Translate("Не назначено"),
                _translateChordColumn, ellipsis: false));
            row.Controls.Add(Button(Translate("Изменить..."), () => EditTranslationProfile(profile)));
            row.Controls.Add(Button(Translate("Удалить"), () =>
            {
                _config.TranslateProfiles.Remove(profile);
                NotifyTranslationChanged();
                ReloadTranslationRows();
            }));
            return row;
        }

        private void AddTranslationProfile() => EditTranslationProfile(null);

        private void EditTranslationProfile(TranslationProfile? existing)
        {
            // The model comes from the field, not from the saved config: the user may have just typed a
            // different one, and "which languages does it know" has to point at the model they mean now.
            using var dialog = new TranslationDialog(existing, _config.UiLanguage, _translateModel.Text.Trim());
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile == null) return;
            TranslationProfile profile = dialog.Profile;
            // Only a real chord can clash - and Hotkey.Parse("") would answer Ctrl+Shift+F12, which
            // would report the seeded EN ⇄ RU row as a conflict with a row that has no chord at all.
            if (profile.Hotkey.Length > 0)
            {
                if (!Hotkey.TryParse(profile.Hotkey, out Hotkey chord)
                    || !ChordIsFree(chord, ChordKind.Translation, existing?.Id ?? profile.Id)) return;
            }
            if (existing == null) _config.TranslateProfiles.Add(profile);
            else
            {
                int index = _config.TranslateProfiles.IndexOf(existing);
                if (index >= 0) _config.TranslateProfiles[index] = profile;
            }
            NotifyTranslationChanged();
            ReloadTranslationRows();
        }

        private async void InstallOllama()
        {
            if (_translateBusy) return;
            if (OllamaManager.IsInstalled())
            {
                _translateStatus.Text = Translate("Ollama уже установлен — нажмите «Запустить Ollama».");
                return;
            }
            if (!OllamaManager.CanInstallInPlace)
            {
                // MSIX: a Store app must not download and run a third-party installer (spec §5.4).
                _translateStatus.Text = Translate("Открываю сайт Ollama — установите его оттуда.");
                OllamaManager.OpenWebPage();
                return;
            }
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Скачать и установить Ollama? Это несколько сотен мегабайт, загрузка может занять время."),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetTranslateBusy(true);
            _translateStatus.Text = Translate("Скачиваю Ollama...");
            var download = new OllamaManager.InstallerDownload();
            try
            {
                var progress = new Progress<string>(text => _translateStatus.Text = Translate("Скачиваю Ollama:") + " " + text);
                download = await OllamaManager.DownloadInstallerAsync(progress, TranslateWorkToken());
            }
            catch (OperationCanceledException)
            {
                // The user closed the window or pressed "Cancel": that is not a failure, and it must
                // not go on to open a browser at ollama.com behind their back.
                SetTranslateBusy(false);
                if (!IsDisposed) _translateStatus.Text = Translate("Загрузка остановлена.");
                return;
            }
            catch { /* the network gave up, or went silent (TR-1) - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (download.Status == OllamaManager.InstallerStatus.Ok)
            {
                _translateStatus.Text = Translate("Запускаю установщик Ollama...");
                OllamaManager.RunInstaller(download);
            }
            else if (download.Status == OllamaManager.InstallerStatus.Untrusted)
            {
                // Deleted, never run (S0010 TD-5); the site is the one source left that the user can judge.
                _translateStatus.Text = Translate("Скачанный установщик не подписан Ollama - он удалён. Открываю сайт Ollama...");
                OllamaManager.OpenWebPage();
            }
            else
            {
                _translateStatus.Text = Translate("Не удалось скачать. Открываю сайт Ollama...");
                OllamaManager.OpenWebPage();
            }
        }

        private async void StartOllama()
        {
            if (_translateBusy) return;
            if (!OllamaManager.IsInstalled())
            {
                _translateStatus.Text = Translate("Ollama не установлен — нажмите «Установить Ollama».");
                return;
            }

            SetTranslateBusy(true);
            _translateStatus.Text = Translate("Запускаю Ollama...");
            OllamaManager.StartServer();
            var client = new OllamaClient(_config.TranslateEndpoint);
            bool up = false;
            try
            {
                CancellationToken ct = TranslateWorkToken();
                for (int i = 0; i < 16 && !up; i++)
                {
                    await Task.Delay(500, ct);
                    up = await client.ProbeAsync(ct);
                }
            }
            catch (OperationCanceledException) { SetTranslateBusy(false); return; } // window closed
            catch { /* nothing to add - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (up) await ShowServerStateAsync(client);
            else _translateStatus.Text = Translate("Не удалось запустить Ollama.");
        }

        private async void CheckOllama()
        {
            if (_translateBusy) return;
            SetTranslateBusy(true);
            var client = new OllamaClient(_config.TranslateEndpoint);
            bool up = false;
            try { up = await client.ProbeAsync(TranslateWorkToken()); }
            catch (OperationCanceledException) { SetTranslateBusy(false); return; } // window closed
            catch { /* nothing to add - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (up) await ShowServerStateAsync(client);
            else
                _translateStatus.Text = OllamaManager.IsInstalled()
                    ? Translate("Ollama не запущен — нажмите «Запустить Ollama».")
                    : Translate("Ollama не найден — нажмите «Установить Ollama».");
        }

        private async void PullTranslateModel()
        {
            if (_translateBusy) return;
            string model = _translateModel.Text.Trim();
            if (model.Length == 0)
            {
                _translateStatus.Text = Translate("Укажите имя модели, например aya-expanse:8b.");
                return;
            }

            SetTranslateBusy(true);
            var client = new OllamaClient(_config.TranslateEndpoint);
            bool ok = false;
            try
            {
                var progress = new Progress<string>(text => _translateStatus.Text = Translate("Загружаю модель:") + " " + text);
                // No timeout on purpose - a multi-gigabyte pull legitimately takes minutes - but a
                // token, so closing the window stops it instead of leaving the tab busy forever.
                ok = await client.PullModelAsync(model, progress, TranslateWorkToken());
            }
            catch (OperationCanceledException) { SetTranslateBusy(false); return; } // window closed
            catch { /* the pull failed - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (ok)
            {
                _translateStatus.Text = Translate("Модель установлена:") + " " + model;
                await FillModelsAsync(client);
            }
            else
            {
                _translateStatus.Text = Translate("Не удалось загрузить модель. Ollama запущен?");
            }
        }

        /// <summary>The one status line, after a successful probe: what is running and what it has.</summary>
        private async Task ShowServerStateAsync(OllamaClient client)
        {
            List<string> installed = await FillModelsAsync(client);
            _translateStatus.Text = installed.Count > 0
                ? Translate("Ollama работает. Установленные модели:") + " " + string.Join(", ", installed.ToArray())
                : Translate("Ollama работает, но ни одна модель не установлена — нажмите «Загрузить модель».");
        }

        /// <summary>Installed models first, then the recommended downloads; the typed name is kept.</summary>
        private async Task<List<string>> FillModelsAsync(OllamaClient client)
        {
            List<string> installed;
            try { installed = await client.ListModelsAsync(CancellationToken.None) ?? new List<string>(); }
            catch { installed = new List<string>(); }
            if (_translateModel.IsDisposed) return installed;

            var merged = new List<string>(installed);
            foreach (string recommended in TranslationService.RecommendedModels)
                if (!merged.Contains(recommended)) merged.Add(recommended);

            bool loading = _loading;
            _loading = true;
            string current = _translateModel.Text;
            _translateModel.BeginUpdate();
            _translateModel.Items.Clear();
            foreach (string model in merged) _translateModel.Items.Add(model);
            _translateModel.EndUpdate();
            _translateModel.Text = current;
            _loading = loading;
            return installed;
        }

        /// <summary>
        /// The scenario launcher tab (absorbed OneClickRunner): the opt-in switch, the searchable
        /// scenario table and the full CRUD surface. The UI talks only to the store and the shared
        /// execution service - never to XML files or Process.Start directly (tech plan Фаза 4.2).
        /// </summary>
        private TabPage LauncherPage()
        {
            var page = Page("Быстрый запуск", "Ваши программы, скрипты и загрузки yt-dlp: запуск из меню в трее, из этой таблицы, по глобальной комбинации и из Jump List значка CyrFlip на панели задач. Пока выключатель снят, CyrFlip ведёт себя как раньше — ни пункта в трее, ни задач в Jump List.");
            var panel = ContentPanel(page);

            // The absorbed feature keeps the mark it had as a separate program - the same icon the
            // taskbar button shows while the launcher is on. Page() puts the description first, so
            // the header has to be moved above it.
            Control header = LauncherHeader();
            panel.Controls.Add(header);
            panel.Controls.SetChildIndex(header, 0);

            panel.Controls.Add(Setting(_launcherEnabled, "Добавляет подменю сценариев в меню трея и задачи в Jump List панели задач (правый клик по значку CyrFlip на панели задач). Сценарии хранятся по одному XML-файлу и не удаляются при выключении."));
            panel.Controls.Add(new Label { Text = "Значок на панели задач: левый клик — список сценариев, правый — Jump List Windows.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(31, 0, 3, 6) });

            var searchRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            searchRow.Controls.Add(new Label { Text = "Поиск:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            searchRow.Controls.Add(_launcherSearch);
            panel.Controls.Add(searchRow);

            panel.Controls.Add(_launcherLoadErrors);
            _launcherList.SmallImageList = _launcherImages;
            panel.Controls.Add(_launcherList);

            var row1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row1.Controls.Add(LauncherButton("Добавить...", LauncherAdd));
            row1.Controls.Add(LauncherButton("Изменить...", LauncherEdit));
            row1.Controls.Add(LauncherButton("Запустить", LauncherRun));
            row1.Controls.Add(LauncherButton("Клонировать", LauncherClone));
            row1.Controls.Add(LauncherButton("Удалить", LauncherRemove));
            var up = LauncherButton("↑", () => LauncherMove(-1));
            up.AccessibleName = Translate("Вверх");
            var down = LauncherButton("↓", () => LauncherMove(+1));
            down.AccessibleName = Translate("Вниз");
            // Sized by their content, never below 32 px wide: built here, before the window takes its real font,
            // a fixed RowHeight was the old small font's and cut the lower half of "↓" (S0019 audit A-16).
            // GrowOnly (a button's default) grows from the current size, so it starts from 32 x 0, not 75 x 23.
            up.AutoSize = down.AutoSize = true; up.MinimumSize = down.MinimumSize = up.Size = down.Size = new Size(32, 0);
            row1.Controls.Add(up); row1.Controls.Add(down);
            panel.Controls.Add(row1);

            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 0, 3, 2) };
            row2.Controls.Add(LauncherButton("Экспорт...", LauncherExport));
            row2.Controls.Add(LauncherButton("Импорт...", LauncherImport));
            _launcherImportOcr = LauncherButton("Импорт из OneClickRunner...", LauncherImportFromOcr);
            row2.Controls.Add(_launcherImportOcr);
            panel.Controls.Add(row2);

            panel.Controls.Add(new Label { Text = "Двойной клик или Enter — запуск, F2 — изменение, Delete — удаление. Импорт из OneClickRunner копирует сценарии и никогда не изменяет исходные файлы.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 4) });

            // Context menu, keyboard shortcuts and the run-on-double-click of the source app.
            var context = new ContextMenuStrip();
            context.Opening += (_, e) =>
            {
                context.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
                context.Items.Clear();
                if (SelectedLauncherScenario() == null) { e.Cancel = true; return; }
                context.Items.Add(new ToolStripMenuItem(Translate("Запустить"), null, (_, _) => LauncherRun()));
                context.Items.Add(new ToolStripMenuItem(Translate("Изменить..."), null, (_, _) => LauncherEdit()));
                context.Items.Add(new ToolStripMenuItem(Translate("Клонировать"), null, (_, _) => LauncherClone()));
                context.Items.Add(new ToolStripMenuItem(Translate("Экспорт..."), null, (_, _) => LauncherExport()));
                context.Items.Add(new ToolStripSeparator());
                context.Items.Add(new ToolStripMenuItem(Translate("Удалить"), null, (_, _) => LauncherRemove()));
            };
            _launcherList.ContextMenuStrip = context;
            _launcherList.MouseDoubleClick += (_, e) => { if (_launcherList.HitTest(e.Location).Item != null) LauncherRun(); };
            _launcherList.KeyDown += (_, e) =>
            {
                if (SelectedLauncherScenario() == null) return;
                if (e.KeyCode == Keys.Enter) { LauncherRun(); e.Handled = true; }
                else if (e.KeyCode == Keys.F2) { LauncherEdit(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { LauncherRemove(); e.Handled = true; }
            };
            _launcherEnabled.CheckedChanged += (_, _) => Changed(_setLauncherEnabled, _launcherEnabled.Checked);
            _launcherSearch.TextChanged += (_, _) => { if (!_loading) ReloadLauncherRows(); };
            // The table follows the window: a FlowLayoutPanel anchors nothing, so it is sized by hand
            // from whatever height the rest of the page leaves over.
            panel.ClientSizeChanged += (_, _) => LayoutLauncherList(panel);
            _launcherPanel = panel;
            return page;
        }

        /// <summary>
        /// The launcher page's header: the icon the feature carried as a separate program next to its
        /// name. No pixel geometry - the row auto-sizes, and the caption is an ordinary translatable
        /// label whose bold font <see cref="RefreshBoldFonts"/> re-derives when the language brings a
        /// different font family. A missing icon resource simply leaves the caption alone.
        /// </summary>
        private Control LauncherHeader()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            Bitmap? mark = LauncherBrand.GetImage(28);
            if (mark != null)
                row.Controls.Add(new PictureBox { Image = mark, SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(2, 0, 10, 0) });
            row.Controls.Add(new Label
            {
                Text = "Быстрый запуск", AutoSize = true,
                Font = BoldFont,
                Margin = new Padding(0, 6, 3, 0),
            });
            return row;
        }

        /// <summary>
        /// Gives the scenario table every pixel the rest of the tab does not use. Re-entrancy is real
        /// here: resizing the list re-runs the flow layout, which can show or hide the panel's scroll
        /// bar and fire <c>ClientSizeChanged</c> again.
        /// </summary>
        private void LayoutLauncherList(FlowLayoutPanel panel)
        {
            if (_layingOutLauncher || panel.ClientSize.Width <= 0 || panel.ClientSize.Height <= 0) return;
            _layingOutLauncher = true;
            try
            {
                int used = panel.Padding.Vertical;
                foreach (Control control in panel.Controls)
                    if (!ReferenceEquals(control, _launcherList) && control.Visible)
                        used += control.Height + control.Margin.Vertical;

                _launcherList.Width = Math.Max(520, panel.ClientSize.Width - panel.Padding.Horizontal - _launcherList.Margin.Horizontal);
                _launcherList.Height = Math.Max(180, panel.ClientSize.Height - used - _launcherList.Margin.Vertical);
                StretchLauncherColumns();
            }
            finally { _layingOutLauncher = false; }
        }

        /// <summary>The path column absorbs the spare width, so a wider window shows longer paths
        /// instead of an empty stripe on the right.</summary>
        private void StretchLauncherColumns()
        {
            if (_launcherList.Columns.Count < 6) return;
            int others = 0;
            for (int i = 0; i < _launcherList.Columns.Count; i++)
                if (i != 1) others += _launcherList.Columns[i].Width;
            // Leave room for the vertical scroll bar unconditionally: it appears as soon as the list
            // fills up, and claiming its width back is what puts a horizontal scroll bar under a table
            // that already fits.
            int free = _launcherList.ClientSize.Width - others - SystemInformation.VerticalScrollBarWidth - 4;
            _launcherList.Columns[1].Width = Math.Max(Column(250), free);
        }

        private Button LauncherButton(string russianText, Action action)
        {
            Button button = Button(russianText, action);
            _launcherButtons.Add(button);
            return button;
        }

        private LauncherScenario? SelectedLauncherScenario()
            => _launcherList.SelectedItems.Count > 0 ? _launcherList.SelectedItems[0].Tag as LauncherScenario : null;

        /// <summary>
        /// Rebuild the scenario table: translated column headers, filtered rows, cached icons. An icon
        /// not yet known arrives later through <see cref="OnLauncherIconReady"/> - it is resolved on
        /// the pool, and only while the launcher is on (S0030 HT-1).
        /// </summary>
        private void ReloadLauncherRows()
        {
            _launcherRowsStamp = LauncherRowsStamp();
            if (_launcherStore.Version != _launcherIconsVersion)
            {
                _launcherIcons.Invalidate();
                _launcherIconsVersion = _launcherStore.Version;
            }
            bool withIcons = _config.EnableScenarioLauncher;
            _launcherList.BeginUpdate();
            _launcherList.Items.Clear();
            _launcherList.Columns.Clear();
            // Images.Add(key, icon) keeps a clone of the icon that Clear() only forgets, so every
            // refresh leaked one handle per scenario (ST-5). The list now holds bitmaps we own and
            // release once the list has let go of them.
            _launcherImages.Images.Clear();
            foreach (Bitmap bitmap in _launcherBitmaps) bitmap.Dispose();
            _launcherBitmaps.Clear();

            _launcherList.Columns.Add(Translate("Имя"), Column(190));
            _launcherList.Columns.Add(Translate("Путь"), Column(250));
            _launcherList.Columns.Add(Translate("Аргументы"), Column(140));
            _launcherList.Columns.Add(Translate("Рабочая папка"), Column(130));
            _launcherList.Columns.Add(Translate("Админ"), Column(70));
            _launcherList.Columns.Add(Translate("Комбинация"), Column(115));

            string filter = _launcherSearch.Text.Trim();
            foreach (LauncherScenario scenario in _launcherStore.All)
            {
                if (filter.Length > 0
                    && scenario.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && scenario.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var item = new ListViewItem(scenario.Name) { Tag = scenario };
                item.SubItems.Add(scenario.IsYtDlp ? "yt-dlp" : scenario.Path);
                item.SubItems.Add(scenario.IsYtDlp ? scenario.YtDlpFormat : scenario.Arguments);
                item.SubItems.Add(scenario.IsYtDlp ? scenario.YtDlpOutputFolder : scenario.WorkingDirectory);
                item.SubItems.Add(scenario.RunAsAdmin ? "✓" : "");
                item.SubItems.Add(scenario.Hotkey);
                System.Drawing.Icon? icon = withIcons ? _launcherIcons.Get(scenario, OnLauncherIconReady) : null;
                if (icon != null)
                    SetLauncherRowIcon(item, scenario.Id, icon);
                _launcherList.Items.Add(item);
            }
            _launcherList.EndUpdate();
            // The surrounding captions just changed language, so the space left for the table did too.
            if (_launcherPanel != null) LayoutLauncherList(_launcherPanel);
            else StretchLauncherColumns();

            _launcherLoadErrors.Visible = _launcherStore.LoadErrors.Count > 0;
            if (_launcherLoadErrors.Visible)
                _launcherLoadErrors.Text = string.Format(Translate("Не прочитано файлов: {0}"), _launcherStore.LoadErrors.Count)
                    + " — " + string.Join(", ", _launcherStore.LoadErrors);

            ApplyLauncherEnabledState();
        }

        /// <summary>An icon resolved on the pool, back on the UI thread: put it on its row, if the row is still there.</summary>
        private void OnLauncherIconReady(Guid id, System.Drawing.Icon icon)
        {
            if (IsDisposed) return;
            foreach (ListViewItem item in _launcherList.Items)
                if (item.Tag is LauncherScenario scenario && scenario.Id == id)
                {
                    SetLauncherRowIcon(item, id, icon);
                    return;
                }
        }

        private void SetLauncherRowIcon(ListViewItem item, Guid id, System.Drawing.Icon icon)
        {
            string key = id.ToString("N");
            if (_launcherImages.Images.ContainsKey(key))
            {
                item.ImageKey = key;
                return;
            }
            Bitmap bitmap = icon.ToBitmap();
            _launcherBitmaps.Add(bitmap);
            _launcherImages.Images.Add(key, bitmap);
            item.ImageKey = key;
        }

        private void ApplyLauncherEnabledState()
        {
            bool enabled = _config.EnableScenarioLauncher;
            _launcherList.Enabled = _launcherSearch.Enabled = enabled;
            foreach (Button button in _launcherButtons) button.Enabled = enabled;
            if (_launcherImportOcr != null && enabled)
                _launcherImportOcr.Enabled = true; // the click itself reports when nothing is found
        }

        private void LauncherNotifyChanged()
        {
            LauncherScenariosChanged?.Invoke(this, EventArgs.Empty);
            ReloadLauncherRows();
        }

        private void LauncherReselect(Guid id)
        {
            foreach (ListViewItem item in _launcherList.Items)
                if (item.Tag is LauncherScenario scenario && scenario.Id == id)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    return;
                }
        }

        /// <summary>
        /// Clear an imported scenario's chord when something already owns it, and say whether it did.
        /// The scenario is already in the store, so it ignores itself in the conflict check.
        /// </summary>
        private bool StripClashingHotkey(LauncherScenario scenario)
            => LauncherMigration.StripClashingHotkey(_launcherStore, scenario, Chords());

        /// <summary>The scenario's chord must be free among all four hotkey owners; warn and reject otherwise.</summary>
        private bool LauncherHotkeyIsFree(LauncherScenario scenario)
        {
            if (scenario.Hotkey.Length == 0) return true;
            // The dialog stores only a chord that parses; anything else is "no chord".
            if (!Hotkey.TryParse(scenario.Hotkey, out Hotkey chord)) return true;
            return ChordIsFree(chord, ChordKind.Launcher, scenario.Id.ToString());
        }

        private void LauncherAdd()
        {
            using var dialog = new LauncherScenarioDialog(null, _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Scenario == null) return;
            if (!LauncherHotkeyIsFree(dialog.Scenario)) return;
            _launcherStore.Add(dialog.Scenario);
            LauncherNotifyChanged();
            LauncherReselect(dialog.Scenario.Id);
        }

        private void LauncherEdit()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            using var dialog = new LauncherScenarioDialog(selected, _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Scenario == null) return;
            if (!LauncherHotkeyIsFree(dialog.Scenario)) return;
            _launcherStore.Update(dialog.Scenario);
            LauncherNotifyChanged();
            LauncherReselect(dialog.Scenario.Id);
        }

        private void LauncherClone()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            LauncherScenario clone = selected.Clone();
            clone.Id = Guid.NewGuid();
            clone.Filename = string.Empty; // the store assigns a fresh file and appends the order
            clone.Hotkey = string.Empty;   // one chord cannot trigger two scenarios
            clone.Name = string.Format(Translate("{0} (копия)"), selected.Name);
            _launcherStore.Add(clone);
            LauncherNotifyChanged();
            LauncherReselect(clone.Id);
        }

        private void LauncherRemove()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, string.Format(Translate("Удалить сценарий «{0}»?"), selected.Name),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            string? failure = _launcherStore.Remove(selected.Id);
            if (failure != null)
            {
                Warn(string.Format(Translate("Не удалось удалить сценарий «{0}»: {1}"), selected.Name, failure));
                return;
            }
            LauncherNotifyChanged();
        }

        private void LauncherMove(int direction)
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            _launcherStore.Move(selected.Id, direction);
            LauncherNotifyChanged();
            LauncherReselect(selected.Id);
        }

        /// <summary>Run from the editor: same execution path as tray/Jump List, but errors are modal here (spec §7).</summary>
        private void LauncherRun()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            string name = selected.Name;
            // The launch runs on the pool (S0030 HT-1); the verdict comes back to this thread.
            LauncherExecution.LaunchAsync(selected, Translate, () =>
            {
                using var prompt = new YtDlpLinkDialog(_config.UiLanguage);
                return prompt.ShowDialog(this) == DialogResult.OK ? prompt.Link : null;
            }).ContinueWith(task =>
            {
                LauncherLaunchResult result = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                    ? task.Result
                    : LauncherLaunchResult.Fail(FailureCause.Describe(task.Exception?.GetBaseException(), _config.UiLanguage));
                if (!result.Success && !result.Cancelled && !IsDisposed)
                    Warn(string.Format(Translate("Не удалось запустить «{0}»: {1}"), name, result.ErrorMessage));
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void LauncherExport()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            string suggested = selected.Name;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');
            using var dialog = new SaveFileDialog
            {
                Filter = "XML|*.xml|*.*|*.*", Title = Translate("Экспортировать сценарий в XML"),
                FileName = (suggested.Trim().Length == 0 ? "scenario" : suggested.Trim()) + ".xml",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _launcherStore.Export(selected, dialog.FileName);
                ConfirmDialog.Show(this, _config.UiLanguage, string.Format(Translate("Сценарий «{0}» экспортирован."), selected.Name),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn(string.Format(Translate("Не удалось экспортировать: {0}"), FailureCause.Describe(ex, _config.UiLanguage)));
            }
        }

        private void LauncherImport()
        {
            using var dialog = new OpenFileDialog { Filter = "XML|*.xml|*.*|*.*", Title = Translate("Выберите XML-файл сценария") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LauncherScenario? imported = _launcherStore.Import(dialog.FileName, out Exception? error);
            if (imported == null)
            {
                // A renamed root or a newer schemaVersion is not damage - say what to do, not the serializer's text.
                Warn(error is ScenarioFormatException
                    ? Translate("Файл не является сценарием в формате, который понимает эта версия CyrFlip. Возможно, он создан более новой версией - обновите CyrFlip.")
                    : string.Format(Translate("Не удалось импортировать: {0}"), FailureCause.Describe(error, _config.UiLanguage)));
                return;
            }
            // A file exported on another machine can carry a chord that is taken here. One chord
            // belongs to one action, so it is dropped - the scenario itself still arrives.
            bool chordDropped = StripClashingHotkey(imported);
            LauncherNotifyChanged();
            LauncherReselect(imported.Id);
            string message = string.Format(Translate("Импортирован сценарий «{0}»."), imported.Name);
            if (chordDropped) message += "\n" + Translate("Его комбинация уже занята, поэтому не перенесена.");
            ConfirmDialog.Show(this, _config.UiLanguage, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>The explicit, repeatable OneClickRunner import (spec §5.3) - the source is never modified.</summary>
        private void LauncherImportFromOcr()
        {
            if (!LauncherMigration.SourceExists())
            {
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("Сценарии OneClickRunner не найдены."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            LauncherMigration.Result result = LauncherMigration.Import(_launcherStore, chords: Chords);
            LauncherNotifyChanged();
            string summary = string.Format(Translate("Перенесено сценариев: {0}."), result.Imported);
            if (result.AlreadyPresent > 0)
                summary += "\n" + string.Format(Translate("Уже перенесены ранее: {0}."), result.AlreadyPresent);
            if (result.Skipped.Count > 0)
                summary += "\n" + string.Format(Translate("Пропущено повреждённых файлов: {0}."), result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + string.Format(Translate("Из-за совпадения идентификаторов назначены новые: {0}."), result.NewIds);
            if (result.ChordsDropped > 0)
                summary += "\n" + string.Format(Translate("Комбинации уже заняты, поэтому не перенесены: {0}."), result.ChordsDropped);
            ConfirmDialog.Show(this, _config.UiLanguage, summary, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static TabPage Page(string title, string description, params Control[] controls)
        {
            var page = new TabPage(title);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = ThemePalette.Light.SurfaceRaised };
            panel.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(930, 0), ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(2, 0, 2, 10) });
            foreach (Control control in controls) panel.Controls.Add(control);
            page.Controls.Add(panel); return page;
        }
        private static FlowLayoutPanel ContentPanel(TabPage page) => (FlowLayoutPanel)page.Controls[0];
        // The kinds are the numbers the pages were always given; the list itself has no slot 4 (the unused
        // "advanced" picture), so the kinds above it sit one place lower.
        private static TabPage WithIcon(TabPage page, int kind) { page.ImageIndex = IconIndex(kind); return page; }
        internal static int IconIndex(int kind) => kind > 4 ? kind - 1 : kind;

        /// <summary>
        /// The vocabulary glyph a tab stands for (ICON-SET). Only the meanings that have a record are here:
        /// Settings, About and Translate. The other pages wait for theirs (catalog proposal
        /// PROPOSAL-2026-09-26-cyrflip-windows-tray-app) and keep the private drawing meanwhile - the
        /// registry exception X1 - rather than borrow another meaning's picture.
        /// </summary>
        internal static string? TabGlyphId(int kind)
        {
            switch (kind)
            {
                case 0: return AppGlyphs.Settings;
                case 5: return AppGlyphs.Info;
                case 9: return AppGlyphs.Translate;
                default: return null;
            }
        }

        internal const int TabIconSize = 20;

        internal static ImageList CreateTabIcons(Color ink)
        {
            var images = new ImageList { ImageSize = new Size(TabIconSize, TabIconSize), ColorDepth = ColorDepth.Depth32Bit };
            // The bitmaps stay owned by the ImageList: it holds them as "originals" until something
            // asks for its native handle, so disposing them here throws on the first paint. The list
            // frees whatever it still holds when the form disposes it (see Dispose) - which is why the
            // ImageList itself has to be kept: a control it is assigned to never owns it.
            images.Images.Add("general", TabIcon(0, ink)); images.Images.Add("indicators", TabIcon(1, ink)); images.Images.Add("hotkeys", TabIcon(2, ink));
            images.Images.Add("clipboard", TabIcon(3, ink)); images.Images.Add("about", TabIcon(5, ink));
            images.Images.Add("languages", TabIcon(6, ink));
            images.Images.Add("conversions", TabIcon(7, ink));
            images.Images.Add("launcher", TabIcon(8, ink));
            images.Images.Add("translate", TabIcon(9, ink));
            images.Images.Add("notes", TabIcon(10, ink));
            images.Images.Add("graphics", TabIcon(11, ink));
            return images;
        }

        /// <summary>A vocabulary glyph where the page has one, else the private 18-grid drawing centred on the same 20 canvas (the pages whose meaning has no record yet - registry exception X1).</summary>
        internal static Bitmap TabIcon(int kind, Color ink)
        {
            string? id = TabGlyphId(kind);
            if (id != null)
            {
                Bitmap? glyph = GlyphRenderer.Render(id, TabIconSize, ink);
                if (glyph != null) return glyph;
            }
            var canvas = new Bitmap(TabIconSize, TabIconSize);
            using (Graphics g = Graphics.FromImage(canvas))
            using (Bitmap legacy = LegacyTabIcon(kind, ink))
                g.DrawImage(legacy, (TabIconSize - 18) / 2, (TabIconSize - 18) / 2, 18, 18);
            return canvas;
        }

        private static Bitmap LegacyTabIcon(int kind, Color accent)
        {
            var image = new Bitmap(18, 18); using var g = Graphics.FromImage(image); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(accent, 1.8f); using var brush = new SolidBrush(accent);
            switch (kind)
            {
                case 1: g.DrawEllipse(pen, 2, 5, 14, 8); g.FillEllipse(brush, 7, 7, 4, 4); break;
                case 2: g.DrawRectangle(pen, 2, 4, 14, 10); for (int x = 4; x <= 12; x += 4) for (int y = 6; y <= 10; y += 4) g.FillRectangle(brush, x, y, 2, 2); break;
                case 3: g.DrawRectangle(pen, 4, 3, 10, 13); g.DrawLine(pen, 6, 6, 12, 6); g.DrawLine(pen, 6, 9, 12, 9); g.DrawLine(pen, 6, 12, 10, 12); break;
                case 6: g.DrawEllipse(pen, 2, 2, 14, 14); g.DrawEllipse(pen, 6.5f, 2, 5, 14); g.DrawLine(pen, 2.5f, 9, 15.5f, 9); break;
                // Two opposite arrows: the layout-to-layout conversion table.
                case 7:
                    g.DrawLine(pen, 3, 6, 14, 6); g.DrawLine(pen, 11, 3, 14, 6); g.DrawLine(pen, 11, 9, 14, 6);
                    g.DrawLine(pen, 15, 12, 4, 12); g.DrawLine(pen, 7, 9, 4, 12); g.DrawLine(pen, 7, 15, 4, 12);
                    break;
                // The absorbed OneClickRunner's own mark, as line art: a list of commands under the
                // pointer. Same shape as the full-colour icon in the page header and on the taskbar.
                case 8: LauncherBrand.DrawGlyph(g, 18, pen, brush); break;
                // A sheet with a turned-down corner and two lines of writing: the quick notes.
                case 10:
                    g.DrawLine(pen, 3, 2, 11, 2); g.DrawLine(pen, 3, 2, 3, 16); g.DrawLine(pen, 3, 16, 15, 16);
                    g.DrawLine(pen, 15, 16, 15, 6); g.DrawLine(pen, 11, 2, 15, 6); g.DrawLine(pen, 11, 2, 11, 6);
                    g.DrawLine(pen, 11, 6, 15, 6);
                    g.FillRectangle(brush, 6, 9, 6, 1); g.FillRectangle(brush, 6, 12, 5, 1);
                    break;
                // A dashed selection rectangle with a corner handle: the Graphics module (S0026).
                case 11:
                    using (var dashed = new Pen(accent, 1.6f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                        g.DrawRectangle(dashed, 2, 3, 11, 10);
                    g.FillRectangle(brush, 11, 11, 5, 5);
                    break;
                // Settings, About and Translate are drawn from the vocabulary (TabGlyphId); a page whose glyph
                // cannot be drawn shows its caption alone rather than another meaning's picture.
                default: break;
            }
            return image;
        }
        private Control MarkerSizeRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Размер метки:", AutoSize = true, Padding = new Padding(0, 8, 5, 0) });
            row.Controls.Add(_markerSize);
            row.Controls.Add(_markerSizeValue);
            return row;
        }

        private string MarkerSizeName(int step)
            => Translate(step <= 0 ? "Маленькая" : step == 1 ? "Средняя" : "Крупная");

        private Control ThemeRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Тема:", AutoSize = true, Width = 155, Padding = new Padding(0, 7, 0, 0) });
            row.Controls.Add(_themeSystem);
            row.Controls.Add(_themeLight);
            row.Controls.Add(_themeDark);
            return row;
        }

        /// <summary>
        /// One of the three theme buttons was checked: store the invariant token and repaint every window
        /// at once (<c>APP-STYLE</c> section 2 - no restart, no "apply"). The unchecking half of the pair
        /// raises the event too and is ignored.
        /// </summary>
        private void ThemeChosen(RadioButton button, ThemeMode mode)
        {
            if (_loading || !button.Checked) return;
            _config.Theme = ThemeModes.Token(mode);
            _config.Save();
            ThemeManager.SetMode(mode);
        }

        private Control LanguageRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Язык интерфейса:", AutoSize = true, Width = 155, Padding = new Padding(0, 7, 0, 0) });
            row.Controls.Add(_uiLanguage);
            return row;
        }
        /// <summary>
        /// The version line on the About tab: the stamped build version, and - only for a Store
        /// build - which package this is. That distinction is not decoration: a packaged CyrFlip
        /// cannot write the autostart entry itself and never downloads the Ollama installer, so it
        /// is the first thing worth knowing when someone reports that either did nothing.
        ///
        /// <para>The version is the same <c>YY.M.D.HHmm</c> stamp the release ZIP carries, so a user
        /// can tell at a glance whether they are on the build a fix went into.</para>
        /// </summary>
        private string VersionLine()
        {
            string line = string.Format(Translate("Версия {0}"), SupportBundle.AppVersion());
            return PackageInfo.IsPackaged ? line + " (Microsoft Store)" : line;
        }

        private TabPage AboutPage()
        {
            var page = Page("О программе и дополнительно", "CyrFlip — лёгкая утилита для Windows: показывает код текущей раскладки у текстового курсора и каретки, исправляет текст, набранный в неверной раскладке, и хранит необязательную локальную историю буфера.\n\nПриложение не передаёт историю буфера в сеть. Если история включена, записи защищены Windows DPAPI и читаются только той же учётной записью Windows.");
            var panel = ContentPanel(page);
            // Filled by ApplyLanguage, not here: it carries the version number, so a text set at
            // build time would be captured by RememberRussianTexts as if it were a Russian caption
            // and then "translated" on every language change. Starting empty keeps it out of that map.
            _version.Font = BoldFont;
            panel.Controls.Add(_version);
            panel.Controls.Add(new Label { Text = "Разработчик: SerZhyAle", AutoSize = true, Font = BoldFont, Margin = new Padding(3, 14, 3, 4) });
            panel.Controls.Add(Link("Сайт программы: serzhyale.github.io/CyrFlip", "https://serzhyale.github.io/CyrFlip/"));
            panel.Controls.Add(Link("GitHub: github.com/SerZhyAle/CyrFlip", "https://github.com/SerZhyAle/CyrFlip"));
            panel.Controls.Add(Link("Сайт разработчика: sza.od.ua", "https://sza.od.ua/"));
            // The licence notice of the vendored vocabulary glyphs (ICON-EXTERNAL rule 5, ticket S0022 A10);
            // the file itself also ships beside the exe and inside the MSIX.
            panel.Controls.Add(Link("Сторонние компоненты: значки Material Icons (Apache-2.0)", "https://github.com/SerZhyAle/CyrFlip/blob/main/THIRD-PARTY-NOTICES.md"));
            panel.Controls.Add(new Label { Text = "CyrFlip работает локально, без телеметрии и сетевой синхронизации истории буфера.", AutoSize = true, MaximumSize = new Size(690, 0), Padding = new Padding(0, 14, 0, 0), ForeColor = ThemePalette.Light.TextMuted });
            panel.Controls.Add(Setting(Button("Диагностика положения каретки...", _diagnoseCaret), "Создаёт локальный отчёт о том, как Windows и UI Automation видят каретку. Нужен только если метка не появляется или рисуется не там в конкретной программе."));
            _sendLogs = Button("Отправить логи автору..", SendLogsToAuthor);
            panel.Controls.Add(Setting(_sendLogs, "Собирает логи CyrFlip в один архив и открывает письмо автору с этим вложением. Письмо отправляете вы сами — CyrFlip ничего не передаёт в сеть. История буфера обмена в архив не попадает."));
            return page;
        }

        /// <summary>
        /// Collect the logs, show what was collected, and - only if the user says so - hand the archive
        /// to their mail client. Off the UI thread twice over, and for two different reasons: packing
        /// is disk work, and <c>MAPISendMail</c> with <c>MAPI_DIALOG</c> blocks until the compose
        /// window is closed.
        /// </summary>
        private void SendLogsToAuthor()
        {
            if (_sendLogs != null) _sendLogs.Enabled = false;
            Task.Run(() =>
            {
                try
                {
                    SupportBundle.Result result = SupportBundle.CreateDefault(_config, DateTime.Now);
                    BeginInvoke(new Action(() => ShowSupportBundle(result)));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (_sendLogs != null) _sendLogs.Enabled = true;
                        ConfirmDialog.Show(this, _config.UiLanguage, Translate("Не удалось собрать архив с логами:") + "\n" + FailureCause.Describe(ex, _config.UiLanguage),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                }
            });
        }

        private void ShowSupportBundle(SupportBundle.Result result)
        {
            DialogResult answer;
            using (var dialog = new SupportBundleDialog(result, _config.UiLanguage))
                answer = dialog.ShowDialog(this);
            if (answer != DialogResult.OK)
            {
                // The archive stays on disk: the user may well want to attach it by hand later.
                if (_sendLogs != null) _sendLogs.Enabled = true;
                return;
            }

            SupportMail mail = MailSender.Compose(result.ArchivePath, SupportBundle.AppVersion(),
                MailSender.LanguageCode(_config.UiLanguage), MailSender.DescribeSystem());
            // A dedicated STA thread, not the pool (S0010 TD-8); the continuation re-enables the button.
            MailSender.SendOnStaAsync(mail, new MailSender.WindowsMailTransport()).ContinueWith(task =>
            {
                MailOutcome outcome = task.Result;
                try { BeginInvoke(new Action(() => AfterSupportMail(outcome, result))); }
                catch (InvalidOperationException) { /* the settings window was closed meanwhile */ }
            }, TaskScheduler.Default);
        }

        private void AfterSupportMail(MailOutcome outcome, SupportBundle.Result result)
        {
            if (_sendLogs != null) _sendLogs.Enabled = true;
            switch (outcome)
            {
                // A mailto: message cannot carry an attachment - no mail client accepts one from a
                // link. Say so plainly instead of letting the user send an empty report.
                case MailOutcome.MailtoOpened:
                    ConfirmDialog.Show(this, _config.UiLanguage,
                        Translate("Ваша почтовая программа не принимает вложение из ссылки. Письмо открыто, а архив выделен в проводнике — перетащите его в письмо перед отправкой."),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case MailOutcome.Manual:
                    ConfirmDialog.Show(this, _config.UiLanguage,
                        Translate("Не удалось открыть почтовую программу. Отправьте архив вручную на адрес:")
                        + "\n" + MailSender.AuthorAddress + "\n\n" + result.ArchivePath,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
        private static LinkLabel Link(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            link.LinkClicked += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            return link;
        }
        private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
        private static RadioButton Radio(string text) => new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 5, 12, 3) };
        private static Button Button(string text, Action action) { var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 5, 3, 5) }; button.Click += (_, _) => action(); return button; }
        private static Control Setting(Control control, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            block.Controls.Add(control);
            block.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }
        private static Control HotkeyRow(string label, Label value, Action change, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            row.Controls.Add(new Label { Text = label + ":", AutoSize = true, Width = 150, Padding = new Padding(0, 7, 0, 0) });
            value.Padding = new Padding(0, 7, 5, 0); row.Controls.Add(value); row.Controls.Add(Button("Изменить...", change));
            block.Controls.Add(row);
            block.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }

        // Variant with a leading enable checkbox: [☑ action]   chord   [Change...].
        private static Control HotkeyRow(CheckBox enable, Label value, Action change, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            // AutoSize (never clip the caption at any DPI or in any language); the chord column is
            // lined up afterwards by AlignHotkeyCaptions, which widens all three to the widest one.
            enable.AutoSize = true; enable.Margin = new Padding(3, 8, 3, 3);
            row.Controls.Add(enable);
            value.Padding = new Padding(0, 7, 5, 0); row.Controls.Add(value); row.Controls.Add(Button("Изменить...", change));
            block.Controls.Add(row);
            block.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }
        private void Changed(Action<bool> action, bool value) { if (_loading) return; action(value); Reload(); }

        protected override void Dispose(bool disposing)
        {
            // Base first: it disposes the control tree, and the launcher ListView still points at
            // the ImageList while it does.
            base.Dispose(disposing);
            if (disposing)
            {
                _launcherIcons.Dispose();
                _launcherImages.Dispose();
                foreach (Bitmap bitmap in _launcherBitmaps) bitmap.Dispose();
                _tabIcons?.Dispose();   // assigned to the TabControl, which never owned it
                _boldFont?.Dispose();
                _ownFont?.Dispose();
                _ownIcon?.Dispose();
            }
        }
    }
}

