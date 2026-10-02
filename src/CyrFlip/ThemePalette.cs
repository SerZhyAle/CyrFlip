using System;
using System.Collections.Generic;
using System.Drawing;

namespace CyrFlip
{
    /// <summary>
    /// The colour roles of <c>APP-STYLE</c> section 4, plus the three it proposes (<c>warning</c>,
    /// <c>danger</c>, <c>success</c>) and CyrFlip's own proposal <c>surface.alternate</c>. Each member's
    /// doc names the contract spelling.
    /// </summary>
    internal enum ThemeRole
    {
        /// <summary><c>surface.window</c> - the window's own background.</summary>
        SurfaceWindow,
        /// <summary><c>surface.raised</c> - a panel, a list or an input sitting on the window.</summary>
        SurfaceRaised,
        /// <summary><c>surface.sunken</c> - a grouped or inset region (the history strip's header).</summary>
        SurfaceSunken,
        /// <summary><c>surface.selected</c> - the raised surface, selected.</summary>
        SurfaceSelected,
        /// <summary><c>surface.alternate</c> (APP-STYLE 0.11) - the alternate row of a list.</summary>
        SurfaceAlternate,
        /// <summary><c>control</c> - an interactive control's own face.</summary>
        Control,
        /// <summary><c>control.hover</c> - the same face under the pointer.</summary>
        ControlHover,
        /// <summary><c>control.pressed</c> - the same face while pressed.</summary>
        ControlPressed,
        /// <summary><c>border</c> - every separating line.</summary>
        Border,
        /// <summary><c>text.primary</c> - body text.</summary>
        TextPrimary,
        /// <summary><c>text.muted</c> - secondary text that must still be read (every hint line).</summary>
        TextMuted,
        /// <summary><c>text.disabled</c> - text of an unavailable control.</summary>
        TextDisabled,
        /// <summary><c>accent</c> - the primary accent (the selected page's bar, the tab icons).</summary>
        Accent,
        /// <summary><c>accent.ink</c> - text drawn on an accent or danger fill.</summary>
        AccentInk,
        /// <summary><c>link</c> - a hyperlink.</summary>
        Link,
        /// <summary><c>info</c> - a neutral informational mark (the history strip's timestamp).</summary>
        Info,
        /// <summary><c>warning</c> (APP-STYLE 0.11) - "this may not do what you expect".</summary>
        Warning,
        /// <summary><c>danger</c> (APP-STYLE 0.11) - an irreversible action, a refusal.</summary>
        Danger,
        /// <summary><c>success</c> (APP-STYLE 0.11) - a positive mark (the history strip's source app).</summary>
        Success,
    }

    /// <summary>What is actually drawn: the mode the user picked, resolved against Windows.</summary>
    internal enum ThemeKind
    {
        Light,
        Dark,
        /// <summary>Windows' high contrast is on - it wins over every mode (S0020 v0.2 item 4).</summary>
        HighContrast,
    }

    /// <summary>
    /// The one palette table (<c>APP-STYLE</c> rule 3): a value per role, three instances. No colour
    /// literal of the app's chrome lives outside this file (<c>ThemeSourceGateTests</c>); the layout
    /// marker's palette is <see cref="LayoutStyle"/> and deliberately not a theme (<c>LAYOUT-PALETTE</c>).
    ///
    /// <para><b>Controls are built in <see cref="Light"/></b> - the design-time default, which is also
    /// exactly what WinForms draws with no theme at all (system colours plus the two literal colours the
    /// settings window always had). <see cref="ThemeApply"/> reads a built colour back to its role
    /// through <see cref="RoleOfDesignColor"/> and paints the role's value in the active palette.</para>
    ///
    /// <para>Every role is a <b>promise of contrast</b> rather than a taste: each text role holds 4.5:1
    /// against every surface it is drawn on (3:1 for disabled text), measured by
    /// <c>ThemePaletteTests</c>. That is why the dark <see cref="Warning"/> cannot stay the light brown -
    /// on a dark surface it would read as "unavailable".</para>
    /// </summary>
    internal sealed class ThemePalette
    {
        private readonly Color[] _colors;

        private ThemePalette(ThemeKind kind, IDictionary<ThemeRole, Color> colors)
        {
            Kind = kind;
            int count = Enum.GetValues(typeof(ThemeRole)).Length;
            _colors = new Color[count];
            foreach (ThemeRole role in Enum.GetValues(typeof(ThemeRole)))
            {
                // A role left out is a build-time mistake, not a runtime default: an Empty colour would
                // draw as transparent black. ThemePaletteTests builds all three.
                if (!colors.TryGetValue(role, out Color color))
                    throw new ArgumentException("The " + kind + " palette has no " + role + ".");
                _colors[(int)role] = color;
            }
        }

        public ThemeKind Kind { get; }

        public bool IsDark => Kind == ThemeKind.Dark;

        public Color this[ThemeRole role] => _colors[(int)role];

