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
        public string CaseHotkey { get; set; } = "Ctrl+Shift+F11";
        public string ClipboardHistoryHotkey { get; set; } = "Ctrl+Shift+F10";
        public string UiLanguage { get; set; } = DefaultUiLanguage();
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
        /// <summary>Per-hotkey switches, so each of the three chords can be enabled independently.</summary>
        public bool EnableFlipHotkey { get; set; } = true;
        public bool EnableCaseHotkey { get; set; } = true;
        public bool EnableHistoryHotkey { get; set; } = true;
        /// <summary>
        /// When true, ignore the hotkeys while a remote-desktop client (mstsc/msrdc) window is focused,
        /// so the key reaches the remote session and the CyrFlip running there handles it. Prevents the
        /// double-instance clash when CyrFlip runs on both ends of an RDP connection.
        /// </summary>
        public bool DeferToRemoteDesktop { get; set; } = false;
        public int FlipCount { get; set; } = 0;
        public int CaseFlipCount { get; set; } = 0;

        /// <summary>
        /// UI language default for a fresh install (no saved value): follow the OS UI language.
        /// Only ru/uk get their own translations; every other OS language falls back to English.
        /// Fixes a Russian UI appearing on an English OS.
        /// </summary>
        private static string DefaultUiLanguage()
        {
            try
            {
                string iso = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
                if (iso == "ru") return "Русский";
                if (iso == "uk") return "Українська";
            }
            catch { /* fall through to English */ }
            return "English";
        }

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
                cfg.CaseHotkey = key.GetValue("CaseHotkey") as string ?? cfg.CaseHotkey;
                cfg.ClipboardHistoryHotkey = key.GetValue("ClipboardHistoryHotkey") as string ?? cfg.ClipboardHistoryHotkey;
                cfg.UiLanguage = key.GetValue("UiLanguage") as string ?? cfg.UiLanguage;
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
                cfg.EnableFlipHotkey = GetBool(key, "EnableFlipHotkey", cfg.EnableFlipHotkey);
                cfg.EnableCaseHotkey = GetBool(key, "EnableCaseHotkey", cfg.EnableCaseHotkey);
                cfg.EnableHistoryHotkey = GetBool(key, "EnableHistoryHotkey", cfg.EnableHistoryHotkey);
                cfg.DeferToRemoteDesktop = GetBool(key, "DeferToRemoteDesktop", cfg.DeferToRemoteDesktop);
                cfg.FlipCount = GetInt(key, "FlipCount", cfg.FlipCount);
                cfg.CaseFlipCount = GetInt(key, "CaseFlipCount", cfg.CaseFlipCount);
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
                key.SetValue("CaseHotkey", CaseHotkey, RegistryValueKind.String);
                key.SetValue("ClipboardHistoryHotkey", ClipboardHistoryHotkey, RegistryValueKind.String);
                key.SetValue("UiLanguage", UiLanguage, RegistryValueKind.String);
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
                key.SetValue("EnableFlipHotkey", EnableFlipHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableCaseHotkey", EnableCaseHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("EnableHistoryHotkey", EnableHistoryHotkey ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("DeferToRemoteDesktop", DeferToRemoteDesktop ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("FlipCount", FlipCount, RegistryValueKind.DWord);
                key.SetValue("CaseFlipCount", CaseFlipCount, RegistryValueKind.DWord);
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
