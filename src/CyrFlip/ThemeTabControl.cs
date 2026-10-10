using System;
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
    ///
    /// <para>The strip's accessibility is ours too (S0060 P4, H2): the native list exposes no
    /// <c>TabItem</c> children an assistive walker can enumerate, so this control provides its own -
    /// a <c>PageTabList</c> whose children are the pages, each named by its caption, selectable, and
    /// reported with its screen rectangle.</para>
    /// </summary>
    internal sealed class ThemeTabControl : TabControl, IThemeAware
    {
        private ThemePalette _palette = ThemePalette.Light;

        public ThemeTabControl()
        {
            // Light is the first palette every window is built in, and it is painted here too (see ApplyTheme).
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            // Painted here in light and dark alike: the native strip draws a bordered box round every page
            // name, which is not the navigation list WINDOWS-UI section 2 describes (an accent bar and a soft
            // fill, nothing else). High contrast keeps the native paint and its system colours.
            bool own = palette.Kind != ThemeKind.HighContrast;
            if (GetStyle(ControlStyles.UserPaint) == own) { Invalidate(); return; }
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, own);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var back = new SolidBrush(_palette.SurfaceWindow)) e.Graphics.FillRectangle(back, ClientRectangle);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle bounds = GetTabRect(i);
                if (!e.ClipRectangle.IntersectsWith(bounds)) continue;
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, bounds, i,
                    i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
            }
            base.OnPaint(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ThemeTabControlAccessibleObject(this);

        /// <summary>
        /// The page list as a <c>PageTabList</c>. net48 returns null from
        /// <see cref="Control.AccessibilityObject"/> for any object that is not a
        /// <see cref="Control.ControlAccessibleObject"/> (proved against the runtime in S0048), so this
        /// derives from it; the children it hands out are plain accessible objects.
        /// </summary>
        internal sealed class ThemeTabControlAccessibleObject : Control.ControlAccessibleObject
        {
            public ThemeTabControlAccessibleObject(ThemeTabControl owner) : base(owner) { }

            private ThemeTabControl? Tabs => Owner as ThemeTabControl;

            public override AccessibleRole Role => AccessibleRole.PageTabList;
            public override string? Name => Tabs?.AccessibilityObject == null ? null : Tabs.Text;
            public override int GetChildCount() => Tabs?.TabPages.Count ?? 0;

            public override AccessibleObject? GetChild(int index)
                => Tabs != null && index >= 0 && index < Tabs.TabPages.Count ? new TabItemAccessibleObject(Tabs, index) : null;

            public override AccessibleObject? GetSelected()
            {
                ThemeTabControl? tabs = Tabs;
                if (tabs == null || tabs.SelectedIndex < 0) return null;
                return GetChild(tabs.SelectedIndex);
            }

            public override AccessibleObject? HitTest(int x, int y)
            {
                ThemeTabControl? tabs = Tabs;
                if (tabs == null) return null;
                Point point = tabs.PointToClient(new Point(x, y));
                for (int i = 0; i < tabs.TabPages.Count; i++)
                    if (tabs.GetTabRect(i).Contains(point)) return GetChild(i);
                return null;
            }
        }

        /// <summary>One page of the strip: named by the caption the language pass keeps current.</summary>
        internal sealed class TabItemAccessibleObject : AccessibleObject
        {
            private readonly ThemeTabControl _tabs;
            private readonly int _index;

            public TabItemAccessibleObject(ThemeTabControl tabs, int index)
            {
                _tabs = tabs;
                _index = index;
            }

            private TabPage Page => _tabs.TabPages[_index];

            public override string? Name => Page.Text;
            public override AccessibleRole Role => AccessibleRole.PageTab;
            public override AccessibleObject? Parent => _tabs.AccessibilityObject;
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (_tabs.SelectedIndex == _index) state |= AccessibleStates.Selected | AccessibleStates.Focused;
                    return state;
                }
            }

            public override Rectangle Bounds => _tabs.RectangleToScreen(_tabs.GetTabRect(_index));

            public override void DoDefaultAction() => _tabs.SelectedIndex = _index;

            public override void Select(AccessibleSelection flags)
            {
                if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) != 0)
                    _tabs.SelectedIndex = _index;
            }
        }
    }
}
