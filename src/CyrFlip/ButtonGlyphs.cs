using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>What a command button says about itself (WINDOWS-UI section 11 item 4).</summary>
    internal enum ButtonLook
    {
        /// <summary>An ordinary command: the mono look, the glyph in the text colour (item 5).</summary>
        Neutral,
        /// <summary>The affirmative answer of a dialog: confirmation glyph, neutral face (BUTTONS 2.0).</summary>
        Affirmative,
        /// <summary>The dismissing answer - Cancel, No, Close: neutral glyph and face.</summary>
        Dismissing,
        /// <summary>The safe answer of an irreversible confirmation: neutral glyph and face.</summary>
        DismissingSafe,
        /// <summary>The acting button of an irreversible confirmation: the <c>danger</c> face and its own ink.</summary>
        Acting,
    }

    /// <summary>The two shared hues of <see cref="ThemePalette.AnswerInk"/>.</summary>
    internal enum AnswerHue { Ok, Error }

    /// <summary>
    /// A command button's glyph (WINDOWS-UI section 11, ICON-RENDER section 14): the vocabulary's own
    /// picture for the caption's verb, 16 logical px, before the caption (after it for a "next"), with a
    /// 8 logical px gap. One place does it for every button of the product, so a dialog answer, a
    /// settings command and the tour's navigation cannot drift apart.
    ///
    /// <para><b>Nothing is stored.</b> The glyph is filled from the shared path
    /// (<see cref="GlyphRenderer"/>) in the button's own <c>Paint</c>, in the colour of the theme that is
    /// live at that moment - so the hue follows a theme change with no value resolved at load, is the
    /// text role under high contrast and <c>text.disabled</c> on an unavailable button - and there is no
    /// bitmap per button. The room for it is the button's <see cref="Control.Padding"/> on the glyph's
    /// side: the button still sizes itself by its caption (AutoSize, wrapping rows, 13 languages),
    /// which is why the glyph is not an <see cref="ButtonBase.Image"/>: WinForms measures an image button
    /// in two different ways, and a button in a table cell came out narrower than its own caption.</para>
    ///
    /// <para>BUTTONS 2.0 keeps ordinary answer faces neutral. Only a destructive confirmation's
    /// acting button uses danger; affirmative confirmation glyphs keep their state.ok ink.</para>
    /// </summary>
    internal static class ButtonGlyphs
    {
        /// <summary>The glyph's tier beside a caption (ICON-RENDER section 3 rule 5), in logical px.</summary>
        public const int GlyphLogical = 16;

        /// <summary>The gap between glyph and caption (BUTTONS section 3), in logical px.</summary>
        public const int GapLogical = 8;

        /// <summary>
        /// The pointer command baseline of BUTTONS section 3, in logical 96-DPI px:
        /// 32 high, 12 padding per side, 8 between commands.
        /// One place says it for the settings window's rows and header, so a command can no longer be
        /// 23 px high in one row and 28 in the next (ticket S0060 M12).
        /// </summary>
        public const int CommandHeightLogical = 32;
        public const int CommandPaddingLogical = 12;
        public const int CommandGapLogical = 8;

        private sealed class Spec
        {
            public string Glyph = "";
            public ButtonLook Look;
            public bool GlyphAfterText;
            /// <summary>The padding the button was built with (96-DPI logical px).</summary>
            public Padding BasePadding;
            /// <summary>Takes the shared command metric (<see cref="UseCommandMetric"/>): minimum height and side gaps.</summary>
            public bool CommandMetric;
        }

        private static readonly ConditionalWeakTable<Button, Spec> Specs = new ConditionalWeakTable<Button, Spec>();

        /// <summary>Give <paramref name="button"/> the vocabulary glyph <paramref name="glyphId"/>.</summary>
        public static void Attach(Button button, string glyphId, ButtonLook look = ButtonLook.Neutral, bool glyphAfterText = false)
        {
            if (Specs.TryGetValue(button, out Spec? existing))
            {
                existing.Glyph = glyphId; existing.Look = look; existing.GlyphAfterText = glyphAfterText;
                Restyle(button, existing);
                return;
            }
            var spec = new Spec { Glyph = glyphId, Look = look, GlyphAfterText = glyphAfterText, BasePadding = button.Padding };
            Specs.Add(button, spec);
            button.Paint += OnPaint;
            button.EnabledChanged += (_, _) => Restyle(button, spec);
            button.RightToLeftChanged += (_, _) => Restyle(button, spec);
            button.HandleCreated += (_, _) => Restyle(button, spec);
            // Everything that can change what the padding should be; the reservation is absolute, so
            // running it again is free (a WinForms autoscale pass that scaled it meanwhile is corrected too).
            button.TextChanged += (_, _) => ReservePadding(button, spec);
            button.FontChanged += (_, _) => ReservePadding(button, spec);
            button.VisibleChanged += (_, _) => ReservePadding(button, spec);
            button.DpiChangedAfterParent += (_, _) => Restyle(button, spec);
            Restyle(button, spec);
        }

        /// <summary>
        /// A dialog's answer button, polarity read from its <see cref="Button.DialogResult"/> (item 4): OK
        /// and Yes are affirmative - the check, or <paramref name="verbGlyph"/> when the answer is a verb of
        /// its own ("Add", "Export", "Start"); Cancel and No are dismissing - the cross. A button with no
        /// answer role is an ordinary command and takes <paramref name="verbGlyph"/> in the mono look.
        /// </summary>
        public static Button Answer(Button button, string? verbGlyph = null)
        {
            switch (button.DialogResult)
            {
                case DialogResult.OK:
                case DialogResult.Yes:
                    Attach(button, verbGlyph ?? AppGlyphs.Confirm, ButtonLook.Affirmative);
                    break;
                case DialogResult.Cancel:
                case DialogResult.No:
                    Attach(button, AppGlyphs.Cancel, ButtonLook.Dismissing);
                    break;
                default:
                    if (verbGlyph != null) Attach(button, verbGlyph);
                    break;
            }
            return button;
        }

        /// <summary>Change the picture of an attached button (the tour's last "Next" becomes "Open settings").</summary>
        public static void SetGlyph(Button button, string glyphId)
        {
            if (!Specs.TryGetValue(button, out Spec? spec) || spec.Glyph == glyphId) return;
            spec.Glyph = glyphId;
            button.Invalidate();
        }

        public static void SetLook(Button button, ButtonLook look)
        {
            if (!Specs.TryGetValue(button, out Spec? spec) || spec.Look == look) return;
            spec.Look = look;
            Restyle(button, spec);
        }

        /// <summary>
        /// Give an attached command button the shared metric of a settings row (see
        /// <see cref="CommandHeightLogical"/>): 12 logical px of padding on each side before the glyph's
        /// room, a 32 logical px minimum height and a 4 + 4 px gap to its neighbours (8 between two
        /// buttons). All of it is scaled by the button's own DPI and re-asserted whenever the glyph's
        /// reservation is - a text, font, visibility or DPI change - so it cannot drift from the glyph.
        /// A button with no glyph attached is sized by its caller.
        /// </summary>
        public static void UseCommandMetric(Button button)
        {
            if (!Specs.TryGetValue(button, out Spec? spec)) return;
            spec.CommandMetric = true;
            spec.BasePadding = new Padding(CommandPaddingLogical, spec.BasePadding.Top, CommandPaddingLogical, spec.BasePadding.Bottom);
            ReservePadding(button, spec);
        }

        /// <summary>Re-derive the padding, height and gaps of an attached button after its DPI changed.</summary>
        public static void Reassert(Button button)
        {
            if (Specs.TryGetValue(button, out Spec? spec)) ReservePadding(button, spec);
        }

        public static bool IsAttached(Button button) => Specs.TryGetValue(button, out _);

        public static string? GlyphOf(Button button) => Specs.TryGetValue(button, out Spec? spec) ? spec.Glyph : null;

        public static ButtonLook LookOf(Button button) => Specs.TryGetValue(button, out Spec? spec) ? spec.Look : ButtonLook.Neutral;

        /// <summary>The glyph's colour: the hue of its answer, the danger face's ink, or the text role.</summary>
        public static Color InkFor(ButtonLook look, ThemePalette palette, bool enabled)
        {
            if (!enabled) return palette.TextDisabled;               // muted together with the caption, no hue
            if (palette.Kind == ThemeKind.HighContrast) return palette.TextPrimary;
            switch (look)
            {
                case ButtonLook.Affirmative: return ThemePalette.AnswerInk.For(AnswerHue.Ok, palette);
                case ButtonLook.Acting: return palette.AccentInk;
                default: return palette.TextPrimary;
            }
        }

        /// <summary>Answer faces no longer have a semantic tint (BUTTONS 2.0 section 3.5).</summary>
        public static AnswerHue? TintOf(ButtonLook look) => null;

        /// <summary>
        /// The face of a dialog's affirmative or dismissing answer, entirely from <paramref name="palette"/>
        /// (BUTTONS section 3.5): a neutral flat face; the native face with system colours
        /// under high contrast. Returns false for a button that is not such an answer, which takes the
        /// ordinary path of <see cref="ThemeApply"/>.
        ///
        /// <para>It runs at <b>build</b> time with the light palette too, so the built state - what
        /// <see cref="ThemeApply"/> records and puts back - already is the light look, exactly as for every
        /// other control (S0020: light is what the code builds).</para>
        /// </summary>
        internal static bool PaintFace(Button button, ThemePalette palette)
        {
            if (!Specs.TryGetValue(button, out Spec? spec)) return false;
            // BUTTONS section 3.5 supersedes the former green/red answer faces.
            // Acting is already assigned the danger role by its confirmation dialog.
            if (spec.Look == ButtonLook.Acting || spec.Look == ButtonLook.Neutral) return false;

            if (palette.Kind == ThemeKind.HighContrast)
            {
                button.FlatStyle = FlatStyle.Standard;
                button.ResetBackColor();
                button.ResetForeColor();
                button.FlatAppearance.BorderColor = Color.Empty;
                button.FlatAppearance.MouseOverBackColor = Color.Empty;
                button.FlatAppearance.MouseDownBackColor = Color.Empty;
                button.UseVisualStyleBackColor = true;
                return true;
            }

            button.FlatStyle = FlatStyle.Flat;
            SetColor(button, true, palette.Control);
            SetColor(button, false, palette.TextPrimary);
            button.FlatAppearance.BorderColor = palette.Border;
            button.FlatAppearance.MouseOverBackColor = HoverBase(palette);
            button.FlatAppearance.MouseDownBackColor = palette.ControlPressed;
            button.UseVisualStyleBackColor = false;
            return true;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private static int Device(int logical, double scale) => (int)Math.Round(logical * scale);

        /// <summary>
        /// The DPI the button is drawn at: what Windows says for its window. <see cref="Control.DeviceDpi"/>
        /// is not that in this app - it does not opt into WinForms' DPI machinery, so the property stays
        /// 96 on a 150 percent screen until a window answers <c>WM_DPICHANGED</c> (see <see cref="ControlDpi"/>)
        /// - while the system fonts the captions are drawn in are already scaled. Before the handle exists
        /// the tree's own value counts if it is truthful, else the primary monitor's.
        /// </summary>
        internal static int Dpi(Control control)
        {
            if (control.IsHandleCreated)
            {
                try { uint dpi = GetDpiForWindow(control.Handle); if (dpi >= 96) return (int)dpi; }
                catch (EntryPointNotFoundException) { /* before Windows 10 1607 - fall through */ }
            }
            if (control.DeviceDpi > 96) return control.DeviceDpi;
            return Math.Max(96, MarkerSize.PrimaryDpi());
        }

        /// <summary>
        /// The face an answer has under the pointer. On the light theme it is the palette's hover face; on
        /// the dark one it is <b>darker</b> than the resting face, not lighter: the catalog's night tone of
        /// <c>state.error</c> holds 3 : 1 only on faces up to about #363636, so the dark theme's usual
        /// lighter hover (#464646, 2.7 : 1 for the red cross) would take the glyph below the contract's
        /// floor for exactly the moment the user is looking at it (measured, ButtonGlyphTests).
        /// </summary>
        internal static Color HoverBase(ThemePalette palette)
            => palette.IsDark ? ThemePalette.Mix(palette.Control, palette.ControlPressed, 0.5f) : palette.ControlHover;

        private static void SetColor(Button button, bool back, Color color)
        {
            if (back) { if (button.BackColor != color) button.BackColor = color; }
            else if (button.ForeColor != color) button.ForeColor = color;
        }

        /// <summary>
        /// The theme painted <paramref name="button"/> with <paramref name="palette"/>: the glyph is
        /// drawn again by the button's next paint, in that palette's ink.
        /// </summary>
        internal static void Refresh(Button button, ThemePalette palette)
        {
            if (Specs.TryGetValue(button, out _)) button.Invalidate();
        }

        /// <summary>True when the glyph sits at the right end of the button's content.</summary>
        internal static bool GlyphOnRight(Button button)
            => Specs.TryGetValue(button, out Spec? spec)
               && ((button.RightToLeft == RightToLeft.Yes) ^ spec.GlyphAfterText);

        private static void Restyle(Button button, Spec spec)
        {
            ThemePalette? own = ThemeApply.PaletteOf(button);
            ReservePadding(button, spec);
            if (own != null && button.IsHandleCreated) { ThemeApply.Apply(button, own); return; }
            PaintFace(button, PaletteFor(button));   // the built face: the light look, recorded as built by the first walk
            button.Invalidate();
        }

        private static ThemePalette PaletteFor(Control control)
        {
            for (Control? c = control; c != null; c = c.Parent)
            {
                ThemePalette? palette = ThemeApply.PaletteOf(c);
                if (palette != null) return palette;
            }
            return ThemePalette.Light;
        }

        /// <summary>
        /// Make room for the glyph on its side: the button's own padding plus the glyph and the gap, at
        /// the button's DPI. Absolute, never incremental, so a DPI or direction change cannot add it twice.
        /// </summary>
        private static void ReservePadding(Button button, Spec spec)
        {
            UiMetrics metrics = UiMetrics.For(button);
            double scale = Dpi(button) / 96.0 * metrics.InterfaceScale;
            Padding logical = BaseOf(button, spec);
            Padding basePadding = new Padding(
                (int)Math.Round(logical.Left * scale), (int)Math.Round(logical.Top * scale),
                (int)Math.Round(logical.Right * scale), (int)Math.Round(logical.Bottom * scale));
            int add = button.Text.Length > 0 ? Device((metrics.Touch ? 20 : GlyphLogical) + GapLogical, scale) : 0;
            bool right = GlyphOnRight(button);
            Padding wanted = right
                ? new Padding(basePadding.Left, basePadding.Top, basePadding.Right + add, basePadding.Bottom)
                : new Padding(basePadding.Left + add, basePadding.Top, basePadding.Right, basePadding.Bottom);
            if (button.Padding != wanted) button.Padding = wanted;
            if (spec.CommandMetric)
            {
                // Height is a floor, not a fixed size: a caption that wraps or a taller script still grows the button.
                int height = Device(metrics.Touch ? 48 : CommandHeightLogical, scale);
                // A picture-only command is a square of the command height; one with a caption is as wide as it needs.
                int width = button.Text.Length == 0 ? height : 0;
                if (button.MinimumSize.Height != height || button.MinimumSize.Width != width) button.MinimumSize = new Size(width, height);
                int half = Device(CommandGapLogical, scale) / 2;
                if (button.Margin.Left != half || button.Margin.Right != half)
                    button.Margin = new Padding(half, button.Margin.Top, half, button.Margin.Bottom);
            }
        }

        /// <summary>
        /// The logical padding the button's content is centred inside: what it was built with - except that a
        /// command with no caption (a picture-only arrow) has no text to pad, so its glyph is centred in the
        /// whole face rather than in a face narrowed by 16 px a side.
        /// </summary>
        private static Padding BaseOf(Button button, Spec spec)
            => spec.CommandMetric && button.Text.Length == 0
                ? new Padding(0, spec.BasePadding.Top, 0, spec.BasePadding.Bottom)
                : spec.BasePadding;

        private static void OnPaint(object? sender, PaintEventArgs e)
        {
            if (sender is Button button) Draw(button, e.Graphics, ThemeApply.PaletteOf(button) ?? PaletteFor(button));
        }

        /// <summary>
        /// Fill the glyph into the button (its caption already drawn, or about to be): centred as one
        /// group with the caption inside the padding the button was built with, on the reading-start side.
        /// Also called by <see cref="ThemeApply"/> after it repaints a disabled button in dark, which wipes
        /// the face the Paint event drew on.
        /// </summary>
        internal static void Draw(Button button, Graphics g, ThemePalette palette)
        {
            if (!Specs.TryGetValue(button, out Spec? spec)) return;
            Rectangle box = GlyphBox(button, g);
            GlyphRenderer.Draw(g, spec.Glyph, box, InkFor(spec.Look, palette, button.Enabled), button.RightToLeft == RightToLeft.Yes);
        }

        /// <summary>
        /// Where the glyph's box sits in the button, in its client pixels: 16 logical px square, centred as
        /// one group with the caption inside the padding the button was built with, on the reading-start
        /// side (the end side for a "next"), the 6 logical px gap between it and the caption. The one
        /// geometry <see cref="Draw"/> and the measuring harness read.
        /// </summary>
        internal static Rectangle GlyphBox(Button button, Graphics g)
        {
            if (!Specs.TryGetValue(button, out Spec? spec)) return Rectangle.Empty;
            UiMetrics metrics = UiMetrics.For(button);
            double scale = Dpi(button) / 96.0 * metrics.InterfaceScale;
            int glyph = Device(metrics.Touch ? 20 : GlyphLogical, scale);
            int gap = button.Text.Length > 0 ? Device(GapLogical, scale) : 0;
            Padding logical = BaseOf(button, spec);
            int baseLeft = (int)Math.Round(logical.Left * scale), baseRight = (int)Math.Round(logical.Right * scale);
            Rectangle client = button.ClientRectangle;
            int contentLeft = client.X + baseLeft, contentWidth = Math.Max(0, client.Width - baseLeft - baseRight);

            int captionWidth = 0;
            if (button.Text.Length > 0)
                captionWidth = Math.Min(Math.Max(1, contentWidth - glyph - gap),
                    TextRenderer.MeasureText(g, button.Text, button.Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
            int group = glyph + gap + captionWidth;
            int start = contentLeft + Math.Max(0, (contentWidth - group) / 2);
            // The glyph is the first of the group in reading order unless it is declared to follow the caption.
            int x = GlyphOnRight(button) ? start + captionWidth + gap : start;
            return new Rectangle(x, client.Y + (client.Height - glyph) / 2, glyph, glyph);
        }
    }
}
