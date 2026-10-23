using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// "Open the board when CyrFlip starts": the option that makes the traffic light's board appear by itself
    /// at every start. On by default, and only ever acts while the module is on.
    /// </summary>
    public class TrafficLightStartupTests
    {
        [Fact]
        public void TheOptionIsOnByDefault()
        {
            Assert.True(new AppConfig().TrafficLightShowAtStart);
        }

        [Theory]
        // module off: the board never opens by itself, whatever else is stored
        [InlineData(false, true, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, false, true, false)]
        // module on, option on: every start, even when the board was closed last time
        [InlineData(true, true, false, true)]
        [InlineData(true, true, true, true)]
        // module on, option off: the old behaviour - only if it was left visible
        [InlineData(true, false, true, true)]
        [InlineData(true, false, false, false)]
        public void TheBoardOpensAtStartPerTheRule(bool module, bool showAtStart, bool leftVisible, bool expected)
        {
            var config = new AppConfig
            {
                EnableTrafficLight = module,
                TrafficLightShowAtStart = showAtStart,
                TrafficLightVisible = leftVisible,
            };
            Assert.Equal(expected, TrafficLightLaunch.ShowBoard(config));
        }

        [Fact]
        public void ADefaultProfileOpensTheBoardAsSoonAsTheModuleIsSwitchedOn()
        {
            var config = new AppConfig { EnableTrafficLight = true };
            Assert.False(config.TrafficLightVisible);
            Assert.True(TrafficLightLaunch.ShowBoard(config));
        }

        [Fact]
        public void TheOptionIsWrittenWithTheModuleAndReadBack()
        {
            foreach (bool value in new[] { true, false })
            {
                var written = new MemoryConfigKey();
                new AppConfig { EnableTrafficLight = true, TrafficLightShowAtStart = value }.SaveTo(written);
                Assert.Equal(value ? 1 : 0, written.Values["TrafficLightShowAtStart"]);
                Assert.Equal(value, written.Load().TrafficLightShowAtStart);
            }
        }

        [Fact]
        public void AProfileFromBeforeTheOptionReadsAsOn()
        {
            // An update must not change what the user chose, and a user who never chose gets the default.
            var stored = new MemoryConfigKey();
            stored.Values["EnableTrafficLight"] = 1;
            stored.Values["TrafficLightVisible"] = 0;
            AppConfig loaded = stored.Load();
            Assert.True(loaded.TrafficLightShowAtStart);
            Assert.True(TrafficLightLaunch.ShowBoard(loaded));
        }

        [Fact]
        public void AnUnreadableValueFallsBackToOn()
        {
            var stored = new MemoryConfigKey();
            stored.Values["EnableTrafficLight"] = 1;
            stored.Values["TrafficLightShowAtStart"] = "not a number";
            Assert.True(stored.Load().TrafficLightShowAtStart);
        }

        [Fact]
        public void TheOptionIsExportedWithTheTrafficLightsModule()
        {
            SettingsFileEntry entry = Assert.Single(SettingsFileInventory.Exported, e => e.Key == "TrafficLightShowAtStart");
            Assert.Equal("trafficlight/module", entry.Group);
            Assert.Equal("Board opens at startup", entry.Caption);
            Assert.Equal(typeof(bool), entry.Property!.PropertyType);
        }

        [Fact]
        public void EveryCaptionOfTheOptionIsTranslatedInAllLanguages()
        {
            string[] sources =
            {
                "Open the board when CyrFlip starts",
                "On: the board appears at every start. Off: it reopens only if it was open when CyrFlip last closed.",
                "Board opens at startup",
            };
            foreach (string language in Localization.Names)
                foreach (string source in sources)
                {
                    string text = Localization.Translate(language, source);
                    Assert.False(string.IsNullOrWhiteSpace(text), language + ": " + source);
                    if (language != "English") Assert.NotEqual(source, text);
                }
            Assert.Equal(13, Localization.Names.Distinct().Count());
        }
    }
}
