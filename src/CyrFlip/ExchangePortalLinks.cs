using System;

namespace CyrFlip
{
    /// <summary>The portal pages this program points at (ticket S0074).</summary>
    internal enum ExchangePortalPage
    {
        /// <summary>The start page of the media exchange portal.</summary>
        Start,
        /// <summary>"Clipboard on the server": the two buttons, one object at a time, only the last value, what the server sees.</summary>
        Clipboard,
        /// <summary>"The exchange server": what it is and how to get an account on one.</summary>
        Server,
    }

    /// <summary>
    /// The pages of the media exchange portal that CyrFlip links to. The portal is hosted by the FastMediaSorter site
    /// and publishes English, Russian and Ukrainian only; every other interface language gets the English page, the
    /// way the phone app does. The addresses are full literals on purpose - a link that is built from pieces cannot be
    /// found when a page is renamed - and <c>ExchangePortalLinksTests</c> holds their shape.
    ///
    /// <para>The portal stays another product's: CyrFlip keeps no copy of its text, only the address. Until
    /// the portal publishes a page, its address answers 404; the settings page and the guide say what the thing is
    /// in their own words, so the link adds to them and never carries them.</para>
    /// </summary>
    internal static class ExchangePortalLinks
    {
        public const string Base = "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/";

        public static string For(ExchangePortalPage page, string languageCode)
        {
            switch (page)
            {
                case ExchangePortalPage.Clipboard:
                    return languageCode == "ru" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-clipboard-ru.html"
                         : languageCode == "uk" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-clipboard-uk.html"
                         : "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-clipboard.html";
                case ExchangePortalPage.Server:
                    return languageCode == "ru" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server-ru.html"
                         : languageCode == "uk" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server-uk.html"
                         : "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/exchange-server.html";
                default:
                    return languageCode == "ru" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/index-ru.html"
                         : languageCode == "uk" ? "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/index-uk.html"
                         : "https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/";
            }
        }

        /// <summary>The portal link for an interface language given by its endonym ("Русский", "Deutsch", ..).</summary>
        public static string ForLanguage(ExchangePortalPage page, string uiLanguage)
        {
            int index = Array.IndexOf(Localization.Names, uiLanguage);
            string code = index >= 0 && index < Localization.Codes.Length ? Localization.Codes[index] : "en";
            return For(page, code);
        }
    }
}