        public Color SurfaceWindow => this[ThemeRole.SurfaceWindow];
        public Color SurfaceRaised => this[ThemeRole.SurfaceRaised];
        public Color SurfaceSunken => this[ThemeRole.SurfaceSunken];
        public Color SurfaceSelected => this[ThemeRole.SurfaceSelected];
        public Color SurfaceAlternate => this[ThemeRole.SurfaceAlternate];
        public Color Control => this[ThemeRole.Control];
        public Color ControlHover => this[ThemeRole.ControlHover];
        public Color ControlPressed => this[ThemeRole.ControlPressed];
        public Color Border => this[ThemeRole.Border];
        public Color TextPrimary => this[ThemeRole.TextPrimary];
        public Color TextMuted => this[ThemeRole.TextMuted];
        public Color TextDisabled => this[ThemeRole.TextDisabled];
        public Color Accent => this[ThemeRole.Accent];
        public Color AccentInk => this[ThemeRole.AccentInk];
        public Color Link => this[ThemeRole.Link];
        public Color Info => this[ThemeRole.Info];
        public Color Warning => this[ThemeRole.Warning];
        public Color Danger => this[ThemeRole.Danger];
        public Color Success => this[ThemeRole.Success];

        /// <summary>
        /// The region capture's overlay (S0026 4.4) - drawn over a frozen picture of the user's own
        /// screen, whose colours are whatever they are, so it is the same in every theme: a translucent
        /// black veil, a white selection frame with a dark hairline beside it, and a dark size label.
        /// </summary>
        public static readonly Color CaptureVeil = Color.FromArgb(110, 0, 0, 0);
        public static readonly Color CaptureFrame = Color.FromArgb(255, 255, 255);
        public static readonly Color CaptureFrameShadow = Color.FromArgb(160, 0, 0, 0);
        public static readonly Color CaptureLabelBack = Color.FromArgb(200, 32, 32, 32);
        public static readonly Color CaptureLabelText = Color.FromArgb(255, 255, 255);

        /// <summary>
        /// The design-time palette: what every control is built with and what the app looked like before
        /// there was a theme. The neutral roles are live system colours, so a customized colour scheme is
        /// honoured exactly as it was; the accent and warning are the two literals the settings window
        /// always drew, now named.
        /// </summary>
        public static readonly ThemePalette Light = new ThemePalette(ThemeKind.Light, new Dictionary<ThemeRole, Color>
        {
            [ThemeRole.SurfaceWindow] = SystemColors.Control,
            [ThemeRole.SurfaceRaised] = SystemColors.Window,
            [ThemeRole.SurfaceSunken] = SystemColors.Control,
            [ThemeRole.SurfaceSelected] = Color.FromArgb(204, 228, 247),
            [ThemeRole.SurfaceAlternate] = Color.FromArgb(242, 242, 242),
            [ThemeRole.Control] = SystemColors.Control,
            [ThemeRole.ControlHover] = Color.FromArgb(229, 241, 251),
            [ThemeRole.ControlPressed] = Color.FromArgb(204, 228, 247),
            [ThemeRole.Border] = SystemColors.ControlDark,
            [ThemeRole.TextPrimary] = SystemColors.ControlText,
            [ThemeRole.TextMuted] = SystemColors.GrayText,
            [ThemeRole.TextDisabled] = SystemColors.GrayText,
            [ThemeRole.Accent] = Color.FromArgb(45, 105, 175),
            [ThemeRole.AccentInk] = Color.White,
            [ThemeRole.Link] = Color.FromArgb(0, 102, 204),
            [ThemeRole.Info] = Color.FromArgb(130, 90, 0),
            [ThemeRole.Warning] = Color.FromArgb(150, 60, 0),  // Dark warning ink meets 4.5:1 on light surfaces.
            [ThemeRole.Danger] = Color.Firebrick,
            [ThemeRole.Success] = Color.FromArgb(30, 120, 70),
        });

        /// <summary>
        /// The dark palette - Windows 11's own dark greys for the surfaces, and every accent lifted until
        /// it holds 4.5:1 on them.
        /// </summary>
        public static readonly ThemePalette Dark = new ThemePalette(ThemeKind.Dark, new Dictionary<ThemeRole, Color>
        {
            [ThemeRole.SurfaceWindow] = Color.FromArgb(32, 32, 32),
            [ThemeRole.SurfaceRaised] = Color.FromArgb(43, 43, 43),
            [ThemeRole.SurfaceSunken] = Color.FromArgb(28, 28, 28),
            [ThemeRole.SurfaceSelected] = Color.FromArgb(52, 64, 82),
            [ThemeRole.SurfaceAlternate] = Color.FromArgb(50, 50, 50),
            [ThemeRole.Control] = Color.FromArgb(55, 55, 55),
            [ThemeRole.ControlHover] = Color.FromArgb(70, 70, 70),
            [ThemeRole.ControlPressed] = Color.FromArgb(38, 38, 38),
            [ThemeRole.Border] = Color.FromArgb(96, 96, 96),
            [ThemeRole.TextPrimary] = Color.FromArgb(240, 240, 240),
            [ThemeRole.TextMuted] = Color.FromArgb(170, 170, 170),
            [ThemeRole.TextDisabled] = Color.FromArgb(135, 135, 135),
            [ThemeRole.Accent] = Color.FromArgb(96, 165, 250),
            [ThemeRole.AccentInk] = Color.Black,
            [ThemeRole.Link] = Color.FromArgb(110, 180, 255),
            [ThemeRole.Info] = Color.FromArgb(240, 200, 60),
            [ThemeRole.Warning] = Color.FromArgb(255, 170, 90),
            [ThemeRole.Danger] = Color.FromArgb(255, 120, 120),
            [ThemeRole.Success] = Color.FromArgb(102, 205, 170),
        });

