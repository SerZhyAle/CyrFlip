using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Base of the small painted pieces of the welcome tour (plates, key caps, state pills, progress
    /// dots). They draw with <see cref="ThemePalette"/> roles only, hear a theme change through
    /// <see cref="IThemeAware"/> and never hold a colour of their own, so the tour follows light, dark and
    /// high contrast like every other window (ThemeSourceGateTests).
    /// </summary>
    internal abstract class WelcomePainted : Control, IThemeAware
    {
        protected ThemePalette Palette { get; private set; } = ThemePalette.Light;

        protected WelcomePainted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        public virtual void ApplyTheme(ThemePalette palette) { Palette = palette; Invalidate(); }

        protected bool Rtl => RightToLeft == RightToLeft.Yes;

        protected TextFormatFlags TextFlags => TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
            | (Rtl ? TextFormatFlags.RightToLeft : TextFormatFlags.Left);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(e.Graphics);
        }

        protected abstract void Draw(Graphics g);

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }

        internal static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// A round plate with a vocabulary glyph (or, where the vocabulary has no picture for it, a short
    /// typographic mark such as "Aa") - the tour's way of putting a recognisable face on a feature.
    /// <see cref="Solid"/> is the page header (accent plate, ink glyph); the soft plate is a list row.
    /// The icon overload is the welcome header alone: the app's own mark, drawn whole (it carries its
    /// own tile), in place of a vocabulary glyph - an identity, not a feature meaning.
    /// </summary>
    internal sealed class GlyphBadge : WelcomePainted
    {
        private readonly string? _glyph;
        private readonly string? _mark;
        private readonly Icon? _icon;

        public bool Solid { get; }

        public GlyphBadge(string? glyph, string? mark, int size, bool solid)
        {
            _glyph = glyph; _mark = mark; Solid = solid;
            Size = new Size(size, size);
            MinimumSize = MaximumSize = Size;
        }

        /// <summary>Draws the given icon whole; the caller owns it (the welcome header's app mark).</summary>
        public GlyphBadge(Icon icon, int size, bool solid)
        {
            _icon = icon; Solid = solid;
            Size = new Size(size, size);
            MinimumSize = MaximumSize = Size;
        }

        protected override void Draw(Graphics g)
        {
            if (_icon != null)
            {
                g.DrawIcon(_icon, new Rectangle(0, 0, Width, Height));
                return;
            }
            var plate = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = Solid ? Palette.Accent : ThemePalette.Mix(Palette.Accent, Palette.SurfaceWindow, 0.84f);
            Color ink = Solid ? Palette.AccentInk : Palette.Accent;
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, plate);
            float side = Width * 0.58f, margin = (Width - side) / 2f;
            if (_glyph != null)
            {
                GlyphRenderer.Draw(g, _glyph, new RectangleF(margin, margin, side, side), ink, Rtl);
                return;
            }
            if (_mark == null) return;
            using var font = new Font(Font.FontFamily, Math.Max(7f, Width * 0.27f), FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, _mark, font, ClientRectangle, ink,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>A chord such as <c>Ctrl+Shift+F12</c> drawn as the keys it is made of.</summary>
    internal sealed class KeyCaps : WelcomePainted
    {
        private readonly string[] _keys;

        public KeyCaps(string chord)
        {
            _keys = Split(chord);
            AutoSize = true;
            AccessibleName = chord;
            AccessibleRole = AccessibleRole.StaticText;
        }

        internal static string[] Split(string chord)
        {
            var keys = chord.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = keys[i].Trim();
                if (string.Equals(keys[i], "PrintScreen", StringComparison.OrdinalIgnoreCase)) keys[i] = "PrtSc";
            }
            return keys;
        }

        private int PadX => LogicalToDeviceUnits(7);
        private int PadY => LogicalToDeviceUnits(3);
        private int Gap => LogicalToDeviceUnits(4);

        private int KeyWidth(string key)
            => Math.Max(TextRenderer.MeasureText(key, Font, Size.Empty, TextFlags).Width + PadX * 2,
                LogicalToDeviceUnits(26));

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = 0;
            foreach (string key in _keys) width += KeyWidth(key) + Gap;
            int height = TextRenderer.MeasureText("Ag", Font, Size.Empty, TextFlags).Height + PadY * 2 + 2;
            return new Size(Math.Max(0, width - Gap), height);
        }

        protected override void Draw(Graphics g)
        {
            int x = 0, top = 0, bottom = Height - 2;
            foreach (string key in _keys)
            {
                int w = KeyWidth(key);
                var cap = new RectangleF(x + 0.5f, top + 0.5f, w - 1f, bottom - top - 1f);
                // The darker lower edge is what makes a rectangle read as a key.
                using (var edge = RoundRect(new RectangleF(cap.X, cap.Y + 1.5f, cap.Width, cap.Height), 4f))
                using (var edgePen = new Pen(Palette.Border, 1f))
                    g.DrawPath(edgePen, edge);
                using (var face = RoundRect(cap, 4f))
                {
                    using (var brush = new SolidBrush(Palette.Control)) g.FillPath(brush, face);
                    using (var pen = new Pen(Palette.Border, 1f)) g.DrawPath(pen, face);
                }
                TextRenderer.DrawText(g, key, Font, Rectangle.Round(cap), Palette.TextPrimary,
                    TextFlags | TextFormatFlags.HorizontalCenter);
                x += w + Gap;
            }
        }
    }

    /// <summary>The hairline under a page header, in the border role (a plain panel would need a literal colour).</summary>
    internal sealed class WelcomeRule : WelcomePainted
    {
        public WelcomeRule(int width) { Size = new Size(width, 1); MinimumSize = MaximumSize = Size; }

        protected override void Draw(Graphics g)
        {
            using (var brush = new SolidBrush(Palette.Border)) g.FillRectangle(brush, 0, 0, Width, Height);
        }
    }
    /// <summary>
    /// A live on/off switch: a capsule track with a knob. It is a real control - one click or Space
    /// flips it, it takes the keyboard focus and tells a screen reader its state - and it only reports
    /// the flip (<see cref="Toggled"/>); what the flip means is the owner's business, which may refuse
    /// it and put the knob back with <see cref="Checked"/>.
    /// </summary>
    internal sealed class ToggleSwitch : WelcomePainted
    {
        private bool _checked;

        public event EventHandler? Toggled;

        public ToggleSwitch(bool on)
        {
            _checked = on;
            AutoSize = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        /// <summary>The state shown. Setting it never raises <see cref="Toggled"/>: only the user's flip does.</summary>
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value; Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        /// <summary>The user's flip: show the new state, then tell the owner.</summary>
        internal void Toggle()
        {
            Checked = !_checked;
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        private int Inset => LogicalToDeviceUnits(2);

        public override Size GetPreferredSize(Size proposedSize)
            => new Size(LogicalToDeviceUnits(44), LogicalToDeviceUnits(26));

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Focus(); Toggle();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { Toggle(); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessible(this);

        private sealed class SwitchAccessible : ControlAccessibleObject
        {
            private readonly ToggleSwitch _owner;
            public SwitchAccessible(ToggleSwitch owner) : base(owner) { _owner = owner; }
            public override AccessibleStates State
                => base.State | (_owner._checked ? AccessibleStates.Checked : AccessibleStates.None);
            public override string DefaultAction => _owner._checked ? "Uncheck" : "Check";
            public override void DoDefaultAction() => _owner.Toggle();
        }

        protected override void Draw(Graphics g)
        {
            if (Focused && ShowFocusCues)
                using (var ring = RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
                using (var pen = new Pen(Palette.Accent, 1f))
                    g.DrawPath(pen, ring);
            float m = Inset + 0.5f;
            var track = new RectangleF(m, m, Width - 2 * m - 0.5f, Height - 2 * m - 0.5f);
            using (var path = RoundRect(track, track.Height / 2f))
            {
                using (var brush = new SolidBrush(_checked ? Palette.Accent : Palette.SurfaceRaised)) g.FillPath(brush, path);
                using (var pen = new Pen(_checked ? Palette.Accent : Palette.TextMuted, 1.25f)) g.DrawPath(pen, path);
            }
            float pad = LogicalToDeviceUnits(3), d = track.Height - 2 * pad;
            // The knob rests at the end of the reading direction when on, so a right-to-left tour mirrors it.
            bool atRight = _checked != Rtl;
            float x = atRight ? track.Right - pad - d : track.Left + pad;
            using (var brush = new SolidBrush(_checked ? Palette.AccentInk : Palette.TextMuted))
                g.FillEllipse(brush, x, track.Top + pad, d, d);
        }
    }

    /// <summary>
    /// A button that carries a vocabulary glyph beside its caption - the tour's navigation. The glyph,
    /// its hue and its tinted face are <see cref="ButtonGlyphs"/>' own, the same as every other command
    /// button of the product: drawn again whenever the theme, the direction, the DPI or the glyph changes
    /// (nothing is baked), and a glyph that cannot be drawn leaves the caption alone.
    /// </summary>
    internal sealed class GlyphButton : Button
    {
        public GlyphButton(string glyph, bool glyphAfterText, ButtonLook look = ButtonLook.Neutral)
        {
            AutoSize = true;
            ButtonGlyphs.Attach(this, glyph, look, glyphAfterText);
        }

        public string Glyph
        {
            get => ButtonGlyphs.GlyphOf(this) ?? "";
            set => ButtonGlyphs.SetGlyph(this, value);
        }
    }

    /// <summary>The tour's progress: one dot per step, the current one stretched into a capsule.</summary>
    internal sealed class StepDots : WelcomePainted
    {
        private readonly int _count;
        private int _current;

        public StepDots(int count) { _count = count; AutoSize = true; }

        public int Current
        {
            get => _current;
            set { if (_current != value) { _current = value; Invalidate(); } }
        }

        private int Dot => LogicalToDeviceUnits(8);
        private int Gap => LogicalToDeviceUnits(6);
        private int Long => LogicalToDeviceUnits(22);

        public override Size GetPreferredSize(Size proposedSize)
            => new Size((_count - 1) * (Dot + Gap) + Long, Dot);

        protected override void Draw(Graphics g)
        {
            // Progress reads along the reading direction, so a right-to-left tour fills from the right.
            if (Rtl) { g.TranslateTransform(Width, 0); g.ScaleTransform(-1, 1); }
            float x = 0;
            for (int i = 0; i < _count; i++)
            {
                bool current = i == _current;
                float w = current ? Long : Dot;
                using (var brush = new SolidBrush(i <= _current ? Palette.Accent : Palette.Border))
                using (var path = RoundRect(new RectangleF(x, 0, w, Dot), Dot / 2f))
                    g.FillPath(brush, path);
                x += w + Gap;
            }
        }
    }

    /// <summary>
    /// "ghbdtn  ⇄  привет": what a conversion does, shown instead of described. The sample is the same
    /// word on the two layouts of one keyboard, which is the whole product in one picture.
    /// </summary>
    internal sealed class ConversionExample : WelcomePainted
    {
        private readonly string _from, _to;
        private readonly Font _mono;

        public ConversionExample(string from, string to)
        {
            _from = from; _to = to;
            _mono = new Font("Consolas", 11f, FontStyle.Regular);
            AutoSize = true;
            AccessibleName = from + " -> " + to;
            AccessibleRole = AccessibleRole.StaticText;
        }

        private int PadX => LogicalToDeviceUnits(12);
        private int PadY => LogicalToDeviceUnits(6);
        private int Arrow => LogicalToDeviceUnits(22);
        private int Gap => LogicalToDeviceUnits(10);
        private int ChipWidth(string text) => TextRenderer.MeasureText(text, _mono, Size.Empty, MonoFlags).Width + PadX * 2;

        private static TextFormatFlags MonoFlags => TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        public override Size GetPreferredSize(Size proposedSize)
            => new Size(ChipWidth(_from) + Gap * 2 + Arrow + ChipWidth(_to),
                TextRenderer.MeasureText("Ag", _mono, Size.Empty, MonoFlags).Height + PadY * 2);

        protected override void Draw(Graphics g)
        {
            // The sample is Latin against Cyrillic whatever the interface language: always left to right.
            int fromW = ChipWidth(_from), toW = ChipWidth(_to);
            var left = new Rectangle(0, 0, fromW, Height - 1);
            var right = new Rectangle(fromW + Gap * 2 + Arrow, 0, toW - 1, Height - 1);
            using (var path = RoundRect(left, 6f))
            {
                using (var brush = new SolidBrush(Palette.SurfaceRaised)) g.FillPath(brush, path);
                using (var pen = new Pen(Palette.Border, 1f)) g.DrawPath(pen, path);
            }
            using (var path = RoundRect(right, 6f))
            {
                using (var brush = new SolidBrush(ThemePalette.Mix(Palette.Accent, Palette.SurfaceWindow, 0.84f))) g.FillPath(brush, path);
                using (var pen = new Pen(Palette.Accent, 1f)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, _from, _mono, left, Palette.TextPrimary, MonoFlags);
            TextRenderer.DrawText(g, _to, _mono, right, Palette.TextPrimary, MonoFlags);
            float top = (Height - Arrow) / 2f;
            GlyphRenderer.Draw(g, AppGlyphs.ConvertLayout, new RectangleF(fromW + Gap, top, Arrow, Arrow), Palette.Accent);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _mono.Dispose();
            base.Dispose(disposing);
        }
    }
}
