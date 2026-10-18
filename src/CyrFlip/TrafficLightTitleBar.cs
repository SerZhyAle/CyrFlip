using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>What the board's title bar can be asked for: its four icon buttons.</summary>
    internal enum TitleCommand
    {
        Help,
        Add,
        Edit,
        Pin,
        Close,
    }

    /// <summary>
    /// The board's own title area (ticket S0069 section 6): the board is a borderless window, so what a
    /// native caption would have been is drawn here - the title, then four icon buttons from the vocabulary:
    /// add a target, edit the targets (the settings, on this page), pin on top (a toggle: the filled pin is
    /// on, the outlined one is off) and close. A press on anything else in the bar drags the window.
    /// <para>
    /// Like the board it never takes the keyboard focus (<c>Selectable</c> is off) and never activates the
    /// window: a click on a button is a command, not a change of the foreground. Every command also lives in
    /// the board's menu, which the keyboard reaches with the Menu key, so no click here is mouse-only.
    /// </para>
    /// </summary>
    internal sealed class TrafficLightTitleBar : Control, IThemeAware
    {
        private static readonly TitleCommand[] Order = { TitleCommand.Add, TitleCommand.Edit, TitleCommand.Pin, TitleCommand.Close, TitleCommand.Help };

        private ThemePalette _palette = ThemePalette.Light;
        private Func<string, string> _t = s => s;
        private string _title = "";
        private bool _showTitle = true;
        private bool _pinned = true;
        private int _hover = -1;
        private int _pressed = -1;
        private int _tipIndex = -1;
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true, InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 8000 };

        public TrafficLightTitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            DoubleBuffered = true;
            AccessibleRole = AccessibleRole.TitleBar;
            ApplyTheme(ThemePalette.Light);
        }

        /// <summary>A button was pressed and released on its own zone.</summary>
        public event Action<TitleCommand>? CommandInvoked;

        /// <summary>A left press on the bar that is not on a button: the window may be dragged by it.</summary>
        public event Action? DragRequested;

        public void SetTranslator(Func<string, string> translate)
        {
            _t = translate;
            AccessibleName = _t("Availability traffic light");
            Invalidate();
        }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; Invalidate(); } }
        }

        /// <summary>False for the bare-square board: there is no room for words, only for the buttons.</summary>
        public bool ShowTitle
        {
            get => _showTitle;
            set { if (_showTitle != value) { _showTitle = value; Invalidate(); } }
        }

        public bool Pinned
        {
            get => _pinned;
            set { if (_pinned != value) { _pinned = value; Invalidate(); } }
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceSunken;
            ForeColor = palette.TextPrimary;
            Invalidate();
        }

        // ---- metrics: measured from the font, never pixel constants ----

        /// <summary>The bar's height and the side of every button zone.</summary>
        public int Side => Math.Max(Font.Height + 8, 44);

        private int GlyphSize => Math.Max(14, Font.Height + 2);

        /// <summary>The narrowest the bar may be: the four buttons and a sliver of title to hold on to.</summary>
        public int MinimumContentWidth => Order.Length * Side + Side / 2;

        public override Size GetPreferredSize(Size proposedSize) => new Size(MinimumContentWidth, Side);

        private bool Rtl => RightToLeft == RightToLeft.Yes;

        public int ButtonCount => Order.Length;

        /// <summary>The zone of button <paramref name="index"/> (in <see cref="Order"/>), mirrored for a right-to-left board.</summary>
        internal Rectangle ZoneOf(int index)
        {
            int side = Side;
            int fromEnd = index == 4 ? 5 : 4 - index;
            int x = Rtl ? (fromEnd - 1) * side : Width - fromEnd * side;
            return new Rectangle(x, 0, side, Math.Max(Height, side));
        }

        /// <summary>The button under a client point, or -1.</summary>
        internal int ZoneAt(Point client)
        {
            for (int i = 0; i < Order.Length; i++)
                if (ZoneOf(i).Contains(client)) return i;
            return -1;
        }

        internal TitleCommand CommandAt(int index) => Order[index];

        /// <summary>The words a button says on hover and to a screen reader.</summary>
        internal string CaptionOf(TitleCommand command)
        {
            switch (command)
            {
                case TitleCommand.Help: return _t("Help (F1)");
                case TitleCommand.Add: return _t("Add a target..");
                case TitleCommand.Edit: return _t("Edit targets..");
                case TitleCommand.Pin: return _t("Always on top");
                default: return _t("Hide");
            }
        }

        /// <summary>The vocabulary glyph a button draws: the pin follows its state.</summary>
        internal static string GlyphOf(TitleCommand command, bool pinned)
        {
            switch (command)
            {
                case TitleCommand.Help: return AppGlyphs.Help;
                case TitleCommand.Add: return AppGlyphs.Add;
                case TitleCommand.Edit: return AppGlyphs.Edit;
                case TitleCommand.Pin: return HistoryStripGlyphs.PinId(pinned);
                default: return AppGlyphs.Close;
            }
        }

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_palette.SurfaceSunken);
            using (var pen = new Pen(_palette.Border, 1f))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            int side = Side;
            int gap = Math.Max(4, side / 4);
            if (_showTitle && _title.Length > 0)
            {
                int left = Rtl ? ZoneOf(4).Right + gap : gap;
                int right = Rtl ? Width - gap : ZoneOf(4).Left - gap;
                if (right - left > Font.Height * 3)
                    TextRenderer.DrawText(g, _title, Font, new Rectangle(left, 0, right - left, Height), _palette.TextPrimary,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                        | (Rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
            }

            for (int i = 0; i < Order.Length; i++)
            {
                Rectangle zone = ZoneOf(i);
                bool hot = i == _hover;
                bool down = i == _pressed && hot;
                if (hot)
                {
                    Rectangle plate = Rectangle.Inflate(zone, -2, -2);
                    using (var brush = new SolidBrush(ThemePalette.Mix(_palette.SurfaceSunken, _palette.TextPrimary, down ? 0.22f : 0.12f)))
                        g.FillRectangle(brush, plate);
                }

                TitleCommand command = Order[i];
                string glyph = GlyphOf(command, _pinned);
                Color ink = command == TitleCommand.Pin ? HistoryStripGlyphs.PinColor(_pinned, _palette) : _palette.TextPrimary;
                Rectangle at = HistoryStripGlyphs.Centered(zone, GlyphSize);
                if (!GlyphRenderer.Draw(g, glyph, at, ink, Rtl))
                {
                    // A glyph that cannot be drawn leaves a typographic stand-in, so the button is never blank.
                    TextRenderer.DrawText(g, FallbackOf(command), Font, zone, ink,

                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        private static string FallbackOf(TitleCommand command)
        {
            switch (command)
            {
                case TitleCommand.Add: return "+";
                case TitleCommand.Edit: return "..";
                case TitleCommand.Pin: return "^";
                default: return "x";
            }
        }

        // ---- pointer ----

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = ZoneAt(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Invalidate();
            if (hit < 0) HideTip();
            else ShowTip(hit);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            HideTip();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = ZoneAt(e.Location);
            HideTip();
            if (hit >= 0)
            {
                _pressed = hit;
                Capture = true;
                Invalidate();
            }
            else DragRequested?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _pressed < 0) return;
            int pressed = _pressed;
            _pressed = -1;
            Capture = false;
            Invalidate();
            if (ZoneAt(e.Location) == pressed) CommandInvoked?.Invoke(Order[pressed]);
        }

        // The control's own tool, its text following the button under the pointer. Not ToolTip.Show(.., this, ..):
        // that one shows only while the window is the active one, and the board is never activated by the mouse.
        private void ShowTip(int index)
        {
            if (index == _tipIndex) return;
            _tipIndex = index;
            SetTipText(CaptionOf(Order[index]));
        }

        private void HideTip()
        {
            if (_tipIndex < 0) return;
            _tipIndex = -1;
            SetTipText("");
        }

        private void SetTipText(string text)
        {
            try
            {
                _tip.Active = false;        // pops a tip still showing the previous button
                _tip.SetToolTip(this, text);
                _tip.Active = true;
            }
            catch (Exception ex) { TrafficLightLog.Fault("titlebar-tip", ex); }
        }

        /// <summary>What the hover tip currently holds - the caption of the button under the pointer, empty off the buttons.</summary>
        internal string HoverTipText => _tip.GetToolTip(this);

        // ---- accessibility: the title bar and one push button per command ----

        protected override AccessibleObject CreateAccessibilityInstance() => new BarAccessibleObject(this);

        private sealed class BarAccessibleObject : ControlAccessibleObject
        {
            private readonly TrafficLightTitleBar _owner;
            public BarAccessibleObject(TrafficLightTitleBar owner) : base(owner) { _owner = owner; }
            public override AccessibleRole Role => AccessibleRole.TitleBar;
            public override int GetChildCount() => Order.Length;
            public override AccessibleObject? GetChild(int index)
                => index >= 0 && index < Order.Length ? new ButtonAccessibleObject(_owner, index) : null;
        }

        private sealed class ButtonAccessibleObject : AccessibleObject
        {
            private readonly TrafficLightTitleBar _owner;
            private readonly int _index;
            public ButtonAccessibleObject(TrafficLightTitleBar owner, int index) { _owner = owner; _index = index; }

            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string Name => _owner.CaptionOf(Order[_index]);
            public override string DefaultAction => Name;
            public override Rectangle Bounds => _owner.RectangleToScreen(_owner.ZoneOf(_index));
            public override AccessibleObject Parent => _owner.AccessibilityObject;
            public override AccessibleStates State
                => Order[_index] == TitleCommand.Pin && _owner._pinned
                    ? AccessibleStates.Pressed | AccessibleStates.Focusable
                    : AccessibleStates.Focusable;
            public override void DoDefaultAction() => _owner.CommandInvoked?.Invoke(Order[_index]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
