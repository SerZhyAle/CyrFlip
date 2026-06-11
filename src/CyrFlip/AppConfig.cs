using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// Application configuration, loaded from config.json (spec §7.3). Any missing or
    /// malformed values fall back to defaults.
    /// </summary>
    internal sealed class AppConfig
    {
        public string Hotkey { get; set; } = "Ctrl+Shift+F12";
        public string[] Layouts { get; set; } = { "EN", "RU" };
        public int CursorSize { get; set; } = 24;

        /// <summary>
        /// Load config.json from %APPDATA%\CyrFlip\ first, otherwise from beside the exe.
        /// Returns defaults if no file is found or parsing fails.
        /// </summary>
        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            string? path = ResolvePath();
            if (path == null || !File.Exists(path))
                return cfg;

            try
            {
                var data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (data == null)
                    return cfg;

                if (data.TryGetValue("hotkey", out var h) && h is string hs && hs.Length > 0)
                    cfg.Hotkey = hs;
                if (data.TryGetValue("cursorSize", out var c) && c != null)
                    cfg.CursorSize = Convert.ToInt32(c);
                if (data.TryGetValue("layouts", out var l) && l is ArrayList arr)
                    cfg.Layouts = arr.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToArray();
            }
            catch
            {
                // Malformed config.json → keep defaults.
            }

            return cfg;
        }

        private static string? ResolvePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string roamed = Path.Combine(appData, "CyrFlip", "config.json");
            if (File.Exists(roamed))
                return roamed;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }
    }
}
