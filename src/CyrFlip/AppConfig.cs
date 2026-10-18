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
        /// <summary>
        /// The region capture's chord (S0026, re-keyed by S0071). PrintScreen is the key people
        /// associate with screenshots, but the shipped Ctrl+PrintScreen "never fired" for the owner -
        /// Windows' own handling of the key is the suspect - so the shipped capture defaults moved to
        /// the F-key family every other CyrFlip chord uses and the hook provably intercepts.
        /// <see cref="Hotkey"/> still parses PrintScreen/PrtSc, so a user may bind it by hand.
        /// </summary>
        public const string DefaultScreenshotHotkey = "Ctrl+F12";
        /// <summary>
        /// The instant all-displays capture (S0071): every monitor as one picture, no selection.
        /// Bare Ctrl+F10..F12 hold folder, full and area capture in one row; the case flip keeps
        /// Ctrl+Shift+F11 and the seeded EN ⇄ RU row keeps Ctrl+Shift+F12 - nothing is displaced.
        /// </summary>
        public const string DefaultScreenshotFullHotkey = "Ctrl+F11";
        /// <summary>
        /// Draw on an area capture (S0071). The first shipped chord on the Ctrl+Alt plane: the hook's
        /// <see cref="ChordMatcher"/> tells AltGr (a fake LCtrl followed by RAlt) from a real Ctrl, so
        /// typists on AltGr layouts cannot fire it by typing, and no layout maps an F-key into a character.
        /// </summary>
        public const string DefaultScreenshotAnnotateAreaHotkey = "Ctrl+Alt+F12";
        /// <summary>Draw on the full capture (S0071), beside the area one on the Ctrl+Alt plane.</summary>
        public const string DefaultScreenshotFullAnnotateHotkey = "Ctrl+Alt+F11";
        /// <summary>The capture chord profiles carried before the S0071 re-key; the one-time migration compares against this, never a literal.</summary>
        internal const string LegacyScreenshotHotkey = "Ctrl+PrintScreen";
        /// <summary>
        /// Opens the folder the captures are saved into (S0057), re-keyed by S0071: the old
        /// Ctrl+Shift+PrintScreen had the same dead-key problem as the capture chord.
        /// </summary>
        public const string DefaultScreenshotFolderHotkey = "Ctrl+F10";
        /// <summary>The folder chord profiles carried before the S0071 re-key; the migration compares against this.</summary>
        internal const string LegacyScreenshotFolderHotkey = "Ctrl+Shift+PrintScreen";
        /// <summary>
        /// Opens the text context menu at the caret (S0045 K2): M for menu, in the quick notes' family
        /// (Ctrl+Shift+Alt+N) - Ctrl+Shift+F10, the Shift+F10 mnemonic, is the clipboard history's. Bound
        /// only while the context menu is on.
        /// </summary>
        public const string DefaultTextMenuHotkey = "Ctrl+Shift+Alt+M";
        /// <summary>Default hotkey for the symbols palette (ticket S0056 section 9).</summary>
        public const string DefaultSymbolsHotkey = "Ctrl+Shift+F9";
        /// <summary>
        /// Default hotkey of the traffic light board (ticket S0069 D11): T for the board, in the quick notes'
        /// and the text menu's Ctrl+Shift+Alt family - free among every shipped default.
        /// </summary>
        public const string DefaultTrafficLightHotkey = "Ctrl+Shift+Alt+T";
        internal const string UsKlid = "00000409";
        internal const string RussianKlid = "00000419";
        /// <summary>The case flip's shipped chord (<c>Hotkey.CaseDefault</c>) as the token the registry stores.</summary>
        public const string DefaultCaseHotkey = "Ctrl+Shift+F11";
        /// <summary>The clipboard history's shipped chord.</summary>
        public const string DefaultHistoryHotkey = "Ctrl+Shift+F10";

        public string CaseHotkey { get; set; } = DefaultCaseHotkey;
        public string ClipboardHistoryHotkey { get; set; } = DefaultHistoryHotkey;
        public string UiLanguage { get; set; } = DefaultUiLanguage();
        /// <summary>
        /// Index of the settings tab the window was last left on, so reopening it comes back to the
        /// page the user was working on instead of always to "Общие". Clamped against the live page
        /// count when applied - the tab strip grows between versions.
        /// </summary>
        public int SettingsTab { get; set; } = 0;
        /// <summary>
        /// The settings page the window was last left on, by its stable <see cref="SettingsPageKey"/>
        /// name rather than the tab's position (S0060 P5, M6): the strip grows between versions, and a
        /// remembered index silently changes meaning. Empty = the fallback index of an older build.
        /// </summary>
        public string SettingsPageId { get; set; } = "";
        /// <summary>
        /// Expansion of the settings window's collapsible groups (WINDOWS-UI section 3): "id=0|1" pairs
        /// joined by ";", the id being the group's stable internal string - never a translated caption.
        /// Unknown ids are dropped on read, so a page that retired a group leaves nothing behind.
        /// </summary>
        public string SettingsGroupsState { get; set; } = "";
        /// <summary>
        /// Where each settings page was scrolled to, as "pageIndex|anchorId|offsetLogical": the anchor
        /// is a group id or a stable row ordinal inside that page, the offset the distance from the
        /// anchor's top to the viewport top in logical (96-DPI) pixels, so a DPI change does not lose
        /// the place. One row per remembered page, rows joined by ";".
        /// </summary>
        public string SettingsViewport { get; set; } = "";
        /// <summary>
        /// The settings window's own rectangle (WINDOWS-UI section 5: retain the user's normal window
        /// rectangle; APP-BEHAVIOUR rule 10: written on close, restored clamped to a work area, never
        /// overwritten by a maximized state). Width/Height 0 = never user-sized; X/Y
        /// <see cref="int.MinValue"/> = never placed.
        /// </summary>
        public int SettingsWindowX { get; set; } = int.MinValue;
        public int SettingsWindowY { get; set; } = int.MinValue;
        public int SettingsWindowWidth { get; set; } = 0;
        public int SettingsWindowHeight { get; set; } = 0;
        public bool EnableClipboardHistory { get; set; } = false;
        /// <summary>One-time marker: the recording notice was shown when the feature was first enabled (S0067, CU-2-06).</summary>
        public bool ClipboardHistoryNoticeShown { get; set; } = false;
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
        /// <summary>One-time marker: the system-wide I-beam notice was shown when the marker was first enabled (S0067, CU-2-08).</summary>
        public bool CursorNoticeShown { get; set; } = false;
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
        /// The keyboard's way to the same menu (S0045 K2, <c>INPUT-PARITY</c> rule 1): opens it at the
        /// caret. Bound only while <see cref="EnableContextMenu"/> and its own switch are both on.
        /// </summary>
        public string TextMenuHotkey { get; set; } = DefaultTextMenuHotkey;
        public bool EnableTextMenuHotkey { get; set; } = true;
        /// <summary>
        /// The app's theme as an invariant token - <c>system</c> (follow Windows), <c>light</c> or
        /// <c>dark</c> (see <see cref="ThemeModes"/>, ticket S0020). Anything unreadable is kept as
        /// <c>system</c>, the one value that cannot be wrong.
        /// </summary>
        public string Theme { get; set; } = ThemeModes.SystemToken;
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
        /// The <c>InputMethodOverride</c> CyrFlip last wrote through "По умолчанию"; empty = none. A
        /// backup taken before ticket S0007 (format 1) never captured that value, so restoring it removes
        /// the value only while it still equals this one (ticket S0007, WL-13).
        /// </summary>
        public string InputMethodOverrideWritten { get; set; } = "";
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
        /// One-time marker for the Store build's move out of the machine-wide %ProgramData%\CyrFlip
        /// (ticket S0016, data folder move): set once a migration pass finished with
        /// nothing left to retry, so the journal merge never runs twice.
        /// </summary>
        public bool DataFolderMigrated { get; set; } = false;
        /// <summary>
        /// One-time marker for the unpackaged build's Start menu and desktop shortcuts (<see cref="AppShortcuts"/>):
        /// set after the first run from a stable folder created them, so a shortcut the user deleted is never
        /// recreated by a later start.
        /// </summary>
        public bool ShortcutsCreated { get; set; } = false;
        public bool WelcomeShown { get; set; } = false;
        /// <summary>
        /// The rolling settings backup in <c>Documents\CyrFlip\Backups</c> (S0072 section 4). On by
        /// default: the owner's requirement - settings survive a Store reinstall - is impossible on
        /// the packaged channel without it. Exported v2: it is a preference, and it travels.
        /// </summary>
        public bool SettingsBackupEnabled { get; set; } = true;
        /// <summary>The restore question of the startup offer is unanswered (S0072 section 4). Machine
        /// history - never in the settings file; while it is set the backup writer stays silent, so a
        /// defaults-only profile cannot evict the backup the question is about.</summary>
        public bool BackupOfferOpen { get; set; } = false;
        /// <summary>The stamp of the backup the user declined to restore ("No - start fresh"); the
        /// question never re-asks. Machine history, like <see cref="BackupOfferOpen"/>.</summary>
        public string BackupRestoreDeclined { get; set; } = "";
        /// <summary>Load-time provenance only; never persisted.</summary>
        public bool IsFirstRun { get; private set; }
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
        /// <summary>
        /// Off by default: show a line numbers gutter along the left edge of the text editor (S0078).
        /// </summary>
        public bool QuickNotesLineNumbers { get; set; } = false;
        /// <summary>
        /// "Do not warn again for notes" of the exchange-file export (ticket S0023, spec section 2).
        /// Only a notes-only export honours it: an export that carries clipboard history warns every time.
        /// </summary>
        public bool ExchangeNotesWarningOff { get; set; } = false;
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
        /// The screen region capture (S0026). No module switch: the tray item is always there, and the
        /// chord has its own switch like the case flip's and the history's (owner, 2026-09-26).
        /// </summary>
        public string ScreenshotHotkey { get; set; } = DefaultScreenshotHotkey;
        public bool EnableScreenshotHotkey { get; set; } = true;
        /// <summary>
        /// The full capture's chord (S0071). Its own switch only, like the region capture's: the tray
        /// item is always there and no module switch exists for the capture pair.
        /// </summary>
        public string ScreenshotFullHotkey { get; set; } = DefaultScreenshotFullHotkey;
        public bool EnableScreenshotFullHotkey { get; set; } = true;
        /// <summary>
        /// The draw-on-capture module switch (S0071), default on ("optional but default-on"). Off =
        /// neither annotate chord is bound, no editor can appear from any entry point.
        /// </summary>
        public bool EnableScreenshotAnnotate { get; set; } = true;
        /// <summary>The area capture that opens the annotation editor (S0071); bound only while <see cref="EnableScreenshotAnnotate"/> and its own switch are on.</summary>
        public string ScreenshotAnnotateAreaHotkey { get; set; } = DefaultScreenshotAnnotateAreaHotkey;
        public bool EnableScreenshotAnnotateAreaHotkey { get; set; } = true;
        /// <summary>The full capture that opens the annotation editor (S0071); same double gate.</summary>
        public string ScreenshotFullAnnotateHotkey { get; set; } = DefaultScreenshotFullAnnotateHotkey;
        public bool EnableScreenshotFullAnnotateHotkey { get; set; } = true;
        /// <summary>
        /// The chord that opens <see cref="ScreenshotFolder"/> in Explorer (S0057). Bound only while
        /// <see cref="ScreenshotSaveEnabled"/> and <see cref="EnableScreenshotFolderHotkey"/> are both on.
        /// </summary>
        public string ScreenshotFolderHotkey { get; set; } = DefaultScreenshotFolderHotkey;
        public bool EnableScreenshotFolderHotkey { get; set; } = true;
        /// <summary>
        /// Also write each captured region as a PNG into <see cref="ScreenshotFolder"/> (CAPTURE-OUTPUT).
        /// On by default since S0071: a fresh profile saves from the first capture. Existing profiles
        /// keep their stored value - the S0071 migration never touches it (a privacy-relevant default
        /// does not move under an installed base; Q2).
        /// </summary>
        public bool ScreenshotSaveEnabled { get; set; } = true;
        /// <summary>Empty = the <c>FOLDERID_Screenshots</c> known folder, resolved at save time (CAPTURE-OUTPUT rule 9).</summary>
        public string ScreenshotFolder { get; set; } = "";
        public int ScreenshotCount { get; set; } = 0;
        /// <summary>
        /// One-time marker of the S0071 chord re-key (section 4.3): present = the migration has run for
        /// this profile. Machine history - it never joins the settings-file export (S0055).
        /// </summary>
        public bool ScreenshotKeyMigrationDone { get; set; } = false;
        /// <summary>The symbols window module switch (ticket S0056). Off by default.</summary>
        public bool EnableSymbols { get; set; } = false;
        public string SymbolsHotkey { get; set; } = DefaultSymbolsHotkey;
        public bool EnableSymbolsHotkey { get; set; } = true;
        /// <summary>The window's "close on pick" switch: a pick copies the character alone and closes the window.</summary>
        public bool SymbolsCloseOnPick { get; set; } = false;
        /// <summary>The group the Symbols window last showed; <see cref="SymbolsData.AllCategories"/> for every symbol.</summary>
        public string SymbolsCategory { get; set; } = SymbolsData.AllCategories;
        public bool SymbolsHideTypable { get; set; } = false;
        public int SymbolsX { get; set; } = int.MinValue;
        public int SymbolsY { get; set; } = int.MinValue;
        public int SymbolsWidth { get; set; } = 0;
        public int SymbolsHeight { get; set; } = 0;
        public int SymbolsCount { get; set; } = 0;

        // ---- The traffic light: a board of last known reachability (ticket S0069). Off by default ----
        /// <summary>
        /// The module switch. Off = no timer, no chord, no tray item, no window object and no network or power
        /// event subscription: a user who never switches it on pays nothing (D6).
        /// </summary>
        public bool EnableTrafficLight { get; set; } = false;
        public string TrafficLightHotkey { get; set; } = DefaultTrafficLightHotkey;
        public bool EnableTrafficLightHotkey { get; set; } = true;
        /// <summary>The user's targets, one JSON value (section 4); <c>[]</c> after the user emptied it.</summary>
        public List<TrafficLightTarget> TrafficLightTargets { get; set; } = new List<TrafficLightTarget>();
        /// <summary>The board was left visible; it reopens (without taking focus) at the next start.</summary>
        public bool TrafficLightVisible { get; set; } = false;
        /// <summary>Always on top of other windows. A switch, not a second window (D3).</summary>
        public bool TrafficLightPinned { get; set; } = true;
        /// <summary>0 small (a bare square), 1 medium, 2 large, 3 microscopic. Existing values stay stable.</summary>
        public int TrafficLightTileSize { get; set; } = 1;
        public const int MicroscopicTrafficLightTileSize = 3;
        /// <summary>Tiles in a row. The rows follow from the number of targets, and the window fits the grid.</summary>
        public int TrafficLightColumns { get; set; } = DefaultTrafficLightColumns;
        public const int DefaultTrafficLightColumns = 3;
        public const int MinTrafficLightColumns = 1;
        public const int MaxTrafficLightColumns = 12;
        /// <summary>
        /// The board window's transparency, percent (0..100): 0 the usual solid window, 100 only the title
        /// bar's buttons and the tiles remain - the plate (the background, the bar, the status line, the
        /// frame) is see-through and lets clicks pass to whatever is under it.
        /// </summary>
        public int TrafficLightWindowTransparency { get; set; } = 0;
        public int TrafficLightX { get; set; } = int.MinValue;
        public int TrafficLightY { get; set; } = int.MinValue;

        // ---- The exchange server clipboard: send and get (ticket S0074). Off by default ----
        /// <summary>Default hotkey for sending clipboard to server (shipped unassigned per D6).</summary>
        public const string DefaultExchangeClipboardSendHotkey = "";
        /// <summary>Default hotkey for getting clipboard from server (shipped unassigned per D6).</summary>
        public const string DefaultExchangeClipboardGetHotkey = "";

        /// <summary>The module switch. Off by default: opt-in (D1).</summary>
        public bool EnableExchangeClipboard { get; set; } = false;
        public string ExchangeClipboardSendHotkey { get; set; } = DefaultExchangeClipboardSendHotkey;
        public bool EnableExchangeClipboardSendHotkey { get; set; } = true;
        public string ExchangeClipboardGetHotkey { get; set; } = DefaultExchangeClipboardGetHotkey;
        public bool EnableExchangeClipboardGetHotkey { get; set; } = true;
        /// <summary>Default folder for received files from exchange server: Downloads\CyrFlip (D4).</summary>
        public static readonly string DefaultExchangeClipboardReceivedFolder =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CyrFlip");
        /// <summary>Empty = user's Downloads\CyrFlip folder (D4).</summary>
        public string ExchangeClipboardReceivedFolder { get; set; } = "";
        /// <summary>Enrolled exchange account entries (excluded from settings-file export for credentials reason).</summary>
        public List<ExchangeAccountEntry> ExchangeClipboardAccounts { get; set; } = new List<ExchangeAccountEntry>();

        /// <summary>
        /// UI language default for a fresh install (no saved value): follow the OS UI language when
        /// CyrFlip is translated into it, else English (see <see cref="Localization.DefaultLanguage"/>).
        /// Fixes a Russian UI appearing on an English OS.
        /// </summary>
        private static string DefaultUiLanguage() => Localization.DefaultLanguage();

        public static AppConfig Load() => Load(() => RuntimeRegistry.User.OpenSubKey(RegPath));

        internal static AppConfig Load(Func<IRuntimeRegistryKey?> openKey)
        {
            AppConfig cfg;
            // True when the conversion table had to be created rather than read - a fresh install or a
            // config written before the table existed - or when a stored table had to be repaired.
            // Persisted below, once the registry key is closed.
            bool mustSave;
            try
            {
                using IRuntimeRegistryKey? key = openKey();
                cfg = LoadFrom(key == null ? null : new RuntimeConfigKey(key), out mustSave);
            }
            catch
            {
                cfg = new AppConfig { WelcomeShown = true };
                cfg._unreadable.Add("*");
                mustSave = false;
            }

            // Outside the try/using: the seeded row has to reach the registry (so the next launch reads
            // it instead of seeding again), and the superseded values are dropped in the same pass.
            if (mustSave)
            {
                cfg.Save();
                DropLegacyFlipValues();
            }
            cfg.FromRegistry = true;
            return cfg;
        }

        /// <summary>
        /// True for the config <see cref="Load"/> produced, i.e. the user's own. The settings window's
        /// UI-context write (<see cref="SaveSettingsUiState"/>) is the one save a window makes by
        /// itself on hide and on every group toggle, so it goes to the registry only from this
        /// instance: a test that shows a settings window over a config of its own must not leave its
        /// window rectangle and group states in the developer's real settings.
        /// </summary>
        internal bool FromRegistry { get; private set; }

        /// <summary>
        /// Registry values that were present but could not be read; "*" means the key itself could not
        /// be opened and every save is disabled. Otherwise: wrong kind, unparsable number,
        /// malformed JSON. Each one fell back to its default <b>alone</b> (ticket S0007, CF-1): one bad
        /// value used to abort the whole load and leave every later field at its default, after which
        /// the next save wiped the conversion table and the one-time Windows snapshots for good.
        /// </summary>
        public IReadOnlyCollection<string> UnreadableValues => _unreadable;
        private readonly HashSet<string> _unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal bool IsUnreadable(string name) => _unreadable.Contains("*") || _unreadable.Contains(name);

        /// <summary>
        /// Tables and backups that were unreadable, with the serialized fallback they were loaded as.
        /// <see cref="SaveTo"/> leaves the raw registry value alone for as long as the in-memory value
        /// still serializes to that fallback - i.e. until the user actually edits that table.
        /// </summary>
        private readonly Dictionary<string, string> _preserveRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The whole read, against a registry seam (null = no key yet, i.e. a first run).</summary>
        internal static AppConfig LoadFrom(IConfigKey? key, out bool mustSave, string? legacyJsonPath = null)
        {
            var cfg = new AppConfig();
            bool seeded = false;
            bool repaired = false;
            // True when the S0071 chord re-key actually moved a stored chord: the pass persists at once.
            bool migrated = false;
            {
                if (key == null)
                {
                    cfg.LayoutConversionProfiles.Add(FlipRow(cfg.MigrateFromJson(out bool legacyFound, legacyJsonPath), true));
                    cfg.IsFirstRun = !legacyFound;
                    cfg.WelcomeShown = legacyFound;
                    seeded = true;
                }
                else
                {
                    // Missing or corrupt markers on existing profiles must never nag after an update.
                    object? welcomeMarker = cfg.Raw(key, "WelcomeShown");
                    cfg.WelcomeShown = welcomeMarker == null || !(welcomeMarker is int) || cfg.ReadBool(key, "WelcomeShown", true);
                    if (welcomeMarker != null && !(welcomeMarker is int)) cfg._unreadable.Add("WelcomeShown");
                    cfg.CaseHotkey = cfg.ReadHotkey(key, "CaseHotkey", cfg.CaseHotkey);
                    cfg.ClipboardHistoryHotkey = cfg.ReadHotkey(key, "ClipboardHistoryHotkey", cfg.ClipboardHistoryHotkey);
                    cfg.UiLanguage = cfg.ReadString(key, "UiLanguage", cfg.UiLanguage);
                    cfg.SettingsTab = Math.Max(0, cfg.ReadInt(key, "SettingsTab", cfg.SettingsTab));
                    cfg.SettingsPageId = cfg.ReadString(key, "SettingsPageId", cfg.SettingsPageId);
                    cfg.SettingsGroupsState = SanitizeGroupState(cfg.ReadString(key, "SettingsGroupsState", ""));
                    cfg.SettingsViewport = SanitizeViewport(cfg.ReadString(key, "SettingsViewport", ""));
                    cfg.SettingsWindowX = cfg.ReadInt(key, "SettingsWindowX", cfg.SettingsWindowX);
                    cfg.SettingsWindowY = cfg.ReadInt(key, "SettingsWindowY", cfg.SettingsWindowY);
                    cfg.SettingsWindowWidth = Math.Max(0, Math.Min(10000, cfg.ReadInt(key, "SettingsWindowWidth", cfg.SettingsWindowWidth)));
                    cfg.SettingsWindowHeight = Math.Max(0, Math.Min(10000, cfg.ReadInt(key, "SettingsWindowHeight", cfg.SettingsWindowHeight)));
                    cfg.EnableClipboardHistory = cfg.ReadBool(key, "EnableClipboardHistory", cfg.EnableClipboardHistory);
                    cfg.ClipboardHistoryNoticeShown = cfg.ReadBool(key, "ClipboardHistoryNoticeShown", cfg.ClipboardHistoryNoticeShown);
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
                    cfg.CursorNoticeShown = cfg.ReadBool(key, "CursorNoticeShown", cfg.CursorNoticeShown);
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
                    cfg.TextMenuHotkey = cfg.ReadHotkey(key, "TextMenuHotkey", cfg.TextMenuHotkey);
                    cfg.EnableTextMenuHotkey = cfg.ReadBool(key, "EnableTextMenuHotkey", cfg.EnableTextMenuHotkey);
                    // Parse, not the raw string, for the same reason: an unknown token reads as "system".
                    cfg.Theme = ThemeModes.Token(ThemeModes.Parse(cfg.ReadString(key, "Theme", cfg.Theme)));
                    // The one-time Windows snapshots cannot be taken again: an unreadable one is kept
                    // on disk untouched rather than replaced by "" on the next save.
                    cfg.LanguageHotkeysBackup = cfg.ReadString(key, "LanguageHotkeysBackup", cfg.LanguageHotkeysBackup, preserve: true);
                    cfg.InputLayoutsBackup = cfg.ReadString(key, "InputLayoutsBackup", cfg.InputLayoutsBackup, preserve: true);
                    cfg.InputMethodOverrideWritten = cfg.ReadString(key, "InputMethodOverrideWritten", cfg.InputMethodOverrideWritten);
                    cfg.EnableScenarioLauncher = cfg.ReadBool(key, "EnableScenarioLauncher", cfg.EnableScenarioLauncher);
                    cfg.LauncherFirstEnableDone = cfg.ReadBool(key, "LauncherFirstEnableDone", cfg.LauncherFirstEnableDone);
                    cfg.DataFolderMigrated = cfg.ReadBool(key, "DataFolderMigrated", cfg.DataFolderMigrated);
                    cfg.ShortcutsCreated = cfg.ReadBool(key, "ShortcutsCreated", cfg.ShortcutsCreated);

                    cfg.EnableTranslate = cfg.ReadBool(key, "EnableTranslate", cfg.EnableTranslate);
                    cfg.TranslateSeeded = cfg.ReadBool(key, "TranslateSeeded", cfg.TranslateSeeded);
                    cfg.TranslateProfiles = cfg.ReadTable<TranslationProfile>(key, "TranslateProfiles", SanitizeTranslationProfiles, ref repaired);
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
                    cfg.QuickNotesHotkey = cfg.ReadHotkey(key, "QuickNotesHotkey", cfg.QuickNotesHotkey);
                    cfg.EnableQuickNotesHotkey = cfg.ReadBool(key, "EnableQuickNotesHotkey", cfg.EnableQuickNotesHotkey);
                    cfg.QuickNotesWordWrap = cfg.ReadBool(key, "QuickNotesWordWrap", cfg.QuickNotesWordWrap);
                    cfg.QuickNotesLineNumbers = cfg.ReadBool(key, "QuickNotesLineNumbers", cfg.QuickNotesLineNumbers);
                    cfg.ExchangeNotesWarningOff = cfg.ReadBool(key, "ExchangeNotesWarningOff", cfg.ExchangeNotesWarningOff);
                    cfg.SettingsBackupEnabled = cfg.ReadBool(key, "SettingsBackupEnabled", cfg.SettingsBackupEnabled);
                    cfg.BackupOfferOpen = cfg.ReadBool(key, "BackupOfferOpen", cfg.BackupOfferOpen);
                    cfg.BackupRestoreDeclined = cfg.ReadString(key, "BackupRestoreDeclined", cfg.BackupRestoreDeclined);
                    cfg.QuickNotesX = cfg.ReadInt(key, "QuickNotesX", cfg.QuickNotesX);
                    cfg.QuickNotesY = cfg.ReadInt(key, "QuickNotesY", cfg.QuickNotesY);
                    cfg.QuickNotesWidth = cfg.ReadInt(key, "QuickNotesWidth", cfg.QuickNotesWidth);
                    cfg.QuickNotesHeight = cfg.ReadInt(key, "QuickNotesHeight", cfg.QuickNotesHeight);
                    cfg.QuickNotesSelected = cfg.ReadString(key, "QuickNotesSelected", cfg.QuickNotesSelected);

                    cfg.FlipCount = cfg.ReadInt(key, "FlipCount", cfg.FlipCount);
                    cfg.CaseFlipCount = cfg.ReadInt(key, "CaseFlipCount", cfg.CaseFlipCount);
                    cfg.TranslateCount = cfg.ReadInt(key, "TranslateCount", cfg.TranslateCount);
                    cfg.QuickNoteCount = cfg.ReadInt(key, "QuickNoteCount", cfg.QuickNoteCount);

                    cfg.ScreenshotHotkey = cfg.ReadHotkey(key, "ScreenshotHotkey", cfg.ScreenshotHotkey);
                    cfg.EnableScreenshotHotkey = cfg.ReadBool(key, "EnableScreenshotHotkey", cfg.EnableScreenshotHotkey);
                    cfg.ScreenshotFullHotkey = cfg.ReadHotkey(key, "ScreenshotFullHotkey", cfg.ScreenshotFullHotkey);
                    cfg.EnableScreenshotFullHotkey = cfg.ReadBool(key, "EnableScreenshotFullHotkey", cfg.EnableScreenshotFullHotkey);
                    cfg.EnableScreenshotAnnotate = cfg.ReadBool(key, "EnableScreenshotAnnotate", cfg.EnableScreenshotAnnotate);
                    cfg.ScreenshotAnnotateAreaHotkey = cfg.ReadHotkey(key, "ScreenshotAnnotateAreaHotkey", cfg.ScreenshotAnnotateAreaHotkey);
                    cfg.EnableScreenshotAnnotateAreaHotkey = cfg.ReadBool(key, "EnableScreenshotAnnotateAreaHotkey", cfg.EnableScreenshotAnnotateAreaHotkey);
                    cfg.ScreenshotFullAnnotateHotkey = cfg.ReadHotkey(key, "ScreenshotFullAnnotateHotkey", cfg.ScreenshotFullAnnotateHotkey);
                    cfg.EnableScreenshotFullAnnotateHotkey = cfg.ReadBool(key, "EnableScreenshotFullAnnotateHotkey", cfg.EnableScreenshotFullAnnotateHotkey);
                    cfg.ScreenshotFolderHotkey = cfg.ReadHotkey(key, "ScreenshotFolderHotkey", cfg.ScreenshotFolderHotkey);
                    cfg.EnableScreenshotFolderHotkey = cfg.ReadBool(key, "EnableScreenshotFolderHotkey", cfg.EnableScreenshotFolderHotkey);
                    cfg.ScreenshotSaveEnabled = cfg.ReadBool(key, "ScreenshotSaveEnabled", cfg.ScreenshotSaveEnabled);
                    cfg.ScreenshotFolder = cfg.ReadString(key, "ScreenshotFolder", cfg.ScreenshotFolder);
                    cfg.ScreenshotCount = cfg.ReadInt(key, "ScreenshotCount", cfg.ScreenshotCount);
                    cfg.ScreenshotKeyMigrationDone = cfg.ReadBool(key, "ScreenshotKeyMigrationDone", cfg.ScreenshotKeyMigrationDone);
                    if (NeedsScreenshotKeyMigration(Has(key, "ScreenshotKeyMigrationDone")))
                        migrated = cfg.ApplyScreenshotKeyMigration();

                    cfg.EnableSymbols = cfg.ReadBool(key, "EnableSymbols", cfg.EnableSymbols);
                    cfg.SymbolsHotkey = cfg.ReadHotkey(key, "SymbolsHotkey", cfg.SymbolsHotkey);
                    cfg.EnableSymbolsHotkey = cfg.ReadBool(key, "EnableSymbolsHotkey", cfg.EnableSymbolsHotkey);
                    cfg.SymbolsCloseOnPick = cfg.ReadBool(key, "SymbolsCloseOnPick", cfg.SymbolsCloseOnPick);
                    cfg.SymbolsCategory = cfg.ReadString(key, "SymbolsCategory", cfg.SymbolsCategory);
                    cfg.SymbolsHideTypable = cfg.ReadBool(key, "SymbolsHideTypable", cfg.SymbolsHideTypable);
                    cfg.SymbolsX = cfg.ReadInt(key, "SymbolsX", cfg.SymbolsX);
                    cfg.SymbolsY = cfg.ReadInt(key, "SymbolsY", cfg.SymbolsY);
                    cfg.SymbolsWidth = cfg.ReadInt(key, "SymbolsWidth", cfg.SymbolsWidth);
                    cfg.SymbolsHeight = cfg.ReadInt(key, "SymbolsHeight", cfg.SymbolsHeight);
                    cfg.SymbolsCount = cfg.ReadInt(key, "SymbolsCount", cfg.SymbolsCount);

                    cfg.EnableTrafficLight = cfg.ReadBool(key, "EnableTrafficLight", cfg.EnableTrafficLight);
                    cfg.TrafficLightHotkey = cfg.ReadHotkey(key, "TrafficLightHotkey", cfg.TrafficLightHotkey);
                    cfg.EnableTrafficLightHotkey = cfg.ReadBool(key, "EnableTrafficLightHotkey", cfg.EnableTrafficLightHotkey);
                    cfg.TrafficLightTargets = cfg.ReadTable<TrafficLightTarget>(key, "TrafficLightTargets", TrafficLightTargetList.Sanitize, ref repaired);
                    cfg.TrafficLightVisible = cfg.ReadBool(key, "TrafficLightVisible", cfg.TrafficLightVisible);
                    cfg.TrafficLightPinned = cfg.ReadBool(key, "TrafficLightPinned", cfg.TrafficLightPinned);
                    cfg.TrafficLightTileSize = Math.Max(0, Math.Min(MicroscopicTrafficLightTileSize, cfg.ReadInt(key, "TrafficLightTileSize", cfg.TrafficLightTileSize)));
                    cfg.TrafficLightColumns = Math.Max(MinTrafficLightColumns, Math.Min(MaxTrafficLightColumns,
                        cfg.ReadInt(key, "TrafficLightColumns", cfg.TrafficLightColumns)));
                    cfg.TrafficLightWindowTransparency = Math.Max(0, Math.Min(100,
                        cfg.ReadInt(key, "TrafficLightWindowTransparency", cfg.TrafficLightWindowTransparency)));
                    cfg.TrafficLightX = cfg.ReadInt(key, "TrafficLightX", cfg.TrafficLightX);
                    cfg.TrafficLightY = cfg.ReadInt(key, "TrafficLightY", cfg.TrafficLightY);

                    cfg.EnableExchangeClipboard = cfg.ReadBool(key, "EnableExchangeClipboard", cfg.EnableExchangeClipboard);
                    cfg.ExchangeClipboardSendHotkey = cfg.ReadHotkey(key, "ExchangeClipboardSendHotkey", cfg.ExchangeClipboardSendHotkey);
                    cfg.EnableExchangeClipboardSendHotkey = cfg.ReadBool(key, "EnableExchangeClipboardSendHotkey", cfg.EnableExchangeClipboardSendHotkey);
                    cfg.ExchangeClipboardGetHotkey = cfg.ReadHotkey(key, "ExchangeClipboardGetHotkey", cfg.ExchangeClipboardGetHotkey);
                    cfg.EnableExchangeClipboardGetHotkey = cfg.ReadBool(key, "EnableExchangeClipboardGetHotkey", cfg.EnableExchangeClipboardGetHotkey);
                    cfg.ExchangeClipboardReceivedFolder = cfg.ReadString(key, "ExchangeClipboardReceivedFolder", cfg.ExchangeClipboardReceivedFolder);
                    string? accountsJson = cfg.ReadOptionalString(key, "ExchangeClipboardAccounts", null);
                    cfg.ExchangeClipboardAccounts = ExchangeAccountEntry.DeserializeList(accountsJson);
                }
            }
            mustSave = seeded || repaired || migrated;
            return cfg;
        }

        /// <summary>
        /// Raised at the end of every successful <see cref="Save"/>: the rolling backup (S0072) keeps
        /// its copy fresh from this one hook. A subscriber must never fail the save.
        /// </summary>
        internal static Action? Saved;
        /// <summary>
        /// Persists the config. Answers whether it landed (S0065 E-9): Save used to be void, so no
        /// setting change could ever fail visibly - the machine whose registry is denied kept the
        /// change for the session and quietly lost it on the next start.
        /// </summary>
        public bool Save()
        {
            if (_unreadable.Contains("*")) return false;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return false;
                SaveTo(new RegistryConfigKey(key));
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
                return true;
            }
            catch { return false; }
        }

        internal void SaveTo(IConfigKey key)
        {
            if (_unreadable.Contains("*")) return;
            {
                key.SetValue("CaseHotkey", CaseHotkey, RegistryValueKind.String);
                key.SetValue("ClipboardHistoryHotkey", ClipboardHistoryHotkey, RegistryValueKind.String);
                key.SetValue("UiLanguage", UiLanguage, RegistryValueKind.String);
                key.SetValue("SettingsTab", SettingsTab, RegistryValueKind.DWord);
                key.SetValue("SettingsPageId", SettingsPageId, RegistryValueKind.String);
                key.SetValue("SettingsGroupsState", SettingsGroupsState, RegistryValueKind.String);
                key.SetValue("SettingsViewport", SettingsViewport, RegistryValueKind.String);
                key.SetValue("SettingsWindowX", SettingsWindowX, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowY", SettingsWindowY, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowWidth", SettingsWindowWidth, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowHeight", SettingsWindowHeight, RegistryValueKind.DWord);
                key.SetValue("EnableClipboardHistory", EnableClipboardHistory ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryNoticeShown", ClipboardHistoryNoticeShown ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("PauseClipboardHistory", PauseClipboardHistory ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ShowClipboardHistoryOnStartup", ShowClipboardHistoryOnStartup ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryX", ClipboardHistoryX, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryY", ClipboardHistoryY, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryWidth", ClipboardHistoryWidth, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryHeight", ClipboardHistoryHeight, RegistryValueKind.DWord);
                key.SetValue("ClipboardHistoryOpacity", ClipboardHistoryOpacity, RegistryValueKind.DWord);
                key.SetValue("CursorSize", CursorSize, RegistryValueKind.DWord);
                key.SetValue("EnableCursorChange", EnableCursorChange ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CursorNoticeShown", CursorNoticeShown ? 1 : 0, RegistryValueKind.DWord);
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
                key.SetValue("TextMenuHotkey", TextMenuHotkey, RegistryValueKind.String);
                key.SetValue("EnableTextMenuHotkey", EnableTextMenuHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Theme", Theme, RegistryValueKind.String);
                WritePreserved(key, "LanguageHotkeysBackup", LanguageHotkeysBackup);
                WritePreserved(key, "InputLayoutsBackup", InputLayoutsBackup);
                key.SetValue("InputMethodOverrideWritten", InputMethodOverrideWritten, RegistryValueKind.String);
                key.SetValue("EnableScenarioLauncher", EnableScenarioLauncher ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("LauncherFirstEnableDone", LauncherFirstEnableDone ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DataFolderMigrated", DataFolderMigrated ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ShortcutsCreated", ShortcutsCreated ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("WelcomeShown", WelcomeShown ? 1 : 0, RegistryValueKind.DWord);
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
                WritePreserved(key, "LayoutConversionProfiles", new JavaScriptSerializer().Serialize(LayoutConversionProfiles));
                WritePreserved(key, "TranslateProfiles", new JavaScriptSerializer().Serialize(TranslateProfiles));
                key.SetValue("EnableQuickNotes", EnableQuickNotes ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesNoticeShown", QuickNotesNoticeShown ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHotkey", QuickNotesHotkey, RegistryValueKind.String);
                key.SetValue("EnableQuickNotesHotkey", EnableQuickNotesHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWordWrap", QuickNotesWordWrap ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("QuickNotesLineNumbers", QuickNotesLineNumbers ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ExchangeNotesWarningOff", ExchangeNotesWarningOff ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SettingsBackupEnabled", SettingsBackupEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("BackupOfferOpen", BackupOfferOpen ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("BackupRestoreDeclined", BackupRestoreDeclined, RegistryValueKind.String);
                key.SetValue("QuickNotesX", QuickNotesX, RegistryValueKind.DWord);
                key.SetValue("QuickNotesY", QuickNotesY, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWidth", QuickNotesWidth, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHeight", QuickNotesHeight, RegistryValueKind.DWord);
                key.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
                key.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
                key.SetValue("CaseFlipCount", CaseFlipCount, RegistryValueKind.DWord);
                key.SetValue("TranslateCount", TranslateCount, RegistryValueKind.DWord);
                key.SetValue("QuickNoteCount", QuickNoteCount, RegistryValueKind.DWord);
                key.SetValue("ScreenshotHotkey", ScreenshotHotkey, RegistryValueKind.String);
                key.SetValue("EnableScreenshotHotkey", EnableScreenshotHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotFullHotkey", ScreenshotFullHotkey, RegistryValueKind.String);
                key.SetValue("EnableScreenshotFullHotkey", EnableScreenshotFullHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableScreenshotAnnotate", EnableScreenshotAnnotate ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotAnnotateAreaHotkey", ScreenshotAnnotateAreaHotkey, RegistryValueKind.String);
                key.SetValue("EnableScreenshotAnnotateAreaHotkey", EnableScreenshotAnnotateAreaHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotFullAnnotateHotkey", ScreenshotFullAnnotateHotkey, RegistryValueKind.String);
                key.SetValue("EnableScreenshotFullAnnotateHotkey", EnableScreenshotFullAnnotateHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotFolderHotkey", ScreenshotFolderHotkey, RegistryValueKind.String);
                key.SetValue("EnableScreenshotFolderHotkey", EnableScreenshotFolderHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotSaveEnabled", ScreenshotSaveEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ScreenshotFolder", ScreenshotFolder, RegistryValueKind.String);
                key.SetValue("ScreenshotCount", ScreenshotCount, RegistryValueKind.DWord);
                // S0071 4.3: written even when the pass moved nothing, so a fresh profile gets the
                // marker on its first save and the block runs exactly once per profile.
                key.SetValue("ScreenshotKeyMigrationDone", ScreenshotKeyMigrationDone ? 1 : 0, RegistryValueKind.DWord);

                if (EnableSymbols || Has(key, "EnableSymbols"))
                {
                    key.SetValue("EnableSymbols", EnableSymbols ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("SymbolsHotkey", SymbolsHotkey, RegistryValueKind.String);
                    key.SetValue("EnableSymbolsHotkey", EnableSymbolsHotkey ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("SymbolsCloseOnPick", SymbolsCloseOnPick ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("SymbolsCategory", SymbolsCategory, RegistryValueKind.String);
                    key.SetValue("SymbolsHideTypable", SymbolsHideTypable ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("SymbolsX", SymbolsX, RegistryValueKind.DWord);
                    key.SetValue("SymbolsY", SymbolsY, RegistryValueKind.DWord);
                    key.SetValue("SymbolsWidth", SymbolsWidth, RegistryValueKind.DWord);
                    key.SetValue("SymbolsHeight", SymbolsHeight, RegistryValueKind.DWord);
                    key.SetValue("SymbolsCount", SymbolsCount, RegistryValueKind.DWord);
                }

                // The list of targets is the one traffic light value that is always written: it is an exported
                // key of the settings file, and the export, snapshot and rollback machinery reads every exported
                // key. Everything else of the module waits until it has been switched on (D6).
                WritePreserved(key, "TrafficLightTargets", TrafficLightTargetList.Serialize(TrafficLightTargets));
                if (EnableTrafficLight || Has(key, "EnableTrafficLight"))
                {
                    key.SetValue("EnableTrafficLight", EnableTrafficLight ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightHotkey", TrafficLightHotkey, RegistryValueKind.String);
                    key.SetValue("EnableTrafficLightHotkey", EnableTrafficLightHotkey ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightVisible", TrafficLightVisible ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightPinned", TrafficLightPinned ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightTileSize", TrafficLightTileSize, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightColumns", TrafficLightColumns, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightWindowTransparency", TrafficLightWindowTransparency, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightX", TrafficLightX, RegistryValueKind.DWord);
                    key.SetValue("TrafficLightY", TrafficLightY, RegistryValueKind.DWord);
                }

                WritePreserved(key, "ExchangeClipboardAccounts", ExchangeAccountEntry.SerializeList(ExchangeClipboardAccounts));
                if (EnableExchangeClipboard || Has(key, "EnableExchangeClipboard"))
                {
                    key.SetValue("EnableExchangeClipboard", EnableExchangeClipboard ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ExchangeClipboardSendHotkey", ExchangeClipboardSendHotkey, RegistryValueKind.String);
                    key.SetValue("EnableExchangeClipboardSendHotkey", EnableExchangeClipboardSendHotkey ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ExchangeClipboardGetHotkey", ExchangeClipboardGetHotkey, RegistryValueKind.String);
                    key.SetValue("EnableExchangeClipboardGetHotkey", EnableExchangeClipboardGetHotkey ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ExchangeClipboardReceivedFolder", ExchangeClipboardReceivedFolder, RegistryValueKind.String);
                }
            }
        }

        /// <summary>
        /// Remember the settings tab and persist only that value (cheap write) - switching tabs
        /// shouldn't rewrite every setting just to record where the user is looking. The page is
        /// recorded by its stable key name when the caller knows it (S0060 P5, M6); the index stays
        /// as the fallback an older build reads.
        /// </summary>
        public void SaveSettingsTab(int index) => SaveSettingsTab(index, null);

        public void SaveSettingsTab(int index, string? pageId)
        {
            if (_unreadable.Contains("*")) return;
            if (index < 0 || (index == SettingsTab && (pageId == null || pageId == SettingsPageId))) return;
            SettingsTab = index;
            if (pageId != null) SettingsPageId = pageId;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("SettingsTab", SettingsTab, RegistryValueKind.DWord);
                key?.SetValue("SettingsPageId", SettingsPageId, RegistryValueKind.String);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>
        /// Persist the settings window's UI context - group expansion, page viewports and the window
        /// rectangle - in one cheap write, the way <see cref="SaveSettingsTab"/> does for the page
        /// index (WINDOWS-UI section 3 rule 3: UI context is private navigation state, saved as the
        /// user moves, not folded into every settings save).
        /// </summary>
        public void SaveSettingsUiState()
        {
            if (_unreadable.Contains("*")) return;
            if (!FromRegistry) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("SettingsGroupsState", SettingsGroupsState, RegistryValueKind.String);
                key.SetValue("SettingsViewport", SettingsViewport, RegistryValueKind.String);
                key.SetValue("SettingsWindowX", SettingsWindowX, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowY", SettingsWindowY, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowWidth", SettingsWindowWidth, RegistryValueKind.DWord);
                key.SetValue("SettingsWindowHeight", SettingsWindowHeight, RegistryValueKind.DWord);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>Keeps only well-formed "id=0|1" pairs, at most 64 of them with ids of at most 64 characters.</summary>
        internal static string SanitizeGroupState(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var kept = new List<string>();
            foreach (string part in raw.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq > 64 || part.Length - eq != 2) continue;
                char flag = part[eq + 1];
                if (flag != '0' && flag != '1') continue;
                kept.Add(part);
                if (kept.Count >= 64) break;
            }
            return string.Join(";", kept);
        }

        /// <summary>Keeps only well-formed "page|anchor|offset" rows, at most 64 of them.</summary>
        internal static string SanitizeViewport(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var kept = new List<string>();
            foreach (string part in raw.Split(';'))
            {
                string[] fields = part.Split('|');
                if (fields.Length != 3 || fields[1].Length == 0 || fields[1].Length > 64) continue;
                // The page column is a page key since S0062 (a legacy row holds the old list index).
                if (!SettingsPageKeys.TryParseViewportPage(fields[0], out _)) continue;
                if (!int.TryParse(fields[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int offset)) continue;
                if (offset < -100000 || offset > 100000) continue;
                kept.Add(part);
                if (kept.Count >= 64) break;
            }
            return string.Join(";", kept);
        }

        /// <summary>Set the one-time <see cref="DataFolderMigrated"/> marker, writing only that value.</summary>
        public void SaveDataFolderMigrated()
        {
            if (_unreadable.Contains("*")) return;
            DataFolderMigrated = true;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("DataFolderMigrated", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Set the one-time <see cref="ShortcutsCreated"/> marker, writing only that value.</summary>
        public void SaveShortcutsCreated()
        {
            if (_unreadable.Contains("*")) return;
            ShortcutsCreated = true;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("ShortcutsCreated", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Persist at display time, so closing or crashing cannot repeat the tour.</summary>
        public void SaveWelcomeShown()
        {
            if (_unreadable.Contains("*")) return;
            WelcomeShown = true;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("WelcomeShown", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the flip counter and persist only that value (cheap write).</summary>
        public void IncrementFlipCount()
        {
            if (_unreadable.Contains("*")) return;
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
            if (_unreadable.Contains("*")) return;
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
            if (_unreadable.Contains("*")) return;
            TranslateCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("TranslateCount", TranslateCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the region-capture counter and persist only that value (cheap write).</summary>
        public void IncrementScreenshotCount()
        {
            if (_unreadable.Contains("*")) return;
            ScreenshotCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("ScreenshotCount", ScreenshotCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>Increment the quick-notes counter and persist only that value (cheap write).</summary>
        public void IncrementQuickNoteCount()
        {
            if (_unreadable.Contains("*")) return;
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
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("QuickNotesX", QuickNotesX, RegistryValueKind.DWord);
                key.SetValue("QuickNotesY", QuickNotesY, RegistryValueKind.DWord);
                key.SetValue("QuickNotesWidth", QuickNotesWidth, RegistryValueKind.DWord);
                key.SetValue("QuickNotesHeight", QuickNotesHeight, RegistryValueKind.DWord);
                key.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>
        /// Remember which note is open - that one value alone. The window calls it whenever the
        /// selection changes, not only when the window is moved (ticket S0006, QN-9).
        /// </summary>
        public void SaveQuickNotesSelected()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("QuickNotesSelected", QuickNotesSelected, RegistryValueKind.String);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        public void IncrementSymbolsCount()
        {
            if (_unreadable.Contains("*")) return;
            SymbolsCount++;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("SymbolsCount", SymbolsCount, RegistryValueKind.DWord);
            }
            catch { }
        }

        public void SaveSymbolsCloseOnPick()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("SymbolsCloseOnPick", SymbolsCloseOnPick ? 1 : 0, RegistryValueKind.DWord);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        public void SaveSymbolsCategory()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("SymbolsCategory", SymbolsCategory, RegistryValueKind.String);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        public void SaveSymbolsWindow()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("SymbolsX", SymbolsX, RegistryValueKind.DWord);
                key.SetValue("SymbolsY", SymbolsY, RegistryValueKind.DWord);
                key.SetValue("SymbolsWidth", SymbolsWidth, RegistryValueKind.DWord);
                key.SetValue("SymbolsHeight", SymbolsHeight, RegistryValueKind.DWord);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>The board's own position, written at the end of a drag and on hide (one value group, not <see cref="Save"/>). Its size is the grid's, never stored.</summary>
        public void SaveTrafficLightWindow()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("TrafficLightX", TrafficLightX, RegistryValueKind.DWord);
                key.SetValue("TrafficLightY", TrafficLightY, RegistryValueKind.DWord);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>Whether the board was left visible - the one value that changes every time it is shown or hidden.</summary>
        public void SaveTrafficLightVisible()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("TrafficLightVisible", TrafficLightVisible ? 1 : 0, RegistryValueKind.DWord);
                try { Saved?.Invoke(); } catch (Exception ex) { DiagnosticLog.Append(DiagnosticLog.Path("settings-file.log"), "backup notification failed " + ex.GetType().Name); }
            }
            catch { }
        }

        /// <summary>Persists enrolled exchange clipboard accounts to HKCU\Software\CyrFlip without overwriting other settings.</summary>
        public void SaveExchangeAccounts()
        {
            if (_unreadable.Contains("*")) return;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                key?.SetValue("ExchangeClipboardAccounts", ExchangeAccountEntry.SerializeList(ExchangeClipboardAccounts), RegistryValueKind.String);
            }
            catch { }
        }

        /// <summary>
        /// Pull what a legacy config.json still has to say (first run, no registry key yet) and return
        /// the flip chord it carried, so the caller can seed the conversion table with it. The caller
        /// persists - a migration that saved on its own would write before the seeding is applied.
        /// </summary>
        private string MigrateFromJson(out bool legacyFound, string? legacyJsonPath = null)
        {
            legacyFound = false;
            string hotkey = DefaultFlipHotkey;
            try
            {
                string? path = legacyJsonPath ?? ResolveJsonPath();
                if (path == null || !File.Exists(path))
                    return hotkey;
                // Even a damaged old file identifies an existing user.
                legacyFound = true;
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
        /// Whether the S0071 chord re-key still owes this profile its pass: true while the one-time
        /// <c>ScreenshotKeyMigrationDone</c> marker is absent (a marker that is there but unreadable
        /// counts as present - the caller checks <see cref="Has"/> before asking).
        /// </summary>
        internal static bool NeedsScreenshotKeyMigration(bool markerPresent) => !markerPresent;

        /// <summary>
        /// The re-key itself, exactly the two rows of S0071 section 4.3: a stored capture chord equal
        /// by <see cref="ChordDefaults.SameChordToken"/> to the shipped Ctrl+PrintScreen moves to
        /// Ctrl+F12, and a folder chord equal to the shipped Ctrl+Shift+PrintScreen to Ctrl+F10 - both
        /// keys did not fire in practice (Windows' own handling of PrintScreen is the suspect).
        /// Answers whether anything moved: the load persists the pass at once when it did.
        ///
        /// <para>Nothing else is touched. A stored value that is anything else was customised on
        /// purpose and stays; every <c>Enable*</c> switch, the master switch, the case flip and the
        /// seeded EN ⇄ RU row keep what they carry (the owner kept those chords - nothing is
        /// displaced). The marker is set even when nothing moved: a fresh profile just gets the
        /// marker, and the block runs its pass once per profile.</para>
        /// </summary>
        internal bool ApplyScreenshotKeyMigration()
        {
            bool moved = false;
            if (ChordDefaults.SameChordToken(ScreenshotHotkey, LegacyScreenshotHotkey))
            {
                ScreenshotHotkey = DefaultScreenshotHotkey;
                moved = true;
            }
            if (ChordDefaults.SameChordToken(ScreenshotFolderHotkey, LegacyScreenshotFolderHotkey))
            {
                ScreenshotFolderHotkey = DefaultScreenshotFolderHotkey;
                moved = true;
            }
            ScreenshotKeyMigrationDone = true;
            return moved;
        }

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

        /// <summary>
        /// One hotkey string. Absent uses <paramref name="def"/>; unreadable or unparsable falls back
        /// to <paramref name="def"/> and records the value in <see cref="UnreadableValues"/> (KC2-5).
        /// A parsable value comes back in its canonical spelling, so the next save writes that
        /// (INPUT-CHORD rule 2: "control + shift + f12" is stored as "Ctrl+Shift+F12").
        /// </summary>
        internal string ReadHotkey(IConfigKey key, string name, string def)
        {
            string? raw = ReadOptionalString(key, name, null);
            if (raw == null) return def;
            if (raw.Length == 0 && def.Length == 0) return "";
            if (Hotkey.TryParse(raw, out Hotkey parsed)) return parsed.Display;
            _unreadable.Add(name);
            return def;
        }

        /// <summary>
        /// The exchange account entries (ticket S0074). Absent or empty is no entries; a value that is there but does not
        /// parse is no entries <b>that are not saved over</b> until the person adds one - the tokens of the lost
        /// entries stay in the secret store, and a hand-fixed value gets them back.
        /// </summary>
        private List<ExchangeAccountEntry> ReadAccounts(IConfigKey key)
        {
            const string name = "ExchangeClipboardAccounts";
            object? val = Raw(key, name);
            if (val == null) return new List<ExchangeAccountEntry>();
            if (val is string json)
            {
                if (json.Trim().Length == 0) return new List<ExchangeAccountEntry>();
                if (ExchangeAccountEntry.TryDeserializeList(json, out List<ExchangeAccountEntry> list)) return list;
            }
            _unreadable.Add(name);
            _preserveRaw[name] = ExchangeAccountEntry.SerializeList(new ExchangeAccountEntry[0]);
            return new List<ExchangeAccountEntry>();
        }

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
                if (CanonicalizeChord(row.Hotkey, out string conversionChord)) { row.Hotkey = conversionChord; changed = true; }
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
                if (CanonicalizeChord(row.Hotkey, out string translationChord)) { row.Hotkey = translationChord; changed = true; }
                if (string.IsNullOrEmpty(row.Id) || !ids.Add(row.Id)) { row.Id = NewId(ids); changed = true; }
            }
            return changed;
        }

        /// <summary>
        /// True when <paramref name="stored"/> parses but is not spelled canonically, with the canonical
        /// spelling in <paramref name="canonical"/> (INPUT-CHORD rule 2). A chord that does not parse is
        /// left exactly as stored: it is inert (rule 3), and the user's value is not ours to rewrite.
        /// </summary>
        internal static bool CanonicalizeChord(string? stored, out string canonical)
        {
            canonical = stored ?? "";
            if (!Hotkey.TryParse(stored, out Hotkey parsed) || parsed.Display == stored) return false;
            canonical = parsed.Display;
            return true;
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

    internal sealed class RuntimeConfigKey : IConfigKey
    {
        private readonly IRuntimeRegistryKey _key;
        public RuntimeConfigKey(IRuntimeRegistryKey key) => _key = key;
        public object? GetValue(string name) => _key.GetValue(name);
        public void SetValue(string name, object value, RegistryValueKind kind) => _key.SetValue(name, value, kind);
    }

    internal sealed class RegistryConfigKey : IConfigKey
    {
        private readonly RegistryKey _key;
        public RegistryConfigKey(RegistryKey key) { _key = key; }
        public object? GetValue(string name) => _key.GetValue(name);
        public void SetValue(string name, object value, RegistryValueKind kind) => _key.SetValue(name, value, kind);
    }
}
