using System;

namespace CyrFlip
{
    internal sealed partial class CyrFlipContext
    {
        /// <summary>
        /// A reset on the hotkeys overview rewrote chords and switches (ticket S0056). One edit can touch
        /// any of the fixed chords, the conversion and translation tables, a scenario and the master
        /// switch, so the hook is given all of them again instead of one owner's slice. The values are
        /// already in <see cref="_config"/> and the scenario files; this saves, rebinds and redraws.
        /// </summary>
        private void OnChordsEdited()
        {
            _config.Save();

            _caseHotkey = Hotkey.TryParse(_config.CaseHotkey, out Hotkey caseChord) ? caseChord : Hotkey.CaseDefault;
            _clipboardHistoryHotkey = Hotkey.Parse(_config.ClipboardHistoryHotkey);
            _quickNotesHotkey = Hotkey.Parse(_config.QuickNotesHotkey);
            _screenshotHotkey = Hotkey.Parse(_config.ScreenshotHotkey);
            _screenshotFolderHotkey = ParsedScreenshotFolderHotkey();
            _symbolsHotkey = Hotkey.TryParse(_config.SymbolsHotkey, out Hotkey symbolsChord)
                ? symbolsChord : Hotkey.Parse(AppConfig.DefaultSymbolsHotkey);

            _hook.UpdateCaseHotkey(_caseHotkey);
            _hook.UpdateClipboardHistoryHotkey(_clipboardHistoryHotkey);
            _hook.UpdateQuickNotesHotkey(_quickNotesHotkey);
            _hook.UpdateScreenshotHotkey(_screenshotHotkey);
            _hook.UpdateScreenshotFolderHotkey(_screenshotFolderHotkey);
            _hook.UpdateTextMenuHotkey(ParsedTextMenuHotkey());
            _hook.UpdateSymbolsHotkey(_symbolsHotkey);

            _hook.UpdateEnabled(_config.EnableHotkeys);
            _hook.UpdateCaseEnabled(_config.EnableCaseHotkey);
            _hook.UpdateHistoryEnabled(_config.EnableHistoryHotkey);
            _hook.UpdateQuickNotesEnabled(QuickNotesChordLive);
            _hook.UpdateScreenshotEnabled(ScreenshotChordLive);
            _hook.UpdateScreenshotFolderEnabled(ScreenshotFolderChordLive);
            _hook.UpdateTextMenuEnabled(TextMenuChordLive);
            _hook.UpdateSymbolsEnabled(_config.EnableHotkeys && SymbolsChordLive);

            _quickNotesChordWasLive = QuickNotesChordLive;
            _screenshotChordWasLive = ScreenshotChordLive;
            _screenshotFolderChordWasLive = ScreenshotFolderChordLive;
            _textMenuChordWasLive = TextMenuChordLive;
            _symbolsChordWasLive = SymbolsChordLive;

            _hook.UpdateConversionProfiles(_config.LayoutConversionProfiles);
            BindTranslationHotkeys();
            RefreshContextMenuBinding();
            RefreshLauncherSurfaces();
            UpdateTrayTexts();
            UpdateTrayTooltip();
            _settings?.Reload();
        }
    }
}
