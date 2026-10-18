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
            // Other editor strips keep their caption/column reflow policy.
            if (buttons.Length == 0 || buttons.Length != panel.Controls.Count) return;
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
    }
}
