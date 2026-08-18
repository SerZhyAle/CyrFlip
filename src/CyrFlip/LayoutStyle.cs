using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CyrFlip
{
    /// <summary>
    /// Shared look for the layout marker across all three surfaces (mouse I-beam cursor,
    /// caret overlay, tray icon): a per-layout bright colour drawn with a black outline so
    /// the letters stay legible on any background.
    ///
    /// The palette has two levels and one exit. <see cref="Curated"/> gives each of the thirteen curated
    /// languages its own colour (what the two letters name); <see cref="Layouts"/> gives each of their 25
    /// keyboard layouts a shade inside that language's hue (what the two letters cannot name, since US
    /// and Dvorak both read "EN"); everything else is <see cref="Other"/>.
    /// </summary>
    internal static class LayoutStyle
    {
        /// <summary>
        /// The curated palette, and <b>the source of truth for it</b>. The app ships as one exe with
        /// nothing to read at runtime, so the table has to live in code; the machine-readable copy for
        /// everything outside the exe (the VS Code extension) is
        /// <c>vscode-extension/src/layout-colors.json</c>, and <c>LayoutColorsTests</c> fails the build
        /// when the two disagree. <c>tools/IconGen</c> needs no copy at all - it compiles this very file.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Color> Curated = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "EN", ColorTranslator.FromHtml("#4DA3FF") }, // blue
            { "RU", ColorTranslator.FromHtml("#FF5A5A") }, // red
            { "UK", ColorTranslator.FromHtml("#5AD86A") }, // green
            { "ZH", ColorTranslator.FromHtml("#F3B33D") },
            { "HI", ColorTranslator.FromHtml("#E68742") },
            { "ES", ColorTranslator.FromHtml("#F06E9C") },
            { "FR", ColorTranslator.FromHtml("#8D7CF2") },
            { "AR", ColorTranslator.FromHtml("#35C6B4") },
            { "BN", ColorTranslator.FromHtml("#C67CDA") },
            { "PT", ColorTranslator.FromHtml("#46B978") },
            { "UR", ColorTranslator.FromHtml("#E5A3C7") },
            { "DE", ColorTranslator.FromHtml("#D8D14A") },
            { "IT", ColorTranslator.FromHtml("#7FB2E5") },
        };

        /// <summary>
        /// One colour per <b>keyboard layout</b>, keyed by KLID - the second half of the palette and,
        /// like <see cref="Curated"/>, source of truth for the copy the VS Code extension ships.
        ///
        /// <para>The marker draws two letters for a <i>language</i> (US and Dvorak both read "EN"), so a
        /// user typing on two layouts of one language sees the same two letters twice; the colour is
        /// what tells them apart, and in the caret overlay's dot mode it is the only thing there is.
        /// Every entry keeps its language's hue - a Russian layout is always red, an English one always
        /// blue - and varies lightness/saturation inside it, so the colour still answers "which
        /// language" first and "which of its layouts" second.</para>
        ///
        /// <para>The set is exactly the layouts of the thirteen curated languages
        /// (<c>WorldLayouts.Popular</c>, 25 of them). Anything else - a Polish keyboard, a Japanese IME -
        /// is <see cref="Other"/>: one colour, deliberately, because there is no honest way to hand out
        /// distinct recognisable colours for the ~218 layouts Windows ships.</para>
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Color> Layouts = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "00000409", ColorTranslator.FromHtml("#4DA3FF") }, // EN  US
            { "00020409", ColorTranslator.FromHtml("#0A58FF") }, // EN  US-International
            { "00010409", ColorTranslator.FromHtml("#C2E3F4") }, // EN  US-Dvorak
            { "00000804", ColorTranslator.FromHtml("#F3B33D") }, // ZH  Simplified (zh-CN)
            { "00000404", ColorTranslator.FromHtml("#FAEAA7") }, // ZH  Traditional (zh-TW)
            { "00000439", ColorTranslator.FromHtml("#E68742") }, // HI  Devanagari-INSCRIPT
            { "00010439", ColorTranslator.FromHtml("#F0B88F") }, // HI  Hindi Traditional
            { "0000040A", ColorTranslator.FromHtml("#F06E9C") }, // ES  Spanish
            { "0000080A", ColorTranslator.FromHtml("#E92D8F") }, // ES  Latin American
            { "0000040C", ColorTranslator.FromHtml("#8D7CF2") }, // FR  French (AZERTY)
            { "00001009", ColorTranslator.FromHtml("#633BEC") }, // FR  Canadian French
            { "00000401", ColorTranslator.FromHtml("#35C6B4") }, // AR  Arabic (101)
            { "00020401", ColorTranslator.FromHtml("#8BE0C7") }, // AR  Arabic (102) AZERTY
            { "00000445", ColorTranslator.FromHtml("#C67CDA") }, // BN  Bangla - INSCRIPT
            { "00000416", ColorTranslator.FromHtml("#46B978") }, // PT  Brazilian ABNT
            { "00000816", ColorTranslator.FromHtml("#96C0A1") }, // PT  Portuguese
            { "00000419", ColorTranslator.FromHtml("#FF5A5A") }, // RU  Russian
            { "00010419", ColorTranslator.FromHtml("#FF1313") }, // RU  Russian (Typewriter)
            { "00000420", ColorTranslator.FromHtml("#E5A3C7") }, // UR  Urdu
            { "00000407", ColorTranslator.FromHtml("#D8D14A") }, // DE  German (QWERTZ)
            { "00010407", ColorTranslator.FromHtml("#CED4A5") }, // DE  German (IBM)
            { "00000410", ColorTranslator.FromHtml("#7FB2E5") }, // IT  Italian
            { "00010410", ColorTranslator.FromHtml("#6587B7") }, // IT  Italian (142)
            { "00000422", ColorTranslator.FromHtml("#5AD86A") }, // UK  Ukrainian
            { "00020422", ColorTranslator.FromHtml("#3FD039") }, // UK  Ukrainian (Enhanced)
        };

        /// <summary>
        /// The colour of everything outside the thirteen curated languages. It replaced a per-code
        /// hash that handed every uncurated layout its own bright hue: that colour looked as
        /// deliberate as the curated ones while meaning nothing, and it could land anywhere - next to
        /// Russian's red, say. One neutral colour says what is true: "a layout CyrFlip has no colour
        /// for". The two letters still name the language (PL, JA, TR), so nothing is lost that the
        /// marker was actually communicating.
        /// </summary>
        public static readonly Color Other = ColorTranslator.FromHtml("#9AA6B2");

        /// <summary>
        /// How opaque the marker is drawn beside the caret, on the mouse I-beam and by the VS Code
        /// extension. It sits on top of the user's text all day, so it is deliberately not solid; the
        /// tray icon keeps its full opacity, where translucency would only make a 16px icon muddy.
        ///
        /// <para>It started at 0.85 and that was not translucency anyone could see - on the dark editor
        /// background the badge read as solid. 0.6 shows through; the black outline around the glyphs is
        /// what keeps them legible at that level. The extension reads the same number from
        /// <c>layout-colors.json</c>, so the marker looks the same in and out of the editor.</para>
        /// </summary>
        public const float MarkerOpacity = 0.6f;

        /// <summary>The gap, in pixels, left between the marker's letters and the edge of its badge.</summary>
        internal const float TextMargin = 1f;

        /// <summary>The black outline's width, as a fraction of the badge height.</summary>
        internal const float OutlineFraction = 0.12f;

        /// <summary>How far antialiasing spreads an edge beyond the geometry it was drawn from.</summary>
        internal const float AntiAliasBleed = 0.5f;

        /// <summary>Curve-flattening tolerance used to measure the glyphs, in pre-scale units.</summary>
        internal const float FlattenTolerance = 0.05f;

        /// <summary>The colour for a language code alone (no layout known) - the tray tooltip, the
        /// settings tables, and any caller that has never seen a KLID.</summary>
        public static Color ColorFor(string code)
            => Curated.TryGetValue(code ?? string.Empty, out Color known) ? known : Other;

        /// <summary>
        /// The colour of one concrete layout: its own entry when it is one of the 25 curated layouts,
        /// otherwise its language's colour (so a British or Swiss keyboard still reads as its
        /// language), otherwise <see cref="Other"/>.
        /// </summary>
        public static Color ColorForLayout(string? klid, string code)
            => klid != null && Layouts.TryGetValue(klid, out Color exact) ? exact : ColorFor(code);

        /// <summary>Draw <paramref name="code"/> in <paramref name="area"/> with the layout's fill and a
        /// black outline, scaled to fill the badge with <see cref="TextMargin"/> px left around it.</summary>
        /// <remarks>
        /// The letters are <b>fitted to the badge</b>, not typeset into it: the glyph outline is built at
        /// the font's nominal size, measured, then scaled so its ink - not its line box - touches one
        /// pixel from every edge. <c>MeasureString</c>/<c>AddString(.., area, ..)</c> reserve room for
        /// ascenders, descenders and leading that two capital letters never use, which is why the marker
        /// used to sit in the middle of an obviously roomier badge. <paramref name="font"/> now supplies
        /// only the family, style and aspect ratio; the badge supplies the size.
        /// </remarks>
        public static void DrawCode(Graphics g, string code, Font font, RectangleF area, string? klid = null)
        {
            if (string.IsNullOrEmpty(code))
                return;

            using var path = new GraphicsPath();
            path.AddString(code, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
            // Flatten first: on a curved path GetBounds answers with the box around the Béziers'
            // *control* points, which is wider than the ink and by an amount that differs per glyph.
            // Fitting to that measurement leaves lopsided margins - visibly so at 14px, where a
            // pixel is a tenth of the badge.
            path.Flatten(null, FlattenTolerance);
            RectangleF ink = path.GetBounds();
            if (ink.Width <= 0f || ink.Height <= 0f)
                return;

            float outline = Math.Max(1.5f, area.Height * OutlineFraction);
            // Two things sit outside the glyph's own bounds: half the outline (a stroke is centred on
            // the edge it follows) and the antialiasing tail, which spills about half a pixel further
            // still - without that half pixel the bottom row of the badge picks up grey and the "one
            // pixel of margin" this method promises is not there.
            float inset = TextMargin + outline / 2f + AntiAliasBleed;
            RectangleF target = RectangleF.Inflate(area, -inset, -inset);
            if (target.Width <= 0f || target.Height <= 0f)
                return;

            float scale = Math.Min(target.Width / ink.Width, target.Height / ink.Height);
            using (var m = new Matrix())
            {
                m.Translate(target.X + (target.Width - ink.Width * scale) / 2f,
                            target.Y + (target.Height - ink.Height * scale) / 2f);
                m.Scale(scale, scale);
                m.Translate(-ink.X, -ink.Y);
                path.Transform(m);
            }

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.Black, outline) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);
            using (var fill = new SolidBrush(ColorForLayout(klid, code)))
                g.FillPath(fill, path);
            g.SmoothingMode = prev;
        }

        /// <summary>
        /// Draw a 1px rounded frame in the layout colour just inside <paramref name="area"/> -
        /// the CapsLock-is-on indicator drawn around the marker on all three surfaces.
        /// </summary>
        public static void DrawCapsFrame(Graphics g, RectangleF area, float radius, string code, string? klid = null)
        {
            // Inset by half a pixel so the 1px stroke lands fully inside the badge.
            var r = RectangleF.Inflate(area, -0.5f, -0.5f);
            if (r.Width <= 0 || r.Height <= 0)
                return;

            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f)))
            using (var pen = new Pen(ColorForLayout(klid, code), 1f))
                g.DrawPath(pen, path);
            g.SmoothingMode = prev;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0f)
            {
                path.AddRectangle(r);
                path.CloseFigure();
                return path;
            }
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

    }
}
