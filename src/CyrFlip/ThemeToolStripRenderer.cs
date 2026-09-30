using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Every <see cref="ToolStrip"/> menu of the app in dark: the tray menu, the text context menu, the
    /// taskbar button's menu and the launcher list's menu (S0020 section 3). A colour table alone does not
    /// reach the text, the check mark or the submenu arrow, hence the overrides.
    ///
    /// <para>Installed on the UI thread (<c>[ThreadStatic]</c> in net48) through <see cref="ToolStripManager.Renderer"/> rather than
    /// assigned per menu: every strip left in manager render mode - all of CyrFlip's - draws with it, so a
    /// menu built next year cannot forget the theme, and <see cref="TextContextMenu"/> stays a pure
    /// builder that knows nothing about colour. Light and high contrast put the stock renderer back, so
    /// both look exactly as they always did.</para>
    /// </summary>
    internal sealed class ThemeToolStripRenderer : ToolStripProfessionalRenderer
    {
        private static ToolStripRenderer? _stock;
        private static ThemeToolStripRenderer? _dark;
        private readonly ThemePalette _palette;

        private ThemeToolStripRenderer(ThemePalette palette) : base(new PaletteColorTable(palette))
        {
            _palette = palette;
            RoundedEdges = false;
        }

        /// <summary>The renderer for <paramref name="palette"/>: ours in dark, the stock one otherwise.</summary>
        public static ToolStripRenderer For(ThemePalette palette)
        {
            if (palette.IsDark) return _dark ??= new ThemeToolStripRenderer(palette);
            // The stock renderer, remembered the first time round: the professional renderer with the
            // system colour table, which follows high contrast by itself.
            return _stock ??= ToolStripManager.Renderer is ThemeToolStripRenderer
                ? new ToolStripProfessionalRenderer()
                : ToolStripManager.Renderer;
        }

        /// <summary>Swap the renderer every manager-mode strip on this thread draws with.</summary>
        public static void Install(ThemePalette palette)
        {
            ToolStripRenderer wanted = For(palette);
            if (!ReferenceEquals(ToolStripManager.Renderer, wanted)) ToolStripManager.Renderer = wanted;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // A ToolStripLabel is a caption - the selection statistics at the foot of the text context
            // menu - and must read as muted, not as a disabled command.
            e.TextColor = e.Item is ToolStripLabel ? _palette.TextMuted
                : e.Item.Enabled ? _palette.TextPrimary : _palette.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? _palette.TextDisabled : _palette.TextPrimary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The stock check mark is a black bitmap - invisible here - so the mark is drawn, not blitted.
            Rectangle box = e.ImageRectangle;
            if (box.Width <= 0 || box.Height <= 0) return;
            box.Inflate(1, 1);
            using (var face = new SolidBrush(e.Item.Selected ? _palette.ControlPressed : _palette.ControlHover))
                e.Graphics.FillRectangle(face, box);
            using (var edge = new Pen(_palette.Border))
                e.Graphics.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);

            SmoothingMode smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = box.Width, h = box.Height;
            using (var mark = new Pen(e.Item.Enabled ? _palette.TextPrimary : _palette.TextDisabled, System.Math.Max(1.5f, w / 9f)))
            {
                e.Graphics.DrawLines(mark, new[]
                {
                    new PointF(box.X + w * 0.25f, box.Y + h * 0.52f),
                    new PointF(box.X + w * 0.43f, box.Y + h * 0.70f),
                    new PointF(box.X + w * 0.76f, box.Y + h * 0.32f),
                });
            }
            e.Graphics.SmoothingMode = smoothing;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle bounds = e.Item.ContentRectangle;
            using var line = new Pen(_palette.Border);
            if (e.Vertical)
            {
                int x = bounds.Left + bounds.Width / 2;
                e.Graphics.DrawLine(line, x, bounds.Top + 2, x, bounds.Bottom - 2);
            }
            else
            {
                int y = bounds.Top + bounds.Height / 2;
                e.Graphics.DrawLine(line, bounds.Left + 4, y, bounds.Right - 4, y);
            }
        }

        /// <summary>The colour table: surfaces, hover, borders - everything that is a fill or a line.</summary>
        private sealed class PaletteColorTable : ProfessionalColorTable
        {
            private readonly ThemePalette _p;

            public PaletteColorTable(ThemePalette palette)
            {
                _p = palette;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => _p.SurfaceRaised;
            public override Color ImageMarginGradientBegin => _p.SurfaceRaised;
            public override Color ImageMarginGradientMiddle => _p.SurfaceRaised;
            public override Color ImageMarginGradientEnd => _p.SurfaceRaised;
            public override Color MenuBorder => _p.Border;
            public override Color MenuItemBorder => _p.ControlHover;
            public override Color MenuItemSelected => _p.ControlHover;
            public override Color MenuItemSelectedGradientBegin => _p.ControlHover;
            public override Color MenuItemSelectedGradientEnd => _p.ControlHover;
            public override Color MenuItemPressedGradientBegin => _p.ControlPressed;
            public override Color MenuItemPressedGradientMiddle => _p.ControlPressed;
            public override Color MenuItemPressedGradientEnd => _p.ControlPressed;
            public override Color MenuStripGradientBegin => _p.SurfaceWindow;
            public override Color MenuStripGradientEnd => _p.SurfaceWindow;
            public override Color SeparatorDark => _p.Border;
            public override Color SeparatorLight => _p.Border;
            public override Color CheckBackground => _p.ControlHover;
            public override Color CheckSelectedBackground => _p.ControlPressed;
            public override Color CheckPressedBackground => _p.ControlPressed;
            public override Color ButtonSelectedBorder => _p.Border;
            public override Color ButtonSelectedHighlight => _p.ControlHover;
            public override Color ButtonPressedHighlight => _p.ControlPressed;
            public override Color ButtonCheckedHighlight => _p.ControlHover;
            public override Color ToolStripBorder => _p.Border;
            public override Color ToolStripGradientBegin => _p.SurfaceWindow;
            public override Color ToolStripGradientMiddle => _p.SurfaceWindow;
            public override Color ToolStripGradientEnd => _p.SurfaceWindow;
            public override Color StatusStripGradientBegin => _p.SurfaceWindow;
            public override Color StatusStripGradientEnd => _p.SurfaceWindow;
        }
    }
}
