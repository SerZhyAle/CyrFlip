using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure half of the theme (ticket S0020 section 8, test 3): the stored token and how a mode meets
    /// Windows. Windows' preference and high contrast are arguments, so the matrix runs the same on a
    /// light, a dark and a contrast machine.
    /// </summary>
    public class ThemeModeTests
    {
        [Theory]
        [InlineData("System", "system")]
        [InlineData("Light", "light")]
        [InlineData("Dark", "dark")]
        public void EveryModeRoundTripsThroughItsToken(string name, string token)
        {
            ThemeMode mode = M(name);
            Assert.Equal(token, ThemeModes.Token(mode));
            Assert.Equal(mode, ThemeModes.Parse(token));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("auto")]      // v0.1's name for it - never shipped, and not a value to honour
        [InlineData("Sepia")]
        [InlineData("1")]
        public void AnythingElseReadsAsSystem(string? token)
            => Assert.Equal(ThemeMode.System, ThemeModes.Parse(token));

        [Theory]
        [InlineData(" Dark ")]
        [InlineData("DARK")]
        public void TheTokenIsReadLeniently(string token)
            => Assert.Equal(ThemeMode.Dark, ThemeModes.Parse(token));

        [Theory]
        [InlineData("System", false, "Light")]
        [InlineData("System", true, "Dark")]
        [InlineData("Light", false, "Light")]
        [InlineData("Light", true, "Light")]
        [InlineData("Dark", false, "Dark")]
        [InlineData("Dark", true, "Dark")]
        public void AnExplicitModeIsItselfAndSystemAsksWindows(string mode, bool systemDark, string expected)
            => Assert.Equal(K(expected), ThemeModes.Resolve(M(mode), systemDark, highContrast: false));

        [Theory]
        [InlineData("System", false)]
        [InlineData("System", true)]
        [InlineData("Light", true)]
        [InlineData("Dark", false)]
        public void HighContrastWinsOverEveryMode(string mode, bool systemDark)
            => Assert.Equal(ThemeKind.HighContrast, ThemeModes.Resolve(M(mode), systemDark, highContrast: true));

        // The enums are internal, and a public theory cannot take them as parameters.
        private static ThemeMode M(string name) => (ThemeMode)System.Enum.Parse(typeof(ThemeMode), name);
        private static ThemeKind K(string name) => (ThemeKind)System.Enum.Parse(typeof(ThemeKind), name);

        [Fact]
        public void AFreshConfigFollowsWindows()
            => Assert.Equal(ThemeModes.SystemToken, new AppConfig().Theme);

        private sealed class FakeKey : IConfigKey
        {
            public readonly System.Collections.Generic.Dictionary<string, object> Values
                = new System.Collections.Generic.Dictionary<string, object>();
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, Microsoft.Win32.RegistryValueKind kind) => Values[name] = value;
        }

        [Theory]
        [InlineData("dark", "dark")]
        [InlineData("light", "light")]
        [InlineData("system", "system")]
        [InlineData("auto", "system")]     // an unknown token is normalized on the way in
        public void TheThemeRoundTripsThroughTheRegistry(string stored, string loaded)
        {
            var key = new FakeKey();
            key.Values["Theme"] = stored;
            AppConfig config = AppConfig.LoadFrom(key, out _);
            Assert.Equal(loaded, config.Theme);

            var written = new FakeKey();
            config.SaveTo(written);
            Assert.Equal(loaded, written.Values["Theme"]);
        }

        [Fact]
        public void AThemeOfTheWrongKindFallsBackToSystem()
        {
            var key = new FakeKey();
            key.Values["Theme"] = 1;   // a DWORD where a string belongs
            Assert.Equal(ThemeModes.SystemToken, AppConfig.LoadFrom(key, out _).Theme);
        }

        /// <summary>The manager is inert until the tray context starts it - which no test ever does.</summary>
        [Fact]
        public void TheManagerPaintsNothingBeforeItIsInitialized()
        {
            Assert.False(ThemeManager.IsInitialized);
            Assert.Same(ThemePalette.Light, ThemeManager.Palette);
        }
    }
}
