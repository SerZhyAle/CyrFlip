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

        public string Hotkey { get; set; } = "Ctrl+Shift+F12";
        public int CursorSize { get; set; } = 24;
        public bool EnableCursorChange { get; set; } = false;
        public bool EnableCaretOverlay { get; set; } = true;
        public bool CaretDotMode { get; set; } = false;
        public int FlipCount { get; set; } = 0;

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath);
                if (key == null)
                {
                    cfg.MigrateFromJson();
                    return cfg;
                }
                cfg.Hotkey = key.GetValue("Hotkey") as string ?? cfg.Hotkey;
                cfg.CursorSize = GetInt(key, "CursorSize", cfg.CursorSize);
                cfg.EnableCursorChange = GetBool(key, "EnableCursorChange", cfg.EnableCursorChange);
                cfg.EnableCaretOverlay = GetBool(key, "EnableCaretOverlay", cfg.EnableCaretOverlay);
                cfg.CaretDotMode = GetBool(key, "CaretDotMode", cfg.CaretDotMode);
                cfg.FlipCount = GetInt(key, "FlipCount", cfg.FlipCount);
            }
            catch { /* keep defaults */ }
            return cfg;
        }

        public void Save()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
                if (key == null) return;
                key.SetValue("Hotkey", Hotkey, RegistryValueKind.String);
                key.SetValue("CursorSize", CursorSize, RegistryValueKind.DWord);
                key.SetValue("EnableCursorChange", EnableCursorChange ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableCaretOverlay", EnableCaretOverlay ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("CaretDotMode", CaretDotMode ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
            }
            catch { /* best effort */ }
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

        private void MigrateFromJson()
        {
            try
            {
                string? path = ResolveJsonPath();
                if (path == null || !File.Exists(path))
                    return;
                var data = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (data == null) return;
                if (data.TryGetValue("hotkey", out var h) && h is string hs && hs.Length > 0)
                    Hotkey = hs;
                if (data.TryGetValue("cursorSize", out var c) && c != null)
                    CursorSize = Convert.ToInt32(c);
                Save(); // persist migrated values so future loads hit the registry
            }
            catch { }
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
    }
}
