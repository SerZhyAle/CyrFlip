using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>The features the tour lets the user switch on and off on the spot.</summary>
    internal enum WelcomeSwitch
    {
        CaretMarker, MouseCursor, Conversion, CaseFlip, Screenshot,
        ClipboardHistory, QuickNotes, Symbols, Translate, QuickLaunch, ContextMenu,
    }

    /// <summary>Facts for a replayable tour, and the switches it flips; no Windows calls or persistence.</summary>
    internal sealed class WelcomeFacts
    {
        public readonly AppConfig Config;
        public readonly InputLayouts.Installed[] Layouts;
        public readonly bool Packaged, Transient, ShortcutsExist;

        public WelcomeFacts(AppConfig config, IEnumerable<InputLayouts.Installed> layouts,
            bool packaged, bool transient, bool shortcutsExist)
        {
            Config = config; Layouts = layouts.Take(4).ToArray();
            Packaged = packaged; Transient = transient; ShortcutsExist = shortcutsExist;
        }

        public string State(bool enabled) => Localization.Translate(Config.UiLanguage, enabled ? "On" : "Off");

        /// <summary>Whether a feature's switch is on right now. A chord-driven one also needs the master switch.</summary>
        public bool IsOn(WelcomeSwitch which, string? id = null)
        {
            switch (which)
            {
                case WelcomeSwitch.CaretMarker: return Config.EnableCaretOverlay;
                case WelcomeSwitch.MouseCursor: return Config.EnableCursorChange;
                case WelcomeSwitch.Conversion:
                    return Config.EnableHotkeys && Config.LayoutConversionProfiles.Any(p => p.Id == id && p.Enabled);
                case WelcomeSwitch.CaseFlip: return Config.EnableHotkeys && Config.EnableCaseHotkey;
                case WelcomeSwitch.Screenshot: return Config.EnableHotkeys && Config.EnableScreenshotHotkey;
                case WelcomeSwitch.ClipboardHistory: return Config.EnableClipboardHistory;
                case WelcomeSwitch.QuickNotes: return Config.EnableQuickNotes;
                case WelcomeSwitch.Symbols: return Config.EnableSymbols;
                case WelcomeSwitch.Translate: return Config.EnableTranslate;
                case WelcomeSwitch.QuickLaunch: return Config.EnableScenarioLauncher;
                case WelcomeSwitch.ContextMenu: return Config.EnableContextMenu;
                default: return false;
            }
        }

        /// <summary>
        /// Writes a switch into the live configuration and nothing else - no hook, no saving. The tray
        /// context applies the same switches with all their side effects; this is what a tour does when
        /// it has no context behind it (and what the tests drive). Turning a chord on turns the master on.
        /// </summary>
        public void Set(WelcomeSwitch which, string? id, bool on)
        {
            if (on && which is WelcomeSwitch.Conversion or WelcomeSwitch.CaseFlip or WelcomeSwitch.Screenshot)
                Config.EnableHotkeys = true;
            switch (which)
            {
                case WelcomeSwitch.CaretMarker: Config.EnableCaretOverlay = on; break;
                case WelcomeSwitch.MouseCursor: Config.EnableCursorChange = on; break;
                case WelcomeSwitch.Conversion:
                    foreach (var row in Config.LayoutConversionProfiles.Where(p => p.Id == id)) row.Enabled = on;
                    break;
                case WelcomeSwitch.CaseFlip: Config.EnableCaseHotkey = on; break;
                case WelcomeSwitch.Screenshot: Config.EnableScreenshotHotkey = on; break;
                case WelcomeSwitch.ClipboardHistory: Config.EnableClipboardHistory = on; break;
                case WelcomeSwitch.QuickNotes: Config.EnableQuickNotes = on; break;
                case WelcomeSwitch.Symbols: Config.EnableSymbols = on; break;
                case WelcomeSwitch.Translate: Config.EnableTranslate = on; break;
                case WelcomeSwitch.QuickLaunch: Config.EnableScenarioLauncher = on; break;
                case WelcomeSwitch.ContextMenu: Config.EnableContextMenu = on; break;
            }
        }

        /// <summary>One conversion-table row as the tour shows it.</summary>
        public sealed class ConversionSummary
        {
            public ConversionSummary(string id, string source, string sourceKlid, string target, string targetKlid, string hotkey, bool active)
            {
                Id = id; Source = source; SourceKlid = sourceKlid; Target = target; TargetKlid = targetKlid;
                Hotkey = hotkey; Active = active;
            }
            public string Id { get; }
            public string Source { get; }
            public string SourceKlid { get; }
            public string Target { get; }
            public string TargetKlid { get; }
            public string Hotkey { get; }
            /// <summary>True while the row's chord really fires: its own switch and the master switch are on.</summary>
            public bool Active { get; }
        }

        /// <summary>How many table rows the conversions page draws; the rest are one click away in settings.</summary>
        public const int MaxConversionRows = 3;

        public IReadOnlyList<ConversionSummary> ConversionRows()
            => Config.LayoutConversionProfiles.Take(MaxConversionRows).Select(row => new ConversionSummary(
                row.Id, LayoutCode(row.SourceKlid), row.SourceKlid, LayoutCode(row.TargetKlid), row.TargetKlid,
                row.Hotkey, Config.EnableHotkeys && row.Enabled)).ToArray();

        public static string LayoutCode(string klid)
        {
            if (klid.Length < 4) return klid;
            try { return LayoutIdentity.CodeFor(uint.Parse(klid.Substring(klid.Length - 4),
                System.Globalization.NumberStyles.HexNumber), klid); }
            catch { return klid; }
        }
    }
}
