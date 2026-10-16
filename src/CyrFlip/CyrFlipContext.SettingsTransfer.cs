using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    internal sealed partial class CyrFlipContext
    {
        private bool _applyingSettings;
        /// <summary>The import turned the launcher switch (S0072 6.2): the apply runs the hand-flip path once.</summary>
        private bool _launcherFlipPending;
        private bool _backupOfferDeferred;
        private void TransferSettings(string operation)
        {
            string language = _config.UiLanguage;
            try
            {
                if (operation == "export")
                {
                    using var save = new SaveFileDialog { Filter = T("CyrFlip settings file (*.json)") + "|*.json",
                        FileName = SettingsFileWriter.DefaultFileName(DateTime.Now), DefaultExt = "json", AddExtension = true };
                    if (save.ShowDialog(_settings) != DialogResult.OK) return;
                    // The file is declared complete (S0072 D3/D4): the targets and the scenario documents
                    // are in it and nothing is asked. The privacy line lives in the confirmation and on
                    // the General page.
                    string text = SettingsFileWriter.Write(_config, DateTime.UtcNow,
                        typeof(AppConfig).Assembly.GetName().Version!.ToString(), ScenariosAsXml());
                    BusyDialog.Run(_settings, language, () => SettingsFileWriter.SaveText(save.FileName, text));
                    DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "export ok");
                    ConfirmDialog.Show(_settings, language,
                        T("Everything is in the file except note texts, clipboard history and Windows input state. It names your hosts, sites and programs - keep it private."),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string? path;
                if (operation == "restore") path = SettingsSnapshot.Newest(SettingsSnapshot.Folder);
                else
                {
                    using var open = new OpenFileDialog { Filter = T("CyrFlip settings file (*.json)") + "|*.json", CheckFileExists = true };
                    if (open.ShowDialog(_settings) != DialogResult.OK) return; path = open.FileName;
                }
                if (path == null) return;
                ImportDocument(path);
            }
            catch (Exception ex)
            {
                ShowTransferFailure(language, ex);
            }
        }

        private void ShowTransferFailure(string language, Exception ex)
        {
            string code = ex is SettingsFileException fault ? fault.Code : ex is AggregateException ? "rollback" : "io";
            DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "operation failed " + code);
            string message = code == "version" ? "Update CyrFlip to read this settings file."
                : code == "format" ? "This is not a CyrFlip settings file."
                : code == "limit" ? "Settings file exceeds an allowed limit."
                : code == "rolled-back" ? "Import failed. Previous settings restored."
                : code == "rollback" ? "Some settings could not be restored. A snapshot is saved in backups."
                : "Could not read or save settings. Nothing changed.";
            ConfirmDialog.Show(_settings, language, Localization.Translate(language, message), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// The import transaction over one file (S0055 I1..I6, S0072 6): read, preview, snapshot - settings and scenario store -
        /// commit, live refresh.
        /// </summary>
        private bool ImportDocument(string path)
        {
            string language = _config.UiLanguage;
            try
            {
                var document = BusyDialog.Run(_settings, language, () => SettingsFileReader.ReadFile(path));
                var scenarios = _launcherStore.All.ToArray(); var windows = LanguageHotkeys.ReadAll();
                var installed = new HashSet<string>(InputLayouts.ListInstalled().Select(r => r.Klid), StringComparer.OrdinalIgnoreCase);
                var local = MemoryConfigKey.Capture(_config).Load();
                SettingsImportPlan plan;
                using (var preview = new SettingsTransferDialog(language, document,
                    selected => SettingsImportPlan.Build(local, document, scenarios, windows, HotkeyRules.Check, installed, selected)))
                {
                    if (preview.ShowDialog(DialogOwner()) != DialogResult.OK) return false;
                    plan = preview.Plan;
                }
                // Freeze the plan's candidate; no second parser or validator at commit.
                using RegistryKey registry = Registry.CurrentUser.CreateSubKey(@"Software\CyrFlip")!;
                List<LauncherScenario>? scenarioSet = plan.Scenarios;
                _launcherFlipPending = plan.Changes.Any(c => c.Entry.Key == "EnableScenarioLauncher");
                string[] beforeScenarios = ScenariosAsXml();
                SettingsSnapshot.Commit(_config, plan, new RegistryConfigKey(registry), ApplyImportedSettings,
                    before => BusyDialog.Run(_settings, language, () => SettingsSnapshot.Create(SettingsSnapshot.Folder, before, beforeScenarios)),
                    () => BusyDialog.Run(_settings, language, () => SettingsSnapshot.CreateScenarios(SettingsSnapshot.Folder, _launcherStore)),
                    scenarioSet == null ? null : () => ReplaceScenarioSet(scenarioSet));
                try { BusyDialog.Run(_settings, language, () => SettingsSnapshot.Prune(SettingsSnapshot.Folder)); }
                catch { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "retention failed io"); }
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "import ok changes=" + plan.Changes.Count + " skipped=" + plan.Issues.Count);
                foreach (var issue in plan.Issues) DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), issue.Key + " " + issue.Reason);
                _backup?.NotifyChanged();
                _settings?.Reload();
                return true;
            }
            catch (Exception ex)
            {
                _launcherFlipPending = false;
                ShowTransferFailure(language, ex);
                return false;
            }
        }

        private string[] ScenariosAsXml() => _launcherStore.ReadDocuments();

        /// <summary>
        /// The startup restore offer (S0072 section 4). Asked only on a fresh profile - the registry key
        /// absent at load, or the question already open from the previous start - and only when a
        /// readable backup sits in Documents; a profile that declined is never asked again, and a
        /// profile that never opened the question is never asked at all. The writer stays silent until
        /// this settles, so the offered backup is never evicted from keep-last-3.
        /// </summary>
        private void OfferBackupRestore()
        {
            if (_backup == null || _disposed) return;
            // ShowDialog pumps posted callbacks while the tour is still open. Post order alone
            // cannot guarantee that the user answers the tour before seeing the restore question.
            if (_welcomePending || _welcome != null) { _backupOfferDeferred = true; return; }
            try
            {
                bool pending = _config.IsFirstRun || _config.BackupOfferOpen;
                bool declined = !string.IsNullOrEmpty(_config.BackupRestoreDeclined);
                string? backup = pending && !declined ? _backup.NewestReadable() : null;
                if (backup == null)
                {
                    if (_config.BackupOfferOpen) { _config.BackupOfferOpen = false; _config.Save(); } // the backup it asked about is gone
                    _backup.QuestionSettled = true;
                    return;
                }
                _config.BackupOfferOpen = true;
                _config.Save();
                string date = File.GetLastWriteTime(backup).ToString("g", System.Globalization.CultureInfo.CurrentCulture);
                DialogResult answer;
                using (var dialog = new BackupRestoreDialog(_config.UiLanguage, date))
                    answer = dialog.ShowDialog(DialogOwner());
                if (answer == DialogResult.Yes)
                {
                    if (!ImportDocument(backup)) return; // cancelled or failed: keep the backup protected
                    _config.BackupOfferOpen = false;
                    _config.Save();
                    _backup.QuestionSettled = true;
                }
                else if (answer == DialogResult.No)
                {
                    // Final for this profile: a newer backup could only be one this profile itself
                    // wrote after this answer, which is not a reason to ask again.
                    _config.BackupRestoreDeclined = Path.GetFileName(backup);
                    _config.BackupOfferOpen = false;
                    _config.Save();
                    _backup.QuestionSettled = true;
                }
                // "Not now": the marker stays, the writer stays silent, the question re-asks next start.
            }
            catch (Exception ex)
            {
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup offer failed " + ex.GetType().Name);
                // Keep the writer gated: a failed offer must never overwrite the recovery file.
            }
        }

        /// <summary>Commit's scenario step (S0072 6.3): replace the store, failures abort and roll back.</summary>
        private void ReplaceScenarioSet(List<LauncherScenario> set)
        {
            Exception? failure = _launcherStore.ReplaceSet(set);
            if (failure != null)
            {
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "scenarios failed " + failure.GetType().Name);
                throw new SettingsFileException("io");
            }
        }

        // A3: all preferences have a live consumer; the measurement is recorded in the ticket's research.
        // Each exported key is mapped to one of these refresh paths by SettingsApplyCoverageTests.
        internal void ApplyImportedSettings()
        {
            _applyingSettings = true;
            try { RefreshImportedSettings(); }
            finally { _applyingSettings = false; }
        }
        private void RefreshImportedSettings()
        {
            var placement = MemoryConfigKey.Capture(_config).Load();
            void Set(ToolStripMenuItem item, bool value, EventHandler handler)
            { item.CheckedChanged -= handler; try { item.Checked = value; } finally { item.CheckedChanged += handler; } }
            Set(_cursorItem, _config.EnableCursorChange, OnCursorToggle);
            Set(_caretItem, _config.EnableCaretOverlay, OnCaretToggle);
            Set(_dotModeItem, _config.CaretDotMode, OnDotModeToggle);
            Set(_langSwitchItem, _config.EnableLanguageSwitch, OnLangSwitchToggle);
            Set(_capsAfterItem, _config.FlipCapsLockAfter, OnCapsAfterToggle);
            Set(_historyEnabledItem, _config.EnableClipboardHistory, OnHistoryEnabledToggle);
            Set(_historyPauseItem, _config.PauseClipboardHistory, OnHistoryPauseToggle);
            Set(_keepAwakeItem, _config.KeepSystemAwake, OnKeepAwakeToggle);
            Set(_keepScreenItem, _config.KeepScreenOn, OnKeepScreenToggle);
            KeepAwake.Restore(_config.KeepSystemAwake, _config.KeepScreenOn);
            _caseHotkey = Hotkey.Parse(_config.CaseHotkey); _clipboardHistoryHotkey = Hotkey.Parse(_config.ClipboardHistoryHotkey);
            _quickNotesHotkey = Hotkey.Parse(_config.QuickNotesHotkey); _screenshotHotkey = Hotkey.Parse(_config.ScreenshotHotkey);
            _hook.UpdateCaseHotkey(_caseHotkey); _hook.UpdateClipboardHistoryHotkey(_clipboardHistoryHotkey);
            _hook.UpdateQuickNotesHotkey(_quickNotesHotkey); _hook.UpdateScreenshotHotkey(_screenshotHotkey);
            _hook.UpdateTextMenuHotkey(ParsedTextMenuHotkey());
            _hook.UpdateEnabled(_config.EnableHotkeys); _hook.UpdateCaseEnabled(_config.EnableCaseHotkey);
            _hook.UpdateHistoryEnabled(_config.EnableHistoryHotkey); _hook.UpdateDeferInRemoteClient(_config.DeferToRemoteDesktop);
            _hook.UpdateQuickNotesEnabled(QuickNotesChordLive); _hook.UpdateScreenshotEnabled(ScreenshotChordLive);
            _screenshotFullHotkey = ParsedScreenshotFullHotkey(); _hook.UpdateScreenshotFullHotkey(_screenshotFullHotkey);
            _hook.UpdateScreenshotAnnotateAreaHotkey(ParsedScreenshotAnnotateAreaHotkey());
            _hook.UpdateScreenshotFullAnnotateHotkey(ParsedScreenshotFullAnnotateHotkey());
            _screenshotFolderHotkey = ParsedScreenshotFolderHotkey(); _hook.UpdateScreenshotFolderHotkey(_screenshotFolderHotkey);
            _hook.UpdateScreenshotFolderEnabled(ScreenshotFolderChordLive);
            _hook.UpdateScreenshotFullEnabled(ScreenshotFullChordLive);
            _hook.UpdateScreenshotAnnotateAreaEnabled(ScreenshotAnnotateAreaChordLive);
            _hook.UpdateScreenshotFullAnnotateEnabled(ScreenshotFullAnnotateChordLive);
            _hook.UpdateTextMenuEnabled(TextMenuChordLive); _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            BindTranslationHotkeys(); _translateWasOn = _config.EnableTranslate;
            _textMenuChordWasLive = TextMenuChordLive; _screenshotChordWasLive = ScreenshotChordLive; _quickNotesChordWasLive = QuickNotesChordLive;
            _screenshotFullChordWasLive = ScreenshotFullChordLive;
            _screenshotAnnotateAreaChordWasLive = ScreenshotAnnotateAreaChordLive;
            _screenshotFullAnnotateChordWasLive = ScreenshotFullAnnotateChordLive;
            _screenshotFolderChordWasLive = ScreenshotFolderChordLive;
            _caretOverlay.SetBaseSize(_config.CursorSize); _layoutCursor.SetBaseSize(_config.CursorSize);
            _caretOverlay.SetDotMode(_config.CaretDotMode); _dotModeItem.Enabled = _config.EnableCaretOverlay;
            _caretOverlay.SetLayout(_config.EnableCaretOverlay ? _currentLayout : "", _currentKlid, _capsOn);
            if (_config.EnableCursorChange) _layoutCursor.Apply(_currentLayout, _currentKlid, _capsOn); else _layoutCursor.Restore();
            _clipboardHistory.SetEnabled(_config.EnableClipboardHistory); _clipboardHistory.SetPaused(_config.PauseClipboardHistory);
            _historyPauseItem.Enabled = _config.EnableClipboardHistory; _clipboardHistoryWindow.ApplyOpacity();
            bool startup = _config.ShowClipboardHistoryOnStartup;
            SetHistoryStartup(_config.EnableClipboardHistory && startup);
            _config.ShowClipboardHistoryOnStartup = startup;
            RefreshQuickNotesSettings(); RefreshContextMenuBinding();
            // The symbols block is in the file since v2 (S0072): the same path as the settings page's
            // handler - rebind the chord, follow the switch, dispose the window when the module is off.
            OnSymbolsChanged();
            // The traffic light's block likewise: the switch through the module path, the chord rebound,
            // the board refits (pin, tile size, columns).
            OnTrafficLightChanged();
            // Imported targets reach the engine only if the module is on; an off module stays off (S0069 D10).
            if (_trafficEngine != null) ApplyTrafficLightTargets();
            UpdateTrafficLightTray();
            RefreshExchangeHotkeys();
            // The launcher switch imports through the hand-flip path (S0072 6.2): the first-enable
            // offer runs once, on the user's explicit record, exactly as if the switch had been
            // flipped on the settings page - LauncherFirstEnableDone stays local.
            if (_launcherFlipPending) { _launcherFlipPending = false; SetLauncherEnabled(_config.EnableScenarioLauncher, refreshSurfaces: false); }
            _translateClipboardItem.Visible = _config.EnableTranslate;
            if (!_config.EnableTranslate) { _translateCts?.Cancel(); _translateWindow?.Hide(); }
            if (_translateWindow != null && !_translateWindow.IsDisposed)
            { _translateWindow.ApplyOpacity(); _translateWindow.ShowSourcePanel(_config.TranslateShowSource); _translateWindow.RefreshImportedTimeout(); }
            ThemeManager.SetMode(ThemeModes.Parse(_config.Theme));
            // The language: the strip, the search window and the popup here, the rest through the tray texts.
            FollowUiLanguageInWindows(); UpdateTrayTexts(); RefreshLauncherSurfaces();
            _settings?.Reload();
            ApplyImportedPlacement(placement);
            _settings?.ApplyImportedNavigation();
            _symbolsWindow?.ApplyImportedPreferences();
            _quickNotesWindow?.ApplyImportedSelection();
        }

        private void ApplyImportedPlacement(AppConfig saved)
        {
            var workAreas = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            void Place(Form? window, int x, int y, int width, int height, bool logical = false)
            {
                if (window == null || window.IsDisposed) return;
                int dpi = logical ? Math.Max(96, window.DeviceDpi) : 96;
                width = width > 0 ? ControlDpi.Scale(dpi, width) : window.Width;
                height = height > 0 ? ControlDpi.Scale(dpi, height) : window.Height;
                Point location = x == int.MinValue || y == int.MinValue ? window.Location
                    : new Point(ControlDpi.Scale(dpi, x), ControlDpi.Scale(dpi, y));
                window.Bounds = ScreenPlacement.Clamp(new Rectangle(location, new Size(width, height)), workAreas);
            }
            Place(_settings, saved.SettingsWindowX, saved.SettingsWindowY, saved.SettingsWindowWidth, saved.SettingsWindowHeight, true);
            Place(_clipboardHistoryWindow, saved.ClipboardHistoryX, saved.ClipboardHistoryY, saved.ClipboardHistoryWidth, saved.ClipboardHistoryHeight);
            Place(_quickNotesWindow, saved.QuickNotesX, saved.QuickNotesY, saved.QuickNotesWidth, saved.QuickNotesHeight);
            Place(_symbolsWindow, saved.SymbolsX, saved.SymbolsY, saved.SymbolsWidth, saved.SymbolsHeight);
            Place(_translateWindow, int.MinValue, int.MinValue, saved.TranslateWindowWidth, saved.TranslateWindowHeight);
            Place(_trafficWindow, saved.TrafficLightX, saved.TrafficLightY, 0, 0);
        }
    }
}
