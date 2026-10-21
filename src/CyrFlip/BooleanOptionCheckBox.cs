using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>BOOLEAN-OPTION sections 2-3. One native accessible checkbox; explanation/help are siblings.</summary>
    internal class BooleanOptionCheckBox : CheckBox
    {
        private Point _press;
        private bool _pointerDown;
        private bool _cancelled;

        internal BooleanOptionCheckBox()
        {
            AutoSize = true;
            UseMnemonic = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal int CaptionInset
        {
            get { UiMetrics metrics = UiMetrics.For(this); return metrics.Device(this, metrics.Checkbox + metrics.Gap); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            UiMetrics metrics = UiMetrics.For(this);
            int target = metrics.Device(this, metrics.Target);
            int available = MaximumSize.Width > 0 ? MaximumSize.Width : proposedSize.Width;
            int textWidth = available > 0 ? Math.Max(1, available - CaptionInset - Padding.Horizontal) : int.MaxValue;
            Size text = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            return new Size(Math.Max(target, text.Width + CaptionInset + Padding.Horizontal),
                Math.Max(target, text.Height + Padding.Vertical + metrics.Device(this, metrics.Logical(4))));
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // The full background is filled atomically in OnPaint on the double-buffer surface.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = ThemeApply.PaletteOf(this) ?? ThemePalette.Light;
            Color own = BackColor;
            Color back = own.A == 255 ? own : palette.SurfaceWindow;
            using (var fill = new SolidBrush(back)) e.Graphics.FillRectangle(fill, ClientRectangle);
            UiMetrics metrics = UiMetrics.For(this);
            bool rtl = RightToLeft == RightToLeft.Yes;
            int square = metrics.Device(this, metrics.Checkbox);
            int x = rtl ? ClientSize.Width - Padding.Right - square : Padding.Left;
            var box = new Rectangle(x, (Height - square) / 2, square - 1, square - 1);
            Color ink = Enabled ? palette.TextPrimary : palette.TextDisabled;
            using (var face = new SolidBrush(palette.Control)) e.Graphics.FillRectangle(face, box);
            using (var border = new Pen(ink, Math.Max(1, metrics.Device(this, metrics.Logical(1))))) e.Graphics.DrawRectangle(border, box);
            if (CheckState != CheckState.Unchecked)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var mark = new Pen(ink, Math.Max(2, square / 9f));
                if (CheckState == CheckState.Indeterminate)
                    e.Graphics.DrawLine(mark, box.Left + square / 4f, box.Top + square / 2f, box.Right - square / 4f, box.Top + square / 2f);
                else
                    e.Graphics.DrawLines(mark, new[] { new PointF(box.Left + square * .2f, box.Top + square * .5f),
                        new PointF(box.Left + square * .43f, box.Top + square * .73f), new PointF(box.Left + square * .8f, box.Top + square * .25f) });
            }
            var text = rtl
                ? new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal - CaptionInset), Height - Padding.Vertical)
                : new Rectangle(Padding.Left + CaptionInset, Padding.Top, Math.Max(1, Width - Padding.Horizontal - CaptionInset), Height - Padding.Vertical);
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.TextBoxControl;
            if (rtl) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, Text, Font, text, ink, flags);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), ink, BackColor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _press = e.Location; _pointerDown = e.Button == MouseButtons.Left; _cancelled = false;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_pointerDown && (Math.Abs(e.X - _press.X) > SystemInformation.DragSize.Width / 2
                || Math.Abs(e.Y - _press.Y) > SystemInformation.DragSize.Height / 2)) _cancelled = true;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pointerDown = false;
            _cancelled = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e) { if (_pointerDown) _cancelled = true; base.OnMouseWheel(e); }
        protected override void OnKeyDown(KeyEventArgs e) { _cancelled = false; base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e) { if (Enabled && !_cancelled) base.OnClick(e); }
    }
}
