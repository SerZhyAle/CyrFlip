using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Background app shell living in the notification area (system tray). Owns the keyboard
    /// hook, the layout indicator and the tray icon/menu, and runs the flip/case-flip pipelines
    /// off the hook on a dedicated background thread.
    ///
    /// The tray stays intentionally short: frequent display/history controls, Settings and Exit.
    /// Less frequent configuration, privacy actions and diagnostics live in <see cref="SettingsForm"/>.
    /// </summary>
    internal sealed class CyrFlipContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private Hotkey _caseHotkey;
        private Hotkey _clipboardHistoryHotkey;
        private readonly KeyboardHook _hook = new KeyboardHook();
        private readonly CursorIndicator _indicator = new CursorIndicator();
        private readonly ClipboardHandler _clipboard = new ClipboardHandler();
        private readonly LayoutCursor _layoutCursor;
        private readonly CaretOverlay _caretOverlay;
        private readonly NotifyIcon _tray;
        // WinForms timer on purpose: the tick runs on the UI thread, where the tray balloon lives.
        private readonly System.Windows.Forms.Timer _trayClickTimer = new System.Windows.Forms.Timer();
        // The hook watchdog - see OnHookWatchdogTick. A WinForms timer for a second reason here: a
        // hook may only be removed by the thread that installed it, and this ticks on that thread.
        private readonly System.Windows.Forms.Timer _hookWatchdog = new System.Windows.Forms.Timer();
        private int _hookReinstallFailures;
        // When the "another clipboard operation is still running" balloon was last shown - see BusyNotice.
        private long _busyNoticeShownAtMs = long.MinValue;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly ToolStripMenuItem _cursorItem;
        private readonly ToolStripMenuItem _caretItem;
        private readonly ToolStripMenuItem _dotModeItem;
        private readonly ToolStripMenuItem _langSwitchItem;
        private readonly ToolStripMenuItem _capsAfterItem;
        private readonly ToolStripMenuItem _keepAwakeItem;
        private readonly ToolStripMenuItem _keepScreenItem;
        private readonly ClipboardHistoryService _clipboardHistory;
        private readonly ClipboardHistoryWindow _clipboardHistoryWindow;
        private ClipboardHistorySearchWindow? _clipboardHistorySearchWindow;
        private readonly ToolStripMenuItem _historyEnabledItem;
        private readonly ToolStripMenuItem _historyPauseItem;
        private readonly ToolStripMenuItem _showHistoryItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _exitItem;
        // ---- Quick notes (local, DPAPI, opt-in) ----
        private Hotkey _quickNotesHotkey;
        // Created only once the feature is on: the store replays a journal and the window builds a
        // control tree, and a user who never switched this on should pay for neither.
        private QuickNotesService? _quickNotes;
        private QuickNotesWindow? _quickNotesWindow;
        private readonly ToolStripMenuItem _quickNotesItem;
        // ---- Screen region capture (S0026) ----
        private Hotkey _screenshotHotkey;
        // A top-level item, always there: one click from the tray, no module switch (owner, 2026-09-26).
        private readonly ToolStripMenuItem _screenshotItem;
        private RegionSelectionOverlay? _regionOverlay;
        private bool _screenshotChordWasLive;
        private bool _textMenuChordWasLive;
        // A start from the tray or the settings button waits for the menu to finish closing, or the
        // closing menu would be frozen into the picture.
        private readonly System.Windows.Forms.Timer _screenshotDelay = new System.Windows.Forms.Timer { Interval = 250 };
        // The open exchange file (ticket S0023): export / import of notes and history.
        private readonly ExchangeFlow _exchange;
        private readonly SettingsForm _settings;
        // ---- Scenario launcher (absorbed OneClickRunner) ----
        private readonly LauncherScenarioStore _launcherStore;
        private readonly ToolStripMenuItem _launcherMenu;
        private readonly LauncherIpc _launcherIpc;
        private LauncherTaskbarWindow? _launcherTaskbar;

        // ---- Text context menu (own menu over the selection) ----
        private readonly MouseHook _mouseHook = new MouseHook();
        private readonly ContextMenuStrip _textMenu = new ContextMenuStrip();
        // The open menu came from the keyboard chord and holds the foreground (S0045 K2).
        private bool _textMenuFromKeyboard;
        private SelectionProbe.Run? _selectionProbe;
        /// <summary>The probe's own thread, alive while the mouse hook is installed (S0030 HT-3).</summary>
        private SelectionProbe.Worker? _probeWorker;
        /// <summary>The last quick-notes service switched off, still finishing its compaction on the pool.</summary>
        private System.Threading.Tasks.Task? _quickNotesRetired;
        private IntPtr _textMenuTarget;

        // ---- Translator (local Ollama) ----
        private readonly TranslationService _translation;
        private readonly ToolStripMenuItem _translateClipboardItem;
        private TranslationResultWindow? _translateWindow;
        private CancellationTokenSource? _translateCts;
        private bool _disposed;
        /// <summary>Bumped by every launcher refresh, so a Jump List built on the pool for an older one is dropped.</summary>
        private int _jumpListGeneration;
        private readonly SessionEndWatcher _sessionEnd = new SessionEndWatcher();

        private string _translateSource = "";
        private string _translateCode = "";
        private IntPtr _translateTarget = IntPtr.Zero;

        private readonly SynchronizationContext? _ui;
        private Icon? _trayIcon;
        private int _busy; // 0 = idle, 1 = a clipboard op (flip or case-flip) is in progress
        // Last seen state of the two switches that bring chords to life from a settings-tab event, so
        // the chord-ownership check runs on the transition to on and not on every change of the tab.
        private bool _translateWasOn;
        private bool _quickNotesChordWasLive;
        private string _currentLayout = "";
        private string _currentKlid = "";
        private bool _capsOn;

        public CyrFlipContext(AppConfig config, bool openSettingsOnStart = false)
        {
            _config = config;
            // First, before any window or menu exists: every one of them is painted when it is created
            // (ticket S0020), and the tray menu reads the process-wide renderer this installs.
            ThemeManager.Initialize(ThemeModes.Parse(_config.Theme));
            // Before anything reads the notes journal or writes a log: the Store build's one-time move out
            // of the machine-wide %ProgramData%\CyrFlip (ticket S0016). A no-op unpackaged and after the first run.
            DataFolderMigration.RunOnce(_config);
            _launcherStore = new LauncherScenarioStore();
            _launcherMenu = new ToolStripMenuItem();
            _caseHotkey = Hotkey.TryParse(_config.CaseHotkey, out Hotkey caseChord) ? caseChord : Hotkey.CaseDefault;
            _clipboardHistoryHotkey = Hotkey.TryParse(_config.ClipboardHistoryHotkey, out Hotkey histChord) ? histChord : new Hotkey(true, true, false, false, 0x79, "F10");
            if (_clipboardHistoryHotkey.SameChord(_caseHotkey))
                _clipboardHistoryHotkey = new Hotkey(true, true, false, false, 0x79, "F10");
            _quickNotesHotkey = Hotkey.TryParse(_config.QuickNotesHotkey, out Hotkey notesChord) ? notesChord : Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey);
            _screenshotHotkey = Hotkey.TryParse(_config.ScreenshotHotkey, out Hotkey shotChord) ? shotChord : Hotkey.Parse(AppConfig.DefaultScreenshotHotkey);
            _translateWasOn = _config.EnableTranslate;
            _quickNotesChordWasLive = QuickNotesChordLive;
            _screenshotChordWasLive = ScreenshotChordLive;
            _clipboardHistory = new ClipboardHistoryService(_config.EnableClipboardHistory, _config.PauseClipboardHistory);
            _clipboardHistoryWindow = new ClipboardHistoryWindow(_clipboardHistory, _config, ShowHistorySearch);
            _exchange = new ExchangeFlow(_config, _clipboardHistory,
                () => { EnsureQuickNotes(); return _quickNotes; },
                () => { if (_quickNotesWindow != null && !_quickNotesWindow.IsDisposed) _quickNotesWindow.CommitCurrent(); });
            _layoutCursor = new LayoutCursor(_config.CursorSize);
            _caretOverlay = new CaretOverlay(_config.CursorSize, _config.CaretDotMode);

            // The saved keep-awake state, re-applied before the tray items are built so their Checked
            // flags are seeded from the live state rather than the other way round. The request is
            // bound to the calling thread, and this constructor runs on the tray UI thread, which
            // lives as long as the process does.
            KeepAwake.Restore(_config.KeepSystemAwake, _config.KeepScreenOn);

            // SetSystemCursor is global and the keep-awake request changes the system idle policy -
            // guarantee both are restored even if the app is killed or throws. A clean exit also takes
            // the layout files away, so their absence tells the VS Code extension nothing is running
            // (LAYOUT-SIGNAL rule 6). Only here, in the primary instance: the forwarding and one-shot
            // paths in Program never construct this context, and must not delete a live instance's files.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); LayoutPublisher.Retract(); };
            AppDomain.CurrentDomain.UnhandledException += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); LayoutPublisher.Retract(); };
            Application.ApplicationExit += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); LayoutPublisher.Retract(); };

            // ---- Autostart ----
            if (Autostart.ManagedByWindows)
            {
                _autostartItem = new ToolStripMenuItem("Start with Windows..", null, OnOpenStartupSettings);
            }
            else
            {
                _autostartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutostart)
                {
                    CheckOnClick = true,
                    Checked = Autostart.IsEnabled,
                };
            }

            // ---- Feature toggle items ----
            _cursorItem = new ToolStripMenuItem("Cursor: layout indicator")
            {
                CheckOnClick = true,
                Checked = _config.EnableCursorChange,
            };
            _cursorItem.CheckedChanged += OnCursorToggle;

            _caretItem = new ToolStripMenuItem("Caret: overlay label")
            {
                CheckOnClick = true,
                Checked = _config.EnableCaretOverlay,
            };
            _caretItem.CheckedChanged += OnCaretToggle;

            _dotModeItem = new ToolStripMenuItem("Caret: dot style")
            {
                CheckOnClick = true,
                Checked = _config.CaretDotMode,
                Enabled = _config.EnableCaretOverlay,
            };
            _dotModeItem.CheckedChanged += OnDotModeToggle;

            _langSwitchItem = new ToolStripMenuItem("Change the layout after converting text")
            {
                CheckOnClick = true,
                Checked = _config.EnableLanguageSwitch,
            };
            _langSwitchItem.CheckedChanged += OnLangSwitchToggle;

            _capsAfterItem = new ToolStripMenuItem("Synchronize CapsLock after the case flip")
            {
                CheckOnClick = true,
                Checked = _config.FlipCapsLockAfter,
            };
            _capsAfterItem.CheckedChanged += OnCapsAfterToggle;

            // ---- Keep-awake toggles (persisted in AppConfig since 2026-07-28) ----
            _keepAwakeItem = new ToolStripMenuItem { CheckOnClick = true, Checked = KeepAwake.KeepSystemAwake };
            _keepAwakeItem.CheckedChanged += OnKeepAwakeToggle;
            _keepScreenItem = new ToolStripMenuItem { CheckOnClick = true, Checked = KeepAwake.KeepScreenOn };
            _keepScreenItem.CheckedChanged += OnKeepScreenToggle;

            _historyEnabledItem = new ToolStripMenuItem { CheckOnClick = true, Checked = _config.EnableClipboardHistory };
            _historyEnabledItem.CheckedChanged += OnHistoryEnabledToggle;
            _historyPauseItem = new ToolStripMenuItem { CheckOnClick = true, Checked = _config.PauseClipboardHistory, Enabled = _config.EnableClipboardHistory };
            _historyPauseItem.CheckedChanged += OnHistoryPauseToggle;
            _showHistoryItem = new ToolStripMenuItem(null, null, (_, _) => _clipboardHistoryWindow.ToggleVisible());
            // Same discipline as the launcher submenu: present but invisible while the feature is
            // off, so a user who never enables the translator sees the menu they always had.
            _translation = new TranslationService(_config);
            _translateClipboardItem = new ToolStripMenuItem(null, null, (_, _) => TranslateClipboard())
            {
                Visible = _config.EnableTranslate,
            };
            // Same discipline as the launcher submenu and the translate entry: present but invisible
            // while the feature is off, so a user who never enables it sees the menu they always had.
            _quickNotesItem = new ToolStripMenuItem(null, null, (_, _) => ShowQuickNotes(startNew: false))
            {
                Visible = _config.EnableQuickNotes,
            };
            _screenshotItem = new ToolStripMenuItem(null, null, (_, _) => StartRegionCaptureDeferred());
            _screenshotDelay.Tick += (_, _) => { _screenshotDelay.Stop(); StartRegionCapture(); };
            _settingsItem = new ToolStripMenuItem(null, null, (_, _) => ShowSettings());
            _exitItem = new ToolStripMenuItem(null, null, (_, _) => ExitThread());

            // ---- Menu ----
            var menu = new ContextMenuStrip();
            menu.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            _textMenu.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            menu.Items.Add(_showHistoryItem);
            menu.Items.Add(_historyEnabledItem);
            menu.Items.Add(_historyPauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_cursorItem);
            menu.Items.Add(_caretItem);
            menu.Items.Add(_dotModeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_keepAwakeItem);
            menu.Items.Add(_keepScreenItem);
            menu.Items.Add(new ToolStripSeparator());
            // The Launcher submenu exists only while the feature is on (spec §4): when disabled the
            // item is invisible and the ordinary menu is exactly as long as before the feature.
            menu.Items.Add(_launcherMenu);
            menu.Items.Add(_translateClipboardItem);
            menu.Items.Add(_quickNotesItem);
            menu.Items.Add(_screenshotItem);
            menu.Items.Add(_settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitItem);

            // Keep dynamic state in sync when the menu opens.
            menu.Opening += (_, _) =>
            {
                // Both builds can now answer this truthfully: the packaged one reads the startupTask.
                _autostartItem.Checked = Autostart.IsEnabled;
                // Dot style only makes sense when the caret overlay is on.
                _dotModeItem.Enabled = _caretItem.Checked;
                _historyPauseItem.Enabled = _historyEnabledItem.Checked;
                // The chord is shown only while it would actually fire.
                _screenshotItem.ShortcutKeyDisplayString = _config.EnableHotkeys && ScreenshotChordLive ? _screenshotHotkey.Display : null;
                _showHistoryItem.ShortcutKeyDisplayString = _config.EnableHotkeys && _config.EnableHistoryHotkey ? _clipboardHistoryHotkey.Display : null;
                _quickNotesItem.ShortcutKeyDisplayString = _config.EnableHotkeys && QuickNotesChordLive ? _quickNotesHotkey.Display : null;
            };

            Icon initialIcon = TryGetAppIcon();
            _tray = new NotifyIcon
            {
                Icon = initialIcon,
                Text = "CyrFlip",
                Visible = true,
                ContextMenuStrip = menu,
            };
            // Track it for disposal when the first layout icon replaces it - but never dispose
            // the shared SystemIcons.Application.
            if (initialIcon != SystemIcons.Application)
                _trayIcon = initialIcon;

            _settings = new SettingsForm(_config,
                SetAutostartFromSettings,
                value => _cursorItem.Checked = value, value => _caretItem.Checked = value, value => _dotModeItem.Checked = value,
                value => _langSwitchItem.Checked = value, value => _capsAfterItem.Checked = value,
                value => _historyEnabledItem.Checked = value, value => _historyPauseItem.Checked = value, SetHistoryStartup,
                SetHistoryOpacity, SetUiLanguage,
                () => OnSetCaseHotkey(null, EventArgs.Empty), () => OnSetHistoryHotkey(null, EventArgs.Empty), ShowHistorySearch,
                () => _clipboardHistory.Clear(), () => OnDiagnoseCaret(null, EventArgs.Empty),
                SetHotkeysEnabled, SetCaseHotkeyEnabled, SetHistoryHotkeyEnabled, SetDeferToRemoteDesktop,
                value => _keepAwakeItem.Checked = value, value => _keepScreenItem.Checked = value,
                _launcherStore, SetLauncherEnabled,
                OnSetQuickNotesHotkey, () => ShowQuickNotes(startNew: false), ClearQuickNotes, ExportQuickNotes, RunExchange,
                () => _clipboardHistory.Entries.Count, () => _quickNotes?.Notes.Count ?? 0);
            _settings.ConversionProfilesChanged += (_, _) => OnConversionProfilesChanged();
            _settings.QuickNotesChanged += (_, _) => OnQuickNotesChanged();
            _settings.GraphicsChanged += (_, _) => OnGraphicsChanged();
            _settings.ScreenshotHotkeyChangeRequested += (_, _) => OnSetScreenshotHotkey();
            _settings.TextMenuHotkeyChangeRequested += (_, _) => OnSetTextMenuHotkey();
            _settings.ScreenshotRequested += (_, _) => StartRegionCaptureDeferred();
            _settings.LauncherScenariosChanged += (_, _) => RefreshLauncherSurfaces();
            _settings.TranslationChanged += (_, _) => OnTranslationChanged();
            _settings.TextMenuChanged += (_, _) => OnContextMenuChanged();
            _settings.MarkerSizeChanged += (_, _) =>
            {
                _config.Save();
                _caretOverlay.SetBaseSize(_config.CursorSize);
                _layoutCursor.SetBaseSize(_config.CursorSize);
            };
            // Tray mouse: double click opens Settings, single click walks the OS layout rotation.
            // The shell delivers the first click of a double click as an ordinary click too, so the
            // switch waits out the system double-click time before acting - otherwise every trip to
            // Settings would also flip the layout.
            _trayClickTimer.Interval = Math.Max(200, SystemInformation.DoubleClickTime);
            _trayClickTimer.Tick += (_, _) => { _trayClickTimer.Stop(); SwitchLayoutFromTray(); };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _trayClickTimer.Stop();
                _trayClickTimer.Start();
            };
            _tray.MouseDoubleClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _trayClickTimer.Stop();
                ShowSettings();
            };
            _clipboardHistoryWindow.VisibleChanged += (_, _) =>
            {
                UpdateTrayTexts();
                // Remember whether the manager window is open so the next launch restores that
                // state: if you closed it, it stays closed instead of always reopening.
                if (_config.ShowClipboardHistoryOnStartup != _clipboardHistoryWindow.Visible)
                {
                    _config.ShowClipboardHistoryOnStartup = _clipboardHistoryWindow.Visible;
                    _config.Save();
                }
            };
            UpdateTrayTexts();

            _indicator.LayoutChanged += OnLayoutChanged;
            // Every hook event is handed to the UI thread and nothing is done inside the callback -
            // which runs while the whole machine's keyboard waits on us, under the ~300 ms
            // LowLevelHooksTimeout after which Windows silently drops the hook. What used to run in
            // there: file I/O (the translator's log), showing a window with AttachThreadInput (the
            // history), and spinning up a thread (the flips). The callback still decides whether to
            // swallow the key before it returns - only the work moves.
            // First, so it is posted ahead of the chord's own action: tap the mask key while the
            // modifiers are still down, or Windows reads their release as a bare Ctrl+Shift / Alt
            // gesture (layout switch, menu bar) - it never saw the trigger (ticket S0004, KC-4).
            _hook.ChordFired += held => _ui?.Post(_ => KeyInjection.SendMask(held), null);
            _hook.CaseHotkeyPressed += (_, _) => _ui?.Post(_ => OnCaseHotkeyPressed(null, EventArgs.Empty), null);
            _hook.ClipboardHistoryHotkeyPressed += (_, _) => _ui?.Post(_ => _clipboardHistoryWindow.ToggleVisible(), null);
            _hook.QuickNotesHotkeyPressed += (_, _) => _ui?.Post(_ => ShowQuickNotes(startNew: true), null);
            // The chord starts the grab at once - that is what freezes a tooltip or an open menu (S0026 scenario 2).
            _hook.ScreenshotHotkeyPressed += (_, _) => _ui?.Post(_ => StartRegionCapture(), null);
            _hook.LayoutConversionHotkeyPressed += id => _ui?.Post(_ => OnLayoutConversionHotkeyPressed(id), null);
            _hook.LauncherHotkeyPressed += OnLauncherHotkeyPressed; // already posts, and re-checks the switch there
            _hook.TranslateHotkeyPressed += id => _ui?.Post(_ => OnTranslateHotkeyPressed(id), null);
            _hook.CancelKeyPressed += (_, _) => _ui?.Post(_ => CancelTranslationFromEscape(), null);
            // F6 into the finished translation popup and back out (S0045 K1).
            _hook.FocusKeyPressed += (_, _) => _ui?.Post(_ =>
            {
                if (_translateWindow != null && !_translateWindow.IsDisposed) _translateWindow.ToggleKeyboardFocus();
            }, null);
            // Mouse chord → own context menu. The probe starts on the press so the 80-150 ms the user
            // spends holding the button pays for it; the menu itself is only ever shown from the UI
            // thread, never from inside the hook callback.
            _mouseHook.ChordPressed += (x, y) =>
            {
                // First, the mask key, posted like the keyboard chord's (KC-4): Windows never saw the
                // swallowed click, so the release of the Alt or Shift of the chord would reach it as a
                // bare tap - menu mode in Notepad/Explorer/Office, an IME mode switch (S0033 KC2-3).
                // Only the snapshot is taken in here; the SendInput runs on the UI thread, still
                // well before the user lets go of the modifier.
                SideModifiers held = PhysicalModifiers.Shared.Snapshot();
                if ((held & ~SideModifiers.Ctrl) != 0)
                    _ui?.Post(_ => KeyInjection.SendMask(held), null);
                // Remember the window the menu is being opened over. Every action of this menu
                // synthesizes input, and synthesized input follows the foreground window - so the
                // one thing we must not lose is which window that was before the menu appeared.
                _textMenuTarget = WindowInterop.GetForegroundWindow();
                // The pointer position is half the question: in read-only text the selection is
                // under the pointer while the focus is elsewhere entirely (see SelectionProbe).
                // Stamp and signal only - the probe thread already exists (S0030 HT-3).
                _selectionProbe = _probeWorker?.Request(x, y);
            };
            _mouseHook.ChordReleased += (x, y) => _ui?.Post(_ => ShowTextContextMenu(x, y), null);
            _hook.TextMenuHotkeyPressed += (_, _) => _ui?.Post(_ => OpenTextMenuFromKeyboard(), null);
            _mouseHook.ForeignButtonDown += (x, y) => _ui?.Post(_ => CloseTextMenuIfOutside(x, y), null);
            _textMenu.Closed += (_, e) =>
            {
                _mouseHook.UpdateForeignClickWatch(false);
                // A menu opened from the keyboard held the foreground; closed without a command (Esc,
                // a click elsewhere) it hands it back. A command does that itself (DeferOnTarget).
                if (_textMenuFromKeyboard && e.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
                    _ui?.Post(_ => RestoreTargetForeground(), null);
                _textMenuFromKeyboard = false;
            };
            _clipboardHistory.ItemTooLarge += (_, _) => _tray.ShowBalloonTip(2000, "CyrFlip", T("Фрагмент слишком велик для истории (>128 КБ)."), ToolTipIcon.Info);
            _clipboardHistory.ClearFailed += (_, _) => _tray.ShowBalloonTip(3000, "CyrFlip",
                T("Файл истории буфера занят другой программой, и он не удалён. Повторите очистку."), ToolTipIcon.Warning);
            // Once per session: the service raises it for the first failed write only (S0031 CH2-4).
            _clipboardHistory.WriteFailed += (_, _) => _tray.ShowBalloonTip(4000, "CyrFlip",
                T("Запись в файл истории буфера не удалась - новые записи могут не сохраниться после перезапуска."), ToolTipIcon.Warning);
            // Once per session, like the quick notes: a damaged line now costs only itself (S0005 CH-2).
            if (_config.EnableClipboardHistory && _clipboardHistory.SkippedRecords > 0)
                _tray.ShowBalloonTip(4000, "CyrFlip",
                    Localization.Format(T, "Не прочитано записей истории буфера: {0}. Остальная история на месте.",
                        _clipboardHistory.SkippedRecords), ToolTipIcon.Warning);
            // Once per start while any stored value is unreadable (S0007 CF-1): each fell back to its
            // own default, and the tables and snapshots among them stay untouched on disk.
            if (_config.UnreadableValues.Count > 0)
                _tray.ShowBalloonTip(4000, "CyrFlip",
                    T("Часть настроек CyrFlip не удалось прочитать - они оставлены как есть."), ToolTipIcon.Warning);
            _caretOverlay.Start();
            _indicator.Start();

            // History is an opt-in feature, but once opted in it should be available immediately
            // after every subsequent launch rather than waiting for a tray action.
            if (_config.EnableClipboardHistory && _config.ShowClipboardHistoryOnStartup)
                _clipboardHistoryWindow.ToggleVisible();

            // The WinForms sync context - lets the background flip thread post tray feedback to the UI.
            // It exists because a control was created on this thread before this line (the field
            // initializers already build one); nothing that needs it may run before it is read here.
            // The clipboard history does not use it at all: it posts to its own window (S0031 CH2-2).
            _ui = SynchronizationContext.Current;

            // Only now, for the same reason the mouse hook and the IPC listener wait: every hook
            // event is posted to _ui, so a chord pressed while it was still null would be swallowed
            // and then dropped - the key eaten, nothing done.
            _hook.Install(_caseHotkey, _clipboardHistoryHotkey, _quickNotesHotkey,
                _config.DeferToRemoteDesktop, _config.EnableHotkeys,
                _config.EnableCaseHotkey, _config.EnableHistoryHotkey, QuickNotesChordLive);
            _hook.UpdateScreenshotHotkey(_screenshotHotkey);
            _hook.UpdateScreenshotEnabled(ScreenshotChordLive);
            // The chord arrived with an update (S0045 K2): a chord the user had already given to something
            // else stays theirs, and this one is switched off rather than bound twice.
            if (TextMenuChordLive && Chords().CyrFlipOwnerOf(ParsedTextMenuHotkey(), ChordKind.TextMenu) != null)
            {
                _config.EnableTextMenuHotkey = false;
                _config.Save();
            }
            _hook.UpdateTextMenuHotkey(ParsedTextMenuHotkey());
            _hook.UpdateTextMenuEnabled(TextMenuChordLive);
            _textMenuChordWasLive = TextMenuChordLive;
            _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            BindTranslationHotkeys();
            _hookWatchdog.Interval = HookWatchdogIntervalMs;
            _hookWatchdog.Tick += (_, _) => OnHookWatchdogTick();
            // Keys pressed or released on the lock screen never reached the hook.
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            _hookWatchdog.Start();

            // Launcher surfaces (tray submenu + Jump List + hook chords) and the command listener.
            // Started after _ui is captured: the IPC thread posts every command to the UI thread.
            // The listener always runs, for /exit alone while the launcher is off (S0008 LS-2) -
            // RefreshLauncherSurfaces tells it which commands it may pass on.
            _launcherIpc = new LauncherIpc(cmd => _ui?.Post(_ => OnLauncherIpcCommand(cmd), null));
            RefreshLauncherSurfaces();
            // The mouse hook, likewise, only after _ui exists - its callback posts to the UI thread.
            RefreshContextMenuBinding();
            _launcherIpc.Start();

            // Sign-out / shutdown: the one exit on which neither Dispose nor ApplicationExit runs.
            _sessionEnd.QueryEnding += (_, _) => FlushQuickNotes();
            _sessionEnd.Ending += (_, _) => OnSessionEnding();
            // A system cursor reload (pointer size or colour, a theme, another tool) takes the branded
            // I-beam away, and a pointer-size or scaling change asks for a new size (S0011 LI-3, LI-6).
            _sessionEnd.SettingChanged += action => _layoutCursor.OnSystemCursorsChanged(action == WindowInterop.SPI_SETCURSORS);
            _sessionEnd.DisplayChanged += (_, _) =>
            {
                _layoutCursor.OnSystemCursorsChanged(cursorsReloaded: false);
                _caretOverlay.OnDisplayChanged(); // the badge re-reads its monitor's DPI (S0036 UI-4)
            };
            // Windows switched light/dark, high contrast or its colours: every open window follows (S0020).
            _sessionEnd.ThemeSignal += (_, _) => ThemeManager.OnSystemSignal();

            if (openSettingsOnStart)
                _ui?.Post(_ => ShowSettings(), null);
        }

        private void OnLayoutChanged(string code, string klid, bool capsOn)
        {
            _currentLayout = code;
            _currentKlid = klid;
            _capsOn = capsOn;

            // Cursor indicator (global I-beam replacement). A 1px frame flags CapsLock.
            if (_config.EnableCursorChange)
                _layoutCursor.Apply(code, klid, capsOn);
            else
                _layoutCursor.Restore();

            // Caret overlay (text label or dot near the blinking caret).
            _caretOverlay.SetLayout(_config.EnableCaretOverlay ? code : "", klid, capsOn);
            LayoutPublisher.Publish(code, klid);

            UpdateTrayTooltip();
            Icon icon = CursorIndicator.RenderIcon(code, klid, capsOn);
            _tray.Icon = icon;
            _trayIcon?.Dispose();
            _trayIcon = icon;
        }

        private void OnCaseHotkeyPressed(object? sender, EventArgs e)
            => RunClipboardOp(
                () => _clipboard.FlipCase(_config.FlipCapsLockAfter),
                ClipboardHandler.FlipResult.Flipped, _config.IncrementCaseFlipCount);

        private void OnLayoutConversionHotkeyPressed(string id)
        {
            LayoutConversionProfile? profile = _config.LayoutConversionProfiles.Find(p => p.Id == id && p.Enabled);
            if (profile == null) return;
            RunClipboardOp(() => _clipboard.ConvertLayout(profile, _config.EnableLanguageSwitch, _config.ConvertSymbols),
                ClipboardHandler.FlipResult.Flipped, _config.IncrementFlipCount);
        }

        private void OnConversionProfilesChanged()
        {
            _config.Save();
            _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Run a clipboard transform off the hook on a dedicated background thread. Every layout
        /// conversion and the case flip share one <see cref="_busy"/> guard so they never run
        /// concurrently (all synthesize Ctrl+C/Ctrl+V and own the clipboard) and so key auto-repeat
        /// can't re-enter.
        /// </summary>
        /// <param name="suppressHistory">
        /// False only for the context menu's own Copy/Cut: a flip's internal copy is scaffolding and
        /// has no business in the history, but a copy the user explicitly asked for is the point.
        /// </param>
        private void RunClipboardOp(Func<ClipboardHandler.FlipResult> op,
            ClipboardHandler.FlipResult countOn, Action onCounted, bool suppressHistory = true)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                // Used to return in silence, exactly like a dead hotkey - and a clipboard owner that
                // hangs can keep the guard taken for seconds (ticket S0009, FP-5). The translator
                // already said so; the flips now say it too, at most once per BusyNotice interval.
                long now = PasteWait.NowMs();
                if (BusyNotice.ShouldShow(_busyNoticeShownAtMs, now))
                {
                    _busyNoticeShownAtMs = now;
                    _tray.ShowBalloonTip(2000, "CyrFlip",
                        T("Другая операция с буфером ещё не закончилась. Повторите через секунду."), ToolTipIcon.Info);
                }
                return;
            }

            StartClipboardWorker(() =>
            {
                // Bracketed by the operation, not by the clock: the history skips everything up to the
                // clipboard's state after the restore, however long the flip took (S0005 CH-5).
                if (suppressHistory) _clipboardHistory.SuppressBegin();
                try
                {
                    ClipboardHandler.FlipResult result = op();
                    if (result == countOn)
                        onCounted();
                    _ui?.Post(_ => ShowFlipResult(result), null);
                }
                catch { /* never let a clipboard op take the app down */ }
                finally
                {
                    if (suppressHistory) _clipboardHistory.SuppressEnd();
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }

        /// <summary>
        /// Starts <paramref name="body"/> on a background worker for a caller that already holds
        /// <see cref="_busy"/>. The worker's own <c>finally</c> releases the guard - and never runs if
        /// the thread never starts, so a <c>Thread.Start</c> that throws (out of memory, thread quota)
        /// used to leave every flip, case flip, capture and translation dead until a restart, silently.
        /// The release lives here, once, so the pattern cannot drift between call sites.
        /// </summary>
        private void StartClipboardWorker(ThreadStart body) => StartClipboardWorker(ref _busy, body);

        /// <summary>The testable core of <see cref="StartClipboardWorker(ThreadStart)"/>; <paramref name="start"/> is the seam.</summary>
        internal static void StartClipboardWorker(ref int busy, ThreadStart body, Action<Thread>? start = null)
        {
            var thread = new Thread(body) { IsBackground = true };
            try { (start ?? (t => t.Start()))(thread); }
            catch { Interlocked.Exchange(ref busy, 0); throw; }
        }

        /// <summary>How often both low-level hooks are re-armed. See <see cref="OnHookWatchdogTick"/>.</summary>
        private const int HookWatchdogIntervalMs = 60 * 1000;

        /// <summary>Consecutive failures before the user is told; a single one is not worth a balloon.</summary>
        private const int HookFailuresBeforeWarning = 3;

        /// <summary>
        /// Re-arm the low-level hooks. Windows removes a hook whose thread overran
        /// <c>LowLevelHooksTimeout</c> (~300 ms) and reports that to nobody - the app keeps running
        /// with every chord dead, which is exactly the "hotkeys stopped working after a while"
        /// failure. There is no way to query whether we are still hooked, so the hook is simply put
        /// back every minute: two API calls, microseconds, against a failure that otherwise lasts
        /// until the user restarts CyrFlip.
        ///
        /// <para>A failure to re-arm is real (the hook is gone), but a single one is not worth
        /// interrupting anybody - it is retried on the next tick, and only a run of them earns one
        /// balloon. The counter resets on success, so the warning cannot repeat every minute.</para>
        /// </summary>
        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            // Raised on a SystemEvents thread; the table and the fired-trigger state belong to the
            // hook's (UI) thread.
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
                _ui?.Post(_ => _hook.RefreshModifiers(), null);
        }

        private void OnHookWatchdogTick()
        {
            // The system's modifier state is false while a clipboard operation is synthesizing keys,
            // so the physical table is only re-read from it while none is (ticket S0004, KC-1).
            bool ok = _hook.Reinstall(refreshModifiers: Volatile.Read(ref _busy) == 0);
            ok &= _mouseHook.Reinstall();

            if (ok)
            {
                _hookReinstallFailures = 0;
                return;
            }

            _hookReinstallFailures++;
            if (_hookReinstallFailures != HookFailuresBeforeWarning) return;
            _tray.ShowBalloonTip(4000, "CyrFlip",
                T("Windows не отдаёт перехват клавиатуры — горячие клавиши могут не работать. Помогает перезапуск CyrFlip."),
                ToolTipIcon.Warning);
        }

        private void ShowFlipResult(ClipboardHandler.FlipResult result)
        {
            switch (result)
            {
                case ClipboardHandler.FlipResult.NoSelection:
                    _tray.ShowBalloonTip(1500, "CyrFlip", T("Ничего не выделено. Я переворачиваю текст, а не воздух — сначала выделите что-нибудь."), ToolTipIcon.Info);
                    break;
                case ClipboardHandler.FlipResult.Failed:
                    _tray.ShowBalloonTip(2000, "CyrFlip", T("Не удалось прочитать или заменить выделение. У буфера обмена были другие планы."), ToolTipIcon.Warning);
                    break;
                case ClipboardHandler.FlipResult.TooLarge:
                    _tray.ShowBalloonTip(2500, "CyrFlip", T("Выделение слишком большое для конвертации (больше миллиона символов). Выделите часть."), ToolTipIcon.Info);
                    break;
            }
        }

        /// <summary>The balloon for a capture of the translator or the quick notes that did not produce text.</summary>
        private void ShowCaptureResult(ClipboardHandler.CaptureResult result)
        {
            switch (result)
            {
                case ClipboardHandler.CaptureResult.Failed: ShowFlipResult(ClipboardHandler.FlipResult.Failed); break;
                case ClipboardHandler.CaptureResult.TooLarge: ShowFlipResult(ClipboardHandler.FlipResult.TooLarge); break;
                default: ShowFlipResult(ClipboardHandler.FlipResult.NoSelection); break;
            }
        }

        // ---- Text context menu (spec: PLAN/ContextMenu_Spec_Idea_v0.1.md) ----

        /// <summary>
        /// Install, retune or remove the mouse hook to match the settings. While the feature is off
        /// the hook is <b>not installed at all</b>: a WH_MOUSE_LL hook sits in the path of every
        /// mouse move on the machine, and a GC pause on its thread stalls the pointer system-wide.
        /// </summary>
        private void RefreshContextMenuBinding()
        {
            if (_config.EnableContextMenu)
            {
                MouseChord chord = MouseChord.Parse(_config.ContextMenuChord);
                // The probe's thread lives exactly as long as the hook, created here rather than
                // inside the hook callback (S0030 HT-3).
                _probeWorker ??= new SelectionProbe.Worker();
                try
                {
                    if (_mouseHook.Installed) _mouseHook.UpdateChord(chord);
                    else _mouseHook.Install(chord);
                }
                catch
                {
                    // A hook Windows refuses is a feature that does not work, not a crash.
                    _tray.ShowBalloonTip(3000, "CyrFlip",
                        T("Не удалось перехватить мышь, контекстное меню не будет открываться."),
                        ToolTipIcon.Warning);
                }
                return;
            }

            _probeWorker?.Dispose();
            _probeWorker = null;
            if (!_mouseHook.Installed) return;
            if (_textMenu.Visible) _textMenu.Close();
            _mouseHook.Dispose();
        }

        /// <summary>The context-menu settings changed (the settings window writes straight into the config).</summary>
        private void OnContextMenuChanged()
        {
            // The keyboard chord going live is checked like assigning it (ticket S0004, KC-6).
            if (TextMenuChordLive && !_textMenuChordWasLive
                && Hotkey.TryParse(_config.TextMenuHotkey, out Hotkey chord)
                && !ChordIsFree(chord, ChordKind.TextMenu, askAboutWindows: false))
            {
                _config.EnableTextMenuHotkey = false;
                _ui?.Post(_ => _settings.Reload(), null);
            }
            _textMenuChordWasLive = TextMenuChordLive;
            _hook.UpdateTextMenuHotkey(ParsedTextMenuHotkey());
            _hook.UpdateTextMenuEnabled(TextMenuChordLive);
            _config.Save();
            RefreshContextMenuBinding();
        }

        /// <summary>
        /// The chord was released: collect whatever the selection probe has and show the menu at the
        /// pointer. Collecting can block this thread for the remainder of
        /// <see cref="SelectionProbe.BudgetMs"/> - deliberately bounded well under the 300 ms Windows
        /// gives a low-level hook, since the keyboard hook shares this thread.
        /// </summary>
        private void ShowTextContextMenu(int x, int y, bool fromKeyboard = false)
        {
            if (!_config.EnableContextMenu) return;
            if (_textMenu.Visible) _textMenu.Close();

            SelectionSnapshot selection = _selectionProbe?.Collect() ?? SelectionSnapshot.Unknown;
            _selectionProbe = null;

            // Actions that synthesize input go through DeferOnTarget: they must land in the window the
            // menu was opened over. The two that merely open a window of ours use plain Defer.
            TextContextMenu.Rebuild(_textMenu, BuildTextMenuState(selection), T,
                command => { TextMenuLog.Log("click: " + command); DeferOnTarget(() => RunEditCommand(command)); },
                target => { TextMenuLog.Log("click: launch " + target.Kind); Defer(() => LaunchSelection(target)); },
                id => { TextMenuLog.Log("click: conversion"); DeferOnTarget(() => OnLayoutConversionHotkeyPressed(id)); },
                () => { TextMenuLog.Log("click: case flip"); DeferOnTarget(() => OnCaseHotkeyPressed(null, EventArgs.Empty)); },
                id => { TextMenuLog.Log("click: translate"); DeferOnTarget(() => OnTranslateHotkeyPressed(id)); },
                () => { TextMenuLog.Log("click: quick note"); DeferOnTarget(CaptureSelectionIntoQuickNote); },
                _config.EnableScenarioLauncher ? (Action<ToolStripMenuItem>)FillLauncherSubmenu : null,
                () => { TextMenuLog.Log("click: history"); Defer(() => _clipboardHistoryWindow.ToggleVisible()); },
                () => { TextMenuLog.Log("click: settings"); Defer(ShowSettings); });

            if (_textMenu.Items.Count == 0) return;
            // The selected text itself is deliberately absent from the log - only its shape.
            TextMenuLog.Log("menu shown at " + x + "," + y + " with " + _textMenu.Items.Count
                + " items, selection=" + selection.State
                + (selection.Text == null ? ", text unavailable" : ", text " + selection.Text.Length + " chars")
                + ", target " + TextMenuLog.Describe(_textMenuTarget));

            // A drop-down whose owner is not the foreground window never sees the click that should
            // dismiss it - so the mouse hook watches for one while the menu is up. SetForegroundWindow
            // (the trick the taskbar button uses) is out of the question here: it would take the focus
            // away from the very field whose selection we are about to work on.
            _mouseHook.UpdateForeignClickWatch(true);
            if (fromKeyboard)
            {
                // The menu never takes the focus from the selection when the mouse opened it; from the
                // keyboard the arrows have to reach it, so it takes the foreground the way the tray's
                // own menu does - through a hidden window of ours - and hands it back on close.
                _textMenuFromKeyboard = true;
                ForegroundActivator.Activate(_sessionEnd.Handle);
            }
            _textMenu.Show(new Point(x, y));
            if (fromKeyboard) SelectFirstTextMenuItem();
        }

        /// <summary>
        /// The keyboard chord (S0045 K2, <c>INPUT-PARITY</c> rule 1): the same menu, opened at the text
        /// caret - the system caret, else the one the caret overlay last found in this window, else the
        /// pointer. The selection probe is asked at that point exactly as for the mouse chord; there is
        /// no held button to pay for it here, so <see cref="SelectionProbe.MaxWaitMs"/> is the cost.
        /// </summary>
        private void OpenTextMenuFromKeyboard()
        {
            if (!_config.EnableContextMenu || !_config.EnableTextMenuHotkey) return;
            IntPtr target = WindowInterop.GetForegroundWindow();
            Point at = TextMenuAnchor(target);
            _textMenuTarget = target;
            _selectionProbe = _probeWorker?.Request(at.X, at.Y);
            TextMenuLog.Log("keyboard chord at " + at.X + "," + at.Y);
            ShowTextContextMenu(at.X, at.Y, fromKeyboard: true);
        }

        /// <summary>Just below the caret's line, so the menu never covers the selection's own line.</summary>
        private Point TextMenuAnchor(IntPtr window)
        {
            if (CaretOverlay.TrySystemCaret(window, out CaretRect caret) || _caretOverlay.TryGetRecentCaret(window, out caret))
                return new Point(caret.X, caret.Bottom + 2);
            return Cursor.Position;
        }

        private void SelectFirstTextMenuItem()
        {
            foreach (ToolStripItem item in _textMenu.Items)
                if (item.Available && item.Enabled && item.CanSelect && !(item is ToolStripSeparator) && !(item is ToolStripLabel))
                {
                    item.Select();
                    return;
                }
        }

        /// <summary>
        /// The outside click that dismisses the menu - and the single most dangerous line in this
        /// feature, because it runs on the <b>button-down</b> while the item's <c>Click</c> is only
        /// raised on the button-up. Get "outside" wrong and the menu closes in between: the item is
        /// disposed before it is ever clicked, and every command silently does nothing.
        ///
        /// So "inside" is not decided by comparing coordinates against a drop-down's bounds - which
        /// misses the sub-menu (a window of its own) and trusts WinForms' idea of a top-level
        /// control's rectangle. It is decided by asking Windows <b>which window is under the
        /// pointer</b>: if it belongs to this process, the click is ours and the menu stays.
        /// </summary>
        private void CloseTextMenuIfOutside(int x, int y)
        {
            if (!_textMenu.Visible) return;
            if (OverOwnWindow(x, y)) return;
            TextMenuLog.Log("outside click at " + x + "," + y + " -> closing the menu");
            _textMenu.Close();
        }

        private static readonly uint OwnProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private static bool OverOwnWindow(int x, int y)
        {
            IntPtr hwnd = WindowInterop.WindowFromPoint(new WindowInterop.POINT { X = x, Y = y });
            if (hwnd == IntPtr.Zero) return false;
            WindowInterop.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == OwnProcessId;
        }

        /// <summary>
        /// Everything the menu's shape depends on, read once per opening. Chords are shown only while
        /// they would actually fire, so the menu never advertises a hotkey the user has switched off.
        /// </summary>
        private TextContextMenuState BuildTextMenuState(SelectionSnapshot selection)
        {
            bool chordsLive = _config.EnableHotkeys;
            SelectionStats.Count(selection.Text, out int lines, out int chars);
            var state = new TextContextMenuState
            {
                Selection = selection.State,
                // Parsed on the probe's worker, never here: the hooks share this thread (S0008 LS-1).
                Launch = selection.Launch,
                SelectionLines = lines,
                SelectionChars = chars,
                SelectionTruncated = selection.Truncated,
                ClipboardHasText = WindowInterop.IsClipboardFormatAvailable(WindowInterop.CF_UNICODETEXT),
                CaseShortcut = chordsLive && _config.EnableCaseHotkey ? _caseHotkey.Display : "",
                ShowLauncher = _config.EnableScenarioLauncher,
                ShowHistory = _config.EnableClipboardHistory,
                ShowQuickNotes = _config.EnableQuickNotes,
            };

            foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
                if (profile.Enabled && profile.IsUsable)
                    state.Conversions.Add(new TextMenuRow(
                        WorldLayouts.CodeForKlid(profile.SourceKlid) + " ⇄ " + WorldLayouts.CodeForKlid(profile.TargetKlid),
                        chordsLive ? profile.Hotkey : "", profile.Id));

            if (_config.EnableTranslate)
                foreach (TranslationProfile profile in _config.TranslateProfiles)
                    if (profile.Enabled && profile.IsUsable)
                        state.Translations.Add(new TextMenuRow(
                            TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage),
                            chordsLive ? profile.Hotkey : "", profile.Id));

            return state;
        }

        /// <summary>
        /// The launcher list inside the context menu - built by the same code as the tray's, but with
        /// its actions deferred for the same reason as the rest of this menu's (see above).
        /// </summary>
        private void FillLauncherSubmenu(ToolStripMenuItem parent)
            => LauncherTrayMenu.Rebuild(parent, _launcherStore.All,
                scenario => Defer(() => RunLauncherScenario(scenario)),
                () => Defer(ShowSettings),
                LauncherMigration.SourceExists() ? (Action)RequestOneClickRunnerImport : null, T);

        /// <summary>Run an action on the next turn of the UI thread's message loop.</summary>
        private void Defer(Action action) => _ui?.Post(_ => action(), null);

        /// <summary>
        /// Run a menu action <b>against the window the menu was opened over</b>, on the next turn of
        /// the message loop.
        ///
        /// Both halves are load-bearing. Deferring matters because inside the <c>Click</c> the
        /// drop-down is still tearing down and still holds the mouse capture. Restoring the
        /// foreground matters because everything this menu does is ultimately a synthesized
        /// keystroke, and a keystroke goes to <b>whatever window is in the foreground at that
        /// instant</b> - not to the window the user was pointing at. The menu is a no-activate
        /// drop-down precisely so the user's window keeps its selection, but "should not have taken
        /// the focus" is not the same as "did not", and when it did, the chord lands nowhere.
        /// </summary>
        private void DeferOnTarget(Action action) => _ui?.Post(_ =>
        {
            RestoreTargetForeground();
            action();
        }, null);

        private void RestoreTargetForeground()
        {
            IntPtr target = _textMenuTarget;
            IntPtr current = WindowInterop.GetForegroundWindow();
            if (target == IntPtr.Zero || !WindowInterop.IsWindow(target) || current == target)
            {
                TextMenuLog.Log("action: foreground already " + TextMenuLog.Describe(current)
                    + ", target " + TextMenuLog.Describe(target));
                return;
            }

            ForegroundActivator.Activate(target);
            TextMenuLog.Log("action: foreground was " + TextMenuLog.Describe(current)
                + ", restored to " + TextMenuLog.Describe(target)
                + " -> now " + TextMenuLog.Describe(WindowInterop.GetForegroundWindow()));
        }

        /// <summary>
        /// Cut / Copy / Paste - down the same guarded background path as every flip, so there is one
        /// clipboard pipeline in this app and not two. The history is <b>not</b> suppressed here:
        /// a copy the user asked for belongs in it, unlike the internal copy a flip makes.
        /// </summary>
        private void RunEditCommand(ClipboardHandler.EditCommand command)
        {
            TextMenuLog.Log(command + " -> " + TextMenuLog.Describe(WindowInterop.GetForegroundWindow()));
            RunClipboardOp(() => _clipboard.RunEdit(command),
                ClipboardHandler.FlipResult.Flipped, () => { }, suppressHistory: false);
        }

        /// <summary>
        /// Open what the user selected - a link, a file, a folder. The target came out of an
        /// untrusted window through <see cref="LaunchTargets"/>, which is why <b>anything that runs
        /// code is confirmed first</b>, naming the full path: the menu caption is elided, and a
        /// truncated path is exactly what a plausible-looking one relies on.
        ///
        /// It deliberately does not go through <see cref="RunClipboardOp"/>: nothing here touches the
        /// clipboard or the selection, and making it wait on the flip guard would only mean a link
        /// that refuses to open while a translation is running. Nor does it restore the foreground -
        /// the shell brings the browser or the app up by itself.
        /// </summary>
        private void LaunchSelection(LaunchTarget target)
        {
            if (target.Kind == LaunchKind.Program
                && ConfirmDialog.Show(_config.UiLanguage,
                    Localization.Format(T, "Запустить программу из выделенного текста?\n\n{0}\n\nCyrFlip не проверяет, что это за файл.",
                        target.Target),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
            {
                TextMenuLog.Log("launch declined by the user");
                return;
            }

            // The confirmation above stays on this thread; the start does not (S0030 HT-5). A
            // confirmed \\host\share\tool.exe makes ShellExecute open an SMB session first, and for
            // as long as the host does not answer this thread - the hooks' thread - would not either.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target.Target) { UseShellExecute = true });
                    // The target itself is not logged: it is the user's own text, exactly like the
                    // yt-dlp link the launcher keeps out of its log.
                    TextMenuLog.Log("launched a " + target.Kind + " target");
                }
                catch (Exception ex)
                {
                    TextMenuLog.Log("launch failed: " + ex.Message);
                    _ui?.Post(_ => ShowWarningBalloon(3000,
                        Localization.Format(T, "Не удалось запустить «{0}»: {1}", target.Display, FailureCause.Describe(ex, _config.UiLanguage))), null);
                }
            });
        }

        // ---- Screen region capture (ticket S0026) ----

        /// <summary>The chord has only its own switch - there is no module switch (owner, 2026-09-26).</summary>
        private bool ScreenshotChordLive => _config.EnableScreenshotHotkey;

        /// <summary>How long the image write may wait for another clipboard operation to finish.</summary>
        private const int ScreenshotClipboardWaitMs = 3000;

        /// <summary>From the tray or the settings button: let the menu finish closing before the grab.</summary>
        private void StartRegionCaptureDeferred()
        {
            if (_regionOverlay != null) return;
            _screenshotDelay.Stop();
            _screenshotDelay.Start();
        }

        /// <summary>
        /// Freeze every screen first, then show the selection over the frozen picture (S0026 4.1). The
        /// grab is one <c>BitBlt</c> on this thread; the encoding and the clipboard write run on a
        /// worker, so a large region never stalls the loop the keyboard hook shares (4.6). A second
        /// chord while the overlay is open is ignored.
        /// </summary>
        private void StartRegionCapture()
        {
            if (_disposed || _regionOverlay != null) return;

            // Where Ctrl+V goes next: the window the user came from, not the tray's taskbar.
            IntPtr returnTo = _indicator.LastActiveWindow;
            // CAPTURE-OUTPUT rule 3: the name carries the moment the capture started - the freeze.
            DateTime started = DateTime.Now;
            Rectangle screen = ScreenCapture.VirtualScreen;
            Bitmap? frame = ScreenCapture.Grab(screen);
            if (frame == null)
            {
                ShowWarningBalloon(3000, T("Не удалось сделать снимок экрана."));
                return;
            }

            RegionSelectionOverlay overlay;
            try { overlay = new RegionSelectionOverlay(frame, screen, ScreenCapture.MonitorBounds()); }
            catch
            {
                frame.Dispose();
                ShowWarningBalloon(3000, T("Не удалось сделать снимок экрана."));
                return;
            }
            _regionOverlay = overlay;
            overlay.Finished += region =>
            {
                // The crop is a copy, so the frame - the one large allocation - goes at once (section 7).
                Bitmap? cropped = null;
                if (region.HasValue)
                {
                    try { cropped = ScreenCapture.Crop(frame, screen, region.Value); }
                    catch { /* a region outside the frame: treated as a cancel */ }
                }
                frame.Dispose();
                _regionOverlay = null;
                // Not from inside the overlay's own event: its windows are still on the call stack.
                _ui?.Post(_ => overlay.Dispose(), null);
                if (returnTo != IntPtr.Zero) ForegroundActivator.Activate(returnTo);
                if (cropped != null) DeliverScreenshot(cropped, started);
            };
            try { overlay.Show(); }
            catch { overlay.Cancel(); }
        }

        /// <summary>
        /// PNG + DIB on the clipboard under the <see cref="_busy"/> guard (the write only, not the
        /// selection - S0026 5.1), then, when saving is on, the same PNG bytes into the folder outside
        /// the guard (5.4). Clipboard first: it is the result the user is waiting for.
        /// </summary>
        private void DeliverScreenshot(Bitmap cropped, DateTime started)
        {
            bool save = _config.ScreenshotSaveEnabled;
            string folder = _config.ScreenshotFolder;
            var worker = new Thread(() =>
            {
                byte[] png, dib;
                try
                {
                    png = ClipboardImage.EncodePng(cropped);
                    dib = ClipboardImage.EncodeDib(cropped);
                }
                catch
                {
                    _ui?.Post(_ => ShowWarningBalloon(3000, T("Не удалось сделать снимок экрана.")), null);
                    return;
                }
                finally { cropped.Dispose(); }

                bool onClipboard = false;
                if (AcquireClipboardGuard(ScreenshotClipboardWaitMs))
                {
                    try { onClipboard = Win32Clipboard.TrySetImage(png, dib); }
                    catch { /* never let a clipboard op take the app down */ }
                    finally { Interlocked.Exchange(ref _busy, 0); }
                }
                if (onClipboard)
                    _config.IncrementScreenshotCount();
                else
                    _ui?.Post(_ => ShowWarningBalloon(3000,
                        T("Не удалось положить снимок в буфер обмена - он занят другой программой. Повторите.")), null);

                if (!save) return;
                ScreenshotSaver.Result result = new ScreenshotSaver().Save(png, started, folder);
                // CAPTURE-OUTPUT rule 11: a fallback is told in the same moment, naming where it went.
                if (result.Outcome == ScreenshotSaver.Outcome.SavedToFallback)
                    _ui?.Post(_ => ShowInfoBalloon(5000,
                        Localization.Format(T, "Папка для снимков недоступна - снимок сохранён сюда: {0}", result.Folder)), null);
                else if (result.Outcome == ScreenshotSaver.Outcome.Failed)
                {
                    // The failure kinds outlive the balloon (ticket S0026); the reason names exception
                    // types and timeouts, never a path.
                    ScreenshotLog.Log("save failed: " + result.Reason);
                    _ui?.Post(_ => ShowWarningBalloon(5000,
                        T("Снимок в буфере обмена, но сохранить файл не удалось ни в одну папку.")), null);
                }
            })
            { IsBackground = true, Name = "CyrFlip screenshot" };
            try { worker.Start(); }
            catch
            {
                cropped.Dispose();
                ShowWarningBalloon(3000, T("Не удалось сделать снимок экрана."));
            }
        }

        /// <summary>
        /// Take <see cref="_busy"/> for the image write, waiting a little for a flip that is still
        /// running - unlike a chord, a finished selection must not simply be dropped on a busy guard.
        /// </summary>
        private bool AcquireClipboardGuard(int waitMs)
        {
            int waited = 0;
            while (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                if (waited >= waitMs) return false;
                Thread.Sleep(20);
                waited += 20;
            }
            return true;
        }

        private void ShowInfoBalloon(int timeout, string text)
        {
            if (_disposed) return;
            try { _tray.ShowBalloonTip(timeout, "CyrFlip", text, ToolTipIcon.Info); }
            catch { /* the tray icon is being torn down */ }
        }

        /// <summary>Anything on the Graphics tab changed: save and rebind the chord.</summary>
        private void OnGraphicsChanged()
        {
            // The chord going live is checked like assigning it (ticket S0004, KC-6).
            if (ScreenshotChordLive && !_screenshotChordWasLive
                && Hotkey.TryParse(_config.ScreenshotHotkey, out Hotkey chord)
                && !ChordIsFree(chord, ChordKind.Screenshot, askAboutWindows: false))
            {
                _config.EnableScreenshotHotkey = false;
                _ui?.Post(_ => _settings.Reload(), null);
            }
            _screenshotChordWasLive = ScreenshotChordLive;

            _config.Save();
            _screenshotHotkey = Hotkey.TryParse(_config.ScreenshotHotkey, out Hotkey parsed) ? parsed : Hotkey.Parse(AppConfig.DefaultScreenshotHotkey);
            _hook.UpdateScreenshotHotkey(_screenshotHotkey);
            _hook.UpdateScreenshotEnabled(ScreenshotChordLive);
            UpdateTrayTexts();
        }

        /// <summary>The text menu's keyboard chord is bound only while the menu and its own switch are on (S0045 K2).</summary>
        private bool TextMenuChordLive => _config.EnableContextMenu && _config.EnableTextMenuHotkey;

        private Hotkey ParsedTextMenuHotkey()
            => Hotkey.TryParse(_config.TextMenuHotkey, out Hotkey chord) ? chord : Hotkey.Parse(AppConfig.DefaultTextMenuHotkey);

        /// <summary>The text menu's keyboard chord, set from the settings tab and checked against every other owner.</summary>
        private void OnSetTextMenuHotkey()
        {
            using var dlg = new HotkeyDialog(ParsedTextMenuHotkey().Display, T("Задать хоткей контекстного меню"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.TextMenu)) return;

            _config.TextMenuHotkey = next.Display;
            _config.Save();
            _hook.UpdateTextMenuHotkey(next);
            _settings.Reload();
        }

        /// <summary>The capture chord, set from the settings tab and checked against every other owner.</summary>
        private void OnSetScreenshotHotkey()
        {
            using var dlg = new HotkeyDialog(_screenshotHotkey.Display, T("Задать хоткей снимка области экрана"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.Screenshot)) return;

            _screenshotHotkey = next;
            _config.ScreenshotHotkey = next.Display;
            _config.Save();
            _hook.UpdateScreenshotHotkey(next);
            UpdateTrayTexts();
            _settings.Reload();
        }

        // ---- Quick notes (spec: PLAN/QuickNotes_Spec_Idea_v0.1.md) ----

        /// <summary>The chord is bound only while the feature and its own switch are both on.</summary>
        private bool QuickNotesChordLive => _config.EnableQuickNotes && _config.EnableQuickNotesHotkey;

        /// <summary>
        /// The service and its window are built on first use, not at startup: the store replays an
        /// encrypted journal and the window builds a control tree, and neither is anybody's business
        /// while the feature is off. Returns null when it is. The replay runs on the pool (ticket
        /// S0006, QN-2) - this is the thread the keyboard hook runs on - and the window opens at once
        /// in a "loading" state until the notes arrive.
        /// </summary>
        private QuickNotesWindow? EnsureQuickNotes()
        {
            if (!_config.EnableQuickNotes) return null;
            if (_quickNotes == null)
            {
                QuickNotesService service = _quickNotes = new QuickNotesService();
                service.Created += (_, _) => _config.IncrementQuickNoteCount();
                service.Loaded += (_, _) =>
                {
                    var lines = new List<string>();
                    if (service.RecoveredFromBackup)
                        lines.Add(T("Журнал быстрых заметок не найден - заметки восстановлены из резервной копии."));
                    if (service.SkippedRecords > 0)
                        lines.Add(Localization.Format(T, "Не прочитано записей быстрых заметок: {0}. Остальные заметки на месте.",
                            service.SkippedRecords));
                    if (lines.Count > 0 && ReferenceEquals(service, _quickNotes))
                        _tray.ShowBalloonTip(4000, "CyrFlip", string.Join("\n", lines), ToolTipIcon.Warning);
                };
                // After a previous service's background retirement, which may still be compacting
                // this very journal (S0030 HT-6).
                service.LoadAsync(_quickNotesRetired);
            }
            if (_quickNotesWindow == null || _quickNotesWindow.IsDisposed)
                _quickNotesWindow = new QuickNotesWindow(_quickNotes, _config, ShowSettings, exchange: RunExchange);
            return _quickNotesWindow;
        }

        private void ShowQuickNotes(bool startNew)
        {
            QuickNotesWindow? window = EnsureQuickNotes();
            window?.ShowNotes(startNew);
        }

        /// <summary>
        /// The context menu's "save the selection". Phase A of the translator's pipeline, for the
        /// same reason: the selection is taken with the same synthesized Ctrl+C every flip uses, the
        /// clipboard is handed straight back, and the history is suppressed for the duration - the
        /// copy is scaffolding for a note the user asked for, not a copy the user made (spec §8.9).
        /// </summary>
        private void CaptureSelectionIntoQuickNote()
        {
            if (!_config.EnableQuickNotes) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                _tray.ShowBalloonTip(2000, "CyrFlip",
                    T("Другая операция с буфером ещё не закончилась. Повторите через секунду."), ToolTipIcon.Info);
                return;
            }
            StartClipboardWorker(() =>
            {
                _clipboardHistory.SuppressBegin();
                ClipboardHandler.CaptureResult captured = ClipboardHandler.CaptureResult.NoSelection;
                string text = "";
                // The note's own cap, in characters: a UTF-8 byte per character at the least (S0032 FP2-5).
                try { captured = _clipboard.TakeSelection(out text, out _, QuickNote.MaxBytes); }
                catch { /* never let a clipboard op take the app down */ }
                finally
                {
                    _clipboardHistory.SuppressEnd();
                    Interlocked.Exchange(ref _busy, 0);
                }

                ClipboardHandler.CaptureResult result = captured;
                string selection = text;
                _ui?.Post(_ =>
                {
                    if (result != ClipboardHandler.CaptureResult.Captured || selection.Length == 0)
                    {
                        ShowCaptureResult(result);
                        return;
                    }
                    // Still only a draft: the user confirms it by closing the window or pressing
                    // Ctrl+S, which is also when the note gets its creation date (spec §3.1).
                    EnsureQuickNotes()?.ShowWithText(selection);
                }, null);
            });
        }

        /// <summary>
        /// Anything on the quick-notes tab changed. The first enable is where the feature explains
        /// itself (spec §5.3) - once, so a user who switches it off and on again is not lectured.
        /// </summary>
        private void OnQuickNotesChanged()
        {
            if (_config.EnableQuickNotes && !_config.QuickNotesNoticeShown)
            {
                _config.QuickNotesNoticeShown = true;
                ConfirmDialog.Show(_config.UiLanguage,
                    T("Заметки хранятся только на этом компьютере, в зашифрованном DPAPI файле вашей учётной записи Windows. CyrFlip не отправляет их в сеть и ничем их не индексирует.")
                    + "\n\n"
                    + T("Это не хранилище секретов: не сохраняйте здесь пароли, боевые токены и приватные ключи."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // The chord going live (the module or its own switch turned on) is checked like assigning
            // it, since a config written before the check may share it (ticket S0004, KC-6).
            if (QuickNotesChordLive && !_quickNotesChordWasLive
                && Hotkey.TryParse(_config.QuickNotesHotkey, out Hotkey notesChord)
                && !ChordIsFree(notesChord, ChordKind.QuickNotes, askAboutWindows: false))
            {
                _config.EnableQuickNotesHotkey = false;
                _ui?.Post(_ => _settings.Reload(), null);
            }
            _quickNotesChordWasLive = QuickNotesChordLive;

            _config.Save();
            _quickNotesHotkey = Hotkey.TryParse(_config.QuickNotesHotkey, out Hotkey parsedNotes) ? parsedNotes : Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey);
            _hook.UpdateQuickNotesHotkey(_quickNotesHotkey);
            _hook.UpdateQuickNotesEnabled(QuickNotesChordLive);
            _quickNotesItem.Visible = _config.EnableQuickNotes;

            if (_quickNotesWindow != null && !_quickNotesWindow.IsDisposed)
            {
                _quickNotesWindow.ApplyWordWrap(_config.QuickNotesWordWrap);
                // Switched off: the open note is written, then the window goes and with it the
                // decrypted notes it was showing.
                if (!_config.EnableQuickNotes)
                {
                    _quickNotesWindow.CommitCurrent();
                    _quickNotesWindow.Dispose();
                    _quickNotesWindow = null;
                }
            }
            if (!_config.EnableQuickNotes && _quickNotes != null)
            {
                // The last edit is written here; the wait for a running compaction and the final one
                // go to the pool (S0030 HT-6) - this is a settings click on the hooks' thread.
                _quickNotesRetired = _quickNotes.RetireAsync();
                _quickNotes = null;
            }
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Wait for the notes replay with the message loop running, behind a small modal window -
        /// never <c>Task.Wait</c> on this thread, which the hooks share (S0030 HT-6).
        /// </summary>
        private void WaitForQuickNotesLoaded(QuickNotesService service)
        {
            if (service.IsLoaded) return;
            IWin32Window? owner = _settings.Visible ? _settings : null;
            BusyDialog.Wait(owner, _config.UiLanguage, service.LoadAsync());
        }

        /// <summary>Settings' "delete every quick note" - the service owns the files.</summary>
        private void ClearQuickNotes()
        {
            if (EnsureQuickNotes() == null || _quickNotes == null) return;
            // A set still loading would come back the moment it arrived, so the replay is finished
            // first. The service drops the open note through its Cleared event and the window
            // forgets the remembered one.
            QuickNotesService service = _quickNotes;
            WaitForQuickNotesLoaded(service);
            if (ReferenceEquals(service, _quickNotes)) service.DeleteAll();
        }

        /// <summary>
        /// Export or import the open exchange file (ticket S0023), owned by the window it was asked
        /// from. A failure is reported, never thrown into a click handler - the file is the user's
        /// and may be anything.
        /// </summary>
        private void RunExchange(IWin32Window owner, bool import)
        {
            try { _exchange.Run(owner, import); }
            catch (Exception ex)
            {
                QuickNotesLog.Log("exchange " + (import ? "import" : "export") + " failed: " + ex.GetType().Name);
                ConfirmDialog.Show(owner, _config.UiLanguage, FailureCause.Describe(ex, _config.UiLanguage), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Settings' "export everything as Markdown" - one file, in note order.</summary>
        private string ExportQuickNotes(string path, bool metadata)
        {
            QuickNotesService? service = _quickNotes;
            if (service == null && _config.EnableQuickNotes) { EnsureQuickNotes(); service = _quickNotes; }
            if (service == null) return "";
            WaitForQuickNotesLoaded(service);   // "everything" must not be whatever the replay has reached
            var notes = new List<QuickNote>(service.Notes);
            // Formatted and written on the pool, behind the same modal window (S0030 HT-6).
            IWin32Window? owner = _settings.Visible ? _settings : null;
            BusyDialog.Run(owner, _config.UiLanguage, () =>
            {
                var text = new StringBuilder();
                bool first = true;
                foreach (QuickNote note in notes)
                {
                    if (!first) text.Append("\n\n---\n\n");
                    text.Append(note.ToMarkdown(metadata));
                    first = false;
                }
                System.IO.File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            });
            return path;
        }

        // ---- Translator (local Ollama) ----

        /// <summary>
        /// A translation chord fired. Runs on the hook thread, so it only reads the row and hands the
        /// work on: phase A (the clipboard) goes to a background thread, phase B (the model) to the
        /// UI thread's async machinery.
        /// </summary>
        private void OnTranslateHotkeyPressed(string id)
        {
            if (!_config.EnableTranslate) { TranslateLog.Log("chord ignored: the translator is off"); return; }
            TranslationProfile? profile = _config.TranslateProfiles.Find(p => p.Id == id && p.Enabled);
            if (profile == null) { TranslateLog.Log("chord ignored: no enabled row with id " + id); return; }
            string code = TranslationLanguages.Resolve(profile.TargetLang, _config.UiLanguage, _currentLayout);
            TranslateLog.Log("chord: row " + profile.TargetLang + " -> into " + code + " (auto-detect source)");
            CaptureSelectionForTranslation(code);
        }

        /// <summary>
        /// Phase A: take the selection with the same synthesized Ctrl+C the flips use, hand the
        /// clipboard straight back, and release <see cref="_busy"/>. The model call must not hold the
        /// clipboard lock: a translation takes seconds, and a flip pressed meanwhile has to work.
        /// </summary>
        private void CaptureSelectionForTranslation(string code)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                // Used to return in silence, which from the outside is indistinguishable from a dead
                // hotkey - the single worst way for this feature to fail (spec §12.2).
                TranslateLog.Log("capture skipped: another clipboard operation is still running");
                _tray.ShowBalloonTip(2000, "CyrFlip",
                    T("Другая операция с буфером ещё не закончилась. Повторите через секунду."), ToolTipIcon.Info);
                return;
            }
            StartClipboardWorker(() =>
            {
                _clipboardHistory.SuppressBegin();
                ClipboardHandler.CaptureResult captured = ClipboardHandler.CaptureResult.NoSelection;
                string text = "";
                IntPtr target = IntPtr.Zero;
                try { captured = _clipboard.TakeSelection(out text, out target); }
                catch { /* never let a clipboard op take the app down */ }
                finally
                {
                    _clipboardHistory.SuppressEnd();
                    Interlocked.Exchange(ref _busy, 0);
                }

                ClipboardHandler.CaptureResult result = captured;
                string selection = text;
                IntPtr window = target;
                TranslateLog.Log("capture: " + result + ", " + selection.Length + " chars");
                _ui?.Post(_ =>
                {
                    if (result == ClipboardHandler.CaptureResult.Cancelled)
                    {
                        // Also used to be silent. The user pressed a chord; they are owed an answer
                        // even when the answer is "you switched windows while I was copying".
                        _tray.ShowBalloonTip(2000, "CyrFlip",
                            T("Окно сменилось, пока я забирал выделение. Перевод отменён."), ToolTipIcon.Info);
                        return;
                    }
                    if (result != ClipboardHandler.CaptureResult.Captured || selection.Trim().Length == 0)
                    {
                        ShowCaptureResult(result);
                        return;
                    }
                    BeginTranslate(selection, window, code);
                }, null);
            });
        }

        /// <summary>The tray entry: translate what is already on the clipboard, with no Ctrl+C at all.</summary>
        private void TranslateClipboard()
        {
            if (!_config.EnableTranslate) return;
            if (!Win32Clipboard.TryGetText(out string text) || text.Trim().Length == 0)
            {
                _tray.ShowBalloonTip(1500, "CyrFlip", T("В буфере обмена нет текста для перевода."), ToolTipIcon.Info);
                return;
            }
            BeginTranslate(text, IntPtr.Zero, TranslationLanguages.Resolve(FirstTranslationTarget(), _config.UiLanguage, _currentLayout));
        }

        /// <summary>
        /// Phase B: show the popup and stream the model's answer into it. A second request supersedes
        /// the first (the user changed their mind about the fragment), which is what the token
        /// identity check at the end is for.
        /// </summary>
        private async void BeginTranslate(string text, IntPtr target, string code)
        {
            _translateSource = text;
            _translateTarget = target;
            _translateCode = code;

            _translateCts?.Cancel();
            var cts = new CancellationTokenSource();
            _translateCts = cts;

            TranslationResultWindow window = EnsureTranslateWindow();
            window.ShowSourcePanel(_config.TranslateShowSource);
            window.ShowTranslating(code, text, T("Перевожу… (Esc — отмена)"), target);
            // The popup never takes focus, so its own Esc never fires; the hook watches for it while
            // the model is writing, and only then.
            _hook.UpdateCancelKeyWatch(true);
            _hook.UpdateFocusKeyWatch(false); // back on when the answer is in (AwaitingUser)

            // The sink marshals to the UI thread itself: chunks arrive on whatever thread finished
            // the HTTP read, and a stale request must not paint over a newer one.
            var sink = new TranslationSink(
                chunk => _ui?.Post(_ => { if (ReferenceEquals(_translateCts, cts)) window.AppendChunk(chunk); }, null),
                () => _ui?.Post(_ => { if (ReferenceEquals(_translateCts, cts)) window.ResetStream(); }, null));

            TranslateLog.Log("translating " + text.Length + " chars into " + code
                + " (model '" + _config.TranslateModel + "', keep_alive "
                + (_config.TranslateKeepAliveMinutes < 0 ? "forever" : _config.TranslateKeepAliveMinutes + "m")
                + ", load budget " + Math.Max(5, _config.TranslateTimeoutSeconds) + "s, then "
                + TranslationService.IdleTimeoutMs / 1000 + "s of silence at most)");

            TranslationResult result;
            try
            {
                result = await _translation.TranslateAsync(text, code, sink, cts.Token);
            }
            catch (Exception ex)
            {
                TranslateLog.Log("translate threw: " + ex.GetType().Name + " " + ex.Message);
                result = new TranslationResult { Status = TranslationStatus.Failed };
            }

            TranslateLog.Log("result: " + result.Status
                + (result.Partial ? " (partial - the model stopped answering)" : "")
                + (result.Model.Length > 0 ? ", model " + result.Model : "")
                + (result.Error.Length > 0 ? ", error " + result.Error : "")
                + ", " + result.Text.Length + " chars back");

            if (!ReferenceEquals(_translateCts, cts))
            {
                // Superseded by a newer request (retry, another language, another selection). The newer
                // one owns _translateCts; this one still owns the source it created and must free it -
                // "cancelled" is not "disposed", and every supersede used to abandon one.
                cts.Dispose();
                return;
            }
            _translateCts = null;
            cts.Dispose();
            _hook.UpdateCancelKeyWatch(false);
            FinishTranslate(result, window);
        }

        /// <summary>
        /// Escape, seen by the hook because the popup deliberately has no focus of its own. Only acts
        /// while a translation is actually running, so Escape is an ordinary key the rest of the time.
        /// </summary>
        private void CancelTranslationFromEscape()
        {
            if (_translateWindow == null || _translateWindow.IsDisposed) return;
            if (!_translateWindow.Visible || !_translateWindow.IsTranslating) return;
            _translateWindow.Dismiss();
        }

        private void FinishTranslate(TranslationResult result, TranslationResultWindow window)
        {
            if (result.Status == TranslationStatus.Cancelled) return; // the window is already gone

            // The user dismissed the popup while the model was still writing. The request is cancelled
            // on that path, but an answer can still be in flight - and delivering it now would drop a
            // translation into whatever they moved on to, seconds after they said "never mind".
            if (!window.Visible) return;

            if (result.Status != TranslationStatus.Ok)
            {
                window.ShowMessage(TranslationMessage(result.Status,
                        result.Status == TranslationStatus.Unreachable ? result.Server : result.Error),
                    offerSettings: result.Status != TranslationStatus.Timeout,
                    offerRetry: result.Status == TranslationStatus.Timeout
                        || result.Status == TranslationStatus.Failed
                        || result.Status == TranslationStatus.ServerError
                        || result.Status == TranslationStatus.Unreachable,
                    offerStart: result.Status == TranslationStatus.NotRunning
                        || result.Status == TranslationStatus.StartFailed);
                return;
            }

            // Nothing to paste over when the text came from the clipboard rather than a selection.
            bool canPaste = _translateTarget != IntPtr.Zero;
            window.ShowResult(result.Text, TranslationNote(result), canPaste);
            _config.IncrementTranslateCount();
            // An incomplete translation is never pasted over the selection by itself (S0010 TD-2):
            // that would replace the whole text with part of its translation. The buttons still work.
            DeliverTranslation(result.Text, window,
                copy: _config.TranslateCopyResult,
                paste: _config.TranslatePasteResult && canPaste && !result.Partial, manual: false);
        }

        /// <summary>
        /// Phase C - everything that owns the clipboard, on one <see cref="_busy"/>-guarded worker so it
        /// can never interleave with a flip. Writing the clipboard from the UI thread instead would let
        /// a flip's copy poll pick the translation up as if it were the user's selection.
        ///
        /// The copy is deliberately <b>not</b> suppressed: the point of the option is that the result
        /// lands in the clipboard history like any other copy, so phase A's suppression is lifted first
        /// in case the model answered inside its two-second window.
        /// </summary>
        /// <param name="manual">
        /// True for the popup's own Paste button. By then the user has clicked the popup, so it - not
        /// their editor - is the foreground window, and <see cref="ClipboardHandler.ReplaceSelection"/>
        /// would refuse. Hand the focus back to the window the selection came from first.
        /// </param>
        private void DeliverTranslation(string text, TranslationResultWindow window, bool copy, bool paste, bool manual)
        {
            if (!copy && !paste) return;
            if (text.Length == 0) return;

            IntPtr target = paste ? _translateTarget : IntPtr.Zero;
            // The language the translation is written in, for an ANSI-only target (S0009 FP-8).
            uint locale = TranslationLanguages.LocaleId(_translateCode);
            var thread = new Thread(() =>
            {
                // Wait briefly for a flip to finish rather than dropping the delivery: a flip owns the
                // clipboard for well under a second, and silently losing the user's translation - or
                // their "copy to clipboard" setting - would be the worse failure.
                bool owned = false;
                for (int i = 0; i < 20 && !owned; i++)
                {
                    owned = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
                    if (!owned) Thread.Sleep(50);
                }
                if (!owned)
                {
                    _ui?.Post(_ =>
                    {
                        if (window.IsDisposed || !window.Visible) return;
                        window.ShowResult(window.ResultText,
                            T("Не удалось прочитать или заменить выделение. У буфера обмена были другие планы."));
                    }, null);
                    return;
                }

                ClipboardHandler.FlipResult pasted = ClipboardHandler.FlipResult.Flipped;
                // With "copy" on, the translation is a copy the user asked for and the history keeps
                // it; without, the paste's backup/restore is scaffolding.
                if (!copy) _clipboardHistory.SuppressBegin();
                try
                {
                    if (copy) Win32Clipboard.TrySetText(text, locale, TransientMarks.None);

                    if (paste && target != IntPtr.Zero)
                    {
                        if (manual)
                        {
                            WindowInterop.SetForegroundWindow(target);
                            Thread.Sleep(80); // let the activation settle before the synthesized Ctrl+V
                        }
                        if (copy)
                        {
                            // The translation is meant to stay on the clipboard: nothing to restore, so
                            // nothing to race - a plain write and the ordinary settle time.
                            pasted = _clipboard.ReplaceSelection(text, target, out _, locale: locale, transient: false);
                        }
                        else
                        {
                            ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard();
                            // A clipboard that could not be backed up is not overwritten (S0009 FP-5).
                            if (backup.Unreadable) pasted = ClipboardHandler.FlipResult.Failed;
                            else
                            {
                                ClipboardHandler.PasteReceipt receipt = default;
                                try { pasted = _clipboard.ReplaceSelection(text, target, out receipt, locale: locale); }
                                finally { ClipboardHandler.RestoreClipboard(backup, receipt); }
                            }
                        }
                    }
                }
                catch { /* never let a clipboard op take the app down */ }
                finally
                {
                    if (!copy) _clipboardHistory.SuppressEnd();
                    Interlocked.Exchange(ref _busy, 0);
                }

                // Only a real paste attempt can report a focus change; with no target there was none.
                if (!paste || target == IntPtr.Zero || pasted == ClipboardHandler.FlipResult.Flipped) return;
                bool failed = pasted == ClipboardHandler.FlipResult.Failed;
                _ui?.Post(_ =>
                {
                    if (window.IsDisposed || !window.Visible) return;
                    window.ShowResult(window.ResultText, failed
                        ? T("Не удалось прочитать или заменить выделение. У буфера обмена были другие планы.")
                        : T("Фокус сменился — вставьте перевод вручную."));
                }, null);
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        private TranslationResultWindow EnsureTranslateWindow()
        {
            if (_translateWindow != null && !_translateWindow.IsDisposed) return _translateWindow;

            var window = new TranslationResultWindow(_config, _config.UiLanguage);
            window.CopyRequested += (_, _) => DeliverTranslation(window.ResultText, window, copy: true, paste: false, manual: true);
            window.PasteRequested += (_, _) => DeliverTranslation(window.ResultText, window, copy: false, paste: true, manual: true);
            window.RetryRequested += (_, _) => BeginTranslate(_translateSource, _translateTarget, _translateCode);
            window.SettingsRequested += (_, _) => ShowSettings();
            window.StartServerRequested += (_, _) => StartOllamaAndRetry();
            window.CancelRequested += (_, _) => _translateCts?.Cancel();
            // F6 is watched only while the finished popup is up: a bare F6 is the user's everywhere else.
            window.AwaitingUser += (_, _) => _hook.UpdateFocusKeyWatch(true);
            window.VisibleChanged += (_, _) => { if (!window.Visible) _hook.UpdateFocusKeyWatch(false); };
            window.TargetLanguageChosen += code => BeginTranslate(_translateSource, _translateTarget,
                TranslationLanguages.Resolve(code, _config.UiLanguage, _currentLayout));
            _translateWindow = window;
            return window;
        }

        /// <summary>
        /// The popup's "start Ollama" button (spec §7): start the server, then translate the same text
        /// again - the auto-start path inside the service does the waiting.
        /// </summary>
        private void StartOllamaAndRetry()
        {
            OllamaManager.StartServer();
            if (_translateSource.Length > 0)
                BeginTranslate(_translateSource, _translateTarget, _translateCode);
        }

        /// <summary>The line above the translation: the language, and anything the user should know.</summary>
        private string TranslationNote(TranslationResult result)
        {
            string note = TranslationLanguages.Label(_translateCode, _config.UiLanguage);
            if (result.Partial)
                note += " — " + T("модель перестала отвечать - перевод может быть неполным");
            if (result.Truncated)
                note += " — " + Localization.Format(T, "переведены первые {0} из {1} символов",
                    TranslationService.MaxChars, result.SourceLength);
            // Only worth saying when it isn't the model the settings promise.
            if (result.Model.Length > 0 && !TranslationService.ModelMatches(result.Model, (_config.TranslateModel ?? "").Trim()))
                note += " — " + result.Model;
            return note;
        }

        private string TranslationMessage(TranslationStatus status) => TranslationMessage(status, "");

        private string TranslationMessage(TranslationStatus status, string error)
        {
            switch (status)
            {
                case TranslationStatus.NoText: return T("Нет текста для перевода.");
                case TranslationStatus.NotInstalled: return T("Не найден Ollama — локальный переводчик. Его нужно установить один раз.");
                case TranslationStatus.NotRunning: return T("Ollama не запущен.");
                case TranslationStatus.StartFailed: return T("Не удалось запустить Ollama.");
                // A server on another machine: CyrFlip does not start it and says whose it is (S0010 TD-6).
                case TranslationStatus.Unreachable:
                    return Localization.Format(T, "Сервер {0} не отвечает. Проверьте адрес в настройках и что Ollama там запущен.", error);
                case TranslationStatus.NoModel: return T("Не установлена ни одна модель. Рекомендуем aya-expanse:8b (~4,7 ГБ) — загрузите её в настройках.");
                case TranslationStatus.Timeout: return T("Модель не ответила вовремя. Возможно, она слишком велика для этого компьютера.");
                // The server's own words, when it gave any: "model not found" is worth reading verbatim.
                case TranslationStatus.ServerError:
                    return error.Length > 0
                        ? T("Ollama ответил ошибкой:") + " " + error
                        : T("Ollama не принял запрос. Проверьте адрес сервера и модель в настройках.");
                default: return T("Модель не справилась с этим текстом.");
            }
        }

        /// <summary>The target of the first usable row - what the tray entry translates into.</summary>
        private string FirstTranslationTarget()
        {
            foreach (TranslationProfile profile in _config.TranslateProfiles)
                if (profile.Enabled) return profile.TargetLang;
            return TranslationLanguages.UiToken;
        }

        /// <summary>The hook gets an empty set while the translator is off, as the launcher does.</summary>
        private void BindTranslationHotkeys()
            => _hook.UpdateTranslationProfiles(_config.EnableTranslate
                ? (IEnumerable<TranslationProfile>)_config.TranslateProfiles
                : new TranslationProfile[0]);

        /// <summary>
        /// Anything on the translator tab changed. The first enable also owes the user a starter row
        /// (spec §3) - offered once, so an emptied table is never refilled behind their back.
        /// </summary>
        private void OnTranslationChanged()
        {
            // The starter row takes the default chord only when nobody - no CyrFlip action and no
            // Windows language hotkey - owns it; otherwise it is created without one.
            ChordRegistry? chords = null;
            AppConfig.SeedTranslateRow(_config, chord =>
            {
                chords ??= Chords();
                return chords.CyrFlipOwnerOf(chord, ChordKind.Translation) == null && chords.WindowsOwnerOf(chord) == null;
            });
            if (_config.EnableTranslate && !_translateWasOn) WarnDuplicateChords();
            _translateWasOn = _config.EnableTranslate;

            _config.Save();
            BindTranslationHotkeys();
            _translateClipboardItem.Visible = _config.EnableTranslate;
            if (_translateWindow != null && !_translateWindow.IsDisposed)
            {
                _translateWindow.ApplyOpacity();
                _translateWindow.ShowSourcePanel(_config.TranslateShowSource);
            }
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Every chord and its owner, read fresh (ticket S0004, KC-6). The settings window asks the
        /// very same registry, so the tray's setters and the tables cannot disagree about who owns
        /// a chord - switched on or off, master switch on or off.
        /// </summary>
        private ChordRegistry Chords() => ChordRegistry.Build(_config, _launcherStore.All, LanguageHotkeys.ReadAll());

        /// <summary>Refuse a chord another CyrFlip action owns; ask about one Windows owns.</summary>
        private bool ChordIsFree(Hotkey chord, ChordKind kind, bool askAboutWindows = true)
            => ChordGuard.IsFree(null, Chords(), chord, kind, null, _config.UiLanguage, WorldLayouts.LabelForKlid, askAboutWindows);

        /// <summary>A module or the master switch went on: say so if an old config shares a chord.</summary>
        private void WarnDuplicateChords()
            => ChordGuard.WarnDuplicates(null, Chords(), _config.UiLanguage, WorldLayouts.LabelForKlid);

        // ---- Tray menu handlers ----

        // The three fixed chords. TryParse, never Parse: a captured name the parser did not know
        // used to come back as the Ctrl+Shift+F12 default and be bound as that (ticket S0004, KC-2).

        private void OnSetCaseHotkey(object? sender, EventArgs e)
        {
            using var dlg = new HotkeyDialog(_caseHotkey.Display, HotkeyTitle("case"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.Case)) return;

            _caseHotkey = next;
            _config.CaseHotkey = next.Display;
            _config.Save();
            _hook.UpdateCaseHotkey(_caseHotkey);
            UpdateTrayTooltip();
            _settings.Reload();
        }

        private void OnSetHistoryHotkey(object? sender, EventArgs e)
        {
            using var dlg = new HotkeyDialog(_clipboardHistoryHotkey.Display, HotkeyTitle("history"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.History)) return;

            _clipboardHistoryHotkey = next;
            _config.ClipboardHistoryHotkey = next.Display;
            _config.Save();
            _hook.UpdateClipboardHistoryHotkey(next);
            _clipboardHistoryWindow.RefreshHeader();
            UpdateTrayTooltip();
            _settings.Reload();
        }

        /// <summary>
        /// The quick-notes chord, set from the settings tab. Checked against every other chord the
        /// registry knows - one chord doing two things is not a compromise anybody wants, it is a
        /// hotkey that appears to be broken half the time.
        /// </summary>
        private void OnSetQuickNotesHotkey()
        {
            using var dlg = new HotkeyDialog(_quickNotesHotkey.Display, T("Задать хоткей быстрых заметок"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.QuickNotes)) return;

            _quickNotesHotkey = next;
            _config.QuickNotesHotkey = next.Display;
            _config.Save();
            _hook.UpdateQuickNotesHotkey(next);
            UpdateTrayTooltip();
            _settings.Reload();
        }

        private void OnHistoryEnabledToggle(object? sender, EventArgs e)
        {
            _config.EnableClipboardHistory = _historyEnabledItem.Checked;
            _historyPauseItem.Enabled = _historyEnabledItem.Checked;
            _clipboardHistory.SetEnabled(_historyEnabledItem.Checked);
            if (_historyEnabledItem.Checked && !_clipboardHistoryWindow.Visible)
                _clipboardHistoryWindow.ToggleVisible();
            else if (!_historyEnabledItem.Checked)
                _clipboardHistoryWindow.Hide();
            _config.Save();
        }

        private void OnHistoryPauseToggle(object? sender, EventArgs e)
        {
            _config.PauseClipboardHistory = _historyPauseItem.Checked;
            _clipboardHistory.SetPaused(_historyPauseItem.Checked);
            _config.Save();
        }

        private void SetHistoryOpacity(int opacity)
        {
            _config.ClipboardHistoryOpacity = opacity;
            _clipboardHistoryWindow.ApplyOpacity();
            _config.Save();
        }

        private void SetHistoryStartup(bool value)
        {
            // The checkbox reflects and drives the live window state, which is remembered across
            // restarts via VisibleChanged. Toggling it shows/hides the window immediately; the
            // VisibleChanged handler persists ShowClipboardHistoryOnStartup and saves.
            if (value && !_clipboardHistoryWindow.Visible)
                _clipboardHistoryWindow.ToggleVisible();
            else if (!value && _clipboardHistoryWindow.Visible)
                _clipboardHistoryWindow.Hide();
        }

        private void SetUiLanguage(string language)
        {
            _config.UiLanguage = language;
            _config.Save();
            UpdateTrayTexts();
            _clipboardHistoryWindow.ApplyLanguage();
            if (_translateWindow != null && !_translateWindow.IsDisposed)
                _translateWindow.ApplyLanguage(language);
            // The launcher submenu title and the Jump List's Manage/Exit tasks carry localized text.
            RefreshLauncherSurfaces();
        }

        /// <summary>Localized text for a Russian source string - see <see cref="Localization"/>.</summary>
        private string T(string ru) => Localization.Translate(_config.UiLanguage, ru);

        private void UpdateTrayTexts()
        {
            _showHistoryItem.Text = T(_clipboardHistoryWindow.Visible ? "Скрыть историю" : "Показать историю");
            _historyEnabledItem.Text = T("История буфера");
            _historyPauseItem.Text = T("Приостановить захват истории");
            _cursorItem.Text = T("Курсор: индикатор раскладки");
            _caretItem.Text = T("Каретка: метка раскладки");
            _dotModeItem.Text = T("Каретка: стиль точки");
            _keepAwakeItem.Text = T("Не давать компьютеру засыпать");
            _keepScreenItem.Text = T("Не блокировать экран (как при видео)");
            _translateClipboardItem.Text = T("Перевести буфер обмена");
            _quickNotesItem.Text = T("Быстрые заметки");
            _screenshotItem.Text = T("Снимок области экрана");
            _screenshotItem.ShortcutKeyDisplayString = _config.EnableHotkeys && ScreenshotChordLive ? _screenshotHotkey.Display : null;
            if (_quickNotesWindow != null && !_quickNotesWindow.IsDisposed)
                _quickNotesWindow.ApplyLanguage(_config.UiLanguage);
            _settingsItem.Text = T("Настройки...");
            _exitItem.Text = T("Выход");
            if (_tray.ContextMenuStrip != null)
                _tray.ContextMenuStrip.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            _textMenu.RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            // Note: _autostartItem / _langSwitchItem / _capsAfterItem are state-holder items synced with
            // the settings checkboxes; they are not added to the tray menu, so their Text is never shown.
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Tray tooltip: the layout/CapsLock state, then every currently active hotkey on its own
        /// line - including each layout-conversion profile, since the table of pairs is open-ended
        /// and the tooltip is the only place the whole set is visible at a glance. Chords that are
        /// switched off are left out, so the tooltip only ever promises what actually works now.
        /// </summary>
        private void UpdateTrayTooltip()
        {
            var head = new StringBuilder("CyrFlip");
            if (_currentLayout.Length > 0) head.Append(" - ").Append(_currentLayout);
            if (_capsOn) head.Append(" (CAPS)");

            var lines = new List<string>();
            if (!_config.EnableHotkeys)
            {
                lines.Add(T("горячие клавиши выключены"));
            }
            else
            {
                if (_config.EnableCaseHotkey)
                    lines.Add(_caseHotkey.Display + " - " + T("регистр"));
                if (_config.EnableHistoryHotkey)
                    lines.Add(_clipboardHistoryHotkey.Display + " - " + T("менеджер буфера"));
                if (QuickNotesChordLive)
                    lines.Add(_quickNotesHotkey.Display + " - " + T("быстрые заметки"));
                if (ScreenshotChordLive)
                    lines.Add(_screenshotHotkey.Display + " - " + T("снимок области экрана"));
                if (TextMenuChordLive)
                    lines.Add(ParsedTextMenuHotkey().Display + " - " + T("контекстное меню текста"));
                foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
                    if (profile.Enabled && profile.IsUsable)
                        lines.Add(profile.Hotkey + " - " + WorldLayouts.CodeForKlid(profile.SourceKlid)
                            + " ⇄ " + WorldLayouts.CodeForKlid(profile.TargetKlid));
                if (_config.EnableScenarioLauncher)
                    foreach (LauncherScenario scenario in _launcherStore.All)
                        if (scenario.Hotkey.Length > 0)
                            lines.Add(scenario.Hotkey + " - " + scenario.Name);
                if (_config.EnableTranslate)
                    foreach (TranslationProfile profile in _config.TranslateProfiles)
                        if (profile.Enabled && profile.IsUsable)
                            lines.Add(profile.Hotkey + " - "
                                + TranslationLanguages.Label(profile.TargetLang, _config.UiLanguage));
            }

            // Keep-awake has no chord, so the tooltip is its only ambient
            // sign it's on. "Screen stays on" implies the system is awake too, so it wins when both.
            if (KeepAwake.KeepScreenOn)
                lines.Add(T("экран не гаснет"));
            else if (KeepAwake.KeepSystemAwake)
                lines.Add(T("не даёт засыпать"));

            SetTrayText(head.ToString(), lines);
        }

        private static readonly FieldInfo? TrayTextField =
            typeof(NotifyIcon).GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TrayAddedField =
            typeof(NotifyIcon).GetField("added", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? TrayUpdateIcon =
            typeof(NotifyIcon).GetMethod("UpdateIcon", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// <see cref="NotifyIcon.Text"/> throws above 63 characters, but the shell's
        /// NOTIFYICONDATA.szTip holds 127 - which is what a multi-line hotkey list needs. Write the
        /// private field and ask WinForms to re-send the icon data. If that private shape ever
        /// changes, fall back to the public setter with the first line only.
        ///
        /// The conversion table has no fixed size, so lines are added only while they still fit:
        /// dropping a whole line beats cutting one mid-chord, and a "+N" tail says how many are hidden.
        /// </summary>
        private void SetTrayText(string head, List<string> lines)
        {
            var text = new StringBuilder(head);
            int shown = 0;
            foreach (string line in lines)
            {
                // Keep room for a "+N…" tail if this isn't the last line that fits.
                int remaining = lines.Count - shown - 1;
                string tail = remaining > 0 ? "\r\n+" + remaining : "";
                if (text.Length + 2 + line.Length + tail.Length > 127) break;
                text.Append("\r\n").Append(line);
                shown++;
            }
            if (shown < lines.Count) text.Append("\r\n+").Append(lines.Count - shown);
            SetTrayText(text.ToString());
        }

        private void SetTrayText(string text)
        {
            if (text.Length > 127) text = text.Substring(0, 127);
            if (text.Length <= 63) { _tray.Text = text; return; }

            try
            {
                if (TrayTextField != null && TrayAddedField != null && TrayUpdateIcon != null)
                {
                    TrayTextField.SetValue(_tray, text);
                    if (TrayAddedField.GetValue(_tray) is bool added && added)
                        TrayUpdateIcon.Invoke(_tray, new object[] { _tray.Visible });
                    return;
                }
            }
            catch { /* fall through to the short form below */ }

            string first = text.Split('\n')[0].TrimEnd('\r');
            _tray.Text = first.Length > 63 ? first.Substring(0, 63) : first;
        }

        private void SetAutostartFromSettings(bool value)
        {
            if (Autostart.ManagedByWindows) { OnOpenStartupSettings(null, EventArgs.Empty); return; }
            try { Autostart.Set(value); _autostartItem.Checked = value; }
            catch (Exception ex) { ConfirmDialog.Show(_config.UiLanguage, T("Не удалось изменить автозапуск Windows:") + "\n" + FailureCause.Describe(ex, _config.UiLanguage), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>
        /// A single left click on the tray icon switches the input language to the next one in
        /// Windows' rotation - the tray icon shows the layout, so it is also where you change it.
        /// It acts on the window the user was last typing in: clicking the notification area moves
        /// the focus to the taskbar, and switching *its* layout would help nobody.
        /// </summary>
        private void SwitchLayoutFromTray()
        {
            if (!LayoutSwitcher.SwitchToNext(_indicator.LastActiveWindow))
                _tray.ShowBalloonTip(2000, "CyrFlip",
                    T("Переключать не на что: в Windows установлена только одна раскладка."), ToolTipIcon.Info);
        }

        private void ShowSettings()
        {
            _settings.Reload();
            if (!_settings.Visible) _settings.Show();
            _settings.WindowState = FormWindowState.Normal;
            // Not _settings.Activate(): opened from the text context menu this process holds no
            // foreground rights, and Windows refuses the activation silently - the window would come
            // up behind the user's editor, which reads as "the menu item did nothing".
            ForegroundActivator.Activate(_settings);
        }

        private void ShowHistorySearch()
        {
            if (_clipboardHistorySearchWindow == null || _clipboardHistorySearchWindow.IsDisposed)
            {
                _clipboardHistorySearchWindow = new ClipboardHistorySearchWindow(_clipboardHistory, _config.UiLanguage, RunExchange);
                _clipboardHistorySearchWindow.FormClosed += (_, _) => _clipboardHistorySearchWindow = null;
                _clipboardHistorySearchWindow.Show();
            }
            _clipboardHistorySearchWindow.WindowState = FormWindowState.Normal;
            ForegroundActivator.Activate(_clipboardHistorySearchWindow);
        }

        /// <summary>
        /// Localized title for the hotkey-capture dialog. Only the two fixed chords are set from here -
        /// the layout-conversion chords are captured by <see cref="LayoutConversionDialog"/>.
        /// </summary>
        private string HotkeyTitle(string which)
            => T(which == "history" ? "Задать хоткей истории буфера" : "Задать хоткей регистра");

        private void OnCursorToggle(object? sender, EventArgs e)
        {
            _config.EnableCursorChange = _cursorItem.Checked;
            if (_cursorItem.Checked)
            {
                if (_currentLayout.Length > 0) _layoutCursor.Apply(_currentLayout, _currentKlid, _capsOn);
            }
            else
            {
                _layoutCursor.Restore();
            }
            _config.Save();
        }

        private void OnCaretToggle(object? sender, EventArgs e)
        {
            _config.EnableCaretOverlay = _caretItem.Checked;
            _dotModeItem.Enabled = _caretItem.Checked;
            _caretOverlay.SetLayout(_caretItem.Checked && _currentLayout.Length > 0 ? _currentLayout : "", _currentKlid, _capsOn);
            _config.Save();
        }

        private void OnCapsAfterToggle(object? sender, EventArgs e)
        {
            _config.FlipCapsLockAfter = _capsAfterItem.Checked;
            _config.Save();
        }

        private void OnDotModeToggle(object? sender, EventArgs e)
        {
            _config.CaretDotMode = _dotModeItem.Checked;
            _caretOverlay.SetDotMode(_dotModeItem.Checked);
            _config.Save();
        }

        private void OnLangSwitchToggle(object? sender, EventArgs e)
        {
            _config.EnableLanguageSwitch = _langSwitchItem.Checked;
            _config.Save();
        }

        // The two keep-awake toggles: change the live idle policy, then remember it. Persisting them
        // reverses the original spec §5 - a switch that forgot itself on every launch read as broken.
        // UpdateTrayTooltip surfaces "keeping awake" while either is on, which is its only other sign.
        private void OnKeepAwakeToggle(object? sender, EventArgs e)
        {
            KeepAwake.SetSystemAwake(_keepAwakeItem.Checked);
            _config.KeepSystemAwake = _keepAwakeItem.Checked;
            _config.Save();
            UpdateTrayTooltip();
        }

        private void OnKeepScreenToggle(object? sender, EventArgs e)
        {
            KeepAwake.SetScreenOn(_keepScreenItem.Checked);
            _config.KeepScreenOn = _keepScreenItem.Checked;
            _config.Save();
            UpdateTrayTooltip();
        }

        private void SetHotkeysEnabled(bool value)
        {
            if (value && !_config.EnableHotkeys) WarnDuplicateChords();
            _config.EnableHotkeys = value;
            _hook.UpdateEnabled(value);
            _config.Save();
            UpdateTrayTooltip();
        }

        private void SetDeferToRemoteDesktop(bool value)
        {
            _config.DeferToRemoteDesktop = value;
            _hook.UpdateDeferInRemoteClient(value);
            _config.Save();
        }

        // Switching a fixed chord back on is checked like assigning it (ticket S0004, KC-6); refused,
        // the config stays off and the settings window's Reload puts the tick back.
        private void SetCaseHotkeyEnabled(bool value)
        {
            if (value && !ChordIsFree(_caseHotkey, ChordKind.Case, askAboutWindows: false)) return;
            _config.EnableCaseHotkey = value;
            _hook.UpdateCaseEnabled(value);
            _config.Save();
            UpdateTrayTooltip();
        }

        private void SetHistoryHotkeyEnabled(bool value)
        {
            if (value && !ChordIsFree(_clipboardHistoryHotkey, ChordKind.History, askAboutWindows: false)) return;
            _config.EnableHistoryHotkey = value;
            _hook.UpdateHistoryEnabled(value);
            _config.Save();
            UpdateTrayTooltip();
        }

        private void OnDiagnoseCaret(object? sender, EventArgs e)
        {
            if (CaretDiagnostics.IsRunning)
                return;

            _tray.ShowBalloonTip(3000, "CyrFlip",
                T("Идёт запись ~7 секунд. Щёлкните в непокорное поле ввода и наберите что-нибудь или подвигайте каретку — покажите, где она прячется."),
                ToolTipIcon.Info);

            bool started = CaretDiagnostics.Run(
                onDone: path => _ui?.Post(_ =>
                {
                    _tray.ShowBalloonTip(5000, "CyrFlip", T("Диагностика каретки сохранена — укрытие каретки раскрыто. Открываю:") + "\n" + path, ToolTipIcon.Info);
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch { /* best effort - the balloon still shows the path */ }
                }, null),
                onError: msg => _ui?.Post(_ =>
                    _tray.ShowBalloonTip(5000, "CyrFlip", T("Диагностика каретки не удалась (каретка выиграла этот раунд):") + " " + msg, ToolTipIcon.Warning), null));

            if (!started)
                _tray.ShowBalloonTip(2000, "CyrFlip", T("Спокойно — одна диагностика уже идёт."), ToolTipIcon.Info);
        }

        private void OnToggleAutostart(object? sender, EventArgs e)
        {
            try
            {
                Autostart.Set(_autostartItem.Checked);
            }
            catch (Exception ex)
            {
                _autostartItem.Checked = Autostart.IsEnabled; // revert the checkmark on failure
                ConfirmDialog.Show(_config.UiLanguage, T("Не удалось изменить автозапуск Windows:") + "\n" + FailureCause.Describe(ex, _config.UiLanguage),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenStartupSettings(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch { /* best effort - never let the tray menu throw */ }
        }

        // ---- Scenario launcher (absorbed OneClickRunner) ----

        /// <summary>
        /// The settings checkbox handler. The very first enable runs the one-time flow (spec §5.3):
        /// offer the OneClickRunner migration when its data exists and our store is empty, otherwise
        /// seed the localized Calculator sample - then never again, however the list changes later.
        /// </summary>
        private void SetLauncherEnabled(bool value)
        {
            if (value && !_config.EnableScenarioLauncher) WarnDuplicateChords();
            _config.EnableScenarioLauncher = value;
            if (value && !_config.LauncherFirstEnableDone)
            {
                _config.LauncherFirstEnableDone = true;
                if (_launcherStore.Count == 0 && LauncherMigration.SourceExists())
                {
                    if (ConfirmDialog.Show(_config.UiLanguage,
                            Localization.Format(T, "Найдены сценарии OneClickRunner ({0} шт.). Перенести их в CyrFlip? Исходные файлы останутся без изменений.",
                                LauncherMigration.SourceCount()),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        ShowMigrationSummary(LauncherMigration.Import(_launcherStore, chords: Chords));
                }
                if (_launcherStore.Count == 0)
                    _launcherStore.SeedSample(T("Калькулятор"));
            }
            _config.Save();
            RefreshLauncherSurfaces();
            // No _settings.Reload() here: this runs from the settings checkbox, whose own Changed
            // helper reloads right after - a second pass would just rebuild the table twice.
        }

        /// <summary>
        /// The tray's "Import from OneClickRunner…". Deferred to the next message-loop turn on
        /// purpose: the import rebuilds the very submenu this item belongs to, and disposing the
        /// item whose Click handler is still on the stack is not something to rely on.
        /// </summary>
        private void RequestOneClickRunnerImport()
            => _ui?.Post(_ =>
            {
                ShowMigrationSummary(LauncherMigration.Import(_launcherStore, chords: Chords));
                RefreshLauncherSurfaces();
                _settings.Reload();
            }, null);

        private void ShowMigrationSummary(LauncherMigration.Result result)
        {
            string summary = Localization.Format(T, "Перенесено сценариев: {0}.", result.Imported);
            if (result.AlreadyPresent > 0)
                summary += "\n" + Localization.Format(T, "Уже перенесены ранее: {0}.", result.AlreadyPresent);
            if (result.Skipped.Count > 0)
                summary += "\n" + Localization.Format(T, "Пропущено повреждённых файлов: {0}.", result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + Localization.Format(T, "Из-за совпадения идентификаторов назначены новые: {0}.", result.NewIds);
            if (result.ChordsDropped > 0)
                summary += "\n" + Localization.Format(T, "Комбинации уже заняты, поэтому не перенесены: {0}.", result.ChordsDropped);
            ConfirmDialog.Show(_config.UiLanguage, summary, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Rebuild everything that reflects the scenario list: the tray submenu, the taskbar button
        /// and its Jump List, and the hook's chord snapshot. Disabled state clears all of them
        /// (spec §4/§8) - the tray item disappears, the taskbar button is closed, the Jump List is
        /// deleted and the hook gets an empty snapshot.
        /// </summary>
        private void RefreshLauncherSurfaces()
        {
            bool enabled = _config.EnableScenarioLauncher;
            List<LauncherScenario> scenarios = enabled ? _launcherStore.All : new List<LauncherScenario>();

            // The pipe passes on launcher commands only while the launcher is on; /exit always.
            _launcherIpc.SetLauncherCommandsEnabled(enabled);

            // Tray submenu, in stored order; separator; Manage; optional re-import.
            _launcherMenu.Visible = enabled;
            if (enabled)
                LauncherTrayMenu.Rebuild(_launcherMenu, scenarios, RunLauncherScenario, ShowSettings,
                    LauncherMigration.SourceExists() ? (Action)RequestOneClickRunnerImport : null, T);
            else
                LauncherTrayMenu.Clear(_launcherMenu);

            // Taskbar button: the surface both clicks live on - right-click is the Jump List Windows
            // draws below, left-click drops down the same list as a menu. A tray-only app has no
            // taskbar button at all, so the launcher brings its own while it is on.
            if (enabled)
            {
                if (_launcherTaskbar == null || _launcherTaskbar.IsDisposed)
                {
                    _launcherTaskbar = new LauncherTaskbarWindow(FillLauncherMenu);
                    _launcherTaskbar.Show();
                }
                _launcherTaskbar.ApplyLanguage(_config.UiLanguage);
            }
            else if (_launcherTaskbar != null)
            {
                _launcherTaskbar.Dispose();
                _launcherTaskbar = null;
            }

            // Jump List: the same ordered list, or nothing at all while disabled. Its icons are looked
            // up on the pool - a scenario on a share or a PATH entry on an unreachable server used to
            // stall this thread, the hooks' thread, at startup and on every settings change (S0030 HT-1).
            int jumpList = ++_jumpListGeneration;
            if (enabled)
                ApplyJumpListInBackground(scenarios, jumpList);
            else
                LauncherJumpList.Clear();

            // Hook chords: only enabled-launcher scenarios that actually carry one.
            var bindings = new List<(Guid, string)>();
            if (enabled)
                foreach (LauncherScenario scenario in scenarios)
                    if (scenario.Hotkey.Length > 0)
                        bindings.Add((scenario.Id, scenario.Hotkey));
            _hook.UpdateLauncherHotkeys(bindings);
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Build the Jump List's tasks on the pool and apply them back here, on the UI (STA) thread
        /// the shell's COM objects want - unless a newer refresh, or switching the launcher off, has
        /// overtaken this one in the meantime (<paramref name="generation"/>).
        /// </summary>
        private void ApplyJumpListInBackground(List<LauncherScenario> scenarios, int generation)
        {
            List<LauncherScenario> snapshot = scenarios.ConvertAll(s => s.Clone());
            string exePath = Application.ExecutablePath;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<LauncherJumpList.TaskSpec> tasks;
                try { tasks = LauncherJumpList.BuildTasks(snapshot, exePath, T); }
                catch (Exception ex)
                {
                    LauncherLog.Log("JumpList: build failed: " + ex.GetType().Name);
                    return;
                }
                _ui?.Post(_ =>
                {
                    if (_disposed || generation != _jumpListGeneration || !_config.EnableScenarioLauncher) return;
                    LauncherJumpList.Apply(tasks);
                }, null);
            });
        }

        /// <summary>
        /// The scenario menu behind a left-click on the taskbar button - built on demand from the
        /// current store, so it can never show a scenario the user has just deleted, and built by the
        /// same code as the tray submenu so the two can never drift apart.
        /// </summary>
        private void FillLauncherMenu(ContextMenuStrip menu)
            => LauncherTrayMenu.Rebuild(menu, _launcherStore.All, RunLauncherScenario, ShowSettings,
                LauncherMigration.SourceExists() ? (Action)RequestOneClickRunnerImport : null, T);

        /// <summary>A launcher command delivered over the pipe (already marshalled to the UI thread).</summary>
        private void OnLauncherIpcCommand(string command)
        {
            if (command == LauncherIpc.SettingsCommand) { ShowSettings(); return; }
            if (command == LauncherIpc.ExitCommand) { ExitThread(); return; }

            Guid? id = LauncherIpc.RunId(command);
            if (id == null) return;
            // Commands from stale Jump List tasks are ignored while the launcher is off (spec §4),
            // and the store is re-read so a task fired right after an edit runs the current version.
            if (!_config.EnableScenarioLauncher) return;
            _launcherStore.Reload();
            LauncherScenario? scenario = _launcherStore.Find(id.Value);
            if (scenario != null)
                RunLauncherScenario(scenario);
        }

        private void OnLauncherHotkeyPressed(Guid id)
        {
            // Raised on the hook thread - marshal to the UI thread (the yt-dlp prompt is modal UI)
            // and re-check the switches there; the snapshot may outlive a just-flipped setting.
            _ui?.Post(_ =>
            {
                if (!_config.EnableScenarioLauncher) return;
                LauncherScenario? scenario = _launcherStore.Find(id);
                if (scenario != null)
                    RunLauncherScenario(scenario);
            }, null);
        }

        /// <summary>
        /// Launch from the tray, Jump List or hotkey: same single execution path as the settings
        /// editor, but failures surface as a tray balloon so no dialog steals the user's focus
        /// (the editor shows its own modal error - spec §7). The launch itself runs on the pool
        /// (S0030 HT-1); only the yt-dlp prompt and the failure balloon are on this thread.
        /// </summary>
        private void RunLauncherScenario(LauncherScenario scenario)
        {
            string name = scenario.Name;
            LauncherExecution.LaunchAsync(scenario, T, () =>
            {
                using var prompt = new YtDlpLinkDialog(_config.UiLanguage);
                return prompt.ShowDialog() == DialogResult.OK ? prompt.Link : null;
            }).ContinueWith(task =>
            {
                LauncherLaunchResult result = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                    ? task.Result
                    : LauncherLaunchResult.Fail(task.Exception != null ? FailureCause.Describe(task.Exception.GetBaseException(), _config.UiLanguage) : "");
                if (!result.Success && !result.Cancelled)
                    _ui?.Post(_ => ShowWarningBalloon(4000,
                        Localization.Format(T, "Не удалось запустить «{0}»: {1}", name, result.ErrorMessage)), null);
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>A warning balloon posted from a worker - the tray may be gone by the time it runs.</summary>
        private void ShowWarningBalloon(int timeout, string text)
        {
            if (_disposed) return;
            try { _tray.ShowBalloonTip(timeout, "CyrFlip", text, ToolTipIcon.Warning); }
            catch { /* the tray icon is being torn down */ }
        }

        private static Icon TryGetAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        /// <summary>
        /// Writes the open note and whatever the debounce is still holding. Cheap, so it runs as soon
        /// as Windows asks whether the session may end, and again once it does.
        /// </summary>
        private void FlushQuickNotes()
        {
            try
            {
                if (_quickNotesWindow != null && !_quickNotesWindow.IsDisposed)
                    _quickNotesWindow.CommitCurrent();
                _quickNotes?.Flush();
            }
            catch { /* the session is ending - nothing may throw out of here */ }
        }

        /// <summary>
        /// <c>WM_ENDSESSION(TRUE)</c>: the process is about to be terminated, and this is the only
        /// cleanup the sign-out path gets. Synchronous, in order, each step on its own - one that fails
        /// must not cost the next - and inside <see cref="SessionEndSequence.Budget"/> (Windows allows
        /// about 5 s before it shows "this app is preventing shutdown").
        /// </summary>
        private void OnSessionEnding()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Data first, the optional compaction last and only when none is running (S0035 QN2-6).
            new SessionEndSequence
            {
                // Absence of layout.txt = CyrFlip not running (LAYOUT-SIGNAL rule 6). Without this, the
                // next sign-in without autostart left the VS Code extension drawing a stale marker.
                Retract = LayoutPublisher.Retract,
                FlushNotes = FlushQuickNotes,
                DrainHistory = wait => _clipboardHistory.WaitForPendingWrites(wait),
                // A no-op unless CyrFlip actually replaced the cursor (LayoutCursor.ForceRestore).
                RestoreCursor = LayoutCursor.ForceRestore,
                CompactionRunning = () => _quickNotes?.CompactionTask is System.Threading.Tasks.Task running && !running.IsCompleted,
                Compact = () => _quickNotes?.CompactIfNeeded(),
                FlushLogs = wait => DiagnosticLog.Flush(wait),
            }.Run(() => clock.Elapsed);
        }

        protected override void Dispose(bool disposing)
        {
            // Runs twice on a normal exit: Program's `using`, and WinForms disposing the context when
            // the message loop ends. The second pass used to Cancel() a source the first had already
            // disposed, and the ObjectDisposedException turned a plain "Exit" into an error box.
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                _sessionEnd.Dispose();
                _launcherIpc.Dispose(); // stop the pipe listener before the UI it posts to goes away
                _launcherTaskbar?.Dispose();
                // Stop an in-flight translation before the window it streams into is torn down.
                _translateCts?.Cancel();
                _translateCts?.Dispose();
                _translateCts = null;
                _translateWindow?.Dispose();
                _trayClickTimer.Stop();
                _trayClickTimer.Dispose();
                _screenshotDelay.Stop();
                _screenshotDelay.Dispose();
                _regionOverlay?.Cancel();
                _regionOverlay?.Dispose();
                _hookWatchdog.Stop();
                _hookWatchdog.Dispose();
                _mouseHook.Dispose();
                _probeWorker?.Dispose();
                _textMenu.Dispose();
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
                _hook.Dispose();
                // The clipboard owner goes after the hook, so no new flip can start: a paste it still
                // promises is rendered for good as its window is destroyed (WM_RENDERALLFORMATS).
                ClipboardOwner.ShutdownShared();
                _indicator.Dispose();
                KeepAwake.Reset(); // restore Windows' normal sleep/screen idle timeouts
                _layoutCursor.Dispose(); // restores the default system cursor
                LayoutPublisher.Retract(); // absence of layout.txt = CyrFlip not running (LAYOUT-SIGNAL rule 6)
                _caretOverlay.Dispose();
                _clipboardHistoryWindow.Dispose();
                _clipboardHistorySearchWindow?.Dispose();
                _clipboardHistory.Dispose();
                // The open note is written first: Dispose is not a close, so the window's own
                // "save on the way out" never runs, and the last edit would be the debounce's to
                // lose. Only then is the service torn down (it flushes and compacts).
                if (_quickNotesWindow != null && !_quickNotesWindow.IsDisposed)
                    _quickNotesWindow.CommitCurrent();
                _quickNotesWindow?.Dispose();
                _quickNotes?.Dispose();
                // A retirement still compacting on the pool finishes before the process goes (S0030 HT-6).
                try { _quickNotesRetired?.Wait(2000); } catch { }
                _settings.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _trayIcon?.Dispose();
                ThemeManager.Shutdown();   // after every window: nothing left for it to hold
                DiagnosticLog.Flush(SessionEndSequence.LogDrain); // last: everything above may still have logged
            }
            base.Dispose(disposing);
        }
    }
}
