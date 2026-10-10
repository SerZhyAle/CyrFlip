using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Persistent settings surface; closing it only hides it so CyrFlip remains tray-first.</summary>
    internal sealed partial class SettingsForm : ThemedForm
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
        // The theme (ticket S0020, the owner's rework of 2026-10-08): a drop-down in the shape of the
        // chord one - a translated caption over the invariant token, refilled by ReloadThemeCombo.
        private readonly ComboBox _themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
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
        private readonly CheckBox _screenshotFolderHotkeyEnabled = Check("Открыть папку со снимками");
        private readonly Label _screenshotFolderHotkeyValue = new Label { AutoSize = true };
        private readonly TextBox _screenshotFolderBox = new TextBox { ReadOnly = true, Width = 520, Margin = new Padding(3, 6, 3, 3) };
        private readonly List<Control> _screenshotFolderControls = new List<Control>();
        // ---- Symbols palette (S0056) ----
        private readonly CheckBox _symbolsEnabled = Check("Включить окно символов");
        private readonly CheckBox _symbolsHotkeyEnabled = Check("Окно символов");
        private readonly Label _symbolsHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _symbolsHideTypable = Check("Скрывать символы, которые есть на моих раскладках");
        private readonly List<Control> _symbolsControls = new List<Control>();
        private readonly Dictionary<Control, string> _russianTexts = new Dictionary<Control, string>();
        // ---- WINDOWS-UI section 3: collapsible groups, remembered context, discovery ----
        // Every group this window built, in build order; the expansion of each is remembered under its
        // stable Id (never a translated caption), and the palette pass walks this list on every theme.
        private readonly List<CollapsibleGroup> _groups = new List<CollapsibleGroup>();
        // (caption-side control, hint label) pairs whose hint must sit under the caption's own text
        // inset (WINDOWS-UI section 4, boolean rows); re-measured on every language/font change.
        private readonly List<KeyValuePair<Control, Label>> _hintPairs = new List<KeyValuePair<Control, Label>>();
        // The hint label of each row, looked up by the control the row is about (the dependency pass)
        private readonly Dictionary<Control, Label> _rowHints = new Dictionary<Control, Label>();
        // The page index a viewport capture belongs to: SelectedIndexChanged fires after the selection
        // moved, so the capture must name the page the user is leaving, not the one they entered.
        private int _lastPageIndex;
        /// <summary>True while saved group/viewport state is being restored, so the restores themselves
        /// do not write the same state back (and never run as user edits - WINDOWS-UI section 3 rule 4).</summary>
        private bool _restoringContext;
        private int _hoveredTab = -1;
        /// <summary>True while the window still holds the content-fit default size: the first
        /// ApplyLanguage then re-derives it from the laid-out content (WINDOWS-UI section 5).</summary>
        private bool _firstSize;
        /// <summary>The DPI the page icons were last rendered for, so a monitor change re-renders them.</summary>
        private int _tabIconDpi = 96;
        private SettingsWheelFilter? _wheelFilter;
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
        private ThemePalette _tabIconsPalette = ThemePalette.Light;
        private System.Drawing.Icon? _ownIcon;
        private TabControl? _tabs;
        private FlowLayoutPanel? _launcherPanel;
        private bool _layingOutLauncher;
        // KLID → installed layout name, so the conversion table doesn't re-read the registry per row.
        private readonly Dictionary<string, string> _layoutNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The audit-facing view of the window's own furniture: the groups built, the page list, and
        // the same match rule the search dialog runs - read by the conformance tests (S0048).
        internal IReadOnlyList<CollapsibleGroup> Groups => _groups;
        internal TabControl? Tabs => _tabs;
        internal static bool SearchMatchesForTest(string texts, string query) => SettingsSearchDialog.Matches(texts, query);

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
        /// <summary>"Изменить..." beside the screenshots-folder chord (S0057).</summary>
        public event EventHandler? ScreenshotFolderHotkeyChangeRequested;
        /// <summary>"Открыть папку" - the one opener the chord uses too (S0057).</summary>
        public event EventHandler? ScreenshotFolderOpenRequested;
        /// <summary>"Изменить..." beside the text context menu's keyboard chord (S0045 K2).</summary>
        public event EventHandler? TextMenuHotkeyChangeRequested;
        /// <summary>"Сделать снимок области" - the same action as the tray item.</summary>
        public event EventHandler? ScreenshotRequested;
        /// <summary>Raised after anything on the Symbols tab changes (S0056).</summary>
        public event EventHandler? SymbolsChanged;
        /// <summary>"Изменить..." beside the symbols window chord.</summary>
        public event EventHandler? SymbolsHotkeyChangeRequested;
        /// <summary>"Открыть окно символов" button.</summary>
        public event EventHandler? OpenSymbolsRequested;

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

            Text = "Настройки CyrFlip";
            // The user's own rectangle (WINDOWS-UI section 5, APP-BEHAVIOUR rule 10): restored clamped
            // to a work area, written back on hide - never while maximized. A never-sized window keeps
            // the content-fit default, centred. The config stores logical 96-DPI pixels, so the same
            // rectangle comes back on a monitor of another DPI (S0060 P2).
            Rectangle savedBounds = SavedWindowBounds();
            if (savedBounds.IsEmpty) { StartPosition = FormStartPosition.CenterScreen; Size = DefaultWindowSize(); _firstSize = true; }
            else { StartPosition = FormStartPosition.Manual; Bounds = savedBounds; }
            MinimumSize = new Size(ControlDpi.Scale(DeviceDpi, 900), ControlDpi.Scale(DeviceDpi, 620)); ShowInTaskbar = true;
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
            _tabIcons = CreateTabIcons(ThemePalette.Light, 96);
            _tabIconDpi = DeviceDpi;
            tabs.ImageList = _tabIcons;
            tabs.DrawItem += DrawTab;
            // INPUT-PARITY rule 2: the focus frame follows the strip's focus (DrawTab). The hover fill
            // follows the pointer (WINDOWS-UI section 2 rule 2).
            tabs.GotFocus += (_, _) => tabs.Invalidate();
            tabs.LostFocus += (_, _) => tabs.Invalidate();
            tabs.MouseMove += (_, e) =>
            {
                int index = TabIndexAt(e.Location);
                if (index == _hoveredTab) return;
                _hoveredTab = index;
                tabs.Invalidate();
            };
            tabs.MouseLeave += (_, _) => { if (_hoveredTab != -1) { _hoveredTab = -1; tabs.Invalidate(); } };
            _uiLanguage.Items.AddRange(Localization.Names);
            tabs.TabPages.Add(WithIcon(Page("Общие", "Основные параметры приложения. Все изменения применяются сразу.",
                Setting(_autostart, Autostart.ManagedByWindows
                    ? "Автозапуском версии из Microsoft Store управляет сама Windows. Флажок показывает текущее состояние, а нажатие открывает «Параметры ▸ Приложения ▸ Автозагрузка», где автозапуск включается и выключается."
                    : "Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."),
                ShortcutSettings(),
                Setting(_keepAwake, "Пока включено, Windows не уходит в сон или гибернацию по простою — удобно для долгих загрузок, копирования или рендера. Состояние сохраняется: забытый включённым переключатель не даст компьютеру уснуть и после перезапуска - следить за вашей батареей CyrFlip не станет."),
                Setting(_keepScreen, "Экран не гаснет и не блокируется по бездействию, как во время просмотра видео. Блокировку по паролю (Win+L или политику безопасности) это не отменяет. Состояние сохраняется: забытый включённым переключатель не даст экрану погаснуть и после перезапуска."),
                Setting(LanguageRow(), "Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее."),
                Setting(ThemeRow(), "«Как в Windows» - CyrFlip светлый или тёмный вместе с Windows и переключается вслед за ней без перезапуска. Контрастные темы Windows всегда важнее этой настройки. Метка раскладки у курсора и каретки от темы не зависит: её цвет обозначает раскладку."),
                Group("general/transfer", "Перенос настроек",
                    Button("Экспортировать настройки...", () => SettingsTransferRequested?.Invoke("export")),
                    Button("Импортировать настройки...", () => SettingsTransferRequested?.Invoke("import")),
                    _restoreSettings = Button("Вернуть настройки до импорта...", () => SettingsTransferRequested?.Invoke("restore")))), 0));
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
            tabs.TabPages.Add(WithIcon(HotkeysOverviewPage(), 13));
            tabs.TabPages.Add(WithIcon(ConversionsPage(), 7));
            tabs.TabPages.Add(WithIcon(LanguagesPage(), 6));
            tabs.TabPages.Add(WithIcon(ClipboardPage(), 3));
            tabs.TabPages.Add(WithIcon(QuickNotesPage(), 10));
            tabs.TabPages.Add(WithIcon(GraphicsPage(), 11));
            TabPage translatePage = WithIcon(TranslatePage(), 9);
            tabs.TabPages.Add(translatePage);
            tabs.TabPages.Add(WithIcon(LauncherPage(), 8));
            tabs.TabPages.Add(WithIcon(SymbolsPage(), 12));
            tabs.TabPages.Add(WithIcon(AboutPage(), 5));
            // The build version at the foot of the page list, beside the product name in the header band: it is
            // where the eye goes to ask "which build is this" (the About page keeps the full line).
            _navVersion = new Label { Text = SupportBundle.AppVersion(), AutoSize = true, UseMnemonic = false, ForeColor = ThemePalette.Light.TextMuted };
            Controls.Add(_navVersion);
            _navVersion.BringToFront();
            SizeChanged += (_, _) => PlaceNavVersion();
            Controls.Add(tabs);
            // The page header (WINDOWS-UI section 2 rule 3): commands that act on the content pane,
            // at the reading end. Text commands rather than glyph-only tools, so the localized word is
            // the control's own caption and no accessible-name pass can miss it.
            Controls.Add(BuildHeaderCommands());

            // Come back to the page the user was last on. Restored before subscribing so the restore
            // itself doesn't count as a change, and clamped: the tab strip grows between versions.
            // Come back to the page the user was last on - by the page's stable key, not its position
            // (M6): the strip grows between versions, and a remembered index silently changes meaning.
            // The old index stays as the fallback a build before this one wrote.
            if (!string.IsNullOrEmpty(_config.SettingsPageId)
                && Enum.TryParse(_config.SettingsPageId, out SettingsPageKey savedPage))
                SelectPage(savedPage);
            else if (_config.SettingsTab > 0 && _config.SettingsTab < tabs.TabPages.Count)
                tabs.SelectedIndex = _config.SettingsTab;
            _lastPageIndex = tabs.SelectedIndex;
            UpdateHeaderCommands();
            // A TabControl reports no selected page before its handle exists, so the first state is taken then.
            tabs.HandleCreated += (_, _) => UpdateHeaderCommands();
            tabs.SelectedIndexChanged += (_, _) =>
            {
                UpdateHeaderCommands();
                _config.SaveSettingsTab(tabs.SelectedIndex, tabs.SelectedTab?.Tag?.ToString());
                // The page being left keeps where it was scrolled to; the page being entered gets it
                // back once it has laid out (WINDOWS-UI section 3 rule 3).
                CaptureViewport(_lastPageIndex);
                _lastPageIndex = tabs.SelectedIndex;
                RestoreViewport(tabs.SelectedIndex);
                // Old installer downloads go when the translator page is looked at (S0037 TR-5).
                if (ReferenceEquals(tabs.SelectedTab, translatePage))
                    OllamaManager.PruneDownloadsInBackground();
            };
            // The wheel must never turn an editor the pointer merely hovers (WINDOWS-UI section 4),
            // so while this window lives the process-level filter turns it into page scrolling.
            _wheelFilter = new SettingsWheelFilter(this);
            Application.AddMessageFilter(_wheelFilter);

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
            _themeCombo.SelectedIndexChanged += (_, _) => ThemeChosen();
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
            _screenshotFolderHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotFolderHotkey = _screenshotFolderHotkeyEnabled.Checked);

            _symbolsEnabled.CheckedChanged += (_, _) =>
                SymbolsSettingChanged(() => _config.EnableSymbols = _symbolsEnabled.Checked, reload: true);
            _symbolsHotkeyEnabled.CheckedChanged += (_, _) =>
                SymbolsSettingChanged(() => _config.EnableSymbolsHotkey = _symbolsHotkeyEnabled.Checked);
            _symbolsHideTypable.CheckedChanged += (_, _) =>
                SymbolsSettingChanged(() => _config.SymbolsHideTypable = _symbolsHideTypable.Checked);

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
            BuildTransferPreferenceGroups();
            RememberRussianTexts(this);
            // Saved context before the first Reload so the restores never count as user edits
            // (WINDOWS-UI section 3 rule 4): the groups come back as they were left, every page's
            // scroll anchor is applied after its layout, and the numeric editors take their logical
            // metrics from the font the window actually opens with.
            _restoringContext = true;
            RestoreGroupStates();
            _restoringContext = false;
            // The metrics pass writes every editor's width and font (M1): under one suspension.
            SuspendLayoutDeep(this);
            try { foreach (NumericUpDown editor in NumericEditors(this)) SettingsNumeric.Apply(editor); }
            finally { ResumeLayoutDeep(this); }
            Reload();
            // Reload() ends with ApplyLanguage() - the window is already in the new language here.
            // The opening page's scroll anchor waits for the first showing: until then the page has
            // no real layout (and the window may have no handle to post the restore to).
            // The restored page is applied to the native control with its handle, which can be after the first
            // header sync: take the title once more when the window is really on screen.
            Shown += (_, _) => { UpdateHeaderCommands(); RestoreViewport(_tabs == null ? 0 : _tabs.SelectedIndex); };
            // The rectangle is written when the window goes away, never while the user drags it
            // (APP-BEHAVIOUR rule 10).
            FormClosing += (_, _) => SaveUiContext();
            VisibleChanged += (_, _) => { if (!Visible) SaveUiContext(); };
        }

        /// <summary>
        /// The window's header band (WINDOWS-UI section 2 rules 2-3): the product name over the page list, then
        /// - over the content pane - the selected page's title (its purpose line is the first line of the pane),
        /// and at the reading end the three commands that act on that pane, as glyph-only buttons with localized tooltips and
        /// accessible names: search, expand all, collapse all. A hairline under the pane part closes the band.
        /// AutoSize all the way down - an AutoSize=false strip inside an AutoSize column reports no preferred
        /// width and the later commands end up clipped (found by the rendered audit).
        /// </summary>
        private Control BuildHeaderCommands()
        {
            Button find = GlyphCommand("settingsSearch", AppGlyphs.Search, "Найти настройку...", OpenSettingsSearch);
            Button expandAll = GlyphCommand("settingsExpandAll", AppGlyphs.Expand, "Развернуть все", () => SetPageGroupsExpanded(true));
            Button collapseAll = GlyphCommand("settingsCollapseAll", AppGlyphs.Collapse, "Свернуть все", () => SetPageGroupsExpanded(false));
            _expandAllButton = expandAll; _collapseAllButton = collapseAll;
            var commands = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0) };
            commands.Controls.Add(find);
            commands.Controls.Add(expandAll);
            commands.Controls.Add(collapseAll);

            var brand = new Label { Text = "CyrFlip", AutoSize = true, Font = BoldFont, Margin = new Padding(16, 14, 3, 6), UseMnemonic = false, Tag = "brand" };
            // The band holds the title only, so its height is the same on every page. The purpose line is the
            // first line of the page itself (Page): a band that grew and shrank with the description squeezed
            // the page list, which wrapped into a second column and moved the selected page's column beside
            // the pane - the items "jumped" and the pane covered them.
            _headerTitle = new Label { AutoSize = true, Font = TitleFont, Margin = new Padding(3, 4, 3, 8), UseMnemonic = false, Tag = "title" };
            var titleBlock = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 2) };
            titleBlock.Controls.Add(_headerTitle);

            // Column 0 is as wide as the page list below it, so the title block starts where the pane does.
            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 8, 14, 0), BackColor = ThemePalette.Light.SurfaceWindow };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(titleBlock, 1, 0);
            header.Controls.Add(commands, 2, 0);
            commands.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            // The hairline under the pane part of the band (the page list keeps its own column).
            header.Paint += (_, e) =>
            {
                ThemePalette palette = ThemeApply.PaletteOf(header) ?? ThemePalette.Light;
                int start = header.ColumnStyles[0].SizeType == SizeType.Absolute ? (int)header.ColumnStyles[0].Width : 0;
                int y = header.Height - 1;
                using var pen = new Pen(palette.Border);
                if (header.RightToLeft == RightToLeft.Yes) e.Graphics.DrawLine(pen, 14, y, header.Width - start, y);
                else e.Graphics.DrawLine(pen, start, y, header.Width - 14, y);
            };
            _header = header;
            // The commands sit above the pages, so the keyboard reaches them first (APP-SETTINGS rule 10:
            // a sensible tab order). TabIndex is independent of the z-order the docking relies on.
            header.TabIndex = 0;
            if (_tabs != null) _tabs.TabIndex = 1;
            return header;
        }

        private TableLayoutPanel? _header;
        private Label? _headerTitle;
        private Font? _titleFont;
        private readonly List<KeyValuePair<Button, string>> _headerCommandKeys = new List<KeyValuePair<Button, string>>();

        /// <summary>The page title's font: the window font at one and a half times, bold - the top of the hierarchy.</summary>
        private Font TitleFont => _titleFont ?? (_titleFont = new Font(Font.FontFamily, Font.SizeInPoints * 1.5f, FontStyle.Bold));

        /// <summary>A glyph-only header command. Its Russian name is a key the language pass translates into the
        /// accessible name and the tooltip (<see cref="NameHeaderCommands"/>), so a live language switch reaches it.</summary>
        private Button GlyphCommand(string name, string glyphId, string russianName, Action action)
        {
            int edge = ControlDpi.Scale(DeviceDpi, 40);
            var button = new Button { Name = name, AutoSize = false, Size = new Size(edge, edge), Margin = new Padding(6, 0, 0, 0), TabStop = true, UseMnemonic = false };
            button.Click += (_, _) => action();
            AsMoveGlyph(button, glyphId);
            button.AccessibleName = russianName;
            _headerCommandKeys.Add(new KeyValuePair<Button, string>(button, russianName));
            return button;
        }

        private void NameHeaderCommands()
        {
            foreach (KeyValuePair<Button, string> pair in _headerCommandKeys)
                pair.Key.AccessibleName = Translate(pair.Value).TrimEnd('.', ' ');
        }

        /// <summary>The selected page's title, copied into the header band (the page's own title label stays in
        /// its tree, hidden, so the language pass and the search keep reading it).</summary>
        private void SyncPageHeader()
        {
            if (_tabs == null || _headerTitle == null) return;
            TabPage? page = _tabs.SelectedTab ?? (_tabs.TabCount > 0 ? _tabs.TabPages[0] : null);
            if (page == null) return;
            _headerTitle.Text = page.Text;
        }
        private Button? _expandAllButton, _collapseAllButton;

        private Label? _navVersion;

        private void PlaceNavVersion()
        {
            if (_tabs == null || _navVersion == null) return;
            int x = RightToLeft == RightToLeft.Yes ? ClientSize.Width - _navVersion.Width - 16 : 16;
            _navVersion.Location = new Point(Math.Max(0, x), Math.Max(0, ClientSize.Height - _navVersion.Height - 12));
        }
        /// <summary>Expand all / Collapse all act on the groups of the current page; on a page with none they
        /// would do nothing, so they are unavailable rather than silently inert (WINDOWS-UI section 2 rule 3).</summary>
        private void UpdateHeaderCommands()
        {
            if (_tabs == null || _expandAllButton == null || _collapseAllButton == null) return;
            SyncPageHeader();
            // Before the strip has a selection (the window is built, not yet shown) the first page is the one on screen.
            TabPage? page = _tabs.SelectedTab ?? (_tabs.TabCount > 0 ? _tabs.TabPages[0] : null);
            if (page == null) return;
            bool any = false;
            foreach (Control child in ContentPanel(page).Controls)
                if (child is CollapsibleGroup) { any = true; break; }
            _expandAllButton.Enabled = _collapseAllButton.Enabled = any;
        }

        /// <summary>Every NumericUpDown the window built, so the logical metrics pass reaches them all.</summary>
        internal static List<NumericUpDown> NumericEditors(Control root)
        {
            var found = new List<NumericUpDown>();
            WalkNumericEditors(root, found);
            return found;
        }

        private static void WalkNumericEditors(Control root, List<NumericUpDown> found)
        {
            if (root is NumericUpDown editor) found.Add(editor);
            foreach (Control child in root.Controls) WalkNumericEditors(child, found);
        }

        /// <summary>
        /// Apply the saved expansion to every group. Unknown ids are skipped (a page that retired a
        /// group leaves nothing behind), and a group never mentioned in the saved state stays expanded
        /// - the default of a page someone has not reorganized is everything visible.
        /// </summary>
        private void RestoreGroupStates()
        {
            Dictionary<string, bool> states = ParseGroupState(_config.SettingsGroupsState);
            foreach (CollapsibleGroup group in _groups)
                if (states.TryGetValue(group.Id, out bool expanded)) group.Expanded = expanded;
        }

        internal static Dictionary<string, bool> ParseGroupState(string raw)
        {
            var states = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return states;
            foreach (string part in raw.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                // The state string is "id=1|0" by contract, not a bool literal: 1 and 0 survive a
                // registry round trip and a hand edit equally.
                if (part.Substring(eq + 1) == "1") states[part.Substring(0, eq)] = true;
                else if (part.Substring(eq + 1) == "0") states[part.Substring(0, eq)] = false;
            }
            return states;
        }

        /// <summary>Serialize every group's current expansion (the AppConfig side re-sanitizes on read).</summary>
        internal string GroupStateString()
        {
            var pairs = new List<string>(_groups.Count);
            foreach (CollapsibleGroup group in _groups) pairs.Add(group.Id + "=" + (group.Expanded ? "1" : "0"));
            return string.Join(";", pairs);
        }

        /// <summary>The persisted context: group expansion, the page viewports and the window rectangle.</summary>
        private void SaveUiContext()
        {
            CaptureViewport(_lastPageIndex);
            _config.SettingsGroupsState = GroupStateString();
            if (WindowState == FormWindowState.Normal)
            {
                // Logical 96-DPI pixels (APP-BEHAVIOUR rule 10): the same rectangle restores at the same
                // visual size on a monitor of another DPI (S0060 P2). Written at the DPI the window
                // lives on right now - ControlDpi keeps that number truthful on a moved window.
                int dpi = Math.Max(96, DeviceDpi);
                _config.SettingsWindowX = ControlDpi.Unscale(dpi, Left);
                _config.SettingsWindowY = ControlDpi.Unscale(dpi, Top);
                _config.SettingsWindowWidth = ControlDpi.Unscale(dpi, Width);
                _config.SettingsWindowHeight = ControlDpi.Unscale(dpi, Height);
            }
            _config.SaveSettingsUiState();
        }

        /// <summary>
        /// The saved window rectangle, clamped to a work area, or <see cref="Rectangle.Empty"/> when
        /// the user never resized (int.MinValue sentinel = never placed, 0 size = never sized). The
        /// stored values are logical 96-DPI pixels; they come back at the monitor's own DPI.
        /// </summary>
        private Rectangle SavedWindowBounds()
        {
            if (_config.SettingsWindowWidth <= 0 || _config.SettingsWindowHeight <= 0
                || _config.SettingsWindowX == int.MinValue || _config.SettingsWindowY == int.MinValue)
                return Rectangle.Empty;
            int dpi = Math.Max(96, DeviceDpi);
            var bounds = new Rectangle(
                ControlDpi.Scale(dpi, _config.SettingsWindowX),
                ControlDpi.Scale(dpi, _config.SettingsWindowY),
                ControlDpi.Scale(dpi, _config.SettingsWindowWidth),
                ControlDpi.Scale(dpi, _config.SettingsWindowHeight));
            try
            {
                Screen[] screens = Screen.AllScreens;
                var workAreas = new Rectangle[screens.Length];
                for (int i = 0; i < screens.Length; i++) workAreas[i] = screens[i].WorkingArea;
                return ScreenPlacement.Clamp(bounds, workAreas);
            }
            catch { return Rectangle.Empty; }
        }

        /// <summary>Record where the given page is scrolled to, as its anchor group/row plus a logical offset.</summary>
        private void CaptureViewport(int pageIndex)
        {
            if (_tabs == null || pageIndex < 0 || pageIndex >= _tabs.TabPages.Count) return;
            FlowLayoutPanel flow = ContentPanel(_tabs.TabPages[pageIndex]);
            int scroll = Math.Max(0, -flow.AutoScrollPosition.Y);
            (string id, int top) anchor = ViewportAnchor(flow, scroll);
            int offset = scroll - anchor.top;
            // Device px -> logical px, so the same anchor+offset holds on a different monitor DPI.
            int offsetLogical = (int)Math.Round(offset * 96.0 / Math.Max(96, DeviceDpi));
            _config.SettingsViewport = UpsertViewportRow(_config.SettingsViewport, pageIndex, anchor.id, offsetLogical);
        }

        internal static string UpsertViewportRow(string raw, int pageIndex, string anchorId, int offsetLogical)
        {
            var kept = new List<string>();
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (!int.TryParse(part.Split('|')[0], out int page) || page != pageIndex)
                    kept.Add(part);
            kept.Add(pageIndex + "|" + anchorId + "|" + offsetLogical);
            return string.Join(";", kept);
        }

        internal static (string id, int top) ViewportAnchor(FlowLayoutPanel flow, int scroll)
        {
            // The anchor is the last group whose top the viewport has reached; on a flat page (no
            // groups) it is the last row the viewport has reached, addressed by child ordinal. Both
            // are stable internal ids - never a translated caption (WINDOWS-UI section 3 rule 3).
            // Positions are compared in content coordinates: a scrolled panel moves its children, so
            // a child's Top is relative to the visible area and only Top - AutoScrollPosition.Y (a
            // non-positive number) says where it sits in the page.
            CollapsibleGroup? bestGroup = null;
            Control? bestRow = null;
            for (int i = 0; i < flow.Controls.Count; i++)
            {
                Control child = flow.Controls[i];
                if (ContentTop(flow, child) > scroll) continue;
                if (child is CollapsibleGroup group) bestGroup = group;
                else if (bestGroup == null) bestRow = child;
            }
            if (bestGroup != null) return ("g:" + bestGroup.Id, ContentTop(flow, bestGroup));
            if (bestRow != null) return ("r:" + flow.Controls.IndexOf(bestRow), ContentTop(flow, bestRow));
            return ("r:0", 0);
        }

        /// <summary>A child's top in the page's content coordinates, whatever the panel is scrolled to.</summary>
        internal static int ContentTop(ScrollableControl flow, Control child) => child.Top - flow.AutoScrollPosition.Y;

        /// <summary>Put a page back where it was, after it has laid out; an unknown anchor stays at the top.</summary>
        private void RestoreViewport(int pageIndex)
        {
            if (_tabs == null || pageIndex < 0 || pageIndex >= _tabs.TabPages.Count) return;
            (string? anchorId, int offsetLogical) = ReadViewportRow(_config.SettingsViewport, pageIndex);
            if (anchorId == null || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _tabs == null || _tabs.SelectedIndex != pageIndex) return;
                FlowLayoutPanel flow = ContentPanel(_tabs.TabPages[pageIndex]);
                Control? anchor = FindViewportAnchor(flow, anchorId);
                if (anchor == null) return;   // missing id falls back to the page top (the current state)
                int offset = (int)Math.Round(offsetLogical * Math.Max(96, DeviceDpi) / 96.0);
                // The capture stored scroll - anchorTop, so the viewport goes back below the anchor.
                ScrollPage(flow, Math.Max(0, ContentTop(flow, anchor) + offset));
            }));
        }

        /// <summary>Scroll a page flow to the given content offset. Through
        /// <see cref="ScrollableControl.AutoScrollPosition"/>, whose setter takes the positive offset
        /// and clamps it: assigning <c>VerticalScroll.Value</c> on an AutoScroll panel can move the bar
        /// without moving the content. The horizontal position is kept as it is.</summary>
        internal static void ScrollPage(FlowLayoutPanel flow, int target)
        {
            try { flow.AutoScrollPosition = new Point(-flow.AutoScrollPosition.X, Math.Max(0, target)); }
            catch { /* no scrollbar yet - nothing to scroll */ }
        }

        internal static (string? id, int offset) ReadViewportRow(string raw, int pageIndex)
        {
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = part.Split(new[] { '|' });
                if (fields.Length == 3 && int.TryParse(fields[0], out int page) && page == pageIndex)
                    return (fields[1], int.TryParse(fields[2], out int offset) ? offset : 0);
            }
            return (null, 0);
        }

        private static Control? FindViewportAnchor(FlowLayoutPanel flow, string anchorId)
        {
            if (anchorId.StartsWith("g:", StringComparison.Ordinal))
            {
                foreach (Control child in flow.Controls)
                    if (child is CollapsibleGroup group && group.Id == anchorId.Substring(2)) return group;
                return null;
            }
            if (anchorId.StartsWith("r:", StringComparison.Ordinal) && int.TryParse(anchorId.Substring(2), out int index)
                && index >= 0 && index < flow.Controls.Count)
                return flow.Controls[index];
            return null;
        }

        /// <summary>Expand or collapse every group of the current page (the header's expand/collapse all).</summary>
        private void SetPageGroupsExpanded(bool expanded)
        {
            if (_tabs?.SelectedTab is not TabPage page) return;
            // One write for the whole command, not one per group it touches - and one layout, not one
            // per group the visibility change would otherwise re-run (M1).
            _restoringContext = true;
            FlowLayoutPanel flow = ContentPanel(page);
            flow.SuspendLayout();
            try
            {
                foreach (Control child in flow.Controls)
                    if (child is CollapsibleGroup group) group.Expanded = expanded;
            }
            finally { flow.ResumeLayout(true); _restoringContext = false; }
            SaveUiContext();
        }

        // ---- Discovery (WINDOWS-UI section 3 rule 5): one search over captions and descriptions ----

        /// <summary>One searchable row: the page it lives on, the group owning it ("" when the page is
        /// one flat list), the row block to reveal, and the texts to match against. The texts are read
        /// from the live controls at search time, so a result is always in the language on screen.</summary>
        internal sealed class SettingsSearchHit
        {
            public TabPage Page = null!;
            public string PageTitle = "";
            public string GroupCaption = "";
            public Control Row = null!;
            public Control Focus = null!;
            /// <summary>The row's own caption (its first text) - what the result list shows.</summary>
            public string Caption = "";
            public string Texts = "";
        }

        internal List<SettingsSearchHit> BuildSearchIndex()
        {
            var hits = new List<SettingsSearchHit>();
            if (_tabs == null) return hits;
            foreach (TabPage page in _tabs.TabPages)
                CollectSearchRows(page, ContentPanel(page), page.Text, "", hits);
            return hits;
        }

        private static void CollectSearchRows(TabPage page, Control container, string pageTitle, string groupCaption, List<SettingsSearchHit> hits)
        {
            foreach (Control child in container.Controls)
            {
                if (child is CollapsibleGroup group)
                {
                    CollectSearchRows(page, group.Body, pageTitle, group.Caption, hits);
                    continue;
                }
                var texts = new List<string>();
                CollectSearchTexts(child, texts);
                if (texts.Count == 0) continue;
                hits.Add(new SettingsSearchHit
                {
                    Page = page, PageTitle = pageTitle, GroupCaption = groupCaption, Row = child,
                    Focus = FirstFocusable(child) ?? child, Caption = OneLine(texts[0]), Texts = string.Join(" ", texts),
                });
            }
        }

        /// <summary>A caption as one list line: a wrapped label's line breaks become spaces.</summary>
        internal static string OneLine(string text)
            => string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        private static void CollectSearchTexts(Control root, List<string> texts)
        {
            if ((root is Label || root is ButtonBase) && !string.IsNullOrWhiteSpace(root.Text)) texts.Add(root.Text);
            foreach (Control child in root.Controls) CollectSearchTexts(child, texts);
        }

        /// <summary>The row's editor - the first leaf control a keyboard user lands on - or the row itself.</summary>
        private static Control? FirstFocusable(Control root)
        {
            if (root is CheckBox || root is RadioButton || root is ButtonBase || root is TextBoxBase
                || root is ListControl || root is NumericUpDown)
                return root;
            foreach (Control child in root.Controls)
            {
                Control? found = FirstFocusable(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OpenSettingsSearch()
        {
            using var dialog = new SettingsSearchDialog(this);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (dialog.Chosen != null) RevealSetting(dialog.Chosen);
        }

        /// <summary>Explicit navigation to a setting (WINDOWS-UI section 3 rule 4): its page is
        /// selected, its group expanded, it is scrolled into view and focused. The restore itself
        /// runs no change handlers - it only moves the view.</summary>
        internal void RevealSetting(SettingsSearchHit hit)
        {
            if (_tabs == null) return;
            _tabs.SelectedTab = hit.Page;
            for (Control? parent = hit.Row.Parent; parent != null; parent = parent.Parent)
                if (parent is CollapsibleGroup group) group.Expanded = true;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                ContentPanel(hit.Page).ScrollControlIntoView(hit.Focus);
                hit.Focus.Focus();
            }));
        }

        /// <summary>
        /// The search dialog: a query, the matches, a status line. Modal, owned, centred on the
        /// settings window; Enter or a double click opens the chosen setting, Escape and the close
        /// box close it without touching anything (APP-BEHAVIOUR rule 1).
        /// </summary>
        internal sealed class SettingsSearchDialog : ThemedForm
        {
            private readonly SettingsForm _owner;
            private readonly TextBox _query = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0) };
            private readonly ListBox _results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            private readonly Label _status = new Label { AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 0) };
            private List<SettingsSearchHit> _index;
            private List<SettingsSearchHit> _matches = new List<SettingsSearchHit>();

            public SettingsSearchHit? Chosen { get; private set; }

            internal SettingsSearchDialog(SettingsForm owner)
            {
                _owner = owner;
                _index = owner.BuildSearchIndex();
                Text = owner.Translate("Поиск по настройкам");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = MaximizeBox = false;
                ShowInTaskbar = false;
                // The owner's script font and direction (ApplyScript already chose them for the
                // current language); a Form never disposes a Font it was merely handed.
                Font = owner.Font;
                if (owner.RightToLeft == RightToLeft.Yes) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
                ClientSize = new Size(560, 420);
                MinimumSize = new Size(460, 320);

                var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(new Label { Text = owner.Translate("Введите слова для поиска: совпадение находится по названию настройки и по её описанию. Enter открывает найденную настройку, Esc закрывает поиск."), AutoSize = true, MaximumSize = new Size(530, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
                // The query box itself - without it the dialog had nothing to type into.
                _query.Margin = new Padding(0, 0, 0, 6);
                _query.AccessibleName = Text;
                table.Controls.Add(_query, 0, 1);
                table.Controls.Add(_results, 0, 2);
                table.Controls.Add(_status, 0, 3);
                Controls.Add(table);

                _query.TextChanged += (_, _) => RunQuery();
                _results.SelectedIndexChanged += (_, _) => _status.Text = _results.SelectedItem == null ? "" : _matches[_results.SelectedIndex].GroupCaption.Length == 0
                    ? _matches[_results.SelectedIndex].PageTitle
                    : _matches[_results.SelectedIndex].PageTitle + " - " + _matches[_results.SelectedIndex].GroupCaption;
                _results.DoubleClick += (_, _) => Accept();
                _results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; Accept(); } };
                // Enter on the query opens the selected match; the arrows walk the list without
                // leaving the query. Escape anywhere is the one no-action exit (ProcessCmdKey) - a
                // hidden CancelButton never fires, PerformClick ignores a button that is not visible.
                _query.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; Accept(); }
                    else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && _results.Items.Count > 0)
                    {
                        int next = _results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                        _results.SelectedIndex = Math.Max(0, Math.Min(_results.Items.Count - 1, next));
                        e.Handled = e.SuppressKeyPress = true;
                    }
                };
                RunQuery();
            }

            internal TextBox QueryForTest => _query;
            internal ListBox ResultsForTest => _results;

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            // Focus belongs to the query once the dialog can take it - setting ActiveControl in
            // the constructor throws for a form that is not visible yet.
            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _query.Focus();
            }

            /// <summary>All whitespace-separated terms must match, case-insensitively, in the current language.</summary>
            internal static bool Matches(string texts, string query)
            {
                foreach (string term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(texts, term, System.Globalization.CompareOptions.IgnoreCase) < 0)
                        return false;
                return true;
            }

            private void RunQuery()
            {
                string query = _query.Text.Trim();
                _matches = _index.Where(hit => Matches(hit.Texts, query)).ToList();
                _results.BeginUpdate();
                _results.Items.Clear();
                // The row's caption only - Texts is caption and description joined by spaces, so
                // it has no first line to cut; the page and group name go to the status line.
                foreach (SettingsSearchHit hit in _matches) _results.Items.Add(hit.Caption);
                _results.EndUpdate();
                _status.Text = query.Length == 0 ? "" : _matches.Count == 0 ? _owner.Translate("Ничего не найдено.") : "";
                if (_results.Items.Count > 0) _results.SelectedIndex = 0;
            }

            private void Accept()
            {
                if (_results.SelectedIndex < 0 || _results.SelectedIndex >= _matches.Count) return;
                Chosen = _matches[_results.SelectedIndex];
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        public void Reload()
        {
            _loading = true;
            _restoreSettings.Enabled = SettingsSnapshot.Newest(SettingsSnapshot.Folder) != null;
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
            ReloadSymbolsState();
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
            // The bulk pass touches every caption of the window. Without layout suspension each
            // assignment re-lays-out the whole parent chain - and every relayout re-measures its
            // labels, which on the complex-script fonts (Nirmala UI, Vrinda, the Arabic faces) is
            // seconds of Uniscribe shaping for nothing. Suspended, the ~400 captions are assigned
            // raw and the tree lays out once, at the resume.
            SuspendLayoutDeep(this);
            try
            {
                ApplyScript();
                foreach (KeyValuePair<Control, string> pair in _russianTexts)
                    pair.Key.Text = Translate(pair.Value);
                _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
                _quickNotesHotkeyValue.Text = _config.QuickNotesHotkey;
                _textMenuHotkeyValue.Text = _config.TextMenuHotkey;
                _screenshotHotkeyValue.Text = _config.ScreenshotHotkey;
                _screenshotFolderHotkeyValue.Text = _config.ScreenshotFolderHotkey;
                _screenshotFolderBox.Text = ScreenshotFolderShown();
                _opacityValue.Text = _opacity.Value + "%";
                _markerSizeValue.Text = MarkerSizeName(_markerSize.Value);
                _version.Text = VersionLine();
            }
            finally { ResumeLayoutDeep(this); }
            // Every pass below re-lays out part of the tree (M1): under one suspension, so each of
            // them writes geometry raw and the tree lays out once, at the resume.
            SuspendLayoutDeep(this);
            try
            {
                AlignHotkeyCaptions();
                AdjustTabStrip();
                // Font-derived geometry is re-measured, not remembered: the boolean hints under their
                // captions, the per-page caption column, and the numeric editors' logical metrics all
                // follow the font (and family) the language just installed (WINDOWS-UI sections 4-5).
                AlignHintAndCaptionColumns();
                foreach (NumericUpDown editor in NumericEditors(this)) SettingsNumeric.Apply(editor);
                ClampPageTextWidths();
            }
            finally { ResumeLayoutDeep(this); }
            // Built in the current language, so these must run after the translation pass above.
            ReloadLayoutRows();
            ReloadLanguageRows();
            ReloadConversionRows();
            ReloadTranslationRows();
            ReloadHotkeysOverview();
            ReloadToggleCombo();
            ReloadContextMenuChords();
            ReloadThemeCombo();
            // Every Reload() ends here - i.e. every checkbox on every page - so the scenario table is
            // rebuilt only when something it shows changed (S0030 HT-1).
            string launcherStamp = LauncherRowsStamp();
            if (launcherStamp != _launcherRowsStamp)
                ReloadLauncherRows();
            else
                ApplyLauncherEnabledState();
            NameEditors(this);
            NameFixedControls();
            ApplyGlyphTooltips();
            if (_firstSize && IsHandleCreated)
            {
                _firstSize = false;
                ResizeToContent();
            }
        }

        // Editors whose accessible name this class derived from the caption beside them, so a later
        // language change may replace it without touching a name somebody set on purpose.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> _derivedNames
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

        /// <summary>
        /// Names every editor after the caption that stands before it in its row (APP-SETTINGS rule 10: an
        /// accessible name on every control). A combo box, a numeric editor, a track bar or a text box carries
        /// no text of its own, so a screen reader would announce only its type and value; the caption label is
        /// a separate control. Re-run after every language change, because the captions change.
        /// </summary>
        internal void NameEditors(Control root, string? caption = null)
        {
            foreach (Control child in root.Controls)
            {
                if (child is TableLayoutPanel { Tag: CaptionedRow row })
                {
                    NameEditors(row.Editors, row.Caption.Text.TrimEnd(':', ' '));
                    continue;
                }
                if (child is Label label)
                {
                    if ("caption".Equals(label.Tag) && label.Text.Length > 0) caption = label.Text.TrimEnd(':', ' ');
                    continue;
                }
                if (caption != null && child is ComboBox or NumericUpDown or TrackBar or TextBoxBase or ListView
                    && (string.IsNullOrEmpty(child.AccessibleName) || _derivedNames.TryGetValue(child, out _)))
                {
                    child.AccessibleName = caption;
                    if (!_derivedNames.TryGetValue(child, out _)) _derivedNames.Add(child, child);
                }
                if (child.HasChildren) NameEditors(child);
            }
        }

        /// <summary>Names of the controls whose caption is not a sibling label, refreshed with the language.</summary>
        private void NameFixedControls()
        {
            NameHeaderCommands();
            SyncPageHeader();
            _launcherSearch.AccessibleName = Translate("Поиск:").TrimEnd(':', ' ');
            if (_launcherUp != null) _launcherUp.AccessibleName = Translate("Переместить вверх");
            if (_launcherDown != null) _launcherDown.AccessibleName = Translate("Переместить вниз");
            _screenshotFolderBox.AccessibleName = Translate("Папка для снимков области экрана");
            _launcherList.AccessibleName = Translate("Быстрый запуск");
        }

        private Button? _launcherUp, _launcherDown;

        private readonly ToolTip _glyphTips = new ToolTip();

        /// <summary>
        /// A glyph-only command says what it does on hover as well as to a screen reader (WINDOWS-UI section 2
        /// rule 3: glyph-only commands have localized tooltips). The tip is the accessible name, so the two
        /// can never disagree. Native tooltips are a declared theme exception (APP-STYLE).
        /// </summary>
        private void ApplyGlyphTooltips()
        {
            foreach (Button button in _moveButtons)
                if (!string.IsNullOrEmpty(button.AccessibleName)) _glyphTips.SetToolTip(button, button.AccessibleName);
        }

        /// <summary>What the scenario table depends on: the language, the store's content and the switch.</summary>
        private string LauncherRowsStamp()
            => _config.UiLanguage + "|" + _launcherStore.Version + "|" + _config.EnableScenarioLauncher;

        /// <summary>
        /// Layout suspension over the whole tree: <see cref="Control.SuspendLayout"/> guards one
        /// container only, and a bulk caption or font pass must not pay a relayout per assignment.
        /// </summary>
        private static void SuspendLayoutDeep(Control root)
        {
            root.SuspendLayout();
            foreach (Control child in root.Controls) SuspendLayoutDeep(child);
        }

        private static void ResumeLayoutDeep(Control root)
        {
            foreach (Control child in root.Controls) ResumeLayoutDeep(child);
            root.ResumeLayout(performLayout: true);
        }

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
            row.Controls.Add(new Label { Text = "Аккорд мыши:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
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
        /// The reach is capped (H3): a long language on a narrow window keeps a strip that fits beside
        /// the content - the caption itself degrades through the draw's ellipsis instead of pushing it.
        /// </summary>
        private void AdjustTabStrip()
        {
            if (_tabs == null) return;
            int widest = 0;
            using (var bold = new Font(Font, FontStyle.Bold))
                foreach (TabPage page in _tabs.TabPages)
                    widest = Math.Max(widest, TextRenderer.MeasureText(page.Text, bold).Width);
            // Measured, not Font.Height: a Font object caches its pixel height at creation DPI, and the
            // row must grow with the monitor the window is on now (S0060 P2).
            int rowHeight = Math.Max(40, TextLineHeight() + 22);

            // The page list must never run out of height. A vertical TabControl is always multiline: when the
            // pages do not fit in one column the native control starts a second one, moves the column of the
            // selected page beside the pane and leaves the pane where it was - pages then jump and vanish
            // behind the content. So the rows are counted against the window: the minimum height grows to hold
            // every row (plus the build number under them), and on a screen too short even for that the rows
            // themselves are made lower rather than letting the strip wrap.
            int count = Math.Max(1, _tabs.TabCount);
            int band = _header == null ? 0 : _header.GetPreferredSize(new Size(Math.Max(1, ClientSize.Width), 0)).Height;
            int foot = (_navVersion == null ? 0 : _navVersion.Height + 12) + 12;
            int chrome = Math.Max(0, Height - ClientSize.Height);
            int baseMinimum = ControlDpi.Scale(DeviceDpi, 620);
            int screenHeight = Screen.FromControl(this).WorkingArea.Height;
            int fits = (screenHeight - chrome - band - foot) / count;
            rowHeight = Math.Max(Math.Min(rowHeight, fits), TextLineHeight() + 8);
            int minimum = Math.Max(baseMinimum, chrome + band + count * rowHeight + foot);
            if (MinimumSize.Height != minimum) MinimumSize = new Size(MinimumSize.Width, minimum);

            int cap = Math.Max(170, Math.Min(widest + 60 + TabIconSizeFor(DeviceDpi) - TabIconSize, Math.Max(170, _tabs.Width / 4)));
            _tabs.ItemSize = new Size(rowHeight, cap);
            if (_header != null) _header.ColumnStyles[0].Width = cap;
            PlaceNavVersion();
        }

        /// <summary>The current pixel height of one text line at the window's real DPI.</summary>
        private int TextLineHeight() => TextRenderer.MeasureText("Аg", Font).Height;

        // The wrap width a text block was authored with (MaximumSize), remembered at first clamp, so a
        // wider window returns it instead of keeping the narrow one (H3). One int per label.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Label, System.Runtime.CompilerServices.StrongBox<int>> _textCaps
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Label, System.Runtime.CompilerServices.StrongBox<int>>();

        /// <summary>The narrowest wrap a text block degrades to before one-word lines.</summary>
        internal const int MinTextWidth = 220;

        /// <summary>
        /// Clamps every wrapping text block (page descriptions, hints, the muted prose labels) to the
        /// width its page actually has (H3): the authored caps (890/930 px) assumed the default window,
        /// and on a narrow window a hint kept overflowing into a horizontal scrollbar. Re-run on every
        /// language pass and every page resize; a wider window restores the authored width.
        /// </summary>
        private void ClampPageTextWidths()
        {
            if (_tabs == null) return;
            foreach (TabPage page in _tabs.TabPages)
            {
                FlowLayoutPanel flow = ContentPanel(page);
                _editorColumn = WidestEditors(flow);
                ClampTextWidths(flow, flow.ClientSize.Width - flow.Padding.Horizontal);
            }
        }

        private void ClampTextWidths(Control container, int available)
        {
            foreach (Control child in container.Controls)
            {
                if (child is Label label && label.MaximumSize.Width > 0)
                {
                    System.Runtime.CompilerServices.StrongBox<int> box;
                    if (!_textCaps.TryGetValue(label, out box!))
                    {
                        box = new System.Runtime.CompilerServices.StrongBox<int>(label.MaximumSize.Width);
                        _textCaps.Add(label, box);
                    }
                    int wanted = Math.Min(box.Value, Math.Max(MinTextWidth, available - child.Margin.Horizontal - child.Padding.Horizontal));
                    if (label.MaximumSize.Width != wanted) label.MaximumSize = new Size(wanted, 0);
                }
                if (child is TableLayoutPanel { Tag: CaptionedRow captioned }) { FitCaptionedRow(captioned, available); continue; }
                // Inside a group the budget shrinks by the group's own insets; a page's children pass
                // the page's. Recurse only into containers that lay their children out top-down.
                if (child is CollapsibleGroup group) { group.FitWidth(available - group.Margin.Horizontal); ClampTextWidths(group.Body, available - group.Margin.Horizontal - group.Body.Margin.Horizontal - group.Body.Padding.Horizontal); }
                else if (child is FlowLayoutPanel flow && flow.FlowDirection == FlowDirection.TopDown)
                    ClampTextWidths(flow, available - flow.Padding.Horizontal);
            }
        }

        /// <summary>
        /// The first size comes from the content (WINDOWS-UI section 5): the widest page's preferred
        /// width plus the strip, never smaller than the minimum and never larger than the work area.
        /// Runs once, after the first language pass has laid the pages out.
        /// </summary>
        private void ResizeToContent()
        {
            if (_tabs == null) return;
            try
            {
                int widest = 0, tallest = 0;
                foreach (TabPage page in _tabs.TabPages)
                {
                    FlowLayoutPanel flow = ContentPanel(page);
                    int width = 0, height = 0;
                    foreach (Control child in flow.Controls)
                    {
                        if (!child.Visible) continue;
                        Size wanted = child.GetPreferredSize(Size.Empty);
                        width = Math.Max(width, wanted.Width + child.Margin.Horizontal);
                        height += wanted.Height + child.Margin.Vertical;
                    }
                    widest = Math.Max(widest, width);
                    tallest = Math.Max(tallest, height);
                }
                int strip = _tabs.ItemSize.Height;
                Rectangle work = Screen.FromControl(this).WorkingArea;
                int width2 = Math.Min(work.Width - 60, Math.Max(MinimumSize.Width, widest + strip + flowChrome()));
                int height2 = Math.Min(work.Height - 60, Math.Max(MinimumSize.Height, tallest + flowChrome() + 8));
                Size = new Size(width2, height2);
            }
            catch { /* a monitor that would not answer - keep the default size */ }
        }

        private static int flowChrome() => 16 * 2 + 6;   // the page panel's Padding plus a scrollbar's shadow

        /// <summary>
        /// The window landed on a monitor of another DPI (S0060 P2): the stored metrics that derive
        /// from pixels re-derive - the minimum size, the strip, the tab icons, the numeric editors'
        /// native halves - and the wrap widths clamp to the page again.
        /// </summary>
        protected override void OnDpiChanged(int oldDpi, int newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            MinimumSize = new Size(ControlDpi.Scale(newDpi, 900), ControlDpi.Scale(newDpi, 620));
            SuspendLayoutDeep(this);
            try
            {
                AdjustTabStrip();
                AlignHotkeyCaptions();
                AlignHintAndCaptionColumns();
                foreach (NumericUpDown editor in NumericEditors(this)) SettingsNumeric.Apply(editor);
            }
            finally { ResumeLayoutDeep(this); }
            ClampPageTextWidths();
            RebuildTabIcons(newDpi);
        }

        /// <summary>Render the page glyphs again at the monitor's DPI (the icon is a bitmap, not a font).</summary>
        private void RebuildTabIcons(int dpi)
        {
            if (_tabs == null || dpi == _tabIconDpi) return;
            _tabIconDpi = dpi;
            if (_tabIcons == null) return;
            ImageList previous = _tabIcons;
            _tabIcons = CreateTabIcons(_tabIconsPalette, dpi);
            _tabs.ImageList = _tabIcons;
            previous.Dispose();
            _tabs.Invalidate();
        }

        /// <summary>Owner-draws one left-aligned tab: icon + horizontal caption, the selected one bold
        /// on a soft accent fill with an accent bar at the reading start, a lighter accent fill on
        /// hover, and a dotted focus frame when the strip holds the keyboard (WINDOWS-UI section 2
        /// rule 2: hover, focus and selection are distinguishable; colour is not the only indication -
        /// the selection also bolds the caption and draws the bar, the hover underlines the caption).
        /// Mirrored for a right-to-left language (H7): the strip switches to <c>TabAlignment.Right</c>
        /// in <see cref="ApplyScript"/>, the bar moves to the outer edge and the icon rides the
        /// reading start - everything drawn here in the control's own unmirrored coordinates.</summary>
        private void DrawTab(object? sender, DrawItemEventArgs e)
        {
            if (_tabs == null || e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            TabPage page = _tabs.TabPages[e.Index];
            bool selected = _tabs.SelectedIndex == e.Index;
            bool hovered = e.Index == _hoveredTab;
            bool rtl = RightToLeft == RightToLeft.Yes;
            Rectangle bounds = e.Bounds;
            ThemePalette palette = ThemeApply.PaletteOf(_tabs) ?? ThemePalette.Light;
            // The fills are mixes of two roles, re-mixed on every paint from the palette being applied
            // - never literals, so a theme switch repaints them correctly (APP-STYLE section 3).
            Color back = selected ? ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, 0.18f)
                : hovered ? ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, 0.14f)
                : palette.SurfaceWindow;
            using (var fill = new SolidBrush(back))
                e.Graphics.FillRectangle(fill, bounds);
            if (selected)
            {
                int barX = rtl ? bounds.Right - 4 : bounds.Left;
                using (var accent = new SolidBrush(palette.Accent))
                    e.Graphics.FillRectangle(accent, barX, bounds.Top, 4, bounds.Height);
            }

            bool hasIcon = _tabs.ImageList != null && page.ImageIndex >= 0 && page.ImageIndex < _tabs.ImageList.Images.Count;
            Image? icon = hasIcon ? _tabs.ImageList!.Images[page.ImageIndex] : null;
            int textLeft, textRight;
            if (rtl)
            {
                // Reading start is the right edge: icon first, caption to its left (H7).
                int iconX = bounds.Right - 12 - (icon?.Width ?? 0);
                if (icon != null)
                    e.Graphics.DrawImage(icon, iconX, bounds.Top + (bounds.Height - icon.Height) / 2, icon.Width, icon.Height);
                textRight = icon != null ? iconX - 9 : bounds.Right - 12;
                textLeft = bounds.Left + 8;
            }
            else
            {
                int x = bounds.Left + 12;
                if (icon != null)
                {
                    e.Graphics.DrawImage(icon, x, bounds.Top + (bounds.Height - icon.Height) / 2, icon.Width, icon.Height);
                    x += icon.Width + 9;
                }
                textLeft = x;
                textRight = bounds.Right - 8;
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
            FontStyle style = selected ? FontStyle.Bold : hovered ? FontStyle.Underline : FontStyle.Regular;
            using (var font = new Font(Font, style))
                TextRenderer.DrawText(e.Graphics, page.Text, font,
                    Rectangle.FromLTRB(textLeft, bounds.Top, Math.Max(textLeft + 10, textRight), bounds.Bottom),
                    palette.TextPrimary, flags);
            // The native strip's own focus rectangle is gone with the owner draw; a keyboard user
            // still has to see that the page list holds the focus (INPUT-PARITY rule 2).
            if (selected && _tabs.Focused)
                using (var focus = new Pen(palette.TextPrimary) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    e.Graphics.DrawRectangle(focus, bounds.Left + 7, bounds.Top + 3, bounds.Width - 12, bounds.Height - 7);
        }

        /// <summary>The hovered tab under a strip-local point (left-aligned tabs stack one per row).</summary>
        private int TabIndexAt(Point location)
        {
            // The tab rectangles themselves, not ItemSize arithmetic: the strip starts a few pixels
            // below the control's top, so a division by the row height lit the wrong row at its edges.
            if (_tabs == null) return -1;
            for (int i = 0; i < _tabs.TabPages.Count; i++)
                if (_tabs.GetTabRect(i).Contains(location)) return i;
            return -1;
        }

        /// <summary>
        /// The page icons are the mono look (ICON-RENDER section 10 C: tabs), in the theme's text colour -
        /// which is what the dark theme changes - so they are drawn again when it changes (S0021 A10 item 3,
        /// S0022 A2). The selected page keeps its accent bar; that is not a glyph. The groups' header
        /// bands and chevrons re-mix from the same palette, and the nav fills in DrawTab mix on paint.
        /// </summary>
        protected override void OnThemeApplied(ThemePalette palette)
        {
            foreach (Button button in _moveButtons.ToArray()) ApplyMoveGlyph(button, palette);
            foreach (CollapsibleGroup group in _groups) group.ApplyPalette(palette);
            if (_tabs == null || _tabIcons == null || ReferenceEquals(_tabIconsPalette, palette)) return;
            ImageList previous = _tabIcons;
            _tabIcons = CreateTabIcons(palette, _tabIconDpi);
            _tabIconsPalette = palette;
            _tabs.ImageList = _tabIcons;
            previous.Dispose();
            _tabs.Invalidate();
        }

        // The buttons that move a row. Each carries the vocabulary's move-up or move-down (S0022 A4) in the
        // theme's text colour with no caption; the accessible name set beside it is what names it. A glyph
        // that cannot be drawn leaves the arrow character it used to be. The button's Tag is the glyph id.
        private readonly List<Button> _moveButtons = new List<Button>();

        private void AsMoveGlyph(Button button, string glyphId)
        {
            button.Tag = glyphId;
            _moveButtons.Add(button);
            // Rows are rebuilt on every change: a button that goes away takes its picture and its place in the list with it.
            button.Disposed += (_, _) => { _moveButtons.Remove(button); button.Image?.Dispose(); button.Image = null; };
            ApplyMoveGlyph(button, ThemeApply.PaletteOf(this) ?? ThemePalette.Light);
        }

        private static void ApplyMoveGlyph(Button button, ThemePalette palette)
        {
            string id = (string)button.Tag!;
            Image? previous = button.Image;
            Bitmap? image = GlyphRenderer.Render(id, HistoryStripGlyphs.Size, palette.TextPrimary);
            button.Image = image;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.Text = image == null ? (id == AppGlyphs.MoveUp ? "↑" : id == AppGlyphs.MoveDown ? "↓" : "") : "";
            previous?.Dispose();   // after the button let go of it
        }

        /// <summary>The settings window closes (hides) on Escape per APP-BEHAVIOUR rule 1 and APP-SETTINGS rule 9.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // An open drop-down list closes first: WinForms offers the key here before the combo box
                // sees it, so without this a reflexive Escape would hide the window over a pending choice.
                if (ActiveControl is ComboBox { DroppedDown: true }) return base.ProcessCmdKey(ref msg, keyData);
                Close();
                return true;
            }
            // The search is reachable from the keyboard as well as from the header command
            // (WINDOWS-UI section 3 rule 5).
            if (keyData == (Keys.Control | Keys.F))
            {
                OpenSettingsSearch();
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
        /// whole form: WinForms propagates <c>RightToLeft</c> and <c>Font</c> to child controls. The
        /// page strip joins the mirror through <c>TabAlignment.Right</c> (H7) rather than
        /// <c>RightToLeftLayout</c>: the strip is owner-drawn, and a mirrored device context would
        /// flip the native text under it while the GDI+ fills stay put.
        /// </summary>
        private void ApplyScript()
        {
            bool rtl = Localization.IsRightToLeft(_config.UiLanguage);
            RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = rtl;
            if (_tabs != null && (_tabs.Alignment == TabAlignment.Right) != rtl)
                _tabs.Alignment = rtl ? TabAlignment.Right : TabAlignment.Left;
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
                Font? previousTitle = _titleFont;
                _titleFont = null;
                RefreshBoldFonts(this);
                if (_headerTitle != null) _headerTitle.Font = TitleFont;
                previousTitle?.Dispose();
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
                if (child.Font.Bold && !"title".Equals(child.Tag)) child.Font = BoldFont;
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

            panel.Controls.Add(Group("languages/layouts", "Раскладки клавиатуры",
                _layoutRows,
                new Label { Text = "Стрелки меняют порядок раскладок внутри одного языка. Порядок самих языков задаёт Windows и восстанавливает его при входе в систему — меняйте его в параметрах Windows.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 2, 3, 2) },
                LayoutButtonsRow(),
                new Label { Text = "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian и Ukrainian. Для языков с популярными вариантами (например, US International, Spanish Latin American) используйте «Добавить раскладку...».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 5) }));

            panel.Controls.Add(Group("languages/toggle", "Переключение по кругу",
                Setting(ToggleRow(), "Одно сочетание перебирает установленные языки по кругу. Это штатная настройка Windows (Alt+Shift, Ctrl+Shift или «`»); «—» отключает перебор.")));

            panel.Controls.Add(Group("languages/hotkeys", "Прямые сочетания на язык",
                new Label { Text = "Эти сочетания обрабатывает сама Windows: они работают, даже когда CyrFlip закрыт. Комбинацию вы выбираете сами — в отличие от штатного окна, здесь не только Ctrl+Shift+цифра.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 4) },
                _languageRows,
                Setting(_restoreLanguageHotkeys, "Возвращает языковые сочетания Windows в то состояние, в котором они были до первого изменения из CyrFlip.")));

            panel.Controls.Add(Button("Открыть настройки Windows", () => Open("ms-settings:keyboard")));
            return page;
        }

        private FlowLayoutPanel LayoutButtonsRow()
        {
            var layoutButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 4, 3, 2) };
            layoutButtons.Controls.Add(Button("Добавить раскладку...", OnAddLayout));
            layoutButtons.Controls.Add(Button("Добавить популярные языки", AddPopularLayouts));
            layoutButtons.Controls.Add(_restoreLayouts);
            return layoutButtons;
        }

        private FlowLayoutPanel ToggleRow()
        {
            var toggleRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            toggleRow.Controls.Add(new Label { Text = "Сочетание для перебора языков:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 6, 8, 0) });
            toggleRow.Controls.Add(_toggleCombo);
            return toggleRow;
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

        // Row height for the layout / hotkey tables. Fixed width but a height derived from the *current*
        // font plus MiddleLeft alignment: the text is vertically centred and never clipped, whether the
        // font grew because of display scaling or because the language needs a taller family (Devanagari,
        // Bengali, Chinese). A constant 26 px was chopping the header line in half on a scaled display.
        private int RowHeight => Math.Max(26, Font.Height + 9);

        /// <summary>The pointer floor of a glyph-only target in logical pixels (ICON-RENDER section 3 rule 5: 28 under a mouse or a pen).</summary>
        private const int MinTarget = 28;

        /// <summary>
        /// Gives the controls of one table row names that say which row they act on (APP-SETTINGS rule 10,
        /// APP-BEHAVIOUR rule 9): every row carries the same "Изменить..." / "Удалить" buttons and a
        /// caption-less switch, which a screen reader would otherwise announce identically for all rows.
        /// The rows are rebuilt in the current language, so the names are built from translated text.
        /// </summary>
        internal static void NameRow(Control row, string rowName)
        {
            foreach (Control control in row.Controls)
            {
                if (control is Button button)
                {
                    string verb = button.AccessibleName;
                    if (string.IsNullOrEmpty(verb)) verb = button.Text;
                    if (verb.Length > 0 && verb.Any(char.IsLetter)) button.AccessibleName = verb + " - " + rowName;
                }
                else if (control is CheckBox box && box.Text.Length == 0 && string.IsNullOrEmpty(box.AccessibleName))
                    box.AccessibleName = rowName;
            }
        }

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
            row.Controls.Add(Button(Translate("Удалить"), () => RemoveConversionProfile(profile)));
            NameRow(row, LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid) + " " + profile.Hotkey);
            return row;
        }

        // A row is a pair of layouts and a chord the user chose, and the window has no undo - so the
        // delete is confirmed like every other irreversible one (APP-BEHAVIOUR rule 5, safe answer default).
        private void RemoveConversionProfile(LayoutConversionProfile profile)
        {
            string pair = LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid);
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Удалить строку конвертации «{0}»? Отменить удаление нельзя.", pair),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            _config.LayoutConversionProfiles.Remove(profile);
            ConversionProfilesChanged?.Invoke(this, EventArgs.Empty);
            ReloadConversionRows();
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
            up.AccessibleName = Translate("Переместить вверх");
            AsMoveGlyph(up, AppGlyphs.MoveUp);
            // Only within one language: the order of the languages is Windows' own and is put back at
            // sign-in (ticket S0007, WL-8) - the hint under the table says so.
            up.Width = 32; up.Height = Math.Max(MinTarget, RowHeight); up.AutoSize = false; up.Margin = new Padding(3, 4, 3, 4); up.Enabled = InputLayouts.CanMove(all, index, -1);
            var down = Button("↓", () => MoveLayout(layout.Klid, +1));
            down.AccessibleName = Translate("Переместить вниз");
            AsMoveGlyph(down, AppGlyphs.MoveDown);
            down.Width = 32; down.Height = Math.Max(MinTarget, RowHeight); down.AutoSize = false; down.Margin = new Padding(3, 4, 3, 4); down.Enabled = InputLayouts.CanMove(all, index, +1);
            row.Controls.Add(up);
            row.Controls.Add(down);

            var makeDefault = Button(Translate("По умолчанию"), () => MakeDefaultLayout(layout.Klid));
            makeDefault.Enabled = !layout.IsDefault;
            row.Controls.Add(makeDefault);

            var remove = Button(Translate("Удалить"), () => OnRemoveLayout(layout));
            remove.Enabled = total > 1; // Windows always keeps at least one layout
            row.Controls.Add(remove);

            row.Controls.Add(new Label { Text = layout.Klid, AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(8, 5, 0, 0), Padding = new Padding(0, 1, 0, 0) });
            NameRow(row, layout.LanguageName + (layout.DisplayName.Length > 0 ? " - " + layout.DisplayName : ""));
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
            if (ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Удалить раскладку «{0}» из Windows?", layout.LanguageName + " — " + layout.DisplayName), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
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
            NameRow(row, LanguageHotkeys.LanguageName(hkl) + (layout.Length > 0 ? " - " + layout : ""));
            return row;
        }

        private Control OrphanHotkeyRow(LanguageHotkeys.Entry entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            Label orphanLabel = ColumnLabel(Localization.Format(Translate, "{0} → раскладка {1} (не установлена)", entry.Display, LanguageHotkeys.HklText(entry.TargetHkl)), Column(455));
            orphanLabel.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(orphanLabel);
            row.Controls.Add(Button(Translate("Удалить"), () => RemoveOrphanHotkey(entry.Id)));
            NameRow(row, entry.Display + " - " + LanguageHotkeys.HklText(entry.TargetHkl));
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
                Warn(Localization.Format(Translate, "Комбинация {0} уже занята горячей клавишей CyrFlip «{1}» — Windows её не получит.", chord.Display, taken));
                return;
            }

            // Capture the pre-CyrFlip state once, before the first write of this feature ever lands.
            if (!EnsureSystemBackups()) return;

            LanguageHotkeys.AssignStatus status = LanguageHotkeys.Assign(hkl, chord, out string conflict);
            switch (status)
            {
                case LanguageHotkeys.AssignStatus.ChordTaken:
                    Warn(Localization.Format(Translate, "Комбинация {0} уже назначена языку «{1}».", chord.Display, conflict));
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
            opacityLine.Controls.Add(new Label { Text = "Прозрачность окна истории:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 5, 0) });
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
                panel.Controls.Add(Group("clipboard/transfer", "Перенос на другой компьютер", transfer));
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

            panel.Controls.Add(Group("notes/opening", "Как открывать",
                Track(HotkeyRow(_quickNotesHotkeyEnabled, _quickNotesHotkeyValue, _setQuickNotesHotkey,
                    "Сразу создаёт новую заметку и ставит курсор в поле текста — записать мысль можно, ничего больше не нажимая. Одну комбинацию нельзя отдать двум действиям CyrFlip.")),
                Track(Setting(Button("Открыть заметки", _openQuickNotes),
                    "То же, что пункт «Быстрые заметки» в меню трея: открывает список без создания новой заметки."))));

            panel.Controls.Add(Group("notes/editor", "Редактор",
                Track(Setting(_quickNotesWrap,
                    "По умолчанию выключено: тело заметки чаще код, чем проза, а перенесённую строку кода приходится мысленно собирать обратно. Включите, если пишете здесь текст."))));

            panel.Controls.Add(Group("notes/storage", "Экспорт и хранение",
                Track(Setting(_quickNotesExportMeta,
                    "По умолчанию в экспорт идут только имя и текст: так фрагмент кода вставляется обратно без правок. С флажком к каждой заметке добавляется дата создания.")),
                Track(Setting(Button("Экспортировать все заметки в Markdown...", ExportQuickNotes),
                    "Один файл со всеми заметками по порядку. Экспортированный файл уже не защищён DPAPI — его прочитает любой, у кого есть доступ к папке.")),
                Track(Setting(Button("Удалить все быстрые заметки", ClearQuickNotes),
                    "Удаляет все заметки, журнал и его резервную копию. Отменить это нельзя."))));
            Control? transfer = ExchangeRow();
            if (transfer != null)
                panel.Controls.Add(Group("notes/transfer", "Перенос на другой компьютер", transfer));
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
                    Localization.Format(Translate, "Заметки сохранены: {0}", path) + "\n\n"
                    + Translate("Этот файл не защищён DPAPI — его прочитает любой, у кого есть доступ к папке."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn(Localization.Format(Translate, "Не удалось сохранить файл: {0}", FailureCause.Describe(ex, _config.UiLanguage)));
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

            panel.Controls.Add(Group("graphics/capture", "Снимок области экрана",
                HotkeyRow(_screenshotHotkeyEnabled, _screenshotHotkeyValue,
                    () => ScreenshotHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Экран замирает, вы выделяете прямоугольник мышью, и картинка сразу в буфере обмена. Esc или правая кнопка - отмена. Одну комбинацию нельзя отдать двум действиям CyrFlip."),
                Setting(Button("Сделать снимок области", () => ScreenshotRequested?.Invoke(this, EventArgs.Empty)),
                    "То же, что пункт в меню трея. Всплывающую подсказку или открытое меню поймает только комбинация: щелчок по трею их закрывает."),
                Setting(_screenshotSave,
                    "По умолчанию выключено: снимок попадает только в буфер обмена. С флажком каждый выделенный фрагмент также сохраняется как PNG в папку ниже."),
                ScreenshotFolderRow(),
                ScreenshotFolderHotkeyRow(),
                new Label
                {
                    Text = "Снимок всегда попадает в буфер обмена и не записывается в историю буфера. На диск он пишется, только если включено сохранение, и только выделенный фрагмент - в показанную папку (а если она недоступна - в «Изображения\\Снимки экрана» или «Загрузки», и CyrFlip сразу скажет, куда). Файлы не шифруются, CyrFlip их не удаляет и никуда не отправляет. Содержимое, которое защищает Windows (видео с DRM, окно UAC), получается чёрным.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 10, 3, 4),
                }));
            return page;
        }

        private FlowLayoutPanel ScreenshotFolderRow()
        {
            var folderRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(28, 0, 3, 3) };
            // The setting names itself: the box used to stand alone under the switch with no caption of its own
            // (APP-SETTINGS rule 3: caption, control and hint for every setting).
            var folderCaption = new Label { Text = "Папка для снимков области экрана", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 6, 0) };
            folderRow.Controls.Add(folderCaption);
            _screenshotFolderControls.Add(folderCaption);
            folderRow.Controls.Add(_screenshotFolderBox);
            Button choose = Button("Выбрать...", ChooseScreenshotFolder);
            Button open = Button("Открыть папку", OpenScreenshotFolder);
            folderRow.Controls.Add(choose);
            folderRow.Controls.Add(open);
            _screenshotFolderControls.Add(_screenshotFolderBox);
            _screenshotFolderControls.Add(choose);
            _screenshotFolderControls.Add(open);
            return folderRow;
        }

        /// <summary>
        /// The folder-opening chord's row (S0057), directly under the folder row and gated with it:
        /// greyed while saving is off, so the user sees why the chord does nothing (D2).
        /// </summary>
        private Control ScreenshotFolderHotkeyRow()
        {
            Control row = HotkeyRow(_screenshotFolderHotkeyEnabled, _screenshotFolderHotkeyValue,
                () => ScreenshotFolderHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                "Работает, пока включено сохранение снимков в папку. Открывает ту папку, в которую CyrFlip пишет снимки. Одну комбинацию нельзя отдать двум действиям CyrFlip.");
            // The whole row follows the folder row's gate; disabling the block disables everything in it.
            _screenshotFolderControls.Add(row);
            return row;
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
            // The chord and this button share the one opener (S0057), so they can never disagree
            // about the target - and the probe never runs on this, the keyboard hook's, thread.
            ScreenshotFolderOpenRequested?.Invoke(this, EventArgs.Empty);
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
            _screenshotFolderHotkeyEnabled.Checked = _config.EnableScreenshotFolderHotkey;
            _screenshotFolderHotkeyValue.Text = _config.ScreenshotFolderHotkey;
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

            panel.Controls.Add(Group("translate/ollama", "Ollama",
                Setting(LabeledRow("Адрес сервера:", _translateEndpoint),
                    "Пусто — http://localhost:11434, то есть Ollama на этом компьютере. Если указать другой адрес, выделенный текст будет отправлен на ту машину."),
                Setting(ModelRow(),
                    "Рекомендуемые: aya-expanse:8b (~4,7 ГБ, по умолчанию - лучший перевод из проверенных), gemma2:9b (~5 ГБ). Модели меньше 4 ГБ проверку на русском и украинском не прошли: gemma2:2b (~1,5 ГБ) берите, только если места мало, и ждите ошибок. Модель занимает память процесса Ollama, а не CyrFlip."),
                TranslateActionsRow(),
                _translateStatus,
                Setting(_translateAutoStart,
                    "Если сервер не отвечает в момент перевода, CyrFlip один раз попробует запустить его сам и подождёт до восьми секунд."),
                Setting(LabeledRow("Держать модель в памяти (мин):", _translateKeepAlive),
                    "Сколько Ollama держит модель загруженной после перевода. -1 держит её всегда, и это значение по умолчанию: первая загрузка на машине без подходящей видеокарты занимает минуты, и платить за неё заново каждые несколько минут хуже, чем занятые гигабайты. 0 выгружает сразу."),
                Setting(LabeledRow("Ждать загрузки модели не дольше (с):", _translateTimeout),
                    "Это время на первый ответ модели, то есть почти целиком на её загрузку в память. Дальше перевод идёт, пока модель пишет: он прерывается, только если она замолчит на 30 секунд, и тогда уже написанное остаётся на экране.")));

            panel.Controls.Add(Group("translate/directions", "Направления перевода",
                _translationRows,
                TranslateButton("Добавить направление...", AddTranslationProfile),
                new Label
                {
                    Text = "«Язык интерфейса» и «Язык активной раскладки» вычисляются в момент нажатия. Одну комбинацию нельзя отдать двум действиям CyrFlip.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 8, 3, 4),
                }));

            panel.Controls.Add(Group("translate/result", "Результат",
                Setting(_translateCopy,
                    "Перевод попадает в буфер обмена обычной записью — и, если история включена, сохраняется в ней, как любое другое копирование."),
                Setting(_translatePaste,
                    "Сразу заменяет выделение переводом. Если к моменту ответа фокус ушёл в другое окно, вставки не будет — окно предложит вставить вручную."),
                Setting(_translateShowSource,
                    "Показывает исходный текст в верхней части окна результата."),
                Setting(LabeledRow("Закрывать окно через (с):", _translateWindowTimeout),
                    "Отсчёт идёт с момента, когда перевод получен, и замирает, пока указатель мыши над окном. 0 — не закрывать по таймеру: окно всё равно закроется по Esc, по кнопке закрытия или когда вы перейдёте в другое окно.")));
            return page;
        }

        private FlowLayoutPanel ModelRow()
        {
            var modelRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            modelRow.Controls.Add(new Label { Text = "Модель:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 6, 0) });
            modelRow.Controls.Add(_translateModel);
            modelRow.Controls.Add(TranslateButton("Загрузить модель", PullTranslateModel));
            return modelRow;
        }

        private FlowLayoutPanel TranslateActionsRow()
        {
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            actions.Controls.Add(TranslateButton("Установить Ollama", InstallOllama));
            actions.Controls.Add(TranslateButton("Запустить Ollama", StartOllama));
            actions.Controls.Add(TranslateButton("Проверить связь", CheckOllama));
            // Live only while a download, a model pull or a start runs (S0037 TR-1): a stalled
            // download used to leave every button here disabled until CyrFlip was restarted.
            _translateCancel = Button("Отмена", CancelTranslateWork);
            _translateCancel.Enabled = false;
            actions.Controls.Add(_translateCancel);
            return actions;
        }

        /// <summary>A label and its control on one line; the label auto-sizes so no language clips it.</summary>
        private static Control LabeledRow(string label, Control control)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 6, 0) });
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
            row.Controls.Add(Button(Translate("Удалить"), () => RemoveTranslationProfile(profile)));
            NameRow(row, TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage) + " " + profile.Hotkey);
            return row;
        }

        // A row of the table is a language and a chord the user chose, and the window has no undo - so the
        // delete is confirmed like every other irreversible one (APP-BEHAVIOUR rule 5, safe answer default).
        private void RemoveTranslationProfile(TranslationProfile profile)
        {
            string label = TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage);
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Удалить строку перевода «{0}»? Отменить удаление нельзя.", label),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            _config.TranslateProfiles.Remove(profile);
            NotifyTranslationChanged();
            ReloadTranslationRows();
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

            panel.Controls.Add(Group("launcher/enable", "Включение",
                Setting(_launcherEnabled, "Добавляет подменю сценариев в меню трея и задачи в Jump List панели задач (правый клик по значку CyrFlip на панели задач). Сценарии хранятся по одному XML-файлу и не удаляются при выключении."),
                new Label { Text = "Значок на панели задач: левый клик — список сценариев, правый — Jump List Windows.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(31, 0, 3, 6) }));

            var searchRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            searchRow.Controls.Add(new Label { Text = "Поиск:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            searchRow.Controls.Add(_launcherSearch);

            var row1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row1.Controls.Add(LauncherButton("Добавить...", LauncherAdd));
            row1.Controls.Add(LauncherButton("Изменить...", LauncherEdit));
            row1.Controls.Add(LauncherButton("Запустить", LauncherRun));
            row1.Controls.Add(LauncherButton("Клонировать", LauncherClone));
            row1.Controls.Add(LauncherButton("Удалить", LauncherRemove));
            var up = LauncherButton("↑", () => LauncherMove(-1));
            up.AccessibleName = Translate("Переместить вверх");
            _launcherUp = up;
            AsMoveGlyph(up, AppGlyphs.MoveUp);
            var down = LauncherButton("↓", () => LauncherMove(+1));
            down.AccessibleName = Translate("Переместить вниз");
            _launcherDown = down;
            AsMoveGlyph(down, AppGlyphs.MoveDown);
            // Sized by their content, never below 32 px wide (the pointer floor of ICON-RENDER rule 5 is 28):
            // built here, before the window takes its real font, a fixed RowHeight was the old small font's
            // and cut the lower half of the arrow (S0019 audit A-16).
            // GrowOnly (a button's default) grows from the current size, so it starts from 32 x 0, not 75 x 23.
            up.AutoSize = down.AutoSize = true; up.MinimumSize = down.MinimumSize = up.Size = down.Size = new Size(32, MinTarget);
            row1.Controls.Add(up); row1.Controls.Add(down);

            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 0, 3, 2) };
            row2.Controls.Add(LauncherButton("Экспорт...", LauncherExport));
            row2.Controls.Add(LauncherButton("Импорт...", LauncherImport));
            _launcherImportOcr = LauncherButton("Импорт из OneClickRunner...", LauncherImportFromOcr);
            row2.Controls.Add(_launcherImportOcr);

            panel.Controls.Add(Group("launcher/scenarios", "Сценарии",
                searchRow,
                _launcherLoadErrors,
                _launcherList,
                row1,
                row2,
                new Label { Text = "Двойной клик или Enter — запуск, F2 — изменение, Delete — удаление. Импорт из OneClickRunner копирует сценарии и никогда не изменяет исходные файлы.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 4) }));
            _launcherList.SmallImageList = _launcherImages;

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
        /// The launcher page's brand mark (the icon the feature carried as a separate program). It
        /// rides alone on one row: the page title under it already says "Быстрый запуск", and a bold
        /// caption beside the mark used to duplicate it (WINDOWS-UI section 2 rule 3 - one title).
        /// A missing icon resource leaves nothing; the mark also lives on the taskbar button.
        /// </summary>
        private Control LauncherHeader()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            Bitmap? mark = LauncherBrand.GetImage(28);
            if (mark != null)
                row.Controls.Add(new PictureBox { Image = mark, SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(2, 0, 0, 0) });
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
                // The list lives inside its group (S0060 P5); the group's height includes the list's
                // own, so the container holding it is counted without the list, not skipped whole.
                Control? listHost = _launcherList.Parent;
                int used = panel.Padding.Vertical;
                foreach (Control control in panel.Controls)
                {
                    if (!control.Visible) continue;
                    if (ReferenceEquals(control, _launcherList)) continue;
                    int height = control.Height;
                    if (ReferenceEquals(control, listHost))
                        height = Math.Max(0, height - _launcherList.Height - _launcherList.Margin.Vertical);
                    used += height + control.Margin.Vertical;
                }

                // The group's own insets (body margin + group margin) stand between the page edge and
                // the list; the page panel still defines the available width.
                int groupInset = listHost!.Margin.Horizontal
                    + (listHost.Parent is Control group ? group.Margin.Horizontal : 0);
                _launcherList.Width = Math.Max(520, panel.ClientSize.Width - panel.Padding.Horizontal - groupInset - _launcherList.Margin.Horizontal);
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
            // The load-errors line and the table resize are one layout, not two (M1).
            if (_launcherPanel != null)
            {
                _launcherPanel.SuspendLayout();
                try
                {
                    _launcherLoadErrors.Visible = _launcherStore.LoadErrors.Count > 0;
                    LayoutLauncherList(_launcherPanel);
                }
                finally { _launcherPanel.ResumeLayout(true); }
            }
            else StretchLauncherColumns();

            if (_launcherLoadErrors.Visible || _launcherStore.LoadErrors.Count > 0)
                _launcherLoadErrors.Text = Localization.Format(Translate, "Не прочитано файлов: {0}", _launcherStore.LoadErrors.Count)
                    + " - " + string.Join(", ", _launcherStore.LoadErrors);

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
            clone.Name = Localization.Format(Translate, "{0} (копия)", selected.Name);
            _launcherStore.Add(clone);
            LauncherNotifyChanged();
            LauncherReselect(clone.Id);
        }

        private void LauncherRemove()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Удалить сценарий «{0}»?", selected.Name),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            string? failure = _launcherStore.Remove(selected.Id);
            if (failure != null)
            {
                Warn(Localization.Format(Translate, "Не удалось удалить сценарий «{0}»: {1}", selected.Name, failure));
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
                    Warn(Localization.Format(Translate, "Не удалось запустить «{0}»: {1}", name, result.ErrorMessage));
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
                ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Сценарий «{0}» экспортирован.", selected.Name),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn(Localization.Format(Translate, "Не удалось экспортировать: {0}", FailureCause.Describe(ex, _config.UiLanguage)));
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
                    : Localization.Format(Translate, "Не удалось импортировать: {0}", FailureCause.Describe(error, _config.UiLanguage)));
                return;
            }
            // A file exported on another machine can carry a chord that is taken here. One chord
            // belongs to one action, so it is dropped - the scenario itself still arrives.
            bool chordDropped = StripClashingHotkey(imported);
            LauncherNotifyChanged();
            LauncherReselect(imported.Id);
            string message = Localization.Format(Translate, "Импортирован сценарий «{0}».", imported.Name);
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
            string summary = Localization.Format(Translate, "Перенесено сценариев: {0}.", result.Imported);
            if (result.AlreadyPresent > 0)
                summary += "\n" + Localization.Format(Translate, "Уже перенесены ранее: {0}.", result.AlreadyPresent);
            if (result.Skipped.Count > 0)
                summary += "\n" + Localization.Format(Translate, "Пропущено повреждённых файлов: {0}.", result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + Localization.Format(Translate, "Из-за совпадения идентификаторов назначены новые: {0}.", result.NewIds);
            if (result.ChordsDropped > 0)
                summary += "\n" + Localization.Format(Translate, "Комбинации уже заняты, поэтому не перенесены: {0}.", result.ChordsDropped);
            ConfirmDialog.Show(this, _config.UiLanguage, summary, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private TabPage Page(string title, string description, params Control[] controls)
        {
            var page = new TabPage(title);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = ThemePalette.Light.SurfaceRaised };
            // A narrower window clamps the wrapping text blocks to what the page has (H3); re-clamped
            // on every resize, since the authored cap comes back when the window grows again.
            panel.Resize += (_, _) => ClampPageTextWidths();
            // The page opens with its title, then the one-line purpose (WINDOWS-UI section 2 rule 3).
            // Both live in the translatable tree: the title is a bold caption like a section header.
            // The title is shown by the header band (SyncPageHeader) and stays here hidden, so the language pass
            // translates it. The purpose line is the first line of the content pane (WINDOWS-UI section 2 rule 3),
            // not part of the band: the band must be the same height on every page or the page list loses rows.
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = BoldFont, Margin = new Padding(3, 0, 3, 2), UseMnemonic = false, Visible = false });
            panel.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(930, 0), ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(2, 0, 2, 10) });
            foreach (Control control in controls) panel.Controls.Add(control);
            page.Controls.Add(panel); return page;
        }
        private static FlowLayoutPanel ContentPanel(TabPage page) => (FlowLayoutPanel)page.Controls[0];
        private void BuildTransferPreferenceGroups()
        {
            if (_tabs == null) return;
            var ids = new Dictionary<string, string> {
                ["Общие"] = "general/preferences", ["Индикаторы"] = "indicators/preferences",
                ["Горячие клавиши"] = "hotkeys/preferences", ["Конвертация раскладок"] = "conversions/preferences",
                ["Буфер обмена"] = "clipboard/preferences", ["Контекстное меню"] = "context/preferences"
            };
            foreach (TabPage page in _tabs.TabPages)
            {
                if (!ids.TryGetValue(page.Text, out string? id)) continue;
                var panel = ContentPanel(page);
                var rows = panel.Controls.Cast<Control>().Skip(2).Where(c => !(c is CollapsibleGroup)).ToArray();
                if (rows.Length == 0) continue;
                foreach (Control row in rows) panel.Controls.Remove(row);
                var group = Group(id, "Параметры", rows); panel.Controls.Add(group); panel.Controls.SetChildIndex(group, 2);
            }
            FlattenSingleGroups();
        }

        /// <summary>
        /// One group on a page is not a grouping: it only repeats the page title above its own rows and
        /// adds a header to click. Such a group hands its rows to the page itself; a page with two or
        /// more groups keeps them all (WINDOWS-UI section 3 - the groups exist to tell parts of a page
        /// apart). The expand / collapse commands are unavailable on a page left with no group.
        /// </summary>
        private void FlattenSingleGroups()
        {
            if (_tabs == null) return;
            foreach (TabPage page in _tabs.TabPages)
            {
                FlowLayoutPanel panel = ContentPanel(page);
                CollapsibleGroup[] groups = panel.Controls.OfType<CollapsibleGroup>().ToArray();
                if (groups.Length != 1) continue;
                CollapsibleGroup group = groups[0];
                int index = panel.Controls.GetChildIndex(group);
                Control[] rows = group.Body.Controls.Cast<Control>().ToArray();
                panel.Controls.Remove(group);
                foreach (Control row in rows)
                {
                    group.Body.Controls.Remove(row);
                    panel.Controls.Add(row);
                    panel.Controls.SetChildIndex(row, index++);
                }
                _groups.Remove(group);
                group.Dispose();
            }
            UpdateHeaderCommands();
        }
        // The kinds are the numbers the pages were always given; the list itself has no slot 4 (the unused
        // "advanced" picture), so the kinds above it sit one place lower.
        public event EventHandler? WelcomeRequested;
        internal event Action<string>? SettingsTransferRequested;
        private Button _restoreSettings = null!;

        internal void SelectPage(SettingsPageKey key)
        {
            if (_tabs == null) return;
            foreach (TabPage page in _tabs.TabPages)
                if (Equals(page.Tag, key)) { _tabs.SelectedTab = page; return; }
        }

        private static TabPage WithIcon(TabPage page, int kind)
        {
            page.ImageIndex = IconIndex(kind);
            page.Tag = kind switch
            {
                0 => SettingsPageKey.General, 1 => SettingsPageKey.Indicators, 2 => SettingsPageKey.Hotkeys,
                3 => SettingsPageKey.Clipboard, 5 => SettingsPageKey.About, 6 => SettingsPageKey.WindowsLanguages,
                7 => SettingsPageKey.Conversions, 8 => SettingsPageKey.Launcher, 9 => SettingsPageKey.Translate,
                10 => SettingsPageKey.QuickNotes, 11 => SettingsPageKey.Graphics, 12 => SettingsPageKey.Symbols,
                13 => SettingsPageKey.HotkeysOverview,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return page;
        }
        internal static int IconIndex(int kind) => kind > 4 ? kind - 1 : kind;

        /// <summary>
        /// The vocabulary glyph a tab stands for (ICON-SET), one per page. Conversions is
        /// <c>action.convert-layout</c> and Languages <c>system.input-language</c> (both recorded in the
        /// catalog since 0.23 - the key cap with a globe, so it is not the globe of <c>system.internet</c>).
        /// The Graphics page takes <c>system.screenshot</c>: its one tool today is the region capture, and
        /// the record says "capture the screen as an image".
        /// </summary>
        internal static string? TabGlyphId(int kind)
        {
            switch (kind)
            {
                case 0: return AppGlyphs.Settings;
                case 1: return AppGlyphs.LayoutIndicator;
                case 2: return AppGlyphs.Shortcuts;
                case 3: return AppGlyphs.ClipboardHistory;
                case 5: return AppGlyphs.Info;
                case 6: return AppGlyphs.InputLanguage;
                case 7: return AppGlyphs.ConvertLayout;
                case 8: return AppGlyphs.QuickLaunch;
                case 9: return AppGlyphs.Translate;
                case 10: return AppGlyphs.Note;
                case 11: return AppGlyphs.Screenshot;
                case 12: return AppGlyphs.Symbols;
                case 13: return AppGlyphs.ShortcutOverview;
                default: return null;
            }
        }

        internal const int TabIconSize = 24;

        /// <summary>The icon's edge in device pixels at the given DPI: authored 20 logical, never smaller.</summary>
        internal static int TabIconSizeFor(int dpi) => ControlDpi.Scale(dpi, TabIconSize);

        internal static ImageList CreateTabIcons(Color ink) => CreateTabIcons(ink, 96);

        internal static ImageList CreateTabIcons(Color ink, int dpi) => CreateTabIcons(_ => ink, dpi);

        /// <summary>The page glyphs in each destination's identity ink (<see cref="ThemePalette.NavInk"/>); high
        /// contrast gets the text role for all of them.</summary>
        internal static ImageList CreateTabIcons(ThemePalette palette, int dpi) => CreateTabIcons(kind => ThemePalette.NavInk.For(kind, palette), dpi);

        private static ImageList CreateTabIcons(Func<int, Color> inkOf, int dpi)
        {
            int size = TabIconSizeFor(dpi);
            var images = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
            // The bitmaps stay owned by the ImageList: it holds them as "originals" until something
            // asks for its native handle, so disposing them here throws on the first paint. The list
            // frees whatever it still holds when the form disposes it (see Dispose) - which is why the
            // ImageList itself has to be kept: a control it is assigned to never owns it.
            images.Images.Add("general", TabIcon(0, inkOf(0), size)); images.Images.Add("indicators", TabIcon(1, inkOf(1), size)); images.Images.Add("hotkeys", TabIcon(2, inkOf(2), size));
            images.Images.Add("clipboard", TabIcon(3, inkOf(3), size)); images.Images.Add("about", TabIcon(5, inkOf(5), size));
            images.Images.Add("languages", TabIcon(6, inkOf(6), size));
            images.Images.Add("conversions", TabIcon(7, inkOf(7), size));
            images.Images.Add("launcher", TabIcon(8, inkOf(8), size));
            images.Images.Add("translate", TabIcon(9, inkOf(9), size));
            images.Images.Add("notes", TabIcon(10, inkOf(10), size));
            images.Images.Add("graphics", TabIcon(11, inkOf(11), size));
            images.Images.Add("symbols", TabIcon(12, inkOf(12), size));
            images.Images.Add("hotkeys-overview", TabIcon(13, inkOf(13), size));
            return images;
        }

        /// <summary>The page's vocabulary glyph. One that cannot be drawn leaves an empty canvas - the caption alone, never another meaning's picture (ICON-SET rule 1).</summary>
        internal static Bitmap TabIcon(int kind, Color ink) => TabIcon(kind, ink, TabIconSize);

        internal static Bitmap TabIcon(int kind, Color ink, int size)
        {
            string? id = TabGlyphId(kind);
            if (id != null)
            {
                Bitmap? glyph = GlyphRenderer.Render(id, size, ink);
                if (glyph != null) return glyph;
            }
            return new Bitmap(size, size);
        }

        private Control MarkerSizeRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Размер метки:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 5, 0) });
            row.Controls.Add(_markerSize);
            row.Controls.Add(_markerSizeValue);
            return row;
        }

        private string MarkerSizeName(int step)
            => Translate(step <= 0 ? "Маленькая" : step == 1 ? "Средняя" : "Крупная");

        private Control ThemeRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Тема:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
            row.Controls.Add(_themeCombo);
            return row;
        }

        /// <summary>
        /// Another entry of the theme drop-down was picked: store the invariant token and repaint every
        /// window at once (<c>APP-STYLE</c> section 2 - no restart, no "apply").
        /// </summary>
        private void ThemeChosen()
        {
            if (_loading || !(_themeCombo.SelectedItem is ThemeItem item)) return;
            _config.Theme = ThemeModes.Token(item.Mode);
            _config.Save();
            ThemeManager.SetMode(item.Mode);
        }

        /// <summary>
        /// Refill the theme dropdown in the current language, like <see cref="ReloadContextMenuChords"/>:
        /// the stored value is an invariant token, so switching the UI language re-labels the list
        /// without touching what is saved.
        /// </summary>
        private void ReloadThemeCombo()
        {
            bool loading = _loading;
            _loading = true;
            _themeCombo.Items.Clear();
            ThemeMode current = ThemeModes.Parse(_config.Theme);
            int select = 0;
            foreach (ThemeMode mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
            {
                int i = _themeCombo.Items.Add(new ThemeItem
                {
                    Mode = mode,
                    Label = Translate(mode == ThemeMode.Light ? "Светлая" : mode == ThemeMode.Dark ? "Тёмная" : "Как в Windows"),
                });
                if (mode == current) select = i;
            }
            _themeCombo.SelectedIndex = select;
            int max = 190;
            foreach (ThemeItem item in _themeCombo.Items)
            {
                int w = TextRenderer.MeasureText(item.Label, Font).Width + 36;
                if (w > max) max = w;
            }
            _themeCombo.Width = max;
            _themeCombo.DropDownWidth = max;
            _loading = loading;
        }

        private sealed class ThemeItem
        {
            public ThemeMode Mode;
            public string Label = "";
            public override string ToString() => Label;
        }

        private Control LanguageRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Язык интерфейса:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
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
            string line = Localization.Format(Translate, "Версия {0}", SupportBundle.AppVersion());
            return PackageInfo.IsPackaged ? line + " (Microsoft Store)" : line;
        }

        // ---- Symbols tab (S0056) ----

        private void SymbolsSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            SymbolsChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        private void ReloadSymbolsState()
        {
            _symbolsEnabled.Checked = _config.EnableSymbols;
            _symbolsHotkeyEnabled.Checked = _config.EnableSymbolsHotkey;
            _symbolsHotkeyValue.Text = _config.SymbolsHotkey;
            _symbolsHideTypable.Checked = _config.SymbolsHideTypable;
            foreach (Control control in _symbolsControls) control.Enabled = _config.EnableSymbols;
        }

        private Control TrackSymbols(Control control) { _symbolsControls.Add(control); return control; }

        private TabPage SymbolsPage()
        {
            var page = Page("Символы",
                "Палитра редких символов: клик копирует символ в буфер обмена; всё остаётся на этом компьютере.",
                Setting(_symbolsEnabled, "Пока выключено, окно символов не создаётся, в трее и контекстном меню нет пункта символов и комбинация не назначена."));
            var panel = ContentPanel(page);

            panel.Controls.Add(Group("symbols/opening", "Как открывать",
                TrackSymbols(HotkeyRow(_symbolsHotkeyEnabled, _symbolsHotkeyValue, () => SymbolsHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Открывает окно с фокусом в строке поиска. Одну комбинацию нельзя отдать двум действиям CyrFlip.")),
                TrackSymbols(Setting(Button("Открыть окно символов", () => OpenSymbolsRequested?.Invoke(this, EventArgs.Empty)),
                    "То же, что пункт «Символы» в меню трея."))));

            panel.Controls.Add(Group("symbols/options", "Настройки",
                TrackSymbols(Setting(_symbolsHideTypable,
                    "Скрывает из палитры символы, которые можно набрать одной клавишей или комбинацией с Shift или AltGr на установленных раскладках."))));

            panel.Controls.Add(new Label
            {
                Text = "Клик копирует символ в буфер обмена: символы накапливаются в поле текста окна, пока не включено «Закрыть по выбору». Выбор символов не добавляется в историю буфера обмена. Данные не покидают компьютер.",
                AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                Margin = new Padding(3, 10, 3, 4),
            });
            return page;
        }

        private TabPage AboutPage()
        {
            var page = Page("О программе", "CyrFlip - набор инструментов в трее для клавиатуры и рабочего стола: показывает активную раскладку у каретки, исправляет текст, набранный в неверной раскладке или с включённым CapsLock, и снимает область экрана; меню над выделением, локальный переводчик, быстрые заметки, история буфера и быстрый запуск - необязательные модули, которые вы включаете сами.\n\nПриложение не передаёт историю буфера в сеть. Если история включена, записи защищены Windows DPAPI и читаются только той же учётной записью Windows.");
            var panel = ContentPanel(page);
            // Two groups, so the page is a grouping (WINDOWS-UI section 3): what the product is, and
            // where help comes from.
            panel.Controls.Add(Group("about/product", "О программе",
                // Filled by ApplyLanguage, not here: it carries the version number, so a text set at
                // build time would be captured by RememberRussianTexts as if it were a Russian caption
                // and then "translated" on every language change. Starting empty keeps it out of that map.
                TrackAbout(_version, _version.Font = BoldFont),
                new Label { Text = "Разработчик: SerZhyAle", AutoSize = true, Font = BoldFont, Margin = new Padding(3, 14, 3, 4) },
                Link("Сайт программы: serzhyale.github.io/CyrFlip", "https://serzhyale.github.io/CyrFlip/"),
                Link("GitHub: github.com/SerZhyAle/CyrFlip", "https://github.com/SerZhyAle/CyrFlip"),
                Link("Сайт разработчика: sza.od.ua", "https://sza.od.ua/"),
                // APP-SETTINGS rule 12: the About page answers "what does the user need to ask for help" -
                // the licence of the application itself, and the privacy policy it keeps in 13 languages.
                Link("Лицензия: MIT", "https://github.com/SerZhyAle/CyrFlip/blob/main/LICENSE"),
                Link("Политика конфиденциальности", "https://serzhyale.github.io/CyrFlip/privacy.html"),
                // The licence notice of the vendored vocabulary glyphs (ICON-EXTERNAL rule 5, ticket S0022 A10);
                // the file itself also ships beside the exe and inside the MSIX.
                Link("Сторонние компоненты: значки Material Icons (Apache-2.0)", "https://github.com/SerZhyAle/CyrFlip/blob/main/THIRD-PARTY-NOTICES.txt"),
                new Label { Text = "CyrFlip работает локально, без телеметрии и сетевой синхронизации истории буфера.", AutoSize = true, MaximumSize = new Size(690, 0), Padding = new Padding(0, 14, 0, 0), ForeColor = ThemePalette.Light.TextMuted }));
            panel.Controls.Add(Group("about/support", "Помощь и поддержка",
                Setting(Button("Показать приветствие..", () => WelcomeRequested?.Invoke(this, EventArgs.Empty)),
                    "Знакомство с CyrFlip и первоначальная настройка."),
                Setting(Button("Диагностика положения каретки...", _diagnoseCaret), "Создаёт локальный отчёт о том, как Windows и UI Automation видят каретку. Нужен только если метка не появляется или рисуется не там в конкретной программе."),
                Setting(_sendLogs = Button("Отправить логи автору..", SendLogsToAuthor), "Собирает логи CyrFlip в один архив и открывает письмо автору с этим вложением. Письмо отправляете вы сами — CyrFlip ничего не передаёт в сеть. История буфера обмена в архив не попадает.")));
            return page;
        }

        /// <summary>A control of the About page, after a statement that only shapes it.</summary>
        private static Control TrackAbout(Control control, Font _) => control;

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
        private static Button Button(string text, Action action) { var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 5, 3, 5) }; button.Click += (_, _) => action(); return button; }
        /// <summary>The parts of a captioned value row (WINDOWS-UI section 4, "Ordinary row"): the caption and its
        /// description share the leading column, the editors stand in the trailing column of the same row.</summary>
        internal sealed class CaptionedRow
        {
            public CaptionedRow(TableLayoutPanel table, Label caption, Label hint, FlowLayoutPanel editors)
            { Table = table; Caption = caption; Hint = hint; Editors = editors; }
            public TableLayoutPanel Table { get; }
            public Label Caption { get; }
            public Label Hint { get; }
            public FlowLayoutPanel Editors { get; }
        }

        private const int CaptionedRowGap = 16;
        private int _editorColumn;

        private static int WidestEditors(Control container)
        {
            int widest = 0;
            foreach (Control child in container.Controls)
            {
                if (child is TableLayoutPanel { Tag: CaptionedRow row })
                {
                    row.Editors.MinimumSize = Size.Empty;
                    widest = Math.Max(widest, row.Editors.GetPreferredSize(Size.Empty).Width);
                }
                else if (child is CollapsibleGroup group) widest = Math.Max(widest, WidestEditors(group.Body));
            }
            return widest;
        }
        private const int CaptionedRowMaxLead = 900;

        private Control CaptionedSetting(FlowLayoutPanel editors, Label caption, string description)
        {
            editors.Controls.Remove(caption);
            caption.Padding = Padding.Empty;
            caption.Font = BoldFont;   // the caption is the row's name; the description is the muted line under it
            caption.Margin = new Padding(3, 7, 3, 2);
            caption.UseMnemonic = false;
            caption.MaximumSize = new Size(CaptionedRowMaxLead, 0);
            var hint = new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(CaptionedRowMaxLead, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 3) };
            var lead = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            lead.Controls.Add(caption);
            lead.Controls.Add(hint);
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(3, 7, 3, 7) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.Controls.Add(lead, 0, 0);
            table.Controls.Add(editors, 1, 0);
            table.Tag = new CaptionedRow(table, caption, hint, editors);
            return table;
        }

        /// <summary>The leading column takes what the editors leave (not less than <see cref="MinTextWidth"/>), and its
        /// text wraps inside it, so the row grows vertically instead of the editor leaving its side (WINDOWS-UI section 4).</summary>
        private void FitCaptionedRow(CaptionedRow row, int available)
        {
            // One editor column per page: every row of the page gives its editors the width of the widest, so
            // their left edges line up (WINDOWS-UI section 4: editors align to a shared page column).
            row.Editors.MinimumSize = Size.Empty;
            int own = row.Editors.GetPreferredSize(Size.Empty).Width;
            int editorWidth = Math.Max(own, _editorColumn) + row.Editors.Margin.Horizontal;
            row.Editors.MinimumSize = new Size(Math.Max(own, _editorColumn), 0);
            int lead = Math.Max(MinTextWidth, available - row.Table.Margin.Horizontal - editorWidth - CaptionedRowGap);
            lead = Math.Min(lead, CaptionedRowMaxLead);
            if ((int)row.Table.ColumnStyles[0].Width != lead) row.Table.ColumnStyles[0].Width = lead;
            int text = Math.Max(1, lead - row.Hint.Margin.Horizontal);
            if (row.Caption.MaximumSize.Width != text) row.Caption.MaximumSize = new Size(text, 0);
            if (row.Hint.MaximumSize.Width != text) row.Hint.MaximumSize = new Size(text, 0);
        }

        private Control Setting(Control control, string description)
        {
            if (control is FlowLayoutPanel captioned && captioned.FlowDirection == FlowDirection.LeftToRight
                && captioned.Controls.Count > 1 && captioned.Controls[0] is Label first && "caption".Equals(first.Tag))
            {
                Control table = CaptionedSetting(captioned, first, description);
                _rowHints[control] = ((CaptionedRow)table.Tag!).Hint;
                return table;
            }
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            if (control is CheckBox) control.Font = BoldFont;   // a switch's caption is the setting's name
            block.Controls.Add(control);
            Label hint = new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) };
            block.Controls.Add(hint);
            // A boolean row puts its description under the caption's own text inset, not under a fixed
            // guess (WINDOWS-UI section 4): the pair is re-measured whenever the font changes.
            if (control is CheckBox || control is RadioButton) _hintPairs.Add(new KeyValuePair<Control, Label>(control, hint));
            // The dependency pass (M5) appends the "why it is off" sentence to this hint when the row's
            // master switch is off - looked up by the control the row is about.
            _rowHints[control] = hint;
            return block;
        }

        /// <summary>
        /// A collapsible settings group (WINDOWS-UI section 3). Russian caption, not pre-translated -
        /// the header lives in the translatable tree like every other caption. The saved expansion is
        /// applied here, at build time, before any handler can see a change.
        /// </summary>
        private CollapsibleGroup Group(string id, string russianCaption, params Control[] rows)
        {
            var group = new CollapsibleGroup(id, russianCaption);
            foreach (Control row in rows) group.Body.Controls.Add(row);
            Dictionary<string, bool> states = ParseGroupState(_config.SettingsGroupsState);
            if (states.TryGetValue(id, out bool expanded)) group.Expanded = expanded;
            group.ExpandedChanged += (_, _) => { if (!_restoringContext && !_loading) SaveUiContext(); };
            _groups.Add(group);
            return group;
        }

        /// <summary>
        /// Re-align every boolean row's hint under its caption text and every page's caption column
        /// after the language (and so the font) changed: the insets and the widths are font-derived,
        /// so they are re-measured, not remembered (WINDOWS-UI section 4).
        /// </summary>
        private void AlignHintAndCaptionColumns()
        {
            foreach (KeyValuePair<Control, Label> pair in _hintPairs)
            {
                // The box and its hint share one block, so the box's own margin and padding are part
                // of where its caption text starts.
                int inset = pair.Key.Margin.Left + pair.Key.Padding.Left + CaptionTextInset(pair.Key);
                pair.Value.Margin = new Padding(inset, 0, 3, 3);
            }
            if (_tabs == null) return;
            foreach (TabPage page in _tabs.TabPages)
            {
                var captions = new List<Label>();
                int widest = 0;
                CollectCaptionLabels(ContentPanel(page), captions, ref widest);
                foreach (Label caption in captions)
                    caption.MinimumSize = new Size(Math.Max(caption.MinimumSize.Width, widest + 8), 0);
            }
        }

        private static void CollectCaptionLabels(Control root, List<Label> captions, ref int widest)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && "caption".Equals(label.Tag))
                {
                    label.MinimumSize = Size.Empty;   // re-measure from the current language
                    widest = Math.Max(widest, label.GetPreferredSize(Size.Empty).Width);
                    captions.Add(label);
                    continue;
                }
                if (child is CollapsibleGroup group) { CollectCaptionLabels(group.Body, captions, ref widest); continue; }
                if (child is FlowLayoutPanel flow) CollectCaptionLabels(flow, captions, ref widest);
            }
        }

        /// <summary>The distance from a checkbox/radio's left edge to where its caption text starts.</summary>
        internal static int CaptionTextInset(Control box)
        {
            try
            {
                using var graphics = box.CreateGraphics();
                Size glyph = box is RadioButton
                    ? RadioButtonRenderer.GetGlyphSize(graphics, System.Windows.Forms.VisualStyles.RadioButtonState.CheckedNormal)
                    : CheckBoxRenderer.GetGlyphSize(graphics, System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal);
                return glyph.Width + 4;
            }
            catch { return 20; }
        }
        private static Control HotkeyRow(string label, Label value, Action change, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            row.Controls.Add(new Label { Text = label + ":", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
            value.Padding = new Padding(0, 7, 5, 0); row.Controls.Add(value); row.Controls.Add(Button("Изменить...", change));
            block.Controls.Add(row);
            block.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) });
            return block;
        }

        // Variant with a leading enable checkbox: [☑ action]   chord   [Change...].
        private Control HotkeyRow(CheckBox enable, Label value, Action change, string description)
        {
            var block = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 7, 3, 7) };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            // AutoSize (never clip the caption at any DPI or in any language); the chord column is
            // lined up afterwards by AlignHotkeyCaptions, which widens all three to the widest one.
            enable.AutoSize = true; enable.Margin = new Padding(3, 8, 3, 3);
            row.Controls.Add(enable);
            value.Padding = new Padding(0, 7, 5, 0); row.Controls.Add(value); row.Controls.Add(Button("Изменить...", change));
            block.Controls.Add(row);
            Label hint = new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) };
            block.Controls.Add(hint);
            _rowHints[enable] = hint;   // the dependency pass appends the "why it is off" note here
            return block;
        }
        private void Changed(Action<bool> action, bool value) { if (_loading) return; action(value); Reload(); }

        /// <summary>The wheel arriving over this window's editors: turned into page scrolling and
        /// swallowed, so the pointer passing an editor cannot turn it (WINDOWS-UI section 4).</summary>
        internal bool ScrollPageWheel(Message m)
        {
            if (_tabs?.SelectedTab is not TabPage page) return false;
            FlowLayoutPanel flow = ContentPanel(page);
            int raw = (int)(((long)m.WParam >> 16) & 0xFFFF);
            int delta = raw >= 0x8000 ? raw - 0x10000 : raw;
            int lines = SystemInformation.MouseWheelScrollLines;
            int amount = lines < 0 ? flow.ClientSize.Height : Math.Max(1, lines) * Font.Height;
            ScrollPage(flow, Math.Max(0, -flow.AutoScrollPosition.Y - delta * amount / 120));
            return true;
        }

        /// <summary>
        /// The wheel must never turn an editor the pointer merely passes (WINDOWS-UI section 4):
        /// net48 routes <c>WM_MOUSEWHEEL</c> to the control under the cursor, so an unfocused combo
        /// box or numeric editor used to change while the user scrolled the page. While the settings
        /// window is open this filter turns such a wheel into page scrolling; an open drop-down list
        /// under the pointer still receives the wheel itself. Every other surface is untouched.
        /// </summary>
        internal sealed class SettingsWheelFilter : IMessageFilter
        {
            private readonly SettingsForm _owner;

            public SettingsWheelFilter(SettingsForm owner) { _owner = owner; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x20A || !_owner.Visible) return false;
                // FromChildHandle, not FromHandle: an editable combo's text box is a native child
                // window with no Control of its own, and the wheel over it reaches that child.
                Control? control = Control.FromChildHandle(m.HWnd);
                while (control != null && control is not (ComboBox or NumericUpDown or TrackBar))
                    control = control.Parent;
                if (control == null || control.FindForm() != _owner) return false;
                if (control is ComboBox combo && combo.DroppedDown
                    && combo.ClientRectangle.Contains(combo.PointToClient(Control.MousePosition)))
                    return false;
                return _owner.ScrollPageWheel(m);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Base first: it disposes the control tree, and the launcher ListView still points at
            // the ImageList while it does.
            if (disposing && _wheelFilter != null) Application.RemoveMessageFilter(_wheelFilter);
            base.Dispose(disposing);
            if (disposing)
            {
                _launcherIcons.Dispose();
                _launcherImages.Dispose();
                foreach (Bitmap bitmap in _launcherBitmaps) bitmap.Dispose();
                _tabIcons?.Dispose();   // assigned to the TabControl, which never owned it
                _glyphTips.Dispose();
                _boldFont?.Dispose();
                _titleFont?.Dispose();
                _ownFont?.Dispose();
                _ownIcon?.Dispose();
            }
        }
    }
}

