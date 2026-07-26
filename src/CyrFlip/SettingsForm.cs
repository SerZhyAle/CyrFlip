using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly Action _setCaseHotkey, _setHistoryHotkey, _openHistorySearch, _clearHistory, _diagnoseCaret;
        private readonly Action<bool> _setHotkeysEnabled, _setCaseEnabled, _setHistoryEnabled, _setDeferRdp;
        private readonly Action<bool> _setKeepAwake, _setKeepScreen;
        private readonly CheckBox _enableHotkeys = Check("Слушать глобальные горячие клавиши");
        private readonly CheckBox _caseEnabled = Check("Исправить CapsLock");
        private readonly CheckBox _historyEnabled = Check("Менеджер буфера");
        private readonly CheckBox _deferRdp = Check("Уступать хоткеи удалённому рабочему столу (mstsc/msrdc)");
        private readonly CheckBox _cursor = Check("Показывать раскладку на текстовом курсоре мыши");
        private readonly CheckBox _autostart = Check("Запускать CyrFlip вместе с Windows");
        private readonly CheckBox _keepAwake = Check("Не давать компьютеру засыпать");
        private readonly CheckBox _keepScreen = Check("Не блокировать экран (как при видео)");
        private readonly ComboBox _uiLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly CheckBox _caret = Check("Показывать метку раскладки рядом с кареткой");
        private readonly CheckBox _dot = Check("Компактная точка вместо букв EN/RU/UK");
        private readonly CheckBox _language = Check("Менять раскладку после конвертации текста");
        private readonly CheckBox _caps = Check("Синхронизировать CapsLock после исправления регистра");
        private readonly CheckBox _history = Check("Включить историю буфера");
        private readonly CheckBox _pause = Check("Приостановить захват истории");
        private readonly CheckBox _historyStartup = Check("Показывать окно менеджера буфера при запуске");
        private readonly TrackBar _opacity = new TrackBar { Minimum = 30, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 245 };
        private readonly Label _opacityValue = new Label { AutoSize = true };
        private readonly Label _caseHotkeyValue = new Label { AutoSize = true };
        private readonly Label _historyHotkeyValue = new Label { AutoSize = true };
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
        private readonly Label _launcherLoadErrors = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 60, 0), Margin = new Padding(3, 2, 3, 2), Visible = false };
        private readonly LauncherIconCache _launcherIcons = new LauncherIconCache();
        private readonly ImageList _launcherImages = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
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
        private readonly NumericUpDown _translateKeepAlive = new NumericUpDown { Minimum = 0, Maximum = 120, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly NumericUpDown _translateTimeout = new NumericUpDown { Minimum = 5, Maximum = 600, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly NumericUpDown _translateWindowTimeout = new NumericUpDown { Minimum = 0, Maximum = 600, Width = 70, Margin = new Padding(3, 4, 3, 4) };
        private readonly Label _translateStatus = new Label { AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6) };
        private readonly FlowLayoutPanel _translationRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly List<Button> _translateButtons = new List<Button>();
        // A model pull is minutes of streaming with no timeout by design; this is how the window
        // closing (or the app exiting) stops it instead of leaving the tab wedged on "busy".
        private CancellationTokenSource? _translateWork;
        private bool _translateBusy;
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

        public SettingsForm(AppConfig config,
            Action<bool> setAutostart, Action<bool> setCursor, Action<bool> setCaret, Action<bool> setDot, Action<bool> setLanguage, Action<bool> setCaps,
            Action<bool> setHistory, Action<bool> setPause, Action<bool> setHistoryStartup, Action<int> setOpacity, Action<string> setUiLanguage,
            Action setCaseHotkey, Action setHistoryHotkey, Action openHistorySearch, Action clearHistory, Action diagnoseCaret,
            Action<bool> setHotkeysEnabled, Action<bool> setCaseEnabled, Action<bool> setHistoryEnabled, Action<bool> setDeferRdp,
            Action<bool> setKeepAwake, Action<bool> setKeepScreen,
            LauncherScenarioStore launcherStore, Action<bool> setLauncherEnabled)
        {
            _config = config;
            _launcherStore = launcherStore;
            _setLauncherEnabled = setLauncherEnabled;
            _setAutostart = setAutostart; _setCursor = setCursor; _setCaret = setCaret; _setDot = setDot; _setLanguage = setLanguage; _setCaps = setCaps;
            _setHistory = setHistory; _setPause = setPause; _setHistoryStartup = setHistoryStartup; _setOpacity = setOpacity;
            _setUiLanguage = setUiLanguage;
            _setCaseHotkey = setCaseHotkey; _setHistoryHotkey = setHistoryHotkey; _openHistorySearch = openHistorySearch; _clearHistory = clearHistory; _diagnoseCaret = diagnoseCaret;
            _setHotkeysEnabled = setHotkeysEnabled; _setCaseEnabled = setCaseEnabled; _setHistoryEnabled = setHistoryEnabled; _setDeferRdp = setDeferRdp;
            _setKeepAwake = setKeepAwake; _setKeepScreen = setKeepScreen;

            Text = "Настройки CyrFlip"; StartPosition = FormStartPosition.CenterScreen; Size = DefaultWindowSize();
            MinimumSize = new Size(900, 620); ShowInTaskbar = true;
            try { Icon = _ownIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormClosing += (_, e) => { e.Cancel = true; Hide(); };

            // Vertical tab strip on the left: the captions are long in most of the 13 languages and a
            // horizontal strip either clipped them or wrapped into a second row that moved on click.
            // Left alignment draws the caption rotated unless we own-draw it - hence OwnerDrawFixed.
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill, Alignment = TabAlignment.Left, Multiline = true,
                SizeMode = TabSizeMode.Fixed, DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(34, 200),
            };
            _tabs = tabs;
            _tabIcons = CreateTabIcons();
            tabs.ImageList = _tabIcons;
            tabs.DrawItem += DrawTab;
            _uiLanguage.Items.AddRange(Localization.Names);
            tabs.TabPages.Add(WithIcon(Page("Общие", "Основные параметры приложения. Все изменения применяются сразу.",
                Setting(_autostart, "Добавляет CyrFlip в автозагрузку только текущего пользователя Windows. При следующем входе утилита запустится в фоне и появится в системном трее."),
                Setting(_keepAwake, "Пока включено, Windows не уходит в сон или гибернацию по простою — удобно для долгих загрузок, копирования или рендера. Действует только до выхода из CyrFlip: при следующем запуске опция снова выключена."),
                Setting(_keepScreen, "Экран не гаснет и не блокируется по бездействию, как во время просмотра видео. Блокировку по паролю (Win+L или политику безопасности) это не отменяет."),
                Setting(LanguageRow(), "Выберите язык интерфейса CyrFlip. Значение сохраняется вместе с остальными настройками и применяется также к меню в трее.")), 0));
            tabs.TabPages.Add(WithIcon(Page("Индикаторы", "Параметры, которые помогают увидеть активную раскладку до ввода текста.",
                Setting(_cursor, "Заменяет стандартный текстовый курсор I-beam на курсор с маленькой меткой EN, RU или UK. Обычная стрелка мыши не меняется."),
                Setting(_caret, "Рисует небольшую метку рядом с мигающей кареткой в поле ввода. Работает в приложениях, которые передают Windows положение каретки."),
                Setting(_dot, "Вместо букв EN/RU/UK рядом с кареткой показывает компактную цветную точку — удобно, если буквы отвлекают."),
                Setting(_language, "После конвертации текста переключает раскладку активного окна на ту, в которой текст теперь набран, чтобы можно было сразу продолжить печатать."),
                Setting(_caps, "После исправления регистра меняет физическое состояние CapsLock, чтобы следующие нажатия соответствовали исправленному тексту.")), 1));
            tabs.TabPages.Add(WithIcon(Page("Горячие клавиши", "Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows. Каждый хоткей можно включить или отключить отдельно.",
                Setting(_enableHotkeys, "Общий выключатель всех горячих клавиш. Когда снят, CyrFlip не перехватывает ни одной комбинации — клавиши проходят в приложение как обычно."),
                HotkeyRow(_caseEnabled, _caseHotkeyValue, _setCaseHotkey, "Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock."),
                HotkeyRow(_historyEnabled, _historyHotkeyValue, _setHistoryHotkey, "Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию."),
                Setting(_deferRdp, "Когда в фокусе окно клиента удалённого рабочего стола (mstsc/msrdc), CyrFlip не перехватывает хоткеи — клавиша уходит в удалённый сеанс, где её обработает CyrFlip на той машине. Включите, если утилита запущена на обеих сторонах RDP."),
                new Label { Text = "Здесь только эти два хоткея. Все комбинации, которые конвертируют текст из одной раскладки в другую — включая EN ⇄ RU на Ctrl+Shift+F12 — живут одной таблицей на вкладке «Конвертация раскладок».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 4) }), 2));
            tabs.TabPages.Add(WithIcon(ConversionsPage(), 7));
            tabs.TabPages.Add(WithIcon(LanguagesPage(), 6));
            tabs.TabPages.Add(WithIcon(ClipboardPage(), 3));
            tabs.TabPages.Add(WithIcon(TranslatePage(), 9));
            tabs.TabPages.Add(WithIcon(LauncherPage(), 8));
            tabs.TabPages.Add(WithIcon(AboutPage(), 5));
            Controls.Add(tabs);

            // Come back to the page the user was last on. Restored before subscribing so the restore
            // itself doesn't count as a change, and clamped: the tab strip grows between versions.
            if (_config.SettingsTab > 0 && _config.SettingsTab < tabs.TabPages.Count)
                tabs.SelectedIndex = _config.SettingsTab;
            tabs.SelectedIndexChanged += (_, _) => _config.SaveSettingsTab(tabs.SelectedIndex);

            _cursor.CheckedChanged += (_, _) => Changed(_setCursor, _cursor.Checked);
            _autostart.CheckedChanged += (_, _) => Changed(_setAutostart, _autostart.Checked);
            _keepAwake.CheckedChanged += (_, _) => Changed(_setKeepAwake, _keepAwake.Checked);
            _keepScreen.CheckedChanged += (_, _) => Changed(_setKeepScreen, _keepScreen.Checked);
            _caret.CheckedChanged += (_, _) => Changed(_setCaret, _caret.Checked);
            _dot.CheckedChanged += (_, _) => Changed(_setDot, _dot.Checked);
            _language.CheckedChanged += (_, _) => Changed(_setLanguage, _language.Checked);
            _caps.CheckedChanged += (_, _) => Changed(_setCaps, _caps.Checked);
            _history.CheckedChanged += (_, _) => Changed(_setHistory, _history.Checked);
            _pause.CheckedChanged += (_, _) => Changed(_setPause, _pause.Checked);
            _historyStartup.CheckedChanged += (_, _) => Changed(_setHistoryStartup, _historyStartup.Checked);
            _enableHotkeys.CheckedChanged += (_, _) => Changed(_setHotkeysEnabled, _enableHotkeys.Checked);
            _caseEnabled.CheckedChanged += (_, _) => Changed(_setCaseEnabled, _caseEnabled.Checked);
            _historyEnabled.CheckedChanged += (_, _) => Changed(_setHistoryEnabled, _historyEnabled.Checked);
            _deferRdp.CheckedChanged += (_, _) => Changed(_setDeferRdp, _deferRdp.Checked);
            _restoreLanguageHotkeys.Click += (_, _) => RestoreLanguageHotkeys();
            _restoreLayouts.Click += (_, _) => RestoreLayouts();
            _toggleCombo.SelectedIndexChanged += (_, _) => OnToggleChanged();
            _uiLanguage.SelectedIndexChanged += (_, _) => { if (!_loading && _uiLanguage.SelectedItem is string language) { _setUiLanguage(language); ApplyLanguage(); } };
            _opacity.ValueChanged += (_, _) => { if (!_loading) { _opacityValue.Text = _opacity.Value + "%"; _setOpacity(_opacity.Value); } };

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
            Reload();
            RememberRussianTexts(this);
            ApplyLanguage();
        }

        public void Reload()
        {
            _loading = true;
            _autostart.Checked = Autostart.IsEnabled;
            // Keep-awake state lives only in memory (never persisted), so mirror the live values.
            _keepAwake.Checked = KeepAwake.KeepSystemAwake;
            _keepScreen.Checked = KeepAwake.KeepScreenOn;
            _uiLanguage.SelectedItem = _config.UiLanguage;
            if (_uiLanguage.SelectedIndex < 0) _uiLanguage.SelectedIndex = 0;
            _cursor.Checked = _config.EnableCursorChange; _caret.Checked = _config.EnableCaretOverlay; _dot.Checked = _config.CaretDotMode;
            _language.Checked = _config.EnableLanguageSwitch; _caps.Checked = _config.FlipCapsLockAfter;
            _history.Checked = _config.EnableClipboardHistory; _pause.Checked = _config.PauseClipboardHistory;
            _historyStartup.Checked = _config.ShowClipboardHistoryOnStartup;
            _opacity.Value = Math.Max(_opacity.Minimum, Math.Min(_opacity.Maximum, _config.ClipboardHistoryOpacity));
            _opacityValue.Text = _opacity.Value + "%";
            _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
            _pause.Enabled = _history.Checked; _historyStartup.Enabled = _history.Checked; _opacity.Enabled = _history.Checked;
            _enableHotkeys.Checked = _config.EnableHotkeys;
            _caseEnabled.Checked = _config.EnableCaseHotkey; _historyEnabled.Checked = _config.EnableHistoryHotkey;
            _deferRdp.Checked = _config.DeferToRemoteDesktop;
            // The per-hotkey switches only matter while the master switch is on.
            _caseEnabled.Enabled = _historyEnabled.Enabled = _enableHotkeys.Checked;
            _launcherEnabled.Checked = _config.EnableScenarioLauncher;
            ReloadTranslateState();
            _loading = false;
            ApplyLanguage();
        }

        private void RememberRussianTexts(Control root)
        {
            // The language-hotkey rows are thrown away and rebuilt on every refresh, already in the
            // current language. Registering them would both leak dead Control keys into the map and
            // capture translated text as if it were the Russian original.
            if (ReferenceEquals(root, _languageRows) || ReferenceEquals(root, _layoutRows)
                || ReferenceEquals(root, _conversionRows) || ReferenceEquals(root, _translationRows)) return;

            // ComboBox.Text is its selected value, not a UI caption. Translating it would reset
            // the language selector back to the first Russian value on every UI refresh. Same for
            // NumericUpDown, whose Text is the number the user just typed, and for every text box:
            // the translator's server address is filled from the config before this walk runs, so a
            // non-default address would be captured as if it were a Russian caption and restored on
            // top of the user's edit at the next language change.
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
            AlignHotkeyCaptions();
            AdjustTabStrip();
            // Built in the current language, so these must run after the translation pass above.
            ReloadLayoutRows();
            ReloadLanguageRows();
            ReloadConversionRows();
            ReloadTranslationRows();
            ReloadToggleCombo();
            ReloadLauncherRows();
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
            CheckBox[] boxes = { _caseEnabled, _historyEnabled };
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
            _tabs.ItemSize = new Size(rowHeight, Math.Max(170, Math.Min(360, widest + 60)));
        }

        /// <summary>Owner-draws one left-aligned tab: icon + horizontal caption, the selected one bold
        /// with an accent bar, so the strip reads as a navigation list rather than a hairline.</summary>
        private void DrawTab(object? sender, DrawItemEventArgs e)
        {
            if (_tabs == null || e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
            TabPage page = _tabs.TabPages[e.Index];
            bool selected = _tabs.SelectedIndex == e.Index;
            Rectangle bounds = e.Bounds;
            using (var back = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
                e.Graphics.FillRectangle(back, bounds);
            if (selected)
                using (var accent = new SolidBrush(Color.FromArgb(45, 105, 175)))
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
                    selected ? SystemColors.WindowText : SystemColors.ControlText, flags);
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
            string wanted = family ?? SystemFonts.MessageBoxFont.FontFamily.Name;
            if (Font.FontFamily.Name == wanted) return;
            try
            {
                // Both fonts are ours: WinForms disposes neither the font assigned to a control nor the
                // one it replaces. Release the outgoing pair only after every control has been moved to
                // the incoming one - a control drawing with a disposed font throws on the next paint.
                Font? previousOwn = _ownFont;
                Font? previousBold = _boldFont;
                Font = _ownFont = new Font(wanted, SystemFonts.MessageBoxFont.SizeInPoints);
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
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = Color.FromArgb(150, 60, 0), Margin = new Padding(3, 0, 3, 10),
                });

            panel.Controls.Add(SectionHeader("Раскладки клавиатуры"));
            panel.Controls.Add(_layoutRows);
            var layoutButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 4, 3, 2) };
            layoutButtons.Controls.Add(Button("Добавить раскладку...", OnAddLayout));
            layoutButtons.Controls.Add(Button("Добавить популярные языки", AddPopularLayouts));
            layoutButtons.Controls.Add(_restoreLayouts);
            panel.Controls.Add(layoutButtons);
            panel.Controls.Add(new Label { Text = "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian и Ukrainian. Для языков с популярными вариантами (например, US International, Spanish Latin American) используйте «Добавить раскладку...».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 5) });

            panel.Controls.Add(SectionHeader("Переключение по кругу"));
            var toggleRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            toggleRow.Controls.Add(new Label { Text = "Сочетание для перебора языков:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            toggleRow.Controls.Add(_toggleCombo);
            panel.Controls.Add(Setting(toggleRow, "Одно сочетание перебирает установленные языки по кругу. Это штатная настройка Windows (Alt+Shift, Ctrl+Shift или «`»); «—» отключает перебор."));

            panel.Controls.Add(SectionHeader("Прямые сочетания на язык"));
            panel.Controls.Add(new Label { Text = "Эти сочетания обрабатывает сама Windows: они работают, даже когда CyrFlip закрыт. Комбинацию вы выбираете сами — в отличие от штатного окна, здесь не только Ctrl+Shift+цифра.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 4) });
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
            panel.Controls.Add(new Label { Text = "Работают только установленные в Windows раскладки — добавьте нужные на вкладке «Языки Windows». Одну комбинацию нельзя отдать двум действиям CyrFlip.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 4) });
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
            _layoutRows.SuspendLayout();
            var old = new Control[_layoutRows.Controls.Count];
            _layoutRows.Controls.CopyTo(old, 0);
            _layoutRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();

            List<InputLayouts.Installed> installed = InputLayouts.ListInstalled();
            RefreshLayoutNames(installed);
            for (int i = 0; i < installed.Count; i++)
                _layoutRows.Controls.Add(LayoutRow(installed[i], i, installed.Count));

            _layoutRows.ResumeLayout();
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
                    AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(25, 6, 3, 4),
                });
            _conversionRows.ResumeLayout();
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
            var enabled = new CheckBox { Text = "", Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 6, 2, 2) };
            enabled.CheckedChanged += (_, _) => { if (_loading) return; profile.Enabled = enabled.Checked; ConversionProfilesChanged?.Invoke(this, EventArgs.Empty); };
            row.Controls.Add(enabled);
            // "⇄", not "→": the pair converts in whichever direction the active layout implies.
            Label pair = ColumnLabel(LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid), _pairColumn);
            // A row is only live while the master switch is on, so say so instead of promising a chord.
            if (!_config.EnableHotkeys) pair.ForeColor = SystemColors.GrayText;
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
            string? conflict = ClashingCyrFlipAction(Hotkey.Parse(profile.Hotkey), existing?.Id);
            if (conflict != null) { Warn(string.Format(Translate("Комбинация {0} уже занята действием «{1}" + "»."), profile.Hotkey, conflict)); return; }
            if (existing == null) _config.LayoutConversionProfiles.Add(profile);
            else
            {
                int index = _config.LayoutConversionProfiles.IndexOf(existing);
                if (index >= 0) _config.LayoutConversionProfiles[index] = profile;
            }
            ConversionProfilesChanged?.Invoke(this, EventArgs.Empty); ReloadConversionRows();
        }

        private Control LayoutRow(InputLayouts.Installed layout, int index, int total)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            string name = layout.LanguageName + (layout.DisplayName.Length > 0 ? " — " + layout.DisplayName : "");
            if (layout.IsDefault) name = "★ " + name;
            Label nameLabel = ColumnLabel(name, Column(330));
            nameLabel.Font = layout.IsDefault ? BoldFont : Font;
            row.Controls.Add(nameLabel);

            var up = Button("↑", () => { InputLayouts.Move(layout.Klid, -1); LanguageHotkeys.ApplyToWindows(); ReloadLayoutRows(); });
            up.Width = 32; up.Height = RowHeight; up.AutoSize = false; up.Margin = new Padding(3, 4, 3, 4); up.Enabled = index > 0;
            var down = Button("↓", () => { InputLayouts.Move(layout.Klid, +1); LanguageHotkeys.ApplyToWindows(); ReloadLayoutRows(); });
            down.Width = 32; down.Height = RowHeight; down.AutoSize = false; down.Margin = new Padding(3, 4, 3, 4); down.Enabled = index < total - 1;
            row.Controls.Add(up);
            row.Controls.Add(down);

            var makeDefault = Button(Translate("По умолчанию"), () => { InputLayouts.MakeDefault(layout.Klid); LanguageHotkeys.ApplyToWindows(); ReloadLayoutRows(); });
            makeDefault.Enabled = !layout.IsDefault;
            row.Controls.Add(makeDefault);

            var remove = Button(Translate("Удалить"), () => OnRemoveLayout(layout));
            remove.Enabled = total > 1; // Windows always keeps at least one layout
            row.Controls.Add(remove);

            row.Controls.Add(new Label { Text = layout.Klid, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 5, 0, 0), Padding = new Padding(0, 1, 0, 0) });
            return row;
        }

        private void OnAddLayout()
        {
            var installedKlids = new List<string>();
            foreach (InputLayouts.Installed i in InputLayouts.ListInstalled()) installedKlids.Add(i.Klid);

            using var picker = new LayoutPickerDialog(installedKlids,
                Translate("Добавить раскладку"), Translate("Введите язык или название раскладки для фильтра"),
                Translate("Добавить"), Translate("Отмена"));
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKlid == null) return;

            BackupLayoutsOnce();
            InputLayouts.Add(picker.SelectedKlid);
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
            BackupLayoutsOnce();
            foreach (WorldLayouts.Recommended language in WorldLayouts.Popular)
            {
                string klid = language.Klids[0];
                if (!installed.Contains(klid) && available.Contains(klid)) { InputLayouts.Add(klid); installed.Add(klid); added++; }
            }
            LanguageHotkeys.ApplyToWindows(); Reload();
            if (added > 0) NotifyLayoutChangeOnce();
        }

        private void OnRemoveLayout(InputLayouts.Installed layout)
        {
            if (MessageBox.Show(this, string.Format(Translate("Удалить раскладку «{0}» из Windows?"), layout.LanguageName + " — " + layout.DisplayName), "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            BackupLayoutsOnce();
            if (!InputLayouts.Remove(layout.Klid))
            {
                Warn(Translate("Windows должна оставить хотя бы одну раскладку — эту удалить нельзя."));
                return;
            }
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        private void OnToggleChanged()
        {
            if (_loading || !(_toggleCombo.SelectedItem is ToggleItem item)) return;
            LanguageHotkeys.SetToggle(item.Code);
            LanguageHotkeys.ApplyToWindows();
        }

        private void ReloadToggleCombo()
        {
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
            _loading = false;
        }

        private sealed class ToggleItem
        {
            public string Code = "";
            public string Label = "";
            public override string ToString() => Label;
        }

        /// <summary>Capture the pre-CyrFlip layout state once, before this feature's first write lands.</summary>
        private void BackupLayoutsOnce()
        {
            if (_config.InputLayoutsBackup.Length == 0)
            {
                _config.InputLayoutsBackup = InputLayouts.BackupAll();
                _config.Save();
            }
        }

        private void NotifyLayoutChangeOnce()
        {
            if (_layoutNoticeShown) return;
            _layoutNoticeShown = true;
            MessageBox.Show(this, Translate("Изменение применено к раскладкам Windows. Обычно оно вступает в силу сразу; если что-то выглядит не так, выйдите из Windows и войдите снова."),
                "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestoreLayouts()
        {
            if (_config.InputLayoutsBackup.Length == 0) return;
            if (MessageBox.Show(this, Translate("Вернуть раскладки Windows в исходное состояние?"), "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            InputLayouts.RestoreAll(_config.InputLayoutsBackup);
            Reload();
        }

        /// <summary>Rebuilds the per-layout rows from the registry. Cheap enough to run on every refresh.</summary>
        private void ReloadLanguageRows()
        {
            _languageRows.SuspendLayout();

            var old = new Control[_languageRows.Controls.Count];
            _languageRows.Controls.CopyTo(old, 0);
            _languageRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();

            List<LanguageHotkeys.Entry> entries = LanguageHotkeys.ReadAll();
            var installed = new List<IntPtr>(LanguageHotkeys.InstalledLayouts());

            foreach (IntPtr hkl in installed)
                _languageRows.Controls.Add(LanguageHotkeyRow(hkl, entries.Find(e => e.TargetHkl == hkl)));

            // Assignments pointing at a layout that is no longer installed. Windows ships some of
            // these out of the box, so they are surfaced for removal rather than deleted for the user.
            foreach (LanguageHotkeys.Entry entry in entries)
                if (!installed.Contains(entry.TargetHkl))
                    _languageRows.Controls.Add(OrphanHotkeyRow(entry));

            _languageRows.ResumeLayout();
            _restoreLanguageHotkeys.Enabled = _config.LanguageHotkeysBackup.Length > 0;
        }

        private Control LanguageHotkeyRow(IntPtr hkl, LanguageHotkeys.Entry? entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            string layout = LanguageHotkeys.LayoutName(hkl);
            row.Controls.Add(ColumnLabel(LanguageHotkeys.LanguageName(hkl) + (layout.Length > 0 ? " — " + layout : ""), Column(290)));

            Label valueLabel = ColumnLabel(entry != null ? entry.Display : Translate("Не назначено"), Column(165), ellipsis: false);
            valueLabel.Font = entry != null ? BoldFont : Font;
            valueLabel.ForeColor = entry != null ? ForeColor : SystemColors.GrayText;
            row.Controls.Add(valueLabel);
            row.Controls.Add(Button(Translate("Задать..."), () => AssignLanguageHotkey(hkl)));

            Button clear = Button(Translate("Очистить"), () =>
            {
                LanguageHotkeys.Clear(hkl);
                LanguageHotkeys.ApplyToWindows();
                ReloadLanguageRows();
            });
            clear.Enabled = entry != null;
            row.Controls.Add(clear);

            // The raw HKL is the only always-correct way to tell two layouts of one language apart.
            row.Controls.Add(new Label { Text = LanguageHotkeys.HklText(hkl), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 5, 0, 0), Padding = new Padding(0, 1, 0, 0) });
            return row;
        }

        private Control OrphanHotkeyRow(LanguageHotkeys.Entry entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            Label orphanLabel = ColumnLabel(string.Format(Translate("{0} → раскладка {1} (не установлена)"), entry.Display, LanguageHotkeys.HklText(entry.TargetHkl)), Column(455));
            orphanLabel.ForeColor = SystemColors.GrayText;
            row.Controls.Add(orphanLabel);
            row.Controls.Add(Button(Translate("Удалить"), () =>
            {
                LanguageHotkeys.Remove(entry.Id);
                LanguageHotkeys.ApplyToWindows();
                ReloadLanguageRows();
            }));
            return row;
        }

        private void AssignLanguageHotkey(IntPtr hkl)
        {
            LanguageHotkeys.Entry? current = LanguageHotkeys.FindFor(hkl);
            using var dialog = new HotkeyDialog(current?.Display ?? "", Translate("Сочетание для переключения на язык"), _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedHotkey == null) return;

            var chord = Hotkey.Parse(dialog.CapturedHotkey);

            // CyrFlip's own hook swallows its chords, so Windows would never see one assigned here.
            string? taken = ClashingCyrFlipAction(chord);
            if (taken != null)
            {
                Warn(string.Format(Translate("Комбинация {0} уже занята горячей клавишей CyrFlip «{1}» — Windows её не получит."), chord.Display, taken));
                return;
            }

            // Capture the pre-CyrFlip state once, before the first write of this feature ever lands.
            if (_config.LanguageHotkeysBackup.Length == 0)
            {
                _config.LanguageHotkeysBackup = LanguageHotkeys.BackupAll();
                _config.Save();
            }

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
                MessageBox.Show(this, Translate("Сочетание записано в настройки Windows — обрабатывать его будет система, а не CyrFlip. Если оно не сработало сразу, выйдите из Windows и войдите снова."),
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RestoreLanguageHotkeys()
        {
            if (_config.LanguageHotkeysBackup.Length == 0) return;
            if (MessageBox.Show(this, Translate("Вернуть языковые сочетания Windows в исходное состояние?"), "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            LanguageHotkeys.RestoreAll(_config.LanguageHotkeysBackup);
            ReloadLanguageRows();
        }

        /// <summary>The CyrFlip action that would eat this chord before Windows saw it, or null.</summary>
        private string? ClashingCyrFlipAction(Hotkey chord, string? ignoreConversionId = null, Guid? ignoreScenarioId = null,
            string? ignoreTranslationId = null)
        {
            if (!_config.EnableHotkeys) return null; // hook passes every key through
            if (_config.EnableCaseHotkey && chord.SameChord(Hotkey.Parse(_config.CaseHotkey))) return Translate("Исправить CapsLock");
            if (_config.EnableHistoryHotkey && chord.SameChord(Hotkey.Parse(_config.ClipboardHistoryHotkey))) return Translate("Менеджер буфера");
            foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
                if (profile.Id != ignoreConversionId && profile.Enabled && profile.IsUsable && chord.SameChord(Hotkey.Parse(profile.Hotkey)))
                    return LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid);
            // Launcher scenario chords are live only while the launcher is on; while off they are inert.
            if (_config.EnableScenarioLauncher)
                foreach (LauncherScenario scenario in _launcherStore.All)
                    if (scenario.Id != ignoreScenarioId && scenario.Hotkey.Length > 0
                        && Hotkey.TryParse(scenario.Hotkey, out Hotkey bound) && chord.SameChord(bound))
                        return scenario.Name;
            // Translation chords are live only while the translator is on; while off they are inert.
            if (_config.EnableTranslate)
                foreach (TranslationProfile profile in _config.TranslateProfiles)
                    if (profile.Id != ignoreTranslationId && profile.Enabled && profile.IsUsable
                        && Hotkey.TryParse(profile.Hotkey, out Hotkey chordBound) && chord.SameChord(chordBound))
                        return Translate("Перевод") + ": " + TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage);
            return null;
        }

        private void Warn(string message)
            => MessageBox.Show(this, message, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
            panel.Controls.Add(Setting(Button("Очистить всю историю", () => { if (MessageBox.Show(Translate("Удалить всю сохранённую историю буфера?"), "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) _clearHistory(); }), "Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить."));
            return page;
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
                "Рекомендуемые: qwen2.5:3b (~2 ГБ, по умолчанию), gemma2:2b (~1,6 ГБ, для слабых машин), llama3.2:3b (~2 ГБ), aya-expanse:8b (~5 ГБ, заметно лучше переводит, нужно от 8 ГБ ОЗУ). Модель занимает память процесса Ollama, а не CyrFlip."));

            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            actions.Controls.Add(TranslateButton("Установить Ollama", InstallOllama));
            actions.Controls.Add(TranslateButton("Запустить Ollama", StartOllama));
            actions.Controls.Add(TranslateButton("Проверить связь", CheckOllama));
            panel.Controls.Add(actions);
            panel.Controls.Add(_translateStatus);

            panel.Controls.Add(Setting(_translateAutoStart,
                "Если сервер не отвечает в момент перевода, CyrFlip один раз попробует запустить его сам и подождёт до восьми секунд."));
            panel.Controls.Add(Setting(LabeledRow("Держать модель в памяти (мин):", _translateKeepAlive),
                "Сколько Ollama держит модель загруженной после перевода. Больше — следующий перевод почти мгновенный, но несколько гигабайт памяти заняты. 0 — выгружать сразу."));
            panel.Controls.Add(Setting(LabeledRow("Ждать ответа не дольше (с):", _translateTimeout),
                "Если модель не ответила за это время, перевод прерывается и окно предлагает повторить."));

            panel.Controls.Add(SectionHeader("Направления перевода"));
            panel.Controls.Add(_translationRows);
            panel.Controls.Add(TranslateButton("Добавить направление...", AddTranslationProfile));
            panel.Controls.Add(new Label
            {
                Text = "«Язык интерфейса» и «Язык активной раскладки» вычисляются в момент нажатия. Одну комбинацию нельзя отдать двум действиям CyrFlip.",
                AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 4),
            });

            panel.Controls.Add(SectionHeader("Результат"));
            panel.Controls.Add(Setting(_translateCopy,
                "Перевод попадает в буфер обмена обычной записью — и, если история включена, сохраняется в ней, как любое другое копирование."));
            panel.Controls.Add(Setting(_translatePaste,
                "Сразу заменяет выделение переводом. Если к моменту ответа фокус ушёл в другое окно, вставки не будет — окно предложит вставить вручную."));
            panel.Controls.Add(Setting(_translateShowSource,
                "Показывает исходный текст в верхней части окна результата."));
            panel.Controls.Add(Setting(LabeledRow("Закрывать окно через (с):", _translateWindowTimeout),
                "0 — не закрывать по таймеру. Окно и так закроется по Esc, по кнопке закрытия или когда вы перейдёте в другое окно."));
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
            UseWaitCursor = value;
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
                    AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(25, 6, 3, 4),
                });
            // Gated here, not in ReloadTranslateState: ApplyLanguage rebuilds these rows after the
            // gating pass, so a switch set there would be lost on every language change.
            _translationRows.Enabled = _config.EnableTranslate;
            _translationRows.ResumeLayout();
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
            var enabled = new CheckBox { Text = "", Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 6, 2, 2) };
            enabled.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                // Switching a row back on can collide with a chord that was assigned while this row
                // slept - the editing paths check, so the enabling path has to check too.
                if (enabled.Checked && profile.IsUsable)
                {
                    string? conflict = ClashingCyrFlipAction(Hotkey.Parse(profile.Hotkey), ignoreTranslationId: profile.Id);
                    if (conflict != null)
                    {
                        Warn(string.Format(Translate("Комбинация {0} уже занята действием «{1}" + "»."), profile.Hotkey, conflict));
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
            if (!_config.EnableHotkeys || !_config.EnableTranslate) target.ForeColor = SystemColors.GrayText;
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
            using var dialog = new TranslationDialog(existing, _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile == null) return;
            TranslationProfile profile = dialog.Profile;
            // Only a real chord can clash - and Hotkey.Parse("") would answer Ctrl+Shift+F12, which
            // would report the seeded EN ⇄ RU row as a conflict with a row that has no chord at all.
            if (profile.Hotkey.Length > 0)
            {
                string? conflict = ClashingCyrFlipAction(Hotkey.Parse(profile.Hotkey), ignoreTranslationId: existing?.Id);
                if (conflict != null)
                {
                    Warn(string.Format(Translate("Комбинация {0} уже занята действием «{1}" + "»."), profile.Hotkey, conflict));
                    return;
                }
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
            if (MessageBox.Show(this, Translate("Скачать и установить Ollama? Это несколько сотен мегабайт, загрузка может занять время."),
                    "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetTranslateBusy(true);
            _translateStatus.Text = Translate("Скачиваю Ollama...");
            string installer = "";
            try
            {
                var progress = new Progress<string>(text => _translateStatus.Text = Translate("Скачиваю Ollama:") + " " + text);
                installer = await OllamaManager.DownloadInstallerAsync(progress, TranslateWorkToken());
            }
            catch (OperationCanceledException)
            {
                // The user closed the window: that is not a failure, and it must not go on to open a
                // browser at ollama.com behind their back.
                SetTranslateBusy(false);
                return;
            }
            catch { /* the network gave up - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (installer.Length > 0)
            {
                _translateStatus.Text = Translate("Запускаю установщик Ollama...");
                OllamaManager.RunInstaller(installer);
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
                _translateStatus.Text = Translate("Укажите имя модели, например qwen2.5:3b.");
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
            panel.Controls.Add(new Label { Text = "Значок на панели задач: левый клик — список сценариев, правый — Jump List Windows.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(31, 0, 3, 6) });

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
            var down = LauncherButton("↓", () => LauncherMove(+1));
            up.Width = down.Width = 32; up.AutoSize = down.AutoSize = false; up.Height = down.Height = RowHeight;
            row1.Controls.Add(up); row1.Controls.Add(down);
            panel.Controls.Add(row1);

            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 0, 3, 2) };
            row2.Controls.Add(LauncherButton("Экспорт...", LauncherExport));
            row2.Controls.Add(LauncherButton("Импорт...", LauncherImport));
            _launcherImportOcr = LauncherButton("Импорт из OneClickRunner...", LauncherImportFromOcr);
            row2.Controls.Add(_launcherImportOcr);
            panel.Controls.Add(row2);

            panel.Controls.Add(new Label { Text = "Двойной клик или Enter — запуск, F2 — изменение, Delete — удаление. Импорт из OneClickRunner копирует сценарии и никогда не изменяет исходные файлы.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 4) });

            // Context menu, keyboard shortcuts and the run-on-double-click of the source app.
            var context = new ContextMenuStrip();
            context.Opening += (_, e) =>
            {
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

        /// <summary>Rebuild the scenario table: translated column headers, filtered rows, cached icons.</summary>
        private void ReloadLauncherRows()
        {
            _launcherList.BeginUpdate();
            _launcherList.Items.Clear();
            _launcherList.Columns.Clear();
            _launcherImages.Images.Clear();

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
                System.Drawing.Icon? icon = _launcherIcons.Get(scenario);
                if (icon != null)
                {
                    string key = scenario.Id.ToString("N");
                    _launcherImages.Images.Add(key, icon);
                    item.ImageKey = key;
                }
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
        {
            if (scenario.Hotkey.Length == 0) return false;
            if (ClashingCyrFlipAction(Hotkey.Parse(scenario.Hotkey), ignoreScenarioId: scenario.Id) == null) return false;
            scenario.Hotkey = "";
            _launcherStore.Update(scenario);
            return true;
        }

        /// <summary>The scenario's chord must be free among all four hotkey owners; warn and reject otherwise.</summary>
        private bool LauncherHotkeyIsFree(LauncherScenario scenario)
        {
            if (scenario.Hotkey.Length == 0) return true;
            var chord = Hotkey.Parse(scenario.Hotkey);
            string? conflict = ClashingCyrFlipAction(chord, ignoreScenarioId: scenario.Id);
            if (conflict == null) return true;
            Warn(string.Format(Translate("Комбинация {0} уже занята действием «{1}»."), scenario.Hotkey, conflict));
            return false;
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
            if (MessageBox.Show(this, string.Format(Translate("Удалить сценарий «{0}»?"), selected.Name),
                    "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _launcherStore.Remove(selected.Id);
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
            LauncherLaunchResult result = LauncherExecution.Launch(selected, Translate, () =>
            {
                using var prompt = new YtDlpLinkDialog(_config.UiLanguage);
                return prompt.ShowDialog(this) == DialogResult.OK ? prompt.Link : null;
            });
            if (!result.Success && !result.Cancelled)
                Warn(string.Format(Translate("Не удалось запустить «{0}»: {1}"), selected.Name, result.ErrorMessage));
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
                MessageBox.Show(this, string.Format(Translate("Сценарий «{0}» экспортирован."), selected.Name),
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn(string.Format(Translate("Не удалось экспортировать: {0}"), ex.Message));
            }
        }

        private void LauncherImport()
        {
            using var dialog = new OpenFileDialog { Filter = "XML|*.xml|*.*|*.*", Title = Translate("Выберите XML-файл сценария") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LauncherScenario? imported = _launcherStore.Import(dialog.FileName, out Exception? error);
            if (imported == null)
            {
                Warn(string.Format(Translate("Не удалось импортировать: {0}"), error?.Message));
                return;
            }
            // A file exported on another machine can carry a chord that is taken here. One chord
            // belongs to one action, so it is dropped - the scenario itself still arrives.
            bool chordDropped = StripClashingHotkey(imported);
            LauncherNotifyChanged();
            LauncherReselect(imported.Id);
            string message = string.Format(Translate("Импортирован сценарий «{0}»."), imported.Name);
            if (chordDropped) message += "\n" + Translate("Его комбинация уже занята, поэтому не перенесена.");
            MessageBox.Show(this, message, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>The explicit, repeatable OneClickRunner import (spec §5.3) - the source is never modified.</summary>
        private void LauncherImportFromOcr()
        {
            if (!LauncherMigration.SourceExists())
            {
                MessageBox.Show(this, Translate("Сценарии OneClickRunner не найдены."),
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            LauncherMigration.Result result = LauncherMigration.Import(_launcherStore);
            LauncherNotifyChanged();
            string summary = string.Format(Translate("Перенесено сценариев: {0}."), result.Imported);
            if (result.Skipped.Count > 0)
                summary += "\n" + string.Format(Translate("Пропущено повреждённых файлов: {0}."), result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + string.Format(Translate("Из-за совпадения идентификаторов назначены новые: {0}."), result.NewIds);
            MessageBox.Show(this, summary, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // The bitmaps stay owned by the ImageList: it holds them as "originals" until something
            // asks for its native handle, so disposing them here throws on the first paint. The list
            // frees whatever it still holds when the form disposes it (see Dispose) - which is why the
            // ImageList itself has to be kept: a control it is assigned to never owns it.
            images.Images.Add("general", TabIcon(0)); images.Images.Add("indicators", TabIcon(1)); images.Images.Add("hotkeys", TabIcon(2));
            images.Images.Add("clipboard", TabIcon(3)); images.Images.Add("advanced", TabIcon(4)); images.Images.Add("about", TabIcon(5));
            images.Images.Add("languages", TabIcon(6));
            images.Images.Add("conversions", TabIcon(7));
            images.Images.Add("launcher", TabIcon(8));
            images.Images.Add("translate", TabIcon(9));
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
                case 6: g.DrawEllipse(pen, 2, 2, 14, 14); g.DrawEllipse(pen, 6.5f, 2, 5, 14); g.DrawLine(pen, 2.5f, 9, 15.5f, 9); break;
                // Two opposite arrows: the layout-to-layout conversion table.
                case 7:
                    g.DrawLine(pen, 3, 6, 14, 6); g.DrawLine(pen, 11, 3, 14, 6); g.DrawLine(pen, 11, 9, 14, 6);
                    g.DrawLine(pen, 15, 12, 4, 12); g.DrawLine(pen, 7, 9, 4, 12); g.DrawLine(pen, 7, 15, 4, 12);
                    break;
                // The absorbed OneClickRunner's own mark, as line art: a list of commands under the
                // pointer. Same shape as the full-colour icon in the page header and on the taskbar.
                case 8: LauncherBrand.DrawGlyph(g, 18, pen, brush); break;
                // Two speech bubbles, one answering the other: the translator.
                case 9:
                    g.DrawRectangle(pen, 2, 3, 9, 7); g.DrawLine(pen, 4, 10, 6, 13); g.DrawLine(pen, 6, 13, 7, 10);
                    g.DrawRectangle(pen, 8, 8, 8, 7); g.FillRectangle(brush, 10, 11, 4, 1);
                    break;
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
            panel.Controls.Add(new Label { Text = "Разработчик: SerZhyAle", AutoSize = true, Font = BoldFont, Margin = new Padding(3, 14, 3, 4) });
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
            block.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(28, 0, 3, 3) });
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
                _tabIcons?.Dispose();   // assigned to the TabControl, which never owned it
                _boldFont?.Dispose();
                _ownFont?.Dispose();
                _ownIcon?.Dispose();
            }
        }
    }
}

