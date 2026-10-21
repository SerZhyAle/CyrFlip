namespace CyrFlip
{
    /// <summary>Maintained resource inventory (SETTINGS-NAVIGATION section 3). No work runs when read.</summary>
    internal static class AboutResources
    {
        internal sealed class Resource
        {
            internal Resource(string id, string caption, string url) { Id = id; Caption = caption; Url = url; }
            internal string Id { get; }
            internal string Caption { get; }
            internal string Url { get; }
        }

        internal static readonly Resource[] Product =
        {
            new Resource("site", "App website: serzhyale.github.io/CyrFlip", "https://serzhyale.github.io/CyrFlip/"),
            new Resource("repository", "Source repository", "https://github.com/SerZhyAle/CyrFlip"),
            new Resource("license", "Licence: MIT", "https://github.com/SerZhyAle/CyrFlip/blob/main/LICENSE"),
            new Resource("privacy", "Privacy policy", "https://serzhyale.github.io/CyrFlip/privacy.html"),
            new Resource("notices", "Third-party notices: Material Icons glyphs (Apache-2.0)", "https://github.com/SerZhyAle/CyrFlip/blob/main/THIRD-PARTY-NOTICES.txt"),
            new Resource("downloads", "Downloads and updates", "https://github.com/SerZhyAle/CyrFlip/releases/latest"),
            new Resource("contact", "Support", "mailto:sza@ukr.net"),
            new Resource("developer", "Developer website: sza.od.ua", "https://sza.od.ua/"),
        };

        internal static readonly Resource[] Dependencies =
        {
            new Resource("guide", "Help", "https://serzhyale.github.io/CyrFlip/guide.html"),
            new Resource("ollama", "Ollama: local translation", "https://ollama.com/"),
            new Resource("yt-dlp", "yt-dlp: video downloads", "https://github.com/yt-dlp/yt-dlp"),
            // What the program connects to when the clipboard on the server is on: the credit of the exchange server,
            // which wears its mark (EXCHANGE-MARK rule 8 (e), S0079 M11) and is named as itself, by its full name
            // (rule 14). The address is the portal's page in English; the About page opens the one of the interface language.
            new Resource(ExchangeServerId, ExchangeMark.FullName, ExchangePortalLinks.For(ExchangePortalPage.Server, "en")),
        };

        /// <summary>The id of the credit that shows the exchange server's mark.</summary>
        internal const string ExchangeServerId = "exchange-server";

        internal static readonly Resource[] Related =
        {
            new Resource("extension", "VS Code extension: layout at the caret", "https://marketplace.visualstudio.com/items?itemName=SerZhyAle.cyrflip-vscode"),
            new Resource("launcher", "OneClickRunner: scenario launcher", "https://github.com/SerZhyAle/OneClickRunner"),
        };
    }
}
