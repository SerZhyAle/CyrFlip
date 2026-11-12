using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using CyrFlip;
using Xunit;
using Xunit.Abstractions;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The translucent carriers' promise (ticket S0093): an ink and its ground that the screen shows through
    /// are measured as they reach the eye - composed over the worst backdrop grey from black to white - never
    /// as the two palette values alone. The composition is <see cref="ThemePalette.Over"/>, the one point of
    /// alpha arithmetic; every floor is derived by <see cref="ThemePalette.LowestHolding"/> and printed beside
    /// the constant it holds up.
    /// </summary>
    public class PanelColorContractTests
    {
        private static readonly Color White = Color.FromArgb(255, 255, 255);
        private readonly ITestOutputHelper _output;

        public PanelColorContractTests(ITestOutputHelper output) { _output = output; }

        // ---- the composition itself ----

        [Fact]
        public void OverIsSourceOverRoundedToTheNearestByte()
        {
            Color backdrop = Color.FromArgb(10, 200, 30);
            // No coverage leaves the backdrop, full coverage the layer; the result is always opaque.
            Assert.Equal(backdrop.ToArgb(), ThemePalette.Over(Color.FromArgb(0, 255, 0, 0), backdrop).ToArgb());
            Assert.Equal(Color.FromArgb(255, 1, 2, 3).ToArgb(), ThemePalette.Over(Color.FromArgb(255, 1, 2, 3), backdrop).ToArgb());
            Assert.Equal(255, ThemePalette.Over(Color.FromArgb(17, 0, 0, 0), Color.FromArgb(0, 9, 9, 9)).A);
            // Exact quotients: 240 x 170 / 255 = 160; 255 x 145 / 255 = 145.
            Assert.Equal(Color.FromArgb(160, 160, 160).ToArgb(), ThemePalette.Over(Color.FromArgb(170, 240, 240, 240), Color.Black).ToArgb());
            Assert.Equal(Color.FromArgb(145, 145, 145).ToArgb(), ThemePalette.Over(ThemePalette.CaptureVeil, White).ToArgb());
            // Nearest, not truncated: 254 x 85 / 255 = 84.67 -> 85; (32 x 200 + 255 x 55) / 255 = 80.10 -> 80.
            Assert.Equal(85, ThemePalette.Over(Color.FromArgb(170, 0, 0, 0), Color.FromArgb(254, 254, 254)).R);
            Assert.Equal(80, ThemePalette.Over(ThemePalette.CaptureLabelBack, White).R);
            // Per channel: half-covered pure red over pure blue.
            Assert.Equal(Color.FromArgb(128, 0, 127).ToArgb(), ThemePalette.Over(Color.FromArgb(128, 255, 0, 0), Color.FromArgb(0, 0, 255)).ToArgb());
        }

        [Fact]
        public void WindowAlphaTruncatesAsWinFormsDoes()
        {
            Assert.Equal(170, ThemePalette.WindowAlpha(0.67));   // 170.85
            Assert.Equal(178, ThemePalette.WindowAlpha(0.70));   // 178.5
            Assert.Equal(76, ThemePalette.WindowAlpha(0.30));    // 76.5
            Assert.Equal(255, ThemePalette.WindowAlpha(1.0));
            Assert.Equal(0, ThemePalette.WindowAlpha(0.0));
            Assert.Equal(255, ThemePalette.WindowAlpha(1.5));    // Form clamps; so does the model
            Assert.Equal(0, ThemePalette.WindowAlpha(-0.2));
        }

        [Fact]
        public void TheWorstGreyIsTheLowestRatioOverEveryBackdrop()
        {
            // White on the veil: the veil over white is #919191, the lightest ground it ever makes.
            double worst = ThemePalette.WorstOverGrey(_ => White, b => ThemePalette.Over(ThemePalette.CaptureVeil, b), out int grey);
            Assert.Equal(255, grey);
            Assert.Equal(ThemePalette.Contrast(White, Color.FromArgb(145, 145, 145)), worst, 6);
            // A fully opaque window is the palette pair itself, whatever is behind it.
            double opaque = ThemePalette.WorstThroughWindow(Color.Black, White, 1.0, out _);
            Assert.Equal(21.0, opaque, 2);
            // A fully transparent one is the backdrop against itself.
            Assert.Equal(1.0, ThemePalette.WorstThroughWindow(Color.Black, White, 0.0, out _), 6);
            // Dark body text on the raised surface through the toolbar's 0.67 window: section 4 read 4.48 with an
            // untruncated alpha; the 170 WinForms really hands the window makes it 4.41, at a white backdrop.
            double toolbar = ThemePalette.WorstThroughWindow(ThemePalette.Dark.TextPrimary, ThemePalette.Dark.SurfaceRaised, 0.67, out int toolbarGrey);
            _output.WriteLine($"dark text.primary on surface.raised through 0.67: {toolbar:0.00}:1 at #{toolbarGrey:X2}");
            Assert.InRange(toolbar, 4.40, 4.42);
            Assert.Equal(255, toolbarGrey);
        }

        [Fact]
        public void TheFloorIsTheLowestValueFromWhichEveryHigherOneHolds()
        {
            // A rounding dip above the first passing value moves the floor above the dip.
            double[] ratios = { 1, 2, 5, 4, 5, 6 };
            Assert.Equal(4, ThemePalette.LowestHolding(0, 5, v => ratios[v], 4.5));
            Assert.Equal(1, ThemePalette.LowestHolding(0, 5, v => ratios[v], 2.0));
            Assert.Null(ThemePalette.LowestHolding(0, 5, v => ratios[v], 7.0));
            Assert.Equal(0, ThemePalette.LowestHolding(0, 5, v => ratios[v], 1.0));
        }

        // ---- the region capture's overlay: pinned constants with a proven worst case (mechanism c) ----

        /// <summary>
        /// The size label is white text on a translucent dark plate drawn straight over the frozen screen. The
        /// veil may or may not lie between them (the label can sit inside the selection), so the plate is
        /// measured over the raw screen - the lighter, worse ground. Text owes 4.5:1.
        /// </summary>
        [Fact]
        public void TheCaptureSizeLabelHoldsTextContrastOverEveryScreen()
        {
            Color plate = ThemePalette.CaptureLabelBack;
            double LabelAt(int alpha) => ThemePalette.WorstOverGrey(_ => ThemePalette.CaptureLabelText,
                b => ThemePalette.Over(Color.FromArgb(alpha, plate), b), out _);

            double worst = ThemePalette.WorstOverGrey(_ => ThemePalette.CaptureLabelText, b => ThemePalette.Over(plate, b), out int grey);
            int? floor = ThemePalette.LowestHolding(0, 255, LabelAt, 4.5);
            _output.WriteLine($"capture label: {worst:0.00}:1 at #{grey:X2} with alpha {plate.A}; the lowest alpha that holds 4.5:1 is {floor}");
            Assert.True(worst >= 4.5, $"capture label {worst:0.00}:1 at #{grey:X2}, target 4.5:1");
            Assert.NotNull(floor);
            Assert.True(plate.A >= floor, $"label plate alpha {plate.A} is below the derived floor {floor}");
            Assert.Equal(255, ThemePalette.CaptureLabelText.A);
        }

        /// <summary>
        /// The selection frame is a 1 px white line between the selection and a dark hairline that sits on the
        /// veil. Inside, it meets the user's own pixels and can vanish against white content - that is why the
        /// hairline exists, and why the edge the promise rests on is the outer one: the frame against the veiled
        /// screen (3:1, a non-text edge), and the frame against its hairline over that veil. The annotation
        /// editor draws the same frame on the same veil without the hairline, so the first measure is its edge.
        /// </summary>
        [Fact]
        public void TheCaptureFrameHoldsEdgeContrastAgainstTheVeil()
        {
            Color veil = ThemePalette.CaptureVeil, frame = ThemePalette.CaptureFrame, shadow = ThemePalette.CaptureFrameShadow;
            double FrameOnVeil(int alpha) => ThemePalette.WorstOverGrey(_ => frame,
                b => ThemePalette.Over(Color.FromArgb(alpha, veil), b), out _);

            double onVeil = ThemePalette.WorstOverGrey(_ => frame, b => ThemePalette.Over(veil, b), out int veilGrey);
            double onShadow = ThemePalette.WorstOverGrey(_ => frame, b => ThemePalette.Over(shadow, ThemePalette.Over(veil, b)), out int shadowGrey);
            int? floor = ThemePalette.LowestHolding(0, 255, FrameOnVeil, 3.0);
            _output.WriteLine($"capture frame on veil: {onVeil:0.00}:1 at #{veilGrey:X2} with alpha {veil.A}; the lowest veil alpha that holds 3:1 is {floor}");
            _output.WriteLine($"capture frame on its hairline over the veil: {onShadow:0.00}:1 at #{shadowGrey:X2}");
            Assert.Equal(255, frame.A);
            Assert.True(onVeil >= 3.0, $"frame on veil {onVeil:0.00}:1 at #{veilGrey:X2}, target 3:1");
            Assert.True(onShadow >= 3.0, $"frame on hairline {onShadow:0.00}:1 at #{shadowGrey:X2}, target 3:1");
            Assert.NotNull(floor);
            Assert.True(veil.A >= floor, $"veil alpha {veil.A} is below the derived floor {floor}");
        }

        // ---- whole-window opacity floors (mechanism b, owner decisions Q1 = b and Q4 = B) ----

        private static readonly ThemePalette[] Themes = { ThemePalette.Light, ThemePalette.Dark };

        /// <summary>The lowest whole-window opacity, in per cent, at which an ink holds a norm on its ground.</summary>
        private static int FloorPercent(Color ink, Color ground, double norm)
        {
            int? floor = ThemePalette.LowestHolding(0, 100, p => ThemePalette.WorstThroughWindow(ink, ground, p / 100.0, out _), norm);
            Assert.NotNull(floor);   // fully opaque, every pair here holds its norm
            return floor!.Value;
        }

        /// <summary>The pairs that set a floor: the body text on every surface and face, the captions on theirs.</summary>
        private static IEnumerable<(string name, Color ink, Color ground)> ToolbarPairs(ThemePalette p)
        {
            yield return ("text.primary on control (tool and command buttons)", p.TextPrimary, p.Control);
            yield return ("text.primary on control.hover", p.TextPrimary, p.ControlHover);
            yield return ("text.primary on surface.selected (the active tool)", p.TextPrimary, p.SurfaceSelected);
            yield return ("accent.ink on accent (the OK caption)", p.AccentInk, p.Accent);
        }

        private static IEnumerable<(string name, Color ink, Color ground)> WindowPairs(ThemePalette p)
        {
            yield return ("text.primary on surface.raised (history rows)", p.TextPrimary, p.SurfaceRaised);
            yield return ("text.primary on surface.alternate (history rows)", p.TextPrimary, p.SurfaceAlternate);
            yield return ("text.primary on surface.window (translation text)", p.TextPrimary, p.SurfaceWindow);
            yield return ("text.primary on control (buttons)", p.TextPrimary, p.Control);
            yield return ("text.primary on control.hover (buttons)", p.TextPrimary, p.ControlHover);
            yield return ("text.primary on the glyph button hover", p.TextPrimary, ButtonGlyphs.HoverBase(p));
            yield return ("text.primary on control.pressed (buttons)", p.TextPrimary, p.ControlPressed);
        }

        [Fact]
        public void TheDrawingToolbarOpacityIsAtLeastItsComputedFloor()
        {
            int constant = (int)Math.Round(ScreenshotToolbar.ToolbarOpacity * 100);
            int floor = 0;
            foreach (ThemePalette palette in Themes)
                foreach (var (name, ink, ground) in ToolbarPairs(palette))
                {
                    int pair = FloorPercent(ink, ground, 4.5);
                    double at = ThemePalette.WorstThroughWindow(ink, ground, ScreenshotToolbar.ToolbarOpacity, out int grey);
                    _output.WriteLine($"toolbar {palette.Kind} {name}: floor {pair}%, at {constant}% {at:0.00}:1 (worst at #{grey:X2})");
                    Assert.True(at >= 4.5, $"toolbar {palette.Kind} {name}: {at:0.00}:1 at #{grey:X2}");
                    floor = Math.Max(floor, pair);
                }
            _output.WriteLine($"toolbar floor {floor}%, constant {constant}%");
            Assert.True(constant >= floor, $"ToolbarOpacity {constant}% is below the computed floor {floor}%");
        }

        [Fact]
        public void TheTranslucentWindowsFloorIsAtLeastItsComputedFloor()
        {
            int floor = 0;
            foreach (ThemePalette palette in Themes)
                foreach (var (name, ink, ground) in WindowPairs(palette))
                {
                    int pair = FloorPercent(ink, ground, 4.5);
                    _output.WriteLine($"windows {palette.Kind} {name}: floor {pair}%");
                    floor = Math.Max(floor, pair);
                }
            _output.WriteLine($"windows floor {floor}%, constant {ThemePalette.TranslucentWindowMinOpacity}%");
            Assert.True(ThemePalette.TranslucentWindowMinOpacity >= floor,
                $"TranslucentWindowMinOpacity {ThemePalette.TranslucentWindowMinOpacity}% is below the computed floor {floor}%");
            Assert.Equal(ThemePalette.TranslucentWindowMinOpacity, ThemePalette.ClampWindowOpacity(30));
            Assert.Equal(100, ThemePalette.ClampWindowOpacity(140));
            Assert.Equal(90, ThemePalette.ClampWindowOpacity(90));
        }

        /// <summary>
        /// The recorded exception of Q4 = B (2026-11-10): the windows' and the toolbar's secondary inks are not
        /// what sets their floors, and at the floor some of them stay below their norm on a bright or dark desktop.
        /// Measured and printed here, pair by pair, so the exception has numbers and cannot grow silently: every
        /// pair below its norm must be one of the named roles.
        /// </summary>
        [Fact]
        public void TheSecondaryInksAtTheFloorAreTheRecordedException()
        {
            var excepted = new HashSet<string> { "text.muted", "accent", "info", "success", "danger", "text.disabled" };
            var below = new HashSet<string>();
            double windows = ThemePalette.TranslucentWindowMinOpacity / 100.0;
            foreach (ThemePalette p in Themes)
            {
                var pairs = new List<(string ink, string ground, Color inkColor, Color groundColor, double norm, double opacity)>
                {
                    ("text.muted", "surface.raised", p.TextMuted, p.SurfaceRaised, 4.5, windows),
                    ("text.muted", "surface.alternate", p.TextMuted, p.SurfaceAlternate, 4.5, windows),
                    ("text.muted", "surface.sunken", p.TextMuted, p.SurfaceSunken, 4.5, windows),
                    ("accent", "surface.sunken", p.Accent, p.SurfaceSunken, 4.5, windows),
                    ("info", "surface.raised", p.Info, p.SurfaceRaised, 4.5, windows),
                    ("success", "surface.raised", p.Success, p.SurfaceRaised, 4.5, windows),
                    ("danger", "surface.raised", p.Danger, p.SurfaceRaised, 4.5, windows),
                    ("text.disabled", "control", p.TextDisabled, p.Control, 3.0, windows),
                    ("text.disabled", "control (toolbar)", p.TextDisabled, p.Control, 3.0, ScreenshotToolbar.ToolbarOpacity),
                };
                foreach (var (ink, ground, inkColor, groundColor, norm, opacity) in pairs)
                {
                    double at = ThemePalette.WorstThroughWindow(inkColor, groundColor, opacity, out int grey);
                    bool holds = at >= norm;
                    _output.WriteLine($"secondary {p.Kind} {ink} on {ground} at {opacity:0.00}: {at:0.00}:1 (worst at #{grey:X2}), norm {norm} - {(holds ? "holds" : "recorded exception")}");
                    Assert.True(holds || excepted.Contains(ink), $"{ink} on {ground} is below its norm and not a recorded exception");
                    if (!holds) below.Add(ink);
                }
            }
            // A role that now holds everywhere leaves the exception: the list only ever shrinks.
            Assert.Equal(excepted.OrderBy(n => n), below.OrderBy(n => n));
        }

        // ---- the stored opacities: raised to the floor on load, nothing else touched ----

        private static MemoryConfigKey Settled(params (string name, object value)[] values)
        {
            // A profile the loader has nothing else to do for: the table exists, the S0071 re-key ran.
            var key = new MemoryConfigKey();
            key.Values["LayoutConversionProfiles"] = "[]";
            key.Values["ScreenshotKeyMigrationDone"] = 1;
            foreach (var (name, value) in values) key.Values[name] = value;
            return key;
        }

        [Fact]
        public void AStoredOpacityBelowTheFloorIsRaisedToItAndWrittenBack()
        {
            var key = Settled(("ClipboardHistoryOpacity", 30), ("TranslateWindowOpacity", 55), ("TranslateModel", "kept-model"),
                ("ClipboardHistoryWidth", 333));
            AppConfig loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.Equal(ThemePalette.TranslucentWindowMinOpacity, loaded.ClipboardHistoryOpacity);
            Assert.Equal(ThemePalette.TranslucentWindowMinOpacity, loaded.TranslateWindowOpacity);
            Assert.Equal("kept-model", loaded.TranslateModel);   // bystanders keep their stored values
            Assert.Equal(333, loaded.ClipboardHistoryWidth);
            Assert.True(mustSave);                               // the raised values are persisted at once
        }

        [Fact]
        public void AStoredOpacityAtOrAboveTheFloorIsLeftAlone()
        {
            var key = Settled(("ClipboardHistoryOpacity", ThemePalette.TranslucentWindowMinOpacity), ("TranslateWindowOpacity", 91));
            AppConfig loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.Equal(ThemePalette.TranslucentWindowMinOpacity, loaded.ClipboardHistoryOpacity);
            Assert.Equal(91, loaded.TranslateWindowOpacity);
            Assert.False(mustSave);
        }

        // ---- the traffic-light board's words keep a solid underlay (mechanism a, owner decision Q5 = A) ----

        [Fact]
        public void ATextUnderlayIsTheTextsOwnExtentAtItsReadingStart()
        {
            using var font = new Font("Segoe UI", 9f);
            var area = new Rectangle(10, 20, 400, 24);
            Rectangle ltr = TrafficLightWindow.TextUnderlay(area, "Office", font, rtl: false);
            Rectangle rtl = TrafficLightWindow.TextUnderlay(area, "Office", font, rtl: true);
            Assert.Equal(area.Left, ltr.Left);
            Assert.Equal(area.Right, rtl.Right);
            Assert.Equal(area.Top, ltr.Top);
            Assert.Equal(area.Height, ltr.Height);
            Assert.InRange(ltr.Width, 2 * TrafficLightWindow.UnderlayMargin + 1, area.Width / 2);   // the words, not the band
            Assert.Equal(ltr.Width, rtl.Width);
            // A text longer than its area is ellipsized there: the underlay never leaves the area.
            Rectangle longer = TrafficLightWindow.TextUnderlay(new Rectangle(0, 0, 40, 20), new string('W', 80), font, rtl: false);
            Assert.Equal(40, longer.Width);
            Assert.True(TrafficLightWindow.TextUnderlay(area, "", font, rtl: false).IsEmpty);
            Assert.True(TrafficLightWindow.TextUnderlay(Rectangle.Empty, "Office", font, rtl: false).IsEmpty);
        }
    }
}
