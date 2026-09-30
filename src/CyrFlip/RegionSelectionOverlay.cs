using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The frozen full-screen selection of the region capture (ticket S0026, section 4) - the only part
    /// of the feature that knows WinForms.
    ///
    /// <para><b>One window per monitor</b> (4.3): a single window spanning monitors of different
    /// scaling is scaled by Windows per the monitor it is mostly on, which breaks the pixel mapping on
    /// the others. Each window covers exactly its monitor in physical pixels and paints its own slice of
    /// the one frozen frame; the selection is one rectangle in virtual-screen pixels shared by all of
    /// them, so a drag across monitors is one picture. The pointer is always read in screen
    /// coordinates (<see cref="ScreenCapture.CursorPosition"/>), never from control-relative event args.</para>
    ///
    /// <para>Unlike every other CyrFlip popup the overlay takes the foreground: it must receive Esc, and
    /// there is no selection of the user's to protect.</para>
    /// </summary>
    internal sealed class RegionSelectionOverlay : IDisposable
    {
        private readonly Bitmap _frame;
        private readonly Rectangle _virtual;
        private readonly List<MonitorWindow> _windows = new List<MonitorWindow>();
        private Point _anchor;
        private bool _dragging;
        private Rectangle _selection = Rectangle.Empty;
        private bool _finished;

        /// <summary>
        /// Raised once: the selected rectangle in virtual-screen pixels, or null for a cancel (Esc, a
        /// right click, a click without a drag, the focus lost, the display changed).
        /// </summary>
        public event Action<Rectangle?>? Finished;

        /// <param name="frame">The frozen virtual screen; owned by the caller, painted from, never copied.</param>
        public RegionSelectionOverlay(Bitmap frame, Rectangle virtualScreen, IEnumerable<Rectangle> monitors)
        {
            _frame = frame;
            _virtual = virtualScreen;
            foreach (Rectangle monitor in monitors)
                _windows.Add(new MonitorWindow(this, monitor));
        }

        /// <summary>Show every window and give the foreground to the one under the pointer.</summary>
        public void Show()
        {
            foreach (MonitorWindow window in _windows) window.Show();
            Point pointer = ScreenCapture.CursorPosition();
            MonitorWindow active = _windows.Find(w => w.Monitor.Contains(pointer)) ?? _windows[0];
            ForegroundActivator.Activate(active);
            active.Focus();
        }

        private void Begin()
        {
            _anchor = ScreenCapture.CursorPosition();
            _dragging = true;
            SetSelection(new Rectangle(_anchor, Size.Empty));
        }

        private void Track()
        {
            if (!_dragging) return;
            Rectangle next = ScreenCapture.ClampToVirtualScreen(
                ScreenCapture.Normalize(_anchor, ScreenCapture.CursorPosition()), _virtual);
            SetSelection(next);
        }

        private void End()
        {
            if (!_dragging) return;
            Track();
            _dragging = false;
            Finish(ScreenCapture.IsClick(_selection) ? (Rectangle?)null : _selection);
        }

        /// <summary>The step of one arrow press, in physical pixels; with Ctrl, one pixel.</summary>
        internal const int KeyStep = 10;

        /// <summary>
        /// The keyboard route through the capture (ticket S0045 K4, <c>INPUT-PARITY</c> rule 1): Esc
        /// cancels; Enter takes the selection, or the whole monitor the overlay is focused on when there
        /// is none; Ctrl+A selects every monitor; an arrow moves the selection - starting one in the middle
        /// of the monitor when there is none - and Shift+arrow resizes it from its right and bottom edges,
        /// ten pixels a press, one with Ctrl. A drag with the mouse replaces a keyboard selection as usual.
        /// </summary>
        internal bool OnKey(Keys keyData, Rectangle monitor)
        {
            if (_finished) return false;
            Keys key = keyData & Keys.KeyCode;
            switch (key)
            {
                case Keys.Escape:
                    Finish(null);
                    return true;
                case Keys.Enter:
                    _dragging = false;
                    Finish(_selection.Width > 0 && _selection.Height > 0
                        ? _selection
                        : ScreenCapture.ClampToVirtualScreen(monitor, _virtual));
                    return true;
                case Keys.A when (keyData & Keys.Modifiers) == Keys.Control:
                    _dragging = false;
                    SetSelection(_virtual);
                    return true;
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    _dragging = false;
                    SetSelection(Nudge(_selection, keyData, monitor, _virtual));
                    return true;
            }
            return false;
        }

        /// <summary>
        /// One arrow press applied to the selection: moved, or resized with Shift, by
        /// <see cref="KeyStep"/> (one pixel with Ctrl), kept inside the virtual screen and never smaller
        /// than one pixel. An empty selection first becomes a third of the monitor, centred on it.
        /// </summary>
        internal static Rectangle Nudge(Rectangle selection, Keys keyData, Rectangle monitor, Rectangle virtualScreen)
        {
            if (selection.Width <= 0 || selection.Height <= 0)
                return ScreenCapture.ClampToVirtualScreen(new Rectangle(
                    monitor.X + monitor.Width / 3, monitor.Y + monitor.Height / 3,
                    Math.Max(1, monitor.Width / 3), Math.Max(1, monitor.Height / 3)), virtualScreen);

            int step = (keyData & Keys.Control) == Keys.Control ? 1 : KeyStep;
            int dx = 0, dy = 0;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                default: return selection;
            }

            if ((keyData & Keys.Shift) == Keys.Shift)
            {
                int width = Math.Max(1, Math.Min(selection.Width + dx, virtualScreen.Right - selection.X));
                int height = Math.Max(1, Math.Min(selection.Height + dy, virtualScreen.Bottom - selection.Y));
                return new Rectangle(selection.X, selection.Y, width, height);
            }

            int x = Math.Max(virtualScreen.Left, Math.Min(selection.X + dx, virtualScreen.Right - selection.Width));
            int y = Math.Max(virtualScreen.Top, Math.Min(selection.Y + dy, virtualScreen.Bottom - selection.Height));
            return new Rectangle(x, y, selection.Width, selection.Height);
        }

        private void SetSelection(Rectangle next)
        {
            if (next == _selection) return;
            Rectangle before = Decorated(_selection), after = Decorated(next);
            _selection = next;
            foreach (MonitorWindow window in _windows)
            {
                window.InvalidateVirtual(before);
                window.InvalidateVirtual(after);
            }
        }

        /// <summary>The selection plus the frame around it and the size label - what a change repaints.</summary>
        private Rectangle Decorated(Rectangle selection)
        {
            if (selection.IsEmpty && selection.Location == Point.Empty) return Rectangle.Empty;
            Rectangle area = selection;
            area.Inflate(3, 3);
            Rectangle label = LabelRect(selection);
            return label.IsEmpty ? area : Rectangle.Union(area, label);
        }

        private Size _labelSize;
        private Font? _labelFont;

        private string LabelText(Rectangle selection)
            => selection.Width.ToString(CultureInfo.InvariantCulture) + " × " + selection.Height.ToString(CultureInfo.InvariantCulture);

        /// <summary>The size label's place: below the rectangle where it fits, else above, else inside.</summary>
        private Rectangle LabelRect(Rectangle selection)
        {
            if (selection.Width <= 0 && selection.Height <= 0) return Rectangle.Empty;
            _labelFont ??= new Font(SystemFonts.MessageBoxFont.FontFamily, 9f);
            if (_labelSize.IsEmpty)
            {
                Size text = TextRenderer.MeasureText("99999 × 99999", _labelFont);
                _labelSize = new Size(text.Width + 8, text.Height + 4);
            }
            int x = Math.Max(_virtual.Left, Math.Min(selection.Left, _virtual.Right - _labelSize.Width));
            int y = selection.Bottom + 4;
            if (y + _labelSize.Height > _virtual.Bottom) y = selection.Top - 4 - _labelSize.Height;
            if (y < _virtual.Top) y = selection.Top + 4;
            return new Rectangle(new Point(x, y), _labelSize);
        }

        private void Finish(Rectangle? result)
        {
            if (_finished) return;
            _finished = true;
            foreach (MonitorWindow window in _windows) window.Hide();
            Finished?.Invoke(result);
        }

        /// <summary>Cancel from outside (the module switched off, the app exiting).</summary>
        public void Cancel() => Finish(null);

        public void Dispose()
        {
            _finished = true;
            foreach (MonitorWindow window in _windows) window.Dispose();
            _windows.Clear();
            _labelFont?.Dispose();
        }

        /// <summary>
        /// The foreground left the overlay for something that is not one of its own windows (Alt+Tab,
        /// the Win key, a UAC prompt) - checked a message later, since activating one overlay window
        /// deactivates another.
        /// </summary>
        private void CheckFocus()
        {
            if (_finished) return;
            IntPtr foreground = WindowInterop.GetForegroundWindow();
            foreach (MonitorWindow window in _windows)
                if (window.IsHandleCreated && window.Handle == foreground) return;
            Finish(null);
        }

        private void Paint(MonitorWindow window, Graphics g, Rectangle clip)
        {
            Rectangle monitor = window.Monitor;
            // The frozen frame, 1:1 - only the part that needs painting.
            var source = new Rectangle(clip.X + monitor.X - _virtual.X, clip.Y + monitor.Y - _virtual.Y, clip.Width, clip.Height);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_frame, clip, source, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // The veil everywhere but the selection: the dimming is painted, never baked into a second
            // copy of the frame (section 7).
            Rectangle slice = ScreenCapture.MonitorSlice(_selection, monitor);
            using (var veil = new SolidBrush(ThemePalette.CaptureVeil))
            using (var region = new Region(clip))
            {
                if (!slice.IsEmpty) region.Exclude(slice);
                g.FillRegion(veil, region);
            }
            if (_selection.Width <= 0 || _selection.Height <= 0) return;

            var local = new Rectangle(_selection.X - monitor.X, _selection.Y - monitor.Y, _selection.Width, _selection.Height);
            using (var shadow = new Pen(ThemePalette.CaptureFrameShadow))
                g.DrawRectangle(shadow, local.X - 2, local.Y - 2, local.Width + 3, local.Height + 3);
            using (var frame = new Pen(ThemePalette.CaptureFrame))
                g.DrawRectangle(frame, local.X - 1, local.Y - 1, local.Width + 1, local.Height + 1);

            Rectangle label = LabelRect(_selection);
            label.Offset(-monitor.X, -monitor.Y);
            if (!label.IntersectsWith(clip)) return;
            using (var back = new SolidBrush(ThemePalette.CaptureLabelBack))
                g.FillRectangle(back, label);
            TextRenderer.DrawText(g, LabelText(_selection), _labelFont!, label, ThemePalette.CaptureLabelText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>One monitor's borderless, topmost window, covering exactly its bounds in physical pixels.</summary>
        private sealed class MonitorWindow : Form
        {
            private const int WM_DISPLAYCHANGE = 0x007E;
            private const int WM_DPICHANGED = 0x02E0;
            private readonly RegionSelectionOverlay _owner;

            public MonitorWindow(RegionSelectionOverlay owner, Rectangle monitor)
            {
                _owner = owner;
                Monitor = monitor;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Bounds = monitor;
                KeyPreview = true;
                Cursor = Cursors.Cross;
                Text = "CyrFlip";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            }

            public Rectangle Monitor { get; }

            public void InvalidateVirtual(Rectangle area)
            {
                if (area.IsEmpty || !IsHandleCreated) return;
                Rectangle local = Rectangle.Intersect(area, Monitor);
                if (local.IsEmpty) return;
                local.Offset(-Monitor.X, -Monitor.Y);
                Invalidate(local);
            }

            protected override void OnPaint(PaintEventArgs e) => _owner.Paint(this, e.Graphics, e.ClipRectangle);

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { _owner.Finish(null); return; }
                if (e.Button == MouseButtons.Left)
                {
                    // WinForms captures the mouse for the window the press landed on, so the drag goes
                    // on when the pointer crosses onto another monitor.
                    Capture = true;
                    _owner.Begin();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e) => _owner.Track();

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _owner.End();
            }

            /// <summary>
            /// Every key the overlay knows, before WinForms can spend an arrow on focus navigation
            /// (<see cref="RegionSelectionOverlay.OnKey"/>).
            /// </summary>
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
                => _owner.OnKey(keyData, Monitor) || base.ProcessCmdKey(ref msg, keyData);

            protected override void OnDeactivate(EventArgs e)
            {
                base.OnDeactivate(e);
                if (IsHandleCreated) BeginInvoke(new Action(_owner.CheckFocus));
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_DISPLAYCHANGE:
                        // The frozen frame no longer matches the screen.
                        _owner.Finish(null);
                        break;
                    case WM_DPICHANGED:
                        // The window already covers its monitor in physical pixels; the suggested
                        // rectangle would rescale it. Keep the bounds and do not let WinForms rescale.
                        Bounds = Monitor;
                        m.Result = IntPtr.Zero;
                        return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
