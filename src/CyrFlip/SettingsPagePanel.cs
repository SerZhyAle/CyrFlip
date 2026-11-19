using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The content panel of one settings page (S0076, SETTINGS-NAVIGATION section 5 rule 4: a warm page change
    /// under 150 ms). WinForms lays a <see cref="ScrollableControl"/> out again every time it becomes visible, and
    /// the visibility change cascades: the page lays out all of its rows, then every group, every group body and
    /// every row panel lays its own children out again - a row four panels deep is measured four times per page
    /// change, which was most of a warm change on the heavy pages. A hidden page is never out of date: a layout
    /// request on a hidden control runs at once (<c>Control.PerformLayout</c> does not look at visibility), so
    /// once every control on the page has its handle the cascade only recomputes what is already on it. Then the
    /// whole subtree's layout is suspended for the length of the cascade and resumed without a layout pass.
    /// A show that still creates a handle (the first visit, a row rebuilt while the page was hidden) runs the
    /// ordinary path: handle creation may change a control's size (a combo box takes its height from its
    /// handle), and those layout requests must not be dropped.
    /// </summary>
    internal sealed class SettingsPagePanel : FlowLayoutPanel
    {
        /// <summary>
        /// The test seam: off, every show takes WinForms' own path, which is what
        /// <c>SettingsPagePanelTests</c> compares the skipped layout against.
        /// </summary>
        internal static bool SkipRelayoutOnShow = true;

        /// <summary>How many shows took the skip - the test seam that proves the comparison compared something.</summary>
        internal static int SkippedShows;

        private bool _layoutDeferred;

        /// <summary>True while this page's layout is held back until the page is first shown (ticket S0135, L3).</summary>
        internal bool LayoutDeferred => _layoutDeferred;

        /// <summary>
        /// Holds the layout of this page's whole subtree back until the page is first shown. The window builds all
        /// fifteen pages before it appears, and the first layout of the whole tree was about two thirds of the first
        /// refresh (S0135 after-phase1 trace: <c>first:lang:resume-captions</c>, <c>resume-geometry</c> and
        /// <c>notes:resume</c>) - spent on fourteen pages nobody has looked at. One extra suspension on every
        /// control of the page makes every later suspend/resume pair inside it (the language pass, the notes, a
        /// table rebuild) balanced and silent: nothing is laid out until <see cref="ReleaseDeferredLayout"/>, which
        /// the page's first show calls. The properties are all applied meanwhile; only the layout waits.
        /// </summary>
        internal void DeferLayout()
        {
            if (_layoutDeferred) return;
            _layoutDeferred = true;
            SuspendDeep(this);
        }

        /// <summary>
        /// Lays the page out now. Called when the page is first shown (before its first paint), and by whoever needs
        /// the geometry of a page nobody has shown - the tests and the capture tools. Controls created while the
        /// layout was held back carry no suspension of their own, and resuming one is a no-op, so the walk is balanced.
        /// </summary>
        internal void ReleaseDeferredLayout()
        {
            if (!_layoutDeferred) return;
            _layoutDeferred = false;
            ResumeDeep(this);
        }

        private static void SuspendDeep(Control control)
        {
            control.SuspendLayout();
            foreach (Control child in control.Controls) SuspendDeep(child);
        }

        private static void ResumeDeep(Control control)
        {
            // Inner first, so no container lays itself out while a child of it is still suspended.
            foreach (Control child in control.Controls) ResumeDeep(child);
            control.ResumeLayout(performLayout: true);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible && _layoutDeferred)
            {
                // The first show of a page that was built hidden: lay it out once, before its first paint. The
                // handles are not created yet, so the ordinary path below would run anyway.
                ReleaseDeferredLayout();
                base.OnVisibleChanged(e);
                return;
            }
            if (!SkipRelayoutOnShow || !Visible || !IsHandleCreated || !AllHandlesCreated(this))
            {
                base.OnVisibleChanged(e);
                return;
            }
            var suspended = new List<Control>();
            SkippedShows++;
            Suspend(this, suspended);
            try { base.OnVisibleChanged(e); }
            finally
            {
                // Inner first, so no container resumes while a child of it is still suspended.
                for (int i = suspended.Count - 1; i >= 0; i--) suspended[i].ResumeLayout(false);
            }
        }

        /// <summary>
        /// Whether every control the show reaches already has its handle - false on a first visit, and after a row
        /// was rebuilt while the page was hidden. Asked while this panel is visible, so a child's <c>Visible</c>
        /// is its own state.
        /// </summary>
        private static bool AllHandlesCreated(Control control)
        {
            foreach (Control child in control.Controls)
            {
                if (!child.Visible) continue;
                if (!child.IsHandleCreated || !AllHandlesCreated(child)) return false;
            }
            return true;
        }

        private static void Suspend(Control control, List<Control> suspended)
        {
            if (!(control is ScrollableControl) || control.Controls.Count == 0) return;
            control.SuspendLayout();
            suspended.Add(control);
            foreach (Control child in control.Controls) Suspend(child, suspended);
        }
    }
}
