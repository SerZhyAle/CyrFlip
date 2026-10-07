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
            Assert.Contains("Alt+F7 (On)", facts.ConversionText());
            config.EnableHotkeys = false;
            Assert.Contains("Alt+F7 (Off)", facts.ConversionText());
            config.EnableHotkeys = true; config.LayoutConversionProfiles[0].Enabled = false;
            Assert.Contains("(Off)", facts.ConversionText());
            config.LayoutConversionProfiles.Clear();
            Assert.Contains("empty", facts.ConversionText());
            Assert.True(facts.Packaged); Assert.True(facts.Transient); Assert.False(facts.ShortcutsExist);
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
