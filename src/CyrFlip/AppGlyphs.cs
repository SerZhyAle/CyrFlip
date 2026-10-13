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
        public const string Back = "nav.back";
        public const string Forward = "nav.forward";
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
        public const string ConvertLayout = "action.convert-layout";
        public const string InputLanguage = "system.input-language";
        public const string Symbols = "feature.symbols";
        public const string ShortcutOverview = "app.shortcut-overview";
        public const string SelectionMenu = "feature.selection-menu";
        public const string NetworkMonitor = "feature.network-monitor";
        // The four states of a reachability tile (S0069): the shape carries the state, the fill role only
        // supplements it. The vocabulary's own words: ok "works, granted or verified", warning "works, but with a
        // risk", error "something failed and needs attention", unknown "no result yet".
        public const string StatusOk = "status.ok";
        public const string StatusWarning = "status.warning";
        public const string StatusError = "status.error";
        public const string StatusUnknown = "status.unknown";
        // Disclosure chevrons (WINDOWS-UI section 3: a collapsed group shows nav.expand, an open one
        // nav.collapse - the glyph names the action a click performs). Both records are rtl: fixed.
        public const string Expand = "nav.expand";
        public const string Collapse = "nav.collapse";
        // The command buttons' own meanings (WINDOWS-UI section 11 item 1): the verb of the caption, the
        // vocabulary's record for it. A button whose verb has no record waits for one (the quick notes'
        // "Transfer..." - PROPOSAL-2026-10-11-cyrflip-button-meanings), it never borrows a neighbour's.
        public const string Confirm = "action.confirm";
        public const string Cancel = "action.cancel";
        public const string Add = "action.add";
        public const string Remove = "action.remove";
        public const string Copy = "action.copy";
        public const string Paste = "action.paste";
        public const string Edit = "action.edit";
        public const string Export = "action.export";
        public const string Import = "action.import";
        public const string Refresh = "action.refresh";
        public const string Run = "action.run";
        public const string Reset = "action.reset";
        public const string Undo = "action.undo";
        public const string TestConnection = "action.test-connection";
        public const string Install = "action.install";
        public const string ChooseFolder = "action.choose-folder";
        public const string ChooseFile = "action.choose-file";
        public const string Verify = "action.verify";
        public const string ClearInput = "action.clear-input";
        public const string ClearAll = "action.clear-all";
        public const string Email = "action.email";
        public const string CreateNote = "action.create-note";
        public const string Folder = "content.folder";
        public const string Checklist = "content.checklist";
        public const string Help = "app.help";
    }
}