        /// <summary>
        /// Windows' high contrast: system colours only, and no colour of our own anywhere - a user who
        /// runs a contrast theme runs it for a reason (<c>APP-STYLE</c> section 8). Used by the
        /// owner-drawn parts; the control tree simply goes back to what it was built with.
        /// </summary>
        public static readonly ThemePalette HighContrast = new ThemePalette(ThemeKind.HighContrast, new Dictionary<ThemeRole, Color>
        {
            [ThemeRole.SurfaceWindow] = SystemColors.Control,
            [ThemeRole.SurfaceRaised] = SystemColors.Window,
            [ThemeRole.SurfaceSunken] = SystemColors.Control,
            [ThemeRole.SurfaceSelected] = SystemColors.Highlight,
            [ThemeRole.SurfaceAlternate] = SystemColors.Window,
            [ThemeRole.Control] = SystemColors.Control,
            [ThemeRole.ControlHover] = SystemColors.Highlight,
            [ThemeRole.ControlPressed] = SystemColors.Highlight,
            [ThemeRole.Border] = SystemColors.WindowFrame,
            [ThemeRole.TextPrimary] = SystemColors.ControlText,
            [ThemeRole.TextMuted] = SystemColors.GrayText,
            [ThemeRole.TextDisabled] = SystemColors.GrayText,
            [ThemeRole.Accent] = SystemColors.Highlight,
            [ThemeRole.AccentInk] = SystemColors.HighlightText,
            [ThemeRole.Link] = SystemColors.HotTrack,
            [ThemeRole.Info] = SystemColors.WindowText,
            [ThemeRole.Warning] = SystemColors.WindowText,
            [ThemeRole.Danger] = SystemColors.WindowText,
            [ThemeRole.Success] = SystemColors.WindowText,
        });

        public static ThemePalette For(ThemeKind kind)
            => kind == ThemeKind.Dark ? Dark : kind == ThemeKind.HighContrast ? HighContrast : Light;

        /// <summary>
        /// The role a colour a control was <b>built</b> with stands for, or null for a colour that is not
        /// part of the design-time palette (which the coverage test reports). An exact match first - the
        /// system colours are "known colours" and compare by name - and then by value, so a literal that
        /// happens to equal one of the two named literals is still recognized.
        /// </summary>
        public static ThemeRole? RoleOfDesignColor(Color color)
        {
            if (color.IsEmpty) return null;
            foreach (KeyValuePair<Color, ThemeRole> pair in DesignColors)
                if (pair.Key.Equals(color)) return pair.Value;
            if (color.IsSystemColor) return null;
            int argb = color.ToArgb();
            foreach (KeyValuePair<Color, ThemeRole> pair in DesignColors)
                if (!pair.Key.IsSystemColor && pair.Key.ToArgb() == argb) return pair.Value;
            return null;
        }

        /// <summary>
        /// The colours code builds controls with, and what each one means. Ordered: the first match wins,
        /// which matters where two roles share a light value (disabled text is grey text too).
        /// </summary>
        private static readonly KeyValuePair<Color, ThemeRole>[] DesignColors =
        {
            new KeyValuePair<Color, ThemeRole>(SystemColors.Control, ThemeRole.SurfaceWindow),
            new KeyValuePair<Color, ThemeRole>(SystemColors.Window, ThemeRole.SurfaceRaised),
            new KeyValuePair<Color, ThemeRole>(SystemColors.ControlText, ThemeRole.TextPrimary),
            new KeyValuePair<Color, ThemeRole>(SystemColors.WindowText, ThemeRole.TextPrimary),
            new KeyValuePair<Color, ThemeRole>(SystemColors.GrayText, ThemeRole.TextMuted),
            new KeyValuePair<Color, ThemeRole>(Light.Accent, ThemeRole.Accent),
            new KeyValuePair<Color, ThemeRole>(Light.AccentInk, ThemeRole.AccentInk),
            new KeyValuePair<Color, ThemeRole>(Light.Warning, ThemeRole.Warning),
            new KeyValuePair<Color, ThemeRole>(Light.Danger, ThemeRole.Danger),
            new KeyValuePair<Color, ThemeRole>(Light.Success, ThemeRole.Success),
            new KeyValuePair<Color, ThemeRole>(Light.Link, ThemeRole.Link),
            new KeyValuePair<Color, ThemeRole>(Light.Info, ThemeRole.Info),
        };

        /// <summary>WCAG 2.1 relative luminance of an sRGB colour.</summary>
        public static double Luminance(Color color)
        {
            double Channel(int value)
            {
                double c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        /// <summary>WCAG 2.1 contrast ratio between two colours, 1..21.</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
