using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>Read-only facts for a replayable tour; no Windows calls or persistence.</summary>
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

        public string State(bool enabled) => Localization.Translate(Config.UiLanguage, enabled ? "Включено" : "Выключено");
        public string ConversionText()
        {
            string language = Config.UiLanguage;
            var rows = Config.LayoutConversionProfiles;
            if (rows.Count == 0) return Localization.Translate(language, "Таблица конвертации пуста. Добавьте пару в настройках.");
            return string.Join("\n", rows.Select(row => Localization.Format(language,
                "{0} ⇄ {1}: {2} ({3}). Выделите текст и нажмите комбинацию.",
                LayoutCode(row.SourceKlid), LayoutCode(row.TargetKlid), row.Hotkey,
                State(Config.EnableHotkeys && row.Enabled))));
        }

        public static string LayoutCode(string klid)
        {
            if (klid.Length < 4) return klid;
            try { return LayoutIdentity.CodeFor(uint.Parse(klid.Substring(klid.Length - 4),
                System.Globalization.NumberStyles.HexNumber), klid); }
            catch { return klid; }
        }
    }
}
