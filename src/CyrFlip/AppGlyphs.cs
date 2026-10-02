namespace CyrFlip
{
    /// <summary>
    /// The vocabulary ids (ICON-SET) the app names in code - the only place an id is spelled, so a
    /// glyph cannot enter under a made-up name. Every constant is a record in <c>Glyphs.g.cs</c>
    /// (<c>GlyphVendoringTests</c>); a meaning the vocabulary lacks waits for its record, it does not
    /// get a private picture here (ticket S0022).
    /// </summary>
    internal static class AppGlyphs
    {
        public const string Settings = "app.settings";
        public const string Info = "app.info";
        public const string Translate = "action.translate";
        public const string Search = "action.search";
        public const string Pin = "action.pin";
        public const string PinOff = "action.pin--off";
        public const string Delete = "action.delete";
        public const string Close = "nav.close";
        public const string Exit = "nav.exit";
        public const string MoveUp = "action.move-up";
        public const string MoveDown = "action.move-down";
        public const string Download = "action.download";
        public const string Apps = "content.apps";
        public const string LayoutIndicator = "feature.layout-indicator";
        public const string Shortcuts = "app.shortcuts";
        public const string ClipboardHistory = "feature.clipboard-history";
        public const string QuickLaunch = "feature.quick-launch";
        public const string Note = "content.note";
        public const string Screenshot = "system.screenshot";
    }
}
