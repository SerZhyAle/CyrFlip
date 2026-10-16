using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The draw-on-capture editor (ticket S0071, section 6): the frozen capture held on screen while
    /// the user draws primitive shapes on it, before anything reaches the clipboard or the disk.
    /// Area mode shows the chosen region at full brightness and dims everything outside it; full mode
    /// shows everything at full brightness. OK composes the shapes onto a copy of the frame
    /// (<see cref="AnnotationComposer"/>) and hands it to the caller - Cancel hands over nothing and
    /// disposes the frame. Every exit path disposes the frame exactly once.
    ///
    /// <para><b>The per-monitor windows are a private copy of <see cref="RegionSelectionOverlay"/>'s
    /// <c>MonitorWindow</c> core</b> (one topmost borderless window per monitor painting its slice of
    /// the one frozen frame in virtual-screen physical pixels, mouse capture by the window the drag
    /// started on), not an extraction: the differences (shapes on top, the region outline, drag =
    /// draw instead of select) touch most of the paint and the whole mouse half, and the overlay's
    /// behaviour must stay identical for S0026. The keyboard/focus/display-change rules are shared
    /// discipline, not shared code.</para>
    ///
    /// <para>Like the selection overlay the editor takes the foreground - it must receive Esc and
    /// Enter, and there is nothing of the user's to protect. State lives on the UI thread (the hook's
    /// thread) exactly as the overlay's paint does; the composition on OK is a few dozen Graphics
    /// calls before the hand-off, and the encode + clipboard + file stay on the worker.</para>
    /// </summary>
    internal sealed class AnnotationEditor : IDisposable
    {
        private readonly Bitmap _frame;             // owned here; disposed on every exit path
        private readonly Rectangle _virtual;
        private readonly Rectangle? _region;        // null = full mode
        private readonly IntPtr _returnTo;
        private readonly List<MonitorWindow> _windows = new List<MonitorWindow>();
        private readonly List<AnnotationShape> _shapes = new List<AnnotationShape>();
        private readonly List<AnnotationOp> _ops = new List<AnnotationOp>();
        private readonly ScreenshotToolbar _toolbar;
        private readonly Func<string, string> _translate;
        private int _nextIndex;
        private AnnotationTool _tool = AnnotationTool.Rectangle;
        private Color _ink = AnnotationPalette.Default;
        private readonly int _strokeWidth;
        private bool _dragging;
        private bool _erasing;
        private Point _anchor;
        private Rectangle _previewBounds;
        private readonly List<AnnotationShape> _erasedThisDrag = new List<AnnotationShape>();
        private bool _finished;

        /// <summary>Raised once, on OK, with the composed image; the caller owns it from here.</summary>
        public event Action<Bitmap>? Delivered;
        /// <summary>Raised once, on every other exit (Esc, the toolbar's Cancel, focus lost, display changed).</summary>
        public event Action? Cancelled;

        /// <summary>
        /// The one outline width of the whole editor (Q6): 3 physical px scaled by the DPI of the
        /// monitor under the pointer at editor open - the frame is one bitmap, so one scale for all.
        /// </summary>
        public static int StrokeWidthFor(Point pointerPhysical)
            => Math.Max(1, (int)Math.Round(3.0 * MarkerSize.MonitorDpi(MarkerSize.MonitorAt(pointerPhysical.X, pointerPhysical.Y)) / 96.0));

        public AnnotationEditor(Bitmap frame, Rectangle virtualScreen, Rectangle? region,
            IEnumerable<Rectangle> monitors, IntPtr returnTo, Func<string, string> translate)
        {
            _frame = frame;
            _virtual = virtualScreen;
            _region = region;
            _returnTo = returnTo;
            _translate = translate;
            Point pointer = ScreenCapture.CursorPosition();
            _strokeWidth = StrokeWidthFor(pointer);
            foreach (Rectangle monitor in monitors)
                _windows.Add(new MonitorWindow(this, monitor));
            _toolbar = new ScreenshotToolbar(_tool, _ink, translate);
            _toolbar.ToolChanged += tool => { _tool = tool; };
            _toolbar.ColorChanged += ink => { _ink = ink; RepaintAll(); };
            _toolbar.CancelRequested += Cancel;
            _toolbar.ClearRequested += ClearAll;
            _toolbar.OkRequested += Ok;
            _toolbar.HelpAction += () => { if (_windows.Count > 0) ContextHelp.Open(_windows.FirstOrDefault(w => w.Monitor.Contains(ScreenCapture.CursorPosition())) ?? _windows[0]); };
        }

        /// <summary>Shapes committed so far, in commit order - the audit-facing view the tests read.</summary>
        public IReadOnlyList<AnnotationShape> Shapes => _shapes;

        /// <summary>The window the editor returns the foreground to on every exit.</summary>
        public IntPtr ReturnTo => _returnTo;

        /// <summary>Show every window, take the foreground under the pointer, and put the toolbar beside it.</summary>
        public void Show()
        {
            foreach (MonitorWindow window in _windows) window.Show();
            Point pointer = ScreenCapture.CursorPosition();
            MonitorWindow active = _windows.Find(w => w.Monitor.Contains(pointer)) ?? _windows[0];
            ForegroundActivator.Activate(active);
            active.Focus();
            // The toolbar is shown near the current pointer and clamped into the pointer's monitor;
            // its Show would steal the foreground, and the editor has to keep it (Esc/Enter), so the
            // editor window under the pointer takes it straight back.
            _toolbar.Show();
            _toolbar.PlaceNear(pointer);
            ForegroundActivator.Activate(active);
            active.Focus();
        }

        private void RepaintAll()
        {
            foreach (MonitorWindow window in _windows)
                window.InvalidateVirtual(_virtual);
        }

        // ---- Drawing ---------------------------------------------------------------------------

        private void BeginDrag(Point pointer)
        {
            if (_tool == AnnotationTool.Eraser)
            {
                _erasing = true;
                _erasedThisDrag.Clear();
                EraseAt(pointer);
                return;
            }
            _dragging = true;
            _anchor = pointer;
            _previewBounds = PreviewBounds(pointer);
            foreach (MonitorWindow window in _windows)
                window.InvalidateVirtual(_previewBounds);
        }

        private void TrackDrag(Point pointer)
        {
            if (_erasing)
            {
                EraseAt(pointer);
                return;
            }
            if (!_dragging) return;
            // Repaint the preview's old and new area, never the whole screen: a mouse move must not
            // invalidate a multi-monitor frame.
            Rectangle next = PreviewBounds(pointer);
            Rectangle repaint = Rectangle.Union(_previewBounds, next);
            _previewBounds = next;
            foreach (MonitorWindow window in _windows)
                window.InvalidateVirtual(repaint);
        }

        private Rectangle PreviewBounds(Point pointer)
        {
            Rectangle bounds = ScreenCapture.Normalize(_anchor, pointer);
            bounds.Inflate(_strokeWidth + 2, _strokeWidth + 2);
            return bounds;
        }

        private void EndDrag(Point pointer)
        {
            if (_erasing)
            {
                _erasing = false;
                if (_erasedThisDrag.Count > 0)
                {
                    // One drag is one undoable action, whatever it removed.
                    _ops.Add(new AnnotationOp.EraseOps(_erasedThisDrag.ToList()));
                    _erasedThisDrag.Clear();
                }
                return;
            }
            if (!_dragging) return;
            _dragging = false;
            if (AnnotationShape.IsDegenerate(_anchor, pointer)) return;   // a click, not a shape
            var shape = new AnnotationShape(KindOf(_tool), _anchor, pointer, _ink, _nextIndex++);
            _shapes.Add(shape);
            _ops.Add(new AnnotationOp.DrawOp(shape));
            foreach (MonitorWindow window in _windows)
                window.InvalidateVirtual(PreviewBounds(pointer));
        }

        private static AnnotationKind KindOf(AnnotationTool tool) => tool switch
        {
            AnnotationTool.Ellipse => AnnotationKind.Ellipse,
            AnnotationTool.FilledEllipse => AnnotationKind.FilledEllipse,
            AnnotationTool.Rectangle => AnnotationKind.Rectangle,
            AnnotationTool.FilledRectangle => AnnotationKind.FilledRectangle,
            AnnotationTool.Line => AnnotationKind.Line,
            AnnotationTool.Arrow => AnnotationKind.Arrow,
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "not a drawing tool"),
        };

        /// <summary>
        /// The eraser removes objects, not pixels: every committed shape whose bounds (inflated by
        /// max(strokeWidth, 8) px, so thin lines and arrows are reachable) contain the pointer,
        /// topmost first as encountered. It cannot touch the frozen frame.
        /// </summary>
        private void EraseAt(Point pointer)
        {
            int inflate = Math.Max(_strokeWidth, 8);
            // The bounds grow on every removal; the loop protects against the pointer hitting a
            // shape twice in one call.
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                AnnotationShape shape = _shapes[i];
                if (_erasedThisDrag.Contains(shape)) continue;
                if (!shape.HitTest(pointer, inflate)) continue;
                _shapes.RemoveAt(i);
                _erasedThisDrag.Add(shape);
                Rectangle bounds = shape.Bounds;
                bounds.Inflate(inflate + 2, inflate + 2);
                foreach (MonitorWindow window in _windows)
                    window.InvalidateVirtual(bounds);
            }
        }

        /// <summary>"Очистить": the shape list empties in one step; the editor stays open. No confirmation - nothing is lost that a redraw (or Ctrl+Z) cannot bring back.</summary>
        private void ClearAll()
        {
            if (_shapes.Count == 0) return;
            _ops.Add(new AnnotationOp.EraseOps(_shapes.ToList()));
            _shapes.Clear();
            RepaintAll();
        }

        /// <summary>Ctrl+Z (Q4, in): undo the last action - a commit or an eraser removal.</summary>
        private void Undo()
        {
            if (_ops.Count == 0) return;
            AnnotationOp op = _ops[_ops.Count - 1];
            _ops.RemoveAt(_ops.Count - 1);
            switch (op)
            {
                case AnnotationOp.DrawOp draw:
                    _shapes.Remove(draw.Shape);
                    break;
                case AnnotationOp.EraseOps erase:
                    // Back in their commit order, wherever the later commits sit now.
                    _shapes.AddRange(erase.Removed);
                    _shapes.Sort((a, b) => a.Index.CompareTo(b.Index));
                    break;
            }
            RepaintAll();
        }

        // ---- Finishing --------------------------------------------------------------------------

        /// <summary>OK (button or Enter): compose, hand over, close. The frame and the editor dispose on the caller's path.</summary>
        private void Ok()
        {
            if (_finished) return;
            Bitmap composed = AnnotationComposer.Compose(_frame, _virtual, _region, _shapes, _strokeWidth);
            // The frame is dead the moment the copy exists - the composed image is the delivery.
            _frame.Dispose();
            _frameDisposed = true;
            Finish();
            Delivered?.Invoke(composed);
        }

        /// <summary>Cancel (button, Esc, focus lost, the display changed): the capture is gone entirely.</summary>
        public void Cancel()
        {
            if (_finished) return;
            _frame.Dispose();
            _frameDisposed = true;
            Finish();
            Cancelled?.Invoke();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            foreach (MonitorWindow window in _windows) window.Hide();
            _toolbar.Hide();
            // The foreground returns to the window the user came from; like the overlay's dispose,
            // the windows' own disposal is the caller's next UI-thread step.
        }

        public void Dispose()
        {
            _finished = true;
            foreach (MonitorWindow window in _windows) window.Dispose();
            _windows.Clear();
            if (!_frameDisposed) _frame.Dispose();
            _toolbar.Dispose();
        }

        private bool _frameDisposed;

        // ---- Per-monitor window (the private copy of the overlay's MonitorWindow core) ----------

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

            // Area mode: the chosen region at full brightness, everything outside dimmed (the S0026
            // dimming fill), a 1px outline on the region edge - the user sees exactly what OK will
            // deliver. Full mode: everything at full brightness, no veil.
            if (_region.HasValue)
            {
                Rectangle slice = ScreenCapture.MonitorSlice(_region.Value, monitor);
                using (var veil = new SolidBrush(ThemePalette.CaptureVeil))
                using (var outside = new Region(clip))
                {
                    if (!slice.IsEmpty) outside.Exclude(slice);
                    g.FillRegion(veil, outside);
                }
                Rectangle local = new Rectangle(_region.Value.X - monitor.X, _region.Value.Y - monitor.Y,
                    _region.Value.Width, _region.Value.Height);
                using (var outline = new Pen(ThemePalette.CaptureFrame, 1f))
                    g.DrawRectangle(outline, local.X, local.Y, local.Width - 1, local.Height - 1);
            }

            // The shapes, in commit order; the in-progress drag as a preview. Shape coordinates are
            // virtual-screen pixels: translate so they land on this window's client area (its top-left
            // is screen point monitor.Location).
            g.TranslateTransform(-monitor.X, -monitor.Y);
            foreach (AnnotationShape shape in _shapes)
                shape.Draw(g, _strokeWidth);
            if (_dragging)
                new AnnotationShape(KindOf(_tool), _anchor, ScreenCapture.CursorPosition(), _ink, _nextIndex)
                    .Draw(g, _strokeWidth);
            g.ResetTransform();
        }

        /// <summary>
        /// One monitor's borderless, topmost window, covering exactly its bounds in physical pixels -
        /// the overlay's MonitorWindow with the editor's mouse and paint on top.
        /// </summary>
        private sealed class MonitorWindow : Form
        {
            private const int WM_DISPLAYCHANGE = 0x007E;
            private const int WM_DPICHANGED = 0x02E0;
            private readonly AnnotationEditor _owner;

            public MonitorWindow(AnnotationEditor owner, Rectangle monitor)
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
                ContextHelpUi.Attach(this);
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
                if (e.Button == MouseButtons.Left)
                {
                    // WinForms captures the mouse for the window the press landed on, so the drag goes
                    // on when the pointer crosses onto another monitor.
                    Capture = true;
                    _owner.BeginDrag(ScreenCapture.CursorPosition());
                }
            }

            protected override void OnMouseMove(MouseEventArgs e) => _owner.TrackDrag(ScreenCapture.CursorPosition());

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _owner.EndDrag(ScreenCapture.CursorPosition());
            }

            /// <summary>Every key the editor knows, before WinForms spends an arrow on focus navigation.</summary>
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (CaptureHelpLayer.HandleKey(this, keyData)) return true;
                if (keyData == Keys.F1) { ContextHelp.Open(this); return true; }
                switch (keyData)
                {
                    case Keys.Escape:
                        _owner.Cancel();
                        return true;
                    case Keys.Enter:
                        _owner.Ok();
                        return true;
                    case Keys.Z when (keyData & Keys.Modifiers) == Keys.Control:
                        _owner.Undo();
                        return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            /// <summary>
            /// The foreground left the editor for something that is not one of its own windows
            /// (Alt+Tab, the Win key, a UAC prompt) - checked a message later, since activating one
            /// editor window deactivates another. The unsaved drawings are lost; the settings page's
            /// hint says so.
            /// </summary>
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
                        _owner.Cancel();
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

        private void CheckFocus()
        {
            if (_finished) return;
            IntPtr foreground = WindowInterop.GetForegroundWindow();
            foreach (MonitorWindow window in _windows)
                if (window.IsHandleCreated && window.Handle == foreground) return;
            // The toolbar counts as ours: clicking it must not cancel the capture.
            if (_toolbar.IsHandleCreated && _toolbar.Handle == foreground) return;
            if (_toolbar.ContainsFocus) return;
            Cancel();
        }
    }
}
