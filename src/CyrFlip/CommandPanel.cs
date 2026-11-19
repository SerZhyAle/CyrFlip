using System;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>BUTTONS sections 3-4: content widths, whole-button wrapping and row-local heights.</summary>
    internal static class CommandPanel
    {
        private sealed class State { internal bool Busy; internal int Width; }
        private static readonly ConditionalWeakTable<FlowLayoutPanel, State> States = new ConditionalWeakTable<FlowLayoutPanel, State>();

        /// <summary>What a mixed strip's button was built with: its minimum height and its own height.</summary>
        private sealed class BuiltHeights
        {
            internal int MinimumHeight;
            internal int Height;
        }

        /// <summary>A mixed strip's button by what it was built with, so a row floor never ratchets.</summary>
        private static readonly ConditionalWeakTable<Button, BuiltHeights> BaseHeights = new ConditionalWeakTable<Button, BuiltHeights>();

        internal static void Fit(FlowLayoutPanel panel, int availableWidth)
        {
            if (!States.TryGetValue(panel, out State? state))
            {
                state = new State();
                States.Add(panel, state);
                State captured = state;
                panel.Layout += (_, _) => Normalize(panel, captured);
            }
            state.Width = Math.Max(1, availableWidth);
            Normalize(panel, state);
        }

        private static void Normalize(FlowLayoutPanel panel, State state)
        {
            if (state.Busy || panel.IsDisposed) return;
            Button[] buttons = panel.Controls.OfType<Button>().ToArray();
            if (buttons.Length == 0) return;
            if (buttons.Length != panel.Controls.Count) { NormalizeMixed(panel, state, buttons); return; }
            state.Busy = true;
            panel.SuspendLayout();
            try
            {
                panel.WrapContents = true;
                panel.FlowDirection = FlowDirection.LeftToRight; // WinForms mirrors this under RTL
                panel.MaximumSize = new Size(state.Width, 0);
                foreach (Button button in buttons)
                {
                    UiMetrics metrics = UiMetrics.For(button);
                    int target = metrics.Device(button, metrics.Target);
                    int width = Math.Max(1, state.Width - panel.Padding.Horizontal - button.Margin.Horizontal);
                    button.Dock = DockStyle.None;
                    button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    button.AutoSize = true;
                    button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    button.MaximumSize = new Size(width, 0);
                    int textWidth = Math.Max(1, width - button.Padding.Horizontal - 6);
                    int textHeight = TextRenderer.MeasureText(button.Text, button.Font, new Size(textWidth, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
                    int naturalHeight = Math.Max(target, textHeight + button.Padding.Vertical + metrics.Device(button, metrics.Logical(8)));
                    button.MinimumSize = new Size(Math.Min(width, target), naturalHeight);
                }
            }
            finally { panel.ResumeLayout(true); }
            try
            {
                // Row membership is taken after wrapping and after clearing the previous row's floor.
                foreach (var row in buttons.Where(b => b.Visible).GroupBy(b => b.Top))
                {
                    int height = row.Max(b => b.GetPreferredSize(new Size(b.MaximumSize.Width, 0)).Height);
                    foreach (Button button in row) button.MinimumSize = new Size(button.MinimumSize.Width, height);
                }
            }
            finally { state.Busy = false; }
        }

        /// <summary>
        /// An editor strip that holds buttons beside other controls (a caption, a combo box, a box). A strip
        /// built to stay on one line (<c>WrapContents</c> off) keeps its own policy. A wrapping one is capped to
        /// the column, so its items wrap whole instead of running past the page, and the buttons of each visual
        /// row take the height of the row's tallest button (BUTTONS section 4). Widths and the other controls
        /// stay as they were built.
        /// </summary>
        private static void NormalizeMixed(FlowLayoutPanel panel, State state, Button[] buttons)
        {
            if (!panel.WrapContents) return;
            state.Busy = true;
            try
            {
                panel.SuspendLayout();
                try
                {
                    if (panel.MaximumSize.Width != state.Width) panel.MaximumSize = new Size(state.Width, panel.MaximumSize.Height);
                    foreach (Button button in buttons)
                    {
                        BuiltHeights built = BaseHeights.GetValue(button,
                            b => new BuiltHeights { MinimumHeight = b.MinimumSize.Height, Height = b.Height });
                        if (button.MinimumSize.Height == built.MinimumHeight) continue;
                        button.MinimumSize = new Size(button.MinimumSize.Width, built.MinimumHeight);
                        // A Button is AutoSize in the default GrowOnly mode, where its own Size is a floor as well: the
                        // height a row floor gave it would stay after the floor is gone, and the strip would ratchet
                        // the other way. Give back the height it was built with; the layout grows it again to whatever
                        // its content needs.
                        if (button.AutoSize && button.AutoSizeMode == AutoSizeMode.GrowOnly && button.Height > built.Height)
                            button.Height = built.Height;
                    }
                }
                finally { panel.ResumeLayout(true); }
                // Row membership is taken after wrapping and after the previous floors were cleared.
                foreach (var row in buttons.Where(b => b.Visible).GroupBy(b => b.Top))
                {
                    Button[] members = row.ToArray();
                    if (members.Length < 2) continue;
                    // A fixed-size button counts at its own height and is never raised: growing it through its
                    // floor would keep it grown after the floor is cleared.
                    int height = members.Max(b => b.AutoSize ? b.GetPreferredSize(Size.Empty).Height : b.Height);
                    foreach (Button button in members)
                        if (button.AutoSize && button.MinimumSize.Height < height) button.MinimumSize = new Size(button.MinimumSize.Width, height);
                }
            }
            finally { state.Busy = false; }
        }
    }
}
