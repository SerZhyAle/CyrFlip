using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The first-enable wiring, not just the decision behind it: switching the translator on has to
    /// hand the user exactly one starter row, once, and a table they emptied on purpose must stay
    /// empty however often they toggle the switch. The decision is pure (the chord check is a
    /// delegate), so this runs without a tray app and without touching HKCU.
    /// </summary>
    public class TranslationSeedWiringTests
    {
        private static bool Free(Hotkey chord) => true;
        private static bool Taken(Hotkey chord) => false;

        [Fact]
        public void TheFirstEnableAddsOneRowOnTheDefaultChordAndMarksItSeeded()
        {
            var config = new AppConfig { EnableTranslate = true };

            Assert.True(AppConfig.SeedTranslateRow(config, Free));

            TranslationProfile row = Assert.Single(config.TranslateProfiles);
            Assert.Equal(AppConfig.DefaultTranslateHotkey, row.Hotkey);
            Assert.Equal(TranslationLanguages.UiToken, row.TargetLang);
            Assert.True(row.Enabled);
            Assert.True(config.TranslateSeeded);
        }

        [Fact]
        public void WhenTheDefaultChordIsTakenTheRowArrivesWithoutOne()
        {
            var config = new AppConfig { EnableTranslate = true };

            Assert.True(AppConfig.SeedTranslateRow(config, Taken));

            TranslationProfile row = Assert.Single(config.TranslateProfiles);
            Assert.Equal("", row.Hotkey);   // visible in the table, inert - never stolen from its owner
            Assert.False(row.IsUsable);
        }

        [Fact]
        public void TheSecondEnableAddsNothing()
        {
            var config = new AppConfig { EnableTranslate = true };
            AppConfig.SeedTranslateRow(config, Free);

            Assert.False(AppConfig.SeedTranslateRow(config, Free));
            Assert.Single(config.TranslateProfiles);
        }

        [Fact]
        public void ATableTheUserEmptiedIsNeverRefilled()
        {
            var config = new AppConfig { EnableTranslate = true };
            AppConfig.SeedTranslateRow(config, Free);
            config.TranslateProfiles.Clear();   // the user deleted the row on purpose

            Assert.False(AppConfig.SeedTranslateRow(config, Free));
            Assert.Empty(config.TranslateProfiles);
        }

        [Fact]
        public void NothingIsSeededWhileTheFeatureIsOff()
        {
            var config = new AppConfig { EnableTranslate = false };

            Assert.False(AppConfig.SeedTranslateRow(config, Free));
            Assert.Empty(config.TranslateProfiles);
            Assert.False(config.TranslateSeeded); // the offer is still owed for the day it is switched on
        }

        [Fact]
        public void AConfigThatAlreadyHasRowsIsLeftAlone()
        {
            var config = new AppConfig { EnableTranslate = true };
            config.TranslateProfiles.Add(new TranslationProfile { TargetLang = "en", Hotkey = "Ctrl+Alt+E" });

            Assert.False(AppConfig.SeedTranslateRow(config, Free));
            Assert.Single(config.TranslateProfiles);
        }
    }
}
