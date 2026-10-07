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

            foreach (TrayChordGroup group in model.Groups)
            {
                if (displayedActiveRows >= allowedRows && capped)
                    break;

                // Group header
                var headerLabel = new Label
                {
                    Text = group.Title,
                    AutoSize = true,
                    ForeColor = _palette.TextMuted,
                    Font = Font,
                    Margin = new Padding(0, rowIndex == 0 ? 0 : 6, 0, 2),
                };
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _table.Controls.Add(headerLabel, 0, rowIndex);
                _table.SetColumnSpan(headerLabel, 2);
                rowIndex++;

                foreach (TrayChordRow row in group.Rows)
                {
                    if (displayedActiveRows >= allowedRows && capped)
                        break;

                    _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                    if (string.IsNullOrEmpty(row.ChordDisplay))
                    {
                        var fullRow = new Label
                        {
                            Text = row.ActionDisplay,
                            AutoSize = true,
                            ForeColor = row.IsMuted ? _palette.TextMuted : _palette.TextPrimary,
                            Font = Font,
                            Margin = new Padding(0, 1, 0, 1),
                        };
                        _table.Controls.Add(fullRow, 0, rowIndex);
                        _table.SetColumnSpan(fullRow, 2);
                    }
                    else
                    {
                        Color textColor = row.IsClash ? _palette.Warning : (row.IsMuted ? _palette.TextMuted : _palette.TextPrimary);
                        var chordLabel = new Label
                        {
                            Text = row.ChordDisplay,
                            AutoSize = true,
                            TextAlign = ContentAlignment.MiddleRight,
                            ForeColor = textColor,
                            Font = Font,
                            Margin = new Padding(0, 1, 10, 1),
                        };
                        var actionLabel = new Label
                        {
                            Text = (row.IsClash ? "⚠ " : "") + row.ActionDisplay,
                            AutoSize = true,
                            TextAlign = ContentAlignment.MiddleLeft,
                            ForeColor = textColor,
                            Font = Font,
                            Margin = new Padding(0, 1, 0, 1),
                        };

                        _table.Controls.Add(chordLabel, 0, rowIndex);
                        _table.Controls.Add(actionLabel, 1, rowIndex);
                    }

                    displayedActiveRows++;
                    rowIndex++;
                }
            }

            if (capped && totalActiveRows > displayedActiveRows)
            {
                int remaining = totalActiveRows - displayedActiveRows;
                var capLabel = new Label
                {
                    Text = Localization.Format(_uiLanguage, "ещё {0} — в настройках", remaining),
                    AutoSize = true,
                    ForeColor = _palette.TextMuted,
                    Font = Font,
                    Margin = new Padding(0, 6, 0, 0),
                };
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _table.Controls.Add(capLabel, 0, rowIndex);
                _table.SetColumnSpan(capLabel, 2);
            }

            _table.ResumeLayout(true);
            Size preferred = _table.GetPreferredSize(new Size(maxCapWidth, maxCapHeight));
            Size = new Size(
                Math.Min(maxCapWidth, Math.Max(200, preferred.Width + 2)),
                Math.Min(maxCapHeight, Math.Max(60, preferred.Height + 2)));
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
