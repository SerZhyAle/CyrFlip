using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// Application settings, persisted to HKCU\Software\CyrFlip. On first run with no registry
    /// key present, attempts to migrate from a legacy config.json file.
    /// </summary>
    internal sealed class AppConfig
    {
        private const string RegPath = @"Software\CyrFlip";

        /// <summary>The chord the seeded EN ⇄ RU row of the conversion table carries out of the box.</summary>
        public const string DefaultFlipHotkey = "Ctrl+Shift+F12";
        /// <summary>
        /// The chord the starter translation row carries. F12/F11/F10 are taken by the flip, the case
        /// flip and the clipboard history, so the translator starts one key further down.
        /// </summary>
        public const string DefaultTranslateHotkey = "Ctrl+Shift+F9";
        /// <summary>Small enough for an ordinary laptop (~2 GB), good enough to translate.</summary>
        public const string DefaultTranslateModel = "qwen2.5:3b";
        private const string UsKlid = "00000409";
        private const string RussianKlid = "00000419";

        public string CaseHotkey { get; set; } = "Ctrl+Shift+F11";
        public string ClipboardHistoryHotkey { get; set; } = "Ctrl+Shift+F10";
        public string UiLanguage { get; set; } = DefaultUiLanguage();
        /// <summary>
        /// Index of the settings tab the window was last left on, so reopening it comes back to the
        /// page the user was working on instead of always to "Общие". Clamped against the live page
        /// count when applied - the tab strip grows between versions.
        /// </summary>
        public int SettingsTab { get; set; } = 0;
        public bool EnableClipboardHistory { get; set; } = false;
        public bool PauseClipboardHistory { get; set; } = false;
        public bool ShowClipboardHistoryOnStartup { get; set; } = true;
        public int ClipboardHistoryX { get; set; } = int.MinValue;
        public int ClipboardHistoryY { get; set; } = int.MinValue;
        public int ClipboardHistoryWidth { get; set; } = 260;
        public int ClipboardHistoryHeight { get; set; } = 360;
        /// <summary>Opacity percentage for the clipboard-history strip (30..100).</summary>
        public int ClipboardHistoryOpacity { get; set; } = 100;
        public int CursorSize { get; set; } = 24;
        public bool EnableCursorChange { get; set; } = false;
        public bool EnableCaretOverlay { get; set; } = true;
        public bool CaretDotMode { get; set; } = false;
        public bool EnableLanguageSwitch { get; set; } = false;
        public bool FlipCapsLockAfter { get; set; } = false;
        /// <summary>Master switch for the global hotkeys. When false the keyboard hook passes every key through.</summary>
        public bool EnableHotkeys { get; set; } = true;
        /// <summary>
        /// Per-hotkey switches for the two fixed chords. The layout→layout chords are not here: each
        /// row of <see cref="LayoutConversionProfiles"/> carries its own switch.
        /// </summary>
        public bool EnableCaseHotkey { get; set; } = true;
        public bool EnableHistoryHotkey { get; set; } = true;
        /// <summary>
        /// When true, ignore the hotkeys while a remote-desktop client (mstsc/msrdc) window is focused,
        /// so the key reaches the remote session and the CyrFlip running there handles it. Prevents the
        /// double-instance clash when CyrFlip runs on both ends of an RDP connection.
        /// </summary>
        public bool DeferToRemoteDesktop { get; set; } = false;
        /// <summary>
        /// Snapshot of Windows' own input-language hotkeys as they were before CyrFlip first touched
        /// them (see <see cref="LanguageHotkeys"/>). Captured once, never overwritten, so "put it back
        /// as it was" always means the state the user arrived with - not the state after our last edit.
        /// </summary>
        public string LanguageHotkeysBackup { get; set; } = "";
        /// <summary>
        /// One-time snapshot of the keyboard-layout stores (legacy Preload/Substitutes + the modern
        /// User Profile subtree) as they were before CyrFlip first changed them (see <see cref="InputLayouts"/>).
        /// </summary>
        public string InputLayoutsBackup { get; set; } = "";
        /// <summary>
        /// Every layout→layout conversion, each with its own hotkey and its own on/off switch - the
        /// single home of these chords, including the EN ⇄ RU flip CyrFlip started life with.
        /// </summary>
        public List<LayoutConversionProfile> LayoutConversionProfiles { get; set; } = new List<LayoutConversionProfile>();
        /// <summary>
        /// The scenario launcher (absorbed OneClickRunner). Off by default: while false the tray has
        /// no Launcher submenu, the Jump List carries no scenario tasks, and launcher commands from
        /// stale Jump List entries are ignored - CyrFlip behaves exactly as before the feature.
        /// </summary>
        public bool EnableScenarioLauncher { get; set; } = false;
        /// <summary>
        /// One-time marker for the launcher's first enable: the migration offer / sample seeding runs
        /// once, so a user who empties the list (or declines the migration) is never nagged again.
        /// </summary>
        public bool LauncherFirstEnableDone { get; set; } = false;
        /// <summary>
        /// The built-in translator (a local Ollama server). Off by default: while false no chord is
        /// bound, the tray has no translate entry and CyrFlip never opens a socket.
        /// </summary>
        public bool EnableTranslate { get; set; } = false;
        /// <summary>
        /// One-time marker for the translator's first enable, so the starter row is offered once and
        /// a table the user empties later is never refilled behind their back.
        /// </summary>
        public bool TranslateSeeded { get; set; } = false;
        /// <summary>Every "translate into this language" row, each with its own chord and switch.</summary>
        public List<TranslationProfile> TranslateProfiles { get; set; } = new List<TranslationProfile>();
        /// <summary>Empty means <see cref="OllamaClient.DefaultEndpoint"/> (localhost).</summary>
        public string TranslateEndpoint { get; set; } = "";
        /// <summary>Empty means "use whichever model is installed" (see TranslationService).</summary>
        public string TranslateModel { get; set; } = DefaultTranslateModel;
        public int TranslateTimeoutSeconds { get; set; } = 120;
        /// <summary>How long Ollama keeps the model in RAM after a translation; 0 unloads at once.</summary>
        public int TranslateKeepAliveMinutes { get; set; } = 5;
        public bool TranslateAutoStartServer { get; set; } = true;
        /// <summary>Put the translation on the clipboard - where the history picks it up as usual.</summary>
        public bool TranslateCopyResult { get; set; } = false;
        public bool TranslatePasteResult { get; set; } = false;
        public bool TranslateShowSource { get; set; } = false;
        /// <summary>Seconds before the result window closes itself; 0 = never.</summary>
        public int TranslateWindowTimeout { get; set; } = 0;
        public int TranslateWindowWidth { get; set; } = 460;
        public int TranslateWindowHeight { get; set; } = 260;
        /// <summary>Opacity percentage for the result window (30..100).</summary>
        public int TranslateWindowOpacity { get; set; } = 100;
        public int FlipCount { get; set; } = 0;
        public int CaseFlipCount { get; set; } = 0;
        public int TranslateCount { get; set; } = 0;

        /// <summary>
        /// UI language default for a fresh install (no saved value): follow the OS UI language when
        /// CyrFlip is translated into it, else English (see <see cref="Localization.DefaultLanguage"/>).
        /// Fixes a Russian UI appearing on an English OS.
        /// </summary>
        private static string DefaultUiLanguage() => Localization.DefaultLanguage();

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            // True when the conversion table had to be created rather than read - a fresh install or a
            // config written before the table existed. Persisted below, once the registry key is closed.
            bool seeded = false;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath);
                if (key == null)
                {
                    cfg.LayoutConversionProfiles.Add(FlipRow(cfg.MigrateFromJson(), true));
                    seeded = true;
                }
                else
                {
                    cfg.CaseHotkey = key.GetValue("CaseHotkey") as string ?? cfg.CaseHotkey;
                    cfg.ClipboardHistoryHotkey = key.GetValue("ClipboardHistoryHotkey") as string ?? cfg.ClipboardHistoryHotkey;
                    cfg.UiLanguage = key.GetValue("UiLanguage") as string ?? cfg.UiLanguage;
                    cfg.SettingsTab = Math.Max(0, GetInt(key, "SettingsTab", cfg.SettingsTab));
                    cfg.EnableClipboardHistory = GetBool(key, "EnableClipboardHistory", cfg.EnableClipboardHistory);
                    cfg.PauseClipboardHistory = GetBool(key, "PauseClipboardHistory", cfg.PauseClipboardHistory);
                    cfg.ShowClipboardHistoryOnStartup = GetBool(key, "ShowClipboardHistoryOnStartup", cfg.ShowClipboardHistoryOnStartup);
                    cfg.ClipboardHistoryX = GetInt(key, "ClipboardHistoryX", cfg.ClipboardHistoryX);
                    cfg.ClipboardHistoryY = GetInt(key, "ClipboardHistoryY", cfg.ClipboardHistoryY);
                    cfg.ClipboardHistoryWidth = GetInt(key, "ClipboardHistoryWidth", cfg.ClipboardHistoryWidth);
                    cfg.ClipboardHistoryHeight = GetInt(key, "ClipboardHistoryHeight", cfg.ClipboardHistoryHeight);
                    cfg.ClipboardHistoryOpacity = Math.Max(30, Math.Min(100, GetInt(key, "ClipboardHistoryOpacity", cfg.ClipboardHistoryOpacity)));
                    cfg.CursorSize = GetInt(key, "CursorSize", cfg.CursorSize);
                    cfg.EnableCursorChange = GetBool(key, "EnableCursorChange", cfg.EnableCursorChange);
                    cfg.EnableCaretOverlay = GetBool(key, "EnableCaretOverlay", cfg.EnableCaretOverlay);
                    cfg.CaretDotMode = GetBool(key, "CaretDotMode", cfg.CaretDotMode);
                    cfg.EnableLanguageSwitch = GetBool(key, "EnableLanguageSwitch", cfg.EnableLanguageSwitch);
                    cfg.FlipCapsLockAfter = GetBool(key, "FlipCapsLockAfter", cfg.FlipCapsLockAfter);
                    cfg.EnableHotkeys = GetBool(key, "EnableHotkeys", cfg.EnableHotkeys);
                    cfg.EnableCaseHotkey = GetBool(key, "EnableCaseHotkey", cfg.EnableCaseHotkey);
                    cfg.EnableHistoryHotkey = GetBool(key, "EnableHistoryHotkey", cfg.EnableHistoryHotkey);
                    cfg.DeferToRemoteDesktop = GetBool(key, "DeferToRemoteDesktop", cfg.DeferToRemoteDesktop);
                    cfg.LanguageHotkeysBackup = key.GetValue("LanguageHotkeysBackup") as string ?? cfg.LanguageHotkeysBackup;
                    cfg.InputLayoutsBackup = key.GetValue("InputLayoutsBackup") as string ?? cfg.InputLayoutsBackup;
                    cfg.EnableScenarioLauncher = GetBool(key, "EnableScenarioLauncher", cfg.EnableScenarioLauncher);
                    cfg.LauncherFirstEnableDone = GetBool(key, "LauncherFirstEnableDone", cfg.LauncherFirstEnableDone);

                    cfg.EnableTranslate = GetBool(key, "EnableTranslate", cfg.EnableTranslate);
                    cfg.TranslateSeeded = GetBool(key, "TranslateSeeded", cfg.TranslateSeeded);
                    cfg.TranslateProfiles = ReadTranslationProfiles(key.GetValue("TranslateProfiles") as string);
                    cfg.TranslateEndpoint = key.GetValue("TranslateEndpoint") as string ?? cfg.TranslateEndpoint;
                    cfg.TranslateModel = key.GetValue("TranslateModel") as string ?? cfg.TranslateModel;
                    cfg.TranslateTimeoutSeconds = Math.Max(5, GetInt(key, "TranslateTimeoutSeconds", cfg.TranslateTimeoutSeconds));
                    cfg.TranslateKeepAliveMinutes = Math.Max(0, GetInt(key, "TranslateKeepAliveMinutes", cfg.TranslateKeepAliveMinutes));
                    cfg.TranslateAutoStartServer = GetBool(key, "TranslateAutoStartServer", cfg.TranslateAutoStartServer);
                    cfg.TranslateCopyResult = GetBool(key, "TranslateCopyResult", cfg.TranslateCopyResult);
                    cfg.TranslatePasteResult = GetBool(key, "TranslatePasteResult", cfg.TranslatePasteResult);
                    cfg.TranslateShowSource = GetBool(key, "TranslateShowSource", cfg.TranslateShowSource);
                    cfg.TranslateWindowTimeout = Math.Max(0, GetInt(key, "TranslateWindowTimeout", cfg.TranslateWindowTimeout));
                    cfg.TranslateWindowWidth = GetInt(key, "TranslateWindowWidth", cfg.TranslateWindowWidth);
                    cfg.TranslateWindowHeight = GetInt(key, "TranslateWindowHeight", cfg.TranslateWindowHeight);
                    cfg.TranslateWindowOpacity = Math.Max(30, Math.Min(100, GetInt(key, "TranslateWindowOpacity", cfg.TranslateWindowOpacity)));

                    string? profiles = key.GetValue("LayoutConversionProfiles") as string;
                    string? legacyHotkey = key.GetValue("Hotkey") as string;
                    bool legacyPresent = legacyHotkey != null || key.GetValue("EnableFlipHotkey") != null;
                    cfg.LayoutConversionProfiles = ReadProfiles(profiles);
                    seeded = NeedsFlipRow(profiles, legacyPresent, cfg.LayoutConversionProfiles.Count);
                    if (seeded)
                        cfg.LayoutConversionProfiles.Insert(0,
                            FlipRow(legacyHotkey, GetBool(key, "EnableFlipHotkey", true)));

                    cfg.FlipCount = GetInt(key, "FlipCount", cfg.FlipCount);
                    cfg.CaseFlipCount = GetInt(key, "CaseFlipCount", cfg.CaseFlipCount);
                    cfg.TranslateCount = GetInt(key, "TranslateCount", cfg.TranslateCount);
                }
            }
            catch { /* keep defaults */ }

            // Outside the try/using: the seeded row has to reach the registry (so the next launch reads
            // it instead of seeding again), and the superseded values are dropped in the same pass.
            if (seeded)
            {
                cfg.Save();
                DropLegacyFlipValues();
            }
            return cfg;
        }

        public void Save()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("CaseHotkey", CaseHotkey, RegistryValueKind.String);
                key.SetValue("ClipboardHistoryHotkey", ClipboardHistoryHotkey, RegistryValueKind.String);
                key.SetValue("UiLanguage", UiLanguage, RegistryValueKind.String);
                key.SetValue("SettingsTab", SettingsTab, RegistryValueKind.DWord);
                key.SetValue("EnableClipboardHistory", EnableClipboardHistory ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("PauseClipboardHistory", PauseClipboardHistory ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ShowClipboardHistoryOnStartup", ShowClipboardHistoryOnStartup ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryX", ClipboardHistoryX, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryY", ClipboardHistoryY, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryWidth", ClipboardHistoryWidth, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryHeight", ClipboardHistoryHeight, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryOpacity", ClipboardHistoryOpacity, RegistryValueKind.DWord);
                key.SetValue("CursorSize", CursorSize, RegistryValueKind.DWord);
                key.SetValue("EnableCursorChange", EnableCursorChange ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableCaretOverlay", EnableCaretOverlay ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CaretDotMode", CaretDotMode ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableLanguageSwitch", EnableLanguageSwitch ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("FlipCapsLockAfter", FlipCapsLockAfter ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableHotkeys", EnableHotkeys ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableCaseHotkey", EnableCaseHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableHistoryHotkey", EnableHistoryHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DeferToRemoteDesktop", DeferToRemoteDesktop ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("LanguageHotkeysBackup", LanguageHotkeysBackup, RegistryValueKind.String);
                key.SetValue("InputLayoutsBackup", InputLayoutsBackup, RegistryValueKind.String);
                key.SetValue("EnableScenarioLauncher", EnableScenarioLauncher ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("LauncherFirstEnableDone", LauncherFirstEnableDone ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableTranslate", EnableTranslate ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateSeeded", TranslateSeeded ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateEndpoint", TranslateEndpoint, RegistryValueKind.String);
                key.SetValue("TranslateModel", TranslateModel, RegistryValueKind.String);
                key.SetValue("TranslateTimeoutSeconds", TranslateTimeoutSeconds, RegistryValueKind.DWord);
                key.SetValue("TranslateKeepAliveMinutes", TranslateKeepAliveMinutes, RegistryValueKind.DWord);
                key.SetValue("TranslateAutoStartServer", TranslateAutoStartServer ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateCopyResult", TranslateCopyResult ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslatePasteResult", TranslatePasteResult ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateShowSource", TranslateShowSource ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateWindowTimeout", TranslateWindowTimeout, RegistryValueKind.DWord);
                key.SetValue("TranslateWindowWidth", TranslateWindowWidth, RegistryValueKind.DWord);
                key.SetValue("TranslateWindowHeight", TranslateWindowHeight, RegistryValueKind.DWord);
                key.SetValue("TranslateWindowOpacity", TranslateWindowOpacity, RegistryValueKind.DWord);
                key.SetValue("LayoutConversionProfiles", new JavaScriptSerializer().Serialize(LayoutConversionProfiles), RegistryValueKind.String);
                key.SetValue("TranslateProfiles", new JavaScriptSerializer().Serialize(TranslateProfiles), RegistryValueKind.String);
                key.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
                key.SetValue("CaseFlipCount", CaseFlipCount, RegistryValueKind.DWord);
                key.SetValue("TranslateCount", TranslateCount, RegistryValueKind.DWord);
            }
            catch { /* best effort */ }
        }

        /// <summary>
        /// Remember the settings tab and persist only that value (cheap write) - switching tabs
        /// shouldn't rewrite every setting just to record where the user is looking.
        /// </summary>
        public void SaveSettingsTab(int index)
        {
            if (index < 0 || index == SettingsTab) return;
            SettingsTab = index;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("SettingsTab", SettingsTab, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the flip counter and persist only that value (cheap write).</summary>
        public void IncrementFlipCount()
        {
            FlipCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the case-flip counter and persist only that value (cheap write).</summary>
        public void IncrementCaseFlipCount()
        {
            CaseFlipCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("CaseFlipCount", CaseFlipCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the translation counter and persist only that value (cheap write).</summary>
        public void IncrementTranslateCount()
        {
            TranslateCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("TranslateCount", TranslateCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>
        /// Pull what a legacy config.json still has to say (first run, no registry key yet) and return
        /// the flip chord it carried, so the caller can seed the conversion table with it. The caller
        /// persists - a migration that saved on its own would write before the seeding is applied.
        /// </summary>
        private string MigrateFromJson()
        {
            string hotkey = DefaultFlipHotkey;
            try
            {
                string? path = ResolveJsonPath();
                if (path == null || !File.Exists(path))
                    return hotkey;
                var data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (data == null) return hotkey;
                if (data.TryGetValue("hotkey", out var h) && h is string hs && hs.Length > 0)
                    hotkey = hs;
                if (data.TryGetValue("cursorSize", out var c) && c != null)
                    CursorSize = Convert.ToInt32(c);
            }
            catch { }
            return hotkey;
        }

        /// <summary>
        /// Whether the EN ⇄ RU flip still has to be moved into the conversion table, which is the single
        /// home of every layout→layout chord. True when the table predates this config entirely (an old
        /// release, or a fresh install), and also when the table is there but empty while the superseded
        /// <c>Hotkey</c>/<c>EnableFlipHotkey</c> values are still around - the config a build that had the
        /// table but not yet the merge left behind, where the flip was a separate feature and the table
        /// started out empty.
        ///
        /// The marker is those two values, not the emptiness of the table: they are deleted as soon as the
        /// row is seeded, so a table the user empties later is never refilled behind their back.
        /// </summary>
        internal static bool NeedsFlipRow(string? profilesJson, bool legacyValuesPresent, int rowCount)
            => profilesJson == null || (legacyValuesPresent && rowCount == 0);

        /// <summary>
        /// The seeded EN ⇄ RU row, carrying over the chord and the on/off switch that used to live in
        /// their own registry values (or the defaults, on a fresh install).
        /// </summary>
        internal static LayoutConversionProfile FlipRow(string? legacyHotkey, bool legacyEnabled)
            => new LayoutConversionProfile
            {
                SourceKlid = UsKlid,
                TargetKlid = RussianKlid,
                Hotkey = string.IsNullOrEmpty(legacyHotkey) ? DefaultFlipHotkey : legacyHotkey!,
                Enabled = legacyEnabled,
            };

        /// <summary>
        /// Whether the translator still owes the user its starter row. Unlike the conversion table this
        /// is not a migration: the row appears the first time the feature is switched on, and the
        /// <c>TranslateSeeded</c> marker - not the emptiness of the table - is what says "already
        /// offered", so a table the user empties on purpose stays empty.
        /// </summary>
        internal static bool NeedsTranslateRow(bool seeded, int rowCount) => !seeded && rowCount == 0;

        /// <summary>
        /// The starter translation row: into the language of the UI, on the default chord. When that
        /// chord is already spoken for the row is created without one - inert and visible in the table,
        /// which is honest, rather than silently stealing a hotkey another feature owns.
        /// </summary>
        /// <summary>
        /// The whole first-enable decision in one testable place: add the starter row when the feature
        /// has just been switched on and has never been offered one, giving it the default chord only
        /// if <paramref name="chordFree"/> says nothing else owns it. Returns true when a row was added.
        /// </summary>
        internal static bool SeedTranslateRow(AppConfig config, Func<Hotkey, bool> chordFree)
        {
            if (!config.EnableTranslate) return false;
            if (!NeedsTranslateRow(config.TranslateSeeded, config.TranslateProfiles.Count)) return false;

            config.TranslateSeeded = true;
            config.TranslateProfiles.Add(TranslateRow(chordFree(Hotkey.Parse(DefaultTranslateHotkey))));
            return true;
        }

        internal static TranslationProfile TranslateRow(bool chordAvailable)
            => new TranslationProfile
            {
                TargetLang = TranslationLanguages.UiToken,
                Hotkey = chordAvailable ? DefaultTranslateHotkey : "",
                Enabled = true,
            };

        /// <summary>Drop the two values the conversion table replaced, once their content has moved into it.</summary>
        private static void DropLegacyFlipValues()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
                if (key == null) return;
                key.DeleteValue("Hotkey", throwOnMissingValue: false);
                key.DeleteValue("EnableFlipHotkey", throwOnMissingValue: false);
            }
            catch { /* best effort - a leftover value is inert either way */ }
        }

        private static string? ResolveJsonPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string roamed = Path.Combine(appData, "CyrFlip", "config.json");
            if (File.Exists(roamed)) return roamed;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        private static bool GetBool(RegistryKey key, string name, bool def)
        {
            var val = key.GetValue(name);
            return val == null ? def : Convert.ToInt32(val) != 0;
        }

        private static int GetInt(RegistryKey key, string name, int def)
        {
            var val = key.GetValue(name);
            return val == null ? def : Convert.ToInt32(val);
        }

        private static List<LayoutConversionProfile> ReadProfiles(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<LayoutConversionProfile>();
            try
            {
                var profiles = new JavaScriptSerializer().Deserialize<List<LayoutConversionProfile>>(json);
                if (profiles == null) return new List<LayoutConversionProfile>();
                foreach (LayoutConversionProfile profile in profiles)
                    if (string.IsNullOrEmpty(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                return profiles;
            }
            catch { return new List<LayoutConversionProfile>(); }
        }

        /// <summary>
        /// The stored translation table. Malformed JSON is an empty table, never an exception - and a
        /// row that arrived without an id (hand-edited registry, or an older shape) gets one, because
        /// the id is what the hook hands back when the chord fires.
        /// </summary>
        internal static List<TranslationProfile> ReadTranslationProfiles(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<TranslationProfile>();
            try
            {
                var profiles = new JavaScriptSerializer().Deserialize<List<TranslationProfile>>(json);
                if (profiles == null) return new List<TranslationProfile>();
                foreach (TranslationProfile profile in profiles)
                    if (string.IsNullOrEmpty(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                return profiles;
            }
            catch { return new List<TranslationProfile>(); }
        }
    }
}
