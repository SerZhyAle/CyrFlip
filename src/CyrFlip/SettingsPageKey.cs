using System;

namespace CyrFlip
{
    /// <summary>
    /// The stable key of a settings page. The names are what <c>SettingsPageId</c> stores, so a name is
    /// never renamed or reused; a retired page keeps its member (<see cref="HotkeysOverview"/>) and is
    /// read as its successor by <see cref="SettingsPageKeys.Parse"/>.
    /// </summary>
    internal enum SettingsPageKey
    {
        General, Indicators, Hotkeys, Conversions, WindowsLanguages, Clipboard,
        QuickNotes, Graphics, Translate, Launcher, About, Symbols,
        /// <summary>Retired by S0062: the overview and the old Hotkeys page are one page, <see cref="Hotkeys"/>.</summary>
        HotkeysOverview,
        /// <summary>CyrFlip's own menu over the selected text (the context menu and its keyboard chord).</summary>
        SelectionMenu,
    }

    /// <summary>The pure half of page navigation: reading a stored key, and the one-time legacy index table.</summary>
    internal static class SettingsPageKeys
    {
        /// <summary>
        /// The order of the page list before S0062 (S0062 section 3): the only meaning a bare index ever had.
        /// Used once, to turn a remembered <c>SettingsTab</c> or a numeric viewport row into a key; the page
        /// order itself is <see cref="DisplayOrder"/>.
        /// </summary>
        private static readonly SettingsPageKey[] LegacyOrder =
        {
            SettingsPageKey.General, SettingsPageKey.Indicators, SettingsPageKey.Hotkeys, SettingsPageKey.HotkeysOverview,
            SettingsPageKey.Conversions, SettingsPageKey.WindowsLanguages, SettingsPageKey.Clipboard, SettingsPageKey.QuickNotes,
            SettingsPageKey.Graphics, SettingsPageKey.Translate, SettingsPageKey.Launcher, SettingsPageKey.Symbols,
            SettingsPageKey.About,
        };

        /// <summary>The page list, top to bottom (S0062 section 4): the pillars of POSITIONING.md in order, About last.</summary>
        internal static readonly SettingsPageKey[] DisplayOrder =
        {
            SettingsPageKey.General, SettingsPageKey.Indicators, SettingsPageKey.WindowsLanguages, SettingsPageKey.Conversions,
            SettingsPageKey.SelectionMenu, SettingsPageKey.Symbols, SettingsPageKey.Translate, SettingsPageKey.Clipboard,
            SettingsPageKey.QuickNotes, SettingsPageKey.Graphics, SettingsPageKey.Launcher, SettingsPageKey.Hotkeys,
            SettingsPageKey.About,
        };

        /// <summary>
        /// A stored page name, or false for anything that is not one: empty, unknown, or a number (which
        /// <c>Enum.TryParse</c> would accept as an ordinal). The retired <c>HotkeysOverview</c> reads as
        /// <see cref="SettingsPageKey.Hotkeys"/>, so a profile that last left the overview opens on the merged page.
        /// </summary>
        internal static bool TryParse(string? name, out SettingsPageKey key)
        {
            key = SettingsPageKey.General;
            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(SettingsPageKey), name!)) return false;
            key = (SettingsPageKey)Enum.Parse(typeof(SettingsPageKey), name!);
            if (key == SettingsPageKey.HotkeysOverview) key = SettingsPageKey.Hotkeys;
            return true;
        }

        /// <summary>As <see cref="TryParse"/>, with <see cref="SettingsPageKey.General"/> for an unknown name.</summary>
        internal static SettingsPageKey Parse(string? name)
            => TryParse(name, out SettingsPageKey key) ? key : SettingsPageKey.General;

        /// <summary>The page a pre-S0062 index named, or null for an index outside the old list.</summary>
        internal static SettingsPageKey? FromLegacyIndex(int index)
        {
            if (index < 0 || index >= LegacyOrder.Length) return null;
            // The overview page (3) merged into Hotkeys: a remembered position there opens the merged page.
            SettingsPageKey key = LegacyOrder[index];
            return key == SettingsPageKey.HotkeysOverview ? SettingsPageKey.Hotkeys : key;
        }

        /// <summary>
        /// A page column of a stored viewport row: a key name (written since S0062) or a legacy numeric
        /// index. False when it is neither.
        /// </summary>
        internal static bool TryParseViewportPage(string? token, out SettingsPageKey key)
        {
            key = SettingsPageKey.General;
            if (string.IsNullOrEmpty(token)) return false;
            if (int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                SettingsPageKey? legacy = FromLegacyIndex(index);
                if (legacy == null) return false;
                key = legacy.Value;
                return true;
            }
            return TryParse(token, out key);
        }
    }
}
