using System;
using System.Globalization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// A fresh install must open in the language Windows' own interface is in, and fall back to English -
    /// never to Russian, which is only the source language of the translation table. Everything here is
    /// pure: the culture is passed in rather than read from the machine, so the whole matrix is testable
    /// on any machine.
    /// </summary>
    public class DefaultLanguageTests
    {
        [Fact]
        public void EverySupportedLanguageIsRecognisedFromItsCulture()
        {
            for (int i = 0; i < Localization.Codes.Length; i++)
            {
                var culture = new CultureInfo(Localization.Codes[i]);
                Assert.Equal(Localization.Names[i], Localization.MatchLanguage(culture));
            }
        }

        [Theory]
        [InlineData("de-AT", "Deutsch")]      // Austrian German, not the de-DE the table lists
        [InlineData("pt-BR", "Português")]    // Brazilian Portuguese
        [InlineData("zh-Hans-CN", "中文")]     // a script-qualified Chinese
        [InlineData("ar-EG", "العربية")]
        [InlineData("en-GB", "English")]
        [InlineData("ru-RU", "Русский")]
        public void ARegionalVariantResolvesToItsLanguage(string culture, string expected)
            => Assert.Equal(expected, Localization.MatchLanguage(new CultureInfo(culture)));

        [Theory]
        [InlineData("pl-PL")]
        [InlineData("ja-JP")]
        [InlineData("sv-SE")]
        public void AnUntranslatedLanguageIsNotClaimed(string culture)
            => Assert.Null(Localization.MatchLanguage(new CultureInfo(culture)));

        [Fact]
        public void NothingUsableMeansEnglish()
        {
            Assert.Null(Localization.MatchLanguage(CultureInfo.InvariantCulture));
            Assert.Null(Localization.MatchLanguage(null));
        }

        [Fact]
        public void TheDefaultIsAlwaysALanguageTheSettingsWindowOffers()
        {
            // Whatever this machine runs, the answer has to be selectable in the language combo box.
            Assert.Contains(Localization.DefaultLanguage(), Localization.Names);
        }
    }
}
