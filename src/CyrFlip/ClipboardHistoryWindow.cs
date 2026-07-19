using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>Small topmost, owner-painted history strip. It hides instead of closing.</summary>
    internal sealed class ClipboardHistoryWindow : Form
    {
        private const int HeaderHeight = 40;
        private const int CellHeight = 84;
        private const int ResizeGrip = 6;
        private const int CellButtonWidth = 36;
        private const int SearchButtonLeft = 8;
        private const int HideButtonLeft = 38;
        private const int HeaderTextLeft = 70;
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private readonly ClipboardHistoryService _service;
        private readonly AppConfig _config;
        private readonly Action _openSearch;
        private bool _dragging;
        private Point _dragStart;

        public ClipboardHistoryWindow(ClipboardHistoryService service, AppConfig config, Action openSearch)
        {
            _service = service; _config = config; _openSearch = openSearch;
            Text = "Менеджер буфера"; TopMost = true; ShowInTaskbar = false;
            // WinForms otherwise paints a second, native title bar above our owner-drawn header.
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(100, HeaderHeight + 3 * CellHeight);
            Size = new Size(Math.Max(100, config.ClipboardHistoryWidth), Math.Max(MinimumSize.Height, config.ClipboardHistoryHeight));
            ApplyOpacity();
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            _service.Changed += (_, _) => { if (!IsDisposed) Invalidate(); };
            FormClosing += OnFormClosing;
            ResizeEnd += (_, _) => SaveBounds();
            Move += (_, _) => { if (Visible) SaveBounds(); };
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += (_, _) => _dragging = false;
            Resize += (_, _) => Invalidate();
        }

        public void ToggleVisible()
        {
            if (Visible) { Hide(); return; }
            RestorePlacement(); Show(); Activate(); Invalidate();
        }

        public void ApplyOpacity() => Opacity = Math.Max(30, Math.Min(100, _config.ClipboardHistoryOpacity)) / 100.0;
        public void RefreshHeader() => Invalidate(new Rectangle(0, 0, ClientSize.Width, HeaderHeight));

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e) { e.Cancel = true; Hide(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest)
            {
                base.WndProc(ref m);
                if ((int)m.Result != HtClient) return;
                int packed = m.LParam.ToInt32();
                Point point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                bool left = point.X <= ResizeGrip, right = point.X >= ClientSize.Width - ResizeGrip;
                bool top = point.Y <= ResizeGrip, bottom = point.Y >= ClientSize.Height - ResizeGrip;
                if (top && left) m.Result = (IntPtr)HtTopLeft;
                else if (top && right) m.Result = (IntPtr)HtTopRight;
                else if (bottom && left) m.Result = (IntPtr)HtBottomLeft;
                else if (bottom && right) m.Result = (IntPtr)HtBottomRight;
                else if (left) m.Result = (IntPtr)HtLeft;
                else if (right) m.Result = (IntPtr)HtRight;
                else if (top) m.Result = (IntPtr)HtTop;
                else if (bottom) m.Result = (IntPtr)HtBottom;
                return;
            }
            base.WndProc(ref m);
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            bool dark = IsDarkTheme();
            Color header = dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(235, 235, 235);
            // A layered (Opacity) owner-painted form can retain stale pixels in the newly exposed
            // area after a shrink followed by an expansion unless every pixel is repainted.
            e.Graphics.Clear(dark ? Color.FromArgb(55, 55, 55) : Color.White);
            using var headerBrush = new SolidBrush(header);
            e.Graphics.FillRectangle(headerBrush, new Rectangle(0, 0, ClientSize.Width, HeaderHeight));
            using var title = new Font(Font.FontFamily, 9, FontStyle.Bold);
            using var hotkeyFont = new Font(Font.FontFamily, 7);
            using var headerForeground = new SolidBrush(dark ? Color.LightSkyBlue : Color.MidnightBlue);
            using var hotkeyBrush = new SolidBrush(dark ? Color.Gray : Color.DimGray);
            string caption = "Менеджер буфера (" + _service.Entries.Count + ")";
            using var iconPen = new Pen(dark ? Color.Gainsboro : SystemColors.ControlText, 1.5f);
            // Keep the two actions before the caption: they remain reachable at every allowed width.
            e.Graphics.DrawEllipse(iconPen, SearchButtonLeft, 13, 10, 10);
            e.Graphics.DrawLine(iconPen, SearchButtonLeft + 9, 22, SearchButtonLeft + 13, 26);
            e.Graphics.DrawLine(iconPen, HideButtonLeft + 1, 22, HideButtonLeft + 11, 22);
            e.Graphics.DrawString(caption, title, headerForeground, HeaderTextLeft, 11);
            float captionWidth = e.Graphics.MeasureString(caption, title).Width;
            e.Graphics.DrawString(_config.ClipboardHistoryHotkey, hotkeyFont, hotkeyBrush,
                new RectangleF(HeaderTextLeft + captionWidth + 4, 10, Math.Max(1, ClientSize.Width - HeaderTextLeft - captionWidth - 4), 20),
                new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            var items = _service.Entries.Take(Math.Max(3, (ClientSize.Height - HeaderHeight) / CellHeight)).ToList();
            for (int i = 0; i < items.Count; i++) DrawCell(e.Graphics, items[i], i, new Rectangle(0, HeaderHeight + i * CellHeight, ClientSize.Width, CellHeight), dark);
        }

        private void DrawCell(Graphics g, ClipboardHistoryEntry entry, int index, Rectangle r, bool dark)
        {
            Color bg = index % 2 == 0 ? (dark ? Color.FromArgb(55, 55, 55) : Color.White) : (dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(242, 242, 242));
            using var background = new SolidBrush(bg);
            g.FillRectangle(background, r);
            if (entry.IsCurrent)
            {
                using var currentPen = new Pen(dark ? Color.LightSkyBlue : Color.RoyalBlue, 2);
                g.DrawRectangle(currentPen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            }
            string normalized = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            string anchor = normalized.Length <= 15 ? normalized : normalized.Substring(0, 15);
            string rest = normalized.Length > 15 ? normalized.Substring(15) : "";
            using var big = new Font(Font.FontFamily, 10, FontStyle.Bold);
            using var small = new Font(Font.FontFamily, 7.5f);
            using var buttons = new Font(Font.FontFamily, 15);
            using var anchorBrush = new SolidBrush(dark ? Color.White : Color.Black);
            using var restBrush = new SolidBrush(dark ? Color.Silver : Color.DimGray);
            using var timeBrush = new SolidBrush(Color.Gold);
            int textRight = r.Right - CellButtonWidth - 3;
            const float firstLineTopOffset = 4;
            using var typographic = (StringFormat)StringFormat.GenericTypographic.Clone();
            SizeF anchorSize = g.MeasureString(anchor, big, PointF.Empty, typographic);
            // Draw both parts against one baseline. A layout rectangle gives GDI+ extra leading
            // and was clipping the small continuation on some DPI/font combinations.
            g.DrawString(anchor, big, anchorBrush, new PointF(6, r.Y + firstLineTopOffset), typographic);
            float restLeft = Math.Min(textRight, 6 + anchorSize.Width);
            int firstLineCount = CountThatFits(g, rest, small, textRight - restLeft);
            string firstLineRest = firstLineCount == 0 ? "" : rest.Substring(0, firstLineCount);
            string remainingRest = firstLineCount >= rest.Length ? "" : rest.Substring(firstLineCount);
            float smallTop = r.Y + firstLineTopOffset + big.GetHeight(g) - small.GetHeight(g);
            var clip = g.Save();
            g.SetClip(new RectangleF(restLeft, r.Y, Math.Max(1, textRight - restLeft), r.Height));
            g.DrawString(firstLineRest, small, restBrush, new PointF(restLeft, smallTop), typographic);
            g.Restore(clip);
            using var bodyFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
            float bodyTop = r.Y + firstLineTopOffset + big.GetHeight(g) + 2;
            g.DrawString(remainingRest, small, restBrush, new RectangleF(6, bodyTop, textRight - 6, r.Bottom - 24 - bodyTop), bodyFormat);
            DrawPin(g, r.Right - 18, r.Y + 13, entry.IsPinned);
            g.DrawString("×", buttons, anchorBrush, r.Right - 23, r.Bottom - 29);
            g.DrawString(entry.CreatedAt.ToLocalTime().ToString("MM-dd:HH:mm"), small, timeBrush, 6, r.Bottom - 19);
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Y < HeaderHeight)
            {
                if (e.X >= HideButtonLeft && e.X < HeaderTextLeft) Hide();
                else if (e.X >= SearchButtonLeft && e.X < HideButtonLeft) _openSearch();
                else { _dragging = true; _dragStart = e.Location; }
                return;
            }
            int index = (e.Y - HeaderHeight) / CellHeight;
            var items = _service.Entries.Take(Math.Max(3, (ClientSize.Height - HeaderHeight) / CellHeight)).ToList();
            if (index < 0 || index >= items.Count) return;
            ClipboardHistoryEntry item = items[index];
            if (e.X >= ClientSize.Width - CellButtonWidth) { if (e.Y - HeaderHeight - index * CellHeight < 36) _service.TogglePin(item); else _service.Delete(item); }
            else if (e.Button == MouseButtons.Right) _service.TogglePin(item);
            else if (e.Button == MouseButtons.Left) _service.Restore(item);
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            Location = new Point(Left + e.X - _dragStart.X, Top + e.Y - _dragStart.Y);
        }

        private void RestorePlacement()
        {
            Rectangle saved = new Rectangle(_config.ClipboardHistoryX, _config.ClipboardHistoryY, Width, Height);
            if (_config.ClipboardHistoryX != int.MinValue && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved))) Location = saved.Location;
            else { Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12); }
        }
        private void SaveBounds() { _config.ClipboardHistoryX = Left; _config.ClipboardHistoryY = Top; _config.ClipboardHistoryWidth = Width; _config.ClipboardHistoryHeight = Height; _config.Save(); }

        private static int CountThatFits(Graphics g, string text, Font font, float width)
        {
            if (width <= 1 || text.Length == 0) return 0;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (g.MeasureString(text.Substring(0, middle), font, PointF.Empty, StringFormat.GenericTypographic).Width <= width) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        private static void DrawPin(Graphics g, int x, int y, bool pinned)
        {
            Color color = pinned ? Color.IndianRed : Color.White;
            using var pen = new Pen(color, 1.8f);
            using var brush = new SolidBrush(color);
            var state = g.Save();
            g.TranslateTransform(x, y);
            if (!pinned) g.RotateTransform(-25); // unpinned: visibly tilted outline
            if (pinned) g.FillEllipse(brush, -4, -7, 8, 8);
            else g.DrawEllipse(pen, -4, -7, 8, 8);
            g.DrawLine(pen, 0, 1, 0, 10);
            g.DrawLine(pen, -5, 4, 5, 4);
            g.Restore(state);
        }

        private static bool IsDarkTheme()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                return value != null && Convert.ToInt32(value) == 0;
            }
            catch { return false; }
        }
    }
}
