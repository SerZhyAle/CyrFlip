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
        private readonly CheckBox _enableHotkeys = Check("Listen for global hotkeys");
        private readonly CheckBox _caseEnabled = Check("Case flip");
        private readonly CheckBox _historyEnabled = Check("Clipboard history");
        private readonly CheckBox _deferRdp = Check("Yield hotkeys to the remote desktop (mstsc/msrdc)");
        private readonly CheckBox _convertSymbols = Check("Convert punctuation along with the text");
        // ---- Text context menu (CyrFlip's own menu over the selection) ----
        private readonly CheckBox _contextMenuEnabled = Check("CyrFlip's own context menu over the selected text");
        private readonly ComboBox _contextMenuChord = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly CheckBox _cursor = Check("Show layout on the text mouse cursor");
        private readonly CheckBox _autostart = Check("Start CyrFlip with Windows");
        private readonly CheckBox _startMenuShortcut = Check("CyrFlip shortcut in the Start menu");
        private readonly CheckBox _desktopShortcut = Check("CyrFlip shortcut on the desktop");
        private readonly CheckBox _keepAwake = Check("Keep the computer awake");
        private readonly CheckBox _keepScreen = Check("Keep the screen on (like video)");
        private readonly CheckBox _settingsBackup = Check("Keep a settings backup in Documents");
        private readonly Label _backupState = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 0) };
        private readonly ComboBox _uiLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        // The theme (ticket S0020, the owner's rework of 2026-10-08): a drop-down in the shape of the
        // chord one - a translated caption over the invariant token, refilled by ReloadThemeCombo.
        private readonly ComboBox _themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly CheckBox _caret = Check("Show layout label near the caret");
        private readonly CheckBox _dot = Check("Compact dot instead of language letters");
        // The marker size (S0011 LI-3): three steps, see MarkerSize.Presets. The caption beside it is a
        // translated word, so it is rebuilt on a language change rather than remembered as source text.
        private readonly TrackBar _markerSize = new TrackBar { Minimum = 0, Maximum = 2, TickFrequency = 1, SmallChange = 1, LargeChange = 1, Width = 150 };
        private readonly Label _markerSizeValue = new Label { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        private readonly CheckBox _language = Check("Change layout after converting text");
        private readonly CheckBox _caps = Check("Synchronize CapsLock after case correction");
        private readonly CheckBox _history = Check("Enable clipboard history");
        private readonly CheckBox _pause = Check("Pause history capture");
        private readonly CheckBox _historyStartup = Check("Show clipboard history on startup");
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
        private readonly Button _restoreLanguageHotkeys = new Button { Text = "Restore shortcuts", AutoSize = true, Margin = new Padding(4, 3, 4, 3) };
        private readonly Button _restoreLayouts = new Button { Text = "Restore layouts", AutoSize = true, Margin = new Padding(4, 3, 4, 3) };
        private bool _languageHotkeyNoticeShown;
        private bool _layoutNoticeShown;
        private bool _loading;
        // ---- Scenario launcher tab ----
        private readonly LauncherScenarioStore _launcherStore;
        private readonly Action<bool> _setLauncherEnabled;
        private readonly CheckBox _launcherEnabled = Check("Enable quick launch");
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
        private readonly CheckBox _translateEnabled = Check("Enable translation of the selected text");
        private readonly CheckBox _translateAutoStart = Check("Start Ollama automatically when translating");
        private readonly CheckBox _translateCopy = Check("Put the translation on the clipboard");
        private readonly CheckBox _translatePaste = Check("Paste the translation over the selection right away");
        private readonly CheckBox _translateShowSource = Check("Show the source text next to the translation");
        // Value-carrying controls are created with no Text on purpose: RememberSourceTexts only
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
        // Hiding the window hands a running download / pull over instead of cancelling it (S0060 P7-i).
        private readonly WorkHandover _translateHandover = new WorkHandover();
        // ---- Quick notes tab ----
        private readonly Action _setQuickNotesHotkey, _openQuickNotes, _clearQuickNotes;
        private readonly Func<string, bool, string> _exportQuickNotes;
        // Export / import of the open exchange file (ticket S0023); null leaves the rows out.
        private readonly Action<IWin32Window, bool>? _exchange;
        private readonly CheckBox _quickNotesEnabled = Check("Enable the quick notes");
        private readonly CheckBox _quickNotesHotkeyEnabled = Check("Quick notes");
        private readonly CheckBox _quickNotesWrap = Check("Wrap long lines in the editor");
        private readonly CheckBox _quickNotesLineNumbers = Check("Line numbers");
        private readonly CheckBox _quickNotesExportMeta = Check("Add the dates when exporting");
        private readonly Label _quickNotesHotkeyValue = new Label { AutoSize = true };
        private readonly List<Control> _quickNotesControls = new List<Control>();
        // ---- The text context menu's keyboard chord (S0045 K2) ----
        private readonly CheckBox _textMenuHotkeyEnabled = Check("Open the menu from the keyboard");
        private readonly Label _textMenuHotkeyValue = new Label { AutoSize = true };
        // ---- Graphics (S0026/S0057/S0071): the fields live in SettingsForm.Graphics.cs ----
        // ---- Symbols palette (S0056) ----
        private readonly CheckBox _symbolsEnabled = Check("Enable symbols window");
        private readonly CheckBox _symbolsHotkeyEnabled = Check("Symbols window");
        private readonly Label _symbolsHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _symbolsHideTypable = Check("Hide symbols available on my layouts");
        private readonly List<Control> _symbolsControls = new List<Control>();
        private readonly Dictionary<Control, string> _sourceTexts = new Dictionary<Control, string>();
        // ---- WINDOWS-UI section 3: collapsible groups, remembered context, discovery ----
        // Every group this window built, in build order; the expansion of each is remembered under its
        // stable Id (never a translated caption), and the palette pass walks this list on every theme.
        private readonly List<CollapsibleGroup> _groups = new List<CollapsibleGroup>();
        // (caption-side control, hint label) pairs whose hint must sit under the caption's own text
        // inset (WINDOWS-UI section 4, boolean rows); re-measured on every language/font change.
        private readonly List<KeyValuePair<Control, Label>> _hintPairs = new List<KeyValuePair<Control, Label>>();
        // The hint label of each row, looked up by the control the row is about. The dependency pass
        // (SettingsForm.Dependencies.cs) reads it to tell a row that has a hint - its "why unavailable" note
        // stands under it, at the hint's inset - from one that has none. Never written to by that pass: the hint
        // is a registered caption of the language pass, the note is a label of its own.
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
        /// The fonts a family swap replaced (CRASH-1, S0066 L-1): kept alive until the window is
        /// torn down, because a control that still holds one - a row built outside the tree - must
        /// never see it disposed. Never grows but with the script changes a user makes.
        /// </summary>
        private readonly List<Font> _retiredFonts = new List<Font>();
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
        /// <summary>The (control, description) pairs whose description stands under the control's own text - what the row-anatomy gates measure.</summary>
        internal IReadOnlyList<KeyValuePair<Control, Label>> HintPairs => _hintPairs;
        internal SettingsPageKey HelpPageKey => _tabs?.SelectedTab?.Tag is SettingsPageKey key ? key : SettingsPageKey.General;
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
        /// A long translator operation - the Ollama download or a model pull - ended while the window was
        /// hidden (it is handed over when the window hides, not cancelled; S0060 P7-i). The text is its
        /// last status line, already in the UI language; the context tells it through the tray.
        /// </summary>
        public event EventHandler<string>? TranslateWorkFinishedInBackground;

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
        /// <summary>"Изменить..." beside the full-capture chord (S0071).</summary>
        public event EventHandler? ScreenshotFullHotkeyChangeRequested;
        /// <summary>"Изменить..." beside the area-capture-with-drawing chord (S0071).</summary>
        public event EventHandler? ScreenshotAnnotateAreaHotkeyChangeRequested;
        /// <summary>"Изменить..." beside the full-capture-with-drawing chord (S0071).</summary>
        public event EventHandler? ScreenshotFullAnnotateHotkeyChangeRequested;
        /// <summary>"Изменить..." beside the screenshots-folder chord (S0057).</summary>
        public event EventHandler? ScreenshotFolderHotkeyChangeRequested;
        /// <summary>"Открыть папку" - the one opener the chord uses too (S0057).</summary>
        public event EventHandler? ScreenshotFolderOpenRequested;
        /// <summary>"Изменить..." beside the text context menu's keyboard chord (S0045 K2).</summary>
        public event EventHandler? TextMenuHotkeyChangeRequested;
        /// <summary>"Сделать снимок области" - the same action as the tray item.</summary>
        public event EventHandler? ScreenshotRequested;
        /// <summary>"Сделать снимок всего экрана" (S0071) - the same action as its tray item.</summary>
        public event EventHandler? ScreenshotFullRequested;
        /// <summary>The annotate section's two try-out buttons (S0071) - the same entry points as the chords.</summary>
        public event EventHandler? ScreenshotAnnotateAreaRequested;
        public event EventHandler? ScreenshotFullAnnotateRequested;
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

            Text = "CyrFlip Settings";
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
            // The page order is the pillar order of POSITIONING.md (S0062 section 4); each page is built by
            // its own method and added in SettingsPageKeys.DisplayOrder, so the order is one table.
            _quickNotesHotkeyEnabled.Name = "setting.quick-notes-chord";
            _symbolsHotkeyEnabled.Name = "setting.symbols-chord";
            _uiLanguage.Name = "setting.ui-language";
            _themeCombo.Name = "setting.theme";
            _autostart.Name = "setting.autostart";
            _startMenuShortcut.Name = "setting.shortcut-start-menu";
            _desktopShortcut.Name = "setting.shortcut-desktop";
            _keepAwake.Name = "setting.keep-system-awake";
            _keepScreen.Name = "setting.keep-screen-on";
            var pages = new Dictionary<SettingsPageKey, TabPage>();
            pages[SettingsPageKey.General] = WithIcon(Page("General", "Core app settings; changes take effect immediately.",
                Group("general/language", "Language and theme",
                    Setting(LanguageRow(), "Choose the CyrFlip interface language. The choice is saved and also applies to the tray menu."),
                    Setting(ThemeRow(), "“Same as Windows” - CyrFlip is light or dark with Windows and follows it when it switches, without a restart. Windows contrast themes always take precedence over this setting. The layout marker at the cursor and caret does not change with the theme: its colour names the layout.")),
                Group("general/startup", "Startup",
                Setting(_autostart, Autostart.ManagedByWindows
                    ? "Windows itself owns autostart for the Microsoft Store build. The checkbox shows the current state, and clicking it opens Settings ▸ Apps ▸ Startup, where autostart is turned on or off."
                    : "Adds CyrFlip to startup for the current Windows user only. It starts in the background and appears in the notification area at the next sign-in."),
                ShortcutSettings()),
                Group("general/power", "Power and screen",
                Setting(_keepAwake, "While on, Windows will not sleep or hibernate on idle — handy for long downloads, copies or renders. The state is saved: a switch left on keeps the computer awake after a restart too - CyrFlip will not watch your battery for you."),
                Setting(_keepScreen, "The screen does not turn off or lock on idle, just like while watching a video. It does not override a password lock (Win+L or a security policy). The state is saved: a switch left on keeps the screen alive after a restart too.")),
                Group("general/transfer", "Settings transfer",
                    Button("Export settings...", AppGlyphs.Export, () => SettingsTransferRequested?.Invoke("export")),
                    Button("Import settings...", AppGlyphs.Import, () => SettingsTransferRequested?.Invoke("import")),
                    _restoreSettings = Button("Restore settings before import...", AppGlyphs.Undo, () => SettingsTransferRequested?.Invoke("restore")),
                    Setting(_settingsBackup, "While on, CyrFlip keeps a rolling backup of every setting in Documents\\CyrFlip\\Backups - the three newest are kept. The backup survives a Microsoft Store reinstall: a fresh install finds it and offers to restore. The file carries your hosts, sites and scenarios - keep it private."),
                    Setting(BackupFolderRow(), "The backups are plain settings files named by date. Open the folder to copy one or to take it to another computer."))), 0);
            _restoreSettings.Name = "group.transfer";
            _cursor.Name = "setting.cursor-marker";
            _caret.Name = "setting.caret-marker";
            _dot.Name = "setting.dot-mode";
            _markerSize.Name = "setting.marker-size";
            pages[SettingsPageKey.Indicators] = WithIcon(Page("Indicators", "Options that help you see the active layout before typing.",
                Setting(_cursor, "Replaces the standard I-beam with a cursor carrying a small language marker. The normal mouse arrow is unchanged."),
                Setting(_caret, "Draws a small marker beside the blinking caret. It works in apps that expose the caret position to Windows."),
                Setting(_dot, "Shows a compact colour dot beside the caret instead of the language letters — handy if the letters distract you."),
                Setting(MarkerSizeRow(), "Size of the marker beside the caret and on the text mouse pointer. It also follows the display scaling, and the mouse pointer follows the pointer size from Windows accessibility settings too. Applied immediately.")), 1);
            // The two settings that used to close this page change what happens to typed text, not what is
            // seen before typing: they live with the conversion table and the case fix (S0062 L13, L14).
            _language.Name = "setting.layout-after-convert";
            _caps.Name = "setting.caps-sync";
            pages[SettingsPageKey.WindowsLanguages] = WithIcon(LanguagesPage(), 6);
            pages[SettingsPageKey.Conversions] = WithIcon(ConversionsPage(), 7);
            pages[SettingsPageKey.SelectionMenu] = WithIcon(SelectionMenuPage(), 14);
            pages[SettingsPageKey.Symbols] = WithIcon(SymbolsPage(), 12);
            pages[SettingsPageKey.TrafficLight] = WithIcon(TrafficLightPage(), 15);
            TabPage translatePage = pages[SettingsPageKey.Translate] = WithIcon(TranslatePage(), 9);
            pages[SettingsPageKey.Clipboard] = WithIcon(ClipboardPage(), 3);
            pages[SettingsPageKey.QuickNotes] = WithIcon(QuickNotesPage(), 10);
            pages[SettingsPageKey.Graphics] = WithIcon(GraphicsPage(), 11);
            pages[SettingsPageKey.Launcher] = WithIcon(LauncherPage(), 8);
            // The merged page: the master switch, the RDP option and the overview of every chord (D8, D9).
            pages[SettingsPageKey.Hotkeys] = WithIcon(HotkeysOverviewPage(), 2);
            pages[SettingsPageKey.About] = WithIcon(AboutPage(), 5);
            pages[SettingsPageKey.ExchangeServer] = WithIcon(ExchangeServerPage(), 16);
            foreach (SettingsPageKey key in SettingsPageKeys.DisplayOrder) tabs.TabPages.Add(pages[key]);
            // The build version at the foot of the page list, beside the product name in the header band: it is
            // where the eye goes to ask "which build is this" (the About page keeps the full line).
            _navVersion = new Label { Text = SupportBundle.AppVersion(), AutoSize = true, UseMnemonic = false, ForeColor = ThemePalette.Light.TextMuted };
            Controls.Add(_navVersion);
            _navVersion.BringToFront();
            SizeChanged += (_, _) =>
            {
                PlaceNavVersion();
                if (_tabs?.SelectedTab != null) ClampPage(ContentPanel(_tabs.SelectedTab));
            };
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
            // S0062: the stored key is read through SettingsPageKeys (the retired overview key opens the
            // merged page), and a bare SettingsTab index is an index into the list as it was before the
            // regrouping - turned into a key once, so nobody lands on another page because of the reorder.
            if (SettingsPageKeys.TryParse(_config.SettingsPageId, out SettingsPageKey savedPage))
                SelectPage(savedPage);
            else if (_config.SettingsTab > 0 && SettingsPageKeys.FromLegacyIndex(_config.SettingsTab) is SettingsPageKey legacyPage)
                SelectPage(legacyPage);
            _lastPageIndex = tabs.SelectedIndex;
            UpdateHeaderCommands();
            // A TabControl reports no selected page before its handle exists, so the first state is taken then.
            tabs.HandleCreated += (_, _) => UpdateHeaderCommands();
            tabs.SelectedIndexChanged += (_, _) =>
            {
                UpdateHeaderCommands();
                if (!_restoringContext) _config.SaveSettingsTab(tabs.SelectedIndex, tabs.SelectedTab?.Tag?.ToString());
                // The page being left keeps where it was scrolled to; the page being entered gets it
                // back once it has laid out (WINDOWS-UI section 3 rule 3).
                if (!_restoringContext) CaptureViewport(_lastPageIndex);
                _lastPageIndex = tabs.SelectedIndex;
                // A page's own Resize re-clamps only that page, and only while it is the selected one: a page
                // that was hidden while the window changed size is brought up to date here, before its
                // scroll position is restored.
                FlowLayoutPanel? selectedPanel = tabs.SelectedTab == null ? null : ContentPanel(tabs.SelectedTab);
                if (selectedPanel != null)
                    ClampPage(selectedPanel);
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
            _settingsBackup.CheckedChanged += (_, _) => Changed(v => _config.SettingsBackupEnabled = v, _settingsBackup.Checked);
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
                SaveConfig();
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
            ButtonGlyphs.Attach(_restoreLanguageHotkeys, AppGlyphs.Undo);
            ButtonGlyphs.Attach(_restoreLayouts, AppGlyphs.Undo);
            ButtonGlyphs.UseCommandMetric(_restoreLanguageHotkeys);
            ButtonGlyphs.UseCommandMetric(_restoreLayouts);
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
            _quickNotesLineNumbers.CheckedChanged += (_, _) =>
                QuickNotesSettingChanged(() => _config.QuickNotesLineNumbers = _quickNotesLineNumbers.Checked);

            // The Graphics tab (S0026), same shape again.
            _screenshotHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotHotkey = _screenshotHotkeyEnabled.Checked);
            _screenshotSave.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.ScreenshotSaveEnabled = _screenshotSave.Checked, reload: true);
            _screenshotFullHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotFullHotkey = _screenshotFullHotkeyEnabled.Checked);
            _screenshotAnnotateEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotAnnotate = _screenshotAnnotateEnabled.Checked, reload: true);
            _screenshotAnnotateAreaHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotAnnotateAreaHotkey = _screenshotAnnotateAreaHotkeyEnabled.Checked);
            _screenshotFullAnnotateHotkeyEnabled.CheckedChanged += (_, _) =>
                GraphicsSettingChanged(() => _config.EnableScreenshotFullAnnotateHotkey = _screenshotFullAnnotateHotkeyEnabled.Checked);
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
            // Between the first keystroke and the commit the field is the user's, not the config's: a Reload()
            // that arrives meanwhile (a background result, a switch on another page) must not type over it.
            _translateEndpoint.TextChanged += (_, _) => { if (!_loading) _translateEndpointUncommitted = true; };
            _translateModel.TextChanged += (_, _) => { if (!_loading) _translateModelUncommitted = true; };
            VisibleChanged += (_, _) => { if (!Visible) { CommitTranslateText(); HandOverTranslateWork(); } else _translateHandover.OnShown(); };
            // The real end - the application exiting - stops it; hiding the window does not.
            Disposed += (_, _) => CancelTranslateWork();
            // The captions are remembered before the first Reload fills any value in (S0036 UI-10): a
            // value label filled there - a chord, the version, a size - was registered as a "source
            // original" and put back over the live value by every re-translation, the S0007 ST-4 bug
            // class. It only held because ApplyLanguage happened to set each of them again.
            BuildTransferPreferenceGroups();
            // After the pages are final (a single group is flattened above): a note is placed by the row its
            // control ended up in. The notes have no text yet, so the walk below does not register them.
            BuildDependencyNotes();
            RememberSourceTexts(this);
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
            Button find = GlyphCommand("settingsSearch", AppGlyphs.Search, "Find a setting...", OpenSettingsSearch);
            Button expandAll = GlyphCommand("settingsExpandAll", AppGlyphs.Expand, "Expand all", () => SetPageGroupsExpanded(true));
            Button collapseAll = GlyphCommand("settingsCollapseAll", AppGlyphs.Collapse, "Collapse all", () => SetPageGroupsExpanded(false));
            _expandAllButton = expandAll; _collapseAllButton = collapseAll;
            var commands = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0) };
            Button help = GlyphCommand("contextHelp", AppGlyphs.Help, "Help (F1)", () => ContextHelp.Open(this));
            commands.Controls.Add(help);
            commands.Controls.Add(find);
            commands.Controls.Add(expandAll);
            commands.Controls.Add(collapseAll);

            var brand = new Label { Text = "CyrFlip", AutoSize = true, Font = BoldFont, Margin = new Padding(16, 14, 3, 6), UseMnemonic = false, Tag = "brand" };
            // The band holds the title only, so its height is the same on every page. The purpose line is the
            // first line of the page itself (Page): a band that grew and shrank with the description squeezed
            // the page list, which wrapped into a second column and moved the selected page's column beside
            // the pane - the items "jumped" and the pane covered them.
            _headerTitle = new Label { AutoSize = true, Font = TitleFont, Margin = new Padding(3, 4, 3, 8), UseMnemonic = false, Tag = "title" };
            var titleBlock = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 2) };
            // The exchange server page's mark stands before its title (EXCHANGE-MARK rule 8 (a), S0079 M1) and exists
            // only while that page is selected (SyncPageHeader). The row stays as tall as the title's, so the band is
            // one height on every page; the margins centre the star on the title's letters, not on its leading.
            _headerMark = new ExchangeMarkPicture(24) { Name = "headerExchangeMark", Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 6, 4), Visible = false };
            _exchangeMarks.Add(_headerMark);
            titleBlock.Controls.Add(_headerMark);
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
        private ExchangeMarkPicture? _headerMark;
        // The marks that live as long as the window (the band's and the About credit's); the account rows' own are
        // rebuilt with the list and found by walking it (NameExchangeMarks).
        private readonly List<ExchangeMarkPicture> _exchangeMarks = new List<ExchangeMarkPicture>();
        private Font? _titleFont;
        private readonly List<KeyValuePair<Button, string>> _headerCommandKeys = new List<KeyValuePair<Button, string>>();

        /// <summary>The page title's font: the window font at one and a half times, bold - the top of the hierarchy.</summary>
        private Font TitleFont => _titleFont ?? (_titleFont = new Font(Font.FontFamily, Font.SizeInPoints * 1.5f, FontStyle.Bold));

        /// <summary>A glyph-only header command. Its English name is a key the language pass translates into the
        /// accessible name and the tooltip (<see cref="NameHeaderCommands"/>), so a live language switch reaches it.</summary>
        private Button GlyphCommand(string name, string glyphId, string englishName, Action action)
        {
            // The command metric's square (32 logical px, 8 px between neighbours), like every other glyph-only
            // command of the window; ReassertCommandMetrics scales it again on a DPI change.
            int edge = CommandEdge;
            var button = new Button { Name = name, AutoSize = false, Size = new Size(edge, edge), Margin = new Padding(4, 0, 4, 0), TabStop = true, UseMnemonic = false };
            button.Click += (_, _) => action();
            AsMoveGlyph(button, glyphId);
            button.AccessibleName = englishName;
            _headerCommandKeys.Add(new KeyValuePair<Button, string>(button, englishName));
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
            if (_headerMark != null) _headerMark.Visible = Equals(page.Tag, SettingsPageKey.ExchangeServer);
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
            if (_restoringContext) return;
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
            if (_tabs.TabPages[pageIndex].Tag is not SettingsPageKey key) return;
            FlowLayoutPanel flow = ContentPanel(_tabs.TabPages[pageIndex]);
            int scroll = Math.Max(0, -flow.AutoScrollPosition.Y);
            (string id, int top) anchor = ViewportAnchor(flow, scroll);
            int offset = scroll - anchor.top;
            // Device px -> logical px, so the same anchor+offset holds on a different monitor DPI.
            int offsetLogical = (int)Math.Round(offset * 96.0 / Math.Max(96, DeviceDpi));
            _config.SettingsViewport = UpsertViewportRow(_config.SettingsViewport, key, anchor.id, offsetLogical);
        }

        /// <summary>
        /// A page's row is written under its key (S0062 D11), never its position - the list is reordered
        /// between versions. A legacy numeric row of the same page is replaced, not kept beside it.
        /// </summary>
        internal static string UpsertViewportRow(string raw, SettingsPageKey page, string anchorId, int offsetLogical)
        {
            var kept = new List<string>();
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (!SettingsPageKeys.TryParseViewportPage(part.Split('|')[0], out SettingsPageKey existing) || existing != page)
                    kept.Add(part);
            kept.Add(page + "|" + anchorId + "|" + offsetLogical);
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
            if (_tabs.TabPages[pageIndex].Tag is not SettingsPageKey key) return;
            (string? anchorId, int offsetLogical) = ReadViewportRow(_config.SettingsViewport, key);
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

        /// <summary>The stored anchor of a page. A row of a build before S0062 carries the old list index in its
        /// page column and is read through the old order table (S0062 section 8); a key name is read as itself.</summary>
        internal static (string? id, int offset) ReadViewportRow(string raw, SettingsPageKey page)
        {
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = part.Split(new[] { '|' });
                if (fields.Length == 3 && SettingsPageKeys.TryParseViewportPage(fields[0], out SettingsPageKey stored) && stored == page)
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

            /// <summary>Where the row lives, "Page / Group" - or the page alone for a row outside every group. Shown
            /// beside the caption (S0060 M14), so two rows with one caption are told apart without opening either.</summary>
            public string Context => GroupCaption.Length == 0 ? PageTitle : PageTitle + " / " + GroupCaption;

            /// <summary>What a list item reads as to a screen reader: the caption, then where it lives.</summary>
            public override string ToString() => Caption + " - " + Context;
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
                // A dependency note explains a row; it is not a setting of its own, so it is never a result.
                if (child is Label { Tag: DependencyNoteTag }) continue;
                // The index holds what a person can change or run: a row with an editor or a command. A page's
                // title and purpose line, a group's note and a status line are prose - finding them would be
                // finding a sentence, not a setting (S0060 M14).
                if (!CarriesEditorOrCommand(child)) continue;
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

        /// <summary>True when the control, or anything under it, is a value editor or a command (a link is one: it opens something).</summary>
        internal static bool CarriesEditorOrCommand(Control control)
        {
            if (control is ButtonBase || control is ListControl || control is NumericUpDown || control is TrackBar
                || control is TextBoxBase || control is ListView || control is LinkLabel)
                return true;
            foreach (Control child in control.Controls)
                if (CarriesEditorOrCommand(child)) return true;
            return false;
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
                || root is ListControl || root is NumericUpDown || root is TrackBar)
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
            // Two lines per result - the caption, then "Page / Group" muted (S0060 M14) - so the list is
            // owner-drawn at a fixed height measured from the dialog's own font.
            private readonly ListBox _results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
            private readonly Label _status = new Label { AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 0) };
            private List<SettingsSearchHit> _index;
            private List<SettingsSearchHit> _matches = new List<SettingsSearchHit>();

            public SettingsSearchHit? Chosen { get; private set; }

            internal SettingsSearchDialog(SettingsForm owner)
            {
                _owner = owner;
                _index = owner.BuildSearchIndex();
                Text = owner.Translate("Search settings");
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
                table.Controls.Add(new Label { Text = owner.Translate("Type the words to look for: a match is found by a setting's caption and by its description. Enter opens the found setting, Esc closes the search."), AutoSize = true, MaximumSize = new Size(530, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
                // The query box itself - without it the dialog had nothing to type into.
                _query.Margin = new Padding(0, 0, 0, 6);
                _query.AccessibleName = Text;
                table.Controls.Add(_query, 0, 1);
                table.Controls.Add(_results, 0, 2);
                table.Controls.Add(_status, 0, 3);
                Controls.Add(table);

                _results.ItemHeight = ResultItemHeight(Font);
                _results.DrawItem += DrawResult;
                _query.TextChanged += (_, _) => RunQuery();
                // The status line repeats where the selected result lives, whole: the list elides a long one.
                _results.SelectedIndexChanged += (_, _) => _status.Text =
                    _results.SelectedIndex < 0 || _results.SelectedIndex >= _matches.Count ? "" : _matches[_results.SelectedIndex].Context;
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
                // The hit itself is the item: the list draws its caption and where it lives (DrawResult), and a
                // screen reader reads its ToString - "caption - Page / Group" (S0060 M14).
                foreach (SettingsSearchHit hit in _matches) _results.Items.Add(hit);
                _results.EndUpdate();
                _status.Text = query.Length == 0 ? "" : _matches.Count == 0 ? _owner.Translate("Nothing found.") : "";
                if (_results.Items.Count > 0) _results.SelectedIndex = 0;
            }

            /// <summary>The height of a two-line result for a font: two text lines and a little air, never below one row's worth.</summary>
            internal static int ResultItemHeight(Font font) => font.Height * 2 + 10;

            /// <summary>
            /// The colour the second line takes: <c>text.muted</c> on an ordinary row, the row's own ink on the
            /// selected one (the highlight would not hold a muted grey), and the primary ink whenever the muted
            /// role would fall below 4.5:1 on the face the row is on (WINDOWS-UI section 6 rule 4).
            /// </summary>
            internal static Color ContextInk(ThemePalette palette, Color primary, Color face, bool selected)
            {
                if (selected) return primary;
                return ThemePalette.Contrast(palette.TextMuted, face) >= 4.5 ? palette.TextMuted : primary;
            }

            private void DrawResult(object? sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= _matches.Count) return;
                SettingsSearchHit hit = _matches[e.Index];
                e.DrawBackground();
                bool selected = (e.State & DrawItemState.Selected) != 0;
                ThemePalette palette = ThemeApply.PaletteOf(_results) ?? ThemePalette.Light;
                Color primary = e.ForeColor;
                Color context = ContextInk(palette, primary, e.BackColor, selected);
                // The reading order follows the language; the start side is the logical one, which a mirrored
                // window (RightToLeftLayout) puts on the right by itself.
                TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft : TextFormatFlags.Default);
                const int pad = 6;
                int half = e.Bounds.Height / 2;
                var first = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 2 * pad), half);
                var second = new Rectangle(first.X, e.Bounds.Y + half, first.Width, e.Bounds.Height - half);
                TextRenderer.DrawText(e.Graphics, hit.Caption, e.Font, first, primary, flags);
                TextRenderer.DrawText(e.Graphics, hit.Context, e.Font, second, context, flags);
                e.DrawFocusRectangle();
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
            _settingsBackup.Checked = _config.SettingsBackupEnabled;
            RefreshBackupState();
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
            ReloadTrafficLightState();
            ReloadExchangeServerState();
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
            panel.Controls.Add(Setting(_startMenuShortcut, "CyrFlip has no installer, so it creates its Start menu and desktop shortcuts itself on the first run. If you move the program's folder, the shortcuts follow it on the next start."));
            panel.Controls.Add(Setting(_desktopShortcut, "A shortcut you delete is never recreated by CyrFlip on its own - only this checkbox brings it back."));
            return panel;
        }

        /// <summary>A shortcut checkbox changed: create or delete the link, and show the truth if that failed.</summary>
        private void SetShortcut(ShortcutPlace place, CheckBox box)
        {
            if (_loading) return;
            bool wanted = box.Checked;
            Exception? failure = null;
            try { AppShortcuts.Set(place, wanted); }
            catch (Exception ex) { failure = ex; }
            // The box shows the truth first (a link that could not be made is not ticked), then the cause is said.
            RefreshShortcuts();
            if (failure != null)
                Fail(Localization.Format(Translate, "Could not change the shortcut: {0}", FailureCause.Describe(failure, Translate)),
                    () => { _loading = true; box.Checked = wanted; _loading = false; SetShortcut(place, box); });
        }

        private void RefreshShortcuts()
        {
            bool loading = _loading;
            _loading = true;
            _startMenuShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.StartMenu);
            _desktopShortcut.Checked = AppShortcuts.Exists(ShortcutPlace.Desktop);
            _loading = loading;
        }

        private void RememberSourceTexts(Control root)
        {
            // The language-hotkey rows are thrown away and rebuilt on every refresh, already in the
            // current language. Registering them would both leak dead Control keys into the map and
            // capture translated text as if it were the source original.
            if (ReferenceEquals(root, _languageRows) || ReferenceEquals(root, _layoutRows)
                || ReferenceEquals(root, _conversionRows) || ReferenceEquals(root, _translationRows)
                || ReferenceEquals(root, _tlRows) || ReferenceEquals(root, _exchangeAccountsList)) return;
            // A value label, not a caption: its "100%" registered here was put back on top of the
            // slider's real value by every re-translation (ticket S0007, ST-4).
            if (ReferenceEquals(root, _opacityValue)) return;
            if (ReferenceEquals(root, _tlTransparencyValue)) return;

            // ComboBox.Text is its selected value, not a UI caption. Translating it would reset
            // the language selector back to its first value on every UI refresh. Same for
            // NumericUpDown, whose Text is the number the user just typed, and for every text box:
            // a box filled from the config (the translator's server address) would be captured as if
            // it were a Russian caption and restored on top of the user's edit at the next language
            // change. The walk runs before the first Reload for the same reason (S0036 UI-10).
            if (!(root is ComboBox) && !(root is NumericUpDown) && !(root is TextBoxBase)
                && !_sourceTexts.ContainsKey(root) && !string.IsNullOrEmpty(root.Text))
                _sourceTexts.Add(root, root.Text);
            foreach (Control child in root.Controls) RememberSourceTexts(child);
        }

        private void ApplyLanguage()
        {
            HelpLanguage = _config.UiLanguage;
            // The bulk pass touches every caption of the window. Without layout suspension each
            // assignment re-lays-out the whole parent chain - and every relayout re-measures its
            // labels, which on the complex-script fonts (Nirmala UI, Vrinda, the Arabic faces) is
            // seconds of Uniscribe shaping for nothing. Suspended, the ~400 captions are assigned
            // raw and the tree lays out once, at the resume.
            SuspendLayoutDeep(this);
            try
            {
                ApplyScript();
                foreach (KeyValuePair<Control, string> pair in _sourceTexts)
                    pair.Key.Text = Translate(pair.Value);
                _caseHotkeyValue.Text = _config.CaseHotkey; _historyHotkeyValue.Text = _config.ClipboardHistoryHotkey;
                _quickNotesHotkeyValue.Text = _config.QuickNotesHotkey;
                _textMenuHotkeyValue.Text = _config.TextMenuHotkey;
                _screenshotHotkeyValue.Text = _config.ScreenshotHotkey;
                _screenshotFullHotkeyValue.Text = _config.ScreenshotFullHotkey;
                _screenshotAnnotateAreaHotkeyValue.Text = _config.ScreenshotAnnotateAreaHotkey;
                _screenshotFullAnnotateHotkeyValue.Text = _config.ScreenshotFullAnnotateHotkey;
                _screenshotFolderHotkeyValue.Text = _config.ScreenshotFolderHotkey;
                _screenshotFolderBox.Text = ScreenshotFolderShown();
                _opacityValue.Text = _opacity.Value + "%";
                _tlTransparencyValue.Text = _tlTransparency.Value + "%";
                _markerSizeValue.Text = MarkerSizeName(_markerSize.Value);
                _version.Text = VersionLine();
            }
            finally { ResumeLayoutDeep(this); }
            // Every pass below re-lays out part of the tree (M1): under one suspension, so each of
            // them writes geometry raw and the tree lays out once, at the resume.
            SuspendLayoutDeep(this);
            try
            {
                // The drop-downs are filled in this language first: their width is part of the page's editor column.
                ReloadToggleCombo();
                ReloadContextMenuChords();
                ReloadThemeCombo();
                FitDropDowns();
                AlignChordColumn();
                AdjustTabStrip();
                ReassertCommandMetrics();
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
            ReloadTrafficLightRows();
            ReloadHotkeysOverview();
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
            // Last of the state passes: the tables and the buttons above have just been rebuilt and re-gated,
            // and the notes read what is greyed now (S0060 M5). Before the first size is derived from content.
            ApplyDependencyNotes();
            // One checkbox name/state; the authored visible explanation is its accessible description too.
            // The reason a switch is greyed (ApplyDependencyNotes, just above) outranks the hint: it is what the
            // reader needs to hear while the control cannot be used.
            foreach (KeyValuePair<Control, Label> pair in _rowHints)
                if (pair.Key is CheckBox box && !IsExplainedByDependencyNote(box)) box.AccessibleDescription = pair.Value.Text;
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
                    NameCaptionedEditors(row.Editors, row.Caption.Text.TrimEnd(':', ' '));
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

        /// <summary>
        /// The editors and commands of one captioned row's strip, however deep the strip nests them. An editor is
        /// named after the row's caption; a command with a caption of its own is named "verb - caption", so the
        /// Change buttons of eight rows are eight different names to a screen reader (APP-BEHAVIOUR rule 9, S0060
        /// M9). A name somebody set on purpose is left alone; a derived one follows the language.
        /// </summary>
        private void NameCaptionedEditors(Control strip, string caption)
        {
            foreach (Control child in strip.Controls)
            {
                if (child is Label) continue;
                bool derivable = string.IsNullOrEmpty(child.AccessibleName) || _derivedNames.TryGetValue(child, out _);
                if (child is ComboBox or NumericUpDown or TrackBar or TextBoxBase or ListView)
                {
                    if (derivable) DeriveName(child, caption);
                    continue;
                }
                if (child is Button button)
                {
                    string verb = button.Text.TrimEnd('.', ' ');
                    if (derivable && verb.Any(char.IsLetter)) DeriveName(button, verb + " - " + caption);
                    continue;
                }
                if (child.HasChildren) NameCaptionedEditors(child, caption);
            }
        }

        private void DeriveName(Control control, string name)
        {
            if (control.AccessibleName != name) control.AccessibleName = name;
            if (!_derivedNames.TryGetValue(control, out _)) _derivedNames.Add(control, control);
        }

        /// <summary>Names of the controls whose caption is not a sibling label, refreshed with the language.</summary>
        private void NameFixedControls()
        {
            NameHeaderCommands();
            SyncPageHeader();
            NameExchangeMarks();
            _launcherSearch.AccessibleName = Translate("Search:").TrimEnd(':', ' ');
            if (_launcherUp != null) _launcherUp.AccessibleName = Translate("Move up");
            if (_launcherDown != null) _launcherDown.AccessibleName = Translate("Move down");
            _screenshotFolderBox.AccessibleName = Translate("Folder for screen region captures");
            _launcherList.AccessibleName = Translate("Quick launch");
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
            int max = ComboWidth(_contextMenuChord.Items.Cast<ChordItem>().Select(item => item.Label), 260);
            _contextMenuChord.Width = max;
            _contextMenuChord.DropDownWidth = max;
            _loading = loading;
        }

        /// <summary>The two drop-downs that are filled once or by their own page: as wide as their longest entry in this language and display.</summary>
        private void FitDropDowns()
        {
            int language = ComboWidth(Localization.Names, 190);
            if (_uiLanguage.Width != language) _uiLanguage.Width = language;
            // The tile sizes are words of the language on screen; a language switch alone does not rebuild them.
            bool loading = _loading;
            _loading = true;
            try { FillTileSizeCombo(); }
            finally { _loading = loading; }
            int tile = ComboWidth(_tlTileSize.Items.Cast<object>().Select(item => item.ToString() ?? ""), 120);
            if (_tlTileSize.Width != tile) _tlTileSize.Width = tile;
        }

        /// <summary>
        /// The width a drop-down needs for its longest entry: the text in the font that is drawn, plus the arrow and
        /// the text's own padding at the display's scale. The 36 px of chrome used to be a 96 DPI constant, which
        /// at 200 % left the longest entry under its own arrow (S0060 P7-f); the nominal width is a floor.
        /// </summary>
        internal int ComboWidth(IEnumerable<string> labels, int nominalLogical)
        {
            int dpi = DeviceDpi;
            int width = ControlDpi.Scale(dpi, nominalLogical);
            int chrome = ControlDpi.Scale(dpi, 36);
            foreach (string label in labels)
                width = Math.Max(width, TextRenderer.MeasureText(label, Font).Width + chrome);
            return width;
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
            row.Controls.Add(new Label { Text = "Mouse chord:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
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
            // A field being typed in keeps what is on screen (S0060 U-L6): the value in the config is the
            // one before the user's edit, and the edit is committed on Leave or when the window hides.
            if (!IsBeingEdited(_translateEndpoint, _translateEndpointUncommitted)) _translateEndpoint.Text = _config.TranslateEndpoint;
            if (_translateModel.Items.Count == 0)
                foreach (string model in TranslationService.RecommendedModels) _translateModel.Items.Add(model);
            if (!IsBeingEdited(_translateModel, _translateModelUncommitted)) _translateModel.Text = _config.TranslateModel;
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

        // An edit of a free-text translator field that was typed but not committed yet (see ReloadTranslateState).
        private bool _translateEndpointUncommitted, _translateModelUncommitted;

        /// <summary>A text field is the user's while it holds the focus or holds an edit nobody has committed.</summary>
        internal static bool IsBeingEdited(Control field, bool uncommitted) => uncommitted || field.ContainsFocus;

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

        /// <summary>The narrowest wrap a text block degrades to before one-word lines.</summary>
        internal const int MinTextWidth = 220;

        /// <summary>
        /// Clamps every wrapping text block (page descriptions, hints, the muted prose labels) to the
        /// width its page actually has (H3): the authored caps (890/930 px) assumed the default window,
        /// and on a narrow window a hint kept overflowing into a horizontal scrollbar. Re-run on every
        /// language pass and every page resize; wider windows follow the available content space.
        /// </summary>
        private void ClampPageTextWidths()
        {
            if (_tabs == null) return;
            foreach (TabPage page in _tabs.TabPages)
                ClampPage(ContentPanel(page));
        }

        /// <summary>
        /// One page's share of <see cref="ClampPageTextWidths"/>. A page panel's own Resize re-clamps only
        /// that page: it used to re-clamp all thirteen, and showing the window resizes every panel several
        /// times, so the first open spent about four seconds measuring the same captions over and over.
        /// A panel that did not change size has nothing new to clamp to.
        /// </summary>
        private void ClampPage(FlowLayoutPanel flow)
        {
            _staleClampPanels.Remove(flow);
            PageClampCount++;
            _editorColumn = WidestEditors(flow);
            ClampTextWidths(flow, flow.ClientSize.Width - flow.Padding.Horizontal);
        }

        /// <summary>
        /// Pages whose panel changed size while another page was the selected one: they are clamped when they
        /// are selected, not at that moment. Opening the window resizes all thirteen panels, and a clamp
        /// measures every caption of its page (about a tenth of a second each) - twelve of them for pages
        /// nobody is looking at.
        /// </summary>
        private readonly HashSet<FlowLayoutPanel> _staleClampPanels = new HashSet<FlowLayoutPanel>();

        /// <summary>How many single-page clamps have run; the tests hold the open of the window to a count.</summary>
        internal int PageClampCount { get; private set; }

        private void ClampPageOnResize(FlowLayoutPanel panel)
        {
            if (_tabs != null && panel.Parent is TabPage page && !ReferenceEquals(_tabs.SelectedTab, page))
            {
                _staleClampPanels.Add(panel);
                return;
            }
            ClampPage(panel);
        }

        private void ClampTextWidths(Control container, int available)
        {
            foreach (Control child in container.Controls)
            {
                if (child is Label label && label.MaximumSize.Width > 0)
                {
                    int wanted = Math.Max(MinTextWidth, available - child.Margin.Horizontal - child.Padding.Horizontal);
                    if (label.MaximumSize.Width != wanted) label.MaximumSize = new Size(wanted, 0);
                }
                if (child is TableLayoutPanel { Tag: CaptionedRow captioned }) { FitCaptionedRow(captioned, available); continue; }
                // Inside a group the budget shrinks by the group's own insets; a page's children pass
                // the page's. Recurse only into containers that lay their children out top-down.
                if (child is CollapsibleGroup group) { group.FitWidth(available - group.Margin.Horizontal); ClampTextWidths(group.Body, available - group.Margin.Horizontal - group.Body.Margin.Horizontal - group.Body.Padding.Horizontal); }
                else if (child is FlowLayoutPanel flow && flow.FlowDirection == FlowDirection.TopDown)
                    ClampTextWidths(flow, available - flow.Margin.Horizontal - flow.Padding.Horizontal);
                else if (child is FlowLayoutPanel commands)
                    CommandPanel.Fit(commands, Math.Max(1, available - commands.Margin.Horizontal));
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
                AlignChordColumn();
                ReassertCommandMetrics();
                RescaleExchangeMarks();
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

        /// <summary>
        /// Whether Escape, arriving at the window while <paramref name="active"/> holds the focus, leaves the
        /// window (APP-BEHAVIOUR rule 1, APP-SETTINGS rule 9) - the one decision <see cref="ProcessCmdKey"/> takes.
        /// A drop-down list that is open takes the key first: it closes, and the choice it was about to make is
        /// not thrown away with the window. Anything else - a closed list, an edit box, a button, nothing - leaves.
        /// Pure, so the rule is held without a live window and an open list.
        /// </summary>
        internal static bool EscapeLeavesWindow(Control? active) => !(active is ComboBox { DroppedDown: true });

        /// <summary>The settings window closes (hides) on Escape per APP-BEHAVIOUR rule 1 and APP-SETTINGS rule 9.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // An open drop-down list closes first: WinForms offers the key here before the combo box
                // sees it, so without this a reflexive Escape would hide the window over a pending choice.
                if (!EscapeLeavesWindow(ActiveControl)) return base.ProcessCmdKey(ref msg, keyData);
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

        /// <summary>Every user-facing string here is keyed by its English original - see <see cref="Localization"/>.</summary>
        private string Translate(string en) => Localization.Translate(_config.UiLanguage, en);

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
                // Both fonts are ours: WinForms disposes neither the font assigned to a control nor
                // the one it replaces. A replaced font is *retired*, not disposed here (CRASH-1,
                // S0066 L-1): the tree walk below re-points the controls that are attached, but a
                // row built outside the tree (every reload builds its rows unparented) would keep
                // the outgoing instance, and a handle created under a disposed font is the field
                // crash of 2026-10-09 ended by. Retirement is bounded by the family changes a user
                // makes and ends at window teardown, where no row outlives the window.
                Font? previousOwn = _ownFont;
                Font? previousBold = _boldFont;
                Font = _ownFont = new Font(wanted, MessageBoxFont.SizeInPoints);
                _boldFont = null;       // BoldFont re-derives itself from the new font on first use
                Font? previousTitle = _titleFont;
                _titleFont = null;
                RefreshBoldFonts(this);
                if (_headerTitle != null) _headerTitle.Font = TitleFont;
                if (previousOwn != null) _retiredFonts.Add(previousOwn);
                if (previousBold != null) _retiredFonts.Add(previousBold);
                if (previousTitle != null) _retiredFonts.Add(previousTitle);
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
            var page = Page("Windows languages", "Keyboard layouts, their order and the switch shortcuts - what the Windows «Language & region» pane used to do.");
            var panel = ContentPanel(page);

            panel.Controls.Add(Group("languages/layouts", "Keyboard layouts",
                _layoutRows,
                new Label { Text = "The arrows reorder the layouts within one language. The order of the languages themselves belongs to Windows, which restores it at sign-in — change it in the Windows settings.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 2, 3, 2) },
                new Label { Text = "The layouts already ship with Windows - nothing is downloaded.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 2) },
                LayoutButtonsRow(),
                new Label { Text = "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian and Ukrainian. For popular variants (for example US International and Spanish Latin American), use «Add layout...».", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 5) }));

            panel.Controls.Add(Group("languages/toggle", "Cycle switch",
                Setting(ToggleRow(), "One chord cycles through the installed languages. This is the built-in Windows setting (Alt+Shift, Ctrl+Shift or «`»); «—» turns cycling off.")));

            panel.Controls.Add(Group("languages/hotkeys", "Direct per-language shortcuts",
                new Label { Text = "Windows itself handles these shortcuts: they work even when CyrFlip is closed. You choose the combination yourself — unlike the built-in dialog, it is not limited to Ctrl+Shift+digit.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 4) },
                _languageRows,
                Setting(_restoreLanguageHotkeys, "Returns the Windows language shortcuts to the state they were in before CyrFlip first changed them.")));

            // The deep link into the system's own page (APP-SETTINGS rule 9) and the Store build's warning about
            // the registry redirection that it answers: a group like the others (S0060 M4) - the button is a
            // command, so it stands in a group with its hint, not loose on the page.
            var windowsRows = new List<Control>();
            if (PackageInfo.IsPackaged)
                windowsRows.Add(new Label
                {
                    Text = "The Microsoft Store build runs in a container, so Windows may redirect the registry write into the package. If a layout or shortcut is not picked up even after signing out and back in, set it in the Windows settings using the button below.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.Warning, Margin = new Padding(3, 4, 3, 6),
                });
            windowsRows.Add(Setting(Button("Open Windows settings", AppGlyphs.Settings, () => OpenAddress("ms-settings:keyboard")),
                "Opens the Windows settings, where a display language (the translation of Windows itself) is installed."));
            panel.Controls.Add(Group("languages/windows", "Windows settings", windowsRows.ToArray()));
            return page;
        }

        private FlowLayoutPanel LayoutButtonsRow()
        {
            var layoutButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 4, 3, 2) };
            layoutButtons.Controls.Add(Button("Add layout...", AppGlyphs.Add, OnAddLayout));
            layoutButtons.Controls.Add(Button("Add popular languages", AppGlyphs.Add, AddPopularLayouts));
            layoutButtons.Controls.Add(_restoreLayouts);
            return layoutButtons;
        }

        private FlowLayoutPanel ToggleRow()
        {
            var toggleRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            toggleRow.Controls.Add(new Label { Text = "Chord to cycle languages:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 6, 8, 0) });
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
            // The page of "fix mistyped text" (S0062 D2): the layout table, and the case fix beside it.
            _conversionRows.Name = "setting.conversion-table";
            _convertSymbols.Name = "setting.convert-symbols";
            _caseEnabled.Name = "setting.case-fix-chord";
            var page = Page("Text correction", "Text typed in the wrong layout or with CapsLock on is fixed with one hotkey.");
            var panel = ContentPanel(page);
            panel.Controls.Add(Group("conversions/table", "Layout table",
                new Label { Text = "A table of conversions: as many layout pairs as you like, each with its own hotkey. The EN ⇄ RU row on Ctrl+Shift+F12 is created on the first run — you can edit or delete it like any other. Every pair works both ways: when the second layout of the pair is active, the same shortcut converts back. Text is converted by the physical keys of the chosen layouts.", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 4) },
                _conversionRows,
                Button("Add conversion...", AppGlyphs.Add, AddConversionProfile),
                new Label { Text = "Only layouts installed in Windows work here — add the ones you need on the «Windows languages» tab. One shortcut cannot be shared by two CyrFlip actions.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 8, 3, 4) }));
            panel.Controls.Add(Group("conversions/after", "After conversion",
                Setting(_language, "After a conversion, switches the active window to the layout the text now reads in, so you can carry on typing right away.")));
            panel.Controls.Add(Group("conversions/symbols", "Punctuation",
                Setting(_convertSymbols, "A key that gives a symbol rather than a letter in both layouts says nothing about the layout it was pressed in: the Russian key behind «/» carries a full stop, and the «/» of the numeric keypad is the same everywhere and cannot be told from the ordinary one once the text is copied. While this is ticked, such symbols are converted along with the text - the way CyrFlip has always behaved. Clear it if you type them on purpose more often: «/ghbdtn» then keeps its slash instead of starting with a full stop. Symbols whose key carries a letter in the other layout (the comma, the brackets) are converted either way.")));
            // The case fix is a chord and a CapsLock sync, and now sits as one feature (S0062 2A, L14, L17).
            panel.Controls.Add(Group("conversions/case", "Letter case",
                HotkeyRow(_caseEnabled, _caseHotkeyValue, _setCaseHotkey, "Swaps upper and lower case in the selection; useful after accidentally enabling CapsLock."),
                Setting(_caps, "Changes the physical CapsLock state after case correction, so subsequent keystrokes match the corrected text.")));
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

        // Anchored to the left only: in a row of a flow that is taller than the label (a 32 px command beside it) the
        // label stands on the row's centre line instead of its top edge.
        private Label ColumnLabel(string text, int width, bool ellipsis = true)
            => new Label
            {
                Text = text, AutoSize = false, Width = width, Height = RowHeight,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = ellipsis,
                Margin = new Padding(3, 4, 3, 4), Anchor = AnchorStyles.Left,
            };

        /// <summary>
        /// The edge of a row's glyph-only arrow, device px: the command metric's height (32 logical px, above the
        /// 28 px pointer floor of ICON-RENDER section 3 rule 5), so an arrow is as tall as the buttons beside it.
        /// </summary>
        private int CommandEdge => ControlDpi.Scale(DeviceDpi, Math.Max(MinTarget, ButtonGlyphs.CommandHeightLogical));

        /// <summary>A move arrow of a table row or of the launcher's buttons: a square of <see cref="CommandEdge"/>, on the row's centre line.</summary>
        private void AsRowArrow(Button button)
        {
            int edge = CommandEdge;
            button.AutoSize = false;
            button.Size = new Size(edge, edge);
            button.Margin = new Padding(4, 3, 4, 3);
            button.Anchor = AnchorStyles.Left;
        }

        /// <summary>
        /// The command metric as the window has it now (S0060 M12): every glyph button re-derives its padding,
        /// height and gap for the DPI and font in force, and the glyph-only arrows - which are not
        /// <c>ButtonGlyphs</c> buttons but a picture on a plain one - take the square edge again.
        /// </summary>
        private void ReassertCommandMetrics()
        {
            ReassertButtons(this);
            int edge = CommandEdge;
            foreach (Button arrow in _moveButtons.ToArray())
            {
                if (arrow.AutoSize) continue;
                if (arrow.Size.Width != edge || arrow.Size.Height != edge) arrow.Size = new Size(edge, edge);
            }
        }

        private static void ReassertButtons(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Button button) ButtonGlyphs.Reassert(button);
                if (child.HasChildren) ReassertButtons(child);
            }
        }

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
            _pairColumn = Math.Max(Column(390), TextWidth(Translate("Layout pair"), bold));
            _chordColumn = Math.Max(Column(145), TextWidth(Translate("Shortcut"), bold));
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
                    Text = Translate("The table is empty — no shortcut converts text right now."),
                    AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(25, 6, 3, 4),
                });
            _conversionRows.ResumeLayout();
            focus.Restore(_conversionRows, TakeRowFocusDelta());
        }

        private Control ConversionHeaderRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row.Controls.Add(new Label { Text = "", AutoSize = false, Width = 22, Height = 18 });
            Label pair = ColumnLabel(Translate("Layout pair"), _pairColumn);
            Label chord = ColumnLabel(Translate("Shortcut"), _chordColumn, ellipsis: false);
            // Derived from the form's font, not from MessageBoxFont: the header has to follow the family
            // the current language needs, and keep the row height the labels were built with.
            pair.Font = chord.Font = BoldFont;
            row.Controls.Add(pair); row.Controls.Add(chord);
            return row;
        }

        private Control ConversionRow(LayoutConversionProfile profile)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            var enabled = new RowCheckBox { Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 0, 2, 0), Anchor = AnchorStyles.Left };
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
            row.Controls.Add(Button(Translate("Change..."), AppGlyphs.Edit, () => EditConversionProfile(profile)));
            row.Controls.Add(Button(Translate("Delete"), AppGlyphs.Delete, () => RemoveConversionProfile(profile)));
            NameRow(row, LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid) + " " + profile.Hotkey);
            return row;
        }

        // A row is a pair of layouts and a chord the user chose, and the window has no undo - so the
        // delete is confirmed like every other irreversible one (APP-BEHAVIOUR rule 5, safe answer default).
        private void RemoveConversionProfile(LayoutConversionProfile profile)
        {
            string pair = LayoutName(profile.SourceKlid) + " ⇄ " + LayoutName(profile.TargetKlid);
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Delete the conversion row \"{0}\"? This cannot be undone.", pair),
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
            return (code.Length > 0 ? code : klid) + " " + Translate("(not installed)");
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
            if (layouts.Count < 2) { Warn(Translate("Install at least two layouts to create a conversion.")); return; }
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
            // Same rule as the language rows: only the bold case is an explicit font (CRASH-1, S0066 L-1).
            if (layout.IsDefault) nameLabel.Font = BoldFont;
            row.Controls.Add(nameLabel);

            var up = Button("↑", () => MoveLayout(layout.Klid, -1));
            up.AccessibleName = Translate("Move up");
            AsMoveGlyph(up, AppGlyphs.MoveUp);
            // Only within one language: the order of the languages is Windows' own and is put back at
            // sign-in (ticket S0007, WL-8) - the hint under the table says so.
            AsRowArrow(up); up.Enabled = InputLayouts.CanMove(all, index, -1);
            var down = Button("↓", () => MoveLayout(layout.Klid, +1));
            down.AccessibleName = Translate("Move down");
            AsMoveGlyph(down, AppGlyphs.MoveDown);
            AsRowArrow(down); down.Enabled = InputLayouts.CanMove(all, index, +1);
            row.Controls.Add(up);
            row.Controls.Add(down);

            var makeDefault = Button(Translate("Set default"), AppGlyphs.Pin, () => MakeDefaultLayout(layout.Klid));
            makeDefault.Enabled = !layout.IsDefault;
            row.Controls.Add(makeDefault);

            var remove = Button(Translate("Delete"), AppGlyphs.Delete, () => OnRemoveLayout(layout));
            remove.Enabled = total > 1; // Windows always keeps at least one layout
            row.Controls.Add(remove);

            row.Controls.Add(new Label { Text = layout.Klid, AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(8, 0, 0, 0), Anchor = AnchorStyles.Left });
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
            if (ReportLayoutEdit(InputLayouts.Move(klid, delta), () => MoveLayout(klid, delta))) _rowFocusDelta = delta; // the focus follows the moved layout
            LanguageHotkeys.ApplyToWindows();
            ReloadLayoutRows();
        }

        private void MakeDefaultLayout(string klid)
        {
            if (!EnsureSystemBackups()) return;
            if (ReportLayoutEdit(InputLayouts.MakeDefault(klid, out string? writtenOverride), () => MakeDefaultLayout(klid)) && writtenOverride != null)
            {
                _config.InputMethodOverrideWritten = writtenOverride;
                SaveConfig();
            }
            LanguageHotkeys.ApplyToWindows();
            ReloadLayoutRows();
        }

        /// <summary>
        /// Says why a layout edit wrote nothing; true when it went through. A write that failed, or a list that
        /// could not be read, is a failure of what the user asked for and is presented as actions
        /// (<paramref name="retry"/> is what retrying means); a rule - the last layout, an IME - is a plain warning.
        /// </summary>
        private bool ReportLayoutEdit(InputLayouts.EditResult result, Action? retry = null)
        {
            switch (result)
            {
                case InputLayouts.EditResult.Ok:
                    return true;
                case InputLayouts.EditResult.Failed:
                    Fail(FailureCause.Describe(InputLayouts.LastFailure, _config.UiLanguage), retry);
                    return false;
                case InputLayouts.EditResult.LastLayout:
                    Warn(Translate("Windows must keep at least one layout — this one cannot be removed."));
                    return false;
                case InputLayouts.EditResult.ManagedByWindows:
                    Warn(Translate("This is an input method (IME) that Windows manages: removed here, it would come back at the next sign-in. Remove it in the Windows «Language & region» settings."));
                    return false;
                case InputLayouts.EditResult.CrossLanguage:
                    // The arrows are greyed for this; reaching it means the stores changed under the window.
                    return false;
                default:
                    Fail(Translate("The Windows layout list could not be read in full — CyrFlip changed nothing."), retry);
                    return false;
            }
        }

        private void OnAddLayout()
        {
            var installedKlids = new List<string>();
            foreach (InputLayouts.Installed i in InputLayouts.ListInstalled()) installedKlids.Add(i.Klid);

            using var picker = new LayoutPickerDialog(installedKlids, _config.UiLanguage);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKlid == null) return;
            AddLayout(picker.SelectedKlid);
        }

        /// <summary>Installs one chosen layout. Its own method so a failure's Retry repeats the write, not the picker.</summary>
        private void AddLayout(string klid)
        {
            if (!EnsureSystemBackups()) return;
            if (!ReportLayoutEdit(InputLayouts.Add(klid), () => AddLayout(klid))) return;
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        /// <summary>
        /// "Add popular languages": what would be added is worked out first, off the hooks' thread behind the
        /// wait window; with nothing to add the answer is said, not silence; the layouts are named before they
        /// are written; the writing is one more job behind the wait window; and the result is counted after
        /// (APP-BEHAVIOUR rules 3 and 5, S0060 P7-h).
        /// </summary>
        private void AddPopularLayouts()
        {
            string language = _config.UiLanguage;
            PopularLayoutPlan plan = BusyDialog.Run(this, language, () => PopularLayoutPlan.Build(
                InputLayouts.ListInstalled().Select(layout => layout.Klid), InputLayouts.ListAvailable(), WorldLayouts.Popular));
            if (plan.ToAdd.Count == 0)
            {
                ConfirmDialog.Show(this, language, Translate("Nothing to add: the popular layouts are already installed."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string names = string.Join("\n", plan.ToAdd.Select(layout =>
                layout.LanguageName + (layout.DisplayName.Length > 0 ? " — " + layout.DisplayName : "")));
            if (ConfirmDialog.Show(this, language,
                    Localization.Format(Translate, "Add {0} keyboard layouts to Windows?", plan.ToAdd.Count) + "\n\n" + names,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (!EnsureSystemBackups()) return;

            // One job for the whole batch: the writes share the registry stores and must keep their order.
            (int Added, InputLayouts.EditResult Stopped) outcome = BusyDialog.Run(this, language, () =>
            {
                int done = 0;
                foreach (InputLayouts.Available layout in plan.ToAdd)
                {
                    InputLayouts.EditResult result = InputLayouts.Add(layout.Klid);
                    if (result != InputLayouts.EditResult.Ok) return (done, result);
                    done++;
                }
                return (done, InputLayouts.EditResult.Ok);
            });

            LanguageHotkeys.ApplyToWindows();
            Reload();
            if (outcome.Stopped != InputLayouts.EditResult.Ok)
            {
                // What was added stays added; the retry plans again and goes on with the rest.
                ReportLayoutEdit(outcome.Stopped, AddPopularLayouts);
                return;
            }
            string summary = Localization.Format(Translate, "Added: {0}. Skipped: {1}.", outcome.Added, plan.Skipped);
            if (outcome.Added > 0 && !_layoutNoticeShown)
            {
                // The one-time "may need a sign-out" notice rides on the summary: one box, not two.
                _layoutNoticeShown = true;
                summary += "\n\n" + Translate("The change has been applied to the Windows layouts. It usually takes effect immediately; if something looks off, sign out of Windows and back in.");
            }
            ConfirmDialog.Show(this, language, summary, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnRemoveLayout(InputLayouts.Installed layout)
        {
            // Said before the question, not after it (ticket S0007, WL-9).
            if (layout.ManagedByWindows) { ReportLayoutEdit(InputLayouts.EditResult.ManagedByWindows); return; }
            if (ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Remove the «{0}» layout from Windows?", layout.LanguageName + " — " + layout.DisplayName), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            RemoveLayout(layout.Klid);
        }

        /// <summary>Removes a confirmed layout. Its own method so a failure's Retry repeats the write, not the question.</summary>
        private void RemoveLayout(string klid)
        {
            if (!EnsureSystemBackups()) return;
            if (!ReportLayoutEdit(InputLayouts.Remove(klid), () => RemoveLayout(klid))) return;
            LanguageHotkeys.ApplyToWindows();
            Reload();
            NotifyLayoutChangeOnce();
        }

        private void OnToggleChanged()
        {
            if (_loading || !(_toggleCombo.SelectedItem is ToggleItem item)) return;
            ApplyToggle(item.Code);
        }

        /// <summary>Writes the cycle-switch chord. Its own method so a failure's Retry repeats this write.</summary>
        private void ApplyToggle(string code)
        {
            if (!EnsureSystemBackups()) { ReloadToggleCombo(); return; }
            bool applied = LanguageHotkeys.SetToggle(code);
            LanguageHotkeys.ApplyToWindows();
            ReloadToggleCombo();
            if (!applied) Fail(FailureCause.Describe(LanguageHotkeys.LastFailure, _config.UiLanguage), () => ApplyToggle(code));
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
            int max = ComboWidth(_toggleCombo.Items.Cast<ToggleItem>().Select(item => item.Label), 200);
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
            if (_config.IsUnreadable("*"))
            {
                Fail(Translate("Some CyrFlip settings could not be read and were left as they are."));
                return false;
            }
            bool changed = false, ok = true;
            bool needLayouts = NeedsBackup(_config.InputLayoutsBackup, "InputLayoutsBackup");
            bool needHotkeys = NeedsBackup(_config.LanguageHotkeysBackup, "LanguageHotkeysBackup");
            if (needLayouts || needHotkeys)
            {
                // Two whole registry trees: read on the pool behind the wait window (a job that is over within
                // BusyDialog.GraceMs shows nothing), not on the thread both low-level hooks share (S0060 h).
                (string Layouts, string Hotkeys) taken = BusyDialog.Run(this, _config.UiLanguage, () =>
                    (needLayouts ? InputLayouts.BackupAll() : "", needHotkeys ? LanguageHotkeys.BackupAll() : ""));
                if (needLayouts)
                {
                    _config.InputLayoutsBackup = taken.Layouts;
                    changed |= _config.InputLayoutsBackup.Length > 0;
                    ok &= _config.InputLayoutsBackup.Length > 0;
                }
                if (needHotkeys)
                {
                    _config.LanguageHotkeysBackup = taken.Hotkeys;
                    changed |= _config.LanguageHotkeysBackup.Length > 0;
                    ok &= _config.LanguageHotkeysBackup.Length > 0;
                }
            }
            if (changed) SaveConfig();
            if (!ok) Fail(Translate("The original state of the Windows settings could not be saved — CyrFlip changed nothing."));
            return ok;
        }

        private bool NeedsBackup(string backup, string valueName)
            => backup.Length == 0 && !_config.IsUnreadable(valueName);

        /// <summary>The once-latch of the settings-save notice (S0065 E-9), pure so the policy is testable.</summary>
        internal static class SaveNotice
        {
            /// <summary>
            /// True exactly once per run of failed saves: a machine whose registry is denied must not
            /// hear the same warning on every toggle - a dozen identical warnings is one nobody
            /// reads. A successful save re-arms it.
            /// </summary>
            public static bool ShouldTell(bool saved, ref bool told)
            {
                if (saved) { told = false; return false; }
                if (told) return false;
                told = true;
                return true;
            }
        }

        private bool _saveFailureTold;

        /// <summary>
        /// One place for every settings write (S0065 E-9): <see cref="AppConfig.Save"/> answers, and
        /// this window is the one surface that says it failed - the change still works for the
        /// session, so what the user is told is that it will not survive a restart.
        /// </summary>
        private void SaveConfig()
        {
            if (SaveNotice.ShouldTell(_config.Save(), ref _saveFailureTold))
                Fail(Translate("The setting could not be saved - it will work until CyrFlip is restarted."),
                    () => { _saveFailureTold = false; SaveConfig(); });
        }

        private void NotifyLayoutChangeOnce()
        {
            if (_layoutNoticeShown) return;
            _layoutNoticeShown = true;
            ConfirmDialog.Show(this, _config.UiLanguage, Translate("The change has been applied to the Windows layouts. It usually takes effect immediately; if something looks off, sign out of Windows and back in."),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RestoreLayouts()
        {
            if (_config.InputLayoutsBackup.Length == 0) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Restore the Windows layouts to their original state?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            ApplyLayoutRestore();
        }

        /// <summary>The confirmed restore. Its own method so a failure's Retry repeats the write, not the question.</summary>
        private void ApplyLayoutRestore()
        {
            if (!InputLayouts.RestoreAll(_config.InputLayoutsBackup, _config.InputMethodOverrideWritten))
            {
                Fail(InputLayouts.LastFailure != null ? FailureCause.Describe(InputLayouts.LastFailure, _config.UiLanguage)
                    : Translate("The backup could not be read — nothing was changed."), ApplyLayoutRestore);
                return;
            }
            _config.InputMethodOverrideWritten = "";
            SaveConfig();
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

            Label valueLabel = ColumnLabel(entry != null ? entry.Display : Translate("Not assigned"), _languageChordColumn, ellipsis: false);
            // A row never takes the form's own Font instance explicitly (CRASH-1, S0066 L-1): an
            // explicit reference survives the family swap as a pointer at the font being retired,
            // while an ambient font is re-resolved through the parent chain - and a row built
            // outside the tree has no parent chain to cover it.
            if (entry != null) valueLabel.Font = BoldFont;
            if (entry == null) valueLabel.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(valueLabel);
            row.Controls.Add(Button(Translate("Assign..."), AppGlyphs.Edit, () => AssignLanguageHotkey(hkl)));

            Button clear = Button(Translate("Clear"), AppGlyphs.ClearInput, () => ClearLanguageHotkey(hkl));
            clear.Enabled = entry != null;
            row.Controls.Add(clear);

            // The raw HKL is the only always-correct way to tell two layouts of one language apart.
            row.Controls.Add(new Label { Text = LanguageHotkeys.HklText(hkl), AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(8, 0, 0, 0), Anchor = AnchorStyles.Left });
            NameRow(row, LanguageHotkeys.LanguageName(hkl) + (layout.Length > 0 ? " - " + layout : ""));
            return row;
        }

        private Control OrphanHotkeyRow(LanguageHotkeys.Entry entry)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            Label orphanLabel = ColumnLabel(Localization.Format(Translate, "{0} → layout {1} (not installed)", entry.Display, LanguageHotkeys.HklText(entry.TargetHkl)), Column(455));
            orphanLabel.ForeColor = ThemePalette.Light.TextMuted;
            row.Controls.Add(orphanLabel);
            row.Controls.Add(Button(Translate("Delete"), AppGlyphs.Delete, () => RemoveOrphanHotkey(entry.Id)));
            NameRow(row, entry.Display + " - " + LanguageHotkeys.HklText(entry.TargetHkl));
            return row;
        }

        // The chord column of the language-hotkey rows, measured on every refresh like the conversion
        // table's (see MeasureConversionColumns): a fixed 165 px clipped "Ctrl+Shift+Alt+F12" at 125 %.
        private int _languageChordColumn;

        private void MeasureLanguageHotkeyColumn(List<LanguageHotkeys.Entry> entries)
        {
            _languageChordColumn = Math.Max(Column(165), TextWidth(Translate("Not assigned"), Font));
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
            // Ask first, back up after the answer (S0060 U-L1): a Cancel must leave nothing written - the one-time
            // snapshot is still taken before the write, which is all the backup rule asks.
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Translate("Delete saved shortcut for the uninstalled layout?"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            if (!EnsureSystemBackups()) return;
            LanguageHotkeys.Remove(id);
            LanguageHotkeys.ApplyToWindows();
            ReloadLanguageRows();
        }

        private void AssignLanguageHotkey(IntPtr hkl)
        {
            LanguageHotkeys.Entry? current = LanguageHotkeys.FindFor(hkl);
            using var dialog = new HotkeyDialog(current?.Display ?? "", Translate("Shortcut for switching to this language"), _config.UiLanguage);
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
                Warn(Localization.Format(Translate, "The {0} combination is already used by the CyrFlip hotkey «{1}» — Windows would never receive it.", chord.Display, taken));
                return;
            }

            // Capture the pre-CyrFlip state once, before the first write of this feature ever lands.
            if (!EnsureSystemBackups()) return;

            LanguageHotkeys.AssignStatus status = LanguageHotkeys.Assign(hkl, chord, out string conflict);
            switch (status)
            {
                case LanguageHotkeys.AssignStatus.ChordTaken:
                    Warn(Localization.Format(Translate, "The {0} combination is already assigned to «{1}».", chord.Display, conflict));
                    return;
                case LanguageHotkeys.AssignStatus.NoFreeSlot:
                    Warn(Translate("Windows has no free slots left for language shortcuts."));
                    return;
                case LanguageHotkeys.AssignStatus.Failed:
                    Fail(Translate("Could not write the shortcut to the Windows registry."), () => AssignLanguageHotkey(hkl));
                    return;
            }

            LanguageHotkeys.ApplyToWindows();
            ReloadLanguageRows();

            if (!_languageHotkeyNoticeShown)
            {
                _languageHotkeyNoticeShown = true;
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("The shortcut has been written to the Windows settings — the system will handle it, not CyrFlip. If it does not work right away, sign out of Windows and back in."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RestoreLanguageHotkeys()
        {
            if (_config.LanguageHotkeysBackup.Length == 0) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Restore the Windows language shortcuts to their original state?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            if (!LanguageHotkeys.RestoreAll(_config.LanguageHotkeysBackup))
            {
                Fail(Translate("The backup could not be read — nothing was changed."));
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

        /// <summary>A validation or rule warning: the request was understood and refused, and there is nothing to retry.</summary>
        private void Warn(string message)
            => ConfirmDialog.Show(this, _config.UiLanguage, message, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        /// <summary>
        /// A failure of something the user asked for, as actions (APP-BEHAVIOUR rule 6, WINDOWS-UI section 7):
        /// Retry - when the caller can say what retrying is - Open log folder, Close. The message is a named
        /// cause from <see cref="FailureCause"/>, never an exception's text; the details are in the logs.
        /// </summary>
        private void Fail(string message, Action? retry = null)
            => ConfirmDialog.ShowFailure(this, _config.UiLanguage, message, retry);

        /// <summary>
        /// Hands an address - a web page, a <c>ms-settings:</c> page - to the shell. A refusal is said with its
        /// cause and the address itself, so the user can copy it from the box (Ctrl+C) and open it by hand (S0060 e).
        /// </summary>
        private void OpenAddress(string target)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex)
            {
                Fail(Localization.Format(Translate, "Could not open the address: {0}", FailureCause.Describe(ex, Translate)) + "\n\n" + target,
                    () => OpenAddress(target));
            }
        }

        /// <summary>
        /// "Меню для выделенного" (S0062 2A): CyrFlip's own menu over the selected text, its mouse chord and its
        /// keyboard chord - the three settings that sat among the keyboard chords on the old Hotkeys page.
        /// The switch of the module leads; the keyboard chord is greyed with it (<see cref="Reload"/>).
        /// </summary>
        private TabPage SelectionMenuPage()
        {
            _contextMenuEnabled.Name = "setting.context-menu";
            _contextMenuChord.Name = "setting.mouse-chord";
            _textMenuHotkeyEnabled.Name = "setting.keyboard-menu-chord";
            var page = Page("Selection menu", "CyrFlip's own menu over the selected text: copy, cut, paste, conversion, case, translation and quick launch.");
            ContentPanel(page).Controls.Add(Group("selection/mouse", "Menu over a selection",
                new Label { Text = "Off by default: while it is off, CyrFlip does not watch the mouse.", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 2) },
                Setting(_contextMenuEnabled, "The mouse chord opens CyrFlip's menu next to the pointer: copy, cut, paste, layout conversion, letter case, translation, quick launch. The application's own menu does not appear. While the box is clear, CyrFlip does not watch the mouse at all."),
                Setting(ContextMenuChordRow(), "Ctrl with the right button is claimed by nothing in Windows. Shift with the right button takes the extended menu away from File Explorer, and the middle button takes away autoscroll and opening a link in a new tab.")));
            ContentPanel(page).Controls.Add(Group("selection/keyboard", "From the keyboard",
                HotkeyRow(_textMenuHotkeyEnabled, _textMenuHotkeyValue, () => TextMenuHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "The same menu from the keyboard: it opens at the text cursor, or at the mouse pointer where the cursor cannot be found. The arrows pick an item, Enter runs it, Esc closes the menu.")));
            return page;
        }

        private TabPage ClipboardPage()
        {
            var page = Page("Clipboard", "History is stored locally only and encrypted with Windows DPAPI for your account.");
            var panel = ContentPanel(page);
            _history.Name = "setting.history-on";
            _pause.Name = "setting.history-pause";
            _historyStartup.Name = "setting.history-remember-window";
            _historyEnabled.Name = "setting.history-chord";
            _opacity.Name = "setting.history-opacity";
            panel.Controls.Add(Group("clipboard/recording", "Recording",
                Setting(_history, "Off by default: while off, nothing is kept. When on: text only, never images or files, and nothing is trimmed."),
                Setting(_pause, "Temporarily stops capturing new copies without deleting saved history. Useful while handling passwords or private data.")));

            var opacityLine = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 7, 0, 0) };
            opacityLine.Controls.Add(new Label { Text = "History window transparency:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 5, 0) });
            opacityLine.Controls.Add(_opacity); opacityLine.Controls.Add(_opacityValue);
            Button searchHistory = Button("Search history", AppGlyphs.Search, _openHistorySearch);
            searchHistory.Name = "setting.history-search";
            // The chord that opens the window leads its group (S0062 L18); the window's own options follow.
            panel.Controls.Add(Group("clipboard/window", "History window",
                HotkeyRow(_historyEnabled, _historyHotkeyValue, _setHistoryHotkey, "Shows or hides the text-history window. Two CyrFlip actions cannot share one shortcut."),
                Setting(_historyStartup, "Remembers whether the clipboard manager window is open: if you closed it, it stays closed on the next launch (history is still captured in the background)."),
                Setting(opacityLine, "Sets the floating history window opacity from 30% to 100%. Applied immediately."),
                Setting(searchHistory, "Opens a separate window to search by a text fragment. Enter at least three characters to search.")));

            Button clearHistory = Button("Clear all history", AppGlyphs.ClearAll, () =>
            {
                if (_getHistoryCount != null && _getHistoryCount() == 0)
                {
                    ConfirmDialog.Show(this, _config.UiLanguage, Translate("History is empty."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Delete all saved clipboard history?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) == DialogResult.Yes)
                    _clearHistory();
            });
            clearHistory.Name = "setting.history-clear";
            panel.Controls.Add(Group("clipboard/clear", "Clearing",
                Setting(clearHistory, "Deletes every entry from memory and the encrypted local file. This cannot be undone.")));
            Control? transfer = ExchangeRow();
            if (transfer != null)
            {
                transfer.Name = "group.clipboard-transfer";
                panel.Controls.Add(Group("clipboard/transfer", "Moving to another computer", transfer));
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
            row.Controls.Add(Button("Export...", AppGlyphs.Export, () => _exchange(this, false)));
            row.Controls.Add(Button("Import...", AppGlyphs.Import, () => _exchange(this, true)));
            return Setting(row, "One open text file with your notes and the clipboard history you choose: read it in any editor or on a phone, and import it into CyrFlip on another computer. The file is not encrypted. The import shows what will be added and deletes nothing.");
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
            var page = Page("Quick notes",
                "A small local notepad: open it with a chord, drop in a fragment of code or a thought, close it.");
            var panel = ContentPanel(page);

            // The module switch leads in a group of its own, like the hotkeys page's (S0060 M4): a command or an
            // editor never stands loose on a page that is made of groups.
            panel.Controls.Add(Group("notes/switches", "Main switches",
                Setting(_quickNotesEnabled, "While it is off, CyrFlip neither reads nor creates the notes file, the tray has no notes entry and the chord is not bound. Notes already saved stay on disk.")));

            panel.Controls.Add(Group("notes/opening", "How to open it",
                Track(HotkeyRow(_quickNotesHotkeyEnabled, _quickNotesHotkeyValue, _setQuickNotesHotkey,
                    "Creates a new note at once and puts the caret in the body - the thought can be written down without pressing anything else. One chord cannot be given to two CyrFlip actions.")),
                Track(Setting(Button("Open the notes", AppGlyphs.Note, _openQuickNotes),
                    "The same as the \"Quick notes\" entry in the tray menu: it opens the list without creating a new note."))));

            panel.Controls.Add(Group("notes/editor", "Editor",
                new Label { Text = "The text is stored exactly as typed, with no markup, highlighting or auto-formatting, so code pastes back unchanged.", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 2) },
                Track(Setting(_quickNotesWrap,
                    "Off by default: the body of a note is more often code than prose, and a wrapped line of code is one you have to mentally un-wrap. Turn it on if you write prose here.")),
                Track(Setting(_quickNotesLineNumbers,
                    "Shows line numbers next to logical lines. The marker (↳) shows wrapped lines."))));

            panel.Controls.Add(Group("notes/storage", "Export and storage",
                // Where the notes live and what is promised about them - the privacy paragraph that used to trail
                // the page outside every group (S0060 M4).
                new Label
                {
                    Text = "Notes lie only on this computer and are encrypted with Windows DPAPI for your account. CyrFlip does not send them anywhere, does not index them with Windows Search and does not turn clipboard-history entries into notes - there is a separate command for that. This is not a secret store: do not keep passwords, production tokens or private keys here.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 4, 3, 4),
                },
                Track(Setting(_quickNotesExportMeta,
                    "By default only the name and the text are exported, so a fragment of code pastes back without edits. With the box ticked, each note also carries its creation date.")),
                Track(Setting(Button("Export every note to Markdown...", AppGlyphs.Export, ExportQuickNotes),
                    "One file with every note in order. The exported file is no longer protected by DPAPI - anyone with access to the folder can read it.")),
                Track(Setting(Button("Delete every quick note", AppGlyphs.ClearAll, ClearQuickNotes),
                    "Deletes every note, the journal and its backup. There is no undo."))));
            Control? transfer = ExchangeRow();
            if (transfer != null)
                panel.Controls.Add(Group("notes/transfer", "Moving to another computer", transfer));
            return page;
        }

        /// <summary>Remember a control so the master switch can grey the whole group (see Reload).</summary>
        private Control Track(Control control) { _quickNotesControls.Add(control); return control; }

        private void ExportQuickNotes()
        {
            using var dialog = new SaveFileDialog
            {
                Title = Translate("Export every note"),
                Filter = "Markdown (*.md)|*.md",
                FileName = "cyrflip-notes.md",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string path = _exportQuickNotes(dialog.FileName, _quickNotesExportMeta.Checked);
                if (path.Length == 0) return;
                ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Notes saved: {0}", path) + "\n\n"
                    + Translate("This file is not protected by DPAPI - anyone with access to the folder can read it."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Fail(Localization.Format(Translate, "Could not save the file: {0}", FailureCause.Describe(ex, _config.UiLanguage)), ExportQuickNotes);
            }
        }

        private void ClearQuickNotes()
        {
            if (_getQuickNotesCount != null && _getQuickNotesCount() == 0)
            {
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("No notes found."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Translate("Delete every quick note, the journal and its backup? There is no undo."),
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
            _quickNotesLineNumbers.Checked = _config.QuickNotesLineNumbers;
            _quickNotesHotkeyValue.Text = _config.QuickNotesHotkey;
            foreach (Control control in _quickNotesControls) control.Enabled = _config.EnableQuickNotes;
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
            var page = Page("Translation", "Translate the selected text with a local Ollama model; the text goes only to the server below.");
            var panel = ContentPanel(page);

            // The module switch leads in a group of its own, like the hotkeys page's (S0060 M4).
            panel.Controls.Add(Group("translate/switches", "Main switches",
                Setting(_translateEnabled, "While this is off no translation chord is bound, the tray has no translate entry, and CyrFlip opens no network connection at all.")));

            panel.Controls.Add(Group("translate/ollama", "Ollama",
                new Label { Text = "Ollama itself and a language model have to be installed once, with the buttons below.", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 2) },
                Setting(LabeledRow("Server address:", _translateEndpoint),
                    "Empty means http://localhost:11434, that is Ollama on this computer. Enter another address and the selected text will be sent to that machine."),
                Setting(ModelRow(),
                    "Recommended: aya-expanse:8b (~4.7 GB, the default - the best translation of those tested), gemma2:9b (~5 GB). Models under 4 GB failed the Russian and Ukrainian check: take gemma2:2b (~1.5 GB) only if space is tight, and expect mistakes. The model occupies the memory of the Ollama process, not of CyrFlip."),
                TranslateActionsRow(),
                _translateStatus,
                Setting(_translateAutoStart,
                    "If the server does not answer when a translation starts, CyrFlip tries to start it once and waits up to eight seconds."),
                Setting(LabeledRow("Keep the model in memory (min):", _translateKeepAlive),
                    "How long Ollama keeps the model loaded after a translation. -1 keeps it forever, and that is the default: the first load on a machine without a suitable graphics card takes minutes, and paying it again every few minutes is worse than the gigabytes it holds. 0 unloads it at once."),
                Setting(LabeledRow("Wait for the model to load no longer than (s):", _translateTimeout),
                    "This is the allowance for the model's first token, i.e. almost entirely for loading it into memory. After that the translation runs for as long as the model keeps writing: it is only cut off if the model goes silent for 30 seconds, and then whatever it has written stays on screen.")));

            panel.Controls.Add(Group("translate/directions", "Translation directions",
                _translationRows,
                TranslateButton("Add a direction...", AppGlyphs.Add, AddTranslationProfile),
                new Label
                {
                    Text = "\"Interface language\" and \"Active layout language\" are resolved at the moment the chord is pressed. One chord cannot be given to two CyrFlip actions.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 8, 3, 4),
                }));

            panel.Controls.Add(Group("translate/result", "Result",
                Setting(_translateCopy,
                    "The translation lands on the clipboard as an ordinary entry and, when the history is on, is stored there like any other copy."),
                Setting(_translatePaste,
                    "Replaces the selection with the translation. If the focus has moved to another window by the time the answer arrives nothing is pasted, and the window offers to paste it by hand."),
                Setting(_translateShowSource,
                    "Shows the source text at the top of the result window."),
                Setting(LabeledRow("Close the window after (s):", _translateWindowTimeout),
                    "The countdown starts when the translation arrives and pauses while the mouse pointer is over the window. 0 means no timer: the window still closes on Esc, on its close button, or when you move to another window.")));
            return page;
        }

        private FlowLayoutPanel ModelRow()
        {
            var modelRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            modelRow.Controls.Add(new Label { Text = "Model:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 6, 0) });
            modelRow.Controls.Add(_translateModel);
            modelRow.Controls.Add(TranslateButton("Download the model", AppGlyphs.Download, PullTranslateModel));
            return modelRow;
        }

        private FlowLayoutPanel TranslateActionsRow()
        {
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            actions.Controls.Add(TranslateButton("Install Ollama", AppGlyphs.Install, InstallOllama));
            actions.Controls.Add(TranslateButton("Start Ollama", AppGlyphs.Run, StartOllama));
            actions.Controls.Add(TranslateButton("Check the connection", AppGlyphs.TestConnection, CheckOllama));
            // Live only while a download, a model pull or a start runs (S0037 TR-1): a stalled
            // download used to leave every button here disabled until CyrFlip was restarted.
            _translateCancel = Button("Cancel", AppGlyphs.Cancel, CancelTranslateWork);
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
        private Button TranslateButton(string englishText, string glyph, Action action)
        {
            Button button = Button(englishText, glyph, action);
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

        /// <summary>The token for the long Ollama operations; cancelled by the Cancel button and when the window is disposed (hiding hands the work over).</summary>
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

        /// <summary>
        /// The window was hidden. A download or a model pull in flight keeps running and its result is
        /// told through the tray when it ends; with nothing running, or the window being disposed, the
        /// token is cancelled as before (<see cref="WorkHandover"/>).
        /// </summary>
        private void HandOverTranslateWork()
        {
            if (_translateHandover.OnHidden(_translateBusy, windowEnding: IsDisposed || Disposing))
            {
                CancelTranslateWork();
                return;
            }
            _translateStatus.TextChanged -= AnnounceHandedOverTranslateWork;
            _translateStatus.TextChanged += AnnounceHandedOverTranslateWork;
        }

        private void AnnounceHandedOverTranslateWork(object? sender, EventArgs e)
        {
            if (!_translateHandover.ShouldAnnounce(_translateBusy, Visible)) return;
            _translateStatus.TextChanged -= AnnounceHandedOverTranslateWork;
            string text = _translateStatus.Text;
            if (text.Length > 0) TranslateWorkFinishedInBackground?.Invoke(this, text);
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
            // Whatever was typed is now the stored value (or equal to it): the field is the config's again.
            _translateEndpointUncommitted = _translateModelUncommitted = false;
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
            _targetColumn = Math.Max(Column(300), TextWidth(Translate("Into language"), bold));
            _translateChordColumn = Math.Max(Column(145), TextWidth(Translate("Shortcut"), bold));
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
                    Text = Translate("No directions yet - add the first one to assign a chord."),
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
            Label target = ColumnLabel(Translate("Into language"), _targetColumn);
            Label chord = ColumnLabel(Translate("Shortcut"), _translateChordColumn, ellipsis: false);
            target.Font = chord.Font = BoldFont;
            row.Controls.Add(target);
            row.Controls.Add(chord);
            return row;
        }

        private Control TranslationRow(TranslationProfile profile)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            var enabled = new RowCheckBox { Checked = profile.Enabled, AutoSize = true, Margin = new Padding(3, 0, 2, 0), Anchor = AnchorStyles.Left };
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
            row.Controls.Add(ColumnLabel(profile.Hotkey.Length > 0 ? profile.Hotkey : Translate("Not assigned"),
                _translateChordColumn, ellipsis: false));
            row.Controls.Add(Button(Translate("Change..."), AppGlyphs.Edit, () => EditTranslationProfile(profile)));
            row.Controls.Add(Button(Translate("Delete"), AppGlyphs.Delete, () => RemoveTranslationProfile(profile)));
            NameRow(row, TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage) + " " + profile.Hotkey);
            return row;
        }

        // A row of the table is a language and a chord the user chose, and the window has no undo - so the
        // delete is confirmed like every other irreversible one (APP-BEHAVIOUR rule 5, safe answer default).
        private void RemoveTranslationProfile(TranslationProfile profile)
        {
            string label = TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage);
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Delete the translation row \"{0}\"? This cannot be undone.", label),
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
                _translateStatus.Text = Translate("Ollama is already installed - press \"Start Ollama\".");
                return;
            }
            if (!OllamaManager.CanInstallInPlace)
            {
                // MSIX: a Store app must not download and run a third-party installer (spec §5.4).
                _translateStatus.Text = Translate("Opening the Ollama website - install it from there.");
                OllamaManager.OpenWebPage();
                return;
            }
            if (ConfirmDialog.Show(this, _config.UiLanguage, Translate("Download and install Ollama? This is several hundred megabytes and may take a while."),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetTranslateBusy(true);
            _translateStatus.Text = Translate("Downloading Ollama...");
            var download = new OllamaManager.InstallerDownload();
            try
            {
                var progress = new Progress<string>(text => _translateStatus.Text = Translate("Downloading Ollama:") + " " + text);
                download = await OllamaManager.DownloadInstallerAsync(progress, TranslateWorkToken());
            }
            catch (OperationCanceledException)
            {
                // The user closed the window or pressed "Cancel": that is not a failure, and it must
                // not go on to open a browser at ollama.com behind their back.
                SetTranslateBusy(false);
                if (!IsDisposed) _translateStatus.Text = Translate("Download stopped.");
                return;
            }
            catch { /* the network gave up, or went silent (TR-1) - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (download.Status == OllamaManager.InstallerStatus.Ok)
            {
                _translateStatus.Text = Translate("Launching the Ollama installer...");
                OllamaManager.RunInstaller(download);
            }
            else if (download.Status == OllamaManager.InstallerStatus.Untrusted)
            {
                // Deleted, never run (S0010 TD-5); the site is the one source left that the user can judge.
                _translateStatus.Text = Translate("The downloaded installer is not signed by Ollama - it has been deleted. Opening the Ollama website...");
                OllamaManager.OpenWebPage();
            }
            else
            {
                _translateStatus.Text = Translate("The download failed. Opening the Ollama website...");
                OllamaManager.OpenWebPage();
            }
        }

        private async void StartOllama()
        {
            if (_translateBusy) return;
            if (!OllamaManager.IsInstalled())
            {
                _translateStatus.Text = Translate("Ollama is not installed - press \"Install Ollama\".");
                return;
            }

            SetTranslateBusy(true);
            _translateStatus.Text = Translate("Starting Ollama...");
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
            else _translateStatus.Text = Translate("Could not start Ollama.");
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
                    ? Translate("Ollama is not running - press \"Start Ollama\".")
                    : Translate("Ollama was not found - press \"Install Ollama\".");
        }

        private async void PullTranslateModel()
        {
            if (_translateBusy) return;
            string model = _translateModel.Text.Trim();
            if (model.Length == 0)
            {
                _translateStatus.Text = Translate("Enter a model name, for example aya-expanse:8b.");
                return;
            }

            SetTranslateBusy(true);
            var client = new OllamaClient(_config.TranslateEndpoint);
            bool ok = false;
            try
            {
                var progress = new Progress<string>(text => _translateStatus.Text = Translate("Downloading the model:") + " " + text);
                // No timeout on purpose - a multi-gigabyte pull legitimately takes minutes - but a
                // token, so closing the window stops it instead of leaving the tab busy forever.
                ok = await client.PullModelAsync(model, progress, TranslateWorkToken());
            }
            catch (OperationCanceledException) { SetTranslateBusy(false); return; } // window closed
            catch { /* the pull failed - the status line below says so */ }
            finally { SetTranslateBusy(false); }

            if (ok)
            {
                _translateStatus.Text = Translate("Model installed:") + " " + model;
                await FillModelsAsync(client);
            }
            else
            {
                _translateStatus.Text = Translate("Could not download the model. Is Ollama running?");
            }
        }

        /// <summary>The one status line, after a successful probe: what is running and what it has.</summary>
        private async Task ShowServerStateAsync(OllamaClient client)
        {
            List<string> installed = await FillModelsAsync(client);
            _translateStatus.Text = installed.Count > 0
                ? Translate("Ollama is running. Installed models:") + " " + string.Join(", ", installed.ToArray())
                : Translate("Ollama is running but no model is installed - press \"Download the model\".");
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
            var page = Page("Quick launch", "Run your programs, scripts and yt-dlp downloads from the tray, this table, a shortcut or the taskbar.");
            var panel = ContentPanel(page);

            // The absorbed feature keeps the mark it had as a separate program - the same icon the
            // taskbar button shows while the launcher is on. Page() puts the description first, so
            // the header has to be moved above it.
            Control header = LauncherHeader();
            panel.Controls.Add(header);
            panel.Controls.SetChildIndex(header, 0);

            panel.Controls.Add(Group("launcher/enable", "Enable",
                Setting(_launcherEnabled, "Adds a scenario submenu to the tray menu and tasks to the taskbar Jump List (right-click the CyrFlip taskbar icon). Scenarios are stored one XML file each and are kept when the feature is switched off."),
                new Label { Text = "Taskbar button: left-click for the scenario list, right-click for the Windows Jump List.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(31, 0, 3, 2) },
                new Label { Text = "While the switch is off, CyrFlip behaves exactly as before - no tray entry and no Jump List tasks.", AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(31, 0, 3, 6) }));

            var searchRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            searchRow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            searchRow.Controls.Add(_launcherSearch);

            var row1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row1.Controls.Add(LauncherButton("Add...", AppGlyphs.Add, LauncherAdd));
            row1.Controls.Add(LauncherButton("Change...", AppGlyphs.Edit, LauncherEdit));
            row1.Controls.Add(LauncherButton("Run", AppGlyphs.Run, LauncherRun));
            row1.Controls.Add(LauncherButton("Clone", AppGlyphs.Copy, LauncherClone));
            row1.Controls.Add(LauncherButton("Delete", AppGlyphs.Delete, LauncherRemove));
            var up = LauncherButton("↑", () => LauncherMove(-1));
            up.AccessibleName = Translate("Move up");
            _launcherUp = up;
            AsMoveGlyph(up, AppGlyphs.MoveUp);
            var down = LauncherButton("↓", () => LauncherMove(+1));
            down.AccessibleName = Translate("Move down");
            _launcherDown = down;
            AsMoveGlyph(down, AppGlyphs.MoveDown);
            // A square of the command metric (32 logical px - above the 28 px pointer floor of ICON-RENDER rule 5),
            // the same height as the buttons beside it; ReassertCommandMetrics scales it again on a DPI change.
            AsRowArrow(up); AsRowArrow(down);
            row1.Controls.Add(up); row1.Controls.Add(down);

            var row2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 0, 3, 2) };
            row2.Controls.Add(LauncherButton("Export... (short form)", AppGlyphs.Export, LauncherExport));
            row2.Controls.Add(LauncherButton("Import... (short form)", AppGlyphs.Import, LauncherImport));
            _launcherImportOcr = LauncherButton("Import from OneClickRunner...", AppGlyphs.Import, LauncherImportFromOcr);
            row2.Controls.Add(_launcherImportOcr);

            panel.Controls.Add(Group("launcher/scenarios", "Scenarios",
                searchRow,
                _launcherLoadErrors,
                _launcherList,
                row1,
                row2,
                new Label { Text = "Double-click or Enter runs, F2 edits, Delete removes. Importing from OneClickRunner copies the scenarios and never changes the source files.", AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 4) }));
            _launcherList.SmallImageList = _launcherImages;

            // Context menu, keyboard shortcuts and the run-on-double-click of the source app.
            var context = new ContextMenuStrip();
            context.Opening += (_, e) =>
            {
                context.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
                context.Items.Clear();
                if (SelectedLauncherScenario() == null) { e.Cancel = true; return; }
                context.Items.Add(new ToolStripMenuItem(Translate("Run"), null, (_, _) => LauncherRun()));
                context.Items.Add(new ToolStripMenuItem(Translate("Change..."), null, (_, _) => LauncherEdit()));
                context.Items.Add(new ToolStripMenuItem(Translate("Clone"), null, (_, _) => LauncherClone()));
                context.Items.Add(new ToolStripMenuItem(Translate("Export... (short form)"), null, (_, _) => LauncherExport()));
                context.Items.Add(new ToolStripSeparator());
                context.Items.Add(new ToolStripMenuItem(Translate("Delete"), null, (_, _) => LauncherRemove()));
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
            _launcherList.Columns[1].Width = Math.Max(_launcherPathMinimum, free);
        }

        // The nominal widths of the scenario table's columns, measured against the Russian headers at 100 %
        // (Name, Path, Arguments, Working folder, Admin, Shortcut) - a floor, never the answer (see MeasureLauncherColumns).
        private static readonly int[] LauncherColumnNominal = { 190, 250, 140, 130, 70, 115 };

        private int _launcherPathMinimum = 250;

        internal string[] LauncherColumnHeads() => new[]
        {
            Translate("Name"), Translate("Path"), Translate("Arguments"), Translate("Working folder"), Translate("Admin"), Translate("Shortcut"),
        };

        /// <summary>
        /// The widths of the scenario table's columns, measured from what is drawn (S0060 P7-c): the nominal width
        /// - scaled for the language and the display - is a floor, and a header the language writes longer
        /// ("Arbeitsordner", "Рабочая папка") or a font the display makes larger widens its column instead of
        /// being cut. The shortcut column also holds the widest chord a scenario carries. The path column is
        /// the one that later absorbs the spare width, so its measured width is its minimum.
        /// </summary>
        internal int[] MeasureLauncherColumns(string[] heads)
        {
            var widths = new int[heads.Length];
            int dpi = DeviceDpi;
            for (int column = 0; column < heads.Length; column++)
                widths[column] = Math.Max(ControlDpi.Scale(dpi, Column(LauncherColumnNominal[column])),
                    TextWidth(heads[column], _launcherList.Font) + ControlDpi.Scale(dpi, 8));
            int chord = 0;
            foreach (LauncherScenario scenario in _launcherStore.All)
                chord = Math.Max(chord, TextWidth(scenario.Hotkey, _launcherList.Font));
            widths[5] = Math.Max(widths[5], chord);
            _launcherPathMinimum = widths[1];
            return widths;
        }

        private Button LauncherButton(string englishText, Action action)
        {
            Button button = Button(englishText, action);
            _launcherButtons.Add(button);
            return button;
        }

        private Button LauncherButton(string englishText, string glyph, Action action)
        {
            Button button = Button(englishText, glyph, action);
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

            string[] heads = LauncherColumnHeads();
            int[] widths = MeasureLauncherColumns(heads);
            for (int column = 0; column < heads.Length; column++)
                _launcherList.Columns.Add(heads[column], widths[column]);

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
                _launcherLoadErrors.Text = Localization.Format(Translate, "Unreadable files: {0}", _launcherStore.LoadErrors.Count)
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

        /// <summary>A scenario write that failed is a failure of what the user asked for: the cause, and Retry when the caller can repeat it.</summary>
        private bool ReportScenarioSave(Exception? failure, Action? retry = null)
        {
            if (failure == null) return true;
            Fail(FailureCause.Describe(failure, _config.UiLanguage), retry);
            return false;
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
            => LauncherMigration.StripClashingHotkey(_launcherStore, scenario, Chords(), failure => ReportScenarioSave(failure));

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
            SaveNewScenario(dialog.Scenario);
        }

        /// <summary>Stores a new scenario. Its own method so a failure's Retry repeats the write, not the dialog.</summary>
        private void SaveNewScenario(LauncherScenario scenario)
        {
            if (!ReportScenarioSave(_launcherStore.Add(scenario), () => SaveNewScenario(scenario))) return;
            LauncherNotifyChanged();
            LauncherReselect(scenario.Id);
        }

        private void LauncherEdit()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            using var dialog = new LauncherScenarioDialog(selected, _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Scenario == null) return;
            if (!LauncherHotkeyIsFree(dialog.Scenario)) return;
            SaveEditedScenario(dialog.Scenario);
        }

        /// <summary>Stores an edited scenario. Its own method so a failure's Retry repeats the write, not the dialog.</summary>
        private void SaveEditedScenario(LauncherScenario scenario)
        {
            if (!ReportScenarioSave(_launcherStore.Update(scenario), () => SaveEditedScenario(scenario))) return;
            LauncherNotifyChanged();
            LauncherReselect(scenario.Id);
        }

        private void LauncherClone()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            LauncherScenario clone = selected.Clone();
            clone.Id = Guid.NewGuid();
            clone.Filename = string.Empty; // the store assigns a fresh file and appends the order
            clone.Hotkey = string.Empty;   // one chord cannot trigger two scenarios
            clone.Name = Localization.Format(Translate, "{0} (copy)", selected.Name);
            SaveNewScenario(clone);
        }

        private void LauncherRemove()
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            if (ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Remove the scenario \"{0}\"?", selected.Name),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            DeleteScenario(selected);
        }

        /// <summary>Deletes a confirmed scenario. Its own method so a failure's Retry repeats the deletion, not the question.</summary>
        private void DeleteScenario(LauncherScenario selected)
        {
            string? failure = _launcherStore.Remove(selected.Id, Translate);
            if (failure != null)
            {
                Fail(Localization.Format(Translate, "Could not delete scenario \"{0}\": {1}", selected.Name, failure),
                    () => DeleteScenario(selected));
                return;
            }
            LauncherNotifyChanged();
        }

        private void LauncherMove(int direction)
        {
            LauncherScenario? selected = SelectedLauncherScenario();
            if (selected == null) return;
            if (!ReportScenarioSave(_launcherStore.Move(selected.Id, direction), () => LauncherMove(direction))) return;
            LauncherNotifyChanged();
            LauncherReselect(selected.Id);
        }

        /// <summary>Run from the editor: same execution path as tray/Jump List, but errors are modal here (spec §7).</summary>
        private void LauncherRun() => RunScenario(SelectedLauncherScenario());

        private void RunScenario(LauncherScenario? selected)
        {
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
                    Fail(Localization.Format(Translate, "Couldn't start \"{0}\": {1}", name, result.ErrorMessage), () => RunScenario(selected));
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
                Filter = "XML|*.xml|*.*|*.*", Title = Translate("Export scenario to XML"),
                FileName = (suggested.Trim().Length == 0 ? "scenario" : suggested.Trim()) + ".xml",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                _launcherStore.Export(selected, dialog.FileName);
                ConfirmDialog.Show(this, _config.UiLanguage, Localization.Format(Translate, "Scenario \"{0}\" exported.", selected.Name),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Fail(Localization.Format(Translate, "Export failed: {0}", FailureCause.Describe(ex, _config.UiLanguage)), LauncherExport);
            }
        }

        private void LauncherImport()
        {
            using var dialog = new OpenFileDialog { Filter = "XML|*.xml|*.*|*.*", Title = Translate("Select a scenario XML file") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LauncherScenario? imported = _launcherStore.Import(dialog.FileName, out Exception? error);
            if (imported == null)
            {
                // A renamed root or a newer schemaVersion is not damage - say what to do, not the serializer's text.
                if (error is ScenarioFormatException)
                    Warn(Translate("This file is not a scenario in a format this version of CyrFlip understands. It may have been written by a newer version - update CyrFlip."));
                else
                    Fail(Localization.Format(Translate, "Import failed: {0}", FailureCause.Describe(error, _config.UiLanguage)), LauncherImport);
                return;
            }
            // A file exported on another machine can carry a chord that is taken here. One chord
            // belongs to one action, so it is dropped - the scenario itself still arrives.
            bool chordDropped = StripClashingHotkey(imported);
            LauncherNotifyChanged();
            LauncherReselect(imported.Id);
            string message = Localization.Format(Translate, "Imported scenario \"{0}\".", imported.Name);
            if (chordDropped) message += "\n" + Translate("Its shortcut is already taken, so it was not carried over.");
            ConfirmDialog.Show(this, _config.UiLanguage, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>The explicit, repeatable OneClickRunner import (spec §5.3) - the source is never modified.</summary>
        private void LauncherImportFromOcr()
        {
            if (!LauncherMigration.SourceExists())
            {
                ConfirmDialog.Show(this, _config.UiLanguage, Translate("No OneClickRunner scenarios were found."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            LauncherMigration.Result result = LauncherMigration.Import(_launcherStore, chords: Chords);
            LauncherNotifyChanged();
            string summary = Localization.Format(Translate, "Scenarios imported: {0}.", result.Imported);
            if (result.AlreadyPresent > 0)
                summary += "\n" + Localization.Format(Translate, "Already imported earlier: {0}.", result.AlreadyPresent);
            if (result.Skipped.Count > 0)
                summary += "\n" + Localization.Format(Translate, "Corrupt files skipped: {0}.", result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + Localization.Format(Translate, "New ids assigned because of collisions: {0}.", result.NewIds);
            if (result.ChordsDropped > 0)
                summary += "\n" + Localization.Format(Translate, "Shortcuts already taken, so not carried over: {0}.", result.ChordsDropped);
            foreach (var failure in result.SaveFailures)
                summary += "\n" + failure.Key + ": " + FailureCause.Describe(failure.Value, _config.UiLanguage);
            ConfirmDialog.Show(this, _config.UiLanguage, summary, MessageBoxButtons.OK,
                result.SaveFailures.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        /// <summary>The <see cref="Control.Tag"/> of a page's purpose line, for the conformance gates.</summary>
        internal const string PageDescriptionTag = "page-description";

        private TabPage Page(string title, string description, params Control[] controls)
        {
            var page = new TabPage(title);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = ThemePalette.Light.SurfaceRaised };
            // A narrower window clamps the wrapping text blocks to what the page has (H3); re-clamped
            // on every resize, since the authored cap comes back when the window grows again.
            panel.Resize += (_, _) => ClampPageOnResize(panel);
            // The page opens with its title, then the one-line purpose (WINDOWS-UI section 2 rule 3).
            // Both live in the translatable tree: the title is a bold caption like a section header.
            // The title is shown by the header band (SyncPageHeader) and stays here hidden, so the language pass
            // translates it. The purpose line is the first line of the content pane (WINDOWS-UI section 2 rule 3),
            // not part of the band: the band must be the same height on every page or the page list loses rows.
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = BoldFont, Margin = new Padding(3, 0, 3, 2), UseMnemonic = false, Visible = false });
            // One short sentence (S0060 M7): the tag is how the description gate finds it among the page's labels.
            panel.Controls.Add(new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(930, 0), ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(2, 0, 2, 10), Tag = PageDescriptionTag });
            foreach (Control control in controls) panel.Controls.Add(control);
            page.Controls.Add(panel); return page;
        }
        private static FlowLayoutPanel ContentPanel(TabPage page) => (FlowLayoutPanel)page.Controls[0];
        private void BuildTransferPreferenceGroups()
        {
            if (_tabs == null) return;
            // Keyed by the page's key, never its (translated, renamed) title (S0062 D12). Only "Индикаторы"
            // keeps loose rows; every other page builds its groups itself.
            foreach (TabPage page in _tabs.TabPages)
            {
                if (!Equals(page.Tag, SettingsPageKey.Indicators)) continue;
                const string id = "indicators/preferences";
                var panel = ContentPanel(page);
                var rows = panel.Controls.Cast<Control>().Skip(2).Where(c => !(c is CollapsibleGroup)).ToArray();
                if (rows.Length == 0) continue;
                foreach (Control row in rows) panel.Controls.Remove(row);
                var group = Group(id, "Options", rows); panel.Controls.Add(group); panel.Controls.SetChildIndex(group, 2);
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
                14 => SettingsPageKey.SelectionMenu, 15 => SettingsPageKey.TrafficLight,
                16 => SettingsPageKey.ExchangeServer,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return page;
        }

        /// <summary>
        /// The page kinds in the order their pictures sit in the page list's image list. 4 and 13 are
        /// gone (the unused "advanced" picture; the overview page that merged into Hotkeys, S0062 D8), 14 is
        /// the selection menu (<c>feature.selection-menu</c>, the record it asked on 2026-10-11 - it drew its
        /// caption alone until the record existed, S0062 D7).
        /// </summary>
        private static readonly int[] TabKinds = { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16 };

        internal static int IconIndex(int kind) => Array.IndexOf(TabKinds, kind);

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
                case 14: return AppGlyphs.SelectionMenu;
                case 15: return AppGlyphs.NetworkMonitor;
                case 16: return AppGlyphs.ExchangeServer;
                default: return null;
            }
        }

        internal const int TabIconSize = 24;

        /// <summary>The icon's edge in device pixels at the given DPI: authored 24 logical, never smaller.</summary>
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
            images.Images.Add("selection-menu", TabIcon(14, inkOf(14), size));
            images.Images.Add("traffic-light", TabIcon(15, inkOf(15), size));
            images.Images.Add("exchange-server", TabIcon(16, inkOf(16), size));
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
            row.Controls.Add(new Label { Text = "Marker size:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 5, 0) });
            row.Controls.Add(_markerSize);
            row.Controls.Add(_markerSizeValue);
            return row;
        }

        private string MarkerSizeName(int step)
            => Translate(step <= 0 ? "Small" : step == 1 ? "Medium" : "Large");

        private Control ThemeRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            row.Controls.Add(new Label { Text = "Theme:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
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
            SaveConfig();
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
                    Label = Translate(mode == ThemeMode.Light ? "Light" : mode == ThemeMode.Dark ? "Dark" : "Same as Windows"),
                });
                if (mode == current) select = i;
            }
            _themeCombo.SelectedIndex = select;
            int max = ComboWidth(_themeCombo.Items.Cast<ThemeItem>().Select(item => item.Label), 190);
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
            row.Controls.Add(new Label { Text = "Interface language:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
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
            string line = Localization.Format(Translate, "Version {0}", SupportBundle.AppVersion());
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
            var page = Page("Symbols",
                "A palette of rare characters: clicking copies the character to the clipboard; everything stays on this computer.");
            var panel = ContentPanel(page);

            // The module switch leads in a group of its own, like the hotkeys page's (S0060 M4).
            panel.Controls.Add(Group("symbols/switches", "Main switches",
                Setting(_symbolsEnabled, "While disabled, the symbols window is not created, no symbols item appears in the tray or context menu, and the hotkey is not bound.")));

            panel.Controls.Add(Group("symbols/opening", "How to open it",
                TrackSymbols(HotkeyRow(_symbolsHotkeyEnabled, _symbolsHotkeyValue, () => SymbolsHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Opens the window with focus in the search box. One combination cannot belong to two CyrFlip actions.")),
                TrackSymbols(Setting(Button("Open symbols window", AppGlyphs.Symbols, () => OpenSymbolsRequested?.Invoke(this, EventArgs.Empty)),
                    "Same as the \"Symbols\" item in the tray menu."))));

            // The closing paragraph about what a click does is a muted note of the options group, not a loose
            // label at the foot of the page (S0060 M4).
            panel.Controls.Add(Group("symbols/options", "Settings",
                TrackSymbols(Setting(_symbolsHideTypable,
                    "Hides symbols from the palette that can be typed with a single key or Shift / AltGr combination on installed layouts.")),
                new Label
                {
                    Text = "A click copies the symbol to the clipboard. Symbols collect in the window's text field until \"Close on pick\" is on. Symbol picks are not added to clipboard history. Nothing leaves this computer.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 6, 3, 4),
                }));
            return page;
        }

        private TabPage AboutPage()
        {
            var page = Page("About", "The version, the licence, the project links and where to ask for help.");
            var panel = ContentPanel(page);
            // Two groups, so the page is a grouping (WINDOWS-UI section 3): what the product is, and
            // where help comes from.
            panel.Controls.Add(Group("about/product", "About",
                // Filled by ApplyLanguage, not here: it carries the version number, so a text set at
                // build time would be captured by RememberSourceTexts as if it were a source caption
                // and then "translated" on every language change. Starting empty keeps it out of that map.
                TrackAbout(_version, _version.Font = BoldFont),
                // What the product is and what it never does with the network: the paragraphs that were this
                // page's description until S0060 M7 made that one sentence. The key is unchanged, so its 13
                // translations are too.
                new Label
                {
                    Text = "CyrFlip is a tray toolkit for your keyboard and desktop: it shows the active layout at the caret, fixes text typed in the wrong layout or with CapsLock on, and captures a screen region; a selection menu, a local translator, quick notes, clipboard history and quick launch are optional modules you switch on yourself.\n\nThe app never sends clipboard history over the network. When history is on, entries are protected by Windows DPAPI and readable only by the same Windows account.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 8, 3, 4),
                },
                new Label { Text = "Developer: SerZhyAle", AutoSize = true, Font = BoldFont, Margin = new Padding(3, 14, 3, 4) },
                ResourceLinks(AboutResources.Product),
                new Label { Text = "CyrFlip runs locally, with no telemetry and no network sync of clipboard history.", AutoSize = true, MaximumSize = new Size(690, 0), Padding = new Padding(0, 14, 0, 0), ForeColor = ThemePalette.Light.TextMuted }));
            panel.Controls.Add(Group("about/support", "Help and support",
                Setting(Button("Help", AppGlyphs.Help, () => ContextHelp.Open("index", _config.UiLanguage)),
                    "Help is available offline. Press F1 in a CyrFlip window or use its help button."),
                Setting(Button("Show the welcome tour..", AppGlyphs.Info, () => WelcomeRequested?.Invoke(this, EventArgs.Empty)),
                    "Get to know CyrFlip and set it up."),
                Setting(Button("Diagnose caret position...", AppGlyphs.Verify, _diagnoseCaret), "Creates a local report of how Windows and UI Automation see the caret. Use it only when the marker is missing or misplaced in a particular app."),
                Setting(_exportDiagnostics = Button("Export diagnostics...", AppGlyphs.Export, ExportDiagnostics),
                    "Save a local log archive. Nothing is sent."),
                Setting(_sendLogs = Button("Send logs to the author..", AppGlyphs.Email, SendLogsToAuthor), "Collects CyrFlip's logs into one archive and opens a message to the author with it attached. You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history never goes into the archive.")));
            panel.Controls.Add(Group("about/resources", "Resources", ResourceLinks(AboutResources.Dependencies)));
            panel.Controls.Add(Group("about/related", "Related products", ResourceLinks(AboutResources.Related)));
            return page;
        }

        private Control ResourceLinks(AboutResources.Resource[] resources)
        {
            var links = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            foreach (AboutResources.Resource resource in resources)
            {
                if (resource.Id == AboutResources.ExchangeServerId)
                {
                    links.Controls.Add(ExchangeServerCredit(resource));
                    continue;
                }
                LinkLabel link = Link(resource.Caption, resource.Url);
                link.Name = "resource." + resource.Id;
                link.MaximumSize = new Size(890, 0);
                links.Controls.Add(link);
            }
            return links;
        }

        /// <summary>
        /// The exchange server's credit on the About page (EXCHANGE-MARK rule 8 (e), S0079 M11): its mark at 16 px
        /// before the link, which carries the full name (rule 14) and opens the portal's page about the server in the
        /// interface language, the way the settings page's own link does. A row of its own, since a link label has no
        /// picture slot.
        /// </summary>
        private Control ExchangeServerCredit(AboutResources.Resource resource)
        {
            // A two-column table, not a flow: the page's text clamp walks top-down flows for wrapping labels and hands a
            // left-to-right flow to the command-button fit, and this row is neither. The link is the proper name, one short
            // line, so it carries no wrap width to clamp.
            var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var mark = new ExchangeMarkPicture(16) { Name = "resource.exchange-server.mark", Anchor = AnchorStyles.Left, Margin = new Padding(3, 4, 4, 4) };
            mark.NameIn(Translate);
            var link = new LinkLabel { Text = resource.Caption, Name = "resource." + resource.Id, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 4, 3, 4) };
            link.LinkClicked += (_, _) => OpenAddress(ExchangePortalLinks.ForLanguage(ExchangePortalPage.Server, _config.UiLanguage));
            row.Controls.Add(mark, 0, 0);
            row.Controls.Add(link, 1, 0);
            _exchangeMarks.Add(mark);
            return row;
        }

        private Button? _exportDiagnostics;

        private void ExportDiagnostics()
        {
            if (_exportDiagnostics?.Enabled != true) return;
            using var picker = new SaveFileDialog { FileName = "CyrFlip-diagnostics.zip", DefaultExt = "zip", AddExtension = true,
                Filter = "ZIP (*.zip)|*.zip", OverwritePrompt = true, Title = Translate("Export diagnostics...") };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            _exportDiagnostics.Enabled = false;
            try
            {
                string destination = picker.FileName;
                BusyDialog.Run(this, _config.UiLanguage, () =>
                {
                    SupportBundle.Result result = SupportBundle.CreateDefault(_config, DateTime.Now);
                    System.IO.File.Copy(result.ArchivePath, destination, overwrite: true);
                    return true;
                });
            }
            catch (Exception ex) { Fail(FailureCause.Describe(ex, _config.UiLanguage), ExportDiagnostics); }
            finally { if (!IsDisposed) _exportDiagnostics.Enabled = true; }
        }

        /// <summary>A control of the About page, after a statement that only shapes it.</summary>
        private static Control TrackAbout(Control control, Font _) => control;

        /// <summary>
        /// Collect the logs, show what was collected, and - only if the user says so - hand the archive
        /// to their mail client. Off the UI thread twice over, and for two different reasons: packing
        /// is disk work (here behind the wait window, so the user sees that something is happening rather
        /// than a button gone grey - S0060 P7-h), and <c>MAPISendMail</c> with <c>MAPI_DIALOG</c> blocks
        /// until the compose window is closed.
        /// </summary>
        private void SendLogsToAuthor()
        {
            if (_sendLogs != null) _sendLogs.Enabled = false;
            SupportBundle.Result result;
            try { result = BusyDialog.Run(this, _config.UiLanguage, () => SupportBundle.CreateDefault(_config, DateTime.Now)); }
            catch (Exception ex)
            {
                if (_sendLogs != null) _sendLogs.Enabled = true;
                Fail(Translate("Could not build the log archive:") + "\n" + FailureCause.Describe(ex, _config.UiLanguage), SendLogsToAuthor);
                return;
            }
            ShowSupportBundle(result);
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
                        Translate("Your mail program does not accept an attachment from a link. The message is open and the archive is selected in Explorer - drag it into the message before sending."),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case MailOutcome.Manual:
                    ConfirmDialog.Show(this, _config.UiLanguage,
                        Translate("The mail program could not be opened. Please send the archive by hand to:")
                        + "\n" + MailSender.AuthorAddress + "\n\n" + result.ArchivePath,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
        }
        private LinkLabel Link(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            link.LinkClicked += (_, _) => OpenAddress(url);
            return link;
        }
        private static CheckBox Check(string text) => new BooleanOptionCheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        private static Button Button(string text, Action action) { var button = new Button { Text = text, AutoSize = true, Margin = new Padding(4, 3, 4, 3) }; button.Click += (_, _) => action(); return button; }

        /// <summary>A command button with its verb's vocabulary glyph before the caption (WINDOWS-UI
        /// section 11 item 1) - in the mono look: a settings row is not a dialog answer. It takes the shared
        /// command metric (WINDOWS-UI section 5: 32 px high, 16 px of padding a side, 8 px between neighbours),
        /// which is what makes every command of every row the same height (S0060 M12).</summary>
        private static Button Button(string text, string glyph, Action action)
        {
            Button button = Button(text, action);
            ButtonGlyphs.Attach(button, glyph);
            ButtonGlyphs.UseCommandMetric(button);
            return button;
        }
        /// <summary>The parts of a captioned value row (WINDOWS-UI section 4, "Ordinary row" and "Boolean"): the
        /// caption and its description share the leading column, the editors stand in the trailing column of the
        /// same row. The caption is a label for a value row and the switch itself for a boolean one.</summary>
        internal sealed class CaptionedRow
        {
            public CaptionedRow(TableLayoutPanel table, Control caption, Label hint, FlowLayoutPanel editors)
            { Table = table; Caption = caption; Hint = hint; Editors = editors; }
            public TableLayoutPanel Table { get; }
            public Control Caption { get; }
            public Label Hint { get; }
            public FlowLayoutPanel Editors { get; }
        }

        private const int CaptionedRowGap = 16;
        private int _editorColumn;

        /// <summary>
        /// Computes the true preferred height of an editors strip. In Windows Forms, ComboBox has AutoSize == false,
        /// so FlowLayoutPanel.GetPreferredSize() measures it via SpecifiedBounds.Height (the unscaled 96-DPI default 21 px)
        /// rather than its DPI-scaled native window height (PreferredHeight). Measuring children directly ensures
        /// FlowLayoutPanel does not clip tall or scaled controls (e.g., ComboBox at high DPI).
        /// </summary>
        internal static int EditorsPreferredHeight(FlowLayoutPanel editors)
        {
            int required = editors.Padding.Vertical;
            if (editors.FlowDirection == FlowDirection.TopDown)
            {
                foreach (Control child in editors.Controls)
                {
                    if (!child.Visible) continue;
                    int h = Math.Max(child.Height, child.GetPreferredSize(Size.Empty).Height);
                    required += h + child.Margin.Vertical;
                }
            }
            else
            {
                int maxChild = 0;
                foreach (Control child in editors.Controls)
                {
                    if (!child.Visible) continue;
                    int h = Math.Max(child.Height, child.GetPreferredSize(Size.Empty).Height) + child.Margin.Vertical;
                    if (h > maxChild) maxChild = h;
                }
                required += maxChild;
            }
            Size min = editors.MinimumSize;
            try
            {
                editors.MinimumSize = Size.Empty;
                return Math.Max(editors.GetPreferredSize(Size.Empty).Height, required);
            }
            finally
            {
                editors.MinimumSize = min;
            }
        }

        private static int WidestEditors(Control container)
        {
            int widest = 0;
            foreach (Control child in container.Controls)
            {
                if (child is TableLayoutPanel { Tag: CaptionedRow row })
                {
                    row.Editors.MinimumSize = new Size(0, EditorsPreferredHeight(row.Editors));
                    widest = Math.Max(widest, row.Editors.GetPreferredSize(Size.Empty).Width);
                }
                else if (child is CollapsibleGroup group) widest = Math.Max(widest, WidestEditors(group.Body));
            }
            return widest;
        }
        private const int CaptionedRowMaxLead = 900;

        /// <summary>
        /// The one table every captioned row is: the leading column holds the caption and, under it, its muted
        /// description; the trailing column holds the editors, and the editors of a left-to-right strip stand on
        /// one centre line whatever their heights (a drop-down of the font's height beside a 32 px command).
        /// </summary>
        private static TableLayoutPanel BuildCaptionedRow(Control caption, Label hint, FlowLayoutPanel editors)
        {
            var lead = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            lead.Controls.Add(caption);
            lead.Controls.Add(hint);
            if (editors.FlowDirection == FlowDirection.LeftToRight)
                foreach (Control editor in editors.Controls)
                {
                    editor.Anchor = AnchorStyles.Left;
                    // A value shown beside an editor carried a top padding to look level with it; centred, it is level already.
                    if (editor is Label) editor.Padding = Padding.Empty;
                }
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(3, 7, 3, 7) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.Controls.Add(lead, 0, 0);
            table.Controls.Add(editors, 1, 0);
            table.Tag = new CaptionedRow(table, caption, hint, editors);
            editors.MinimumSize = new Size(editors.MinimumSize.Width, EditorsPreferredHeight(editors));
            return table;
        }

        private Control CaptionedSetting(FlowLayoutPanel editors, Label caption, string description)
        {
            editors.Controls.Remove(caption);
            caption.Padding = Padding.Empty;
            caption.Font = BoldFont;   // the caption is the row's name; the description is the muted line under it
            caption.Margin = new Padding(3, 7, 3, 2);
            caption.UseMnemonic = false;
            caption.MaximumSize = new Size(CaptionedRowMaxLead, 0);
            var hint = new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(CaptionedRowMaxLead, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 3) };
            return BuildCaptionedRow(caption, hint, editors);
        }

        /// <summary>
        /// A boolean row with editors (WINDOWS-UI section 4, "Boolean"): the switch at the reading start with its
        /// caption beside it and its description below, at the caption text's own inset, in the leading column;
        /// whatever else the row carries - the chord and the Change button of a hotkey - in the trailing column,
        /// on the page's shared editor edge. The inset is a pair of the hint pass, re-measured with the font.
        /// </summary>
        private Control CaptionedBoolean(CheckBox box, FlowLayoutPanel editors, string description)
        {
            box.AutoSize = true;
            box.Font = BoldFont;   // a switch's caption is the setting's name, as in Setting()
            box.Margin = new Padding(3, 7, 3, 2);
            box.MaximumSize = new Size(CaptionedRowMaxLead, 0);
            var hint = new Label { Text = description, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(CaptionedRowMaxLead, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(28, 0, 3, 3) };
            _hintPairs.Add(new KeyValuePair<Control, Label>(box, hint));
            return BuildCaptionedRow(box, hint, editors);
        }

        /// <summary>The leading column takes what the editors leave (not less than <see cref="MinTextWidth"/>), and its
        /// text wraps inside it, so the row grows vertically instead of the editor leaving its side (WINDOWS-UI section 4).</summary>
        private void FitCaptionedRow(CaptionedRow row, int available)
        {
            // One editor column per page: every row of the page gives its editors the width of the widest, so
            // their left edges line up (WINDOWS-UI section 4: editors align to a shared page column).
            int editorHeight = EditorsPreferredHeight(row.Editors);
            row.Editors.MinimumSize = new Size(0, editorHeight);
            int own = row.Editors.GetPreferredSize(Size.Empty).Width;
            int editorWidth = Math.Max(own, _editorColumn) + row.Editors.Margin.Horizontal;
            row.Editors.MinimumSize = new Size(Math.Max(own, _editorColumn), editorHeight);
            int lead = Math.Max(MinTextWidth, available - row.Table.Margin.Horizontal - editorWidth - CaptionedRowGap);
            if ((int)row.Table.ColumnStyles[0].Width != lead) row.Table.ColumnStyles[0].Width = lead;
            // Each wraps inside its own inset: a switch's description stands under its text, not under its box.
            int captionWidth = Math.Max(1, lead - row.Caption.Margin.Horizontal);
            int hintWidth = Math.Max(1, lead - row.Hint.Margin.Horizontal);
            if (row.Caption.MaximumSize.Width != captionWidth) row.Caption.MaximumSize = new Size(captionWidth, 0);
            if (row.Hint.MaximumSize.Width != hintWidth) row.Hint.MaximumSize = new Size(hintWidth, 0);
            AlignCaptionToEditors(row);
        }

        /// <summary>
        /// The caption's first line stands level with the editors' centre line: a 32 px command beside a caption
        /// pinned to the top of the cell read as two rows. Measured from the live font, so a taller script or a
        /// larger display keeps them level.
        /// </summary>
        private static void AlignCaptionToEditors(CaptionedRow row)
        {
            int strip = EditorsPreferredHeight(row.Editors);
            int centre = row.Editors.Margin.Top + strip / 2;
            int line = row.Caption.Font.Height + (row.Caption is CheckBox ? 4 : 0);
            int top = Math.Max(2, centre - line / 2);
            Padding margin = row.Caption.Margin;
            if (margin.Top != top) row.Caption.Margin = new Padding(margin.Left, top, margin.Right, margin.Bottom);
        }

        /// <summary>The backup state row (S0072): the last write, and the folder they live in.</summary>
        internal void ApplyImportedNavigation()
        {
            if (_tabs == null || IsDisposed) return;
            _restoringContext = true;
            try
            {
                RestoreGroupStates();
                if (SettingsPageKeys.TryParse(_config.SettingsPageId, out SettingsPageKey page)) SelectPage(page);
                else if (SettingsPageKeys.FromLegacyIndex(_config.SettingsTab) is SettingsPageKey legacy) SelectPage(legacy);
                RestoreViewport(_tabs.SelectedIndex);
            }
            finally { _restoringContext = false; }
        }

        internal void RefreshBackupState()
        {
            if (IsDisposed) return;
            _backupState.Text = SettingsBackup.NewestBackupTime() is { } last
                ? Localization.Format(Translate, "Last backup: {0}", last.ToString("g"))
                : Translate("No backup yet");
        }

        private Control BackupFolderRow()
        {
            Button open = Button("Open backups folder", AppGlyphs.Folder, OpenBackupsFolder);
            var strip = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 5, 3, 5) };
            strip.Controls.Add(_backupState);
            strip.Controls.Add(open);
            return strip;
        }

        private void OpenBackupsFolder()
        {
            try
            {
                string folder = SettingsBackup.DefaultFolder;
                // A folder that was never created has nothing to show; the parent is the nearest honest place.
                if (!System.IO.Directory.Exists(folder))
                    folder = System.IO.Path.GetDirectoryName(folder.TrimEnd('\\', '/')) ?? folder;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"")
                { UseShellExecute = true })?.Dispose();
            }
            catch { /* the shell refused: nothing this button can do */ }
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
            // The description stands under the text the row's control starts with, not under a fixed guess
            // (WINDOWS-UI section 4): a switch's text after its box ("Boolean"), a command's caption after its
            // glyph and padding ("Commands": a command is not a value row, so its description stays under it
            // rather than beside it). The pair is re-measured whenever the font or the DPI changes.
            Control? anchor = control is CheckBox || control is RadioButton || control is Button ? control : FirstButton(control);
            if (anchor != null) _hintPairs.Add(new KeyValuePair<Control, Label>(anchor, hint));
            // The dependency pass (M5) stands a "why it is off" note under this row when its master switch is
            // off - the row is found from the control the hint was registered for.
            _rowHints[control] = hint;
            return block;
        }

        /// <summary>The first command button inside a control, or null - the anchor a command row's description is aligned to.</summary>
        private static Button? FirstButton(Control control)
        {
            foreach (Control child in control.Controls)
            {
                if (child is Button button) return button;
                Button? deeper = FirstButton(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>
        /// A collapsible settings group (WINDOWS-UI section 3). English caption, not pre-translated -
        /// the header lives in the translatable tree like every other caption. The saved expansion is
        /// applied here, at build time, before any handler can see a change.
        /// </summary>
        private CollapsibleGroup Group(string id, string englishCaption, params Control[] rows)
        {
            var group = new CollapsibleGroup(id, englishCaption);
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
                // The control and its hint share one block, so the control's own margin and padding are part
                // of where its caption text starts.
                int inset = HintInset(pair.Key, pair.Value);
                Padding margin = pair.Value.Margin;
                if (margin.Left != inset) pair.Value.Margin = new Padding(inset, 0, 3, 3);
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

        /// <summary>
        /// How far from its block's reading-start edge a row's description starts: where the text of the control
        /// above it starts. A switch's text begins after its box (<see cref="CaptionTextInset"/>); a command's
        /// caption begins after the padding the button reserves for its glyph (<c>ButtonGlyphs</c> reserves it on
        /// the reading-start side, so the right padding under a right-to-left language); the margins of anything
        /// between the control and the hint's own block count too. A flow mirrors a margin's left side to the
        /// reading start itself, so <c>Margin.Left</c> is the reading-start margin in both directions.
        /// </summary>
        internal static int HintInset(Control anchor, Control hint)
        {
            bool rtl = anchor.RightToLeft == RightToLeft.Yes;
            int inset = anchor.Margin.Left;
            if (anchor is CheckBox || anchor is RadioButton)
                inset += (rtl ? anchor.Padding.Right : anchor.Padding.Left) + CaptionTextInset(anchor);
            else if (anchor is Button)
                inset += rtl ? anchor.Padding.Right : anchor.Padding.Left;

            int between = 0;
            for (Control? up = anchor.Parent; up != null; up = up.Parent)
            {
                if (ReferenceEquals(up, hint.Parent)) return inset + between;
                between += up.Margin.Left + (rtl ? up.Padding.Right : up.Padding.Left);
            }
            return inset;   // the hint is not above the control (not built into one block): nothing between
        }

        /// <summary>The distance from a checkbox/radio's left edge to where its caption text starts.</summary>
        internal static int CaptionTextInset(Control box)
        {
            if (box is BooleanOptionCheckBox option) return option.CaptionInset;
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
        /// <summary>
        /// A chord setting, [☑ action] chord [Change...], in the shape of every other boolean row with editors
        /// (WINDOWS-UI section 4): the switch and its description in the leading column - the description under
        /// the caption's own text - and the chord and its Change button in the trailing column of the same row,
        /// on the page's shared editor edge. Every chord label is as wide as the widest chord (<see
        /// cref="AlignChordColumn"/>), so the Change buttons of different rows stand under one another.
        /// </summary>
        private Control HotkeyRow(CheckBox enable, Label value, Action change, string description)
        {
            var editors = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(3, 5, 3, 5) };
            value.AutoSize = true;
            value.Margin = new Padding(3, 0, 6, 0);
            if (!_chordValueLabels.Contains(value)) _chordValueLabels.Add(value);
            editors.Controls.Add(value);
            editors.Controls.Add(Button("Change...", AppGlyphs.Edit, change));
            Control row = CaptionedBoolean(enable, editors, description);
            _rowHints[enable] = ((CaptionedRow)row.Tag!).Hint;   // the dependency pass stands its "why it is off" note under this row
            return row;
        }

        // The chord labels of every hotkey row, so one pass gives them the width of the widest chord.
        private readonly List<Label> _chordValueLabels = new List<Label>();

        /// <summary>
        /// Every chord label is as wide as the longest chord a capture can produce (<see
        /// cref="HotkeyDialog.LongestChord"/>), measured in the font the window actually draws: the Change
        /// buttons of the hotkey rows - on one page and across pages - then stand at one edge without a
        /// pixel width. Re-run on every language and DPI pass, since the font is what it follows.
        /// </summary>
        private void AlignChordColumn()
        {
            int width = TextRenderer.MeasureText(HotkeyDialog.LongestChord, Font).Width + ControlDpi.Scale(DeviceDpi, 8);
            foreach (Label value in _chordValueLabels)
                if (value.MinimumSize.Width != width) value.MinimumSize = new Size(width, 0);
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
                foreach (Font retired in _retiredFonts) retired.Dispose();
                _retiredFonts.Clear();
                _ownIcon?.Dispose();
            }
        }
    }
}

