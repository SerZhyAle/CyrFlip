using System.Drawing;

namespace CyrFlip
{
    /// <summary>
    /// The four glyphs of the clipboard history strip - search, close, pin, delete - drawn from the
    /// vocabulary in the theme's colours (ticket S0022 A3). Split from the window so that the colour each
    /// one takes on each row of each theme is testable without a window, a service or a clipboard.
    /// </summary>
    internal static class HistoryStripGlyphs
    {
        /// <summary>Tier 20 of ICON-RENDER rule 5: a dense row.</summary>
        public const int Size = 20;

        /// <summary>Search, close and delete take the text colour; the two are told apart by shape (ICON-SET rule 2: close is not delete).</summary>
        public static Color ActionColor(ThemePalette palette) => palette.TextPrimary;

        /// <summary>
        /// A toggle shows its state (ICON-RENDER rule 4): pinned is the filled pin in the danger role the
        /// strip has always used for it, not pinned is the outlined pin in the muted text colour - never a
        /// colour baked white, which drew nothing on the light rows.
        /// </summary>
        public static Color PinColor(bool pinned, ThemePalette palette) => pinned ? palette.Danger : palette.TextMuted;

        public static string PinId(bool pinned) => pinned ? AppGlyphs.Pin : AppGlyphs.PinOff;

        public static Rectangle Centered(Rectangle zone)
            => new Rectangle(zone.X + (zone.Width - Size) / 2, zone.Y + (zone.Height - Size) / 2, Size, Size);

        public static bool DrawSearch(Graphics g, Rectangle zone, ThemePalette palette)
            => GlyphRenderer.Draw(g, AppGlyphs.Search, Centered(zone), ActionColor(palette));

        public static bool DrawClose(Graphics g, Rectangle zone, ThemePalette palette)
            => GlyphRenderer.Draw(g, AppGlyphs.Close, Centered(zone), ActionColor(palette));

        public static bool DrawDelete(Graphics g, Rectangle zone, ThemePalette palette)
            => GlyphRenderer.Draw(g, AppGlyphs.Delete, Centered(zone), ActionColor(palette));

        public static bool DrawPin(Graphics g, Rectangle zone, bool pinned, ThemePalette palette)
            => GlyphRenderer.Draw(g, PinId(pinned), Centered(zone), PinColor(pinned, palette));
    }
}
