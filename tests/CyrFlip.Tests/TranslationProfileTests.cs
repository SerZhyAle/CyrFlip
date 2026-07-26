using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The translation table: how the starter row appears (once, and never again behind the user's
    /// back), how a row survives the registry round trip, and how a row's target language is resolved
    /// at the moment the chord fires. The seeding decision is pure, so none of this touches HKCU.
    /// </summary>
    public class TranslationProfileTests
    {
        [Fact]
        public void TheFirstEnableOffersAStarterRow()
        {
            Assert.True(AppConfig.NeedsTranslateRow(seeded: false, rowCount: 0));

            TranslationProfile row = AppConfig.TranslateRow(chordAvailable: true);
            Assert.Equal(TranslationLanguages.UiToken, row.TargetLang);
            Assert.Equal(AppConfig.DefaultTranslateHotkey, row.Hotkey);
            Assert.True(row.Enabled);
            Assert.True(row.IsUsable);
        }

        [Fact]
        public void WhenTheDefaultChordIsTakenTheRowArrivesWithoutOneRatherThanStealingIt()
        {
            TranslationProfile row = AppConfig.TranslateRow(chordAvailable: false);

            Assert.Equal("", row.Hotkey);
            Assert.False(row.IsUsable); // visible in the table, but inert until the user assigns a chord
        }

        [Fact]
        public void ATableEmptiedByTheUserIsNeverRefilled()
        {
            // The marker is "we already offered", not "the table is empty".
            Assert.False(AppConfig.NeedsTranslateRow(seeded: true, rowCount: 0));
            Assert.False(AppConfig.NeedsTranslateRow(seeded: false, rowCount: 2));
            Assert.False(AppConfig.NeedsTranslateRow(seeded: true, rowCount: 2));
        }

        [Fact]
        public void TheStoredTableIsReadBackRowForRow()
        {
            List<TranslationProfile> rows = AppConfig.ReadTranslationProfiles(
                "[{\"Id\":\"aaa\",\"TargetLang\":\"en\",\"Hotkey\":\"Ctrl+Alt+E\",\"Enabled\":false},"
                + "{\"Id\":\"bbb\",\"TargetLang\":\"active\",\"Hotkey\":\"Ctrl+Shift+F9\",\"Enabled\":true}]");

            Assert.Equal(2, rows.Count);
            Assert.Equal("aaa", rows[0].Id);
            Assert.Equal("en", rows[0].TargetLang);
            Assert.Equal("Ctrl+Alt+E", rows[0].Hotkey);
            Assert.False(rows[0].Enabled);
            Assert.Equal(TranslationLanguages.ActiveToken, rows[1].TargetLang);
            Assert.True(rows[1].Enabled);
        }

        [Fact]
        public void ARowWithNoIdGetsOneBecauseTheHookHandsTheIdBack()
        {
            List<TranslationProfile> rows = AppConfig.ReadTranslationProfiles(
                "[{\"TargetLang\":\"en\",\"Hotkey\":\"Ctrl+Alt+E\"}]");

            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].Id));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[")]
        [InlineData("not json")]
        public void AnUnreadableTableIsEmptyRatherThanAnExceptionOnStartup(string? json)
            => Assert.Empty(AppConfig.ReadTranslationProfiles(json));

        [Fact]
        public void TheComputedUsabilityFlagIsKeptOutOfTheStoredJson()
        {
            // [ScriptIgnore]; without it JavaScriptSerializer writes the read-only member into the
            // registry value and the next release has to keep parsing it.
            object[] attributes = typeof(TranslationProfile).GetProperty("IsUsable",
                BindingFlags.Public | BindingFlags.Instance)!.GetCustomAttributes(false);

            Assert.Contains(attributes, attribute => attribute.GetType().Name == "ScriptIgnoreAttribute");
        }

        [Fact]
        public void CloningARowCopiesEveryFieldSoAnEditCannotMutateALiveBinding()
        {
            // Enabled = false on purpose: a clone that dropped it would leave the hook holding a row
            // that looks live and swallows its chord for a row the user switched off.
            var row = new TranslationProfile { TargetLang = "de", Hotkey = "Ctrl+Alt+D", Enabled = false };
            TranslationProfile copy = row.Clone();
            row.Hotkey = "Ctrl+Alt+X";
            row.TargetLang = "fr";
            row.Enabled = true;

            Assert.Equal(row.Id, copy.Id);
            Assert.Equal("Ctrl+Alt+D", copy.Hotkey);
            Assert.Equal("de", copy.TargetLang);
            Assert.False(copy.Enabled);
        }

        [Fact]
        public void ARowWithoutAChordIsNotUsable()
        {
            Assert.False(new TranslationProfile { TargetLang = "en", Hotkey = "" }.IsUsable);
            Assert.False(new TranslationProfile { TargetLang = "", Hotkey = "Ctrl+Alt+E" }.IsUsable);
        }

        [Fact]
        public void TheUiTokenResolvesToTheLanguageOfTheInterface()
        {
            Assert.Equal("de", TranslationLanguages.Resolve(TranslationLanguages.UiToken, "Deutsch", "EN"));
            Assert.Equal("ru", TranslationLanguages.Resolve(TranslationLanguages.UiToken, "Русский", "EN"));
            // An unknown UI language falls back to English the same way Localization does.
            Assert.Equal("en", TranslationLanguages.Resolve(TranslationLanguages.UiToken, "Klingon", null));
        }

        [Fact]
        public void TheActiveTokenFollowsTheLayoutInTheTargetWindow()
        {
            Assert.Equal("uk", TranslationLanguages.Resolve(TranslationLanguages.ActiveToken, "English", "UK"));
            // No layout code yet (the indicator has not reported one): the UI language is the honest guess.
            Assert.Equal("en", TranslationLanguages.Resolve(TranslationLanguages.ActiveToken, "English", ""));
            Assert.Equal("en", TranslationLanguages.Resolve(TranslationLanguages.ActiveToken, "English", null));
        }

        [Fact]
        public void AnExplicitCodeIsUsedAsIs()
        {
            Assert.Equal("fr", TranslationLanguages.Resolve("fr", "Русский", "EN"));
            Assert.Equal("pl", TranslationLanguages.Resolve("PL", "Русский", "EN")); // outside the curated 13
        }

        [Fact]
        public void ThePromptGetsARealEnglishLanguageName()
        {
            Assert.Equal("English", TranslationLanguages.EnglishName("en"));
            Assert.Equal("Ukrainian", TranslationLanguages.EnglishName("uk"));
            Assert.Equal("Chinese", TranslationLanguages.EnglishName("zh"));
            // Not in the curated set, but Windows knows it - "translate into Polish" beats "into pl".
            Assert.Equal("Polish", TranslationLanguages.EnglishName("pl"));
            // Nonsense stays recognisable instead of throwing.
            Assert.Equal("QQ", TranslationLanguages.EnglishName("qq"));
            Assert.Equal("English", TranslationLanguages.EnglishName(""));
        }

        [Fact]
        public void TheLabelIsTheEndonymForACuratedLanguageAndAWordForTheTwoTokens()
        {
            Assert.Equal("English", TranslationLanguages.Label("en", "English"));
            Assert.Equal("Deutsch", TranslationLanguages.Label("de", "English"));
            Assert.Equal("Українська", TranslationLanguages.Label("uk", "Русский"));

            // The tokens are translated, so they read as words in the user's own UI language.
            Assert.NotEqual("", TranslationLanguages.Label(TranslationLanguages.UiToken, "English"));
            Assert.NotEqual(TranslationLanguages.Label(TranslationLanguages.UiToken, "English"),
                TranslationLanguages.Label(TranslationLanguages.ActiveToken, "English"));
        }
    }
}
