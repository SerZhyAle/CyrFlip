using System.Collections.Generic;

namespace CyrFlip
{
    internal static class SettingsApplyCoverage
    {
        // Independent from the export inventory: a new exported preference must identify its consumer.
        internal static readonly IReadOnlyDictionary<string, string> Paths = Build();
        private static Dictionary<string, string> Build()
        {
            var result = new Dictionary<string, string>();
            void Add(string path, string keys) { foreach (string key in keys.Split(' ')) result.Add(key, path); }
            Add("ThemeManager.SetMode", "Theme");
            Add("UpdateTrayTexts", "UiLanguage");
            Add("KeepAwake.Restore", "KeepSystemAwake KeepScreenOn");
            Add("_caretOverlay.SetBaseSize", "CursorSize");
            Add("_caretOverlay.SetLayout", "EnableCaretOverlay");
            Add("_layoutCursor.Apply", "EnableCursorChange");
            Add("_caretOverlay.SetDotMode", "CaretDotMode");
            Add("live config at conversion dispatch", "EnableLanguageSwitch FlipCapsLockAfter ConvertSymbols");
            Add("_hook.UpdateEnabled", "EnableHotkeys"); Add("_hook.UpdateCaseEnabled", "EnableCaseHotkey");
            Add("_hook.UpdateHistoryEnabled", "EnableHistoryHotkey"); Add("_hook.UpdateDeferInRemoteClient", "DeferToRemoteDesktop");
            Add("_hook.UpdateCaseHotkey", "CaseHotkey"); Add("_hook.UpdateClipboardHistoryHotkey", "ClipboardHistoryHotkey");
            Add("_hook.UpdateConversionProfiles", "LayoutConversionProfiles");
            Add("_clipboardHistory.SetEnabled", "EnableClipboardHistory"); Add("_clipboardHistory.SetPaused", "PauseClipboardHistory");
            Add("SetHistoryStartup", "ShowClipboardHistoryOnStartup"); Add("_clipboardHistoryWindow.ApplyOpacity", "ClipboardHistoryOpacity");
            Add("RefreshContextMenuBinding", "EnableContextMenu ContextMenuChord"); Add("_hook.UpdateTextMenuEnabled", "EnableTextMenuHotkey");
            Add("_hook.UpdateTextMenuHotkey", "TextMenuHotkey"); Add("BindTranslationHotkeys", "EnableTranslate TranslateProfiles");
            Add("live config in TranslationService", "TranslateEndpoint TranslateModel TranslateTimeoutSeconds TranslateKeepAliveMinutes TranslateAutoStartServer TranslateCopyResult TranslatePasteResult");
            Add("_translateWindow.RefreshImportedTimeout", "TranslateWindowTimeout");
            Add("_translateWindow.ShowSourcePanel", "TranslateShowSource"); Add("_translateWindow.ApplyOpacity", "TranslateWindowOpacity");
            Add("RefreshQuickNotesSettings", "EnableQuickNotes QuickNotesWordWrap");
            Add("_hook.UpdateQuickNotesHotkey", "QuickNotesHotkey"); Add("_hook.UpdateQuickNotesEnabled", "EnableQuickNotesHotkey");
            Add("live config in exchange dialog", "ExchangeNotesWarningOff");
            Add("_hook.UpdateScreenshotHotkey", "ScreenshotHotkey"); Add("_hook.UpdateScreenshotEnabled", "EnableScreenshotHotkey");
            Add("_hook.UpdateScreenshotFullHotkey", "ScreenshotFullHotkey"); Add("_hook.UpdateScreenshotFullEnabled", "EnableScreenshotFullHotkey");
            Add("_hook.UpdateScreenshotAnnotateAreaHotkey", "ScreenshotAnnotateAreaHotkey"); Add("_hook.UpdateScreenshotAnnotateAreaEnabled", "EnableScreenshotAnnotateAreaHotkey");
            Add("_hook.UpdateScreenshotFullAnnotateHotkey", "ScreenshotFullAnnotateHotkey"); Add("_hook.UpdateScreenshotFullAnnotateEnabled", "EnableScreenshotFullAnnotateHotkey");
            Add("_hook.UpdateScreenshotFolderHotkey", "ScreenshotFolderHotkey"); Add("_hook.UpdateScreenshotFolderEnabled", "EnableScreenshotFolderHotkey");
            Add("live config at screenshot dispatch", "ScreenshotSaveEnabled");
            Add("live config at annotate dispatch", "EnableScreenshotAnnotate");
            Add("ApplyTrafficLightTargets", "TrafficLightTargets");
            // S0072: the v2 inventory's new consumers.
            Add("OnSymbolsChanged", "EnableSymbols EnableSymbolsHotkey SymbolsHotkey");
            Add("live config in SymbolsWindow", "SymbolsHideTypable SymbolsCloseOnPick");
            Add("OnTrafficLightChanged", "EnableTrafficLight EnableTrafficLightHotkey TrafficLightHotkey TrafficLightPinned TrafficLightTileSize TrafficLightColumns TrafficLightWindowTransparency");
            Add("SetLauncherEnabled", "EnableScenarioLauncher");
            Add("live config in ScreenshotSaver and ScreenshotFolderOpener", "ScreenshotFolder");
            Add("ApplyImportedPlacement", "SettingsWindowX SettingsWindowY SettingsWindowWidth SettingsWindowHeight"
                + " ClipboardHistoryX ClipboardHistoryY ClipboardHistoryWidth ClipboardHistoryHeight"
                + " QuickNotesX QuickNotesY QuickNotesWidth QuickNotesHeight"
                + " TranslateWindowWidth TranslateWindowHeight"
                + " SymbolsX SymbolsY SymbolsWidth SymbolsHeight"
                + " TrafficLightX TrafficLightY TrafficLightVisible");
            Add("live config at next settings open", "SettingsTab SettingsPageId SettingsGroupsState SettingsViewport QuickNotesSelected SymbolsCategory");
            Add("live config in SettingsBackup", "SettingsBackupEnabled");
            Add("RefreshExchangeHotkeys", "EnableExchangeClipboard EnableExchangeClipboardSendHotkey ExchangeClipboardSendHotkey EnableExchangeClipboardGetHotkey ExchangeClipboardGetHotkey");
            Add("live config in ExchangeClipboardService", "ExchangeClipboardReceivedFolder");
            return result;
        }
    }
}
