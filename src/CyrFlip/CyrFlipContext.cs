using System;
using System.Collections.Generic;
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
        private readonly SettingsForm _settings;
        // ---- Scenario launcher (absorbed OneClickRunner) ----
        private readonly LauncherScenarioStore _launcherStore;
        private readonly ToolStripMenuItem _launcherMenu;
        private readonly LauncherIpc _launcherIpc;
        private LauncherTaskbarWindow? _launcherTaskbar;

        // ---- Translator (local Ollama) ----
        private readonly TranslationService _translation;
        private readonly ToolStripMenuItem _translateClipboardItem;
        private TranslationResultWindow? _translateWindow;
        private CancellationTokenSource? _translateCts;
        private string _translateSource = "";
        private string _translateCode = "";
        private IntPtr _translateTarget = IntPtr.Zero;

        private readonly SynchronizationContext? _ui;
        private Icon? _trayIcon;
        private int _busy; // 0 = idle, 1 = a clipboard op (flip or case-flip) is in progress
        private string _currentLayout = "";
        private bool _capsOn;

        public CyrFlipContext(AppConfig config, bool openSettingsOnStart = false)
        {
            _config = config;
            _launcherStore = new LauncherScenarioStore();
            _launcherMenu = new ToolStripMenuItem();
            _caseHotkey = Hotkey.Parse(_config.CaseHotkey);
            _clipboardHistoryHotkey = Hotkey.Parse(_config.ClipboardHistoryHotkey);
            if (_clipboardHistoryHotkey.SameChord(_caseHotkey))
                _clipboardHistoryHotkey = new Hotkey(true, true, false, false, 0x79, "F10");
            _clipboardHistory = new ClipboardHistoryService(_config.EnableClipboardHistory, _config.PauseClipboardHistory);
            _clipboardHistoryWindow = new ClipboardHistoryWindow(_clipboardHistory, _config, ShowHistorySearch);
            _layoutCursor = new LayoutCursor(_config.CursorSize);
            _caretOverlay = new CaretOverlay(_config.CursorSize, _config.CaretDotMode);

            // SetSystemCursor is global and the keep-awake request changes the system idle policy -
            // guarantee both are restored even if the app is killed or throws.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); };
            AppDomain.CurrentDomain.UnhandledException += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); };
            Application.ApplicationExit += (_, _) => { LayoutCursor.ForceRestore(); KeepAwake.Reset(); };

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

            _capsAfterItem = new ToolStripMenuItem("Flip CapsLock after the flip")
            {
                CheckOnClick = true,
                Checked = _config.FlipCapsLockAfter,
            };
            _capsAfterItem.CheckedChanged += OnCapsAfterToggle;

            // ---- Keep-awake toggles (state lives only in memory, off on every launch) ----
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
            _settingsItem = new ToolStripMenuItem(null, null, (_, _) => ShowSettings());
            _exitItem = new ToolStripMenuItem(null, null, (_, _) => ExitThread());

            // ---- Menu ----
            var menu = new ContextMenuStrip();
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
            menu.Items.Add(_settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitItem);

            // Keep dynamic state in sync when the menu opens.
            menu.Opening += (_, _) =>
            {
                if (!Autostart.ManagedByWindows)
                    _autostartItem.Checked = Autostart.IsEnabled;
                // Dot style only makes sense when the caret overlay is on.
                _dotModeItem.Enabled = _caretItem.Checked;
                _historyPauseItem.Enabled = _historyEnabledItem.Checked;
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
                _launcherStore, SetLauncherEnabled);
            _settings.ConversionProfilesChanged += (_, _) => OnConversionProfilesChanged();
            _settings.LauncherScenariosChanged += (_, _) => RefreshLauncherSurfaces();
            _settings.TranslationChanged += (_, _) => OnTranslationChanged();
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
            _hook.CaseHotkeyPressed += OnCaseHotkeyPressed;
            _hook.ClipboardHistoryHotkeyPressed += (_, _) => _clipboardHistoryWindow.ToggleVisible();
            _hook.LayoutConversionHotkeyPressed += OnLayoutConversionHotkeyPressed;
            _hook.LauncherHotkeyPressed += OnLauncherHotkeyPressed;
            _hook.TranslateHotkeyPressed += OnTranslateHotkeyPressed;
            _hook.CancelKeyPressed += (_, _) => _ui?.Post(_ => CancelTranslationFromEscape(), null);
            _clipboardHistory.ItemTooLarge += (_, _) => _tray.ShowBalloonTip(2000, "CyrFlip", T("Фрагмент слишком велик для истории (>128 КБ)."), ToolTipIcon.Info);
            _hook.Install(_caseHotkey, _clipboardHistoryHotkey,
                _config.DeferToRemoteDesktop, _config.EnableHotkeys,
                _config.EnableCaseHotkey, _config.EnableHistoryHotkey);
            _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            BindTranslationHotkeys();
            _caretOverlay.Start();
            _indicator.Start();

            // History is an opt-in feature, but once opted in it should be available immediately
            // after every subsequent launch rather than waiting for a tray action.
            if (_config.EnableClipboardHistory && _config.ShowClipboardHistoryOnStartup)
                _clipboardHistoryWindow.ToggleVisible();

            // Captured after the overlay/indicator created control handles, so this is the
            // WinForms sync context - lets the background flip thread post tray feedback to the UI.
            _ui = SynchronizationContext.Current;

            // Launcher surfaces (tray submenu + Jump List + hook chords) and the command listener.
            // Started after _ui is captured: the IPC thread posts every command to the UI thread.
            RefreshLauncherSurfaces();
            _launcherIpc = new LauncherIpc(cmd => _ui?.Post(_ => OnLauncherIpcCommand(cmd), null));
            _launcherIpc.Start();

            if (openSettingsOnStart)
                _ui?.Post(_ => ShowSettings(), null);
        }

        private void OnLayoutChanged(string code, bool capsOn)
        {
            _currentLayout = code;
            _capsOn = capsOn;

            // Cursor indicator (global I-beam replacement). A 1px frame flags CapsLock.
            if (_config.EnableCursorChange)
                _layoutCursor.Apply(code, capsOn);
            else
                _layoutCursor.Restore();

            // Caret overlay (text label or dot near the blinking caret).
            _caretOverlay.SetLayout(_config.EnableCaretOverlay ? code : "", capsOn);
            LayoutPublisher.Publish(code);

            UpdateTrayTooltip();
            Icon icon = CursorIndicator.RenderIcon(code, capsOn);
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
            RunClipboardOp(() => _clipboard.ConvertLayout(profile, _config.EnableLanguageSwitch),
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
        private void RunClipboardOp(Func<ClipboardHandler.FlipResult> op,
            ClipboardHandler.FlipResult countOn, Action onCounted)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;
            _clipboardHistory.SuppressFor(TimeSpan.FromSeconds(2));

            var thread = new Thread(() =>
            {
                try
                {
                    ClipboardHandler.FlipResult result = op();
                    if (result == countOn)
                        onCounted();
                    _ui?.Post(_ => ShowFlipResult(result), null);
                }
                catch { /* never let a clipboard op take the app down */ }
                finally { Interlocked.Exchange(ref _busy, 0); }
            })
            {
                IsBackground = true,
            };
            thread.Start();
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
            }
        }

        // ---- Translator (local Ollama) ----

        /// <summary>
        /// A translation chord fired. Runs on the hook thread, so it only reads the row and hands the
        /// work on: phase A (the clipboard) goes to a background thread, phase B (the model) to the
        /// UI thread's async machinery.
        /// </summary>
        private void OnTranslateHotkeyPressed(string id)
        {
            if (!_config.EnableTranslate) return;
            TranslationProfile? profile = _config.TranslateProfiles.Find(p => p.Id == id && p.Enabled);
            if (profile == null) return;
            string code = TranslationLanguages.Resolve(profile.TargetLang, _config.UiLanguage, _currentLayout);
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
                return;
            _clipboardHistory.SuppressFor(TimeSpan.FromSeconds(2));

            var thread = new Thread(() =>
            {
                ClipboardHandler.CaptureResult captured = ClipboardHandler.CaptureResult.NoSelection;
                string text = "";
                IntPtr target = IntPtr.Zero;
                try
                {
                    ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard();
                    try { captured = _clipboard.TryCaptureSelection(out text, out target, out _); }
                    finally { ClipboardHandler.RestoreClipboard(backup); }
                }
                catch { /* never let a clipboard op take the app down */ }
                finally { Interlocked.Exchange(ref _busy, 0); }

                ClipboardHandler.CaptureResult result = captured;
                string selection = text;
                IntPtr window = target;
                _ui?.Post(_ =>
                {
                    if (result == ClipboardHandler.CaptureResult.Cancelled) return;
                    if (result != ClipboardHandler.CaptureResult.Captured || selection.Trim().Length == 0)
                    {
                        ShowFlipResult(ClipboardHandler.FlipResult.NoSelection);
                        return;
                    }
                    BeginTranslate(selection, window, code);
                }, null);
            })
            {
                IsBackground = true,
            };
            thread.Start();
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

            // The sink marshals to the UI thread itself: chunks arrive on whatever thread finished
            // the HTTP read, and a stale request must not paint over a newer one.
            var sink = new TranslationSink(
                chunk => _ui?.Post(_ => { if (ReferenceEquals(_translateCts, cts)) window.AppendChunk(chunk); }, null),
                () => _ui?.Post(_ => { if (ReferenceEquals(_translateCts, cts)) window.ResetStream(); }, null));

            TranslationResult result;
            try
            {
                result = await _translation.TranslateAsync(text, code, sink, cts.Token);
            }
            catch
            {
                result = new TranslationResult { Status = TranslationStatus.Failed };
            }

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
                window.ShowMessage(TranslationMessage(result.Status, result.Error),
                    offerSettings: result.Status != TranslationStatus.Timeout,
                    offerRetry: result.Status == TranslationStatus.Timeout
                        || result.Status == TranslationStatus.Failed
                        || result.Status == TranslationStatus.ServerError,
                    offerStart: result.Status == TranslationStatus.NotRunning
                        || result.Status == TranslationStatus.StartFailed);
                return;
            }

            // Nothing to paste over when the text came from the clipboard rather than a selection.
            bool canPaste = _translateTarget != IntPtr.Zero;
            window.ShowResult(result.Text, TranslationNote(result), canPaste);
            _config.IncrementTranslateCount();
            DeliverTranslation(result.Text, window,
                copy: _config.TranslateCopyResult, paste: _config.TranslatePasteResult && canPaste, manual: false);
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
                try
                {
                    _clipboardHistory.SuppressFor(copy ? TimeSpan.Zero : TimeSpan.FromSeconds(2));
                    if (copy) Win32Clipboard.TrySetText(text);

                    if (paste && target != IntPtr.Zero)
                    {
                        if (manual)
                        {
                            WindowInterop.SetForegroundWindow(target);
                            Thread.Sleep(80); // let the activation settle before the synthesized Ctrl+V
                        }
                        ClipboardHandler.ClipboardBackup backup = ClipboardHandler.BackupClipboard();
                        try { pasted = _clipboard.ReplaceSelection(text, target); }
                        // When the user asked for the translation to stay in the clipboard, leave it there.
                        finally { if (!copy) ClipboardHandler.RestoreClipboard(backup); }
                    }
                }
                catch { /* never let a clipboard op take the app down */ }
                finally { Interlocked.Exchange(ref _busy, 0); }

                // Only a real paste attempt can report a focus change; with no target there was none.
                if (!paste || target == IntPtr.Zero || pasted == ClipboardHandler.FlipResult.Flipped) return;
                _ui?.Post(_ =>
                {
                    if (window.IsDisposed || !window.Visible) return;
                    window.ShowResult(window.ResultText, T("Фокус сменился — вставьте перевод вручную."));
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
            if (result.Truncated)
                note += " — " + string.Format(T("переведены первые {0} из {1} символов"),
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
                case TranslationStatus.NoModel: return T("Не установлена ни одна модель. Рекомендуем qwen2.5:3b (~2 ГБ) — загрузите её в настройках.");
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
            AppConfig.SeedTranslateRow(_config, chord => ClashingChordOwner(chord) == null);

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
        /// Which CyrFlip action already owns this chord, or null. Used for the one decision the
        /// context makes on its own (is the default translate chord free?); the settings window has
        /// its own richer check for the tables (<c>SettingsForm.ClashingCyrFlipAction</c>).
        /// </summary>
        private string? ClashingChordOwner(Hotkey chord)
        {
            if (_config.EnableCaseHotkey && chord.SameChord(_caseHotkey)) return "case";
            if (_config.EnableHistoryHotkey && chord.SameChord(_clipboardHistoryHotkey)) return "history";
            if (ConversionUsing(chord) != null) return "conversion";
            if (LauncherUsing(chord) != null) return "launcher";
            if (TranslationUsing(chord) != null) return "translation";
            return null;
        }

        /// <summary>The translation row already bound to this chord, or null - the 5th party of the check.</summary>
        private TranslationProfile? TranslationUsing(Hotkey chord)
        {
            if (!_config.EnableTranslate) return null;
            foreach (TranslationProfile profile in _config.TranslateProfiles)
                if (profile.Enabled && profile.IsUsable
                    && Hotkey.TryParse(profile.Hotkey, out Hotkey bound) && chord.SameChord(bound))
                    return profile;
            return null;
        }

        // ---- Tray menu handlers ----

        private void OnSetCaseHotkey(object? sender, EventArgs e)
        {
            using var dlg = new HotkeyDialog(_caseHotkey.Display, HotkeyTitle("case"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null)
                return;

            var newHotkey = Hotkey.Parse(dlg.CapturedHotkey);
            if (newHotkey.Vk == 0)
                return; // parse produced no trigger key - shouldn't happen if dialog is correct

            if (newHotkey.SameChord(_clipboardHistoryHotkey))
            {
                WarnHotkeyClash("other");
                return;
            }

            if (ConversionUsing(newHotkey) != null)
            {
                WarnHotkeyClash("conversion");
                return;
            }

            if (LauncherUsing(newHotkey) != null)
            {
                WarnHotkeyClash("launcher");
                return;
            }

            if (TranslationUsing(newHotkey) != null)
            {
                WarnHotkeyClash("translation");
                return;
            }

            _caseHotkey = newHotkey;
            _config.CaseHotkey = dlg.CapturedHotkey;
            _config.Save();

            _hook.UpdateCaseHotkey(_caseHotkey);
            UpdateTrayTooltip();
            _settings.Reload();
        }

        private void OnSetHistoryHotkey(object? sender, EventArgs e)
        {
            using var dlg = new HotkeyDialog(_clipboardHistoryHotkey.Display, HotkeyTitle("history"), _config.UiLanguage);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null) return;
            Hotkey next = Hotkey.Parse(dlg.CapturedHotkey);
            if (next.Vk == 0 || next.SameChord(_caseHotkey)) { WarnHotkeyClash("other"); return; }
            if (ConversionUsing(next) != null) { WarnHotkeyClash("conversion"); return; }
            if (LauncherUsing(next) != null) { WarnHotkeyClash("launcher"); return; }
            if (TranslationUsing(next) != null) { WarnHotkeyClash("translation"); return; }
            _clipboardHistoryHotkey = next; _config.ClipboardHistoryHotkey = dlg.CapturedHotkey; _config.Save(); _hook.UpdateClipboardHistoryHotkey(next);
            _clipboardHistoryWindow.RefreshHeader();
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
            _settingsItem.Text = T("Настройки...");
            _exitItem.Text = T("Выход");
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

            // Keep-awake has no chord and no persisted state, so the tooltip is its only ambient
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
            catch (Exception ex) { MessageBox.Show(T("Не удалось изменить автозапуск Windows:") + "\n" + ex.Message, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            _settings.Activate();
        }

        private void ShowHistorySearch()
        {
            if (_clipboardHistorySearchWindow == null || _clipboardHistorySearchWindow.IsDisposed)
            {
                _clipboardHistorySearchWindow = new ClipboardHistorySearchWindow(_clipboardHistory, _config.UiLanguage);
                _clipboardHistorySearchWindow.FormClosed += (_, _) => _clipboardHistorySearchWindow = null;
                _clipboardHistorySearchWindow.Show();
            }
            _clipboardHistorySearchWindow.WindowState = FormWindowState.Normal;
            _clipboardHistorySearchWindow.Activate();
        }

        /// <summary>
        /// Localized title for the hotkey-capture dialog. Only the two fixed chords are set from here -
        /// the layout-conversion chords are captured by <see cref="LayoutConversionDialog"/>.
        /// </summary>
        private string HotkeyTitle(string which)
            => T(which == "history" ? "Задать хоткей истории буфера" : "Задать хоткей регистра");

        /// <summary>
        /// The conversion profile already bound to this chord, or null. The table of layout pairs is
        /// open-ended, so every built-in hotkey has to be checked against it - the settings window
        /// checks the other direction (see <c>SettingsForm.ClashingCyrFlipAction</c>).
        /// </summary>
        private LayoutConversionProfile? ConversionUsing(Hotkey chord)
        {
            foreach (LayoutConversionProfile profile in _config.LayoutConversionProfiles)
                if (profile.Enabled && profile.IsUsable
                    && Hotkey.TryParse(profile.Hotkey, out Hotkey bound) && chord.SameChord(bound))
                    return profile;
            return null;
        }

        private void WarnHotkeyClash(string which)
        {
            string other = which == "case" ? T("хоткеем регистра")
                : which == "conversion" ? T("конвертацией раскладок")
                : which == "launcher" ? T("сценарием быстрого запуска")
                : which == "translation" ? T("переводом текста")
                : T("другим хоткеем CyrFlip");
            MessageBox.Show(string.Format(T("Эта комбинация уже занята {0}. Их нельзя объединять — выберите другую."), other),
                "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnCursorToggle(object? sender, EventArgs e)
        {
            _config.EnableCursorChange = _cursorItem.Checked;
            if (_cursorItem.Checked)
            {
                if (_currentLayout.Length > 0) _layoutCursor.Apply(_currentLayout, _capsOn);
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
            _caretOverlay.SetLayout(_caretItem.Checked && _currentLayout.Length > 0 ? _currentLayout : "", _capsOn);
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

        // The two keep-awake toggles change only the live idle policy, never persisted state (§5):
        // no _config write, no Save. UpdateTrayTooltip surfaces "keeping awake" while either is on.
        private void OnKeepAwakeToggle(object? sender, EventArgs e)
        {
            KeepAwake.SetSystemAwake(_keepAwakeItem.Checked);
            UpdateTrayTooltip();
        }

        private void OnKeepScreenToggle(object? sender, EventArgs e)
        {
            KeepAwake.SetScreenOn(_keepScreenItem.Checked);
            UpdateTrayTooltip();
        }

        private void SetHotkeysEnabled(bool value)
        {
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

        private void SetCaseHotkeyEnabled(bool value)
        {
            _config.EnableCaseHotkey = value;
            _hook.UpdateCaseEnabled(value);
            _config.Save();
            UpdateTrayTooltip();
        }

        private void SetHistoryHotkeyEnabled(bool value)
        {
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
                MessageBox.Show(T("Не удалось изменить автозапуск Windows:") + "\n" + ex.Message,
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            _config.EnableScenarioLauncher = value;
            if (value && !_config.LauncherFirstEnableDone)
            {
                _config.LauncherFirstEnableDone = true;
                if (_launcherStore.Count == 0 && LauncherMigration.SourceExists())
                {
                    if (MessageBox.Show(
                            string.Format(T("Найдены сценарии OneClickRunner ({0} шт.). Перенести их в CyrFlip? Исходные файлы останутся без изменений."),
                                LauncherMigration.SourceCount()),
                            "CyrFlip", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        ShowMigrationSummary(LauncherMigration.Import(_launcherStore));
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
                ShowMigrationSummary(LauncherMigration.Import(_launcherStore));
                RefreshLauncherSurfaces();
                _settings.Reload();
            }, null);

        private void ShowMigrationSummary(LauncherMigration.Result result)
        {
            string summary = string.Format(T("Перенесено сценариев: {0}."), result.Imported);
            if (result.Skipped.Count > 0)
                summary += "\n" + string.Format(T("Пропущено повреждённых файлов: {0}."), result.Skipped.Count)
                    + "\n" + string.Join(", ", result.Skipped);
            if (result.NewIds > 0)
                summary += "\n" + string.Format(T("Из-за совпадения идентификаторов назначены новые: {0}."), result.NewIds);
            MessageBox.Show(summary, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                _launcherTaskbar.ApplyLanguage(T);
            }
            else if (_launcherTaskbar != null)
            {
                _launcherTaskbar.Dispose();
                _launcherTaskbar = null;
            }

            // Jump List: the same ordered list, or nothing at all while disabled.
            if (enabled)
                LauncherJumpList.Apply(LauncherJumpList.BuildTasks(scenarios, Application.ExecutablePath, T));
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
        /// (the editor shows its own modal error - spec §7).
        /// </summary>
        private void RunLauncherScenario(LauncherScenario scenario)
        {
            LauncherLaunchResult result = LauncherExecution.Launch(scenario, T, () =>
            {
                using var prompt = new YtDlpLinkDialog(_config.UiLanguage);
                return prompt.ShowDialog() == DialogResult.OK ? prompt.Link : null;
            });
            if (!result.Success && !result.Cancelled)
                _tray.ShowBalloonTip(4000, "CyrFlip",
                    string.Format(T("Не удалось запустить «{0}»: {1}"), scenario.Name, result.ErrorMessage),
                    ToolTipIcon.Warning);
        }

        /// <summary>The launcher scenario already bound to this chord, or null - the 4th party of the conflict check.</summary>
        private LauncherScenario? LauncherUsing(Hotkey chord)
        {
            if (!_config.EnableScenarioLauncher) return null;
            foreach (LauncherScenario scenario in _launcherStore.All)
                if (scenario.Hotkey.Length > 0 && Hotkey.TryParse(scenario.Hotkey, out Hotkey bound) && chord.SameChord(bound))
                    return scenario;
            return null;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _launcherIpc.Dispose(); // stop the pipe listener before the UI it posts to goes away
                _launcherTaskbar?.Dispose();
                // Stop an in-flight translation before the window it streams into is torn down.
                _translateCts?.Cancel();
                _translateCts?.Dispose();
                _translateWindow?.Dispose();
                _trayClickTimer.Stop();
                _trayClickTimer.Dispose();
                _hook.Dispose();
                _indicator.Dispose();
                KeepAwake.Reset(); // restore Windows' normal sleep/screen idle timeouts
                _layoutCursor.Dispose(); // restores the default system cursor
                _caretOverlay.Dispose();
                _clipboardHistoryWindow.Dispose();
                _clipboardHistorySearchWindow?.Dispose();
                _clipboardHistory.Dispose();
                _settings.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
