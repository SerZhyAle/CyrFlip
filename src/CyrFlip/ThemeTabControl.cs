using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The settings window's page list. Its pages are owner-drawn already, but the strip around them -
    /// the band below the last page and the frame round the page area - is painted by the native control
    /// in the light visual style whatever colour it is given. In dark the control therefore paints
    /// itself: the background, then every page through the same <see cref="TabControl.DrawItem"/> handler
    /// the native paint calls. Light and high contrast leave the native paint alone, so both look exactly
    /// as they always did. Mouse and keyboard stay native in every theme; only the painting moves.
    /// </summary>
    internal sealed class ThemeTabControl : TabControl, IThemeAware
    {
        private ThemePalette _palette = ThemePalette.Light;

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            bool own = palette.IsDark;
            if (GetStyle(ControlStyles.UserPaint) == own) { Invalidate(); return; }
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, own);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var back = new SolidBrush(_palette.SurfaceWindow)) e.Graphics.FillRectangle(back, ClientRectangle);
            Rectangle page = DisplayRectangle;
            page.Inflate(1, 1);
            using (var line = new Pen(_palette.Border)) e.Graphics.DrawRectangle(line, page.X, page.Y, page.Width - 1, page.Height - 1);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle bounds = GetTabRect(i);
                if (!e.ClipRectangle.IntersectsWith(bounds)) continue;
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, bounds, i,
                    i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
            }
            base.OnPaint(e);
        }
    }
}
