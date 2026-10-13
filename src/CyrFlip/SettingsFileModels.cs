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
        internal static MemoryConfigKey Capture(AppConfig config) { var key = new MemoryConfigKey(); config.SaveTo(key); return key; }
        internal AppConfig Load() => AppConfig.LoadFrom(this, out _);
    }

    internal sealed class SettingsFileEntry
    {
        internal SettingsFileEntry(string key, string group, string caption) { Key = key; Group = group; Caption = caption; }
        internal string Key { get; }
        internal string Group { get; }
        internal string Caption { get; }
        internal PropertyInfo Property => typeof(AppConfig).GetProperty(Key)!;
        internal bool IsTable => Key == "LayoutConversionProfiles" || Key == "TranslateProfiles" || Key == "TrafficLightTargets";
    }

    // Ordered allow-list. Adding a property cannot make it leave the machine; the inventory test
    // demands an explicit classification for every registry value.
    internal static class SettingsFileInventory
    {
        internal static readonly SettingsFileEntry[] Exported = {
            E("UiLanguage", "general/language", "Interface language:"), E("Theme", "general/language", "Theme:"),
            E("KeepSystemAwake", "general/power", "Keep the computer awake"), E("KeepScreenOn", "general/power", "Keep the screen on (like video)"),
            E("EnableCursorChange", "indicators/preferences", "Show layout on the text mouse cursor"),
            E("EnableCaretOverlay", "indicators/preferences", "Show layout label near the caret"), E("CaretDotMode", "indicators/preferences", "Compact dot instead of language letters"),
            E("CursorSize", "indicators/preferences", "Marker size:"), E("EnableLanguageSwitch", "conversions/after", "Change layout after converting text"),
            E("FlipCapsLockAfter", "conversions/case", "Synchronize CapsLock after case correction"),
            E("EnableHotkeys", "hotkeys/switches", "Listen for global hotkeys"), E("EnableCaseHotkey", "conversions/case", "Fix CapsLock"),
            E("CaseHotkey", "conversions/case", "Set case hotkey"), E("EnableHistoryHotkey", "clipboard/window", "Clipboard manager"),
            E("ClipboardHistoryHotkey", "clipboard/window", "Set clipboard history hotkey"), E("DeferToRemoteDesktop", "hotkeys/switches", "Yield hotkeys to the remote desktop (mstsc/msrdc)"),
            E("ConvertSymbols", "conversions/symbols", "Convert punctuation along with the text"), E("LayoutConversionProfiles", "conversions/table", "Layout conversions"),
            E("EnableClipboardHistory", "clipboard/recording", "Enable clipboard history"), E("PauseClipboardHistory", "clipboard/recording", "Pause history capture"),
            E("ShowClipboardHistoryOnStartup", "clipboard/window", "Show clipboard manager on startup"), E("ClipboardHistoryOpacity", "clipboard/window", "Opacity"),
            E("EnableContextMenu", "selection/mouse", "CyrFlip's own context menu over the selected text"), E("ContextMenuChord", "selection/mouse", "Mouse chord:"),
            E("EnableTextMenuHotkey", "selection/keyboard", "Open the menu from the keyboard"), E("TextMenuHotkey", "selection/keyboard", "Set the context-menu hotkey"),
            E("EnableTranslate", "translate/ollama", "Enable translation of the selected text"), E("TranslateEndpoint", "translate/ollama", "Server address:"), E("TranslateModel", "translate/ollama", "Model:"),
            E("TranslateTimeoutSeconds", "translate/ollama", "Wait for the model to load no longer than (s):"), E("TranslateKeepAliveMinutes", "translate/ollama", "Keep the model in memory (min):"),
            E("TranslateAutoStartServer", "translate/ollama", "Start Ollama automatically when translating"), E("TranslateProfiles", "translate/directions", "Translation directions"),
            E("TranslateCopyResult", "translate/result", "Put the translation on the clipboard"), E("TranslatePasteResult", "translate/result", "Paste the translation over the selection right away"),
            E("TranslateShowSource", "translate/result", "Show the source text next to the translation"), E("TranslateWindowTimeout", "translate/result", "Close the window after (s):"),
            E("TranslateWindowOpacity", "translate/result", "Opacity"),
            E("EnableQuickNotes", "notes/opening", "Enable the quick notes"), E("EnableQuickNotesHotkey", "notes/opening", "Quick notes"),
            E("QuickNotesHotkey", "notes/opening", "Set the quick-notes hotkey"), E("QuickNotesWordWrap", "notes/editor", "Wrap long lines in the editor"),
            E("ExchangeNotesWarningOff", "notes/storage", "Don't warn again for notes"),
            E("EnableScreenshotHotkey", "graphics/capture", "Screen region capture"), E("ScreenshotHotkey", "graphics/capture", "Screen region capture"),
            E("EnableScreenshotFolderHotkey", "graphics/capture", "Open the screenshots folder"), E("ScreenshotFolderHotkey", "graphics/capture", "Open the screenshots folder"),
            E("ScreenshotSaveEnabled", "graphics/capture", "Also save captures to a folder"),
            // The one slice that is opt-in on both ends (S0069 D10): the list names the user's own infrastructure,
            // so an export carries it only when asked, and an import takes it only on an explicit yes.
            E("TrafficLightTargets", "trafficlight/targets", "Availability traffic light targets")
        };
        private static SettingsFileEntry E(string key, string group, string caption) => new SettingsFileEntry(key, group, caption);
        internal static readonly Dictionary<string, string> Excluded = Classify();
        private static Dictionary<string, string> Classify()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string reason, string names) { foreach (string name in names.Split(' ')) result.Add(name, reason); }
            Add("navigation", "SettingsTab SettingsPageId SettingsGroupsState SettingsViewport SettingsWindowX SettingsWindowY SettingsWindowWidth SettingsWindowHeight ClipboardHistoryX ClipboardHistoryY ClipboardHistoryWidth ClipboardHistoryHeight QuickNotesX QuickNotesY QuickNotesWidth QuickNotesHeight QuickNotesSelected TranslateWindowWidth TranslateWindowHeight SymbolsX SymbolsY SymbolsWidth SymbolsHeight SymbolsCategory TrafficLightX TrafficLightY TrafficLightVisible");
            Add("usage", "FlipCount CaseFlipCount TranslateCount QuickNoteCount ScreenshotCount SymbolsCount");
            Add("machine history", "QuickNotesNoticeShown ClipboardHistoryNoticeShown CursorNoticeShown TranslateSeeded LauncherFirstEnableDone DataFolderMigrated ShortcutsCreated WelcomeShown");
            Add("Windows state", "LanguageHotkeysBackup InputLayoutsBackup InputMethodOverrideWritten");
            Add("machine path", "ScreenshotFolder"); Add("launcher excluded in v1", "EnableScenarioLauncher");
            Add("symbols window not in v1", "EnableSymbols EnableSymbolsHotkey SymbolsHotkey SymbolsHideTypable SymbolsCloseOnPick");
            // The module switch itself is never part of the slice: an import into an off module leaves it off (D10).
            Add("traffic light preferences not in v1", "EnableTrafficLight EnableTrafficLightHotkey TrafficLightHotkey TrafficLightPinned TrafficLightTileSize TrafficLightColumns");
            return result;
        }
        internal static void Copy(AppConfig source, AppConfig target)
        {
            // Also restores the internal TranslateSeeded marker; all other non-preferences stay local.
            foreach (SettingsFileEntry entry in Exported) entry.Property.SetValue(target, entry.Property.GetValue(source));
            target.TranslateSeeded = source.TranslateSeeded;
        }
    }

    internal sealed class SettingsFileDocument
    {
        internal string App = "";
        internal string ExportedAtUtc = "";
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
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
