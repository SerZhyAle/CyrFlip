using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    internal sealed partial class CyrFlipContext
    {
        private bool _applyingSettings;
        private void TransferSettings(string operation)
        {
            string language = _config.UiLanguage;
            try
            {
                if (operation == "export")
                {
                    using var save = new SaveFileDialog { Filter = T("Файл настроек CyrFlip (*.json)") + "|*.json",
                        FileName = SettingsFileWriter.DefaultFileName(DateTime.Now), DefaultExt = "json", AddExtension = true };
                    if (save.ShowDialog(_settings) != DialogResult.OK) return;
                    string text = SettingsFileWriter.Write(_config, DateTime.UtcNow, typeof(AppConfig).Assembly.GetName().Version!.ToString());
                    BusyDialog.Run(_settings, language, () => SettingsFileWriter.SaveText(save.FileName, text));
                    DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "export ok");
                    ConfirmDialog.Show(_settings, language, T("Настройки записаны. Заметки, история буфера и сценарии быстрого запуска в файл не входят."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string? path;
                if (operation == "restore") path = SettingsSnapshot.Newest(SettingsSnapshot.Folder);
                else
                {
                    using var open = new OpenFileDialog { Filter = T("Файл настроек CyrFlip (*.json)") + "|*.json", CheckFileExists = true };
                    if (open.ShowDialog(_settings) != DialogResult.OK) return; path = open.FileName;
                }
                if (path == null) return;
                var document = BusyDialog.Run(_settings, language, () => SettingsFileReader.ReadFile(path));
                var scenarios = _launcherStore.All.ToArray(); var windows = LanguageHotkeys.ReadAll();
                var installed = new HashSet<string>(InputLayouts.ListInstalled().Select(r => r.Klid), StringComparer.OrdinalIgnoreCase);
                var local = MemoryConfigKey.Capture(_config).Load();
                using var preview = new SettingsTransferDialog(language, document,
                    selected => SettingsImportPlan.Build(local, document, scenarios, windows, HotkeyRules.Check, installed, selected));
                if (preview.ShowDialog(_settings) != DialogResult.OK) return;
                // Freeze the preview's candidate; no second parser or validator at commit.
                using RegistryKey registry = Registry.CurrentUser.CreateSubKey(@"Software\CyrFlip")!;
                SettingsSnapshot.Commit(_config, preview.Plan, new RegistryConfigKey(registry), ApplyImportedSettings,
                    before => BusyDialog.Run(_settings, language, () => SettingsSnapshot.Create(SettingsSnapshot.Folder, before)));
                try { BusyDialog.Run(_settings, language, () => SettingsSnapshot.Prune(SettingsSnapshot.Folder)); }
                catch { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "retention failed io"); }
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "import ok changes=" + preview.Plan.Changes.Count + " skipped=" + preview.Plan.Issues.Count);
                foreach (var issue in preview.Plan.Issues) DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), issue.Key + " " + issue.Reason);
                _settings?.Reload();
            }
            catch (Exception ex)
            {
                string code = ex is SettingsFileException fault ? fault.Code : ex is AggregateException ? "rollback" : "io";
                DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "operation failed " + code);
                string message = code == "version" ? "Обновите CyrFlip, чтобы прочитать этот файл настроек."
                    : code == "format" ? "Это не файл настроек CyrFlip."
                    : code == "limit" ? "Файл настроек превышает допустимый размер."
                    : code == "rolled-back" ? "Импорт не удался. Предыдущие настройки восстановлены."
                    : code == "rollback" ? "Не удалось восстановить все настройки. Снимок сохранён в папке backups."
                    : "Не удалось прочитать или сохранить настройки. Ничего не изменено.";
                ConfirmDialog.Show(_settings, language, Localization.Translate(language, message), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _screenshotFolderHotkey = ParsedScreenshotFolderHotkey(); _hook.UpdateScreenshotFolderHotkey(_screenshotFolderHotkey);
            _hook.UpdateScreenshotFolderEnabled(ScreenshotFolderChordLive);
            _hook.UpdateTextMenuEnabled(TextMenuChordLive); _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            BindTranslationHotkeys(); _translateWasOn = _config.EnableTranslate;
            _textMenuChordWasLive = TextMenuChordLive; _screenshotChordWasLive = ScreenshotChordLive; _quickNotesChordWasLive = QuickNotesChordLive;
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
            _translateClipboardItem.Visible = _config.EnableTranslate;
            if (!_config.EnableTranslate) { _translateCts?.Cancel(); _translateWindow?.Hide(); }
            if (_translateWindow != null && !_translateWindow.IsDisposed)
            { _translateWindow.ApplyOpacity(); _translateWindow.ShowSourcePanel(_config.TranslateShowSource); _translateWindow.ApplyLanguage(_config.UiLanguage); _translateWindow.RefreshImportedTimeout(); }
            if (_clipboardHistorySearchWindow != null && !_clipboardHistorySearchWindow.IsDisposed)
                _clipboardHistorySearchWindow.ApplyLanguage(_config.UiLanguage);
            ThemeManager.SetMode(ThemeModes.Parse(_config.Theme));
            _clipboardHistoryWindow.ApplyLanguage(); UpdateTrayTexts(); RefreshLauncherSurfaces();
            _settings?.Reload();
        }
    }
}
