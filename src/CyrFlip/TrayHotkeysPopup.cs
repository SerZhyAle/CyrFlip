using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Lightweight, non-activating, click-through hover popup displaying all currently active hotkeys
    /// and shortcuts (ticket S0059).
    /// </summary>
    internal sealed class TrayHotkeysPopup : ThemedForm
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOPMOST = 0x00000008;

        private readonly TableLayoutPanel _table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(12, 10, 12, 10),
        };

        private string _uiLanguage;
        private ThemePalette _palette = ThemePalette.Light;
        private TrayChordList? _model;

        public TrayHotkeysPopup(string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = ThemePalette.Light.SurfaceWindow;

            _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(_table);

            ApplyLanguage(uiLanguage);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ApplyLanguage(string language)
        {
            _uiLanguage = language;
            RightToLeft = Localization.IsRightToLeft(language) ? RightToLeft.Yes : RightToLeft.No;
            _table.RightToLeft = RightToLeft;
        }

        public void SetModel(TrayChordList model, Point? anchorPoint = null)
        {
            _model = model;
            RebuildControls(model);

            if (anchorPoint.HasValue)
                PositionBeside(anchorPoint.Value);
        }

        private void RebuildControls(TrayChordList model)
        {
            _table.SuspendLayout();

            // Clear() does not dispose: every render used to leak its labels until a collection.
            Control[] stale = new Control[_table.Controls.Count];
            _table.Controls.CopyTo(stale, 0);
            foreach (Control control in stale)
                control.Dispose();
            _table.Controls.Clear();
            _table.RowStyles.Clear();

            Screen currentScreen = Screen.FromPoint(Location.IsEmpty ? Cursor.Position : Location);
            Rectangle workArea = currentScreen.WorkingArea;
            int maxCapHeight = (int)(workArea.Height * 0.60);
            int maxCapWidth = (int)(workArea.Width * 0.40);

            int rowIndex = 0;
            int totalActiveRows = 0;
            int displayedActiveRows = 0;
            bool capped = false;

            // Count total active items across all groups
            foreach (TrayChordGroup group in model.Groups)
                totalActiveRows += group.Rows.Count;

            // Approximate line height ~22px
            int estimatedHeight = 24 + (model.Groups.Count * 22) + (totalActiveRows * 22);
            int allowedRows = totalActiveRows;
            if (estimatedHeight > maxCapHeight)
            {
                // Leave room for group headers and the overflow row
                int rowBudget = Math.Max(4, (maxCapHeight - 24 - (model.Groups.Count * 22) - 24) / 22);
                if (rowBudget < totalActiveRows)
                {
                    allowedRows = rowBudget;
                    capped = true;
                }
            }

            // Collect what the table will hold before touching controls: measuring is done once
            // per string below (S0064 H-4), so the row set must be known first.
            var rows = new List<RowSpec>();
            foreach (TrayChordGroup group in model.Groups)
            {
                if (displayedActiveRows >= allowedRows && capped)
                    break;

                rows.Add(RowSpec.Header(group.Title, rowIndex == 0));
                rowIndex++;

                foreach (TrayChordRow row in group.Rows)
                {
                    if (displayedActiveRows >= allowedRows && capped)
                        break;

                    if (string.IsNullOrEmpty(row.ChordDisplay))
                        rows.Add(RowSpec.Full(row.ActionDisplay, row.IsMuted));
                    else
                        rows.Add(RowSpec.Pair(row.ChordDisplay, row.ActionDisplay, row.IsClash, row.IsMuted));

                    displayedActiveRows++;
                    rowIndex++;
                }
            }

            if (capped && totalActiveRows > displayedActiveRows)
                rows.Add(RowSpec.Cap(Localization.Format(_uiLanguage, "ещё {0} — в настройках", totalActiveRows - displayedActiveRows)));

            // One measure per string with the one font every label uses; the chord and action
            // columns take the widest of their cells, and a spanning row wider than the pair
            // widens the action column. Sizes are then fixed, so the layout engine only sums
            // them - the labels never auto-measure, and GetPreferredSize below never touches text.
            Font font = Font;
            int colChord = 0, colAction = 0;
            foreach (RowSpec row in rows)
            {
                row.TextSize = TextRenderer.MeasureText(row.Action, font);
                if (row.IsHeader || row.IsCap || row.Chord == null)
                {
                    int spanWidth = row.TextSize.Width + 2;
                    if (spanWidth > colChord + colAction) colAction = spanWidth - colChord;
                }
                else
                {
                    row.ChordSize = TextRenderer.MeasureText(row.Chord, font);
                    colChord = Math.Max(colChord, row.ChordSize.Width + 2 + 10); // the chord label's right margin
                    colAction = Math.Max(colAction, row.TextSize.Width + 2);
                }
            }

            int tableRow = 0;
            foreach (RowSpec row in rows)
            {
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                int topMargin = row.IsHeader ? (row.First ? 0 : 6) : (row.IsCap ? 6 : 1);
                int bottomMargin = row.IsHeader ? 2 : (row.IsCap ? 0 : 1);

                if (row.IsHeader || row.IsCap)
                {
                    var label = new Label
                    {
                        Text = row.Action,
                        AutoSize = false,
                        Size = row.TextSize,
                        ForeColor = _palette.TextMuted,
                        Font = font,
                        Margin = new Padding(0, topMargin, 0, bottomMargin),
                    };
                    _table.Controls.Add(label, 0, tableRow);
                    _table.SetColumnSpan(label, 2);
                }
                else if (row.Chord == null)
                {
                    var fullRow = new Label
                    {
                        Text = row.Action,
                        AutoSize = false,
                        Size = row.TextSize,
                        ForeColor = row.Muted ? _palette.TextMuted : _palette.TextPrimary,
                        Font = font,
                        Margin = new Padding(0, topMargin, 0, bottomMargin),
                    };
                    _table.Controls.Add(fullRow, 0, tableRow);
                    _table.SetColumnSpan(fullRow, 2);
                }
                else
                {
                    Color textColor = row.Clash ? _palette.Warning : (row.Muted ? _palette.TextMuted : _palette.TextPrimary);
                    var chordLabel = new Label
                    {
                        Text = row.Chord,
                        AutoSize = false,
                        Size = row.ChordSize,
                        TextAlign = ContentAlignment.MiddleRight,
                        ForeColor = textColor,
                        Font = font,
                        Margin = new Padding(0, topMargin, 10, bottomMargin),
                    };
                    var actionLabel = new Label
                    {
                        Text = (row.Clash ? "⚠ " : "") + row.Action,
                        AutoSize = false,
                        Size = row.TextSize,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = textColor,
                        Font = font,
                        Margin = new Padding(0, topMargin, 0, bottomMargin),
                    };

                    _table.Controls.Add(chordLabel, 0, tableRow);
                    _table.Controls.Add(actionLabel, 1, tableRow);
                }
                tableRow++;
            }

            _table.ResumeLayout(true);
            Size preferred = _table.GetPreferredSize(new Size(maxCapWidth, maxCapHeight));
            Size = new Size(
                Math.Min(maxCapWidth, Math.Max(200, preferred.Width + 2)),
                Math.Min(maxCapHeight, Math.Max(60, preferred.Height + 2)));
        }

        /// <summary>One visual row of the popup, measured once before any control is built (S0064 H-4).</summary>
        private sealed class RowSpec
        {
            public string? Chord;     // null: the row spans both columns
            public string Action = "";
            public bool Clash, Muted, IsHeader, IsCap, First;
            public Size TextSize, ChordSize;

            public static RowSpec Header(string title, bool first) => new RowSpec { Action = title, IsHeader = true, First = first };
            public static RowSpec Full(string action, bool muted) => new RowSpec { Action = action, Muted = muted };
            public static RowSpec Pair(string chord, string action, bool clash, bool muted) => new RowSpec { Chord = chord, Action = action, Clash = clash, Muted = muted };
            public static RowSpec Cap(string text) => new RowSpec { Action = text, IsCap = true };
        }

        public void PositionBeside(Point anchor)
        {
            Screen screen = Screen.FromPoint(anchor);
            Rectangle work = screen.WorkingArea;
            Rectangle full = screen.Bounds;

            int popupWidth = Width;
            int popupHeight = Height;

            int targetX = anchor.X - (popupWidth / 2);
            int targetY = anchor.Y - popupHeight - 8;

            // Place based on taskbar dock edge
            if (work.Bottom < full.Bottom) // Bottom taskbar
            {
                targetY = anchor.Y - popupHeight - 8;
                targetX = anchor.X - (popupWidth / 2);
            }
            else if (work.Top > full.Top) // Top taskbar
            {
                targetY = anchor.Y + 24;
                targetX = anchor.X - (popupWidth / 2);
            }
            else if (work.Right < full.Right) // Right taskbar
            {
                targetX = anchor.X - popupWidth - 8;
                targetY = anchor.Y - (popupHeight / 2);
            }
            else if (work.Left > full.Left) // Left taskbar
            {
                targetX = anchor.X + 24;
                targetY = anchor.Y - (popupHeight / 2);
            }

            Rectangle clamped = ScreenPlacement.Clamp(new Rectangle(targetX, targetY, popupWidth, popupHeight), new[] { work });
            Bounds = clamped;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var borderPen = new Pen(_palette.Border);
            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnThemeApplied(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceWindow;
            if (_model != null)
                RebuildControls(_model);
            Invalidate();
        }
    }
}
