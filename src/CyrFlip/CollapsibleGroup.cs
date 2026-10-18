using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace CyrFlip
{
    /// <summary>
    /// One independently collapsible settings group (WINDOWS-UI section 3): a header that is a real
    /// button - disclosure glyph, caption, keyboard activation - over a body that is only ever hidden,
    /// never rebuilt or reset. Collapsing changes visibility alone: the values inside keep whatever the
    /// user left them at, hidden children leave the tab order (invisible controls do in WinForms), and
    /// a child that held the focus hands it to the header before it disappears. The header exposes the
    /// expanded/collapsed state to UI Automation and re-blends its band from the current palette on
    /// every theme change, so the mix never outlives the theme that produced it.
    ///
    /// <para>COLLAPSIBLE-GROUP section 3 owns the native filled state triangle: down when open,
    /// reading-end when closed. This supersedes the older action-chevron convention.</para>
    /// </summary>
    internal sealed class CollapsibleGroup : FlowLayoutPanel
    {
        public const int ChevronSize = 16;
        /// <summary>How far the header band leans toward the accent role; a mix of two roles, never a literal.</summary>
        internal const float BandAmount = 0.08f;

        private readonly HeaderButton _header;
        private readonly FlowLayoutPanel _body;
        // The group's own expansion intent. Not _body.Visible: WinForms' Visible reflects the
        // whole parent chain, so a group on a page that is not the selected one would read as
        // collapsed even when the user left it open.
        private bool _expanded = true;
        private string _summary = "";
        private ThemePalette _palette = ThemePalette.Light;

        /// <param name="id">The stable internal id the group's expansion is remembered under - never a
        /// translated caption (WINDOWS-UI section 3 rule 3).</param>
        public CollapsibleGroup(string id, string caption)
        {
            Id = id;
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(3, 4, 3, 4);

            _header = new HeaderButton(this)
            {
                Text = caption,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                TabStop = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                ImageAlign = System.Drawing.ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(32, 4, 8, 4),
                Margin = new Padding(0),
                UseMnemonic = false,
                // No explicit AccessibleName: the button's own Text is its name, and Text is what the
                // language pass re-assigns - a name set here once stayed in the build language (APP-BEHAVIOUR rule 9).
            };
            _header.FlatAppearance.BorderSize = 0;
            _header.Click += (_, _) => Toggle();
            _header.RightToLeftChanged += (_, _) => UpdateChevron();

            _body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(6, 0, 0, 4),
                Padding = new Padding(0),
            };

            Controls.Add(_header);
            Controls.Add(_body);
            UpdateChevron();
        }

        /// <summary>The header is a band across the whole page (WINDOWS-UI section 3 rule 1), not a pill round its
        /// caption: the page tells it how much room it has and the band takes all of it.</summary>
        public void FitWidth(int width)
        {
            int wanted = Math.Max(1, width);
            UiMetrics metrics = UiMetrics.For(this);
            int target = metrics.Device(this, metrics.Target);
            _header.MaximumSize = new Size(wanted, 0);
            if (_header.MinimumSize != new Size(wanted, target)) _header.MinimumSize = new Size(wanted, target);
            int bodyWidth = Math.Max(1, wanted - _body.Margin.Horizontal);
            _body.MinimumSize = new Size(bodyWidth, 0);
            _body.MaximumSize = new Size(bodyWidth, 0);
            UpdateChevron();
        }

        public string Id { get; }
        public FlowLayoutPanel Body => _body;

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_header != null) _header.Font = Font;
        }

        /// <summary>The group's caption - the header button's text. The group deliberately does not
        /// override <see cref="Control.Text"/> for it: a second Text declaration makes every
        /// <c>GetProperty("Text")</c> walk ambiguous (the localization tests walk exactly so).</summary>
        public string Caption => _header.Text;

        /// <summary>A muted one-line summary of current values shown in the accessible description; the
        /// header keeps painting the caption alone (WINDOWS-UI section 3 rule 1 leaves this "when useful").</summary>
        public string Summary
        {
            get => _summary;
            set
            {
                _summary = value ?? "";
                _header.AccessibleDescription = _summary;
            }
        }

        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                ScrollableControl? viewport = Parent as ScrollableControl;
                bool anchor = Visible && IsHandleCreated && viewport?.AutoScroll == true;
                int before = anchor ? _header.PointToScreen(Point.Empty).Y : 0;
                // Hand the focus to the header before the body goes (WINDOWS-UI section 3 rule 2).
                if (!value && _body.ContainsFocus) _header.Focus();
                _expanded = value;
                _body.Visible = value;
                UpdateChevron();
                _header.Invalidate();
                if (anchor && viewport != null)
                {
                    viewport.PerformLayout();
                    int delta = _header.PointToScreen(Point.Empty).Y - before;
                    if (delta != 0)
                        viewport.AutoScrollPosition = new Point(-viewport.AutoScrollPosition.X, Math.Max(0, -viewport.AutoScrollPosition.Y + delta));
                }
                OnExpandedChanged(EventArgs.Empty);
            }
        }

        public event EventHandler? ExpandedChanged;

        public void Toggle() => Expanded = !Expanded;

        private void OnExpandedChanged(EventArgs e) => ExpandedChanged?.Invoke(this, e);

        /// <summary>The palette pass, run by the settings window after every theme walk (so after every
        /// switch and on build): the band and its hover/pressed edges are re-mixed from the roles of the
        /// palette being applied, and the chevron is drawn again in its text colour.</summary>
        public void ApplyPalette(ThemePalette palette)
        {
            _palette = palette;
            _header.BackColor = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, BandAmount);
            _header.FlatAppearance.MouseOverBackColor = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, BandAmount + 0.06f);
            _header.FlatAppearance.MouseDownBackColor = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, BandAmount + 0.12f);
            _header.ForeColor = palette.TextPrimary;
            UpdateChevron();
        }

        private void UpdateChevron()
        {
            UiMetrics metrics = UiMetrics.For(this);
            int inset = metrics.Device(this, metrics.Logical(ChevronSize + 16));
            int pad = metrics.Device(this, metrics.Logical(8));
            int vertical = metrics.Device(this, metrics.Logical(4));
            bool rtl = _header.RightToLeft == RightToLeft.Yes;
            Padding wanted = rtl ? new Padding(pad, vertical, inset, vertical) : new Padding(inset, vertical, pad, vertical);
            if (_header.Padding != wanted) _header.Padding = wanted;
            _header.Invalidate();
        }

        internal static PointF[] StateTriangle(Rectangle box, bool expanded, bool rtl)
        {
            if (expanded) return new[] { new PointF(box.Left, box.Top + box.Height * .25f),
                new PointF(box.Right, box.Top + box.Height * .25f), new PointF(box.Left + box.Width / 2f, box.Top + box.Height * .75f) };
            float start = rtl ? box.Right - box.Width * .25f : box.Left + box.Width * .25f;
            float end = rtl ? box.Left + box.Width * .25f : box.Right - box.Width * .25f;
            return new[] { new PointF(start, box.Top), new PointF(end, box.Top + box.Height / 2f), new PointF(start, box.Bottom) };
        }

        /// <summary>The header button, carrying the group's expanded state in its accessible object
        /// (WINDOWS-UI section 3 rule 1: "an accessible expanded state").</summary>
        private sealed class HeaderButton : Button
        {
            private readonly CollapsibleGroup _owner;
            private Point _press;
            private bool _drag;
            private Font? _headingFont;

            public HeaderButton(CollapsibleGroup owner) { _owner = owner; MakeHeadingFont(); }

            private void MakeHeadingFont()
            {
                if (Font.Bold) return;
                Font? previous = _headingFont;
                _headingFont = new Font(Font, FontStyle.Bold);
                Font = _headingFont;
                previous?.Dispose();
            }

            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); MakeHeadingFont(); }
            protected override void OnMouseDown(MouseEventArgs e) { _press = e.Location; _drag = false; base.OnMouseDown(e); }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && (Math.Abs(e.X - _press.X) > SystemInformation.DragSize.Width / 2
                    || Math.Abs(e.Y - _press.Y) > SystemInformation.DragSize.Height / 2)) _drag = true;
                base.OnMouseMove(e);
            }
            protected override void OnClick(EventArgs e) { if (!_drag) base.OnClick(e); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                _drag = false;
                HandleKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                UiMetrics metrics = UiMetrics.For(this);
                int size = metrics.Device(this, metrics.Logical(ChevronSize));
                int inset = metrics.Device(this, metrics.Logical(8));
                bool rtl = RightToLeft == RightToLeft.Yes;
                var box = new Rectangle(rtl ? Width - inset - size : inset, (Height - size) / 2, size, size);
                using var ink = new SolidBrush(_owner._palette.TextPrimary);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPolygon(ink, StateTriangle(box, _owner.Expanded, rtl));
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) _headingFont?.Dispose();
            }

            protected override bool IsInputKey(Keys keyData)
                => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

            private void HandleKeyDown(KeyEventArgs e)
            {
                // Left collapses and Right expands at the reading start; mirrored under RTL below.
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    bool rightward = e.KeyCode == Keys.Right;
                    if (RightToLeft == RightToLeft.Yes) rightward = !rightward;
                    _owner.Expanded = rightward;
                    e.Handled = true;
                    return;
                }
                base.OnKeyDown(e);
            }

            protected override AccessibleObject CreateAccessibilityInstance()
                => new GroupAccessibleObject(this, _owner);

            /// <summary>
            /// The group's accessible object. net48's <c>Control.AccessibilityObject</c> returns null
            /// for any accessible object that is not a <see cref="Control.ControlAccessibleObject"/>
            /// (found by the conformance tests the hard way), so this derives from it and layers the
            /// expanded/collapsed state the contract asks for on top of the button's own state.
            /// </summary>
            internal sealed class GroupAccessibleObject : Control.ControlAccessibleObject
            {
                private readonly CollapsibleGroup _owner;

                public GroupAccessibleObject(HeaderButton header, CollapsibleGroup owner) : base(header)
                {
                    _owner = owner;
                }

                public override string? Description => string.IsNullOrEmpty(_owner.Summary) ? null : _owner.Summary;
                public override AccessibleRole Role => AccessibleRole.ButtonDropDown;
                public override AccessibleStates State
                {
                    get
                    {
                        AccessibleStates state = base.State | (_owner.Expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
                        return state;
                    }
                }

                public override void DoDefaultAction() => _owner.Toggle();
            }
        }
    }

    /// <summary>
    /// The net48 half of the reference's numeric-stepper pass (WINDOWS-UI section 4: "Readable digits
    /// AND readable increment/decrement buttons. Scale the entire native editor"). A pure decision is
    /// not enough here - the editor has to be widened, the digits given a pixel-sized font so points
    /// cannot re-scale them twice, and the native spin halves widened to the pointer target floor
    /// (28 logical px under ICON-RENDER section 3 rule 5). net48 has no <c>_defaultButtonsWidth</c>;
    /// the spin halves are an ordinary child control (<c>UpDownBase.upDownButtons</c>), so they are
    /// resized directly, and a runtime that refuses leaves the native width - the editor is still
    /// readable, only the halves stay stock. Everything here is re-applied after a language change,
    /// because the font the metrics derive from is the one that changed.
    /// </summary>
    internal static class SettingsNumeric
    {
        internal const int EditorWidth = 110;
        internal const int DigitFontPixels = 16;
        internal const int SpinWidth = 28;

        // The pixel fonts this class handed to editors. Ownership matters: the font a control reports
        // before we ever set one is the framework's shared default - disposing that poisons every
        // later text measurement in the process (found by the conformance tests the hard way).
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, Font> OwnedFonts
            = new System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, Font>();
        // The editors whose SizeChanged already re-widens the spin halves.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, object> Hooked
            = new System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, object>();

        /// <summary>Logical 96-DPI px to device px for the control's current monitor.</summary>
        internal static int Scale(Control control, int logical)
            => Math.Max(logical, (int)Math.Round(logical * control.DeviceDpi / 96.0));

        public static void Apply(NumericUpDown editor)
        {
            float digitPx = (float)Math.Round(DigitFontPixels * editor.DeviceDpi / 96.0);
            if (!OwnedFonts.TryGetValue(editor, out Font? owned) || Math.Abs(owned.Size - digitPx) >= 0.5f)
            {
                Font next = new Font(editor.Font.FontFamily, digitPx, GraphicsUnit.Pixel);
                editor.Font = next;
                if (owned != null) { OwnedFonts.Remove(editor); owned.Dispose(); }
                OwnedFonts.Add(editor, next);
            }
            editor.Width = Math.Max(editor.Width, Scale(editor, EditorWidth));
            editor.MinimumSize = new Size(Scale(editor, EditorWidth), 0);
            // Apply runs again after every language change; the handler goes on once, not once per run.
            if (!Hooked.TryGetValue(editor, out _))
            {
                Hooked.Add(editor, Hooked);
                editor.SizeChanged += (_, _) => WidenSpins(editor);
            }
            WidenSpins(editor);
        }

        private static void WidenSpins(NumericUpDown editor)
        {
            try
            {
                if (SpinButtons(editor) is Control spins)
                {
                    int width = Scale(editor, SpinWidth);
                    // The floor first: whatever re-lays the halves out afterwards still clamps to it.
                    if (spins.MinimumSize.Width < width) spins.MinimumSize = new Size(width, 0);
                    if (spins.Width < width) spins.Width = width;
                }
            }
            catch
            {
                // A future runtime that re-lays the halves out from its own metric: leave them native.
            }
        }

        internal static Control? SpinButtonsForTest(NumericUpDown editor) => SpinButtons(editor);

        private static Control? SpinButtons(NumericUpDown editor)
            => typeof(UpDownBase).GetField("upDownButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(editor) as Control;
    }
}
