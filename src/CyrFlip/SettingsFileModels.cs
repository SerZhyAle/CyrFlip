using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace CyrFlip
{
    internal sealed class MemoryConfigKey : IConfigKey
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
        public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        internal static MemoryConfigKey Capture(AppConfig config)
        {
            var key = new MemoryConfigKey(); config.SaveTo(key);
            // The symbols and traffic-light blocks are written to the registry only once their module
            // has been enabled (D6), but the transfer machinery reads every exported key (S0072): an
            // off module reads as its in-memory values - the defaults, until the user changes them.
            foreach (SettingsFileEntry entry in SettingsFileInventory.Exported)
            {
                if (key.Values.ContainsKey(entry.Key)) continue;
                PropertyInfo? property = entry.Property;
                if (property == null) continue;
                object? value = property.GetValue(config);
                if (value is bool flag) key.Values[entry.Key] = flag ? 1 : 0;
                else if (value is int || value is string) key.Values[entry.Key] = value;
            }
            return key;
        }
        internal AppConfig Load() => AppConfig.LoadFrom(this, out _);
    }

    internal sealed class SettingsFileEntry
    {
        internal SettingsFileEntry(string key, string group, string caption) { Key = key; Group = group; Caption = caption; }
        internal string Key { get; }
        internal string Group { get; }
        internal string Caption { get; }
        /// <summary>Null for the synthetic "scenarios" entry - it is not a registry value (S0072 5.3).</summary>
        internal PropertyInfo? Property => typeof(AppConfig).GetProperty(Key);
        internal bool IsTable => Key == "LayoutConversionProfiles" || Key == "TranslateProfiles"
            || Key == "TrafficLightTargets" || Key == "scenarios";
    }

    // Ordered allow-list. Adding a property cannot make it leave the machine; the inventory test
    // demands an explicit classification for every registry value. Since v2 (S0072) the list is the
    // complete settings inventory: every chord, table, module switch and window rectangle the app
    // owns; only usage counters, machine history and this PC's Windows input state stay out.
    internal static class SettingsFileInventory
    {
        /// <summary>
        /// The launcher's quick-launch scenarios: not a registry value but a section of verbatim scenario
        /// documents (S0072 5.3). The writer takes them as a parameter, the reader fills
        /// <see cref="SettingsFileDocument.Scenarios"/>, and the plan carries them outside the per-value apply.
        /// </summary>
        internal static readonly SettingsFileEntry Scenarios = new SettingsFileEntry("scenarios", "launcher/scenarios", "Quick launch scenarios");
        internal static readonly SettingsFileEntry[] Exported = {
            E("UiLanguage", "general/language", "Interface language:"), E("Theme", "general/language", "Theme:"),
            E("KeepSystemAwake", "general/power", "Keep the computer awake"), E("KeepScreenOn", "general/power", "Keep the screen on (like video)"),
            E("SettingsBackupEnabled", "general/transfer", "Keep a settings backup in Documents"),
            E("EnableCursorChange", "indicators/preferences", "Show layout on the text mouse cursor"),
            E("EnableCaretOverlay", "indicators/preferences", "Show layout label near the caret"), E("CaretDotMode", "indicators/preferences", "Compact dot instead of language letters"),
            E("CursorSize", "indicators/preferences", "Marker size:"), E("EnableLanguageSwitch", "conversions/after", "Change layout after converting text"),
            E("FlipCapsLockAfter", "conversions/case", "Synchronize CapsLock after case correction"),
            E("EnableHotkeys", "hotkeys/switches", "Listen for global hotkeys"), E("EnableCaseHotkey", "conversions/case", "Case flip"),
            E("CaseHotkey", "conversions/case", "Set case shortcut"), E("EnableHistoryHotkey", "clipboard/window", "Clipboard history"),
            E("ClipboardHistoryHotkey", "clipboard/window", "Set clipboard history shortcut"), E("DeferToRemoteDesktop", "hotkeys/switches", "Yield hotkeys to the remote desktop (mstsc/msrdc)"),
            E("ConvertSymbols", "conversions/symbols", "Convert punctuation along with the text"), E("LayoutConversionProfiles", "conversions/table", "Layout conversions"),
            E("EnableClipboardHistory", "clipboard/recording", "Enable clipboard history"), E("PauseClipboardHistory", "clipboard/recording", "Pause history capture"),
            E("ShowClipboardHistoryOnStartup", "clipboard/window", "Show clipboard history on startup"), E("ClipboardHistoryOpacity", "clipboard/window", "Opacity"),
            E("ClipboardHistoryX", "navigation/windows", "History window position X"), E("ClipboardHistoryY", "navigation/windows", "History window position Y"),
            E("ClipboardHistoryWidth", "navigation/windows", "History window width"), E("ClipboardHistoryHeight", "navigation/windows", "History window height"),
            E("EnableContextMenu", "selection/mouse", "CyrFlip's own context menu over the selected text"), E("ContextMenuChord", "selection/mouse", "Mouse chord:"),
            E("EnableTextMenuHotkey", "selection/keyboard", "Open the menu from the keyboard"), E("TextMenuHotkey", "selection/keyboard", "Set the context-menu shortcut"),
            E("EnableTranslate", "translate/switches", "Enable translation of the selected text"), E("TranslateEndpoint", "translate/ollama", "Server address:"), E("TranslateModel", "translate/ollama", "Model:"),
            E("TranslateTimeoutSeconds", "translate/ollama", "Wait for the model to load no longer than (s):"), E("TranslateKeepAliveMinutes", "translate/ollama", "Keep the model in memory (min):"),
            E("TranslateAutoStartServer", "translate/ollama", "Start Ollama automatically when translating"), E("TranslateProfiles", "translate/directions", "Translation directions"),
            E("TranslateCopyResult", "translate/result", "Put the translation on the clipboard"), E("TranslatePasteResult", "translate/result", "Paste the translation over the selection right away"),
            E("TranslateShowSource", "translate/result", "Show the source text next to the translation"), E("TranslateWindowTimeout", "translate/result", "Close the window after (s):"),
            E("TranslateWindowOpacity", "translate/result", "Opacity"),
            E("TranslateWindowWidth", "navigation/windows", "Translation window width"), E("TranslateWindowHeight", "navigation/windows", "Translation window height"),
            E("EnableQuickNotes", "notes/switches", "Enable the quick notes"), E("EnableQuickNotesHotkey", "notes/opening", "Quick notes"),
            E("QuickNotesHotkey", "notes/opening", "Set the quick-notes shortcut"), E("QuickNotesWordWrap", "notes/editor", "Wrap long lines in the editor"),
            E("QuickNotesLineNumbers", "notes/editor", "Line numbers in the editor"),
            E("ExchangeNotesWarningOff", "notes/storage", "Don't warn again for notes"),
            E("QuickNotesX", "navigation/windows", "Quick-notes window position X"), E("QuickNotesY", "navigation/windows", "Quick-notes window position Y"),
            E("QuickNotesWidth", "navigation/windows", "Quick-notes window width"), E("QuickNotesHeight", "navigation/windows", "Quick-notes window height"),
            E("QuickNotesSelected", "navigation/windows", "Selected quick note"),
            E("EnableScenarioLauncher", "launcher/enable", "Quick launch module"),
            E("EnableScreenshotHotkey", "graphics/capture", "Screen region capture"), E("ScreenshotHotkey", "graphics/capture", "Screen region capture"),
            E("EnableScreenshotFullHotkey", "graphics/capture", "Full screen capture"), E("ScreenshotFullHotkey", "graphics/capture", "Full screen capture"),
            E("EnableScreenshotAnnotate", "graphics/capture", "Draw on screen captures"),
            E("EnableScreenshotAnnotateAreaHotkey", "graphics/capture", "Area capture with drawing"), E("ScreenshotAnnotateAreaHotkey", "graphics/capture", "Area capture with drawing"),
            E("EnableScreenshotFullAnnotateHotkey", "graphics/capture", "Full screen capture with drawing"), E("ScreenshotFullAnnotateHotkey", "graphics/capture", "Full screen capture with drawing"),
            E("EnableScreenshotFolderHotkey", "graphics/capture", "Open the screenshots folder"), E("ScreenshotFolderHotkey", "graphics/capture", "Open the screenshots folder"),
            E("ScreenshotSaveEnabled", "graphics/capture", "Also save captures to a folder"),
            E("ScreenshotFolder", "graphics/capture", "Folder for screen region captures"),
            E("SettingsTab", "navigation/windows", "Last settings page (index)"), E("SettingsPageId", "navigation/windows", "Last settings page"),
            E("SettingsGroupsState", "navigation/windows", "Expanded settings groups"), E("SettingsViewport", "navigation/windows", "Settings scroll positions"),
            E("SettingsWindowX", "navigation/windows", "Settings window position X"), E("SettingsWindowY", "navigation/windows", "Settings window position Y"),
            E("SettingsWindowWidth", "navigation/windows", "Settings window width"), E("SettingsWindowHeight", "navigation/windows", "Settings window height"),
            // The symbols window is in the file since v2 (S0072 5.1); its geometry is navigation.
            E("EnableSymbols", "symbols/switches", "Symbols window"), E("EnableSymbolsHotkey", "symbols/opening", "Open from the keyboard"),
            E("SymbolsHotkey", "symbols/opening", "Set the symbols shortcut"), E("SymbolsHideTypable", "symbols/options", "Hide symbols a layout can type"),
            E("SymbolsCloseOnPick", "symbols/options", "Close on pick"),
            E("SymbolsX", "navigation/windows", "Symbols window position X"), E("SymbolsY", "navigation/windows", "Symbols window position Y"),
            E("SymbolsWidth", "navigation/windows", "Symbols window width"), E("SymbolsHeight", "navigation/windows", "Symbols window height"),
            E("SymbolsCategory", "navigation/windows", "Symbols group"),
            // The traffic light is in the file since v2 (S0072 5.1); the targets are always exported (D3).
            E("EnableTrafficLight", "trafficlight/switches", "Availability traffic light"),
            E("EnableTrafficLightHotkey", "trafficlight/switches", "Open from the keyboard"),
            E("TrafficLightHotkey", "trafficlight/switches", "Set the traffic light shortcut"),
            E("TrafficLightPinned", "trafficlight/module", "Pinned on top"), E("TrafficLightTileSize", "trafficlight/module", "Tile size"),
            E("TrafficLightColumns", "trafficlight/module", "Tiles in a row"), E("TrafficLightWindowTransparency", "trafficlight/module", "Board window transparency"),
            E("TrafficLightX", "navigation/windows", "Traffic light board position X"), E("TrafficLightY", "navigation/windows", "Traffic light board position Y"),
            E("TrafficLightVisible", "navigation/windows", "Traffic light board was visible"),
            // Targets always in the file since v2 (S0072 5.3, D3): the export ask of S0069 D10 retired.
            E("TrafficLightTargets", "trafficlight/targets", "Availability traffic light targets"),
            // Exchange server clipboard is in the file since v2 (S0074); account credentials stay excluded.
            E("EnableExchangeClipboard", "exchangeserver/switches", "Enable the clipboard on the server"),
            E("EnableExchangeClipboardSendHotkey", "exchangeserver/switches", "Send clipboard to server"),
            E("ExchangeClipboardSendHotkey", "exchangeserver/switches", "Set send clipboard shortcut"),
            E("EnableExchangeClipboardGetHotkey", "exchangeserver/switches", "Get clipboard from server"),
            E("ExchangeClipboardGetHotkey", "exchangeserver/switches", "Set get clipboard shortcut"),
            E("ExchangeClipboardReceivedFolder", "exchangeserver/options", "Folder for files received from the server")
        };
        private static SettingsFileEntry E(string key, string group, string caption) => new SettingsFileEntry(key, group, caption);
        internal static readonly Dictionary<string, string> Excluded = Classify();
        private static Dictionary<string, string> Classify()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string reason, string names) { foreach (string name in names.Split(' ')) result.Add(name, reason); }
            Add("usage", "FlipCount CaseFlipCount TranslateCount QuickNoteCount ScreenshotCount SymbolsCount");
            Add("machine history", "QuickNotesNoticeShown ClipboardHistoryNoticeShown CursorNoticeShown TranslateSeeded LauncherFirstEnableDone DataFolderMigrated ShortcutsCreated WelcomeShown ScreenshotKeyMigrationDone BackupOfferOpen BackupRestoreDeclined");
            Add("Windows state", "LanguageHotkeysBackup InputLayoutsBackup InputMethodOverrideWritten");
            Add("credentials", "ExchangeClipboardAccounts");
            return result;
        }
        internal static void Copy(AppConfig source, AppConfig target)
        {
            // Also restores the internal TranslateSeeded marker; all other non-preferences stay local.
            foreach (SettingsFileEntry entry in Exported) entry.Property!.SetValue(target, entry.Property.GetValue(source));
            target.TranslateSeeded = source.TranslateSeeded;
        }
    }

    internal sealed class SettingsFileDocument
    {
        internal string App = "";
        internal string ExportedAtUtc = "";
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        /// <summary>The verbatim scenario documents of <c>tables.scenarios</c>; null when the section is absent (a v1 file) - keep local (S4).</summary>
        internal List<string>? Scenarios;
        internal readonly List<SettingsFileIssue> Issues = new List<SettingsFileIssue>();
        internal int UnknownKeys;
    }
    internal sealed class SettingsFileIssue
    {
        internal SettingsFileIssue(string key, string reason) { Key = key; Reason = reason; }
        internal string Key { get; }
        internal string Reason { get; }
    }
    internal sealed class SettingsFileException : Exception
    {
        internal SettingsFileException(string code) : base(code) { Code = code; }
        internal string Code { get; }
    }
}
