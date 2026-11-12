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

        protected override void OnVisibleChanged(EventArgs e)
        {
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
