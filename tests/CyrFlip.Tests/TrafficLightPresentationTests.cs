using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Which settings-page change puts the traffic light's board in front of the user (ticket S0084): switching
    /// the module on and every setting the board draws do; the chord and the startup preference do not; and
    /// nothing does while the module is off.
    /// </summary>
    public class TrafficLightPresentationTests
    {
        private static AppConfig On() => new AppConfig { EnableTrafficLight = true };

        [Fact]
        public void SwitchingTheModuleOnShowsTheBoard()
        {
            Assert.True(TrafficLightPresentation.ShowsBoard(nameof(AppConfig.EnableTrafficLight), On()));
        }

        [Fact]
        public void NothingShowsTheBoardWhileTheModuleIsOff()
        {
            var off = new AppConfig { EnableTrafficLight = false };
            Assert.False(TrafficLightPresentation.ShowsBoard(nameof(AppConfig.EnableTrafficLight), off));
            foreach (string setting in TrafficLightPresentation.BoardSettings.Concat(TrafficLightPresentation.QuietSettings))
                Assert.False(TrafficLightPresentation.ShowsBoard(setting, off), setting);
        }

        [Theory]
        [InlineData(nameof(AppConfig.TrafficLightPinned))]
        [InlineData(nameof(AppConfig.TrafficLightTileSize))]
        [InlineData(nameof(AppConfig.TrafficLightColumns))]
        [InlineData(nameof(AppConfig.TrafficLightWindowTransparency))]
        [InlineData(nameof(AppConfig.TrafficLightTargets))]
        public void ASettingTheBoardDrawsShowsIt(string setting)
        {
            Assert.True(TrafficLightPresentation.ShowsBoard(setting, On()));
        }

        [Theory]
        [InlineData(nameof(AppConfig.EnableTrafficLightHotkey))]
        [InlineData(nameof(AppConfig.TrafficLightHotkey))]
        [InlineData(nameof(AppConfig.TrafficLightShowAtStart))]
        [InlineData("")]
        [InlineData("SomethingElse")]
        public void ASettingTheBoardDoesNotDrawLeavesItAlone(string setting)
        {
            Assert.False(TrafficLightPresentation.ShowsBoard(setting, On()));
        }

        [Fact]
        public void EveryTrafficLightSettingIsClassifiedExactlyOnce()
        {
            // The settings the module applies live, read from the apply table the settings file is held to: a
            // traffic light setting added there without a decision here fails this test.
            var applied = new HashSet<string>(SettingsApplyCoverage.Paths
                .Where(p => p.Value == "OnTrafficLightChanged" || p.Value == "ApplyTrafficLightTargets"
                            || p.Key == nameof(AppConfig.TrafficLightShowAtStart))
                .Select(p => p.Key));
            Assert.Contains(nameof(AppConfig.TrafficLightShowAtStart), applied);

            var classified = new List<string> { nameof(AppConfig.EnableTrafficLight) };
            classified.AddRange(TrafficLightPresentation.BoardSettings);
            classified.AddRange(TrafficLightPresentation.QuietSettings);
            Assert.Equal(classified.Count, classified.Distinct().Count());
            Assert.Equal(applied.OrderBy(k => k), classified.OrderBy(k => k));
        }
    }
}
