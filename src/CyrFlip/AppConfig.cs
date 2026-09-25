using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>
        /// The default model. <b>Chosen by measurement, not by size</b> (2026-07-28, live Ollama): the
        /// previous default <c>qwen2.5:3b</c> put <i>Chinese characters into a Russian translation</i> -
        /// against an explicit instruction in the prompt not to - and answered Ukrainian with gibberish.
        /// <c>gemma2:2b</c> was no better on Ukrainian. Every small model tested failed the same way, so
        /// there is no ~2 GB option that clears the bar; <c>aya-expanse:8b</c> is the smallest that does.
        ///
        /// <para>The trade is honest and deliberate: ~4.7 GB to download once, and roughly 3 s per
        /// translation on the dev machine. The RAM belongs to the Ollama process, not to CyrFlip.
        /// Changing this only affects a fresh install - an existing user keeps the model they saved.</para>
        /// </summary>
        public const string DefaultTranslateModel = "aya-expanse:8b";
        /// <summary>
        /// The chord that opens a new quick note. Not another Ctrl+Shift+F-key: the quick notes are
        /// reached far more often than any of the F-key chords and from inside an editor.
        /// <para>Not Ctrl+Alt+N any more (ticket S0004, KC-5): Ctrl+Alt is how Windows spells AltGr,
        /// and AltGr+N types "ń" on Polish (Programmers). Not Ctrl+Shift+N either - Chromium's
        /// incognito window and Explorer's new folder. A stored Ctrl+Alt+N is left alone: the hook
        /// no longer reads AltGr as Ctrl+Alt, so it has stopped eating the character.</para>
        /// </summary>
        public const string DefaultQuickNotesHotkey = "Ctrl+Shift+Alt+N";
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
        /// <summary>
        /// Convert a key that is punctuation in <b>both</b> layouts ("/" against ".", "@" against "«").
        /// Such a key carries no evidence of which layout the user meant, so it is the one part of the
        /// conversion that is a preference rather than a fact - default <b>on</b>, which is how every
        /// release before this switch behaved. Off, "/ghbdtn" becomes "/привет" instead of ".привет",
        /// which is what a user who types slashes, paths or numpad symbols on purpose wants. A key whose
        /// other side is a <i>letter</i> ("," → "б", "[" → "х") is never ambiguous and ignores this.
        /// </summary>
        public bool ConvertSymbols { get; set; } = true;
        /// <summary>
        /// The two keep-awake switches (see <see cref="KeepAwake"/>). Persisted since 2026-07-28, on the
        /// user's second call: a switch that silently forgets itself on every launch reads as broken.
        /// The original spec (§5) kept them in memory only, to guarantee a forgotten switch can never
        /// keep a laptop warm in a bag - that guarantee is now the user's to hold, and the tray tooltip
        /// is what says the mode is live.
        /// </summary>
        public bool KeepSystemAwake { get; set; } = false;
        public bool KeepScreenOn { get; set; } = false;
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
        /// CyrFlip's own context menu over the selected text. Off by default: while false the
        /// low-level mouse hook is not installed at all, so a feature nobody asked for never sits in
        /// the path of every mouse move on the machine (see <see cref="MouseHook"/>).
        /// </summary>
        public bool EnableContextMenu { get; set; } = false;
        /// <summary>
        /// The chord that opens it, as an invariant token (see <see cref="MouseChord"/>). Ctrl + right
        /// click by default - Shift + right click is the shell's own extended menu and the bare middle
        /// button is autoscroll, so neither may be taken without the user saying so.
        /// </summary>
        public string ContextMenuChord { get; set; } = MouseChord.Default.Token;
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
        /// <summary>
        /// "My language" - the one the user writes in, used by the two fixed-pair rows. Empty follows
        /// the UI language, which is right for almost everyone; it is a setting of its own so that an
        /// English interface and a Russian keyboard can coexist.
        /// </summary>
        public string TranslateSourceLang { get; set; } = "";
        /// <summary>The language the user wants text in. Empty means English.</summary>
        public string TranslateTargetLang { get; set; } = "en";
        /// <summary>Empty means <see cref="OllamaClient.DefaultEndpoint"/> (localhost).</summary>
        public string TranslateEndpoint { get; set; } = "";
        /// <summary>Empty means "use whichever model is installed" (see TranslationService).</summary>
        public string TranslateModel { get; set; } = DefaultTranslateModel;
        public int TranslateTimeoutSeconds { get; set; } = 120;
        /// <summary>
        /// How long Ollama keeps the model in RAM after a translation. <b>-1 = forever</b> and is the
        /// default: a cold load costs minutes on a machine without a usable GPU, so unloading after a
        /// few idle minutes is what makes the next translation feel broken. 0 unloads at once.
        /// </summary>
        public int TranslateKeepAliveMinutes { get; set; } = -1;
        public bool TranslateAutoStartServer { get; set; } = true;
        /// <summary>Put the translation on the clipboard - where the history picks it up as usual.</summary>
        public bool TranslateCopyResult { get; set; } = false;
        public bool TranslatePasteResult { get; set; } = false;
        public bool TranslateShowSource { get; set; } = false;
        /// <summary>Seconds before the result window closes itself; 0 = never.</summary>
        public int TranslateWindowTimeout { get; set; } = 0;
        /// <summary>
        /// Roomy on purpose: the popup shows a paragraph of translated text next to the pointer, and
        /// the first build's 460×260 had people scrolling to read three sentences.
        /// </summary>
        public const int DefaultTranslateWindowWidth = 700;
        public const int DefaultTranslateWindowHeight = 460;
        public int TranslateWindowWidth { get; set; } = DefaultTranslateWindowWidth;
        public int TranslateWindowHeight { get; set; } = DefaultTranslateWindowHeight;
        /// <summary>Opacity percentage for the result window (30..100).</summary>
        public int TranslateWindowOpacity { get; set; } = 100;
        /// <summary>
        /// The local quick notes (spec §5.3). Off by default and explained on the first enable: the
        /// notes are a place people put tokens and internal code, so the feature says what it does
        /// with them before it starts holding any.
        /// </summary>
        public bool EnableQuickNotes { get; set; } = false;
        /// <summary>One-time marker: the privacy notice was shown when the feature was first enabled.</summary>
        public bool QuickNotesNoticeShown { get; set; } = false;
        public string QuickNotesHotkey { get; set; } = DefaultQuickNotesHotkey;
        public bool EnableQuickNotesHotkey { get; set; } = true;
        /// <summary>
        /// Off by default, which is the opposite of what a notepad usually does - and deliberate:
        /// the body is as often code as prose, and a wrapped line of code is a line you have to
        /// mentally un-wrap before you can read it (spec §3.4).
        /// </summary>
        public bool QuickNotesWordWrap { get; set; } = false;
        public int QuickNotesX { get; set; } = int.MinValue;
        public int QuickNotesY { get; set; } = int.MinValue;
        // 0 = never sized by the user, so the window opens at its own measured minimum - the size
        // at which every caption fits in that language at that display scaling. A pair of constants
        // here could only ever be right for one of the 13 languages and one scaling.
        public int QuickNotesWidth { get; set; }
        public int QuickNotesHeight { get; set; }
        /// <summary>The note the window was last left on, so it reopens where the user was.</summary>
        public string QuickNotesSelected { get; set; } = "";
        public int FlipCount { get; set; } = 0;
        public int CaseFlipCount { get; set; } = 0;
        public int TranslateCount { get; set; } = 0;
        public int QuickNoteCount { get; set; } = 0;

        /// <summary>
        /// UI language default for a fresh install (no saved value): follow the OS UI language when
        /// CyrFlip is translated into it, else English (see <see cref="Localization.DefaultLanguage"/>).
        /// Fixes a Russian UI appearing on an English OS.
        /// </summary>
        private static string DefaultUiLanguage() => Localization.DefaultLanguage();

        public static AppConfig Load()
        {
            AppConfig cfg;
            // True when the conversion table had to be created rather than read - a fresh install or a
            // config written before the table existed - or when a stored table had to be repaired.
            // Persisted below, once the registry key is closed.
            bool mustSave;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath);
                cfg = LoadFrom(key == null ? null : new RegistryConfigKey(key), out mustSave);
            }
            catch
            {
                cfg = new AppConfig();
                mustSave = false;
            }

            // Outside the try/using: the seeded row has to reach the registry (so the next launch reads
            // it instead of seeding again), and the superseded values are dropped in the same pass.
            if (mustSave)
            {
                cfg.Save();
                DropLegacyFlipValues();
            }
            return cfg;
        }

        /// <summary>
        /// Registry values that were present but could not be read - wrong kind, unparsable number,
        /// malformed JSON. Each one fell back to its default <b>alone</b> (ticket S0007, CF-1): one bad
        /// value used to abort the whole load and leave every later field at its default, after which
        /// the next save wiped the conversion table and the one-time Windows snapshots for good.
        /// </summary>
        public IReadOnlyCollection<string> UnreadableValues => _unreadable;
        private readonly HashSet<string> _unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tables and backups that were unreadable, with the serialized fallback they were loaded as.
        /// <see cref="SaveTo"/> leaves the raw registry value alone for as long as the in-memory value
        /// still serializes to that fallback - i.e. until the user actually edits that table.
        /// </summary>
        private readonly Dictionary<string, string> _preserveRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The whole read, against a registry seam (null = no key yet, i.e. a first run).</summary>
        internal static AppConfig LoadFrom(IConfigKey? key, out bool mustSave)
        {
            var cfg = new AppConfig();
            bool seeded = false;
            bool repaired = false;
            {
                if (key == null)
                {
                    cfg.LayoutConversionProfiles.Add(FlipRow(cfg.MigrateFromJson(), true));
                    seeded = true;
                }
                else
                {
                    cfg.CaseHotkey = cfg.ReadString(key, "CaseHotkey", cfg.CaseHotkey);
                    cfg.ClipboardHistoryHotkey = cfg.ReadString(key, "ClipboardHistoryHotkey", cfg.ClipboardHistoryHotkey);
                    cfg.UiLanguage = cfg.ReadString(key, "UiLanguage", cfg.UiLanguage);
                    cfg.SettingsTab = Math.Max(0, cfg.ReadInt(key, "SettingsTab", cfg.SettingsTab));
                    cfg.EnableClipboardHistory = cfg.ReadBool(key, "EnableClipboardHistory", cfg.EnableClipboardHistory);
                    cfg.PauseClipboardHistory = cfg.ReadBool(key, "PauseClipboardHistory", cfg.PauseClipboardHistory);
                    cfg.ShowClipboardHistoryOnStartup = cfg.ReadBool(key, "ShowClipboardHistoryOnStartup", cfg.ShowClipboardHistoryOnStartup);
                    cfg.ClipboardHistoryX = cfg.ReadInt(key, "ClipboardHistoryX", cfg.ClipboardHistoryX);
                    cfg.ClipboardHistoryY = cfg.ReadInt(key, "ClipboardHistoryY", cfg.ClipboardHistoryY);
                    cfg.ClipboardHistoryWidth = cfg.ReadInt(key, "ClipboardHistoryWidth", cfg.ClipboardHistoryWidth);
                    cfg.ClipboardHistoryHeight = cfg.ReadInt(key, "ClipboardHistoryHeight", cfg.ClipboardHistoryHeight);
                    cfg.ClipboardHistoryOpacity = Math.Max(30, Math.Min(100, cfg.ReadInt(key, "ClipboardHistoryOpacity", cfg.ClipboardHistoryOpacity)));
                    // Every number is clamped to what its UI (or its consumer) can take (CF-1): a cursor
                    // of 0 px cannot be drawn, and a timeout past ~24.8 days overflows Timer.Interval.
                    cfg.CursorSize = Math.Max(12, Math.Min(128, cfg.ReadInt(key, "CursorSize", cfg.CursorSize)));
                    cfg.EnableCursorChange = cfg.ReadBool(key, "EnableCursorChange", cfg.EnableCursorChange);
                    cfg.EnableCaretOverlay = cfg.ReadBool(key, "EnableCaretOverlay", cfg.EnableCaretOverlay);
                    cfg.CaretDotMode = cfg.ReadBool(key, "CaretDotMode", cfg.CaretDotMode);
                    cfg.EnableLanguageSwitch = cfg.ReadBool(key, "EnableLanguageSwitch", cfg.EnableLanguageSwitch);
                    cfg.ConvertSymbols = cfg.ReadBool(key, "ConvertSymbols", cfg.ConvertSymbols);
                    cfg.FlipCapsLockAfter = cfg.ReadBool(key, "FlipCapsLockAfter", cfg.FlipCapsLockAfter);
                    cfg.KeepSystemAwake = cfg.ReadBool(key, "KeepSystemAwake", cfg.KeepSystemAwake);
                    cfg.KeepScreenOn = cfg.ReadBool(key, "KeepScreenOn", cfg.KeepScreenOn);
                    cfg.EnableHotkeys = cfg.ReadBool(key, "EnableHotkeys", cfg.EnableHotkeys);
                    cfg.EnableCaseHotkey = cfg.ReadBool(key, "EnableCaseHotkey", cfg.EnableCaseHotkey);
                    cfg.EnableHistoryHotkey = cfg.ReadBool(key, "EnableHistoryHotkey", cfg.EnableHistoryHotkey);
                    cfg.DeferToRemoteDesktop = cfg.ReadBool(key, "DeferToRemoteDesktop", cfg.DeferToRemoteDesktop);
                    cfg.EnableContextMenu = cfg.ReadBool(key, "EnableContextMenu", cfg.EnableContextMenu);
                    // Parse, not the raw string: a hand-edited or corrupt token must land on the
                    // default rather than on something that swallows every context menu in Windows.
                    cfg.ContextMenuChord = MouseChord.Parse(cfg.ReadString(key, "ContextMenuChord", "")).Token;
                    // The one-time Windows snapshots cannot be taken again: an unreadable one is kept
                    // on disk untouched rather than replaced by "" on the next save.
                    cfg.LanguageHotkeysBackup = cfg.ReadString(key, "LanguageHotkeysBackup", cfg.LanguageHotkeysBackup, preserve: true);
                    cfg.InputLayoutsBackup = cfg.ReadString(key, "InputLayoutsBackup", cfg.InputLayoutsBackup, preserve: true);
                    cfg.EnableScenarioLauncher = cfg.ReadBool(key, "EnableScenarioLauncher", cfg.EnableScenarioLauncher);
                    cfg.LauncherFirstEnableDone = cfg.ReadBool(key, "LauncherFirstEnableDone", cfg.LauncherFirstEnableDone);

                    cfg.EnableTranslate = cfg.ReadBool(key, "EnableTranslate", cfg.EnableTranslate);
                    cfg.TranslateSeeded = cfg.ReadBool(key, "TranslateSeeded", cfg.TranslateSeeded);
                    cfg.TranslateProfiles = cfg.ReadTable<TranslationProfile>(key, "TranslateProfiles", SanitizeTranslationProfiles, ref repaired);
                    cfg.TranslateSourceLang = cfg.ReadString(key, "TranslateSourceLang", cfg.TranslateSourceLang);
                    cfg.TranslateTargetLang = cfg.ReadString(key, "TranslateTargetLang", cfg.TranslateTargetLang);
                    cfg.TranslateEndpoint = cfg.ReadString(key, "TranslateEndpoint", cfg.TranslateEndpoint);
                    cfg.TranslateModel = cfg.ReadString(key, "TranslateModel", cfg.TranslateModel);
                    // The ranges the translator tab's spin boxes offer.
                    cfg.TranslateTimeoutSeconds = Math.Max(5, Math.Min(600, cfg.ReadInt(key, "TranslateTimeoutSeconds", cfg.TranslateTimeoutSeconds)));
                    // -1 (forever) is a legitimate value, so the floor is -1 and not 0.
                    cfg.TranslateKeepAliveMinutes = Math.Max(-1, Math.Min(120, cfg.ReadInt(key, "TranslateKeepAliveMinutes", cfg.TranslateKeepAliveMinutes)));
                    cfg.TranslateAutoStartServer = cfg.ReadBool(key, "TranslateAutoStartServer", cfg.TranslateAutoStartServer);
                    cfg.TranslateCopyResult = cfg.ReadBool(key, "TranslateCopyResult", cfg.TranslateCopyResult);
                    cfg.TranslatePasteResult = cfg.ReadBool(key, "TranslatePasteResult", cfg.TranslatePasteResult);
                    cfg.TranslateShowSource = cfg.ReadBool(key, "TranslateShowSource", cfg.TranslateShowSource);
                    cfg.TranslateWindowTimeout = Math.Max(0, Math.Min(600, cfg.ReadInt(key, "TranslateWindowTimeout", cfg.TranslateWindowTimeout)));
                    cfg.TranslateWindowWidth = cfg.ReadInt(key, "TranslateWindowWidth", cfg.TranslateWindowWidth);
                    cfg.TranslateWindowHeight = cfg.ReadInt(key, "TranslateWindowHeight", cfg.TranslateWindowHeight);
                    cfg.TranslateWindowOpacity = Math.Max(30, Math.Min(100, cfg.ReadInt(key, "TranslateWindowOpacity", cfg.TranslateWindowOpacity)));

                    bool profilesPresent = Has(key, "LayoutConversionProfiles");
                    string? legacyHotkey = cfg.ReadOptionalString(key, "Hotkey", null);
                    bool legacyPresent = Has(key, "Hotkey") || Has(key, "EnableFlipHotkey");
                    cfg.LayoutConversionProfiles = cfg.ReadTable<LayoutConversionProfile>(key, "LayoutConversionProfiles", SanitizeConversionProfiles, ref repaired);
                    // A table that is there but unreadable is never "missing": seeding would save over it.
                    seeded = !cfg._unreadable.Contains("LayoutConversionProfiles")
                        && NeedsFlipRow(profilesPresent ? "" : null, legacyPresent, cfg.LayoutConversionProfiles.Count);
                    if (seeded)
                        cfg.LayoutConversionProfiles.Insert(0,
                            FlipRow(legacyHotkey, cfg.ReadBool(key, "EnableFlipHotkey", true)));

                    cfg.EnableQuickNotes = cfg.ReadBool(key, "EnableQuickNotes", cfg.EnableQuickNotes);
                    cfg.QuickNotesNoticeShown = cfg.ReadBool(key, "QuickNotesNoticeShown", cfg.QuickNotesNoticeShown);
                    cfg.QuickNotesHotkey = cfg.ReadString(key, "QuickNotesHotkey", cfg.QuickNotesHotkey);
                    cfg.EnableQuickNotesHotkey = cfg.ReadBool(key, "EnableQuickNotesHotkey", cfg.EnableQuickNotesHotkey);
                    cfg.QuickNotesWordWrap = cfg.ReadBool(key, "QuickNotesWordWrap", cfg.QuickNotesWordWrap);
                    cfg.QuickNotesX = cfg.ReadInt(key, "QuickNotesX", cfg.QuickNotesX);
                    cfg.QuickNotesY = cfg.ReadInt(key, "QuickNotesY", cfg.QuickNotesY);
                    cfg.QuickNotesWidth = cfg.ReadInt(key, "QuickNotesWidth", cfg.QuickNotesWidth);
                    cfg.QuickNotesHeight = cfg.ReadInt(key, "QuickNotesHeight", cfg.QuickNotesHeight);
                    cfg.QuickNotesSelected = cfg.ReadString(key, "QuickNotesSelected", cfg.QuickNotesSelected);

                    cfg.FlipCount = cfg.ReadInt(key, "FlipCount", cfg.FlipCount);
                    cfg.CaseFlipCount = cfg.ReadInt(key, "CaseFlipCount", cfg.CaseFlipCount);
                    cfg.TranslateCount = cfg.ReadInt(key, "TranslateCount", cfg.TranslateCount);
                    cfg.QuickNoteCount = cfg.ReadInt(key, "QuickNoteCount", cfg.QuickNoteCount);
                }
            }
            mustSave = seeded || repaired;
            return cfg;
        }

        public void Save()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key != null) SaveTo(new RegistryConfigKey(key));
            }
            catch { /* best effort */ }
        }

        internal void SaveTo(IConfigKey key)
        {
            {
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
                key.SetValue("ConvertSymbols", ConvertSymbols ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("FlipCapsLockAfter", FlipCapsLockAfter ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("KeepSystemAwake", KeepSystemAwake ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("KeepScreenOn", KeepScreenOn ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableHotkeys", EnableHotkeys ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableCaseHotkey", EnableCaseHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableHistoryHotkey", EnableHistoryHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DeferToRemoteDesktop", DeferToRemoteDesktop ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableContextMenu", EnableContextMenu ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ContextMenuChord", ContextMenuChord, RegistryValueKind.String);
                WritePreserved(key, "LanguageHotkeysBackup", LanguageHotkeysBackup);
                WritePreserved(key, "InputLayoutsBackup", InputLayoutsBackup);
                key.SetValue("EnableScenarioLauncher", EnableScenarioLauncher ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("LauncherFirstEnableDone", LauncherFirstEnableDone ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableTranslate", EnableTranslate ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateSeeded", TranslateSeeded ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("TranslateSourceLang", TranslateSourceLang, RegistryValueKind.String);
                key.SetValue("TranslateTargetLang", TranslateTargetLang, RegistryValueKind.String);
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
                WritePreserved(key, "LayoutConversionProfiles", new JavaScriptSerializer().Serialize(LayoutConversionProfiles));
                WritePreserved(key, "TranslateProfiles", new JavaScriptSerializer().Serialize(TranslateProfiles));
                key.SetValue("EnableQuickNotes", EnableQuickNotes ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesNoticeShown", QuickNotesNoticeShown ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHotkey", QuickNotesHotkey, RegistryValueKind.String);
                key.SetValue("EnableQuickNotesHotkey", EnableQuickNotesHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWordWrap", QuickNotesWordWrap ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesX", QuickNotesX, RegistryValueKind.DWord);
                key.SetValue("QuickNotesY", QuickNotesY, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWidth", QuickNotesWidth, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHeight", QuickNotesHeight, RegistryValueKind.DWord);
                key.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
                key.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
                key.SetValue("CaseFlipCount", CaseFlipCount, RegistryValueKind.DWord);
                key.SetValue("TranslateCount", TranslateCount, RegistryValueKind.DWord);
                key.SetValue("QuickNoteCount", QuickNoteCount, RegistryValueKind.DWord);
            }
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

        /// <summary>Increment the quick-notes counter and persist only that value (cheap write).</summary>
        public void IncrementQuickNoteCount()
        {
            QuickNoteCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("QuickNoteCount", QuickNoteCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>
        /// Remember the window geometry and which note was open, without rewriting forty registry
        /// values - the window saves this on every move and resize, and a full <see cref="Save"/>
        /// there would serialize both profile tables on each drag.
        /// </summary>
        public void SaveQuickNotesWindow()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("QuickNotesX", QuickNotesX, RegistryValueKind.DWord);
                key.SetValue("QuickNotesY", QuickNotesY, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWidth", QuickNotesWidth, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHeight", QuickNotesHeight, RegistryValueKind.DWord);
                key.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
            }
            catch { }
        }

        /// <summary>
        /// Remember which note is open - that one value alone. The window calls it whenever the
        /// selection changes, not only when the window is moved (ticket S0006, QN-9).
        /// </summary>
        public void SaveQuickNotesSelected()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
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

        private static bool Has(IConfigKey key, string name)
        {
            try { return key.GetValue(name) != null; }
            catch { return true; } // there, but unreadable - never "absent"
        }

        private object? Raw(IConfigKey key, string name)
        {
            try { return key.GetValue(name); }
            catch { _unreadable.Add(name); return null; }
        }

        /// <summary>
        /// One number, read on its own: a DWORD, a QWORD that fits, or a string that parses. Anything
        /// else (REG_BINARY, REG_MULTI_SZ, <c>""</c>, <c>"true"</c>) is that value's default and is
        /// recorded in <see cref="UnreadableValues"/> - it no longer costs the rest of the config.
        /// </summary>
        internal int ReadInt(IConfigKey key, string name, int def)
        {
            object? val = Raw(key, name);
            if (val == null) return def;
            if (val is int i) return i;
            if (val is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
            if (val is string s && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            _unreadable.Add(name);
            return def;
        }

        internal bool ReadBool(IConfigKey key, string name, bool def)
        {
            object? val = Raw(key, name);
            if (val is string s && bool.TryParse(s.Trim(), out bool flag)) return flag;
            if (val == null) return def;
            const int unset = int.MinValue;
            int number = ReadInt(key, name, unset);
            return number == unset ? def : number != 0;
        }

        /// <summary>
        /// One string. With <paramref name="preserve"/>, a value that is there but is not a string is
        /// also left on disk untouched by <see cref="SaveTo"/> until the in-memory value changes.
        /// </summary>
        internal string ReadString(IConfigKey key, string name, string def, bool preserve = false)
            => ReadOptionalString(key, name, def, preserve) ?? def;

        internal string? ReadOptionalString(IConfigKey key, string name, string? def, bool preserve = false)
        {
            object? val = Raw(key, name);
            if (val is string s) return s;
            if (val != null)
            {
                _unreadable.Add(name);
                if (preserve) _preserveRaw[name] = def ?? "";
            }
            return def;
        }

        /// <summary>
        /// One JSON table. Absent is an empty table; present but unreadable (not a string, malformed
        /// JSON) is an empty table <b>that is not saved over</b> until the user edits it. Rows are
        /// sanitized (<paramref name="sanitize"/>); a repair sets <paramref name="repaired"/> so the
        /// repaired form is written back.
        /// </summary>
        internal List<T> ReadTable<T>(IConfigKey key, string name, Func<List<T>?, bool> sanitize, ref bool repaired) where T : class
        {
            object? val = Raw(key, name);
            if (val == null && !_unreadable.Contains(name)) return new List<T>();
            List<T>? rows = null;
            if (val is string json)
            {
                if (json.Length == 0) return new List<T>();
                try { rows = new JavaScriptSerializer().Deserialize<List<T>>(json); }
                catch { rows = null; }
            }
            if (rows == null)
            {
                _unreadable.Add(name);
                _preserveRaw[name] = new JavaScriptSerializer().Serialize(new List<T>());
                return new List<T>();
            }
            if (sanitize(rows)) repaired = true;
            return rows;
        }

        /// <summary>A table or backup that was unreadable is written only once it differs from its fallback.</summary>
        private void WritePreserved(IConfigKey key, string name, string value)
        {
            if (_preserveRaw.TryGetValue(name, out string? fallback))
            {
                if (value == fallback) return;
                _preserveRaw.Remove(name);
                _unreadable.Remove(name);
            }
            key.SetValue(name, value, RegistryValueKind.String);
        }

        /// <summary>
        /// Makes a deserialized conversion table safe to use (ticket S0007, CF-2): a <c>null</c> row is
        /// dropped, a <c>null</c> string becomes <c>""</c> (a hand-edited <c>"Hotkey": null</c> used to
        /// crash every start), and a missing or duplicate id is regenerated - with a duplicate id the
        /// chord of the second row ran the first row. Returns true when anything changed.
        /// </summary>
        internal static bool SanitizeConversionProfiles(List<LayoutConversionProfile>? rows)
        {
            if (rows == null) return false;
            bool changed = rows.RemoveAll(r => r == null) > 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutConversionProfile row in rows)
            {
                if (row.SourceKlid == null) { row.SourceKlid = ""; changed = true; }
                if (row.TargetKlid == null) { row.TargetKlid = ""; changed = true; }
                if (row.Hotkey == null) { row.Hotkey = ""; changed = true; }
                if (string.IsNullOrEmpty(row.Id) || !ids.Add(row.Id)) { row.Id = NewId(ids); changed = true; }
            }
            return changed;
        }

        /// <summary>The translation table's counterpart of <see cref="SanitizeConversionProfiles"/>.</summary>
        internal static bool SanitizeTranslationProfiles(List<TranslationProfile>? rows)
        {
            if (rows == null) return false;
            bool changed = rows.RemoveAll(r => r == null) > 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TranslationProfile row in rows)
            {
                if (row.TargetLang == null) { row.TargetLang = ""; changed = true; }
                if (row.Hotkey == null) { row.Hotkey = ""; changed = true; }
                if (string.IsNullOrEmpty(row.Id) || !ids.Add(row.Id)) { row.Id = NewId(ids); changed = true; }
            }
            return changed;
        }

        private static string NewId(HashSet<string> taken)
        {
            string id = Guid.NewGuid().ToString("N");
            taken.Add(id);
            return id;
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
                SanitizeTranslationProfiles(profiles);
                return profiles;
            }
            catch { return new List<TranslationProfile>(); }
        }
    }

    /// <summary>
    /// The registry key <see cref="AppConfig"/> reads and writes, as a seam: the real
    /// <see cref="RegistryConfigKey"/> in the app, an in-memory one in <c>AppConfigLoadTests</c>.
    /// </summary>
    internal interface IConfigKey
    {
        object? GetValue(string name);
        void SetValue(string name, object value, RegistryValueKind kind);
    }

    internal sealed class RegistryConfigKey : IConfigKey
    {
        private readonly RegistryKey _key;
        public RegistryConfigKey(RegistryKey key) { _key = key; }
        public object? GetValue(string name) => _key.GetValue(name);
        public void SetValue(string name, object value, RegistryValueKind kind) => _key.SetValue(name, value, kind);
    }
}
