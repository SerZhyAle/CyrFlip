using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The small surfaces around the exchange clipboard (ticket S0074): the two pipe commands the desktop shortcuts and
    /// the Jump List send, the portal addresses, and the shortcut files. The wire, the trust, the sealing and the
    /// clipboard sides each have their own test class.
    /// </summary>
    public class ExchangeClipboardTests
    {
        // ---- the pipe commands (APP-ACTIVATION: the command set is closed and strict) ----

        [Theory]
        [InlineData("/clipboard-send")]
        [InlineData("/CLIPBOARD-SEND")]
        [InlineData(" /clipboard-send ")]
        public void TheSendCommandParses(string arg)
            => Assert.Equal(LauncherIpc.ClipboardSendCommand, LauncherIpc.ParseCommand(new[] { arg }));

        [Theory]
        [InlineData("/clipboard-get")]
        [InlineData("/CLIPBOARD-GET")]
        [InlineData(" /clipboard-get ")]
        public void TheGetCommandParses(string arg)
            => Assert.Equal(LauncherIpc.ClipboardGetCommand, LauncherIpc.ParseCommand(new[] { arg }));

        [Theory]
        [InlineData("/clipboard-send now")]
        [InlineData("/clipboard-sendall")]
        [InlineData("/clipboard")]
        [InlineData("clipboard-send")]
        [InlineData("/clipboard-get:{00000000-0000-0000-0000-000000000000}")]
        public void ANearMissIsNotACommand(string arg)
            => Assert.Null(LauncherIpc.ParseCommand(new[] { arg }));

        [Fact]
        public void TheTwoCommandsAreApplicationCommandsHonouredWhateverTheLauncherSwitchSays()
        {
            Assert.True(LauncherIpc.IsAccepted(LauncherIpc.ClipboardSendCommand, launcherCommandsEnabled: false));
            Assert.True(LauncherIpc.IsAccepted(LauncherIpc.ClipboardGetCommand, launcherCommandsEnabled: false));
            Assert.False(LauncherIpc.IsAccepted(LauncherIpc.RunPrefix + Guid.NewGuid().ToString("D"), launcherCommandsEnabled: false));
        }

        // ---- the shortcuts ----

        [Fact]
        public void EachShortcutRunsItsOwnCommand()
        {
            Assert.Equal("/clipboard-send", ExchangeShortcuts.CommandOf(ExchangeShortcutKind.Send));
            Assert.Equal("/clipboard-get", ExchangeShortcuts.CommandOf(ExchangeShortcutKind.Get));
            Assert.Equal(AppGlyphs.SendClipboard, ExchangeShortcuts.GlyphOf(ExchangeShortcutKind.Send));
            Assert.Equal(AppGlyphs.ReceiveClipboard, ExchangeShortcuts.GlyphOf(ExchangeShortcutKind.Get));
        }

        [Fact]
        public void AShortcutFileIsNamedInTheInterfaceLanguageAndIsAValidFileName()
        {
            foreach (string language in Localization.Names)
                foreach (ExchangeShortcutKind kind in Enum.GetValues(typeof(ExchangeShortcutKind)))
                {
                    string name = ExchangeShortcuts.FileNameOf(kind, s => Localization.Translate(language, s));
                    Assert.EndsWith(".lnk", name);
                    Assert.Equal(name, Path.GetFileName(name));
                    Assert.True(name.Length > 5);
                    Assert.DoesNotContain(name.Substring(0, name.Length - 4), Path.GetInvalidFileNameChars().Select(c => c.ToString()).ToArray());
                }
            Assert.Equal("Отправить буфер на сервер.lnk", ExchangeShortcuts.FileNameOf(ExchangeShortcutKind.Send, s => Localization.Translate("Русский", s)));
            Assert.Equal("Получить буфер с сервера.lnk", ExchangeShortcuts.FileNameOf(ExchangeShortcutKind.Get, s => Localization.Translate("Русский", s)));
        }

        // ---- the portal ----

        [Fact]
        public void EveryPortalAddressIsAFullLiteralOnThePortalAndTheThreeLanguagesDiffer()
        {
            foreach (ExchangePortalPage page in Enum.GetValues(typeof(ExchangePortalPage)))
            {
                string en = ExchangePortalLinks.For(page, "en");
                string ru = ExchangePortalLinks.For(page, "ru");
                string uk = ExchangePortalLinks.For(page, "uk");
                Assert.StartsWith(ExchangePortalLinks.Base, en);
                Assert.StartsWith(ExchangePortalLinks.Base, ru);
                Assert.StartsWith(ExchangePortalLinks.Base, uk);
                Assert.Equal(3, new[] { en, ru, uk }.Distinct().Count());
                Assert.All(new[] { en, ru, uk }, u => Assert.True(Uri.IsWellFormedUriString(u, UriKind.Absolute), u));
            }
        }

        [Fact]
        public void TheClipboardPageIsTheOneTheOtherProgramsNameAndOtherLanguagesGetEnglish()
        {
            Assert.EndsWith("/exchange-clipboard.html", ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "en"));
            Assert.EndsWith("/exchange-clipboard-ru.html", ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "ru"));
            Assert.EndsWith("/exchange-clipboard-uk.html", ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "uk"));
            Assert.Equal(ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "en"), ExchangePortalLinks.ForLanguage(ExchangePortalPage.Clipboard, "Deutsch"));
            Assert.Equal(ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "ru"), ExchangePortalLinks.ForLanguage(ExchangePortalPage.Clipboard, "Русский"));
            Assert.Equal(ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "uk"), ExchangePortalLinks.ForLanguage(ExchangePortalPage.Clipboard, "Українська"));
            Assert.Equal(ExchangePortalLinks.For(ExchangePortalPage.Clipboard, "en"), ExchangePortalLinks.ForLanguage(ExchangePortalPage.Clipboard, "not a language"));
        }

        // ---- the module's defaults ----

        [Fact]
        public void TheModuleIsOffByDefaultWithNoChordAndNothingRegisteredUntilItIsOn()
        {
            var cfg = new AppConfig();
            Assert.False(cfg.EnableExchangeClipboard);
            Assert.Equal("", cfg.ExchangeClipboardSendHotkey);
            Assert.Equal("", cfg.ExchangeClipboardGetHotkey);
            Assert.Empty(cfg.ExchangeClipboardAccounts);
            Assert.False(ChordBinding.ExchangeSendLive(cfg));
            Assert.False(ChordBinding.ExchangeGetLive(cfg));
            cfg.EnableExchangeClipboard = true;
            Assert.True(ChordBinding.ExchangeSendLive(cfg)); // the switch is on; a chord is live only once it parses
            Assert.False(ChordBinding.IsExchangeSendActive(cfg));
        }
    }
}
