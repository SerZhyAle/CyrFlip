using System;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class WelcomeFactsTests
    {
        [Fact]
        public void ChordsAndSwitchesAreReadFromLiveConfiguration()
        {
            var config = new AppConfig { UiLanguage = "English" };
            config.LayoutConversionProfiles.Add(AppConfig.FlipRow("Alt+F7", true));
            var facts = new WelcomeFacts(config, Array.Empty<InputLayouts.Installed>(), true, true, false);
            var row = Assert.Single(facts.ConversionRows());
            Assert.Equal("Alt+F7", row.Hotkey); Assert.Equal("EN", row.Source); Assert.Equal("RU", row.Target); Assert.True(row.Active);
            config.EnableHotkeys = false;
            Assert.False(Assert.Single(facts.ConversionRows()).Active);
            config.EnableHotkeys = true; config.LayoutConversionProfiles[0].Enabled = false;
            Assert.False(Assert.Single(facts.ConversionRows()).Active);
            config.LayoutConversionProfiles.Clear();
            Assert.Empty(facts.ConversionRows());
            Assert.True(facts.Packaged); Assert.True(facts.Transient); Assert.False(facts.ShortcutsExist);
        }

        [Fact]
        public void ConversionRowsAreCappedForThePage()
        {
            var config = new AppConfig { UiLanguage = "English" };
            for (int i = 0; i < WelcomeFacts.MaxConversionRows + 2; i++) config.LayoutConversionProfiles.Add(AppConfig.FlipRow("Alt+F" + (i + 1), true));
            var facts = new WelcomeFacts(config, Array.Empty<InputLayouts.Installed>(), false, false, false);
            Assert.Equal(WelcomeFacts.MaxConversionRows, facts.ConversionRows().Count);
            Assert.Equal("Alt+F1", facts.ConversionRows()[0].Hotkey);
        }

        [Fact]
        public void OnlyFourInstalledBadgesInOrder()
        {
            var layouts = Enumerable.Range(0, 6).Select(i => new InputLayouts.Installed { Klid = i.ToString() }).ToArray();
            var facts = new WelcomeFacts(new AppConfig(), layouts, false, false, true);
            Assert.Equal(layouts.Take(4), facts.Layouts);
            Assert.Equal("EN", WelcomeFacts.LayoutCode("00000409"));
        }
    }
}
