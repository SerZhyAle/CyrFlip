using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Small topmost, owner-painted history strip. It hides instead of closing.</summary>
    internal sealed class ClipboardHistoryWindow : ThemedForm
    {
        private int HeaderHeight => Scale(44);
        // A cell holds three lines of text, so its height follows the text enlargement as well (UI-SIZING section 2 rule 3).
        private int CellHeight => ScaleText(84);
        private int ResizeGrip => Scale(6);
        private int CellButtonWidth => Scale(32);
        // The pin answers the top of the button column, delete the rest of the cell.
        private int PinZoneHeight => Scale(36);
        // 28 logical px to the close button (S0060 I-M2, the ICON-RENDER target floor); it was 26.
        private int SearchButtonLeft => Scale(4);
        private int HideButtonLeft => Scale(32);
        private int HeaderTextLeft => Scale(60);
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
        // Shared formats avoid native GDI+ allocations on every repaint.
        private static readonly StringFormat SingleLineFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private static readonly StringFormat BodyFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
        private static readonly StringFormat SourceFormat = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private readonly ClipboardHistoryService _service;
        private readonly AppConfig _config;
        private readonly Action _openSearch;
        // The failure voice (S0065 E-7): the strip owns no tray, so the context lends it its balloon.
        private readonly Action<string>? _tellRestoreFailed;
        private bool _dragging;
        private Point _dragStart;
        // The keyboard cursor through the full history, scrolling it into view; -1 until a key moves it, and again after a click.
        private int _cursor = -1;
        private int _firstVisible;
        private int _wheelDelta;
        private int _dpi = 96;
        private const int WmDpiChanged = 0x02E0;
        // The in-place editor of one entry's text (S0089): created on the first edit, hidden between edits.
        private TextBox? _editor;
        private Font? _editFont;
        private ClipboardHistoryEntry? _editing;
        // What the editor was handed: a text left as it was is no edit, although the control speaks CRLF.
        private string _editOriginal = "";

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        // The strip draws everything itself: geometry composes the monitor DPI with the interface scale, text
        // additionally with the text enlargement (S0076, UI-SIZING section 2).
        private UiMetrics _profile = UiMetrics.Pointer;
        private int Scale(int pixels) => Math.Max(1, (int)Math.Round(pixels * _dpi / 96.0 * _profile.InterfaceScale));
        private int ScaleText(int pixels) => Math.Max(1, (int)Math.Round(pixels * _dpi / 96.0 * _profile.InterfaceScale * _profile.TextScale));
        private int PageSize => Math.Max(1, (ClientSize.Height - HeaderHeight) / CellHeight);
        private int MaxFirstVisible => Math.Max(0, _service.Entries.Count - PageSize);
        // Built once instead of per paint - and the strip repaints on every copy. They follow the
        // window's own Font, so OnFontChanged drops them and the next paint rebuilds them.
        private Font? _titleFont, _hotkeyFont, _bigFont, _smallFont;
        private Font? _uiFont;

        public ClipboardHistoryWindow(ClipboardHistoryService service, AppConfig config, Action openSearch,
            Action<string>? tellRestoreFailed = null)
        {
            HelpLanguage = config.UiLanguage;
            _service = service; _config = config; _openSearch = openSearch; _tellRestoreFailed = tellRestoreFailed;
            Text = Localization.Translate(config.UiLanguage, "Clipboard"); TopMost = true; ShowInTaskbar = false;
            RightToLeft = Localization.IsRightToLeft(config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
            // WinForms otherwise paints a second, native title bar above our owner-drawn header.
            // This owner-painted surface handles WM_DPICHANGED itself: the net48 app does
            // not opt into WinForms DPI message handling, so Control.DeviceDpi stays at 96.
            AutoScaleMode = AutoScaleMode.None;
            _dpi = config.ClipboardHistoryX != int.MinValue && config.ClipboardHistoryY != int.MinValue
                ? MarkerSize.MonitorDpi(MarkerSize.MonitorAt(config.ClipboardHistoryX, config.ClipboardHistoryY))
                : MarkerSize.PrimaryDpi();
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Scale(160), HeaderHeight + 3 * CellHeight);
            Size = new Size(Math.Max(MinimumSize.Width, config.ClipboardHistoryWidth), Math.Max(MinimumSize.Height, config.ClipboardHistoryHeight));
            ApplyUiFont();
            ApplyOpacity();
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            _service.Changed += OnHistoryChanged;
            FormClosing += OnFormClosing;
            ResizeEnd += (_, _) => SaveBounds();
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            // The end of a drag is the one moment the position is worth persisting. There used to be a
            // Move handler here, and Move fires dozens of times per drag - each one running the whole
            // of AppConfig.Save(): forty-odd registry values plus a JSON serialization of both profile
            // tables, on the UI thread, to record a window that had moved four pixels.
            MouseUp += (_, _) => { if (_dragging) { _dragging = false; SaveBounds(); } };
            Resize += (_, _) => { ClampViewport(); if (_editing != null) PlaceEditor(); Invalidate(); };
            CompanionSurfaceRegistry.RegisterCompanion(this, CompanionSurfaceKind.ClipboardHistory);
            VisibleChanged += (_, _) => CompanionSurfaceRegistry.ApplyToForm(this);
            // Hiding the strip ends an edit the way a click outside does: the text is kept.
            VisibleChanged += (_, _) => { if (!Visible) EndEdit(commit: true); };
        }

        public void ToggleVisible()
        {
            if (Visible) { Hide(); return; }
            // The replay is lazy (S0064 H-5): a start with the history off touches no file, so the
            // file's records are read on the first open - on the pool (S0082), the window showing at
            // once and filling through Changed when they land.
            _service.StartReplay();
            // ForegroundActivator, not Activate(): from the text context menu this process has no
            // foreground rights and Windows refuses the activation without saying so.
            RestorePlacement(); Show(); ForegroundActivator.Activate(this); Invalidate();
        }

        public void ApplyOpacity() => Opacity = ThemePalette.ClampWindowOpacity(_config.ClipboardHistoryOpacity) / 100.0;
        public void RefreshHeader() => Invalidate(new Rectangle(0, 0, ClientSize.Width, HeaderHeight));

        /// <summary>Re-reads the UI language for the title and the owner-painted header, with no restart.</summary>
        public void ApplyLanguage()
        {
            EndEdit(commit: true);   // a direction change recreates the handles under the editor
            HelpLanguage = _config.UiLanguage;
            Text = Localization.Translate(_config.UiLanguage, "Clipboard");
            RightToLeft = Localization.IsRightToLeft(_config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
            ApplyUiFont();
            RefreshHeader();
        }

        private void ApplyUiFont()
        {
            Font? previous = _uiFont;
            _uiFont = new Font(Localization.FontFamily(_config.UiLanguage)
                ?? SystemFonts.MessageBoxFont.FontFamily.Name, 9f);
            SetBaseFont(_uiFont);   // the size profile is composed on the new family at once
            previous?.Dispose();
        }

        /// <summary>
        /// INPUT-PARITY rules 1-2: every click on the strip has a key. Up/Down/Home/End move a keyboard
        /// cursor through the full history, scrolling it into view (drawn only once a key moved it, so a mouse user never sees it),
        /// Enter restores that entry, Space pins or unpins it, Delete removes it, F2 edits its text, Ctrl+F opens the search.
        /// While an entry is edited every key is the editor's but three: Enter saves, Shift+Enter breaks the
        /// line, Escape discards (S0089).
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_editing != null)
            {
                switch (keyData)
                {
                    case Keys.Escape: EndEdit(commit: false); return true;
                    case Keys.Enter: EndEdit(commit: true); return true;
                    case Keys.Shift | Keys.Enter: _editor!.SelectedText = "\r\n"; return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (keyData == Keys.Escape) { Hide(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { _openSearch(); return true; }
            IReadOnlyList<ClipboardHistoryEntry> items = _service.Entries;
            switch (keyData)
            {
                case Keys.Down: MoveCursor(_cursor < 0 ? _firstVisible : _cursor + 1, items.Count); return true;
                case Keys.Up: MoveCursor(_cursor < 0 ? _firstVisible : _cursor - 1, items.Count); return true;
                case Keys.PageDown: MoveCursor(_cursor < 0 ? _firstVisible + PageSize : _cursor + PageSize, items.Count); return true;
                case Keys.PageUp: MoveCursor(_cursor < 0 ? _firstVisible - PageSize : _cursor - PageSize, items.Count); return true;
                case Keys.Home: MoveCursor(0, items.Count); return true;
                case Keys.End: MoveCursor(items.Count - 1, items.Count); return true;
            }
            if (_cursor >= 0 && _cursor < items.Count)
            {
                ClipboardHistoryEntry item = items[_cursor];
                switch (keyData)
                {
                    case Keys.Enter: RestoreEntry(item); return true;
                    case Keys.Space: _service.TogglePin(item); return true;
                    case Keys.Delete: _service.Delete(item); return true;
                    case Keys.F2: BeginEdit(item); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Open the editor over <paramref name="entry"/>'s cell with its whole text (S0089): the cell paints a
        /// one-line preview, the editor holds what is stored. The caret starts where the preview does.
        /// </summary>
        private void BeginEdit(ClipboardHistoryEntry entry)
        {
            EndEdit(commit: true);
            TextBox editor = EnsureEditor();
            _editing = entry;
            if (!PlaceEditor()) { _editing = null; return; }
            // An EDIT control breaks a line only on CRLF.
            editor.Text = entry.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            _editOriginal = editor.Text;
            editor.Visible = true;
            editor.Focus();
            editor.Select(0, 0);
            editor.ScrollToCaret();
            Invalidate();
        }

        /// <summary>
        /// Close the editor; with <paramref name="commit"/> its text replaces the entry's - through the service,
        /// which leaves the entry as it was for an empty text. The keyboard focus comes back to the strip only
        /// when the editor held it: a click in another window took it already.
        /// </summary>
        private void EndEdit(bool commit) => EndEdit(commit, refocus: _editor != null && _editor.Focused);

        private void EndEdit(bool commit, bool refocus)
        {
            ClipboardHistoryEntry? entry = _editing;
            TextBox? editor = _editor;
            if (entry == null || editor == null) return;
            // First: hiding the editor moves the focus, and its LostFocus comes back here.
            _editing = null;
            string text = editor.Text;
            bool edited = !string.Equals(text, _editOriginal, StringComparison.Ordinal);
            editor.Visible = false;
            editor.Text = "";
            _editOriginal = "";
            if (refocus && Visible) Focus();
            if (commit && edited) _service.UpdateText(entry, text);
            Invalidate();
        }

        /// <summary>Keep the edited row in view and the editor over its text area; false when the entry is gone.</summary>
        private bool PlaceEditor()
        {
            if (_editing == null || _editor == null) return false;
            IReadOnlyList<ClipboardHistoryEntry> entries = _service.Entries;
            int index = -1;
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i], _editing)) { index = i; break; }
            if (index < 0) return false;
            if (index < _firstVisible) _firstVisible = index;
            else if (index >= _firstVisible + PageSize) _firstVisible = index - PageSize + 1;
            ClampViewport();
            Rectangle text = TextZone(new Rectangle(0, HeaderHeight + (index - _firstVisible) * CellHeight, ClientSize.Width, CellHeight));
            // The painted text's own column (DrawCell), clear of the window's resize grip.
            _editor.Bounds = new Rectangle(text.X + Scale(6), text.Y + Scale(2),
                Math.Max(Scale(20), text.Width - Scale(12)), Math.Max(Scale(20), text.Height - Scale(4)));
            return true;
        }

        private TextBox EnsureEditor()
        {
            if (_editor == null)
            {
                _editor = new TextBox
                {
                    Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = false, AcceptsTab = false,
                    BorderStyle = BorderStyle.FixedSingle, MaxLength = ClipboardHistoryService.MaxTextChars,
                    CausesValidation = false, Visible = false,
                };
                // A click in another window, or anything else that takes the focus, saves the edit - once the
                // focus has moved, so that hiding the editor cannot hand the focus back to it.
                _editor.LostFocus += (_, _) =>
                {
                    ClipboardHistoryEntry? editing = _editing;
                    if (editing == null || !IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (ReferenceEquals(_editing, editing) && _editor != null && !_editor.Focused) EndEdit(commit: true, refocus: false);
                    }));
                };
                Controls.Add(_editor);
            }
            // Pixel units, like the painted cells: the strip, not WinForms, follows the monitor DPI and the size profile.
            float pixels = (float)(9 * _dpi / 72.0 * _profile.InterfaceScale * _profile.TextScale);
            if (_editFont == null || Math.Abs(_editFont.Size - pixels) > 0.01f || _editFont.FontFamily.Name != Font.FontFamily.Name)
            {
                _editFont = new Font(Font.FontFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
                RetireFont(_editFont);   // the editor holds it: freed with the window, never before
                _editor.Font = _editFont;
            }
            return _editor;
        }

        /// <summary>
        /// One restore through the failure voice (S0065 E-7): <see cref="ClipboardHistoryService.Restore"/>
        /// answers false while another program holds the clipboard, and a click that did nothing used
        /// to be indistinguishable from a click that worked - the user pasted the previous clipboard
        /// into their document.
        /// </summary>
        private void RestoreEntry(ClipboardHistoryEntry item)
        {
            string? notice = RestoreNotice(_service.Restore(item), ru => Localization.Translate(_config.UiLanguage, ru));
            if (notice != null) _tellRestoreFailed?.Invoke(notice);
        }

        /// <summary>The sentence a refused restore says, or null when it restored - the pure core.</summary>
        internal static string? RestoreNotice(bool restored, Func<string, string> translate)
            => restored ? null : translate("Could not copy the text to the clipboard. Try again.");

        private void MoveCursor(int index, int count)
        {
            if (count == 0) { _cursor = -1; return; }
            _cursor = Math.Max(0, Math.Min(count - 1, index));
            if (_cursor < _firstVisible) _firstVisible = _cursor;
            else if (_cursor >= _firstVisible + PageSize) _firstVisible = _cursor - PageSize + 1;
            ClampViewport();
            Invalidate();
        }

        private void ClampViewport()
        {
            _firstVisible = Math.Max(0, Math.Min(MaxFirstVisible, _firstVisible));
            _cursor = Math.Min(_cursor, _service.Entries.Count - 1);
        }

        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            ClampViewport();
            // The edited row moves when a copy lands above it, and is gone after a clear: the editor follows it or closes.
            if (_editing != null && !PlaceEditor()) EndEdit(commit: false);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_editing != null) return;   // the edited row stays under its editor
            _wheelDelta += e.Delta;
            int steps = _wheelDelta / SystemInformation.MouseWheelScrollDelta;
            _wheelDelta %= SystemInformation.MouseWheelScrollDelta;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (steps == 0 || lines == 0) return;
            _firstVisible -= steps * (lines < 0 ? PageSize : lines);
            _cursor = -1;
            ClampViewport();
            Invalidate();
        }

        /// <summary>The same viewport is used by painting, mouse input and accessibility.</summary>
        private List<ClipboardHistoryEntry> VisibleItems()
        {
            ClampViewport();
            var items = new List<ClipboardHistoryEntry>();
            IReadOnlyList<ClipboardHistoryEntry> entries = _service.Entries;
            int end = Math.Min(entries.Count, _firstVisible + PageSize);
            for (int i = _firstVisible; i < end; i++) items.Add(entries[i]);
            return items;
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e) { if (CloseToHide.Intercept(e)) Hide(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmDpiChanged)
            {
                SetDpi((int)(m.WParam.ToInt64() & 0xffff));
                var suggested = (WindowInterop.RECT)Marshal.PtrToStructure(m.LParam, typeof(WindowInterop.RECT));
                Bounds = Rectangle.FromLTRB(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom);
                m.Result = IntPtr.Zero;
                return;
            }
            // A click on the strip outside the editor saves the edit and does nothing else (S0089): it never
            // reaches MouseDown, so it neither restores, pins, deletes, drags nor hides.
            if (_editing != null && IsButtonDown(m.Msg))
            {
                EndEdit(commit: true);
                return;
            }
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
            ThemePalette palette = Palette;
            // A layered (Opacity) owner-painted form can retain stale pixels in the newly exposed
            // area after a shrink followed by an expansion unless every pixel is repainted.
            e.Graphics.Clear(palette.SurfaceRaised);
            using var headerBrush = new SolidBrush(palette.SurfaceSunken);
            e.Graphics.FillRectangle(headerBrush, new Rectangle(0, 0, ClientSize.Width, HeaderHeight));
            EnsureFonts();
            Font title = _titleFont!;
            Font hotkeyFont = _hotkeyFont!;
            using var headerForeground = new SolidBrush(palette.Accent);
            using var hotkeyBrush = new SolidBrush(palette.TextMuted);
            // Read the language on every paint: the header follows a settings change with no restart.
            // One Entries read per paint: the list is cached by the service, but the local keeps this
            // paint consistent (the caption count and the cells can't disagree).
            IReadOnlyList<ClipboardHistoryEntry> entries = _service.Entries;
            string caption = Localization.Translate(_config.UiLanguage, "Clipboard") + " (" + entries.Count + ")";
            // Keep the two actions before the caption: they remain reachable at every allowed width.
            // Vocabulary glyphs (ICON-SET), each centred in the zone the mouse hit test uses.
            HistoryStripGlyphs.DrawSearch(e.Graphics, SearchZone, palette, Scale(20));
            HistoryStripGlyphs.DrawClose(e.Graphics, HideZone, palette, Scale(20));
            float available = Math.Max(1, ClientSize.Width - HeaderTextLeft - Scale(50));
            float hotkeyWidth = e.Graphics.MeasureString(_config.ClipboardHistoryHotkey, hotkeyFont).Width;
            float captionWidth = e.Graphics.MeasureString(caption, title).Width;
            bool showHotkey = captionWidth + hotkeyWidth + Scale(12) <= available;
            e.Graphics.DrawString(caption, title, headerForeground,
                new RectangleF(HeaderTextLeft, (HeaderHeight - title.GetHeight(e.Graphics)) / 2,
                    showHotkey ? available - hotkeyWidth - Scale(8) : available, title.GetHeight(e.Graphics)), SingleLineFormat);
            if (showHotkey)
                e.Graphics.DrawString(_config.ClipboardHistoryHotkey, hotkeyFont, hotkeyBrush,
                    new RectangleF(ClientSize.Width - hotkeyWidth - Scale(50), (HeaderHeight - hotkeyFont.GetHeight(e.Graphics)) / 2,
                        hotkeyWidth, hotkeyFont.GetHeight(e.Graphics)), SingleLineFormat);
            var items = VisibleItems();
            for (int i = 0; i < items.Count; i++) DrawCell(e.Graphics, items[i], _firstVisible + i,
                new Rectangle(0, HeaderHeight + i * CellHeight, ClientSize.Width, CellHeight), palette);
            // S0067 CU-2-16: the tray opens this strip while the module is off - the empty body names
            // the switch that starts the recording instead of showing an unexplained blank.
            if (items.Count == 0 && !_config.EnableClipboardHistory)
            {
                string off = Localization.Translate(_config.UiLanguage,
                    "History is off - tick \"Clipboard history\" in the tray menu to start recording.");
                using var offBrush = new SolidBrush(palette.TextMuted);
                e.Graphics.DrawString(off, hotkeyFont, offBrush,
                    new RectangleF(Scale(8), HeaderHeight + Scale(8), ClientSize.Width - Scale(16), ClientSize.Height - HeaderHeight - Scale(16)),
                    BodyFormat);
            }
            if (_cursor >= _firstVisible && _cursor < _firstVisible + items.Count)
            {
                using var focusPen = new Pen(palette.TextPrimary, Scale(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                e.Graphics.DrawRectangle(focusPen, Scale(4), HeaderHeight + (_cursor - _firstVisible) * CellHeight + Scale(4),
                    ClientSize.Width - Scale(9), CellHeight - Scale(9));
            }
        }

        private void DrawCell(Graphics g, ClipboardHistoryEntry entry, int index, Rectangle r, ThemePalette palette)
        {
            Color bg = index % 2 == 0 ? palette.SurfaceRaised : palette.SurfaceAlternate;
            using var background = new SolidBrush(bg);
            g.FillRectangle(background, r);
            if (entry.IsCurrent)
            {
                using var currentPen = new Pen(palette.Accent, Scale(2));
                g.DrawRectangle(currentPen, r.X + Scale(1), r.Y + Scale(1), r.Width - Scale(3), r.Height - Scale(3));
            }
            // The cached preview, never the full text: this runs per visible cell on every copy and
            // every resize step, on the thread both low-level hooks share (S0005 CH-4).
            string normalized = entry.Preview;
            Font big = _bigFont!;
            Font small = _smallFont!;
            using var anchorBrush = new SolidBrush(palette.TextPrimary);
            using var restBrush = new SolidBrush(palette.TextMuted);
            using var timeBrush = new SolidBrush(palette.Info);
            int textLeft = Scale(6);
            int textWidth = Math.Max(1, r.Width - CellButtonWidth - Scale(12));
            float firstTop = r.Y + Scale(4);
            int firstCount = CountThatFits(g, normalized, big, textWidth);
            g.DrawString(normalized.Substring(0, firstCount), big, anchorBrush,
                new RectangleF(textLeft, firstTop, textWidth, big.GetHeight(g)), SingleLineFormat);
            float bodyTop = firstTop + big.GetHeight(g) + Scale(2);
            float footerTop = r.Bottom - Scale(4) - small.GetHeight(g);
            g.DrawString(normalized.Substring(firstCount), small, restBrush,
                new RectangleF(textLeft, bodyTop, textWidth, Math.Max(1, footerTop - Scale(4) - bodyTop)), BodyFormat);
            HistoryStripGlyphs.DrawPin(g, PinZone(r), entry.IsPinned, palette, Scale(20));
            HistoryStripGlyphs.DrawDelete(g, DeleteZone(r), palette, Scale(20));
            string time = entry.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm");
            float timeWidth = g.MeasureString(time, small).Width;
            g.DrawString(time, small, timeBrush,
                new RectangleF(textLeft, footerTop, Math.Min(textWidth, timeWidth), small.GetHeight(g)), SingleLineFormat);
            float sourceLeft = textLeft + timeWidth + Scale(8);
            float sourceWidth = textLeft + textWidth - sourceLeft;
            if (entry.SourceApp.Length > 0 && sourceWidth > Scale(20))
            {
                using var sourceBrush = new SolidBrush(palette.Success);
                g.DrawString(entry.SourceApp, small, sourceBrush,
                    new RectangleF(sourceLeft, footerTop, sourceWidth, small.GetHeight(g)), SourceFormat);
            }
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (_cursor >= 0) { _cursor = -1; Invalidate(); }
            if (e.Y < HeaderHeight)
            {
                if (e.X >= HideButtonLeft && e.X < HeaderTextLeft) Hide();
                else if (e.X >= SearchButtonLeft && e.X < HideButtonLeft) _openSearch();
                else { _dragging = true; _dragStart = e.Location; }
                return;
            }
            int index = (e.Y - HeaderHeight) / CellHeight;
            var items = VisibleItems();
            if (index < 0 || index >= items.Count) return;
            ClipboardHistoryEntry item = items[index];
            if (e.X >= ClientSize.Width - CellButtonWidth) { if (e.Y - HeaderHeight - index * CellHeight < PinZoneHeight) _service.TogglePin(item); else _service.Delete(item); }
            else if (e.Button == MouseButtons.Right) BeginEdit(item);
            else if (e.Button == MouseButtons.Left) RestoreEntry(item);
        }

        /// <summary>The client-area button presses: WM_LBUTTONDOWN, WM_LBUTTONDBLCLK through WM_XBUTTONDBLCLK, ups excluded.</summary>
        private static bool IsButtonDown(int msg)
            => msg == 0x0201 || msg == 0x0203 || msg == 0x0204 || msg == 0x0206 || msg == 0x0207 || msg == 0x0209
               || msg == 0x020B || msg == 0x020D;

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            Location = new Point(Left + e.X - _dragStart.X, Top + e.Y - _dragStart.Y);
        }

        private void RestorePlacement()
        {
            Rectangle saved = new Rectangle(_config.ClipboardHistoryX, _config.ClipboardHistoryY, Width, Height);
            if (_config.ClipboardHistoryX != int.MinValue && _config.ClipboardHistoryY != int.MinValue)
            {
                Rectangle clamped = ScreenPlacement.Clamp(saved, Screen.AllScreens.Select(s => s.WorkingArea));
                Location = clamped.Location;
                Size = clamped.Size;
            }
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
                if (g.MeasureString(text.Substring(0, middle), font).Width <= width) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        // ---- accessibility (ICON-RENDER rule 8) ----------------------------------------------------------
        // The strip is one owner-drawn surface with no child controls, so a screen reader would find a
        // single unnamed window. It exposes what a click can do as children instead: search and close in
        // the header, then pin, delete and edit for every visible cell - each named by the record's name in
        // the UI language, the three that act on a row describing it by its preview; the edit's description
        // names its key (S0089). An open editor is the last child, with its own accessibility.

        private sealed class StripPart
        {
            public string Name = "";
            public string Description = "";
            public string Shortcut = "";
            public AccessibleRole Role = AccessibleRole.PushButton;
            public bool Checked;
            public Rectangle Client;
            public Action Invoke = () => { };
        }

        private List<StripPart> AccessibleParts()
        {
            string language = _config.UiLanguage;
            var parts = new List<StripPart>
            {
                new StripPart { Name = Localization.Translate(language, "Search history"), Client = SearchZone, Invoke = _openSearch },
                new StripPart { Name = Localization.Translate(language, "Close"), Client = HideZone, Invoke = Hide },
            };
            List<ClipboardHistoryEntry> items = VisibleItems();
            for (int i = 0; i < items.Count; i++)
            {
                ClipboardHistoryEntry item = items[i];
                var cell = new Rectangle(0, HeaderHeight + i * CellHeight, ClientSize.Width, CellHeight);
                string preview = item.Preview.Length <= 80 ? item.Preview : item.Preview.Substring(0, 80);
                parts.Add(new StripPart
                {
                    Name = Localization.Translate(language, "Pin"), Description = preview, Role = AccessibleRole.CheckButton,
                    Checked = item.IsPinned, Client = PinZone(cell), Invoke = () => _service.TogglePin(item),
                });
                parts.Add(new StripPart
                {
                    Name = Localization.Translate(language, "Delete"), Description = preview,
                    Client = DeleteZone(cell), Invoke = () => _service.Delete(item),
                });
                parts.Add(new StripPart
                {
                    Name = Localization.Translate(language, "Edit"), Description = preview + " (F2)", Shortcut = "F2",
                    Client = TextZone(cell), Invoke = () => BeginEdit(item),
                });
            }
            return parts;
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new StripAccessibleObject(this);

        private sealed class StripAccessibleObject : ControlAccessibleObject
        {
            private readonly ClipboardHistoryWindow _window;
            public StripAccessibleObject(ClipboardHistoryWindow window) : base(window) { _window = window; }
            public override int GetChildCount() => _window.AccessibleParts().Count + (_window._editing != null ? 1 : 0);
            public override AccessibleObject? GetChild(int index)
            {
                List<StripPart> parts = _window.AccessibleParts();
                if (index >= 0 && index < parts.Count) return new StripPartObject(this, _window, parts[index]);
                return index == parts.Count && _window._editing != null ? _window._editor?.AccessibilityObject : null;
            }
        }

        private sealed class StripPartObject : AccessibleObject
        {
            private readonly StripAccessibleObject _parent;
            private readonly ClipboardHistoryWindow _window;
            private readonly StripPart _part;
            public StripPartObject(StripAccessibleObject parent, ClipboardHistoryWindow window, StripPart part) { _parent = parent; _window = window; _part = part; }
            public override AccessibleObject Parent => _parent;
            public override string Name { get => _part.Name; set { } }
            public override string Description => _part.Description;
            public override string KeyboardShortcut => _part.Shortcut;
            public override AccessibleRole Role => _part.Role;
            public override AccessibleStates State => AccessibleStates.Focusable | (_part.Checked ? AccessibleStates.Checked : AccessibleStates.None);
            public override Rectangle Bounds => _window.RectangleToScreen(_part.Client);
            public override string DefaultAction => _part.Name;
            public override void DoDefaultAction() => _part.Invoke();
        }

        /// <summary>The click zones, in client coordinates: what is painted and what answers a click is one rectangle.</summary>
        private Rectangle SearchZone => new Rectangle(SearchButtonLeft, 0, HideButtonLeft - SearchButtonLeft, HeaderHeight);
        private Rectangle HideZone => new Rectangle(HideButtonLeft, 0, HeaderTextLeft - HideButtonLeft, HeaderHeight);
        private Rectangle PinZone(Rectangle cell) => new Rectangle(cell.Right - CellButtonWidth, cell.Y, CellButtonWidth, PinZoneHeight);
        private Rectangle DeleteZone(Rectangle cell) => new Rectangle(cell.Right - CellButtonWidth, cell.Y + PinZoneHeight, CellButtonWidth, cell.Height - PinZoneHeight);
        /// <summary>The text body beside the button column: a right click there edits the entry.</summary>
        private Rectangle TextZone(Rectangle cell) => new Rectangle(cell.X, cell.Y, cell.Width - CellButtonWidth, cell.Height);

        /// <summary>The four fonts the strip paints with, derived from the window's own <see cref="Control.Font"/>.</summary>
        private void EnsureFonts()
        {
            if (_titleFont != null) return;
            // Pixel units keep text and owner-drawn geometry on the same monitor DPI.
            float factor = (float)(_dpi / 72.0 * _profile.InterfaceScale * _profile.TextScale);
            _titleFont = new Font(Font.FontFamily, 8 * factor, FontStyle.Bold, GraphicsUnit.Pixel);
            _hotkeyFont = new Font(Font.FontFamily, 7.5f * factor, FontStyle.Regular, GraphicsUnit.Pixel);
            _bigFont = new Font(Font.FontFamily, 9 * factor, FontStyle.Bold, GraphicsUnit.Pixel);
            _smallFont = new Font(Font.FontFamily, 8 * factor, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            _titleFont?.Dispose();
            _hotkeyFont?.Dispose();
            _bigFont?.Dispose();
            _smallFont?.Dispose();
            _titleFont = _hotkeyFont = _bigFont = _smallFont = null;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetDpi((int)GetDpiForWindow(Handle)); }
            catch (EntryPointNotFoundException) { }
        }

        private void SetDpi(int dpi)
        {
            _dpi = dpi > 0 ? dpi : 96;
            ApplyGeometry();
        }

        /// <summary>
        /// The size profile changed: the strip's geometry and its four painting fonts are derived again, the
        /// window's own size is only ever grown to the new minimum.
        /// </summary>
        protected override void OnUiProfileApplied(UiMetrics metrics)
        {
            base.OnUiProfileApplied(metrics);
            _profile = metrics;
            ApplyGeometry();
        }

        private void ApplyGeometry()
        {
            EndEdit(commit: true);   // a DPI or size-profile change saves the edit; nothing reopens
            DisposeFonts();
            MinimumSize = new Size(Scale(160), HeaderHeight + 3 * CellHeight);
            ClampViewport();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            EndEdit(commit: true);
            DisposeFonts();   // rebuilt on the next paint, from the new family
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CompanionSurfaceRegistry.UnregisterCompanion(this); _service.Changed -= OnHistoryChanged; DisposeFonts(); _uiFont?.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>
        /// The app's palette, as last put on this window. The strip used to read Windows' preference
        /// itself, with its own two-second cache - which is how one window of the app could be dark
        /// beside another that was still light. It now draws with whatever <see cref="ThemeManager"/>
        /// resolved, like every other window (ticket S0020).
        /// </summary>
        private ThemePalette Palette => ThemeApply.PaletteOf(this) ?? ThemePalette.Light;

        /// <summary>The whole strip is owner-drawn: a theme change is a repaint.</summary>
        protected override void OnThemeApplied(ThemePalette palette) => Invalidate();
    }
}
