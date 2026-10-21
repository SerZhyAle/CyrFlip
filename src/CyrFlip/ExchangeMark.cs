using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The exchange server's mark (<c>EXCHANGE-MARK</c> 0.3, ticket S0079): the coloured star the portfolio owner
    /// ordered for the exchange server, shown beside its name where this program names it.
    ///
    /// <para><b>Declared out of the theme</b> (<c>APP-STYLE</c> rule 5, the layout marker's precedent): the star's five
    /// colours are the picture, not a style - rule 2 forbids recolouring, tinting or outlining it, so no
    /// <see cref="ThemePalette"/> role reaches it. The one change of colour the contract allows is rule 3's: in a
    /// high-contrast theme the coloured mark gives way to the one-tone glyph in the theme's text colour.</para>
    ///
    /// <para><b>Nothing is drawn here.</b> Rule 1: the mark is the artwork, not a likeness of it. The program embeds the
    /// catalog's PNG steps byte for byte (<c>assets/exchange-mark/mark-*.png</c>; rule 6) and takes, for a device size,
    /// the held step of exactly that size - the set holds the 100 to 200 per cent steps of 16, 24 and 32 logical px.
    /// A size between two steps is scaled <b>down</b> from the next larger one, never up (rule 5: a product with no
    /// SVG renderer, which a .NET Framework window is, takes the PNG and may scale it down). The flat drawing up to
    /// 32 px and the faceted one from 33 px are the PNG set's own (rule 5: <c>mark-16..32</c> flat, <c>mark-36..</c>
    /// faceted). The one-tone glyph is the vocabulary record <c>source.exchange-server</c>, whose path data is the
    /// catalog's <c>exchange-mark-mono.svg</c> unchanged (<c>tools/IconGen/glyph-overrides.txt</c>).</para>
    /// </summary>
    internal static class ExchangeMark
    {
        /// <summary>
        /// <c>DEVICE-EXCHANGE</c> item CW's term - what a sentence and a marker call the thing, in the interface language.
        /// It is the accessible name of an image-only mark (<c>ICON-RENDER</c> rule 8, <c>EXCHANGE-MARK</c> rule 14).
        /// </summary>
        internal const string TermKey = "Exchange server";

        /// <summary>The full name of the program (rule 14): a proper name, English in every interface language.</summary>
        internal const string FullName = "Media Exchange Server";

        /// <summary>The steps of <c>png/mark-*.png</c> the program embeds, smallest first.</summary>
        internal static readonly int[] HeldSizes = { 16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 56, 64, 96, 128, 256 };

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, byte[]> Bytes = new Dictionary<int, byte[]>();

        internal static string ResourceName(int size) => "CyrFlip.exchange-mark.mark-" + size + ".png";

        /// <summary>The held step a device size is read from: the size itself, else the next larger one (never a smaller one).</summary>
        internal static int HeldSizeFor(int device)
        {
            foreach (int size in HeldSizes)
                if (size >= device) return size;
            return HeldSizes[HeldSizes.Length - 1];
        }

        /// <summary>The embedded file's bytes as shipped - what the test pins by SHA-256. Null when the resource is missing.</summary>
        internal static byte[]? ResourceBytes(int size)
        {
            lock (Gate)
            {
                if (Bytes.TryGetValue(size, out byte[]? known)) return known;
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(size));
                if (stream == null) return null;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return Bytes[size] = copy.ToArray();
            }
        }

        /// <summary>
        /// The coloured mark at exactly <paramref name="device"/> pixels, or null when the artwork is not in the
        /// build (a missing resource leaves the name alone, never another picture). A step that is held comes back as
        /// it is; any other size is the next larger step scaled down.
        /// </summary>
        internal static Bitmap? Render(int device)
        {
            if (device <= 0) return null;
            int held = HeldSizeFor(device);
            byte[]? data = ResourceBytes(held);
            if (data == null) return null;

            using var stream = new MemoryStream(data);
            using var source = new Bitmap(stream);
            var result = new Bitmap(device, device, PixelFormat.Format32bppArgb);
            if (held == device && source.Width == device && source.Height == device)
            {
                CopyPixels(source, result);
                return result;
            }
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                // Pixel units and an explicit rectangle: a PNG that records a DPI other than the screen's would
                // otherwise be drawn at its physical size, not at the pixels it holds. Scaled down only.
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using var wrap = new ImageAttributes();
                wrap.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, device, device), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
            }
            return result;
        }

        /// <summary>
        /// The step's pixels as they are in the file. A held size is the contract's rasterisation (rule 6: never
        /// re-exported), and drawing it through GDI+ rounds the colour of every part-covered pixel by one level.
        /// </summary>
        private static void CopyPixels(Bitmap source, Bitmap target)
        {
            var area = new Rectangle(0, 0, target.Width, target.Height);
            BitmapData from = source.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData to = target.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[area.Width * 4];
                for (int y = 0; y < area.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(from.Scan0, y * from.Stride), row, 0, row.Length);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(to.Scan0, y * to.Stride), row.Length);
                }
            }
            finally
            {
                source.UnlockBits(from);
                target.UnlockBits(to);
            }
        }

        /// <summary>The one-tone glyph (rule 3) at <paramref name="device"/> pixels in <paramref name="ink"/>.</summary>
        internal static Bitmap? RenderOneTone(int device, Color ink) => GlyphRenderer.Render(AppGlyphs.ExchangeServer, device, ink);
    }

    /// <summary>
    /// The mark as a control: a picture of fixed logical size that is drawn at the device size of its own window (the
    /// held PNG step for that size, <see cref="ExchangeMark.Render"/>) and gives way to the one-tone glyph in the
    /// theme's text colour in high contrast (rule 3). It is never mirrored in a right-to-left window (rule 2, record
    /// kind <c>fixed</c>) - only its place moves, with the layout - and it carries no words (rule 2): its accessible
    /// name is the term (<see cref="ExchangeMark.TermKey"/>), set by the window that owns it in the interface language.
    /// </summary>
    internal sealed class ExchangeMarkPicture : Control, IThemeAware
    {
        private readonly int _logical;
        private ThemePalette _palette = ThemePalette.Light;
        private Bitmap? _drawn;
        private int _drawnSide;
        private bool _drawnOneTone;
        private Color _drawnInk;

        internal ExchangeMarkPicture(int logicalSize)
        {
            _logical = logicalSize;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            Rescale();
        }

        /// <summary>The authored size, in logical (96-DPI) pixels.</summary>
        internal int LogicalSize => _logical;

        /// <summary>
        /// The edge in device pixels at the DPI the window answers with: the per-window value the settings window and
        /// the dialogs keep truthful (<see cref="ControlDpi"/>), which is <see cref="Control.DeviceDpi"/> on the UI thread.
        /// </summary>
        internal int DeviceSize => ControlDpi.Scale(ControlDpi.Peek(this) ?? DeviceDpi, _logical);

        /// <summary>True while the one-tone glyph stands in for the coloured mark (a high-contrast theme).</summary>
        internal bool ShowsOneTone => _palette.Kind == ThemeKind.HighContrast;

        /// <summary>Takes the size of the window's current DPI. Called by the owner when its DPI changes.</summary>
        internal void Rescale()
        {
            int side = DeviceSize;
            if (Width != side || Height != side) Size = new Size(side, side);
        }

        internal void NameIn(Func<string, string> translate) => AccessibleName = translate(ExchangeMark.TermKey);

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Rescale();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Rescale();
        }

        /// <summary>
        /// The ground is the parent's colour, which the theme walk has already put on it: the star is never recoloured
        /// (rule 2) and has no ground of its own, so it stands on whatever the row it sits in stands on.
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color ground = Parent?.BackColor ?? BackColor;
            if (ground.IsEmpty || ground == Color.Transparent) ground = BackColor;
            e.Graphics.Clear(ground);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int side = Math.Min(Width, Height);
            if (side <= 0) return;
            bool oneTone = ShowsOneTone;
            Color ink = _palette.TextPrimary;
            if (_drawn == null || _drawnSide != side || _drawnOneTone != oneTone || (oneTone && _drawnInk != ink))
            {
                _drawn?.Dispose();
                _drawn = oneTone ? ExchangeMark.RenderOneTone(side, ink) : ExchangeMark.Render(side);
                _drawnSide = side; _drawnOneTone = oneTone; _drawnInk = ink;
            }
            if (_drawn != null) e.Graphics.DrawImageUnscaled(_drawn, (Width - side) / 2, (Height - side) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _drawn?.Dispose(); _drawn = null; }
            base.Dispose(disposing);
        }
    }
}
