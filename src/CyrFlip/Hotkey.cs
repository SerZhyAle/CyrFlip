using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// A parsed hotkey: required modifier set plus a single trigger virtual-key.
    /// Parses strings like "Ctrl+Shift+T"; round-trips via <see cref="Display"/>.
    /// </summary>
    internal readonly struct Hotkey
    {
        // Virtual-key codes.
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12; // Alt
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public bool Ctrl { get; }
        public bool Shift { get; }
        public bool Alt { get; }
        public bool Win { get; }
        public int Vk { get; }
        public string KeyName { get; }

        public Hotkey(bool ctrl, bool shift, bool alt, bool win, int vk, string keyName)
        {
            Ctrl = ctrl; Shift = shift; Alt = alt; Win = win; Vk = vk; KeyName = keyName;
        }

        /// <summary>The default hotkey, Ctrl+Shift+T.</summary>
        public static Hotkey Default => new Hotkey(true, true, false, false, 0x54, "T");

        public string Display
        {
            get
            {
                var parts = new List<string>(4);
                if (Ctrl) parts.Add("Ctrl");
                if (Shift) parts.Add("Shift");
                if (Alt) parts.Add("Alt");
                if (Win) parts.Add("Win");
                parts.Add(KeyName);
                return string.Join("+", parts);
            }
        }

        /// <summary>Parse a hotkey string; falls back to <see cref="Default"/> if it has no usable trigger key.</summary>
        public static Hotkey Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Default;

            bool ctrl = false, shift = false, alt = false, win = false;
            int vk = 0;
            string keyName = "";

            foreach (string raw in text!.Split('+'))
            {
                string tok = raw.Trim();
                if (tok.Length == 0)
                    continue;

                switch (tok.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    case "win":
                    case "meta":
                    case "super": win = true; break;
                    default:
                        if (TryParseKey(tok, out int parsedVk, out string canonical))
                        {
                            vk = parsedVk;
                            keyName = canonical;
                        }
                        break;
                }
            }

            return vk == 0 ? Default : new Hotkey(ctrl, shift, alt, win, vk, keyName);
        }

        private static bool TryParseKey(string tok, out int vk, out string canonical)
        {
            string t = tok.ToUpperInvariant();

            // Single letter A-Z.
            if (t.Length == 1 && t[0] >= 'A' && t[0] <= 'Z')
            {
                vk = 0x41 + (t[0] - 'A');
                canonical = t;
                return true;
            }

            // Single digit 0-9.
            if (t.Length == 1 && t[0] >= '0' && t[0] <= '9')
            {
                vk = 0x30 + (t[0] - '0');
                canonical = t;
                return true;
            }

            // Function keys F1-F24.
            if (t.Length >= 2 && t[0] == 'F' && int.TryParse(t.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fn) && fn >= 1 && fn <= 24)
            {
                vk = 0x70 + (fn - 1);
                canonical = "F" + fn;
                return true;
            }

            // Named keys.
            switch (t)
            {
                case "SPACE": vk = 0x20; canonical = "Space"; return true;
                case "ENTER": case "RETURN": vk = 0x0D; canonical = "Enter"; return true;
                case "TAB": vk = 0x09; canonical = "Tab"; return true;
                case "ESC": case "ESCAPE": vk = 0x1B; canonical = "Esc"; return true;
                case "BACKSPACE": vk = 0x08; canonical = "Backspace"; return true;
                case "DELETE": case "DEL": vk = 0x2E; canonical = "Delete"; return true;
                case "INSERT": case "INS": vk = 0x2D; canonical = "Insert"; return true;
                case "HOME": vk = 0x24; canonical = "Home"; return true;
                case "END": vk = 0x23; canonical = "End"; return true;
            }

            vk = 0;
            canonical = "";
            return false;
        }
    }
}
