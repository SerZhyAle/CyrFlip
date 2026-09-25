using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CyrFlip;
using Xunit;
using Xunit.Abstractions;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The palette table (ticket S0020 section 8, test 2; <c>APP-STYLE</c> section 7 rungs 2 and 4): every
    /// role in every palette, and every role a promise of contrast. The measured ratios are written to
    /// the test output - the number <c>APP-STYLE</c> section 8 says no product has.
    /// </summary>
    public class ThemePaletteTests
    {
        private readonly ITestOutputHelper _output;

        public ThemePaletteTests(ITestOutputHelper output) { _output = output; }

        private static IEnumerable<ThemeRole> Roles => Enum.GetValues(typeof(ThemeRole)).Cast<ThemeRole>();

        /// <summary>
        /// Every text role against every surface it is actually drawn on. A pair missing here is a pair
        /// nobody measured, so the list follows the code: hints on every surface, the accent bar and the
        /// page icons on the page list, the history strip's marks on its rows, ink on the danger button.
        /// </summary>
        private static readonly (ThemeRole Text, ThemeRole Surface)[] Pairs =
        {
            (ThemeRole.TextPrimary, ThemeRole.SurfaceWindow), (ThemeRole.TextPrimary, ThemeRole.SurfaceRaised),
            (ThemeRole.TextPrimary, ThemeRole.SurfaceSunken), (ThemeRole.TextPrimary, ThemeRole.SurfaceAlternate),
            (ThemeRole.TextPrimary, ThemeRole.SurfaceSelected), (ThemeRole.TextPrimary, ThemeRole.Control),
            (ThemeRole.TextPrimary, ThemeRole.ControlHover), (ThemeRole.TextPrimary, ThemeRole.ControlPressed),
            (ThemeRole.TextMuted, ThemeRole.SurfaceWindow), (ThemeRole.TextMuted, ThemeRole.SurfaceRaised),
            (ThemeRole.TextMuted, ThemeRole.SurfaceSunken), (ThemeRole.TextMuted, ThemeRole.SurfaceAlternate),
            (ThemeRole.Accent, ThemeRole.SurfaceWindow), (ThemeRole.Accent, ThemeRole.SurfaceRaised),
            (ThemeRole.Accent, ThemeRole.SurfaceSunken),
            (ThemeRole.Link, ThemeRole.SurfaceWindow), (ThemeRole.Link, ThemeRole.SurfaceRaised),
            (ThemeRole.Warning, ThemeRole.SurfaceWindow), (ThemeRole.Warning, ThemeRole.SurfaceRaised),
            (ThemeRole.Danger, ThemeRole.SurfaceWindow), (ThemeRole.Danger, ThemeRole.SurfaceRaised),
            (ThemeRole.Danger, ThemeRole.SurfaceAlternate),
            (ThemeRole.Info, ThemeRole.SurfaceRaised), (ThemeRole.Info, ThemeRole.SurfaceAlternate),
            (ThemeRole.Success, ThemeRole.SurfaceRaised), (ThemeRole.Success, ThemeRole.SurfaceAlternate),
            (ThemeRole.AccentInk, ThemeRole.Accent), (ThemeRole.AccentInk, ThemeRole.Danger),
        };

        /// <summary>Disabled text is exempt from WCAG's 4.5:1, not from being readable.</summary>
        private static readonly (ThemeRole Text, ThemeRole Surface)[] DisabledPairs =
        {
            (ThemeRole.TextDisabled, ThemeRole.SurfaceWindow), (ThemeRole.TextDisabled, ThemeRole.SurfaceRaised),
            (ThemeRole.TextDisabled, ThemeRole.Control), (ThemeRole.TextDisabled, ThemeRole.SurfaceAlternate),
        };

        public static IEnumerable<object[]> Palettes()
        {
            yield return new object[] { "Light" };
            yield return new object[] { "Dark" };
            yield return new object[] { "HighContrast" };
        }

        private static ThemePalette Get(string name)
            => name == "Dark" ? ThemePalette.Dark : name == "HighContrast" ? ThemePalette.HighContrast : ThemePalette.Light;

        [Theory]
        [MemberData(nameof(Palettes))]
        public void EveryRoleIsDefined(string name)
        {
            ThemePalette palette = Get(name);
            foreach (ThemeRole role in Roles)
                Assert.False(palette[role].IsEmpty, name + " has no " + role);
        }

        [Fact]
        public void TheThreePalettesSayWhatTheyAre()
        {
            Assert.Equal(ThemeKind.Light, ThemePalette.Light.Kind);
            Assert.Equal(ThemeKind.Dark, ThemePalette.Dark.Kind);
            Assert.Equal(ThemeKind.HighContrast, ThemePalette.HighContrast.Kind);
            Assert.Same(ThemePalette.Dark, ThemePalette.For(ThemeKind.Dark));
            Assert.Same(ThemePalette.HighContrast, ThemePalette.For(ThemeKind.HighContrast));
            Assert.Same(ThemePalette.Light, ThemePalette.For(ThemeKind.Light));
        }

        /// <summary>
        /// The light palette is measured against Windows' default colour scheme - the neutral roles are
        /// live system colours, so on a machine running a contrast theme this would be measuring that
        /// theme, which is the system's promise rather than ours.
        /// </summary>
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void EveryTextRoleHoldsItsContrast(string name)
        {
            if (name == "Light" && System.Windows.Forms.SystemInformation.HighContrast) return;
            ThemePalette palette = Get(name);
            var failures = new List<string>();
            foreach ((ThemeRole text, ThemeRole surface) in Pairs)
                Measure(palette, text, surface, 4.5, failures);
            foreach ((ThemeRole text, ThemeRole surface) in DisabledPairs)
                Measure(palette, text, surface, 3.0, failures);
            Assert.True(failures.Count == 0, name + " palette below target:\n" + string.Join("\n", failures));
        }

        private void Measure(ThemePalette palette, ThemeRole text, ThemeRole surface, double target, List<string> failures)
        {
            double ratio = ThemePalette.Contrast(palette[text], palette[surface]);
            _output.WriteLine($"{palette.Kind,-6} {text,-14} on {surface,-16} {ratio,5:0.00}:1 (target {target}:1)");
            if (ratio < target) failures.Add($"{text} on {surface}: {ratio:0.00}:1, target {target}:1");
        }

        [Fact]
        public void DarkIsDarkAndLightIsLight()
        {
            if (System.Windows.Forms.SystemInformation.HighContrast) return;
            foreach (ThemeRole surface in new[] { ThemeRole.SurfaceWindow, ThemeRole.SurfaceRaised, ThemeRole.SurfaceSunken, ThemeRole.SurfaceAlternate, ThemeRole.Control })
            {
                Assert.True(ThemePalette.Luminance(ThemePalette.Dark[surface]) < 0.05, "dark " + surface + " is not dark");
                Assert.True(ThemePalette.Luminance(ThemePalette.Light[surface]) > 0.7, "light " + surface + " is not light");
            }
            Assert.True(ThemePalette.Luminance(ThemePalette.Dark.TextPrimary) > 0.7);
            Assert.True(ThemePalette.Luminance(ThemePalette.Light.TextPrimary) < 0.05);
        }

        /// <summary>A warning or a danger that looks like body text is not a warning (S0020 v0.1 section 4.3).</summary>
        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void TheMarksStandApartFromBodyText(string name)
        {
            ThemePalette palette = Get(name);
            foreach (ThemeRole mark in new[] { ThemeRole.Accent, ThemeRole.Warning, ThemeRole.Danger, ThemeRole.Link, ThemeRole.Success, ThemeRole.Info })
                Assert.True(Distance(palette[mark], palette.TextPrimary) > 100, name + " " + mark + " is too close to text.primary");
            Assert.True(Distance(palette.Warning, palette.Danger) > 40, name + " warning and danger are the same colour");
        }

        /// <summary>
        /// The design-time palette is what controls are built with, and <see cref="ThemeApply"/> reads a
        /// built colour back to its role. Every colour code builds with has to be recognised - one that
        /// is not would stay light in the dark theme.
        /// </summary>
        [Fact]
        public void EveryDesignColourReadsBackToItsRole()
        {
            Assert.Equal(ThemeRole.SurfaceWindow, ThemePalette.RoleOfDesignColor(SystemColors.Control));
            Assert.Equal(ThemeRole.SurfaceRaised, ThemePalette.RoleOfDesignColor(SystemColors.Window));
            Assert.Equal(ThemeRole.TextPrimary, ThemePalette.RoleOfDesignColor(SystemColors.ControlText));
            Assert.Equal(ThemeRole.TextPrimary, ThemePalette.RoleOfDesignColor(SystemColors.WindowText));
            Assert.Equal(ThemeRole.TextMuted, ThemePalette.RoleOfDesignColor(ThemePalette.Light.TextMuted));
            Assert.Equal(ThemeRole.Warning, ThemePalette.RoleOfDesignColor(ThemePalette.Light.Warning));
            Assert.Equal(ThemeRole.Danger, ThemePalette.RoleOfDesignColor(ThemePalette.Light.Danger));
            Assert.Equal(ThemeRole.Accent, ThemePalette.RoleOfDesignColor(ThemePalette.Light.Accent));
            Assert.Equal(ThemeRole.AccentInk, ThemePalette.RoleOfDesignColor(ThemePalette.Light.AccentInk));
            // A literal equal to a named one by value is the same colour.
            Assert.Equal(ThemeRole.Warning, ThemePalette.RoleOfDesignColor(Color.FromArgb(150, 60, 0)));
            // A system colour is never matched by value: Window and White are both white, and mean
            // different things.
            Assert.Null(ThemePalette.RoleOfDesignColor(SystemColors.Info));
            Assert.Null(ThemePalette.RoleOfDesignColor(Color.Empty));
            Assert.Null(ThemePalette.RoleOfDesignColor(Color.FromArgb(1, 2, 3)));
        }

        [Fact]
        public void TheContrastFormulaIsWcags()
        {
            Assert.Equal(21.0, ThemePalette.Contrast(Color.Black, Color.White), 2);
            Assert.Equal(1.0, ThemePalette.Contrast(Color.Red, Color.Red), 2);
            // The audit's own number for grey text on white (S0019 section 1.4).
            Assert.Equal(5.17, ThemePalette.Contrast(Color.FromArgb(109, 109, 109), Color.White), 2);
        }

        private static double Distance(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
