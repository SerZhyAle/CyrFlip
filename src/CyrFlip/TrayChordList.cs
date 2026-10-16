using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>Group classification for the tray chords popup and model (S0059 section 4).</summary>
    internal enum TrayChordGroupKind
    {
        Text,
        Conversions,
        Translation,
        Launcher,
        Mouse,
        Windows,
    }

    /// <summary>One row in the tray hotkeys list.</summary>
    internal sealed class TrayChordRow
    {
        public TrayChordGroupKind Group { get; }
        public string ChordDisplay { get; }
        public string ActionDisplay { get; }
        public bool IsClash { get; internal set; }
        public bool IsMuted { get; }
        public ChordKind? Kind { get; }
        public string? Id { get; }
        public Hotkey? ParsedChord { get; }

        public TrayChordRow(TrayChordGroupKind group, string chordDisplay, string actionDisplay,
            bool isClash = false, bool isMuted = false, ChordKind? kind = null, string? id = null,
            Hotkey? parsedChord = null)
        {
            Group = group;
            ChordDisplay = chordDisplay;
            ActionDisplay = actionDisplay;
            IsClash = isClash;
            IsMuted = isMuted;
            Kind = kind;
            Id = id;
            ParsedChord = parsedChord;
        }
    }

    /// <summary>A logical group of active chord rows with a localized title.</summary>
    internal sealed class TrayChordGroup
    {
        public TrayChordGroupKind Group { get; }
        public string Title { get; }
        public IReadOnlyList<TrayChordRow> Rows { get; }

        public TrayChordGroup(TrayChordGroupKind group, string title, IReadOnlyList<TrayChordRow> rows)
        {
            Group = group;
            Title = title;
            Rows = rows;
        }
    }

    /// <summary>
    /// Pure model of every active chord and mouse shortcut (ticket S0059 section 4).
    /// Built without UI state, used by <see cref="TrayHotkeysPopup"/> and to feed the native tray tooltip.
    /// </summary>
    internal sealed class TrayChordList
    {
        public IReadOnlyList<TrayChordGroup> Groups { get; }
        public IReadOnlyList<TrayChordRow> AllRows { get; }

        public TrayChordList(IReadOnlyList<TrayChordGroup> groups, IReadOnlyList<TrayChordRow> allRows)
        {
            Groups = groups;
            AllRows = allRows;
        }

        public static TrayChordList Build(
            AppConfig config,
            IEnumerable<LauncherScenario>? scenarios,
            IEnumerable<LanguageHotkeys.Entry>? windowsHotkeys,
            string? windowsToggleCode,
            string uiLanguage,
            Func<string, string>? layoutName = null,
            Func<string, string>? translate = null)
        {
            Func<string, string> T = translate ?? (ru => Localization.Translate(uiLanguage, ru));
            Func<string, string> LName = layoutName ?? WorldLayouts.CodeForKlid;

            var groups = new List<TrayChordGroup>();
            var allRows = new List<TrayChordRow>();

            // 1. Text group
            var textRows = new List<TrayChordRow>();
            if (!config.EnableHotkeys)
            {
                textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, "", T("hotkeys are off"), isMuted: true));
            }
            else
            {
                if (ChordBinding.IsCaseActive(config) && Hotkey.TryParse(config.CaseHotkey, out Hotkey caseChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, caseChord.Display, T("case flip"), kind: ChordKind.Case, parsedChord: caseChord));

                if (ChordBinding.IsHistoryActive(config) && Hotkey.TryParse(config.ClipboardHistoryHotkey, out Hotkey histChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, histChord.Display, T("clipboard history"), kind: ChordKind.History, parsedChord: histChord));

                if (ChordBinding.IsQuickNotesActive(config) && Hotkey.TryParse(config.QuickNotesHotkey, out Hotkey qnChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, qnChord.Display, T("quick notes"), kind: ChordKind.QuickNotes, parsedChord: qnChord));

                if (ChordBinding.IsSymbolsActive(config) && Hotkey.TryParse(config.SymbolsHotkey, out Hotkey symChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, symChord.Display, T("symbols window"), kind: ChordKind.Symbols, parsedChord: symChord));

                if (ChordBinding.IsTrafficLightActive(config) && Hotkey.TryParse(config.TrafficLightHotkey, out Hotkey tlChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, tlChord.Display, T("availability traffic light"), kind: ChordKind.TrafficLight, parsedChord: tlChord));

                if (ChordBinding.IsScreenshotActive(config) && Hotkey.TryParse(config.ScreenshotHotkey, out Hotkey scChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, scChord.Display, T("screen region capture"), kind: ChordKind.Screenshot, parsedChord: scChord));

                if (ChordBinding.IsScreenshotFullActive(config) && Hotkey.TryParse(config.ScreenshotFullHotkey, out Hotkey sfChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, sfChord.Display, T("full screen capture"), kind: ChordKind.ScreenshotFull, parsedChord: sfChord));

                if (ChordBinding.IsScreenshotAnnotateAreaActive(config) && Hotkey.TryParse(config.ScreenshotAnnotateAreaHotkey, out Hotkey saaChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, saaChord.Display, T("area capture with drawing"), kind: ChordKind.ScreenshotAnnotateArea, parsedChord: saaChord));

                if (ChordBinding.IsScreenshotFullAnnotateActive(config) && Hotkey.TryParse(config.ScreenshotFullAnnotateHotkey, out Hotkey sfaChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, sfaChord.Display, T("full screen capture with drawing"), kind: ChordKind.ScreenshotFullAnnotate, parsedChord: sfaChord));

                if (ChordBinding.IsScreenshotFolderActive(config) && Hotkey.TryParse(config.ScreenshotFolderHotkey, out Hotkey scfChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, scfChord.Display, T("screenshots folder"), kind: ChordKind.ScreenshotFolder, parsedChord: scfChord));

                if (ChordBinding.IsTextMenuActive(config) && Hotkey.TryParse(config.TextMenuHotkey, out Hotkey tmChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, tmChord.Display, T("text context menu"), kind: ChordKind.TextMenu, parsedChord: tmChord));

                if (ChordBinding.IsExchangeSendActive(config) && Hotkey.TryParse(config.ExchangeClipboardSendHotkey, out Hotkey exSendChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, exSendChord.Display, T("Send clipboard to server"), kind: ChordKind.ExchangeSend, parsedChord: exSendChord));

                if (ChordBinding.IsExchangeGetActive(config) && Hotkey.TryParse(config.ExchangeClipboardGetHotkey, out Hotkey exGetChord))
                    textRows.Add(new TrayChordRow(TrayChordGroupKind.Text, exGetChord.Display, T("Get clipboard from server"), kind: ChordKind.ExchangeGet, parsedChord: exGetChord));
            }

            if (textRows.Count > 0)
            {
                groups.Add(new TrayChordGroup(TrayChordGroupKind.Text, T("Text"), textRows));
                allRows.AddRange(textRows);
            }

            // 2. Conversions group
            if (config.EnableHotkeys && config.LayoutConversionProfiles != null)
            {
                var convRows = new List<TrayChordRow>();
                foreach (LayoutConversionProfile profile in config.LayoutConversionProfiles)
                {
                    if (ChordBinding.IsConversionActive(config, profile) && Hotkey.TryParse(profile.Hotkey, out Hotkey convChord))
                    {
                        string action = LName(profile.SourceKlid) + " ⇄ " + LName(profile.TargetKlid);
                        convRows.Add(new TrayChordRow(TrayChordGroupKind.Conversions, convChord.Display, action,
                            kind: ChordKind.Conversion, id: profile.Id, parsedChord: convChord));
                    }
                }
                if (convRows.Count > 0)
                {
                    groups.Add(new TrayChordGroup(TrayChordGroupKind.Conversions, T("Conversion"), convRows));
                    allRows.AddRange(convRows);
                }
            }

            // 3. Translation group
            if (config.EnableHotkeys && config.EnableTranslate && config.TranslateProfiles != null)
            {
                var transRows = new List<TrayChordRow>();
                foreach (TranslationProfile profile in config.TranslateProfiles)
                {
                    if (ChordBinding.IsTranslationActive(config, profile) && Hotkey.TryParse(profile.Hotkey, out Hotkey trChord))
                    {
                        string action = TranslationLanguages.Label(profile.TargetLang, uiLanguage);
                        transRows.Add(new TrayChordRow(TrayChordGroupKind.Translation, trChord.Display, action,
                            kind: ChordKind.Translation, id: profile.Id, parsedChord: trChord));
                    }
                }
                if (transRows.Count > 0)
                {
                    groups.Add(new TrayChordGroup(TrayChordGroupKind.Translation, T("Translation"), transRows));
                    allRows.AddRange(transRows);
                }
            }

            // 4. Launcher group
            if (config.EnableHotkeys && config.EnableScenarioLauncher && scenarios != null)
            {
                var launchRows = new List<TrayChordRow>();
                foreach (LauncherScenario scenario in scenarios)
                {
                    if (ChordBinding.IsLauncherActive(config, scenario) && Hotkey.TryParse(scenario.Hotkey, out Hotkey scnChord))
                    {
                        launchRows.Add(new TrayChordRow(TrayChordGroupKind.Launcher, scnChord.Display, scenario.Name,
                            kind: ChordKind.Launcher, id: scenario.Id.ToString(), parsedChord: scnChord));
                    }
                }
                if (launchRows.Count > 0)
                {
                    groups.Add(new TrayChordGroup(TrayChordGroupKind.Launcher, T("Scenarios"), launchRows));
                    allRows.AddRange(launchRows);
                }
            }

            // 5. Mouse group
            var mouseRows = new List<TrayChordRow>();
            if (config.EnableContextMenu && MouseChord.TryParse(config.ContextMenuChord, out MouseChord mChord))
            {
                mouseRows.Add(new TrayChordRow(TrayChordGroupKind.Mouse, mChord.Display(T), T("text context menu")));
            }
            mouseRows.Add(new TrayChordRow(TrayChordGroupKind.Mouse, T("Left Click"), T("next layout")));
            mouseRows.Add(new TrayChordRow(TrayChordGroupKind.Mouse, T("Double Click"), T("settings")));

            if (mouseRows.Count > 0)
            {
                groups.Add(new TrayChordGroup(TrayChordGroupKind.Mouse, T("Mouse"), mouseRows));
                allRows.AddRange(mouseRows);
            }

            // 6. Windows group (read-only, works independently)
            var winRows = new List<TrayChordRow>();
            if (windowsHotkeys != null)
            {
                foreach (LanguageHotkeys.Entry entry in windowsHotkeys)
                {
                    if (entry.VirtualKey != 0)
                    {
                        string chord = LanguageHotkeys.FormatChord(entry.Modifiers, entry.VirtualKey);
                        string lang = LanguageHotkeys.LanguageName(entry.TargetHkl);
                        winRows.Add(new TrayChordRow(TrayChordGroupKind.Windows, chord, lang, isMuted: true,
                            kind: ChordKind.WindowsLanguage, id: entry.Id.ToString()));
                    }
                }
            }

            string toggle = windowsToggleCode ?? "3";
            if (toggle == "1")
                winRows.Add(new TrayChordRow(TrayChordGroupKind.Windows, "Alt+Shift", T("switch layout"), isMuted: true));
            else if (toggle == "2")
                winRows.Add(new TrayChordRow(TrayChordGroupKind.Windows, "Ctrl+Shift", T("switch layout"), isMuted: true));
            else if (toggle == "4")
                winRows.Add(new TrayChordRow(TrayChordGroupKind.Windows, "`", T("switch layout"), isMuted: true));

            if (winRows.Count > 0)
            {
                groups.Add(new TrayChordGroup(TrayChordGroupKind.Windows, T("Windows"), winRows));
                allRows.AddRange(winRows);
            }

            // Detect clashes among active CyrFlip rows
            for (int i = 0; i < allRows.Count; i++)
            {
                TrayChordRow a = allRows[i];
                if (a.ParsedChord == null || a.Group == TrayChordGroupKind.Windows || a.Group == TrayChordGroupKind.Mouse)
                    continue;

                for (int j = i + 1; j < allRows.Count; j++)
                {
                    TrayChordRow b = allRows[j];
                    if (b.ParsedChord == null || b.Group == TrayChordGroupKind.Windows || b.Group == TrayChordGroupKind.Mouse)
                        continue;

                    if (a.ParsedChord.Value.SameChord(b.ParsedChord.Value))
                    {
                        a.IsClash = true;
                        b.IsClash = true;
                    }
                }
            }

            return new TrayChordList(groups, allRows);
        }

        /// <summary>
        /// Builds the traditional lines for the 127-character native shell tooltip (szTip),
        /// maintaining 100% equivalence with historical tooltip output while sourcing from pure predicates.
        /// </summary>
        public static List<string> BuildTooltipLines(
            AppConfig config,
            IEnumerable<LauncherScenario>? scenarios,
            string uiLanguage,
            Func<string, string>? layoutName = null,
            Func<string, string>? translate = null)
        {
            Func<string, string> T = translate ?? (ru => Localization.Translate(uiLanguage, ru));
            Func<string, string> LName = layoutName ?? WorldLayouts.CodeForKlid;
            var lines = new List<string>();

            if (!config.EnableHotkeys)
            {
                lines.Add(T("hotkeys are off"));
            }
            else
            {
                if (config.EnableCaseHotkey && Hotkey.TryParse(config.CaseHotkey, out Hotkey caseChord))
                    lines.Add(caseChord.Display + " - " + T("case flip"));

                if (config.EnableHistoryHotkey && Hotkey.TryParse(config.ClipboardHistoryHotkey, out Hotkey histChord))
                    lines.Add(histChord.Display + " - " + T("clipboard history"));

                if (ChordBinding.IsQuickNotesActive(config) && Hotkey.TryParse(config.QuickNotesHotkey, out Hotkey qnChord))
                    lines.Add(qnChord.Display + " - " + T("quick notes"));

                if (ChordBinding.IsSymbolsActive(config) && Hotkey.TryParse(config.SymbolsHotkey, out Hotkey symChord))
                    lines.Add(symChord.Display + " - " + T("symbols window"));

                if (ChordBinding.IsTrafficLightActive(config) && Hotkey.TryParse(config.TrafficLightHotkey, out Hotkey tlChord))
                    lines.Add(tlChord.Display + " - " + T("availability traffic light"));

                if (ChordBinding.IsScreenshotActive(config) && Hotkey.TryParse(config.ScreenshotHotkey, out Hotkey scChord))
                    lines.Add(scChord.Display + " - " + T("screen region capture"));

                if (ChordBinding.IsScreenshotFullActive(config) && Hotkey.TryParse(config.ScreenshotFullHotkey, out Hotkey sfChord))
                    lines.Add(sfChord.Display + " - " + T("full screen capture"));

                if (ChordBinding.IsScreenshotAnnotateAreaActive(config) && Hotkey.TryParse(config.ScreenshotAnnotateAreaHotkey, out Hotkey saaChord))
                    lines.Add(saaChord.Display + " - " + T("area capture with drawing"));

                if (ChordBinding.IsScreenshotFullAnnotateActive(config) && Hotkey.TryParse(config.ScreenshotFullAnnotateHotkey, out Hotkey sfaChord))
                    lines.Add(sfaChord.Display + " - " + T("full screen capture with drawing"));

                if (ChordBinding.IsScreenshotFolderActive(config) && Hotkey.TryParse(config.ScreenshotFolderHotkey, out Hotkey scfChord))
                    lines.Add(scfChord.Display + " - " + T("screenshots folder"));

                if (ChordBinding.IsTextMenuActive(config) && Hotkey.TryParse(config.TextMenuHotkey, out Hotkey tmChord))
                    lines.Add(tmChord.Display + " - " + T("text context menu"));

                if (config.LayoutConversionProfiles != null)
                {
                    foreach (LayoutConversionProfile profile in config.LayoutConversionProfiles)
                    {
                        if (ChordBinding.IsConversionActive(config, profile) && Hotkey.TryParse(profile.Hotkey, out Hotkey cvChord))
                            lines.Add(cvChord.Display + " - " + LName(profile.SourceKlid) + " ⇄ " + LName(profile.TargetKlid));
                    }
                }

                if (config.EnableScenarioLauncher && scenarios != null)
                {
                    foreach (LauncherScenario scenario in scenarios)
                    {
                        if (ChordBinding.IsLauncherActive(config, scenario) && Hotkey.TryParse(scenario.Hotkey, out Hotkey scnChord))
                            lines.Add(scnChord.Display + " - " + scenario.Name);
                    }
                }

                if (config.EnableTranslate && config.TranslateProfiles != null)
                {
                    foreach (TranslationProfile profile in config.TranslateProfiles)
                    {
                        if (ChordBinding.IsTranslationActive(config, profile) && Hotkey.TryParse(profile.Hotkey, out Hotkey trChord))
                            lines.Add(trChord.Display + " - " + TranslationLanguages.Label(profile.TargetLang, uiLanguage));
                    }
                }
            }

            return lines;
        }
    }
}
