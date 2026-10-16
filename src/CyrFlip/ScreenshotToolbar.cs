using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The draw-on-capture editor's toolbar (S0071 6.4): one small borderless topmost window -
    /// the six shape tools, the eraser, a separator, the eight palette swatches, a separator,
    /// Cancel / Clear / OK - draggable by any non-button part of its surface.
    ///
    /// <para><b>Never in the picture by construction</b>: the delivered image is composed from the
    /// frozen frame + shape objects (<see cref="AnnotationComposer"/>), not re-grabbed from the
    /// screen, so neither this toolbar nor the dimming can enter the result - and it should be
    /// visible to the user's eye anyway, so <c>WDA_EXCLUDEFROMCAPTURE</c> is deliberately not used
    /// here (unlike the caret overlay, which hides from other people's captures).</para>
    ///
    /// <para>Chrome entirely from <c>ThemePalette</c> roles (<c>APP-STYLE</c> rule 4) - built in
    /// <c>ThemePalette.Light</c> like every control, and every themed part is its own
    /// <c>IThemeAware</c> control: the theme walk recolours each control when it reaches it, so no
    /// window-level pass can write a child before the walk has recorded what its constructor built.
    /// The swatches are content, not theme, drawn in <see cref="SwatchButton.OnPaint"/> so no control
    /// property ever carries a colour the theme cannot reach, and they keep their colours in high
    /// contrast (<see cref="AnnotationPalette"/>, the LAYOUT-PALETTE precedent). <c>Opacity</c> is
    /// 0.67 - the "33% transparency" of the request read as transparency, a toolbar you can see the
    /// picture through but read at once (open question Q3: one constant if 33% opacity was meant).
    /// The position is session-only, never stored.</para>
    /// </summary>
    internal sealed class ScreenshotToolbar : ThemedForm, IThemeAware
    {
        /// <summary>Raised when the user picks a tool (the eraser included).</summary>
        public event Action<AnnotationTool>? ToolChanged;
        /// <summary>Raised when the user picks a swatch.</summary>
        public event Action<Color>? ColorChanged;
        public event Action? CancelRequested;
        public event Action? ClearRequested;
        public event Action? OkRequested;

        /// <summary>"33% transparency" (S0071 6.4, Q3).</summary>
        internal event Action? HelpAction;
        internal void ShowContextHelp() => HelpAction?.Invoke();

        public const double ToolbarOpacity = 0.67;

        private readonly Dictionary<AnnotationTool, ToolButton> _tools = new Dictionary<AnnotationTool, ToolButton>();
        private readonly List<SwatchButton> _swatches = new List<SwatchButton>();
        private readonly Func<string, string> _translate;
        private AnnotationTool _tool;
        private Color _ink = AnnotationPalette.Default;
        // The chrome palette in force - ThemePalette.Light at build time (the design-time default),
        // then whatever the last theme application brought. User actions repaint from this.
        private ThemePalette _chrome = ThemePalette.Light;
        private bool _dragging;
        private Point _dragAnchor;

        public ScreenshotToolbar(AnnotationTool tool, Color ink, Func<string, string> translate)
        {
            _tool = tool;
            _ink = ink;
            _translate = translate;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            KeyPreview = false;
            Text = "CyrFlip";
            Opacity = ToolbarOpacity;
            Padding = new Padding(1);   // the 1px theme border is painted by OnPaintBackground's frame

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                Padding = new Padding(4, 3, 4, 3),
            };
            row.MouseDown += BeginDrag;
            row.MouseMove += Drag;
            row.MouseUp += EndDrag;
            Controls.Add(row);

            Label Separ() => new SeparatorBar
            {
                Width = 2,
                Height = 26,
                Margin = new Padding(4, 3, 4, 3),
            };

            foreach (AnnotationTool toolKind in new[]
                     {
                         AnnotationTool.Ellipse, AnnotationTool.FilledEllipse, AnnotationTool.Rectangle,
                         AnnotationTool.FilledRectangle, AnnotationTool.Line, AnnotationTool.Arrow,
                     })
            {
                ToolButton button = MakeToolButton(toolKind);
                _tools[toolKind] = button;
                row.Controls.Add(button);
            }
            ToolButton eraser = MakeToolButton(AnnotationTool.Eraser);
            _tools[AnnotationTool.Eraser] = eraser;
            row.Controls.Add(eraser);
            row.Controls.Add(Separ());

            for (int i = 0; i < AnnotationPalette.Swatches.Length; i++)
            {
                int index = i;
                var swatch = new SwatchButton(AnnotationPalette.Swatches[i])
                {
                    Size = new Size(22, 22),
                    Margin = new Padding(1, 2, 1, 2),
                    AccessibleName = AnnotationPalette.Name(index, translate),
                    AccessibleRole = AccessibleRole.CheckButton,
                    TabStop = false,
                };
                // The ink itself is the only content: no text on the swatch, the accessible name
                // and the tooltip carry the colour's words (there is no colour-only state).
                _tips.SetToolTip(swatch, AnnotationPalette.Name(index, translate));
                swatch.Click += (_, _) => PickColor(index);
                _swatches.Add(swatch);
                row.Controls.Add(swatch);
            }
            row.Controls.Add(Separ());

            Button cancel = new CommandButton { Text = translate("Cancel") };
            cancel.Click += (_, _) => CancelRequested?.Invoke();
            row.Controls.Add(cancel);
            Button clear = new CommandButton { Text = translate("Clear") };
            clear.Click += (_, _) => ClearRequested?.Invoke();
            row.Controls.Add(clear);
            Button ok = new OkButton { Text = translate("OK") };
            ok.Click += (_, _) => OkRequested?.Invoke();
            row.Controls.Add(ok);
            Button help = ContextHelpUi.Button(this);
            help.Click += (_, _) => HelpAction?.Invoke();
            row.Controls.Add(help);

            _tips.SetToolTip(cancel, translate("Discard the capture; nothing is put on the clipboard."));
            _tips.SetToolTip(clear, translate("Remove everything drawn; the frozen picture stays."));
            _tips.SetToolTip(ok, translate("Put the picture with the drawings on the clipboard."));
            PickTool(_tool, raise: false);
            PickColor(AnnotationPalette.IndexOf(_ink), raise: false);
        }

        private readonly ToolTip _tips = new ToolTip();

        private ToolButton MakeToolButton(AnnotationTool tool)
        {
            var button = new ToolButton
            {
                Tool = tool,
                Size = new Size(28, 26),
                Margin = new Padding(1, 2, 1, 2),
                TabStop = false,
            };
            button.AccessibleName = ToolName(tool, _translate);
            _tips.SetToolTip(button, ToolName(tool, _translate));
            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked) PickTool(tool, raise: true);
            };
            return button;
        }

        /// <summary>The tool's words - the tooltip and the accessible name are the same sentence.</summary>
        public static string ToolName(AnnotationTool tool, Func<string, string> translate)
        {
            switch (tool)
            {
                case AnnotationTool.Ellipse: return translate("Draw an oval");
                case AnnotationTool.FilledEllipse: return translate("Draw a filled oval");
                case AnnotationTool.Rectangle: return translate("Draw a rectangle");
                case AnnotationTool.FilledRectangle: return translate("Draw a filled rectangle");
                case AnnotationTool.Line: return translate("Draw a line");
                case AnnotationTool.Arrow: return translate("Draw an arrow");
                case AnnotationTool.Eraser: return translate("Erase drawn shapes");
                default: return tool.ToString();
            }
        }

        private void PickTool(AnnotationTool tool, bool raise)
        {
            _tool = tool;
            foreach (KeyValuePair<AnnotationTool, ToolButton> pair in _tools)
            {
                // A pressed tool carries the ink so the active tool and colour are one visible choice;
                // each button repaints itself from the palette in force.
                pair.Value.Selected = pair.Key == tool;
                pair.Value.Checked = pair.Key == tool;
                pair.Value.ApplyChrome(_chrome);
            }
            if (raise) ToolChanged?.Invoke(tool);
        }

        private void PickColor(int index, bool raise = true)
        {
            _ink = AnnotationPalette.Swatches[index];
            for (int i = 0; i < _swatches.Count; i++)
            {
                // The selected swatch carries a ring in the chrome's text ink, so the selection is
                // visible whatever the swatch's own colour.
                _swatches[i].Selected = i == index;
                _swatches[i].Ring = _chrome.TextPrimary;
                _swatches[i].Invalidate();
            }
            if (raise) ColorChanged?.Invoke(_ink);
        }

        /// <summary>Show beside the pointer, clamped fully into the pointer's monitor (S0071 6.4).</summary>
        public void PlaceNear(Point pointerPhysical)
        {
            Point target = new Point(pointerPhysical.X + 16, pointerPhysical.Y + 16);
            Rectangle monitor;
            try { monitor = Screen.FromPoint(pointerPhysical).Bounds; }
            catch { monitor = Screen.PrimaryScreen!.Bounds; }
            var area = new Rectangle(target, Size);
            area.X = Math.Max(monitor.X, Math.Min(area.X, monitor.Right - Size.Width));
            area.Y = Math.Max(monitor.Y, Math.Min(area.Y, monitor.Bottom - Size.Height));
            Bounds = area;
        }

        /// <summary>Drag by any non-button part: the row's background and the separators.</summary>
        private void BeginDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragAnchor = e.Location;
        }

        private void Drag(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragAnchor.X, screen.Y - _dragAnchor.Y);
        }

        private void EndDrag(object? sender, MouseEventArgs e) => _dragging = false;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            // The 1px border in a theme ink role - "noticeable, framed" (S0071 6.4).
            using var pen = new Pen(_chrome.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Invalidate();
        }

        /// <summary>
        /// The theme walk applied a palette: remember it for the border and the user-action repaints.
        /// The themed children are <c>IThemeAware</c> themselves and are recoloured by the walk when it
        /// reaches each of them - this form-level pass deliberately touches nobody else, so a child's
        /// "built" record is always what its constructor made.
        /// </summary>
        public void ApplyTheme(ThemePalette palette)
        {
            _chrome = palette;
            Invalidate();
        }

        /// <summary>A shape tool: a check box drawn as a button, pressed while it is the active tool.</summary>
        private sealed class ToolButton : CheckBox, IThemeAware
        {
            public ToolButton()
            {
                Appearance = Appearance.Button;
                FlatStyle = FlatStyle.Flat;
                UseVisualStyleBackColor = false;
                // The built face: the design-time default the walk records.
                BackColor = ThemePalette.Light.Control;
                ForeColor = ThemePalette.Light.TextPrimary;
                FlatAppearance.BorderColor = ThemePalette.Light.Border;
            }

            public AnnotationTool Tool { get; set; }
            public bool Selected { get; set; }

            public void ApplyTheme(ThemePalette palette) => ApplyChrome(palette);

            public void ApplyChrome(ThemePalette palette)
            {
                BackColor = Selected ? palette.SurfaceSelected : palette.Control;
                ForeColor = palette.TextPrimary;
                FlatAppearance.BorderColor = palette.Border;
                Invalidate();
            }
        }

        /// <summary>The row's thin divider; a BackColour the dark walk accepts (ControlHover, not Border).</summary>
        private sealed class SeparatorBar : Label, IThemeAware
        {
            public SeparatorBar()
            {
                AutoSize = false;
                BackColor = ThemePalette.Light.ControlHover;
            }

            public void ApplyTheme(ThemePalette palette)
            {
                BackColor = palette.ControlHover;
                Invalidate();
            }
        }

        /// <summary>Cancel / Clear: plain flat buttons the theme walk dresses itself (no explicit colour).</summary>
        private sealed class CommandButton : Button
        {
            public CommandButton()
            {
                FlatStyle = FlatStyle.Flat;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Margin = new Padding(3, 2, 1, 2);
                Padding = new Padding(6, 2, 6, 2);
            }
        }

        /// <summary>OK: the affirmative answer's accent face, built in Light and re-read per palette.</summary>
        private sealed class OkButton : Button, IThemeAware
        {
            public OkButton()
            {
                FlatStyle = FlatStyle.Flat;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Margin = new Padding(3, 2, 1, 2);
                Padding = new Padding(6, 2, 6, 2);
                BackColor = ThemePalette.Light.Accent;
                ForeColor = ThemePalette.Light.AccentInk;
                FlatAppearance.BorderColor = ThemePalette.Light.Accent;
            }

            public void ApplyTheme(ThemePalette palette)
            {
                BackColor = palette.Accent;
                ForeColor = palette.AccentInk;
                FlatAppearance.BorderColor = palette.Accent;
                Invalidate();
            }
        }

        /// <summary>
        /// One palette swatch: a button whose face is the fixed content ink, painted - never carried
        /// in a control property, so the theme walk has nothing to miss and high contrast keeps the
        /// swatches (S0071 6.4, the LAYOUT-PALETTE precedent).
        /// </summary>
        private sealed class SwatchButton : Button
        {
            public SwatchButton(Color ink)
            {
                Ink = ink;
                FlatStyle = FlatStyle.Flat;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            public Color Ink { get; }
            public bool Selected { get; set; }
            public Color Ring { get; set; } = ThemePalette.Light.TextPrimary;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Rectangle square = ClientRectangle;
                square.Inflate(-3, -3);
                using (var brush = new SolidBrush(Ink))
                    e.Graphics.FillRectangle(brush, square);
                if (Selected)
                    using (var ring = new Pen(Ring, 2f))
                        e.Graphics.DrawRectangle(ring, ClientRectangle.X + 1, ClientRectangle.Y + 1,
                            ClientRectangle.Width - 3, ClientRectangle.Height - 3);
            }
        }
    }
}
