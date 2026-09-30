using System;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Keeps the keyboard focus in a settings row table across a rebuild (ticket S0045 K6,
    /// <c>INPUT-PARITY</c> rule 2). Every row action - ↑/↓, "По умолчанию", "Удалить" - rebuilds the rows
    /// and disposes the button that held the focus, which dropped a keyboard user out of the table after
    /// every step. The table is a panel of row panels; the focus is remembered as (row, control) and put
    /// back on the same control of the row the item now sits in, the nearest focusable one when that one
    /// is gone or disabled, and on whatever follows the table when the table is empty.
    /// </summary>
    internal readonly struct RowFocus
    {
        private readonly int _row;
        private readonly int _column;

        private RowFocus(int row, int column)
        {
            _row = row;
            _column = column;
        }

        /// <summary>True when the focus was inside the table when it was captured.</summary>
        public bool Held => _row >= 0;

        public static RowFocus Capture(Control rows)
        {
            for (int r = 0; r < rows.Controls.Count; r++)
            {
                Control row = rows.Controls[r];
                if (!row.ContainsFocus) continue;
                for (int c = 0; c < row.Controls.Count; c++)
                    if (row.Controls[c].ContainsFocus) return new RowFocus(r, c);
                return new RowFocus(r, 0);
            }
            return new RowFocus(-1, -1);
        }

        /// <param name="rowDelta">How far the item moved: the focus follows a ↑/↓ move.</param>
        public void Restore(Control rows, int rowDelta = 0)
        {
            if (!Held) return;
            Control? target = Find(rows, _row + rowDelta, _column);
            if (target != null)
            {
                target.Focus();
                return;
            }
            rows.Parent?.SelectNextControl(rows, true, true, true, true);
        }

        /// <summary>
        /// The control to focus: the same column of the wanted row, then the nearest focusable one in that
        /// row (left first - a row's trailing buttons are the destructive ones), then the rows around it.
        /// </summary>
        internal static Control? Find(Control rows, int wantedRow, int column)
        {
            int count = rows.Controls.Count;
            if (count == 0) return null;
            int start = Math.Max(0, Math.Min(count - 1, wantedRow));
            for (int step = 0; step < count; step++)
            {
                foreach (int r in new[] { start - step, start + step })
                {
                    if (r < 0 || r >= count || (step == 0 && r != start)) continue;
                    Control? hit = InRow(rows.Controls[r], column);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        private static Control? InRow(Control row, int column)
        {
            int count = row.Controls.Count;
            if (count == 0) return Focusable(row) ? row : null;
            int start = Math.Max(0, Math.Min(count - 1, column));
            for (int c = start; c >= 0; c--)
                if (Focusable(row.Controls[c])) return row.Controls[c];
            for (int c = start + 1; c < count; c++)
                if (Focusable(row.Controls[c])) return row.Controls[c];
            return null;
        }

        private static bool Focusable(Control control)
            => control.CanSelect && control.TabStop && control.Enabled && control.Visible;
    }

    /// <summary>
    /// A row's caption-less "enabled" switch (S0045 K9). A check box draws its focus rectangle round its
    /// caption, so one without a caption showed the keyboard focus nowhere; this one draws it round the
    /// box itself - only after keyboard navigation, like every other focus cue (<c>INPUT-PARITY</c> rule 2).
    /// </summary>
    internal sealed class RowCheckBox : CheckBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor);
        }
    }
}
