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
        internal bool IsTable => Key == "LayoutConversionProfiles" || Key == "TranslateProfiles";
    }

    // Ordered allow-list. Adding a property cannot make it leave the machine; the inventory test
    // demands an explicit classification for every registry value.
    internal static class SettingsFileInventory
    {
        internal static readonly SettingsFileEntry[] Exported = {
            E("UiLanguage", "general/language", "Язык интерфейса:"), E("Theme", "general/language", "Тема:"),
            E("KeepSystemAwake", "general/power", "Не давать компьютеру засыпать"), E("KeepScreenOn", "general/power", "Не блокировать экран (как при видео)"),
            E("EnableCursorChange", "indicators/preferences", "Показывать раскладку на текстовом курсоре мыши"),
            E("EnableCaretOverlay", "indicators/preferences", "Показывать метку раскладки рядом с кареткой"), E("CaretDotMode", "indicators/preferences", "Компактная точка вместо букв раскладки"),
            E("CursorSize", "indicators/preferences", "Размер метки:"), E("EnableLanguageSwitch", "conversions/after", "Менять раскладку после конвертации текста"),
            E("FlipCapsLockAfter", "conversions/case", "Синхронизировать CapsLock после исправления регистра"),
            E("EnableHotkeys", "hotkeys/switches", "Слушать глобальные горячие клавиши"), E("EnableCaseHotkey", "conversions/case", "Исправить CapsLock"),
            E("CaseHotkey", "conversions/case", "Задать хоткей регистра"), E("EnableHistoryHotkey", "clipboard/window", "Менеджер буфера"),
            E("ClipboardHistoryHotkey", "clipboard/window", "Задать хоткей истории буфера"), E("DeferToRemoteDesktop", "hotkeys/switches", "Уступать хоткеи удалённому рабочему столу (mstsc/msrdc)"),
            E("ConvertSymbols", "conversions/symbols", "Конвертировать знаки препинания вместе с текстом"), E("LayoutConversionProfiles", "conversions/table", "Конвертация раскладок"),
            E("EnableClipboardHistory", "clipboard/recording", "Включить историю буфера"), E("PauseClipboardHistory", "clipboard/recording", "Приостановить захват истории"),
            E("ShowClipboardHistoryOnStartup", "clipboard/window", "Показывать окно менеджера буфера при запуске"), E("ClipboardHistoryOpacity", "clipboard/window", "Непрозрачность"),
            E("EnableContextMenu", "selection/mouse", "Своё контекстное меню над выделенным текстом"), E("ContextMenuChord", "selection/mouse", "Аккорд мыши:"),
            E("EnableTextMenuHotkey", "selection/keyboard", "Открыть меню с клавиатуры"), E("TextMenuHotkey", "selection/keyboard", "Задать хоткей контекстного меню"),
            E("EnableTranslate", "translate/ollama", "Включить перевод выделенного текста"), E("TranslateEndpoint", "translate/ollama", "Адрес сервера:"), E("TranslateModel", "translate/ollama", "Модель:"),
            E("TranslateTimeoutSeconds", "translate/ollama", "Ждать загрузки модели не дольше (с):"), E("TranslateKeepAliveMinutes", "translate/ollama", "Держать модель в памяти (мин):"),
            E("TranslateAutoStartServer", "translate/ollama", "Запускать Ollama автоматически при переводе"), E("TranslateProfiles", "translate/directions", "Направления перевода"),
            E("TranslateCopyResult", "translate/result", "Класть перевод в буфер обмена"), E("TranslatePasteResult", "translate/result", "Сразу вставлять перевод вместо выделения"),
            E("TranslateShowSource", "translate/result", "Показывать исходный текст рядом с переводом"), E("TranslateWindowTimeout", "translate/result", "Закрывать окно через (с):"),
            E("TranslateWindowOpacity", "translate/result", "Непрозрачность"),
            E("EnableQuickNotes", "notes/opening", "Включить быстрые заметки"), E("EnableQuickNotesHotkey", "notes/opening", "Быстрые заметки"),
            E("QuickNotesHotkey", "notes/opening", "Задать хоткей быстрых заметок"), E("QuickNotesWordWrap", "notes/editor", "Переносить длинные строки в редакторе"),
            E("ExchangeNotesWarningOff", "notes/storage", "Больше не предупреждать для заметок"),
            E("EnableScreenshotHotkey", "graphics/capture", "Снимок области экрана"), E("ScreenshotHotkey", "graphics/capture", "Снимок области экрана"),
            E("EnableScreenshotFolderHotkey", "graphics/capture", "Открыть папку со снимками"), E("ScreenshotFolderHotkey", "graphics/capture", "Открыть папку со снимками"),
            E("ScreenshotSaveEnabled", "graphics/capture", "Также сохранять снимки в папку")
        };
        private static SettingsFileEntry E(string key, string group, string caption) => new SettingsFileEntry(key, group, caption);
        internal static readonly Dictionary<string, string> Excluded = Classify();
        private static Dictionary<string, string> Classify()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string reason, string names) { foreach (string name in names.Split(' ')) result.Add(name, reason); }
            Add("navigation", "SettingsTab SettingsPageId SettingsGroupsState SettingsViewport SettingsWindowX SettingsWindowY SettingsWindowWidth SettingsWindowHeight ClipboardHistoryX ClipboardHistoryY ClipboardHistoryWidth ClipboardHistoryHeight QuickNotesX QuickNotesY QuickNotesWidth QuickNotesHeight QuickNotesSelected TranslateWindowWidth TranslateWindowHeight SymbolsX SymbolsY SymbolsWidth SymbolsHeight SymbolsCategory");
            Add("usage", "FlipCount CaseFlipCount TranslateCount QuickNoteCount ScreenshotCount SymbolsCount");
            Add("machine history", "QuickNotesNoticeShown TranslateSeeded LauncherFirstEnableDone DataFolderMigrated ShortcutsCreated WelcomeShown");
            Add("Windows state", "LanguageHotkeysBackup InputLayoutsBackup InputMethodOverrideWritten");
            Add("machine path", "ScreenshotFolder"); Add("launcher excluded in v1", "EnableScenarioLauncher");
            Add("symbols window not in v1", "EnableSymbols EnableSymbolsHotkey SymbolsHotkey SymbolsHideTypable SymbolsCloseOnPick");
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
